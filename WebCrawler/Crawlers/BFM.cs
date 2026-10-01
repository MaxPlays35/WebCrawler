using System.Xml.Linq;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WebCrawler.Data;
using WebCrawler.Data.Entities;

namespace WebCrawler.Crawlers;

public record JobData
{
    public required string Url { get; init; }
    
    public required string SitemapUrl { get; init; }
    public required string Source { get; init; }
    public DateTimeOffset? LastModified { get; init; }
}

public class Bfm : BackgroundService
{
    private readonly IHttpClientFactory _clientFactory;
    private readonly ILogger<Bfm> _logger;
    private readonly IBus _sendEndpointProvider;
    private readonly IDbContextFactory<CrawlerPostgresContext> _postgresContextFactory;

    public Bfm(ILogger<Bfm> logger, 
        IHttpClientFactory clientFactory, 
        IBus sendEndpointProvider, 
        IDbContextFactory<CrawlerPostgresContext> postgresContextFactory)
    {
        _logger = logger;
        _clientFactory = clientFactory;
        _sendEndpointProvider = sendEndpointProvider;
        _postgresContextFactory = postgresContextFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var client = _clientFactory.CreateClient("bfm");
        var endpoint = await _sendEndpointProvider.GetSendEndpoint(new Uri("queue:tasks"));
        var postgresContext =  await _postgresContextFactory.CreateDbContextAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var xml = await client.GetStringAsync("https://www.bfm.ru/sitemap.xml", stoppingToken);

            var document = XDocument.Parse(xml);
            XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";

            var sitemaps = document
                .Root!
                .Elements(ns + "sitemap")
                .Select(x => new
                {
                    Url = x.Element(ns + "loc")?.Value,
                    LastModified = DateTimeOffset.TryParse(
                        x.Element(ns + "lastmod")?.Value,
                        out var lastModified)
                        ? lastModified
                        : (DateTimeOffset?)null
                })
                .Where(x => x.Url is not null)
                .Skip(2)
                .ToList();

            foreach (var sitemap in sitemaps)
            {
                var articlesXml = await client.GetStringAsync(sitemap.Url, stoppingToken);

                var articles = XDocument.Parse(articlesXml);

                var parsed = articles.Root!.Elements(ns + "url").Select(x =>
                        new
                        {
                            Url = x.Element(ns + "loc")?.Value,
                            LastModified = DateTimeOffset.TryParse(
                                x.Element(ns + "lastmod")?.Value,
                                out var lastModified)
                                ? lastModified
                                : (DateTimeOffset?)null
                        })
                    .Where(x => !x.Url?.Contains("gallery") ?? false)
                    .ToList();

                foreach (var article in parsed)
                {
                    var exists = await postgresContext.Pages
                        .Where(x => x.Url == article.Url)
                        .AnyAsync(stoppingToken);

                    if (exists)
                    {
                        continue;
                    }
                    
                    await using var transaction =
                         await postgresContext.Database.BeginTransactionAsync(stoppingToken);
                    
                    var page = new Page
                    {
                        Url = article.Url!,
                        SitemapUrl = sitemap.Url!,
                        InQueue = true
                    };

                    await postgresContext.Pages.AddAsync(page, stoppingToken);
                    await postgresContext.SaveChangesAsync(stoppingToken);
                    
                    await endpoint.Send(
                        new JobData {
                        Url = article.Url!,
                        SitemapUrl = sitemap.Url!,
                        LastModified = article.LastModified,
                        Source = "BFM"
                    }, stoppingToken);
                    
                    await transaction.CommitAsync(stoppingToken);
                }

                _logger.LogInformation("Processed {Count} sitemap urls", parsed.Count);
                await Task.Delay(5000, stoppingToken);
            }
            await Task.Delay(1000 * 60 * 5, stoppingToken);
        }
    }
}