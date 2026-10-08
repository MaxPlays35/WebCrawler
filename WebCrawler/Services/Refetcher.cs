using AngleSharp;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WebCrawler.Configs;
using WebCrawler.Crawlers;
using WebCrawler.Data;

namespace WebCrawler.Workers;

public class Refetcher : BackgroundService
{
    private readonly ILogger<Refetcher> _logger;
    private readonly IBus _sendEndpointProvider;
    private readonly IDbContextFactory<CrawlerPostgresContext> _postgresContextFactory;
    private readonly IOptions<BFMConfig> _bmfOptions;
    private readonly IOptions<VedomostiConfig> _vedomostiOptions;

    public Refetcher(ILogger<Refetcher> logger,
        IBus sendEndpointProvider,
        IDbContextFactory<CrawlerPostgresContext> postgresContextFactory,
        IOptions<BFMConfig> bmfOptions,
        IOptions<VedomostiConfig> vedomostiOptions)
    {
        _logger = logger;
        _sendEndpointProvider = sendEndpointProvider;
        _postgresContextFactory = postgresContextFactory;
        _bmfOptions = bmfOptions;
        _vedomostiOptions = vedomostiOptions;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var endpoint = await _sendEndpointProvider.GetSendEndpoint(new Uri("queue:tasks"));

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            
            var postgresContext = await _postgresContextFactory.CreateDbContextAsync(stoppingToken);

            var bfm = await postgresContext.Pages.Where(x =>
                x.InQueue == false &&
                x.Source == "BFM" &&
                x.CrawledAt < DateTime.UtcNow - TimeSpan.FromMilliseconds(_bmfOptions.Value.RefetchDelayMs))
                .ToListAsync(stoppingToken);

            var vedomosti = await postgresContext.Pages.Where(x =>
                x.InQueue == false &&
                x.Source == "Vedomosti" &&
                x.CrawledAt < DateTime.UtcNow - TimeSpan.FromMilliseconds(_vedomostiOptions.Value.RefetchDelayMs))
                .ToListAsync(stoppingToken);

            foreach (var page in bfm)
            {
                await using var transaction = await postgresContext.Database.BeginTransactionAsync(stoppingToken);

                page.InQueue = true;
                await endpoint.Send(
                    new JobData {
                        Url = page.Url,
                        SitemapUrl = page.Url,
                        LastModified = DateTimeOffset.UtcNow,
                        Source = "BFM"
                    }, stoppingToken);
                
                await postgresContext.SaveChangesAsync(stoppingToken);
                await transaction.CommitAsync(stoppingToken);
            }
            
            foreach (var page in vedomosti)
            {
                await using var transaction = await postgresContext.Database.BeginTransactionAsync(stoppingToken);

                page.InQueue = true;
                await endpoint.Send(
                    new JobData {
                        Url = page.Url,
                        SitemapUrl = page.Url,
                        LastModified = DateTimeOffset.UtcNow,
                        Source = "Vedomosti"
                    }, stoppingToken);
                
                await postgresContext.SaveChangesAsync(stoppingToken);
                await transaction.CommitAsync(stoppingToken);
            }
        }
    }
}