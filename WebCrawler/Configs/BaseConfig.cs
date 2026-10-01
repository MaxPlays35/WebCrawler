namespace WebCrawler.Configs;

public record BaseConfig
{
    public long BatchDelayMs { get; init; }
    public long CrawlingDelayMs { get; init; }
    public long RefetchDelayMs { get; init; }
};