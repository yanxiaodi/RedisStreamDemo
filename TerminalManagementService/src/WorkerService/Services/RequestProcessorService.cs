using Polly.Bulkhead;
using TerminalManagement.Shared.Models;
using TerminalManagement.Shared.Services;

namespace WorkerService.Services;

public class RequestProcessorService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<RequestProcessorService> _logger;
    private readonly TerminalConfiguration _configuration;
    private readonly string _consumerGroupName;
    private readonly string _consumerName;

    public RequestProcessorService(
        IServiceProvider serviceProvider,
        ILogger<RequestProcessorService> logger,
        TerminalConfiguration configuration)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _configuration = configuration;
        _consumerGroupName = "workers";
        _consumerName = $"worker-{configuration.PodName}-{Guid.NewGuid():N}";
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Request processor service starting for pod {PodName}", _configuration.PodName);

        // Initialize consumer group
        using var scope = _serviceProvider.CreateScope();
        var redisService = scope.ServiceProvider.GetRequiredService<IRedisService>();
        
        try
        {
            await redisService.CreateConsumerGroupAsync("requests-stream", _consumerGroupName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create consumer group");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessRequestsBatch(stoppingToken);
                await Task.Delay(100, stoppingToken); // Small delay between batches
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in request processing loop");
                await Task.Delay(5000, stoppingToken); // Longer delay on error
            }
        }

        _logger.LogInformation("Request processor service stopping");
    }

    private async Task ProcessRequestsBatch(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var redisService = scope.ServiceProvider.GetRequiredService<IRedisService>();
        var terminalPool = scope.ServiceProvider.GetRequiredService<ITerminalPool>();
        var bulkheadPolicy = scope.ServiceProvider.GetRequiredService<AsyncBulkheadPolicy>();

        try
        {
            var requests = await redisService.ReadRequestsAsync(_consumerGroupName, _consumerName, 4);
            
            var tasks = requests.Select(async request =>
            {
                await bulkheadPolicy.ExecuteAsync(async () =>
                {
                    await ProcessSingleRequest(request, redisService, terminalPool, cancellationToken);
                });
            });

            await Task.WhenAll(tasks);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing request batch");
        }
    }

    private async Task ProcessSingleRequest(
        TerminalRequest request, 
        IRedisService redisService, 
        ITerminalPool terminalPool, 
        CancellationToken cancellationToken)
    {
        TerminalInfo? terminal = null;
        
        try
        {
            _logger.LogDebug("Processing request {RequestId} of type {RequestType}", 
                request.RequestId, request.RequestType);

            // Acquire terminal from pool
            terminal = await terminalPool.AcquireTerminalAsync(cancellationToken);
            
            if (terminal == null)
            {
                _logger.LogWarning("No terminal available for request {RequestId}", request.RequestId);
                await SendErrorResponse(redisService, request, "No terminal available");
                return;
            }

            // Validate or create session
            if (!await terminalPool.ValidateSessionAsync(terminal))
            {
                _logger.LogDebug("Creating new session for terminal {TerminalId}", terminal.TerminalId);
                await terminalPool.CreateSessionAsync(terminal);
            }

            // Process the actual request
            var response = await ProcessTerminalRequest(terminal, request, cancellationToken);

            // Send response back
            await redisService.PublishResponseAsync(request.SourcePod, response);
            
            // Acknowledge the request
            await redisService.AcknowledgeRequestAsync(request.RequestId);

            _logger.LogDebug("Successfully processed request {RequestId}", request.RequestId);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Request processing cancelled for {RequestId}", request.RequestId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing request {RequestId}", request.RequestId);
            
            try
            {
                await SendErrorResponse(redisService, request, ex.Message);
            }
            catch (Exception responseEx)
            {
                _logger.LogError(responseEx, "Failed to send error response for request {RequestId}", 
                    request.RequestId);
            }
        }
        finally
        {
            // Always return terminal to pool
            if (terminal != null)
            {
                await terminalPool.ReleaseTerminalAsync(terminal);
            }
        }
    }

    private async Task<TerminalResponse> ProcessTerminalRequest(
        TerminalInfo terminal, 
        TerminalRequest request, 
        CancellationToken cancellationToken)
    {
        try
        {
            // Simulate terminal processing - replace with actual terminal API call
            using var httpClient = new HttpClient();
            
            // This is where you would make the actual call to the terminal system
            // var response = await httpClient.PostAsync($"{_configuration.Scheme}://{terminal.Host}:{terminal.Port}/api/execute", ...);
            
            // For demo purposes, simulate some processing time
            await Task.Delay(Random.Shared.Next(100, 1000), cancellationToken);
            
            var responseData = $"Processed by terminal {terminal.TerminalId} at {DateTime.UtcNow:O}";
            
            return new TerminalResponse
            {
                RequestId = request.RequestId,
                ResponseData = responseData,
                Success = true,
                Timestamp = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing request on terminal {TerminalId}", terminal.TerminalId);
            
            return new TerminalResponse
            {
                RequestId = request.RequestId,
                ResponseData = string.Empty,
                Success = false,
                ErrorMessage = ex.Message,
                Timestamp = DateTime.UtcNow
            };
        }
    }

    private async Task SendErrorResponse(IRedisService redisService, TerminalRequest request, string errorMessage)
    {
        var errorResponse = new TerminalResponse
        {
            RequestId = request.RequestId,
            ResponseData = string.Empty,
            Success = false,
            ErrorMessage = errorMessage,
            Timestamp = DateTime.UtcNow
        };

        await redisService.PublishResponseAsync(request.SourcePod, errorResponse);
    }
}
