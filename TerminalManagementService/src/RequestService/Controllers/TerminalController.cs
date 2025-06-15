using Microsoft.AspNetCore.Mvc;
using TerminalManagement.Shared.Models;
using TerminalManagement.Shared.Services;

namespace RequestService.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TerminalController : ControllerBase
{
    private readonly IRedisService _redisService;
    private readonly TerminalConfiguration _configuration;
    private readonly ILogger<TerminalController> _logger;

    public TerminalController(
        IRedisService redisService,
        TerminalConfiguration configuration,
        ILogger<TerminalController> logger)
    {
        _redisService = redisService;
        _configuration = configuration;
        _logger = logger;
    }

    [HttpPost("execute")]
    public async Task<IActionResult> ExecuteRequest([FromBody] TerminalRequestDto requestDto)
    {
        try
        {
            // Create unique request ID
            var requestId = Guid.NewGuid().ToString("N");
            
            var terminalRequest = new TerminalRequest
            {
                RequestId = requestId,
                RequestData = requestDto.Data,
                RequestType = requestDto.Type,
                Timestamp = DateTime.UtcNow,
                SourcePod = _configuration.PodName
            };

            _logger.LogInformation("Processing request {RequestId} of type {RequestType}", 
                requestId, requestDto.Type);

            // Publish request to Redis stream
            await _redisService.PublishRequestAsync(terminalRequest);

            // Wait for response with timeout
            var timeout = TimeSpan.FromSeconds(requestDto.TimeoutSeconds ?? 30);
            var response = await _redisService.WaitForResponseAsync(requestId, _configuration.PodName, timeout);

            if (response == null)
            {
                _logger.LogWarning("Timeout waiting for response to request {RequestId}", requestId);
                return StatusCode(408, new { error = "Request timeout", requestId });
            }

            if (!response.Success)
            {
                _logger.LogWarning("Request {RequestId} failed: {ErrorMessage}", requestId, response.ErrorMessage);
                return BadRequest(new { error = response.ErrorMessage, requestId });
            }

            _logger.LogInformation("Request {RequestId} completed successfully", requestId);
            return Ok(new 
            { 
                requestId, 
                data = response.ResponseData, 
                timestamp = response.Timestamp 
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing terminal request");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    [HttpGet("health")]
    public IActionResult Health()
    {
        return Ok(new 
        { 
            status = "healthy", 
            podName = _configuration.PodName,
            timestamp = DateTime.UtcNow 
        });
    }
}

public class TerminalRequestDto
{
    public string Data { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public int? TimeoutSeconds { get; set; }
}
