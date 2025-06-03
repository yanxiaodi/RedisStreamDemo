using IngressService.Models;
using IngressService.Services;
using Microsoft.AspNetCore.Mvc;

namespace IngressService.Controllers;

[ApiController]
[Route("[controller]")]
public class WeatherForecastController : ControllerBase
{
    private readonly RedisStreamService _redisStreamService;
    private readonly ILogger<WeatherForecastController> _logger;

    public WeatherForecastController(RedisStreamService redisStreamService, ILogger<WeatherForecastController> logger)
    {
        _redisStreamService = redisStreamService;
        _logger = logger;
    }    [HttpGet(Name = "GetWeatherForecast")]
    public IEnumerable<WeatherForecast> Get()
    {
        var summaries = new[]
        {
            "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
        };
        
        return Enumerable.Range(1, 5).Select(index => new WeatherForecast
        (
            DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            Random.Shared.Next(-20, 55),
            summaries[Random.Shared.Next(summaries.Length)]
        ))
        .ToArray();
    }
    
    [HttpPost("city")]
    public async Task<ActionResult<WeatherResponse>> GetWeatherForCity([FromBody] WeatherRequest request)
    {
        try
        {
            _logger.LogInformation("Received weather request for city: {City}, location: {Location}", 
                request.City, request.Location);
            
            // Send the request to Redis stream and wait for the response
            var response = await _redisStreamService.SendRequestAsync(request);
            
            return Ok(response);
        }
        catch (TimeoutException)
        {
            return StatusCode(504, "Request timed out waiting for response from weather service");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing weather request");
            return StatusCode(500, "An error occurred while processing your request");
        }
    }
}
