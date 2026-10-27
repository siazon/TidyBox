using System.IO.Pipes;
using PowerTools.Core.Launch;

namespace PowerTools.Core.Tests;

public sealed class AnalyzeRequestChannelTests : IDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    private readonly string _pipe = "PowerToolsTests." + Guid.NewGuid().ToString("N");
    private readonly List<AnalyzeRequestListener> _listeners = new();

    public void Dispose()
    {
        foreach (var listener in _listeners) listener.Dispose();
    }

    private (AnalyzeRequestListener Listener, BlockingQueue Received) Listen(Action<string>? extra = null)
    {
        var received = new BlockingQueue();
        var listener = new AnalyzeRequestListener(_pipe, path =>
        {
            received.Add(path);
            extra?.Invoke(path);
        });
        listener.Start();
        _listeners.Add(listener);
        return (listener, received);
    }

    private sealed class BlockingQueue
    {
        private readonly System.Collections.Concurrent.BlockingCollection<string> _items = new();

        public void Add(string item) => _items.Add(item);

        public string? Next() => _items.TryTake(out var item, (int)Patience.TotalMilliseconds) ? item : null;

        public bool IsEmptyAfter(TimeSpan wait) => !_items.TryTake(out _, (int)wait.TotalMilliseconds);
    }

    [Fact]
    public void Nobody_listening_means_the_request_is_not_delivered()
    {
        Assert.False(AnalyzeRequestSender.TrySend(_pipe, @"C:\Data", TimeSpan.FromMilliseconds(100)));
    }

    [Fact]
    public void A_listener_receives_the_path_that_was_sent()
    {
        var (_, received) = Listen();

        Assert.True(AnalyzeRequestSender.TrySend(_pipe, @"C:\Users\me\Documents", Patience));

        Assert.Equal(@"C:\Users\me\Documents", received.Next());
    }

    [Fact]
    public void An_activate_message_is_delivered_like_any_other_text()
    {
        var (_, received) = Listen();

        Assert.True(AnalyzeRequestSender.TrySend(_pipe, LaunchRequest.ActivateMessage, Patience));

        Assert.Equal("--activate", received.Next());
    }

    [Fact]
    public void Several_requests_in_a_row_all_arrive_in_order()
    {
        var (_, received) = Listen();

        foreach (var path in new[] { @"C:\a", @"D:\b", @"E:\c" }) Assert.True(AnalyzeRequestSender.TrySend(_pipe, path, Patience));

        Assert.Equal(new[] { @"C:\a", @"D:\b", @"E:\c" }, new[] { received.Next(), received.Next(), received.Next() });
    }

    [Fact]
    public void A_path_with_non_ascii_characters_survives()
    {
        var (_, received) = Listen();

        Assert.True(AnalyzeRequestSender.TrySend(_pipe, @"D:\资料\照片 2026", Patience));

        Assert.Equal(@"D:\资料\照片 2026", received.Next());
    }

    [Fact]
    public void A_message_far_too_large_is_dropped_and_the_next_request_still_arrives()
    {
        var (_, received) = Listen();

        AnalyzeRequestSender.TrySend(_pipe, new string('x', 200_000), Patience);
        Assert.True(AnalyzeRequestSender.TrySend(_pipe, @"C:\after", Patience));

        Assert.Equal(@"C:\after", received.Next());
        Assert.True(received.IsEmptyAfter(TimeSpan.FromMilliseconds(100)));
    }

    [Fact]
    public void An_empty_message_is_dropped()
    {
        var (_, received) = Listen();

        AnalyzeRequestSender.TrySend(_pipe, "   ", Patience);
        Assert.True(AnalyzeRequestSender.TrySend(_pipe, @"C:\after", Patience));

        Assert.Equal(@"C:\after", received.Next());
    }

    [Fact]
    public void A_handler_that_throws_does_not_stop_the_listener()
    {
        var count = 0;
        var (_, received) = Listen(_ =>
        {
            if (Interlocked.Increment(ref count) == 1) throw new InvalidOperationException("boom");
        });

        AnalyzeRequestSender.TrySend(_pipe, @"C:\first", Patience);
        received.Next();
        Assert.True(AnalyzeRequestSender.TrySend(_pipe, @"C:\second", Patience));

        Assert.Equal(@"C:\second", received.Next());
    }

    [Fact]
    public void After_the_listener_is_disposed_nobody_is_listening()
    {
        var (listener, received) = Listen();
        Assert.True(AnalyzeRequestSender.TrySend(_pipe, @"C:\a", Patience));
        received.Next();

        listener.Dispose();

        Assert.False(AnalyzeRequestSender.TrySend(_pipe, @"C:\b", TimeSpan.FromMilliseconds(150)));
    }

    [Fact]
    public void The_default_pipe_name_is_per_user()
    {
        Assert.StartsWith("PowerTools.AnalyzeRequests.S-1-", AnalyzeRequestListener.DefaultPipeName);
    }
}
