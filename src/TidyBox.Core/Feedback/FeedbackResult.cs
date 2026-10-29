namespace TidyBox.Core.Feedback;

public enum FeedbackOutcome
{
    Sent,

    /// <summary>The server refused the content (400/413).</summary>
    Rejected,

    /// <summary>Too many feedbacks from this address, or the server is full for today (429/503).</summary>
    RateLimited,

    /// <summary>Could not get a proper answer: no network, a timeout, or an unexpected status.</summary>
    Unreachable,
}

/// <param name="Detail">For a rejection, the field the server named, if it did; for an unexpected status, "HTTP nnn".</param>
public sealed record FeedbackResult(FeedbackOutcome Outcome, string? Detail);
