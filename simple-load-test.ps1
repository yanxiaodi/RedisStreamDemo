# Simple load test for Redis Stream Demo
# This script sends multiple sequential requests to test the processing

$cities = @(
    @{ city = "New York"; location = "Downtown" },
    @{ city = "London"; location = "Westminster" },
    @{ city = "Tokyo"; location = "Shibuya" },
    @{ city = "Paris"; location = "Eiffel Tower" },
    @{ city = "Sydney"; location = "Opera House" },
    @{ city = "Beijing"; location = "Forbidden City" },
    @{ city = "Mumbai"; location = "Gateway of India" },
    @{ city = "Rio de Janeiro"; location = "Copacabana" },
    @{ city = "Cairo"; location = "Pyramids" },
    @{ city = "Moscow"; location = "Red Square" }
)

$baseUrl = "http://localhost:5009/WeatherForecast/city"
$results = @()

Write-Host "Starting load test with 10 sequential requests..."

foreach ($cityData in $cities) {
    $body = @{
        city = $cityData.city
        location = $cityData.location
    } | ConvertTo-Json
    
    $start = Get-Date
    $response = Invoke-RestMethod -Uri $baseUrl -Method Post -ContentType "application/json" -Body $body
    $end = Get-Date
    $duration = ($end - $start).TotalMilliseconds
    
    $results += [PSCustomObject]@{
        City = $cityData.city
        Location = $cityData.location
        Temperature = $response.temperature
        Condition = $response.condition
        RequestId = $response.requestId
        Duration = $duration
    }
    
    Write-Host "Processed request for $($cityData.city), $($cityData.location) in $([math]::Round($duration, 2)) ms"
}

# Display results
Write-Host "Load test complete. Results:"
$results | Format-Table -Property City, Location, Temperature, Condition, RequestId, @{Name="Duration (ms)"; Expression={[math]::Round($_.Duration, 2)}}

# Calculate statistics
$avgDuration = ($results | Measure-Object -Property Duration -Average).Average
$maxDuration = ($results | Measure-Object -Property Duration -Maximum).Maximum
$minDuration = ($results | Measure-Object -Property Duration -Minimum).Minimum

Write-Host "Statistics:"
Write-Host "  Average response time: $([math]::Round($avgDuration, 2)) ms"
Write-Host "  Maximum response time: $([math]::Round($maxDuration, 2)) ms"
Write-Host "  Minimum response time: $([math]::Round($minDuration, 2)) ms"
