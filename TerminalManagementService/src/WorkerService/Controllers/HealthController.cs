using Microsoft.AspNetCore.Mvc;
using TerminalManagement.Shared.Models;
using TerminalManagement.Shared.Services;

namespace WorkerService.Controllers;

[ApiController]
[Route("[controller]")]
public class HealthController : ControllerBase
{
    private readonly TerminalConfiguration _configuration;
    private readonly ITerminalPool _terminalPool;
    private readonly ILogger<HealthController> _logger;

    public HealthController(
        TerminalConfiguration configuration,
        ITerminalPool terminalPool,
        ILogger<HealthController> logger)
    {
        _configuration = configuration;
        _terminalPool = terminalPool;
        _logger = logger;
    }

    [HttpGet]
    public IActionResult Get()
    {
        return Ok(new 
        { 
            status = "healthy", 
            podName = _configuration.PodName,
            availableTerminals = _terminalPool.AvailableCount,
            timestamp = DateTime.UtcNow 
        });
    }
}
