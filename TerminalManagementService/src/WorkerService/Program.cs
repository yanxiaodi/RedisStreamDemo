using StackExchange.Redis;
using TerminalManagement.Shared.Models;
using TerminalManagement.Shared.Services;
using WorkerService.Services;
using Polly;
using Polly.Bulkhead;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// Configuration
var terminalConfig = builder.Configuration.GetSection("TerminalConfiguration").Get<TerminalConfiguration>()
    ?? throw new InvalidOperationException("TerminalConfiguration is required");

builder.Services.AddSingleton(terminalConfig);

// HTTP client for terminal communications
builder.Services.AddHttpClient();

// Redis configuration
builder.Services.AddSingleton<IConnectionMultiplexer>(provider =>
{
    var configuration = ConfigurationOptions.Parse(terminalConfig.RedisConnectionString);
    return ConnectionMultiplexer.Connect(configuration);
});

builder.Services.AddScoped<IRedisService, RedisService>();

// Terminal pool setup
builder.Services.AddSingleton<ITerminalPool>(provider =>
{
    var logger = provider.GetRequiredService<ILogger<TerminalPool>>();
    var httpClientFactory = provider.GetRequiredService<IHttpClientFactory>();
    
    // Parse terminal data and create 4 terminals for this worker
    var terminals = ParseTerminalData(terminalConfig.TerminalsData, terminalConfig.PodName)
        .Take(4) // Each worker manages 4 terminals
        .ToList();
    
    return new TerminalPool(terminals, logger, terminalConfig, httpClientFactory);
});

// Polly bulkhead policy for terminal concurrency
builder.Services.AddSingleton<AsyncBulkheadPolicy>(provider =>
{
    return Policy.BulkheadAsync(4, 100); // 4 concurrent operations, 100 queued
});

// Background service for processing requests
builder.Services.AddHostedService<RequestProcessorService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();

static IEnumerable<TerminalInfo> ParseTerminalData(List<string> terminalsData, string podName)
{
    var random = new Random(podName.GetHashCode()); // Deterministic based on pod name
    var shuffled = terminalsData.OrderBy(x => random.Next()).ToList();
    
    foreach (var terminalData in shuffled)
    {
        var parts = terminalData.Split('|');
        if (parts.Length == 6)
        {
            yield return new TerminalInfo
            {
                Host = parts[0],
                Port = int.Parse(parts[1]),
                Username = parts[2],
                Password = parts[3],
                TerminalId = parts[4],
                Branch = int.Parse(parts[5]),
                IsAvailable = true
            };
        }
    }
}
