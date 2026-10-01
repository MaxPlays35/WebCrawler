namespace WebCrawler.Data.Entities;

public class Page
{
    public Guid Id { get; set; }
    
    public string Url { get; set; } = null!;
    public string SitemapUrl { get; set; } = null!;
    
    public bool InQueue { get; set; }

    public DateTime? CrawledAt { get; set; } = null;
}