namespace WebCrawler.Data.Entities;

public class Source
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public required string Url { get; set; }
    public Status Status { get; set; }
}