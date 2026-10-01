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
    private readonly IOptionsMonitor<BFMConfig> _options;
    private readonly IBrowsingContext _browsingContext;
    
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        throw new NotImplementedException();
    }
}