using System.Security.Cryptography;
using System.Text;
using AngleSharp;
using AngleSharp.Dom;
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
    private readonly IOptions<BFMConfig> _bmfOptions;
    private readonly IBrowsingContext _browsingContext;
    private readonly ILogger<Worker> _logger;
    private readonly CrawlerMongoContext _mongoContext;
    private readonly CrawlerPostgresContext _postgresContext;
    private readonly IOptions<VedomostiConfig> _vedomostiOptions;

    public Worker(CrawlerPostgresContext postgresContext, CrawlerMongoContext mongoContext,
        IBrowsingContext browsingContext, ILogger<Worker> logger, IOptions<BFMConfig> bmfOptions,
        IOptions<VedomostiConfig> vedomostiOptions)
    {
        _postgresContext = postgresContext;
        _mongoContext = mongoContext;
        _browsingContext = browsingContext;
        _logger = logger;
        _bmfOptions = bmfOptions;
        _vedomostiOptions = vedomostiOptions;
    }

    public async Task Consume(ConsumeContext<JobData> context)
    {
        var url = context.Message.Url;
        var source = context.Message.Source;
        var sitemap = context.Message.SitemapUrl;

        var item = await _postgresContext.Pages
            .Where(page => page.Url == url).SingleAsync(context.CancellationToken);
        await using var postgresTransaction =
            await _postgresContext.Database.BeginTransactionAsync(context.CancellationToken);


        if (item.CrawledAt is not null && item.CrawledAt > DateTime.UtcNow - TimeSpan.FromMilliseconds(source == "BFM"
                ? _bmfOptions.Value.RefetchDelayMs
                : _vedomostiOptions.Value.RefetchDelayMs))
        {
            _logger.LogInformation("Page {Url} is already in the crawled.", url);

            item.InQueue = false;
            await _postgresContext.SaveChangesAsync(context.CancellationToken);
            await postgresTransaction.CommitAsync(context.CancellationToken);

            return;
        }

        var page = await _browsingContext.OpenAsync(url, context.CancellationToken);

        await using var mongoTransaction =
            await _mongoContext.Database.BeginTransactionAsync(context.CancellationToken);

        var entity =
            await _mongoContext.ParsedPages.FirstOrDefaultAsync(x => x.Url == url, context.CancellationToken);

        var header = string.Empty;
        var about = string.Empty;
        var content = string.Empty;

        if (source == "BFM")
        {
            header = page.QuerySelector("#og_title")?.GetAttribute("content") ?? "";
            about = page.QuerySelector("#og_description")?.GetAttribute("content") ?? "";
            var article = page.QuerySelector(".current-article")
                ?? throw new InvalidOperationException("BFM article body was not found.");
            var body = ((IElement)article.Clone(true))
                .RemoveElements()
                .RemoveElements(BfmArticleParser.ExcludedSelectors);
            var ps = body
                .QuerySelectorAll("p")
                .Select(p => p.TextContent).ToList();

            content = string.Join(" ", ps);
        }
        else if (source == "Vedomosti")
        {
            var temp = page.QuerySelector(".article__body")
                ?? throw new InvalidOperationException("Vedomosti article body was not found.");

            header = temp.QuerySelector(".article-headline__title")?.TextContent ?? "";

            var article = temp.QuerySelector(".article-boxes-list.article__boxes")
                ?? throw new InvalidOperationException("Vedomosti article content was not found.");
            var body = ((IElement)article.Clone()).RemoveElements();
            var ps = body
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
                Content = content,
                ContentHash = Convert.ToHexString(
                    SHA512.HashData(Encoding.UTF8.GetBytes(header + about + content))),
                RawHtml = page.ToHtml(),
                CrawledAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };

            await _mongoContext.ParsedPages.AddAsync(entity, context.CancellationToken);
        }
        else
        {
            var hash = Convert.ToHexString(SHA512.HashData(
                Encoding.UTF8.GetBytes(header + about + content)));

            if (hash != entity.ContentHash)
            {
                entity.Header = header;
                entity.About = about;
                entity.Content = content;
                entity.ContentHash = hash;
                entity.RawHtml = page.ToHtml();
                entity.CrawledAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

                _mongoContext.ParsedPages.Update(entity);
            }
        }

        item.InQueue = false;
        item.CrawledAt = DateTime.UtcNow;

        await _mongoContext.SaveChangesAsync(context.CancellationToken);
        await mongoTransaction.CommitAsync(context.CancellationToken);

        await _postgresContext.SaveChangesAsync(context.CancellationToken);
        await postgresTransaction.CommitAsync(context.CancellationToken);

        var delay = TimeSpan.FromMilliseconds(source == "BFM"
            ? _bmfOptions.Value.CrawlingDelayMs
            : _vedomostiOptions.Value.CrawlingDelayMs);

        await Task.Delay(delay, context.CancellationToken);
    }
}
