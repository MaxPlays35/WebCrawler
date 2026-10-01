using System.Security.Cryptography;
using System.Text;
using AngleSharp;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
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

    public Worker(CrawlerPostgresContext postgresContext, CrawlerMongoContext mongoContext, IBrowsingContext browsingContext, ILogger<Worker> logger)
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
        

        if (item.CrawledAt + TimeSpan.FromSeconds(100) > DateTime.Now)
        {
            _logger.LogInformation("Page {Url} is already in the database.", url);
            return;
        }
        
        var page = await _browsingContext.OpenAsync(url, context.CancellationToken);

        await using var transaction = await _mongoContext.Database.BeginTransactionAsync(context.CancellationToken);
        
        if (source == "BFM")
        {
            
            var article = BfmArticleParser.Parse(page);
            var header = article.Title;
            var about = article.About;
            var content = article.Content;
            
            var entity =
                await _mongoContext.ParsedPages.FirstOrDefaultAsync(x => x.Url == url, context.CancellationToken);

            if (entity == null)
            {
                entity = new ParsedPage
                {
                    Url = url,
                    Source = "BFM",
                    ContentHash = Convert.ToHexString(SHA512.HashData(Encoding.UTF8.GetBytes(content!))),
                    RawHtml = content,
                    CrawledAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                };
                
                await _mongoContext.ParsedPages.AddAsync(entity, context.CancellationToken);
            } else {
                entity.ContentHash = Convert.ToHexString(SHA512.HashData(Encoding.UTF8.GetBytes(content!)));
                entity.RawHtml = content;
                entity.CrawledAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                
                _mongoContext.ParsedPages.Update(entity);
            }

            await _mongoContext.SaveChangesAsync(context.CancellationToken);
        } else if (source == "Vedomosti") {
            
        }
        
        await transaction.CommitAsync(context.CancellationToken);
        
        item.InQueue = false;
        await _postgresContext.SaveChangesAsync(context.CancellationToken);
        
        await Task.Delay(5000);
    }
}
