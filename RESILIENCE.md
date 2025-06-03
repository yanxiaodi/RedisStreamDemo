# Resilience Patterns Implementation

This document describes the resilience patterns implemented in the Redis Stream Demo application to improve reliability, fault tolerance, and overall system stability.

## Overview

The application now implements several resilience patterns to handle failures and temporary outages gracefully:

1. **Retry Pattern** - Automatically retries failed operations with exponential backoff
2. **Circuit Breaker Pattern** - Prevents cascading failures when Redis is unavailable
3. **Graceful Shutdown** - Ensures in-flight requests are completed before termination
4. **Enhanced Health Checks** - Provides visibility into system health and circuit state
5. **Metrics Tracking** - Offers real-time visibility into queue depths and performance

## Implementation Details

### Retry Pattern

We use Polly's retry policy to automatically retry failed Redis operations:

```csharp
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
```

Key aspects:
- Handles Redis-specific exceptions
- Uses exponential backoff to avoid overwhelming the system
- Retries up to 3 times before giving up
- Logs detailed information about retry attempts

### Circuit Breaker Pattern

The circuit breaker prevents cascading failures by temporarily stopping attempts to perform operations that are likely to fail:

```csharp
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
```

Key aspects:
- Opens after 5 consecutive failures
- Stays open for 30 seconds before trying again
- Provides events for monitoring circuit state changes
- Prevents overwhelming Redis with requests when it's down

### Graceful Shutdown

The application now handles shutdown signals properly:

```csharp
// Register for application stopping event to ensure graceful shutdown
stoppingToken.Register(() => 
{
    _logger.LogInformation("Service stopping. Beginning graceful shutdown.");
    // Allow time for in-flight requests to complete
    Task.Delay(TimeSpan.FromSeconds(5)).Wait();
});
```

Key aspects:
- Detects application shutdown signals
- Allows in-flight requests to complete
- Provides logging for shutdown state

### Enhanced Health Checks

Health checks now monitor not just connectivity but also the circuit breaker state:

```csharp
// Check Redis connection status from the service
bool isHealthy = _redisStreamService.IsHealthy();

if (!isHealthy)
{
    _logger.LogWarning("Redis circuit breaker is open");
    return StatusCode(503, new { status = "Service Unavailable", message = "Redis circuit breaker is open" });
}
```

Key aspects:
- Exposes circuit breaker state
- Returns appropriate HTTP status codes
- Provides detailed health information

### Queue Depth Metrics

We track queue depth for monitoring and scaling purposes:

```csharp
// Add queue depth metric
sb.AppendLine("# HELP redis_stream_queue_depth Current depth of the Redis stream queue");
sb.AppendLine("# TYPE redis_stream_queue_depth gauge");
sb.AppendLine($"redis_stream_queue_depth {_redisStreamService.GetQueueDepth()}");
```

Key aspects:
- Provides Prometheus-compatible metrics
- Tracks active queue size in real-time
- Can be used for alerting and autoscaling

## Best Practices

1. **Don't over-retry** - Our implementation limits retries to avoid overwhelming Redis
2. **Use appropriate timeouts** - We use reasonable timeouts to prevent blocking threads
3. **Log circuit breaker events** - All state changes are logged for observability
4. **Expose health metrics** - Health and metrics endpoints provide visibility
5. **Graceful degradation** - When Redis is unavailable, the system fails gracefully

## Future Enhancements

1. **Fallback Pattern** - Implement fallback mechanisms for critical operations
2. **Bulkhead Pattern** - Isolate components to contain failures
3. **Timeout Pattern** - Add explicit timeouts for all external calls
4. **Cache Aside Pattern** - Cache frequently accessed data locally

## References

- [Polly Documentation](https://github.com/App-vNext/Polly)
- [Circuit Breaker Pattern](https://docs.microsoft.com/en-us/azure/architecture/patterns/circuit-breaker)
- [Retry Pattern](https://docs.microsoft.com/en-us/azure/architecture/patterns/retry)
