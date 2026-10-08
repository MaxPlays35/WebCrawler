using System.Text;
using Microsoft.EntityFrameworkCore;
using WebCrawler.Data;

namespace WebCrawler.Endpoints;

public static class PageEndpoints
{
    public static void MapPageEndpoints(this WebApplication app)
    {
        app.MapGet("/pages/{id:guid}", async Task<IResult> (
            Guid id,
            IDbContextFactory<CrawlerMongoContext> contextFactory,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
            var page = await context.ParsedPages
                .AsNoTracking()
                .Where(page => page.Id == id)
                .Select(page => new { page.RawHtml })
                .FirstOrDefaultAsync(cancellationToken);

            if (page is null || string.IsNullOrEmpty(page.RawHtml))
                return Results.NotFound(new { error = "No saved HTML was found for this page ID." });

            // Render external HTML as a static preview, isolated from this app.
            httpContext.Response.Headers.ContentSecurityPolicy = "sandbox";
            httpContext.Response.Headers.XContentTypeOptions = "nosniff";
            return Results.Content(page.RawHtml, "text/html", Encoding.UTF8);
        });
    }
}
