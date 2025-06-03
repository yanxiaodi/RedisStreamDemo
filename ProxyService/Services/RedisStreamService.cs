using System.Text.Json;
using ProxyService.Models;
using StackExchange.Redis;

namespace ProxyService.Services;

public class RedisStreamService : BackgroundService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisStreamService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private const string RequestStreamKey = "weather-requests";
    private const string ResponseStreamKey = "weather-responses";
    private readonly string[] _weatherConditions = new[] 
    {
        "Sunny", "Partly Cloudy", "Cloudy", "Rainy", "Thunderstorm", 
        "Snowy", "Foggy", "Windy", "Clear", "Stormy"
    };
    private readonly Random _random = new Random();

    public RedisStreamService(
        IConnectionMultiplexer redis, 
        ILogger<RedisStreamService> logger,
        IServiceProvider serviceProvider)
    {
        _redis = redis;
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var db = _redis.GetDatabase();
        var lastId = "0-0"; // Start from the beginning
        
        _logger.LogInformation("Redis Stream Service started, waiting for requests...");
        
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Read messages from the request stream
                var results = await db.StreamReadAsync(RequestStreamKey, lastId, 5);
                
                if (results.Length > 0)
                {
                    foreach (var entry in results)
                    {
                        lastId = entry.Id;
                        
                        var values = entry.Values;
                        var requestId = values.FirstOrDefault(v => v.Name == "requestId").Value.ToString();
                        var data = values.FirstOrDefault(v => v.Name == "data").Value.ToString();
                        
                        if (string.IsNullOrEmpty(requestId) || string.IsNullOrEmpty(data))
                        {
                            continue;
                        }
                        
                        _logger.LogInformation("Processing request: {RequestId}", requestId);
                        
                        // Process the request asynchronously
                        _ = Task.Run(async () => 
                        {
                            try
                            {
                                var request = JsonSerializer.Deserialize<WeatherRequest>(data);
                                
                                if (request != null)
                                {
                                    // Set the request ID from the stream
                                    request.RequestId = requestId;
                                    
                                    // Generate a mock weather response
                                    var response = GenerateWeatherResponse(request);
                                    
                                    // Send the response back
                                    await SendResponseAsync(response);
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Error processing request {RequestId}", requestId);
                            }
                        }, stoppingToken);
                    }
                    
                    // Acknowledge the messages
                    await db.StreamDeleteAsync(RequestStreamKey, results.Select(m => m.Id).ToArray());
                }
                else
                {
                    // No messages, wait for a bit before trying again
                    await Task.Delay(100, stoppingToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading from Redis request stream");
                await Task.Delay(1000, stoppingToken); // Wait before trying again
            }
        }
    }
    
    private WeatherResponse GenerateWeatherResponse(WeatherRequest request)
    {
        // Create a mock weather response
        var response = new WeatherResponse
        {
            City = request.City,
            Location = request.Location,
            RequestId = request.RequestId,
            ForecastTime = DateTime.UtcNow,
            Temperature = _random.Next(-10, 40) + (float)_random.NextDouble(),
            Condition = _weatherConditions[_random.Next(_weatherConditions.Length)],
            Humidity = _random.Next(30, 100),
            WindSpeed = _random.Next(0, 30) + (float)_random.NextDouble()
        };
        
        // Simulate some processing time
        Thread.Sleep(_random.Next(100, 500));
        
        return response;
    }
    
    private async Task SendResponseAsync(WeatherResponse response)
    {
        try
        {
            var db = _redis.GetDatabase();
            
            // Serialize the response to JSON
            var json = JsonSerializer.Serialize(response);
            
            // Add the response to the Redis stream
            await db.StreamAddAsync(ResponseStreamKey, new NameValueEntry[]
            {
                new("requestId", response.RequestId),
                new("data", json)
            });
            
            _logger.LogInformation("Response for request {RequestId} sent to Redis stream", response.RequestId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending response to Redis stream");
        }
    }
}