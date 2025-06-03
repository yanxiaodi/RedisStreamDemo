# Run Redis Stream Demo locally using Docker
# This script builds and runs all components in Docker containers

# Ensure we're in the project root directory
cd d:\dev\learn\aspnetcore\RedisStreamDemo

# Create a Docker network for the services to communicate
docker network create redis-stream-demo-network 2>$null

# Step 1: Run Redis
Write-Host "🚀 Starting Redis..." -ForegroundColor Cyan
docker run --name redis-stream -p 6379:6379 -d --network redis-stream-demo-network redis:latest

# Step 2: Build Docker images
Write-Host "🔨 Building Docker images..." -ForegroundColor Cyan

# Build IngressService
Write-Host "Building IngressService image..." -ForegroundColor Yellow
docker build -t ingress-service:local -f IngressService/Dockerfile .

# Build ProxyService
Write-Host "Building ProxyService image..." -ForegroundColor Yellow
docker build -t proxy-service:local -f ProxyService/Dockerfile .

# Step 3: Run services
Write-Host "🚀 Starting services..." -ForegroundColor Cyan

# Run ProxyService
docker run --name proxy-service -d -p 5010:80 --network redis-stream-demo-network -e "ConnectionStrings__Redis=redis-stream:6379" proxy-service:local

# Run IngressService
docker run --name ingress-service -d -p 5009:80 --network redis-stream-demo-network -e "ConnectionStrings__Redis=redis-stream:6379" ingress-service:local

# Step 4: Check container status
Write-Host "✅ Containers are running:" -ForegroundColor Green
docker ps --filter "network=redis-stream-demo-network"

# Step 5: Display service URLs
Write-Host "🌐 IngressService is available at: http://localhost:5009/swagger" -ForegroundColor Magenta
Write-Host "🌐 ProxyService is available at: http://localhost:5010/swagger" -ForegroundColor Magenta

# Step 6: Print cleanup command
Write-Host "`nTo stop and remove all containers, run:" -ForegroundColor Yellow
Write-Host "docker rm -f redis-stream proxy-service ingress-service" -ForegroundColor Yellow
Write-Host "docker network rm redis-stream-demo-network" -ForegroundColor Yellow
