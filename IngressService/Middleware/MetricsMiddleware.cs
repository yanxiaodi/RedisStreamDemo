using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.AspNetCore.Http.Extensions;

namespace IngressService.Middleware;

public class MetricsMiddleware
{
    private readonly RequestDelegate _next;
    private static readonly Meter _meter = new("IngressService");
    private static readonly Counter<long> _requestCounter = _meter.CreateCounter<long>("http_requests_total", "Number of HTTP requests processed");
    private static readonly Histogram<double> _requestDuration = _meter.CreateHistogram<double>("http_request_duration_seconds", "Duration of HTTP requests in seconds");

    public MetricsMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            await _next(context);
        }
        finally
        {
            stopwatch.Stop();
            
            // Record metrics only for non-metrics endpoints to avoid circular recording
            if (!context.Request.Path.StartsWithSegments("/metrics"))
            {
                var path = context.Request.Path.Value ?? "";
                var method = context.Request.Method;
                var statusCode = context.Response.StatusCode.ToString();
                
                _requestCounter.Add(1, new KeyValuePair<string, object?>("path", path),
                                       new KeyValuePair<string, object?>("method", method),
                                       new KeyValuePair<string, object?>("status", statusCode));
                
                _requestDuration.Record(stopwatch.Elapsed.TotalSeconds, 
                                        new KeyValuePair<string, object?>("path", path),
                                        new KeyValuePair<string, object?>("method", method),
                                        new KeyValuePair<string, object?>("status", statusCode));
            }
        }
    }
}

// Extension method to register the middleware
public static class MetricsMiddlewareExtensions
{
    public static IApplicationBuilder UseMetricsMiddleware(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<MetricsMiddleware>();
    }
}
