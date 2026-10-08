using MongoDB.Bson;

namespace WebCrawler.Data.Entities;

public class ParsedPage
{
    public Guid Id { get; set; }

    public string Url { get; set; } = null!;
    public string Source { get; set; } = null!;

    public string Header { get; set; } = null!;
    
    public string About { get; set; } = null!;
    
    public string Content { get; set; } = null!;

    public string RawHtml { get; set; } = null!;

    public long CrawledAt { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    public string ContentHash { get; set; } = null!;
}