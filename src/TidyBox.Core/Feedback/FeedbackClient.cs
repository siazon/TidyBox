using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TidyBox.Core.Feedback;

/// <summary>Sends one feedback to the feedback server and says what came of it; it never throws for a network problem.</summary>
public sealed class FeedbackClient
{
    // The default encoder writes every non-ASCII character as \uXXXX (6 bytes for a 3-byte Chinese character) and also
    // escapes <, >, + and ' — which would push a legitimate report past the server's body limit. This body is JSON for a
    // server, never HTML, so the relaxed escaping is safe; quotes and control characters are still escaped.
    private static readonly JsonSerializerOptions Json = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly HttpClient _http;
    private readonly Uri _endpoint;

    public FeedbackClient(HttpClient http, Uri endpoint)
    {
        _http = http;
        _endpoint = endpoint;
    }

    /// <summary>A client that gives up after 10 seconds.</summary>
    public static FeedbackClient Create(Uri endpoint) => new(new HttpClient { Timeout = TimeSpan.FromSeconds(10) }, endpoint);

    // Property names are the server's field names.
    private sealed record Payload(string kind, string message, string? contact, string appVersion, string? logs);

    /// <exception cref="OperationCanceledException"><paramref name="ct"/> was cancelled by the caller.</exception>
    public async Task<FeedbackResult> SendAsync(FeedbackSubmission submission, CancellationToken ct)
    {
        var payload = new Payload(
            submission.Kind.ToString().ToLowerInvariant(),
            submission.Message.Trim(),
            string.IsNullOrWhiteSpace(submission.Contact) ? null : submission.Contact.Trim(),
            submission.AppVersion,
            submission.Kind == FeedbackKind.Error && !string.IsNullOrEmpty(submission.Logs) ? FeedbackLimits.TrimLogs(submission.Logs) : null);
        using var content = new StringContent(JsonSerializer.Serialize(payload, Json), Encoding.UTF8, "application/json");

        try
        {
            using var response = await _http.PostAsync(_endpoint, content, ct);
            return (int)response.StatusCode switch
            {
                201 => new FeedbackResult(FeedbackOutcome.Sent, null),
                400 or 413 => new FeedbackResult(FeedbackOutcome.Rejected, await ReadFieldAsync(response, ct)),
                429 or 503 => new FeedbackResult(FeedbackOutcome.RateLimited, null),
                var other => new FeedbackResult(FeedbackOutcome.Unreachable, $"HTTP {other}"),
            };
        }
        catch (HttpRequestException ex)
        {
            return new FeedbackResult(FeedbackOutcome.Unreachable, ex.Message);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // HttpClient reports its own timeout as a cancellation nobody asked for.
            return new FeedbackResult(FeedbackOutcome.Unreachable, "timed out");
        }
    }

    private static async Task<string?> ReadFieldAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            return document.RootElement.TryGetProperty("field", out var field) ? field.GetString() : null;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return null; // the status already says it was refused; the field is a bonus
        }
    }
}
