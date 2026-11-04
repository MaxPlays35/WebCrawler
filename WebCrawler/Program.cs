using AngleSharp;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using MongoDB.Driver;
using Scalar.AspNetCore;
using WebCrawler.Configs;
using WebCrawler.Crawlers;
using WebCrawler.Data;
using WebCrawler.Endpoints;
using WebCrawler.Services;
using WebCrawler.Workers;

var builder = WebApplication.CreateBuilder(args);

// Keep the default provider order so Kubernetes environment variables override JSON.
var configuration = builder.Configuration;
var config = Configuration.Default.WithDefaultLoader().WithXml();

builder.Services.Configure<BFMConfig>(configuration.GetRequiredSection("BFM"));
builder.Services.Configure<VedomostiConfig>(configuration.GetRequiredSection("Vedomosti"));

builder.Services.AddSingleton(BrowsingContext.New(config));

builder.Services.AddLogging();
builder.Services.AddDbContextFactory<CrawlerPostgresContext>(options =>
    options.UseNpgsql(
        configuration.GetConnectionString("postgres")
    ));
var mongoClient = new MongoClient(configuration.GetConnectionString("mongo")
                                  ?? throw new InvalidOperationException("Missing mongo connection string."));
builder.Services.AddSingleton<IMongoClient>(mongoClient);
builder.Services.AddDbContextFactory<CrawlerMongoContext>(options =>
    options.UseMongoDB(configuration.GetConnectionString("mongo"), "crawler"));

builder.Services.AddSingleton<DocumentStatisticsService>();

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
        cfg.ConcurrentMessageLimit = configuration.GetValue<int>("WorkerCount", 5);
    }).Endpoint(endpoint => endpoint.Name = "tasks");

    options.AddSqlMessageScheduler();

    options.UsingPostgres((context, cfg) =>
    {
        cfg.UseSqlMessageScheduler();
        
        cfg.UseMessageRetry(r => r.Exponential(3, TimeSpan.FromSeconds(1), 
            TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(1)));

        cfg.ConfigureEndpoints(context);
    });
});

builder.Services.Configure<MassTransitHostOptions>(options => options.WaitUntilStarted = true);
builder.Services.AddHealthChecks();
builder.Services.AddHostedService<Bfm>();
builder.Services.AddHostedService<Vedomosti>();
// builder.Services.AddHostedService<Refetcher>();
builder.Services.AddOpenApi();

await using var app = builder.Build();

if (args.Contains("--migrate"))
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<CrawlerPostgresContext>();
    await db.Database.MigrateAsync();
    return;
}

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready");
app.MapStatisticsEndpoints();
app.MapPageEndpoints();

app.MapOpenApi();
app.MapScalarApiReference();

await app.RunAsync();
