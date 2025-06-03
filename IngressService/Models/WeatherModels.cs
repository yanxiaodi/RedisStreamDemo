using System.Text.Json.Serialization;

namespace IngressService.Models;

public class WeatherRequest
{
    public string City { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public DateTime RequestTime { get; set; } = DateTime.UtcNow;
    
    [JsonIgnore]
    public string RequestId { get; set; } = Guid.NewGuid().ToString();
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
