namespace PowerTools.App.Feedback;

internal static class FeedbackConfig
{
    /// <summary>The feedback server. Empty until the Worker is deployed; while it is empty the feedback entry stays hidden.</summary>
    public const string Endpoint = "https://wlfeedback.siazon.cc/v1/feedback";

    /// <returns>Where to send feedback, or null when there is no usable address (the entry is then not shown).</returns>
    public static Uri? ResolveEndpoint()
    {
        var text = Endpoint;
#if DEBUG
        // Lets a debug build talk to a local `wrangler dev` before anything is deployed.
        var local = Environment.GetEnvironmentVariable("POWERTOOLS_FEEDBACK_ENDPOINT");
        if (!string.IsNullOrWhiteSpace(local)) text = local;
#endif
        return Uri.TryCreate(text, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" ? uri : null;
    }
}
