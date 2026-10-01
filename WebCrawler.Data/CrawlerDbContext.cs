using Microsoft.EntityFrameworkCore;
using WebCrawler.Data.Entities;
using WebCrawler.Data.Entities.Configurations;

namespace WebCrawler.Data;

public class CrawlerMongoContext(DbContextOptions<CrawlerMongoContext> options) : DbContext(options)
{
    public DbSet<ParsedPage> ParsedPages { get; set; }
}

public class CrawlerPostgresContext(DbContextOptions<CrawlerPostgresContext> options) : DbContext(options)
{
    // public DbSet<Source> Sources { get; set; }
    
    public DbSet<Page> Pages { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new PageConfiguration());
    }
}