namespace TidyBox.Core.Feedback;

/// <param name="Logs">Only sent for <see cref="FeedbackKind.Error"/>; the caller is expected to have redacted them.</param>
public sealed record FeedbackSubmission(FeedbackKind Kind, string Message, string? Contact, string AppVersion, string? Logs);
