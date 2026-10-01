using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MongoDB.EntityFrameworkCore.Extensions;

namespace WebCrawler.Data.Entities.Configurations;

public class ParsedPageConfiguration : IEntityTypeConfiguration<ParsedPage>
{
    public void Configure(EntityTypeBuilder<ParsedPage> builder)
    {
        builder.HasKey(x => x.Id);
        
        builder.HasIndex(x => x.Url).IsUnique();
        
        builder.ToCollection("ParsedPages");
    }
}