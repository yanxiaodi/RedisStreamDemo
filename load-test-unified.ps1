# Load Testing Script for Redis Stream Demo
# This unified script supports sequential and concurrent testing with customizable parameters.
#
# Usage:
#   .\load-test-unified.ps1 [-Mode <sequential|concurrent>] [-Url <baseUrl>] [-Count <requestCount>] [-Timeout <timeoutSeconds>]
#
# Examples:
#   .\load-test-unified.ps1                           # Run default test (sequential, 10 requests, localhost URL)
#   .\load-test-unified.ps1 -Mode concurrent          # Run concurrent test with default parameters
#   .\load-test-unified.ps1 -Url http://localhost:8081 -Count 20  # Run with custom URL and 20 requests
#   .\load-test-unified.ps1 -Mode concurrent -Url http://redis-stream-demo.local # Test K8s deployment

param (
    [Parameter()]
    [ValidateSet("sequential", "concurrent")]
    [string]$Mode = "sequential",
    
    [Parameter()]
    [string]$Url = "http://localhost:5009/WeatherForecast/city",
    
    [Parameter()]
    [int]$Count = 10,
    
    [Parameter()]
    [int]$Timeout = 60
)

# Define the list of test cities and locations
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
    @{ city = "Moscow"; location = "Red Square" },
    @{ city = "Berlin"; location = "Brandenburg Gate" },
    @{ city = "Rome"; location = "Colosseum" },
    @{ city = "Dubai"; location = "Burj Khalifa" },
    @{ city = "Bangkok"; location = "Grand Palace" },
    @{ city = "Toronto"; location = "CN Tower" }
)

# Set default error action
$ErrorActionPreference = "Stop"

# Verify the service is running by calling health endpoint
try {    $healthUrl = $Url -replace "WeatherForecast/city$", "healthz"
    
    Write-Host "Checking service health at $healthUrl..."
    $health = Invoke-RestMethod -Uri $healthUrl -TimeoutSec 5
    Write-Host "Service is healthy: $($health.status)" -ForegroundColor Green
}
catch {
    Write-Host "Error connecting to service: $_" -ForegroundColor Red
    Write-Host "Make sure the service is running and the URL is correct." -ForegroundColor Yellow
    exit 1
}

# Get test data based on the requested count
$testData = if ($Count -le $cities.Count) {
    $cities | Select-Object -First $Count
} else {
    # If more requests than cities, repeat cities as needed
    $repeated = @()
    for ($i = 0; $i -lt $Count; $i++) {
        $repeated += $cities[$i % $cities.Count]
    }
    $repeated
}

$results = @()

