using AngleSharp;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WebCrawler.Data;
using WebCrawler.Data.Entities;

namespace WebCrawler.Crawlers;

public class Vedomosti : BackgroundService
{
    private readonly ILogger<Vedomosti> _logger;
    private readonly IBus _sendEndpointProvider;
    private readonly IDbContextFactory<CrawlerPostgresContext> _postgresContextFactory;
    private readonly IBrowsingContext _browsingContext;
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
        IBrowsingContext browsingContext)
    {
        _logger = logger;
        _sendEndpointProvider = sendEndpointProvider;
        _postgresContextFactory = postgresContextFactory;
        _browsingContext = browsingContext;
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
                                x.QuerySelector("last-modified").TextContent,
                                out var lastModified)
                                ? lastModified
                                : (DateTimeOffset?)null,
                        });

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
                        InQueue = true
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
                
                await Task.Delay(5000, stoppingToken);
            }
            
            await Task.Delay(5000, stoppingToken);
        }
    }
}