using Microsoft.AspNetCore.Mvc;
using IngressService.Services;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text;

namespace IngressService.Controllers;

[Route("[controller]")]
[ApiController]
public class MetricsController : ControllerBase
{
    private readonly ILogger<MetricsController> _logger;
    private readonly RedisStreamService _redisStreamService;

    public MetricsController(ILogger<MetricsController> logger, RedisStreamService redisStreamService)
    {
        _logger = logger;
        _redisStreamService = redisStreamService;
    }

    [HttpGet("/metrics")]
    public IActionResult GetMetrics()
    {
        var sb = new StringBuilder();
        
        // Add basic runtime metrics
        sb.AppendLine("# HELP process_cpu_seconds_total Total user and system CPU time spent in seconds");
        sb.AppendLine("# TYPE process_cpu_seconds_total counter");
        sb.AppendLine($"process_cpu_seconds_total {Process.GetCurrentProcess().TotalProcessorTime.TotalSeconds}");
        
        sb.AppendLine("# HELP process_working_set_bytes Process working set");
        sb.AppendLine("# TYPE process_working_set_bytes gauge");
        sb.AppendLine($"process_working_set_bytes {Process.GetCurrentProcess().WorkingSet64}");
        
        sb.AppendLine("# HELP process_num_threads Total number of threads");
        sb.AppendLine("# TYPE process_num_threads gauge");
        sb.AppendLine($"process_num_threads {Process.GetCurrentProcess().Threads.Count}");
        
        sb.AppendLine("# HELP dotnet_gc_collections_total Number of garbage collections");
        sb.AppendLine("# TYPE dotnet_gc_collections_total counter");
        sb.AppendLine($"dotnet_gc_collections_total{{generation=\"0\"}} {GC.CollectionCount(0)}");        sb.AppendLine($"dotnet_gc_collections_total{{generation=\"1\"}} {GC.CollectionCount(1)}");
        sb.AppendLine($"dotnet_gc_collections_total{{generation=\"2\"}} {GC.CollectionCount(2)}");
        
        // Add ingress service specific metrics
        sb.AppendLine("# HELP ingress_pending_requests Number of pending requests waiting for response");
        sb.AppendLine("# TYPE ingress_pending_requests gauge");
        sb.AppendLine($"ingress_pending_requests {_redisStreamService.GetPendingRequestCount()}");
        
        // Add circuit state metric
        sb.AppendLine("# HELP redis_circuit_state State of the Redis circuit breaker (1=healthy, 0=open)");
        sb.AppendLine("# TYPE redis_circuit_state gauge");
        sb.AppendLine($"redis_circuit_state {(_redisStreamService.IsHealthy() ? 1 : 0)}");
        
        return Content(sb.ToString(), "text/plain");
    }
}
