using System.Xml.Linq;
using AngleSharp;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WebCrawler.Configs;
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
    private readonly IOptions<BFMConfig> _options;
    private readonly IBrowsingContext _browsingContext;

    public Bfm(ILogger<Bfm> logger, 
        IHttpClientFactory clientFactory, 
        IBus sendEndpointProvider, 
        IDbContextFactory<CrawlerPostgresContext> postgresContextFactory,
        IOptions<BFMConfig> options,
        IBrowsingContext browsingContext)
    {
        _logger = logger;
        _clientFactory = clientFactory;
        _sendEndpointProvider = sendEndpointProvider;
        _postgresContextFactory = postgresContextFactory;
        _options = options;
        _browsingContext = browsingContext;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var endpoint = await _sendEndpointProvider.GetSendEndpoint(new Uri("queue:tasks"));
        var postgresContext =  await _postgresContextFactory.CreateDbContextAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var xml = await _browsingContext.OpenAsync("https://www.bfm.ru/sitemap.xml", stoppingToken);
            

            var sitemaps = xml
                .QuerySelectorAll("sitemap")
                .Select(x => new
                {
                    Url = x.QuerySelector("loc").TextContent,
                    LastModified = DateTimeOffset.TryParse(
                        x.QuerySelector("lastmod")?.TextContent,
                        out var lastModified)
                        ? lastModified
                        : (DateTimeOffset?)null
                })
                .Where(x => x.Url is not null)
                .Skip(2)
                .ToList();

            foreach (var sitemap in sitemaps)
            {
                var articles = await _browsingContext.OpenAsync(sitemap.Url, stoppingToken);

                var parsed = articles.QuerySelectorAll("url").Select(x =>
                        new
                        {
                            Url = x.QuerySelector("loc")?.TextContent,
                            LastModified = DateTimeOffset.TryParse(
                                x.QuerySelector("lastmod")?.TextContent,
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
                        InQueue = true,
                        Source = "BFM"
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
                await Task.Delay(TimeSpan.FromMilliseconds(_options.Value.BatchDelayMs), stoppingToken);
            }
            await Task.Delay(TimeSpan.FromMilliseconds(_options.Value.RefetchDelayMs), stoppingToken);
        }
    }
}