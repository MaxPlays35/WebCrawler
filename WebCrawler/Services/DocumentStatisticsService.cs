using Microsoft.EntityFrameworkCore;
using WebCrawler.Data;
using MongoString = MongoDB.Driver.Linq.StringExtensions;

namespace WebCrawler.Services;

public sealed record DocumentSizeSample(string Url, long RawHtmlBytes, long TextBytes);

public sealed record DocumentStatistics(
    long DocumentsCount,
    long TotalRawHtmlBytes,
    long TotalTextBytes,
    double AverageRawHtmlBytes,
    double AverageTextBytes,
    IReadOnlyList<DocumentSizeSample> Samples)
{
    public string SizeUnit => "bytes";
    public string Encoding => "UTF-8";
    public string TextField => "Content";
}

public sealed class DocumentStatisticsService(IDbContextFactory<CrawlerMongoContext> contextFactory)
{
    public async Task<DocumentStatistics> GetAsync(
        int sampleCount, CancellationToken cancellationToken)
    {
        if (sampleCount is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(sampleCount));

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var documents = context.ParsedPages.AsNoTracking();

        // MongoDB translates StrLenBytes to $strLenBytes; coalescing becomes $ifNull.
        // Cast before summing so collection totals can exceed Int32.MaxValue.
        // Run sequentially: EF does not support concurrent queries on one context.
        var documentsCount = await documents.LongCountAsync(cancellationToken);
        var totalRawHtmlBytes = await documents.SumAsync(
            page => (long)MongoString.StrLenBytes(page.RawHtml ?? string.Empty),
            cancellationToken);
        var totalTextBytes = await documents.SumAsync(
            page => (long)MongoString.StrLenBytes(page.Content ?? string.Empty),
            cancellationToken);
        var sampleSizes = await documents
            .Take(sampleCount)
            .Select(page => new
            {
                Url = page.Url ?? string.Empty,
                RawHtmlBytes = (long)MongoString.StrLenBytes(page.RawHtml ?? string.Empty),
                TextBytes = (long)MongoString.StrLenBytes(page.Content ?? string.Empty)
            })
            .ToListAsync(cancellationToken);
        var samples = sampleSizes
            .Select(page => new DocumentSizeSample(page.Url, page.RawHtmlBytes, page.TextBytes))
            .ToList();

        return new DocumentStatistics(
            documentsCount,
            totalRawHtmlBytes,
            totalTextBytes,
            documentsCount == 0 ? 0 : (double)totalRawHtmlBytes / documentsCount,
            documentsCount == 0 ? 0 : (double)totalTextBytes / documentsCount,
            samples);
    }
}
