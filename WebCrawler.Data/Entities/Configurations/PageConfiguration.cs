using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace WebCrawler.Data.Entities.Configurations;

public class PageConfiguration : IEntityTypeConfiguration<Page>
{
    public void Configure(EntityTypeBuilder<Page> builder)
    {
        builder.HasKey(x => x.Id);
        
        builder.Property(x => x.Url).IsRequired();
        builder.Property(x => x.SitemapUrl).IsRequired();
    }
}