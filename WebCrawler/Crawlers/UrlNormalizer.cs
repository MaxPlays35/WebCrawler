namespace WebCrawler.Crawlers;

public static class UrlNormalizer
{
    public static string Normalize(string url)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp &&
             uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException(
                "Expected an absolute HTTP or HTTPS URL.", nameof(url));
        }

        var builder = new UriBuilder(uri)
        {
            Host = uri.Host.ToLowerInvariant(),
            Fragment = string.Empty,
            Port = uri.IsDefaultPort ? -1 : uri.Port
        };

        return builder.Uri.AbsoluteUri;
    }
}
