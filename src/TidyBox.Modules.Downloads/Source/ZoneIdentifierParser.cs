namespace TidyBox.Modules.Downloads.Source;

public static class ZoneIdentifierParser
{
    /// <summary>
    /// The site a download came from, from the text of a file's Zone.Identifier stream: the referring page's domain if there is one, else the
    /// domain it was fetched from. Only the host is returned, never the path or query, which can carry tokens. Null when there is no usable
    /// http(s) address (for example "about:internet", which Windows writes when the browser gave no address).
    /// </summary>
    public static string? ParseDomain(string content)
    {
        string? referrer = null, host = null;
        foreach (var line in content.Split('\n'))
        {
            var text = line.Trim();
            if (text.StartsWith("ReferrerUrl=", StringComparison.OrdinalIgnoreCase)) referrer = text["ReferrerUrl=".Length..];
            else if (text.StartsWith("HostUrl=", StringComparison.OrdinalIgnoreCase)) host = text["HostUrl=".Length..];
        }

        return DomainOf(referrer) ?? DomainOf(host);
    }

    private static string? DomainOf(string? url)
        => url is not null && Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" ? uri.Host : null;
}
