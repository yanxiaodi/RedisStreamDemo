# Monitor Redis Stream Demo in Kubernetes
# This script provides useful commands for monitoring the system in Kubernetes

# Set namespace variable for convenience
$NAMESPACE = "redis-stream-demo"
$MONITORING_NAMESPACE = "monitoring"

function Show-Help {
    Write-Host "Redis Stream Demo Monitoring Tool" -ForegroundColor Cyan
    Write-Host "=================================" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Available commands:" -ForegroundColor Yellow
    Write-Host "  1. Show-Pods                - Show all pods in the application namespace"
    Write-Host "  2. Show-Services            - Show all services in the application namespace"
    Write-Host "  3. Show-Deployments         - Show all deployments in the application namespace"
    Write-Host "  4. Show-HPAs                - Show Horizontal Pod Autoscalers"
    Write-Host "  5. Show-Logs [pod-name]     - Show logs for a specific pod"
    Write-Host "  6. Show-Metrics [pod-name]  - Show metrics endpoint output from a pod"
    Write-Host "  7. Port-Forward-Grafana     - Forward Grafana port to localhost:3000"
    Write-Host "  8. Port-Forward-Prometheus  - Forward Prometheus port to localhost:9090"
    Write-Host "  9. Port-Forward-IngressService - Forward IngressService to localhost:8080"
    Write-Host "  10. Test-Service            - Send a test request to IngressService"
    Write-Host "  11. Show-System-Status      - Show overall system status"
    Write-Host ""
    Write-Host "Example: Show-Logs ingress-service-7d8f9b7c67-abcd1" -ForegroundColor DarkGray
}

function Show-Pods {
    kubectl get pods -n $NAMESPACE
}

function Show-Services {
    kubectl get services -n $NAMESPACE
}

function Show-Deployments {
    kubectl get deployments -n $NAMESPACE
}

function Show-HPAs {
    kubectl get hpa -n $NAMESPACE
}

function Show-Logs {
    param (
        [Parameter(Mandatory=$true)]
        [string]$PodName
    )
    
    kubectl logs -n $NAMESPACE $PodName --tail=100
}

function Show-Metrics {
    param (
        [Parameter(Mandatory=$true)]
        [string]$PodName
    )
    
    $TEMP_PORT = Get-Random -Minimum 10000 -Maximum 20000
    
    Write-Host "Starting port-forward for $PodName on port $TEMP_PORT..." -ForegroundColor Yellow
    $job = Start-Job -ScriptBlock { 
        param($ns, $pod, $port) 
        kubectl port-forward -n $ns $pod ${port}:80
    } -ArgumentList $NAMESPACE, $PodName, $TEMP_PORT
    
    # Wait a moment for port-forward to establish
    Start-Sleep -Seconds 2
    
    try {
        Write-Host "Fetching metrics..." -ForegroundColor Yellow
        $metrics = Invoke-RestMethod -Uri "http://localhost:${TEMP_PORT}/metrics" -TimeoutSec 5
        Write-Host $metrics -ForegroundColor Green
    }
    catch {
        Write-Host "Error fetching metrics: $_" -ForegroundColor Red
    }
    finally {
        # Clean up the port-forward job
        Stop-Job -Job $job
        Remove-Job -Job $job
    }
}

function Port-Forward-Grafana {
    $grafanaPod = (kubectl get pods -n $MONITORING_NAMESPACE -l app=grafana -o jsonpath="{.items[0].metadata.name}")
    if ($grafanaPod) {
        Write-Host "Starting port-forward for Grafana on localhost:3000..." -ForegroundColor Yellow
        Write-Host "Use admin/admin to log in" -ForegroundColor Cyan
        Write-Host "Press Ctrl+C to stop" -ForegroundColor Yellow
        kubectl port-forward -n $MONITORING_NAMESPACE $grafanaPod 3000:3000
    } else {
        Write-Host "No Grafana pod found!" -ForegroundColor Red
    }
}

