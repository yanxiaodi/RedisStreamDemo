using Microsoft.AspNetCore.Mvc;
using StackExchange.Redis;
using IngressService.Services;

namespace IngressService.Controllers;

[ApiController]
[Route("[controller]")]
public class HealthController : ControllerBase
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<HealthController> _logger;
    private readonly RedisStreamService _redisStreamService;

    public HealthController(
        IConnectionMultiplexer redis, 
        ILogger<HealthController> logger,
        RedisStreamService redisStreamService)
    {
        _redis = redis;
        _logger = logger;
        _redisStreamService = redisStreamService;
    }    [HttpGet("/healthz")]
    public async Task<IActionResult> CheckHealth()
    {
        try
        {
            // Check Redis connection status from the service
            bool isHealthy = _redisStreamService.IsHealthy();
            
            if (!isHealthy)
            {
                _logger.LogWarning("Redis circuit breaker is open");
                return StatusCode(503, new { status = "Service Unavailable", message = "Redis circuit breaker is open" });
            }
            
            // Check Redis connection
            if (!_redis.IsConnected)
            {
                _logger.LogWarning("Redis connection is not established");
                return StatusCode(503, new { status = "Service Unavailable", message = "Redis connection is not established" });
            }

            // Ping Redis to ensure it's responsive
            var db = _redis.GetDatabase();
            var pingResult = await db.PingAsync();
            
            if (pingResult.TotalMilliseconds > 500)
            {
                _logger.LogWarning("Redis ping response time is high: {PingTime}ms", pingResult.TotalMilliseconds);
            }

            return Ok(new 
            { 
                status = "Healthy", 
                service = "IngressService", 
                timestamp = DateTime.UtcNow,
                redisResponseTime = $"{pingResult.TotalMilliseconds}ms",
                pendingRequests = _redisStreamService.GetPendingRequestCount()
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Health check failed");
            return StatusCode(503, new { status = "Service Unavailable", message = ex.Message });
        }
    }
}
