using System.Text.Json;
using IngressService.Models;
using StackExchange.Redis;

namespace IngressService.Services;

public class RedisStreamService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisStreamService> _logger;
    private const string StreamKey = "weather-requests";
    private const string ResponseStreamKey = "weather-responses";
    private readonly Dictionary<string, TaskCompletionSource<WeatherResponse>> _pendingRequests;

    public RedisStreamService(IConnectionMultiplexer redis, ILogger<RedisStreamService> logger)
    {
        _redis = redis;
        _logger = logger;
        _pendingRequests = new Dictionary<string, TaskCompletionSource<WeatherResponse>>();

        // Start listening for responses
        _ = StartResponseListening();
    }

    public async Task<WeatherResponse> SendRequestAsync(WeatherRequest request)
    {
        var db = _redis.GetDatabase();
        var requestId = request.RequestId;

        var tcs = new TaskCompletionSource<WeatherResponse>();

        // Store the TaskCompletionSource for this request
        lock (_pendingRequests)
        {
            _pendingRequests[requestId] = tcs;
        }

        try
        {
            // Serialize the request to JSON
            var json = JsonSerializer.Serialize(request);

            // Add the request to the Redis stream
            await db.StreamAddAsync(StreamKey, new NameValueEntry[]
            {
                new("requestId", requestId),
                new("data", json)
            });

            _logger.LogInformation("Request {RequestId} sent to Redis stream", requestId);

            // Wait for the response with a timeout
            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(30));
            var completedTask = await Task.WhenAny(tcs.Task, timeoutTask);

            if (completedTask == timeoutTask)
            {
                throw new TimeoutException("Request timed out waiting for response");
            }

            return await tcs.Task;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending request to Redis stream");

            // Clean up the pending request
            lock (_pendingRequests)
            {
                _pendingRequests.Remove(requestId);
            }

            throw;
        }
    }

    private async Task StartResponseListening()
    {
        var db = _redis.GetDatabase();
        var lastId = "0-0"; // Start from the beginning

        while (!_redis.IsConnected)
        {
            await Task.Delay(1000);
        }

        while (true)
        {
            try
            {
                // Read messages from the response stream
                var results = await db.StreamReadAsync(ResponseStreamKey, lastId, 100);

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
                                TaskCompletionSource<WeatherResponse>? tcs = null;

                                // Try to get the TaskCompletionSource for this request
                                lock (_pendingRequests)
                                {
                                    if (_pendingRequests.TryGetValue(requestId, out tcs))
                                    {
                                        _pendingRequests.Remove(requestId);
                                    }
                                }

                                // Complete the task with the response
                                tcs?.SetResult(response);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error deserializing response from Redis stream");
                        }
                    }

                    // Acknowledge the messages (optional, can be commented out if you want to keep them)
                    await db.StreamDeleteAsync(ResponseStreamKey, results.Select(m => m.Id).ToArray());
                }
                else
                {
                    // No messages, wait for a bit before trying again
                    await Task.Delay(100);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading from Redis response stream");
                await Task.Delay(1000); // Wait before trying again
            }
        }
    }
}