function Port-Forward-Prometheus {
    $prometheusPod = (kubectl get pods -n $MONITORING_NAMESPACE -l app=prometheus -o jsonpath="{.items[0].metadata.name}")
    if ($prometheusPod) {
        Write-Host "Starting port-forward for Prometheus on localhost:9090..." -ForegroundColor Yellow
        Write-Host "Press Ctrl+C to stop" -ForegroundColor Yellow
        kubectl port-forward -n $MONITORING_NAMESPACE $prometheusPod 9090:9090
    } else {
        Write-Host "No Prometheus pod found!" -ForegroundColor Red
    }
}

function Port-Forward-IngressService {
    $ingressPod = (kubectl get pods -n $NAMESPACE -l app=ingress-service -o jsonpath="{.items[0].metadata.name}")
    if ($ingressPod) {
        Write-Host "Starting port-forward for IngressService on localhost:8080..." -ForegroundColor Yellow
        Write-Host "Press Ctrl+C to stop" -ForegroundColor Yellow
        kubectl port-forward -n $NAMESPACE $ingressPod 8080:80
    } else {
        Write-Host "No IngressService pod found!" -ForegroundColor Red
    }
}

function Test-Service {
    $ingressPod = (kubectl get pods -n $NAMESPACE -l app=ingress-service -o jsonpath="{.items[0].metadata.name}")
    if (-not $ingressPod) {
        Write-Host "No IngressService pod found!" -ForegroundColor Red
        return
    }
    
    $TEMP_PORT = Get-Random -Minimum 10000 -Maximum 20000
    
    Write-Host "Starting port-forward for IngressService on port $TEMP_PORT..." -ForegroundColor Yellow
    $job = Start-Job -ScriptBlock { 
        param($ns, $pod, $port) 
        kubectl port-forward -n $ns $pod ${port}:80
    } -ArgumentList $NAMESPACE, $ingressPod, $TEMP_PORT
    
    # Wait a moment for port-forward to establish
    Start-Sleep -Seconds 2
    
    try {
        Write-Host "Sending test request to weather forecast endpoint..." -ForegroundColor Yellow
        $body = @{
            city = "New York"
            location = "Downtown"
        } | ConvertTo-Json
        
        $start = Get-Date
        $response = Invoke-RestMethod -Uri "http://localhost:${TEMP_PORT}/WeatherForecast/city" -Method Post -ContentType "application/json" -Body $body -TimeoutSec 10
        $end = Get-Date
        $duration = ($end - $start).TotalMilliseconds
        
        Write-Host "Response received in $([math]::Round($duration, 2)) ms:" -ForegroundColor Green
        $response | Format-List
    }
    catch {
        Write-Host "Error testing service: $_" -ForegroundColor Red
    }
    finally {
        # Clean up the port-forward job
        Stop-Job -Job $job
        Remove-Job -Job $job
    }
}

function Show-System-Status {
    Write-Host "Redis Stream Demo System Status" -ForegroundColor Cyan
    Write-Host "=============================" -ForegroundColor Cyan
    
    # Check deployments
    Write-Host "`nDeployments:" -ForegroundColor Yellow
    kubectl get deployments -n $NAMESPACE
    
    # Check pods
    Write-Host "`nPods:" -ForegroundColor Yellow
    kubectl get pods -n $NAMESPACE
    
    # Check HPAs
    Write-Host "`nHorizontal Pod Autoscalers:" -ForegroundColor Yellow
    kubectl get hpa -n $NAMESPACE
    
    # Check Redis
    Write-Host "`nRedis Status:" -ForegroundColor Yellow
    $redisPod = (kubectl get pods -n $NAMESPACE -l app=redis -o jsonpath="{.items[0].metadata.name}")
    if ($redisPod) {
        kubectl exec -n $NAMESPACE $redisPod -- redis-cli info | Select-String "connected_clients|used_memory_human|total_system_memory_human"
    } else {
        Write-Host "No Redis pod found!" -ForegroundColor Red
    }
    
    # Check monitoring
    Write-Host "`nMonitoring Status:" -ForegroundColor Yellow
    kubectl get pods -n $MONITORING_NAMESPACE
}

# Export functions for direct use
Export-ModuleMember -Function Show-Help, Show-Pods, Show-Services, Show-Deployments, Show-HPAs, Show-Logs, Show-Metrics, Port-Forward-Grafana, Port-Forward-Prometheus, Port-Forward-IngressService, Test-Service, Show-System-Status

# Show help by default
Show-Help