# Execute requests based on selected mode
if ($Mode -eq "sequential") {
    Write-Host "Starting sequential load test with $Count requests to $Url..." -ForegroundColor Cyan
    
    foreach ($cityData in $testData) {
        $body = @{
            city = $cityData.city
            location = $cityData.location
        } | ConvertTo-Json
        
        try {
            $start = Get-Date
            $response = Invoke-RestMethod -Uri $Url -Method Post -ContentType "application/json" -Body $body -TimeoutSec $Timeout
            $end = Get-Date
            $duration = ($end - $start).TotalMilliseconds
            
            $results += [PSCustomObject]@{
                City = $cityData.city
                Location = $cityData.location
                Temperature = $response.temperature
                Condition = $response.condition
                RequestId = $response.requestId
                Duration = $duration
                Status = "Success"
            }
            
            Write-Host "Processed request for $($cityData.city), $($cityData.location) in $([math]::Round($duration, 2)) ms" -ForegroundColor Green
        }
        catch {
            $end = Get-Date
            $duration = ($end - $start).TotalMilliseconds
            
            $results += [PSCustomObject]@{
                City = $cityData.city
                Location = $cityData.location
                Temperature = $null
                Condition = $null
                RequestId = $null
                Duration = $duration
                Status = "Error: $($_.Exception.Message)"
            }
            
            Write-Host "Error processing request for $($cityData.city), $($cityData.location): $($_.Exception.Message)" -ForegroundColor Red
        }
    }
} 
else {
    # Using PowerShell 7+ native PowerShell Parallel or regular jobs as fallback
    Write-Host "Starting concurrent load test with $Count requests to $Url..." -ForegroundColor Cyan
    
    $useThreadJobs = $PSVersionTable.PSVersion.Major -ge 7
    if ($useThreadJobs) {
        Write-Host "Using ThreadJobs for parallel execution (PowerShell 7+)" -ForegroundColor Cyan
        $jobs = @()
        
        foreach ($cityData in $testData) {
            $body = @{
                city = $cityData.city
                location = $cityData.location
            } | ConvertTo-Json
            
            $job = Start-ThreadJob -ScriptBlock {
                param($url, $body, $city, $location, $timeout)
                
                try {
                    $start = Get-Date
                    $response = Invoke-RestMethod -Uri $url -Method Post -ContentType "application/json" -Body $body -TimeoutSec $timeout
                    $end = Get-Date
                    $duration = ($end - $start).TotalMilliseconds
                    
                    return [PSCustomObject]@{
                        City = $city
                        Location = $location
                        Temperature = $response.temperature
                        Condition = $response.condition
                        RequestId = $response.requestId
                        Duration = $duration
                        Status = "Success"
                    }
                }
                catch {
                    $end = Get-Date
                    $duration = ($end - $start).TotalMilliseconds
                    return [PSCustomObject]@{
                        City = $city
                        Location = $location
                        Temperature = $null
                        Condition = $null
                        RequestId = $null
                        Duration = $duration
                        Status = "Error: $($_.Exception.Message)"
                    }
                }
            } -ArgumentList $Url, $body, $cityData.city, $cityData.location, $Timeout
            
            $jobs += $job
        }
        
        Write-Host "All requests sent, waiting for responses..." -ForegroundColor Yellow
        $results = $jobs | Wait-Job | Receive-Job
        $jobs | Remove-Job
    }
    else {
        Write-Host "Using regular PowerShell jobs (Compatible with PowerShell 5.1)" -ForegroundColor Cyan
        $jobs = @()
        
        foreach ($cityData in $testData) {
            $job = Start-Job -ScriptBlock {
                param($url, $city, $location, $timeout)
                
                try {
                    $body = @{
                        city = $city
                        location = $location
                    } | ConvertTo-Json
                    
                    $start = Get-Date
                    $response = Invoke-RestMethod -Uri $url -Method Post -ContentType "application/json" -Body $body -TimeoutSec $timeout
                    $end = Get-Date
                    $duration = ($end - $start).TotalMilliseconds
                    
                    return @{
                        City = $city
                        Location = $location
                        Temperature = $response.temperature
                        Condition = $response.condition
                        RequestId = $response.requestId
                        Duration = $duration
                        Status = "Success"
                    }
                }
                catch {
                    $end = Get-Date
                    $duration = ($end - $start).TotalMilliseconds
                    
                    return @{
                        City = $city
                        Location = $location
                        Temperature = $null
                        Condition = $null
                        RequestId = $null
                        Duration = $duration
                        Status = "Error: $($_.Exception.Message)"
                    }
                }
            } -ArgumentList $Url, $cityData.city, $cityData.location, $Timeout
            
            $jobs += $job
        }
        
        Write-Host "All requests sent, waiting for responses..." -ForegroundColor Yellow
        
        # Wait for all jobs to complete
        $jobs | Wait-Job | Out-Null
        
        # Get results from all jobs
        foreach ($job in $jobs) {
            $result = Receive-Job -Job $job
            $results += [PSCustomObject]$result
        }
        
        # Clean up jobs
        $jobs | Remove-Job
    }
}

# Separate successful and failed requests
$successfulRequests = $results | Where-Object { $_.Status -eq "Success" }
$failedRequests = $results | Where-Object { $_.Status -ne "Success" }

# Display results
Write-Host "Load test complete. Results:" -ForegroundColor Cyan

# Display successful requests
if ($successfulRequests.Count -gt 0) {
    Write-Host "Successful Requests ($($successfulRequests.Count)):" -ForegroundColor Green
    $successfulRequests | Format-Table -Property City, Location, Temperature, Condition, @{Name="Duration (ms)"; Expression={[math]::Round($_.Duration, 2)}}, RequestId
    
    # Calculate statistics for successful requests
    $avgDuration = ($successfulRequests | Measure-Object -Property Duration -Average).Average
    $maxDuration = ($successfulRequests | Measure-Object -Property Duration -Maximum).Maximum
    $minDuration = ($successfulRequests | Measure-Object -Property Duration -Minimum).Minimum
    
    Write-Host "Statistics:" -ForegroundColor Cyan
    Write-Host "  Average response time: $([math]::Round($avgDuration, 2)) ms" -ForegroundColor Cyan
    Write-Host "  Maximum response time: $([math]::Round($maxDuration, 2)) ms" -ForegroundColor Cyan
    Write-Host "  Minimum response time: $([math]::Round($minDuration, 2)) ms" -ForegroundColor Cyan
}

# Display failed requests
if ($failedRequests.Count -gt 0) {
    Write-Host "Failed Requests ($($failedRequests.Count)):" -ForegroundColor Red
    $failedRequests | Format-Table -Property City, Location, @{Name="Duration (ms)"; Expression={[math]::Round($_.Duration, 2)}}, Status
}

# Summary
Write-Host "Summary:" -ForegroundColor Cyan
Write-Host "  Total Requests: $($results.Count)" -ForegroundColor Cyan
Write-Host "  Successful: $($successfulRequests.Count)" -ForegroundColor Green
Write-Host "  Failed: $($failedRequests.Count)" -ForegroundColor $(if ($failedRequests.Count -gt 0) { "Red" } else { "Green" })
Write-Host "  Success Rate: $([math]::Round(($successfulRequests.Count / $results.Count) * 100, 2))%" -ForegroundColor $(if ($successfulRequests.Count -eq $results.Count) { "Green" } elseif ($successfulRequests.Count -gt 0) { "Yellow" } else { "Red" })
