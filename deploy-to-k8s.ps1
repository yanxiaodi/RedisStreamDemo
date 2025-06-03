# Deploy Redis Stream Demo to Kubernetes
# This script builds Docker images and deploys all components to a Kubernetes cluster

# Set variables
$IMAGE_REGISTRY = "localhost:5000"  # Change this to your container registry
$INGRESS_SERVICE_IMAGE = "$IMAGE_REGISTRY/ingress-service:latest"
$PROXY_SERVICE_IMAGE = "$IMAGE_REGISTRY/proxy-service:latest"

# Ensure we're in the project root directory
cd d:\dev\learn\aspnetcore\RedisStreamDemo

# Step 1: Build Docker images
Write-Host "🔨 Building Docker images..." -ForegroundColor Cyan

# Build IngressService
Write-Host "Building IngressService image..." -ForegroundColor Yellow
docker build -t $INGRESS_SERVICE_IMAGE -f IngressService/Dockerfile .

# Build ProxyService
Write-Host "Building ProxyService image..." -ForegroundColor Yellow
docker build -t $PROXY_SERVICE_IMAGE -f ProxyService/Dockerfile .

# Step 2: Push images to registry
Write-Host "📤 Pushing images to registry..." -ForegroundColor Cyan
docker push $INGRESS_SERVICE_IMAGE
docker push $PROXY_SERVICE_IMAGE

# Step 3: Create namespaces
Write-Host "🔨 Creating namespaces..." -ForegroundColor Cyan
kubectl apply -f k8s/namespaces.yaml

# Step 4: Deploy Redis
Write-Host "🔨 Deploying Redis..." -ForegroundColor Cyan
kubectl apply -f k8s/redis.yaml

# Step 5: Apply ConfigMap
Write-Host "🔨 Applying ConfigMap..." -ForegroundColor Cyan
kubectl apply -f k8s/config-map.yaml

# Step 6: Deploy services with proper image references
Write-Host "🔨 Generating deployment manifests with correct image references..." -ForegroundColor Cyan

# Update IngressService manifest with the correct image
$ingressYaml = Get-Content -Path k8s/ingress-service.yaml -Raw
$ingressYaml = $ingressYaml -replace "image: ingress-service:latest", "image: $INGRESS_SERVICE_IMAGE"
$ingressYaml | Out-File -FilePath k8s/temp-ingress-service.yaml -Encoding utf8

# Update ProxyService manifest with the correct image
$proxyYaml = Get-Content -Path k8s/proxy-service.yaml -Raw
$proxyYaml = $proxyYaml -replace "image: proxy-service:latest", "image: $PROXY_SERVICE_IMAGE"
$proxyYaml | Out-File -FilePath k8s/temp-proxy-service.yaml -Encoding utf8

# Step 7: Deploy the services
Write-Host "🚀 Deploying services..." -ForegroundColor Cyan
kubectl apply -f k8s/temp-ingress-service.yaml
kubectl apply -f k8s/temp-proxy-service.yaml

# Clean up temporary files
Remove-Item -Path k8s/temp-ingress-service.yaml
Remove-Item -Path k8s/temp-proxy-service.yaml

# Step 8: Configure autoscalers
Write-Host "⚖️ Setting up HPA..." -ForegroundColor Cyan
kubectl apply -f k8s/ingress-service-hpa.yaml
kubectl apply -f k8s/proxy-service-hpa.yaml

# Step 8: Deploy ingress rule
Write-Host "🌐 Configuring ingress..." -ForegroundColor Cyan
kubectl apply -f k8s/ingress.yaml

# Step 9: Deploy monitoring
Write-Host "📊 Setting up monitoring..." -ForegroundColor Cyan
kubectl apply -f k8s/prometheus-config.yaml
kubectl apply -f k8s/prometheus.yaml
kubectl apply -f k8s/grafana.yaml

# Step 10: Wait for deployments to be ready
Write-Host "⏳ Waiting for deployments to be ready..." -ForegroundColor Cyan
kubectl rollout status deployment/redis -n redis-stream-demo
kubectl rollout status deployment/ingress-service -n redis-stream-demo
kubectl rollout status deployment/proxy-service -n redis-stream-demo

# Done!
Write-Host "✅ Deployment complete!" -ForegroundColor Green
Write-Host "📊 Prometheus is available at: http://prometheus-service.monitoring:9090" -ForegroundColor Magenta
Write-Host "📈 Grafana is available at: http://grafana-service.monitoring:3000" -ForegroundColor Magenta
Write-Host "   Default credentials: admin/admin" -ForegroundColor Magenta
Write-Host "🌐 Application is available at: http://redis-stream-demo.local/" -ForegroundColor Magenta
Write-Host "   (Make sure to add an entry to your hosts file)" -ForegroundColor Magenta

# Display pod status
kubectl get pods -n redis-stream-demo
kubectl get pods -n monitoring
