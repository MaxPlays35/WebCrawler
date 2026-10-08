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
    private readonly ILogger<Bfm> _logger;
    private readonly IBus _sendEndpointProvider;
    private readonly IDbContextFactory<CrawlerPostgresContext> _postgresContextFactory;
    private readonly IOptions<BFMConfig> _bmfOptions;
    private readonly IOptions<VedomostiConfig> _vedomostiOptions;
    private readonly IBrowsingContext _browsingContext;

    public Refetcher(ILogger<Bfm> logger,
        IBus sendEndpointProvider,
        IDbContextFactory<CrawlerPostgresContext> postgresContextFactory,
        IOptions<BFMConfig> bmfOptions,
        IOptions<VedomostiConfig> vedomostiOptions,
        IBrowsingContext browsingContext)
    {
        _logger = logger;
        _sendEndpointProvider = sendEndpointProvider;
        _postgresContextFactory = postgresContextFactory;
        _bmfOptions = bmfOptions;
        _vedomostiOptions = vedomostiOptions;
        _browsingContext = browsingContext;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var endpoint = await _sendEndpointProvider.GetSendEndpoint(new Uri("queue:tasks"));
        var postgresContext = await _postgresContextFactory.CreateDbContextAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);

            var bfm = postgresContext.Pages.Where(x =>
                x.InQueue == false &&
                x.Source == "BFM" &&
                x.CrawledAt < DateTime.Now - TimeSpan.FromMilliseconds(_bmfOptions.Value.RefetchDelayMs));

            var vedomosti = postgresContext.Pages.Where(x =>
                x.InQueue == false &&
                x.Source == "Vedomosti" &&
                x.CrawledAt < DateTime.Now - TimeSpan.FromMilliseconds(_vedomostiOptions.Value.RefetchDelayMs));

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