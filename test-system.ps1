# Test script for the Redis Stream Terminal Management System (PowerShell)

Write-Host "🚀 Starting Redis Stream Terminal Management System Test" -ForegroundColor Green

function Test-Api {
    param(
        [string]$Endpoint,
        [string]$Data
    )
    
    Write-Host "📡 Testing endpoint: $Endpoint" -ForegroundColor Yellow
    
    try {
        $headers = @{
            'Content-Type' = 'application/json'
        }
        
        $response = Invoke-RestMethod -Uri $Endpoint -Method POST -Body $Data -Headers $headers -TimeoutSec 30
        
        Write-Host "✅ Success:" -ForegroundColor Green
        Write-Host ($response | ConvertTo-Json -Depth 3) -ForegroundColor White
    }
    catch {
        Write-Host "❌ Failed: $($_.Exception.Message)" -ForegroundColor Red
        if ($_.Exception.Response) {
            Write-Host "Status Code: $($_.Exception.Response.StatusCode)" -ForegroundColor Red
        }
    }
    
    Write-Host "---" -ForegroundColor Gray
}

# Wait for services to be ready
Write-Host "⏳ Waiting for services to start..." -ForegroundColor Yellow
Start-Sleep -Seconds 10

# Test health endpoints
Write-Host "🔍 Testing health endpoints..." -ForegroundColor Cyan

try {
    $health = Invoke-RestMethod -Uri "http://localhost:5000/api/terminal/health" -Method GET -TimeoutSec 5
    Write-Host "RequestService Health:" -ForegroundColor Green
    Write-Host ($health | ConvertTo-Json) -ForegroundColor White
}
catch {
    Write-Host "RequestService health check failed: $($_.Exception.Message)" -ForegroundColor Red
}

# Test multiple requests
Write-Host "📝 Testing terminal execution..." -ForegroundColor Cyan

# Test request 1
Test-Api -Endpoint "http://localhost:5000/api/terminal/execute" -Data @'
{
    "data": "SELECT * FROM users WHERE id = 1",
    "type": "query",
    "timeoutSeconds": 15
}
'@

# Test request 2
Test-Api -Endpoint "http://localhost:5000/api/terminal/execute" -Data @'
{
    "data": "UPDATE inventory SET quantity = 100 WHERE product_id = 42",
    "type": "update",
    "timeoutSeconds": 20
}
'@

# Test request 3
Test-Api -Endpoint "http://localhost:5000/api/terminal/execute" -Data @'
{
    "data": "CALL generate_report(2025, 6)",
    "type": "procedure",
    "timeoutSeconds": 30
}
'@

# Test concurrent requests
Write-Host "🔄 Testing concurrent requests..." -ForegroundColor Cyan

$jobs = @()
for ($i = 1; $i -le 5; $i++) {
    $data = @"
{
    "data": "Concurrent request $i - $(Get-Date)",
    "type": "concurrent-test",
    "timeoutSeconds": 25
}
"@
    
    $job = Start-Job -ScriptBlock {
        param($endpoint, $requestData)
        
        try {
            $headers = @{ 'Content-Type' = 'application/json' }
            $response = Invoke-RestMethod -Uri $endpoint -Method POST -Body $requestData -Headers $headers -TimeoutSec 30
            return @{ Success = $true; Response = $response }
        }
        catch {
            return @{ Success = $false; Error = $_.Exception.Message }
        }
    } -ArgumentList "http://localhost:5000/api/terminal/execute", $data
    
    $jobs += $job
}

# Wait for all jobs to complete
Write-Host "⏳ Waiting for concurrent requests to complete..." -ForegroundColor Yellow
$results = $jobs | Wait-Job | Receive-Job

foreach ($result in $results) {
    if ($result.Success) {
        Write-Host "✅ Concurrent request succeeded:" -ForegroundColor Green
        Write-Host ($result.Response | ConvertTo-Json -Depth 3) -ForegroundColor White
    } else {
        Write-Host "❌ Concurrent request failed: $($result.Error)" -ForegroundColor Red
    }
}

# Clean up jobs
$jobs | Remove-Job

Write-Host "✨ Test completed!" -ForegroundColor Green
