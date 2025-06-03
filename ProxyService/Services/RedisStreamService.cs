using System.Text.Json;
using System.Threading.Channels;
using System.Collections.Concurrent;
using ProxyService.Models;
using StackExchange.Redis;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Polly.Wrap;

namespace ProxyService.Services;

public class RedisStreamService : BackgroundService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisStreamService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private const string RequestStreamKey = "weather-requests";
    private const string ResponseStreamKey = "weather-responses";
    private const int MaxConcurrentRequests = 5; // Maximum number of terminals per instance
    private readonly Channel<(string RequestId, string Data)> _requestChannel;
    private readonly ConcurrentDictionary<int, bool> _activeTerminals = new();
    private readonly string[] _weatherConditions = new[]
    {
        "Sunny", "Partly Cloudy", "Cloudy", "Rainy", "Thunderstorm",
        "Snowy", "Foggy", "Windy", "Clear", "Stormy"
    };
    private readonly Random _random = new Random();
    
    // Resilience policies
    private readonly AsyncRetryPolicy _retryPolicy;
    private readonly AsyncCircuitBreakerPolicy _circuitBreakerPolicy;
    private readonly AsyncPolicyWrap _resiliencePolicy;

    // Queue depth metric
    private int _queueDepth = 0;
    
    public RedisStreamService(
        IConnectionMultiplexer redis,
        ILogger<RedisStreamService> logger,
        IServiceProvider serviceProvider)
    {
        _redis = redis;
        _logger = logger;
        _serviceProvider = serviceProvider;
        
        // Create a bounded channel with capacity for MaxConcurrentRequests
        _requestChannel = Channel.CreateBounded<(string RequestId, string Data)>(
            new BoundedChannelOptions(MaxConcurrentRequests)
            {
                FullMode = BoundedChannelFullMode.Wait // Block when full instead of throwing
            });
            
        // Configure retry policy
        _retryPolicy = Policy
            .Handle<RedisConnectionException>()
            .Or<RedisTimeoutException>()
            .Or<RedisServerException>()
            .WaitAndRetryAsync(
                retryCount: 3,
                sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                onRetry: (exception, timeSpan, retryCount, context) =>
                {
                    _logger.LogWarning(
                        exception,
                        "Redis operation failed. Retrying in {RetryTimeSpan}s. Attempt {RetryCount} of 3.",
                        timeSpan.TotalSeconds,
                        retryCount);
                });
                
        // Configure circuit breaker policy
        _circuitBreakerPolicy = Policy
            .Handle<RedisConnectionException>()
            .Or<RedisTimeoutException>()
            .Or<RedisServerException>()
            .CircuitBreakerAsync(
                exceptionsAllowedBeforeBreaking: 5,
                durationOfBreak: TimeSpan.FromSeconds(30),
                onBreak: (exception, timespan) =>
                {
                    _logger.LogError(
                        exception,
                        "Circuit breaker opened for Redis operations. Breaking for {DurationOfBreak}s.",
                        timespan.TotalSeconds);
                },
                onReset: () =>
                {
                    _logger.LogInformation("Circuit breaker reset. Redis operations resuming.");
                },
                onHalfOpen: () =>
                {
                    _logger.LogInformation("Circuit breaker half-open. Testing Redis connection.");
                });
                
        // Combine policies
        _resiliencePolicy = _retryPolicy.WrapAsync(_circuitBreakerPolicy);
    }
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Redis Stream Service started, waiting for requests...");

        // Start the consumer tasks (one for each terminal)
        var consumerTasks = new List<Task>();
        for (int i = 0; i < MaxConcurrentRequests; i++)
        {
            var terminalId = i + 1;
            consumerTasks.Add(ProcessRequestsAsync(terminalId, stoppingToken));
        }

        // Producer task: Read from Redis stream and write to channel
        var producerTask = ReadFromRedisStreamAsync(stoppingToken);

        // Set up a cancellation token that will be triggered on shutdown
        using var shutdownCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        
        // Register for application stopping event to ensure graceful shutdown
        stoppingToken.Register(() => 
        {
            _logger.LogInformation("Service stopping. Beginning graceful shutdown.");
            // Allow time for in-flight requests to complete
            Task.Delay(TimeSpan.FromSeconds(5)).Wait();
        });

        // Wait for any task to complete (or fail)
        await Task.WhenAny(producerTask, Task.WhenAny(consumerTasks));

        // If we get here, something went wrong or we're shutting down
        _requestChannel.Writer.TryComplete();

        // Wait for all tasks to complete
        await Task.WhenAll(consumerTasks.Append(producerTask).ToArray());
    }

    private async Task ReadFromRedisStreamAsync(CancellationToken stoppingToken)
    {
        var db = _redis.GetDatabase();
        var lastId = "0-0"; // Start from the beginning
        
        // Health check flag
        var isHealthy = true;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Execute with resilience policy
                await _resiliencePolicy.ExecuteAsync(async () =>
                {
                    // Read messages from the request stream
                    var results = await db.StreamReadAsync(RequestStreamKey, lastId, 5);
                    
                    // If we get here without errors, the system is healthy
                    if (!isHealthy)
                    {
                        isHealthy = true;
                        _logger.LogInformation("Redis connection restored.");
                    }

                    if (results.Length > 0)
                    {
                        // Update queue depth metric
                        _queueDepth = results.Length;
                        
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

                            _logger.LogInformation("Received request: {RequestId}, adding to processing queue", requestId);

                            // Write to the channel (will block if channel is full)
                            await _requestChannel.Writer.WriteAsync((requestId, data), stoppingToken);
                        }

                        // Acknowledge the messages with resilience
                        await _retryPolicy.ExecuteAsync(async () => 
                        {
                            await db.StreamDeleteAsync(RequestStreamKey, results.Select(m => m.Id).ToArray());
                        });
                    }
                    else
                    {
                        // Reset queue depth metric when empty
                        _queueDepth = 0;
                        
                        // No messages, wait for a bit before trying again
                        await Task.Delay(100, stoppingToken);
                    }
                });
            }
            catch (BrokenCircuitException ex)
            {
                // The circuit is broken, just log and wait
                if (isHealthy)
                {
                    isHealthy = false;
                    _logger.LogError(ex, "Circuit broken for Redis operations. Waiting for circuit to recover.");
                }
                await Task.Delay(1000, stoppingToken);
            }
            catch (Exception ex)
            {
                // Log error and wait before trying again
                if (isHealthy)
                {
                    isHealthy = false;
                    _logger.LogError(ex, "Error reading from Redis request stream");
                }
                await Task.Delay(1000, stoppingToken);
            }
        }

        // Complete the channel when done
        _requestChannel.Writer.Complete();
    }

    private async Task ProcessRequestsAsync(int terminalId, CancellationToken stoppingToken)
    {
        _logger.LogInformation("Terminal {TerminalId} started", terminalId);
        
        // Mark this terminal as active
        _activeTerminals[terminalId] = true;

        await foreach (var (requestId, data) in _requestChannel.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                _logger.LogInformation("Terminal {TerminalId} processing request: {RequestId}", terminalId, requestId);

                var request = JsonSerializer.Deserialize<WeatherRequest>(data);

                if (request != null)
                {
                    // Set the request ID from the stream
                    request.RequestId = requestId;

                    // Generate a mock weather response
                    var response = GenerateWeatherResponse(request);

                    // Send the response back with resilience
                    await SendResponseAsync(response);

                    _logger.LogInformation("Terminal {TerminalId} completed request: {RequestId}", terminalId, requestId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Terminal {TerminalId} error processing request {RequestId}", terminalId, requestId);
            }
        }

        _logger.LogInformation("Terminal {TerminalId} shutting down", terminalId);
        
        // Mark this terminal as inactive
        _activeTerminals.TryRemove(terminalId, out _);
    }
    
    // Method to get the number of active terminals
    public int GetActiveTerminalCount()
    {
        return _activeTerminals.Count;
    }
    
    // Method to get the current queue depth
    public int GetQueueDepth()
    {
        return _queueDepth;
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
            // Execute with resilience policy
            await _resiliencePolicy.ExecuteAsync(async () =>
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
            });
        }
        catch (BrokenCircuitException ex)
        {
            _logger.LogError(ex, "Circuit broken. Unable to send response for request {RequestId}", response.RequestId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending response to Redis stream for request {RequestId}", response.RequestId);
        }
    }
}