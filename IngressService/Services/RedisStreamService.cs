using System.Text.Json;
using System.Collections.Concurrent;
using IngressService.Models;
using StackExchange.Redis;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Polly.Wrap;

namespace IngressService.Services;

public class RedisStreamService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisStreamService> _logger;
    private const string StreamKey = "weather-requests";
    private const string ResponseStreamKey = "weather-responses";
    private readonly ConcurrentDictionary<string, TaskCompletionSource<WeatherResponse>> _pendingRequests;
    
    // Resilience policies
    private readonly AsyncRetryPolicy _retryPolicy;
    private readonly AsyncCircuitBreakerPolicy _circuitBreakerPolicy;
    private readonly AsyncPolicyWrap _resiliencePolicy;
    
    // Health check flag
    private bool _isHealthy = true;
    
    // Metrics
    private int _pendingRequestCount = 0;
    
    public RedisStreamService(IConnectionMultiplexer redis, ILogger<RedisStreamService> logger)
    {
        _redis = redis;
        _logger = logger;
        _pendingRequests = new ConcurrentDictionary<string, TaskCompletionSource<WeatherResponse>>();
        
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
                    _isHealthy = false;
                    _logger.LogError(
                        exception,
                        "Circuit breaker opened for Redis operations. Breaking for {DurationOfBreak}s.",
                        timespan.TotalSeconds);
                },
                onReset: () =>
                {
                    _isHealthy = true;
                    _logger.LogInformation("Circuit breaker reset. Redis operations resuming.");
                },
                onHalfOpen: () =>
                {
                    _logger.LogInformation("Circuit breaker half-open. Testing Redis connection.");
                });
                
        // Combine policies
        _resiliencePolicy = _retryPolicy.WrapAsync(_circuitBreakerPolicy);

        // Start listening for responses
        _ = StartResponseListening();
    }
    
    // Health check method
    public bool IsHealthy() => _isHealthy;
    
    // Metrics method
    public int GetPendingRequestCount() => _pendingRequestCount;
    
    public async Task<WeatherResponse> SendRequestAsync(WeatherRequest request)
    {
        var requestId = request.RequestId;
        var tcs = new TaskCompletionSource<WeatherResponse>();

        // Store the TaskCompletionSource for this request
        _pendingRequests[requestId] = tcs;
        Interlocked.Increment(ref _pendingRequestCount);

        try
        {
            // Execute with resilience policy
            await _resiliencePolicy.ExecuteAsync(async () =>
            {
                var db = _redis.GetDatabase();
                
                // Serialize the request to JSON
                var json = JsonSerializer.Serialize(request);

                // Add the request to the Redis stream
                await db.StreamAddAsync(StreamKey, new NameValueEntry[]
                {
                    new("requestId", requestId),
                    new("data", json)
                });

                _logger.LogInformation("Request {RequestId} sent to Redis stream", requestId);
            });

            // Wait for the response with a timeout
            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(30));
            var completedTask = await Task.WhenAny(tcs.Task, timeoutTask);

            if (completedTask == timeoutTask)
            {
                throw new TimeoutException("Request timed out waiting for response");
            }

            return await tcs.Task;
        }
        catch (BrokenCircuitException ex)
        {
            _logger.LogError(ex, "Circuit broken. Unable to send request {RequestId}", requestId);
            
            // Clean up the pending request
            if (_pendingRequests.TryRemove(requestId, out _))
            {
                Interlocked.Decrement(ref _pendingRequestCount);
            }
            
            throw new ApplicationException("Service is currently unavailable due to connection issues with Redis.", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending request {RequestId} to Redis stream", requestId);

            // Clean up the pending request
            if (_pendingRequests.TryRemove(requestId, out _))
            {
                Interlocked.Decrement(ref _pendingRequestCount);
            }

            throw;
        }
    }

    private async Task StartResponseListening()
    {
        var db = _redis.GetDatabase();
        var lastId = "0-0"; // Start from the beginning

        // Wait for Redis connection
        while (!_redis.IsConnected)
        {
            await Task.Delay(1000);
        }

        while (true)
        {
            try
            {
                // Execute with resilience policy
                await _resiliencePolicy.ExecuteAsync(async () =>
                {
                    // Read messages from the response stream
                    var results = await db.StreamReadAsync(ResponseStreamKey, lastId, 100);
                    
                    // If we get here without errors, the system is healthy
                    if (!_isHealthy)
                    {
                        _isHealthy = true;
                        _logger.LogInformation("Redis connection restored.");
                    }

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

                            try
                            {
                                var response = JsonSerializer.Deserialize<WeatherResponse>(data);

                                if (response != null)
                                {
                                    // Try to get and remove the TaskCompletionSource for this request
                                    if (_pendingRequests.TryRemove(requestId, out var tcs))
                                    {
                                        // Decrement pending request count
                                        Interlocked.Decrement(ref _pendingRequestCount);
                                        
                                        // Complete the task with the response
                                        tcs.SetResult(response);
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Error deserializing response from Redis stream for request {RequestId}", requestId);
                            }
                        }

                        // Acknowledge the messages with resilience policy
                        await _retryPolicy.ExecuteAsync(async () => 
                        {
                            await db.StreamDeleteAsync(ResponseStreamKey, results.Select(m => m.Id).ToArray());
                        });
                    }
                    else
                    {
                        // No messages, wait for a bit before trying again
                        await Task.Delay(100);
                    }
                });
            }
            catch (BrokenCircuitException ex)
            {
                // The circuit is broken, just log and wait
                if (_isHealthy)
                {
                    _isHealthy = false;
                    _logger.LogError(ex, "Circuit broken for Redis operations. Waiting for circuit to recover.");
                }
                await Task.Delay(1000);
            }
            catch (Exception ex)
            {
                if (_isHealthy)
                {
                    _isHealthy = false;
                    _logger.LogError(ex, "Error reading from Redis response stream");
                }
                await Task.Delay(1000); // Wait before trying again
            }
        }
    }
}
