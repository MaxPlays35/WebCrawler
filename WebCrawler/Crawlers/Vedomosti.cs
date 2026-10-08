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

public class Vedomosti : BackgroundService
{
    private readonly ILogger<Vedomosti> _logger;
    private readonly IBus _sendEndpointProvider;
    private readonly IDbContextFactory<CrawlerPostgresContext> _postgresContextFactory;
    private readonly IBrowsingContext _browsingContext;
    private readonly IOptions<VedomostiConfig> _options;
    private readonly string[] _sitemaps = [
        "https://www.vedomosti.ru/sitemap_news1.xml",
        "https://www.vedomosti.ru/sitemap_news2.xml",
        "https://www.vedomosti.ru/sitemap_news3.xml",
        "https://www.vedomosti.ru/sitemap_news4.xml",
        "https://www.vedomosti.ru/sitemap_news5.xml"
    ];
    
    public Vedomosti(ILogger<Vedomosti> logger, 
        IHttpClientFactory clientFactory, 
        IBus sendEndpointProvider, 
        IDbContextFactory<CrawlerPostgresContext> postgresContextFactory,
        IBrowsingContext browsingContext,
        IOptions<VedomostiConfig> options)
    {
        _logger = logger;
        _sendEndpointProvider = sendEndpointProvider;
        _postgresContextFactory = postgresContextFactory;
        _browsingContext = browsingContext;
        _options = options;
    }
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var endpoint = await _sendEndpointProvider.GetSendEndpoint(new Uri("queue:tasks"));
        var postgresContext =  await _postgresContextFactory.CreateDbContextAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            foreach (var sitemap in _sitemaps)
            {
                var articlesRaw = await _browsingContext.OpenAsync(sitemap, stoppingToken);

                var articles = 
                    articlesRaw.QuerySelectorAll("url")
                        .Select(x => new
                        {
                            Url = x.QuerySelector("loc").TextContent,
                            LastModified = DateTimeOffset.TryParse(
                                x.QuerySelector("lastmod")?.TextContent,
                                out var lastModified)
                                ? lastModified
                                : (DateTimeOffset?)null,
                        }).ToList();

                foreach (var article in articles)
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
                        Url = article.Url,
                        SitemapUrl = sitemap,
                        InQueue = true,
                        Source = "Vedomosti"
                    };

                    await postgresContext.Pages.AddAsync(page, stoppingToken);
                    await postgresContext.SaveChangesAsync(stoppingToken);
                    
                    await endpoint.Send(
                        new JobData {
                            Url = article.Url!,
                            SitemapUrl = sitemap,
                            LastModified = article.LastModified,
                            Source = "Vedomosti"
                        }, stoppingToken);
                    
                    await transaction.CommitAsync(stoppingToken);
                }
                
                _logger.LogInformation("Processed {Count} sitemap urls", articles.Count);
                await Task.Delay(TimeSpan.FromMilliseconds(_options.Value.BatchDelayMs), stoppingToken);
            }
            
            await Task.Delay(TimeSpan.FromMilliseconds(_options.Value.RefetchDelayMs), stoppingToken);
        }
    }
}