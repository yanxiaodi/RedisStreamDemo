using Microsoft.AspNetCore.Mvc;
using ProxyService.Services;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text;

namespace ProxyService.Controllers;

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
        
        // Add proxy service specific metrics
        sb.AppendLine("# HELP proxy_active_terminals Number of active terminals processing requests");
        sb.AppendLine("# TYPE proxy_active_terminals gauge");
        sb.AppendLine($"proxy_active_terminals {_redisStreamService.GetActiveTerminalCount()}");
        
        // Add queue depth metric
        sb.AppendLine("# HELP redis_stream_queue_depth Current depth of the Redis stream queue");
        sb.AppendLine("# TYPE redis_stream_queue_depth gauge");
        sb.AppendLine($"redis_stream_queue_depth {_redisStreamService.GetQueueDepth()}");
        
        return Content(sb.ToString(), "text/plain");
    }
}
