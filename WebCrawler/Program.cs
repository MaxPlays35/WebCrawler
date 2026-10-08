using AngleSharp;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using WebCrawler.Configs;
using WebCrawler.Crawlers;
using WebCrawler.Data;
using WebCrawler.Workers;

var builder = Host.CreateApplicationBuilder(args);

var configuration = builder.Configuration
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", false)
    .Build();

var config = Configuration.Default.WithDefaultLoader().WithXml();

builder.Services.Configure<BFMConfig>(configuration.GetRequiredSection("BFM"));
builder.Services.Configure<VedomostiConfig>(configuration.GetRequiredSection("Vedomosti"));

builder.Services.AddSingleton(BrowsingContext.New(config));

builder.Services.AddLogging();
builder.Services.AddDbContextFactory<CrawlerPostgresContext>(options =>
    options.UseNpgsql(
        configuration.GetConnectionString("postgres")
    ));
builder.Services.AddDbContextFactory<CrawlerMongoContext>(options =>
    options.UseMongoDB(configuration.GetConnectionString("mongo"), "crawler"));

builder.Services.AddOptions<SqlTransportOptions>().Configure(options =>
{
    options.ConnectionString = configuration.GetConnectionString("postgres");
});

builder.Services.AddHttpClient();

builder.Services.AddPostgresMigrationHostedService();

builder.Services.AddMassTransit(options =>
{
    options.AddConsumer<Worker>(cfg =>
        {
            cfg.ConcurrentMessageLimit = configuration.GetRequiredSection("WorkerCount").Get<int>();
        }).Endpoint(endpoint => endpoint.Name = "tasks");

    options.AddSqlMessageScheduler();

    options.UsingPostgres((context, cfg) =>
    {
        cfg.UseSqlMessageScheduler();

        cfg.ConfigureEndpoints(context);
    });
});

builder.Services.AddHostedService<Bfm>();
builder.Services.AddHostedService<Vedomosti>();
builder.Services.AddHostedService<Refetcher>();

using var host = builder.Build();

await host.RunAsync();