# Concurrent load test for Redis Stream Demo
# This script sends multiple requests with minimal delay to test concurrency

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

Write-Host "Starting concurrent load test with 10 requests..."

# Start async requests (PowerShell 7+ required)
$tasks = foreach ($cityData in $cities) {
    $body = @{
        city = $cityData.city
        location = $cityData.location
    } | ConvertTo-Json
    
    Start-ThreadJob -ScriptBlock {
        param($url, $body, $city, $location)
        
        $start = Get-Date
        $response = Invoke-RestMethod -Uri $url -Method Post -ContentType "application/json" -Body $body
        $end = Get-Date
        $duration = ($end - $start).TotalMilliseconds
        
        return [PSCustomObject]@{
            City = $city
            Location = $location
            Temperature = $response.temperature
            Condition = $response.condition
            RequestId = $response.requestId
            Duration = $duration
            Terminal = $null  # We'll fill this from the logs later
        }
    } -ArgumentList $baseUrl, $body, $cityData.city, $cityData.location
    
    # Small delay to ensure we can see the sequence in logs
    Start-Sleep -Milliseconds 50
}

Write-Host "All requests sent, waiting for responses..."

# Wait for all jobs to complete
$results = $tasks | Wait-Job | Receive-Job

# Remove jobs
$tasks | Remove-Job

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
