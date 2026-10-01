using System.Security.Cryptography;
using System.Text;
using AngleSharp;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WebCrawler.Configs;
using WebCrawler.Crawlers;
using WebCrawler.Data;
using WebCrawler.Data.Entities;

namespace WebCrawler.Workers;

public class Worker : IConsumer<JobData>
{
    private readonly CrawlerPostgresContext _postgresContext;
    private readonly CrawlerMongoContext _mongoContext;
    private readonly IBrowsingContext _browsingContext;
    private readonly ILogger<Worker> _logger;
    private readonly IOptionsMonitor<BFMConfig> _bmfOptions;
    private readonly IOptionsMonitor<VedomostiConfig> _vedomostiOptions;

    public Worker(CrawlerPostgresContext postgresContext, CrawlerMongoContext mongoContext,
        IBrowsingContext browsingContext, ILogger<Worker> logger)
    {
        _postgresContext = postgresContext;
        _mongoContext = mongoContext;
        _browsingContext = browsingContext;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<JobData> context)
    {
        var url = context.Message.Url;
        var source = context.Message.Source;
        var sitemap = context.Message.SitemapUrl;

        var item = await _postgresContext.Pages
            .Where(page => page.Url == url).SingleAsync(context.CancellationToken);


        if (item.CrawledAt is not null && item.CrawledAt + TimeSpan.FromSeconds(100) > DateTime.Now)
        {
            _logger.LogInformation("Page {Url} is already in the database.", url);
            return;
        }

        var page = await _browsingContext.OpenAsync(url, context.CancellationToken);

        await using var mongoTransaction =
            await _mongoContext.Database.BeginTransactionAsync(context.CancellationToken);
        await using var postgresTransaction =
            await _postgresContext.Database.BeginTransactionAsync(context.CancellationToken);

        var entity =
            await _mongoContext.ParsedPages.FirstOrDefaultAsync(x => x.Url == url, context.CancellationToken);

        var header = string.Empty;
        var about = string.Empty;
        var content = string.Empty;

        if (source == "BFM")
        {
            header = page.QuerySelector("#og_title")?.GetAttribute("content") ?? "";
            about = page.QuerySelector("#og_description")?.GetAttribute("content") ?? "";
            var ps = page.QuerySelector(".current-article")
                    .QuerySelectorAll("p")
                    .Select(p => p.TextContent).ToList();

            content = string.Join(" ", ps);
        }
        else if (source == "Vedomosti")
        {
            var temp = page.QuerySelector(".article__body");

            header = temp.QuerySelector(".article-headline__title")?.TextContent ?? "";

            var ps = temp.QuerySelector(".article-boxes-list.article__boxes")
                .QuerySelectorAll(".box-paragraph")
                .Select(p => p.TextContent).ToList();

            content = string.Join(" ", ps);
        }

        if (entity == null)
        {
            entity = new ParsedPage
            {
                Id = item.Id,
                Url = url,
                Source = source,
                Header = header,
                About = about,
                ContentHash = Convert.ToHexString(SHA512.HashData(Encoding.UTF8.GetBytes(content!))),
                RawHtml = page.ToHtml(),
                CrawledAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            };

            await _mongoContext.ParsedPages.AddAsync(entity, context.CancellationToken);
        }
        else
        {
            entity.Header = header;
            entity.About = about;
            entity.ContentHash = Convert.ToHexString(SHA512.HashData(Encoding.UTF8.GetBytes(content!)));
            entity.RawHtml = page.ToHtml();
            entity.CrawledAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            _mongoContext.ParsedPages.Update(entity);
        }

        item.InQueue = false;
        item.CrawledAt = DateTime.UtcNow;

        await _mongoContext.SaveChangesAsync(context.CancellationToken);
        await mongoTransaction.CommitAsync(context.CancellationToken);

        await _postgresContext.SaveChangesAsync(context.CancellationToken);
        await postgresTransaction.CommitAsync(context.CancellationToken);

        var delay = TimeSpan.FromMilliseconds(source == "BFM"
            ? _bmfOptions.CurrentValue.CrawlingDelayMs
            : _vedomostiOptions.CurrentValue.CrawlingDelayMs);

        await Task.Delay(delay, context.CancellationToken);
    }
}