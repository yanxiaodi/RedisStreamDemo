using System.Text.Json.Serialization;

namespace ProxyService.Models;

public class WeatherRequest
{
    public string City { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public DateTime RequestTime { get; set; } = DateTime.UtcNow;
    public string RequestId { get; set; } = string.Empty;
}

public class WeatherResponse
{
    public string City { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public DateTime ForecastTime { get; set; }
    public float Temperature { get; set; }
    public string Condition { get; set; } = string.Empty;
    public int Humidity { get; set; }
    public float WindSpeed { get; set; }
    public string RequestId { get; set; } = string.Empty;
}
