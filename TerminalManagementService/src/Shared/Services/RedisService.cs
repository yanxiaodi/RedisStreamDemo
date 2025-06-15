using StackExchange.Redis;
using System.Text.Json;
using TerminalManagement.Shared.Models;

namespace TerminalManagement.Shared.Services;

public interface IRedisService
{
    Task<string> PublishRequestAsync(TerminalRequest request);
    Task<TerminalResponse?> WaitForResponseAsync(string requestId, string podName, TimeSpan timeout);
    Task PublishResponseAsync(string podName, TerminalResponse response);
    Task AcknowledgeRequestAsync(string requestId);
    Task<IEnumerable<TerminalRequest>> ReadRequestsAsync(string consumerGroup, string consumerName, int count = 1);
    Task CreateConsumerGroupAsync(string stream, string groupName);
}

public class RedisService : IRedisService, IDisposable
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IDatabase _database;
    private readonly ILogger<RedisService> _logger;
    private const string RequestsStream = "requests-stream";
    private const string ResponsesStreamPrefix = "responses-";

    public RedisService(IConnectionMultiplexer redis, ILogger<RedisService> logger)
    {
        _redis = redis;
        _database = redis.GetDatabase();
        _logger = logger;
    }

    public async Task<string> PublishRequestAsync(TerminalRequest request)
    {
        try
        {
            var fields = new NameValueEntry[]
            {
                new("requestId", request.RequestId),
                new("requestData", request.RequestData),
                new("requestType", request.RequestType),
                new("timestamp", request.Timestamp.ToString("O")),
                new("sourcePod", request.SourcePod)
            };

            var messageId = await _database.StreamAddAsync(RequestsStream, fields);
            _logger.LogDebug("Published request {RequestId} to stream with message ID {MessageId}", 
                request.RequestId, messageId);
            
            return messageId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish request {RequestId} to Redis stream", request.RequestId);
            throw;
        }
    }

    public async Task<TerminalResponse?> WaitForResponseAsync(string requestId, string podName, TimeSpan timeout)
    {
        var responseStream = $"{ResponsesStreamPrefix}{podName}";
        var cts = new CancellationTokenSource(timeout);
        
        try
        {
            // Create consumer group if it doesn't exist
            await CreateConsumerGroupAsync(responseStream, podName);
            
            while (!cts.Token.IsCancellationRequested)
            {
                var results = await _database.StreamReadGroupAsync(
                    responseStream, 
                    podName, 
                    podName, 
                    ">", 
                    1,
                    noAck: false);

                foreach (var result in results)
                {
                    var response = ParseResponse(result);
                    if (response?.RequestId == requestId)
                    {
                        // Acknowledge the message
                        await _database.StreamAcknowledgeAsync(responseStream, podName, result.Id);
                        return response;
                    }
                }

                await Task.Delay(100, cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Timeout waiting for response to request {RequestId}", requestId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error waiting for response to request {RequestId}", requestId);
        }

        return null;
    }

    public async Task PublishResponseAsync(string podName, TerminalResponse response)
    {
        try
        {
            var responseStream = $"{ResponsesStreamPrefix}{podName}";
            var fields = new NameValueEntry[]
            {
                new("requestId", response.RequestId),
                new("responseData", response.ResponseData),
                new("success", response.Success.ToString()),
                new("errorMessage", response.ErrorMessage ?? string.Empty),
                new("timestamp", response.Timestamp.ToString("O"))
            };

            var messageId = await _database.StreamAddAsync(responseStream, fields);
            _logger.LogDebug("Published response for request {RequestId} to stream {Stream} with message ID {MessageId}", 
                response.RequestId, responseStream, messageId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish response for request {RequestId}", response.RequestId);
            throw;
        }
    }

    public async Task AcknowledgeRequestAsync(string requestId)
    {
        try
        {
            // Implementation would depend on how you track message IDs
            // This is a simplified version
            _logger.LogDebug("Acknowledged request {RequestId}", requestId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to acknowledge request {RequestId}", requestId);
            throw;
        }
    }

    public async Task<IEnumerable<TerminalRequest>> ReadRequestsAsync(string consumerGroup, string consumerName, int count = 1)
    {
        try
        {
            var results = await _database.StreamReadGroupAsync(
                RequestsStream, 
                consumerGroup, 
                consumerName, 
                ">", 
                count,
                noAck: false);

            var requests = new List<TerminalRequest>();
            
            foreach (var result in results)
            {
                var request = ParseRequest(result);
                if (request != null)
                {
                    requests.Add(request);
                }
            }

            return requests;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read requests from Redis stream");
            throw;
        }
    }

    public async Task CreateConsumerGroupAsync(string stream, string groupName)
    {
        try
        {
            await _database.StreamCreateConsumerGroupAsync(stream, groupName, "0", createStream: true);
        }
        catch (RedisServerException ex) when (ex.Message.Contains("BUSYGROUP"))
        {
            // Consumer group already exists, ignore
            _logger.LogDebug("Consumer group {GroupName} already exists for stream {Stream}", groupName, stream);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create consumer group {GroupName} for stream {Stream}", groupName, stream);
            throw;
        }
    }

    private TerminalRequest? ParseRequest(StreamEntry entry)
    {
        try
        {
            var fields = entry.Values.ToDictionary(kv => kv.Name.ToString(), kv => kv.Value.ToString());
            
            return new TerminalRequest
            {
                RequestId = fields.GetValueOrDefault("requestId", string.Empty),
                RequestData = fields.GetValueOrDefault("requestData", string.Empty),
                RequestType = fields.GetValueOrDefault("requestType", string.Empty),
                Timestamp = DateTime.Parse(fields.GetValueOrDefault("timestamp", DateTime.UtcNow.ToString("O"))),
                SourcePod = fields.GetValueOrDefault("sourcePod", string.Empty)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse request from stream entry");
            return null;
        }
    }

    private TerminalResponse? ParseResponse(StreamEntry entry)
    {
        try
        {
            var fields = entry.Values.ToDictionary(kv => kv.Name.ToString(), kv => kv.Value.ToString());
            
            return new TerminalResponse
            {
                RequestId = fields.GetValueOrDefault("requestId", string.Empty),
                ResponseData = fields.GetValueOrDefault("responseData", string.Empty),
                Success = bool.Parse(fields.GetValueOrDefault("success", "false")),
                ErrorMessage = fields.GetValueOrDefault("errorMessage"),
                Timestamp = DateTime.Parse(fields.GetValueOrDefault("timestamp", DateTime.UtcNow.ToString("O")))
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse response from stream entry");
            return null;
        }
    }

    public void Dispose()
    {
        _redis?.Dispose();
    }
}
