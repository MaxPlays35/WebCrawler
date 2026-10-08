using System.Text;
using Microsoft.EntityFrameworkCore;
using WebCrawler.Data;

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
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var documents = context.ParsedPages
            .AsNoTracking()
            .Select(page => new { page.Url, page.RawHtml, page.Content })
            .AsAsyncEnumerable();

        long documentsCount = 0;
        long totalRawHtmlBytes = 0;
        long totalTextBytes = 0;
        var samples = new List<DocumentSizeSample>(sampleCount);

        // Stream projected fields through EF; UTF-8 byte counting runs locally.
        // Keep only totals and the requested samples in memory.
        await foreach (var document in documents.WithCancellation(cancellationToken))
        {
            var rawHtmlBytes = Encoding.UTF8.GetByteCount(document.RawHtml ?? string.Empty);
            var textBytes = Encoding.UTF8.GetByteCount(document.Content ?? string.Empty);

            documentsCount++;
            totalRawHtmlBytes += rawHtmlBytes;
            totalTextBytes += textBytes;

            if (samples.Count < sampleCount)
                samples.Add(new DocumentSizeSample(
                    document.Url ?? string.Empty, rawHtmlBytes, textBytes));
        }

        return new DocumentStatistics(
            documentsCount,
            totalRawHtmlBytes,
            totalTextBytes,
            documentsCount == 0 ? 0 : (double)totalRawHtmlBytes / documentsCount,
            documentsCount == 0 ? 0 : (double)totalTextBytes / documentsCount,
            samples);
    }
}
