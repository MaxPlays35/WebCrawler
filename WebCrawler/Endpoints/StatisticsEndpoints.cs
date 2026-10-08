using WebCrawler.Services;

namespace WebCrawler.Endpoints;

public static class StatisticsEndpoints
{
    public static void MapStatisticsEndpoints(this WebApplication app)
    {
        app.MapGet("/stats", async Task<IResult> (
            int? sampleCount,
            DocumentStatisticsService statistics,
            CancellationToken cancellationToken) =>
        {
            var count = sampleCount ?? 5;
            if (count is < 1 or > 100)
                return Results.BadRequest(new { error = "sampleCount must be between 1 and 100." });

            return Results.Ok(await statistics.GetAsync(count, cancellationToken));
        });
    }
}
