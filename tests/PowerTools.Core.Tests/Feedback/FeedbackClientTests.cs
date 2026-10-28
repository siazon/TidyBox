using System.Net;
using System.Text;
using System.Text.Json;
using PowerTools.Core.Feedback;

namespace PowerTools.Core.Tests.Feedback;

public class FeedbackClientTests
{
    private static readonly Uri Endpoint = new("https://feedback.example.test/v1/feedback");

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> _respond;

        public FakeHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> respond) => _respond = respond;

        public HttpRequestMessage? Request { get; private set; }
        public string? Body { get; private set; }
        public string? ContentType { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Request = request;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            ContentType = request.Content?.Headers.ContentType?.MediaType;
            return _respond(request, ct);
        }
    }

    private static HttpResponseMessage Reply(int status, string body = "{}")
        => new((HttpStatusCode)status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static (FeedbackClient Client, FakeHandler Handler) Make(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage>? respond = null)
    {
        var handler = new FakeHandler(respond ?? ((_, _) => Reply(201, """{"id":1}""")));
        return (new FeedbackClient(new HttpClient(handler), Endpoint), handler);
    }

    private static FeedbackSubmission Submission(FeedbackKind kind = FeedbackKind.Suggestion, string message = "hello", string? contact = null, string? logs = null)
        => new(kind, message, contact, "1.2.3.4", logs);

    private static JsonElement Sent(FakeHandler handler) => JsonDocument.Parse(handler.Body!).RootElement;

    [Fact]
    public async Task The_request_is_a_json_post_to_the_endpoint_with_the_fields_the_server_expects()
    {
        var (client, handler) = Make();

        await client.SendAsync(Submission(FeedbackKind.Error, "it broke", "a@b.c", "log line"), CancellationToken.None);

        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal(Endpoint, handler.Request.RequestUri);
        Assert.Equal("application/json", handler.ContentType);
        var body = Sent(handler);
        Assert.Equal("error", body.GetProperty("kind").GetString());
        Assert.Equal("it broke", body.GetProperty("message").GetString());
        Assert.Equal("a@b.c", body.GetProperty("contact").GetString());
        Assert.Equal("1.2.3.4", body.GetProperty("appVersion").GetString());
        Assert.Equal("log line", body.GetProperty("logs").GetString());
    }

    [Theory]
    [InlineData(FeedbackKind.Suggestion, "suggestion")]
    [InlineData(FeedbackKind.Error, "error")]
    [InlineData(FeedbackKind.Other, "other")]
    public async Task The_kind_is_sent_in_lower_case(FeedbackKind kind, string expected)
    {
        var (client, handler) = Make();

        await client.SendAsync(Submission(kind), CancellationToken.None);

        Assert.Equal(expected, Sent(handler).GetProperty("kind").GetString());
    }

    [Theory]
    [InlineData(FeedbackKind.Suggestion)]
    [InlineData(FeedbackKind.Other)]
    public async Task Logs_are_never_sent_unless_the_kind_is_error(FeedbackKind kind)
    {
        var (client, handler) = Make();

        await client.SendAsync(Submission(kind, logs: "secret log"), CancellationToken.None);

        Assert.False(Sent(handler).TryGetProperty("logs", out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_blank_contact_is_left_out(string? contact)
    {
        var (client, handler) = Make();

        await client.SendAsync(Submission(contact: contact), CancellationToken.None);

        Assert.False(Sent(handler).TryGetProperty("contact", out _));
    }

    [Fact]
    public async Task The_message_and_contact_are_trimmed()
    {
        var (client, handler) = Make();

        await client.SendAsync(Submission(message: "  hi  ", contact: "  qq123 "), CancellationToken.None);

        Assert.Equal("hi", Sent(handler).GetProperty("message").GetString());
        Assert.Equal("qq123", Sent(handler).GetProperty("contact").GetString());
    }

    [Fact]
    public async Task Oversized_logs_are_trimmed_to_the_limit_before_sending()
    {
        var (client, handler) = Make();
        var logs = string.Join("\n", Enumerable.Range(0, 3000).Select(i => $"line {i} " + new string('x', 40)));

        await client.SendAsync(Submission(FeedbackKind.Error, logs: logs), CancellationToken.None);

        Assert.True(Encoding.UTF8.GetByteCount(Sent(handler).GetProperty("logs").GetString()!) <= FeedbackLimits.MaxLogBytes);
    }

    [Fact]
    public async Task A_worst_case_chinese_report_still_fits_the_servers_body_limit()
    {
        // JSON written with the default encoder spells every Chinese character as \uXXXX (6 bytes instead of 3), which
        // would push a message and a log that are each within their own limit past the server's 128 KB.
        var (client, handler) = Make();
        var logs = new string('中', 21845); // 65535 bytes of UTF-8

        await client.SendAsync(Submission(FeedbackKind.Error, new string('中', FeedbackLimits.MaxMessageChars), logs: logs), CancellationToken.None);

        Assert.True(Encoding.UTF8.GetByteCount(handler.Body!) <= 131072, $"body was {Encoding.UTF8.GetByteCount(handler.Body!)} bytes");
    }

    [Fact]
    public async Task Redaction_tokens_and_ordinary_punctuation_are_not_escaped()
    {
        var (client, handler) = Make();

        await client.SendAsync(Submission(FeedbackKind.Error, logs: "user=<user> host=<machine> a+b & c's"), CancellationToken.None);

        Assert.Contains("user=<user> host=<machine> a+b & c's", handler.Body);
    }

    [Fact]
    public async Task A_201_means_sent()
    {
        var (client, _) = Make();

        var result = await client.SendAsync(Submission(), CancellationToken.None);

        Assert.Equal(FeedbackOutcome.Sent, result.Outcome);
    }

    [Theory]
    [InlineData(400, FeedbackOutcome.Rejected)]
    [InlineData(413, FeedbackOutcome.Rejected)]
    [InlineData(429, FeedbackOutcome.RateLimited)]
    [InlineData(503, FeedbackOutcome.RateLimited)]
    [InlineData(500, FeedbackOutcome.Unreachable)]
    [InlineData(502, FeedbackOutcome.Unreachable)]
    [InlineData(404, FeedbackOutcome.Unreachable)]
    public async Task Status_codes_map_to_outcomes(int status, FeedbackOutcome expected)
    {
        var (client, _) = Make((_, _) => Reply(status));

        var result = await client.SendAsync(Submission(), CancellationToken.None);

        Assert.Equal(expected, result.Outcome);
    }

    [Fact]
    public async Task A_rejection_names_the_offending_field_when_the_server_does()
    {
        var (client, _) = Make((_, _) => Reply(400, """{"error":"invalid_request","field":"message"}"""));

        var result = await client.SendAsync(Submission(), CancellationToken.None);

        Assert.Equal("message", result.Detail);
    }

    [Fact]
    public async Task A_rejection_with_an_unreadable_body_still_comes_back_rejected()
    {
        var (client, _) = Make((_, _) => Reply(400, "<html>not json</html>"));

        var result = await client.SendAsync(Submission(), CancellationToken.None);

        Assert.Equal(FeedbackOutcome.Rejected, result.Outcome);
        Assert.Null(result.Detail);
    }

    [Fact]
    public async Task A_network_failure_means_unreachable()
    {
        var (client, _) = Make((_, _) => throw new HttpRequestException("no route to host"));

        var result = await client.SendAsync(Submission(), CancellationToken.None);

        Assert.Equal(FeedbackOutcome.Unreachable, result.Outcome);
    }

    [Fact]
    public async Task A_timeout_means_unreachable()
    {
        // HttpClient reports its own timeout as a cancellation the caller did not ask for.
        var (client, _) = Make((_, _) => throw new TaskCanceledException("timed out"));

        var result = await client.SendAsync(Submission(), CancellationToken.None);

        Assert.Equal(FeedbackOutcome.Unreachable, result.Outcome);
    }

    [Fact]
    public async Task The_callers_own_cancellation_is_not_a_failure()
    {
        using var cts = new CancellationTokenSource();
        var (client, _) = Make((_, ct) =>
        {
            cts.Cancel();
            ct.ThrowIfCancellationRequested();
            return Reply(201);
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.SendAsync(Submission(), cts.Token));
    }
}
