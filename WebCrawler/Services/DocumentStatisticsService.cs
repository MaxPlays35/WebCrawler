using MongoDB.Bson;
using MongoDB.Driver;

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

public sealed class DocumentStatisticsService(IMongoDatabase database)
{
    public async Task<DocumentStatistics> GetAsync(
        int sampleCount, CancellationToken cancellationToken)
    {
        if (sampleCount is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(sampleCount));

        // Project only sizes and URLs before the facet; HTML never leaves MongoDB.
        // Both facets consume the same input in one collection scan.
        BsonDocument[] pipeline =
        [
            BsonDocument.Parse("""
                { "$project": {
                    "_id": 0,
                    "url": { "$ifNull": ["$Url", ""] },
                    "rawHtmlBytes": { "$strLenBytes": { "$ifNull": ["$RawHtml", ""] } },
                    "textBytes": { "$strLenBytes": { "$ifNull": ["$Content", ""] } }
                } }
                """),
            BsonDocument.Parse($$"""
                { "$facet": {
                    "totals": [
                        { "$group": {
                            "_id": null,
                            "documentsCount": { "$sum": 1 },
                            "totalRawHtmlBytes": { "$sum": "$rawHtmlBytes" },
                            "totalTextBytes": { "$sum": "$textBytes" },
                            "averageRawHtmlBytes": { "$avg": "$rawHtmlBytes" },
                            "averageTextBytes": { "$avg": "$textBytes" }
                        } }
                    ],
                    "samples": [{ "$limit": {{sampleCount}} }]
                } }
                """),
        ];

        var collection = database.GetCollection<BsonDocument>("ParsedPages");
        using var cursor = await collection.AggregateAsync<BsonDocument>(
            pipeline, cancellationToken: cancellationToken);
        var result = await cursor.FirstOrDefaultAsync(cancellationToken);
        var totals = result?["totals"].AsBsonArray.FirstOrDefault()?.AsBsonDocument;
        var samples = result?["samples"].AsBsonArray
            .Select(value => new DocumentSizeSample(
                value["url"].AsString,
                value["rawHtmlBytes"].ToInt64(),
                value["textBytes"].ToInt64()))
            .ToList() ?? [];

        return new DocumentStatistics(
            totals?["documentsCount"].ToInt64() ?? 0,
            totals?["totalRawHtmlBytes"].ToInt64() ?? 0,
            totals?["totalTextBytes"].ToInt64() ?? 0,
            totals?["averageRawHtmlBytes"].ToDouble() ?? 0,
            totals?["averageTextBytes"].ToDouble() ?? 0,
            samples);
    }
}
