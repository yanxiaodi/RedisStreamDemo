using StackExchange.Redis;
using TerminalManagement.Shared.Models;
using TerminalManagement.Shared.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// Configuration
var terminalConfig = builder.Configuration.GetSection("TerminalConfiguration").Get<TerminalConfiguration>()
    ?? throw new InvalidOperationException("TerminalConfiguration is required");

builder.Services.AddSingleton(terminalConfig);

// Redis configuration
builder.Services.AddSingleton<IConnectionMultiplexer>(provider =>
{
    var configuration = ConfigurationOptions.Parse(terminalConfig.RedisConnectionString);
    return ConnectionMultiplexer.Connect(configuration);
});

builder.Services.AddScoped<IRedisService, RedisService>();

var app = builder.Build();

// Initialize Redis streams on startup
using (var scope = app.Services.CreateScope())
{
    var redisService = scope.ServiceProvider.GetRequiredService<IRedisService>();
    var config = scope.ServiceProvider.GetRequiredService<TerminalConfiguration>();
    
    // Create response stream for this pod
    await redisService.CreateConsumerGroupAsync($"responses-{config.PodName}", config.PodName);
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
