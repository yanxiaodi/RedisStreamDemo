# Redis Stream Demo

This project demonstrates a decoupled service architecture using Redis Streams for communication between services.

## Table of Contents

- [Getting Started](#getting-started)
  - [Prerequisites](#prerequisites)
  - [Running Locally](#running-locally)
  - [Running with Docker Compose](#running-with-docker-compose)
  - [Kubernetes Deployment](#kubernetes-deployment)
- [Architecture](#architecture)
  - [System Components](#system-components)
  - [Communication Flow](#communication-flow)
  - [Redis Streams](#redis-streams)
  - [Concurrency Control](#concurrency-control)
- [Resilience Features](#resilience-features)
  - [Retry Pattern](#retry-pattern)
  - [Circuit Breaker](#circuit-breaker)
  - [Graceful Shutdown](#graceful-shutdown)
  - [Health Checks](#health-checks)
- [Testing](#testing)
  - [Load Testing](#load-testing)
  - [Monitoring Performance](#monitoring-performance)
- [Operations](#operations)
  - [Key Metrics](#key-metrics)
  - [Scaling Considerations](#scaling-considerations)
  - [Disaster Recovery](#disaster-recovery)
- [Updates and Roadmap](#updates-and-roadmap)
  - [Recent Enhancements](#recent-enhancements)
  - [Future Plans](#future-plans)

## Getting Started

### Prerequisites

- .NET 9.0 SDK
- Docker Desktop for Windows
- Kubernetes enabled in Docker Desktop (for Kubernetes deployment)

### Running Locally

#### 1. Start Redis

Run the following command in PowerShell to start Redis in a Docker container:

```powershell
docker run --name redis-stream -p 6379:6379 -d redis:latest
```

#### 2. Run the Applications

Start the ProxyService first:

```powershell
cd ProxyService
dotnet run
```

In a separate terminal, start the IngressService:

```powershell
cd IngressService
dotnet run
```

#### 3. Testing with Swagger UI

- IngressService Swagger UI: [http://localhost:5009/swagger](http://localhost:5009/swagger)
- ProxyService Swagger UI: [http://localhost:5010/swagger](http://localhost:5010/swagger)

### Running with Docker Compose

For local development with Docker, you can use Docker Compose to start all services at once:

```powershell
# Run the convenience script
./run-locally-docker.ps1

# Or use docker-compose directly
docker-compose up -d
```

This will start Redis, IngressService, and ProxyService containers with appropriate networking.

Services will be available at:
- IngressService: http://localhost:5009
- ProxyService: http://localhost:5010

### Kubernetes Deployment

For production deployment on Kubernetes, follow these steps:

#### 1. Configure Docker Registry

Ensure your Docker registry is properly configured in the `deploy-to-k8s.ps1` script:

```powershell
$IMAGE_REGISTRY = "localhost:5000"  # Change this to your container registry
```

#### 2. Deploy to Kubernetes

Run the deployment script:

```powershell
./deploy-to-k8s.ps1
```

This will:
- Build and push Docker images for both services
- Create necessary Kubernetes namespaces
- Deploy Redis instance
- Deploy IngressService and ProxyService with proper configurations
- Set up Horizontal Pod Autoscalers (HPA)
- Configure ingress for external access
- Deploy Prometheus and Grafana for monitoring

#### 3. Access the Application

After deployment completes:

- Add `redis-stream-demo.local` to your hosts file:
  ```powershell
  Add-Content -Path "C:\Windows\System32\drivers\etc\hosts" -Value "`n127.0.0.1 redis-stream-demo.local" -Force
  ```
- Access the application at: http://redis-stream-demo.local/

#### 4. Access Monitoring

For monitoring tools, set up port forwarding:

```powershell
kubectl port-forward service/prometheus-service 9090:9090 -n monitoring
kubectl port-forward service/grafana-service 3000:3000 -n monitoring
```

Then access:
- Prometheus: http://localhost:9090
- Grafana: http://localhost:3000 (credentials: admin/admin)

#### 5. Troubleshooting

If pods don't start correctly, check:
- ConfigMap existence: `kubectl get configmap app-config -n redis-stream-demo`
- Pod status: `kubectl get pods -n redis-stream-demo`
- Pod logs: `kubectl logs -l app=ingress-service -n redis-stream-demo`

## Running with Docker Compose

For local development with Docker, you can use Docker Compose to start all services at once:

```powershell
# Run the convenience script
./run-locally-docker.ps1

# Or use docker-compose directly
docker-compose up -d
```

This will start Redis, IngressService, and ProxyService containers with appropriate networking.

Services will be available at:
- IngressService: http://localhost:5009
- ProxyService: http://localhost:5010

## Flow

When sending a request to the weather forecast endpoint:

1. Send a POST request to `http://localhost:5009/WeatherForecast/city` with a JSON body:
   ```json
   {
     "city": "New York",
     "location": "Downtown"
   }
   ```

2. The IngressService writes this request to the Redis stream
3. ProxyService reads the request, processes it, and sends a response back via Redis stream
4. IngressService receives the response and returns it to the client

This decoupled approach provides several benefits:
- Services can be scaled independently
- Temporary service outages are handled gracefully
- Backpressure is managed through the Redis stream

## Kubernetes Deployment

For production deployment on Kubernetes, follow these steps:

### 1. Configure Docker Registry

Ensure your Docker registry is properly configured in the `deploy-to-k8s.ps1` script:

```powershell
$IMAGE_REGISTRY = "localhost:5000"  # Change this to your container registry
```

### 2. Deploy to Kubernetes

Run the deployment script:

```powershell
./deploy-to-k8s.ps1
```

This will:
- Build and push Docker images for both services
- Create necessary Kubernetes namespaces
- Deploy Redis instance
- Deploy IngressService and ProxyService with proper configurations
- Set up Horizontal Pod Autoscalers (HPA)
- Configure ingress for external access
- Deploy Prometheus and Grafana for monitoring

### 3. Access the Application

After deployment completes:

- Add `redis-stream-demo.local` to your hosts file:
  ```powershell
  Add-Content -Path "C:\Windows\System32\drivers\etc\hosts" -Value "`n127.0.0.1 redis-stream-demo.local" -Force
  ```
- Access the application at: http://redis-stream-demo.local/

### 4. Access Monitoring

For monitoring tools, set up port forwarding:

```powershell
kubectl port-forward service/prometheus-service 9090:9090 -n monitoring
kubectl port-forward service/grafana-service 3000:3000 -n monitoring
```

Then access:
- Prometheus: http://localhost:9090
- Grafana: http://localhost:3000 (credentials: admin/admin)

### 5. Troubleshooting

If pods don't start correctly, check:
- ConfigMap existence: `kubectl get configmap app-config -n redis-stream-demo`
- Pod status: `kubectl get pods -n redis-stream-demo`
- Pod logs: `kubectl logs -l app=ingress-service -n redis-stream-demo`

## Load Testing and Monitoring

### Running Load Tests

Use the unified load testing script to verify the application behavior with various configurations:

```powershell
# Run default sequential test
./load-test-unified.ps1

# Run concurrent test
./load-test-unified.ps1 -Mode concurrent

# Test against Kubernetes deployment with 20 requests
./load-test-unified.ps1 -Url http://redis-stream-demo.local -Count 20 -Mode concurrent
```

The unified script provides:
- Sequential or concurrent request modes
- Customizable request count
- Support for different deployment environments
- Detailed success/failure reporting
- Automatic health checks before testing

Legacy scripts are also available for specific use cases:
- `simple-load-test.ps1` - Basic sequential requests
- `concurrent-load-test.ps1` - PowerShell 7+ optimized parallel testing
- `load-test.ps1` - PowerShell 5.1 compatible parallel testing

### Monitoring Performance

Monitor the application's performance using:

1. **Redis Stream Monitoring**

   Connect to Redis CLI and examine the streams:

   ```powershell
   # Connect to Redis CLI
   docker exec -it redis-stream redis-cli

   # View all entries in the request stream
   XRANGE weather-requests - +

   # View all entries in the response stream
   XRANGE weather-responses - +
   ```

2. **Prometheus Metrics**

   Navigate to http://localhost:9090 when running with Kubernetes, and look for:
   - HTTP request counters and durations
   - Weather request processing times
   - Redis circuit state
   - Pending request counts

3. **Grafana Dashboards**

   After setting up Grafana in Kubernetes, import dashboards to visualize:
   - Service response times
   - Terminal utilization
   - Request queue depths
   - Error rates

## Updates and Roadmap

### Recent Enhancements

#### Testing Improvements (Latest)

- Added unified load testing script with configurable parameters
- Enhanced error handling and reporting in test scripts
- Added support for different deployment environments in tests
- Improved test output with color-coded success/failure reporting
- Automatic health checks before testing

#### Resilience Features

- Implemented Polly-based resilience patterns for improved reliability
- Added retry logic for all Redis operations with exponential backoff
- Implemented circuit breaker pattern to prevent cascading failures
- Enhanced metrics with queue depth and circuit state tracking
- Improved health checks to detect circuit breaker status
- Added graceful shutdown handling in background services

#### Kubernetes Deployment

- Complete Kubernetes deployment configurations for a production environment
- Resource limits and requests for optimal performance
- Readiness and liveness probes for service health monitoring
- Horizontal Pod Autoscalers for automatic scaling based on load
- Network policies for enhanced security
- ConfigMap for centralized configuration management

#### Monitoring and Observability

- Prometheus and Grafana integration for comprehensive monitoring
- Custom metrics endpoints for application-specific metrics
- Active terminal tracking in ProxyService for concurrency visibility
- Basic Kubernetes monitoring script for operational convenience

#### Application Improvements

- Health check endpoints for both services
- Metrics middleware for request tracking
- Improved terminal management in ProxyService
- Thread-safe concurrent collections for request handling

### Future Plans

1. **Persistent Redis**: Configure Redis with persistence for message durability
2. **Distributed Tracing**: Add OpenTelemetry integration for request tracing
3. ~~**Circuit Breakers**: Implement circuit breakers for resilience~~ (Implemented)
4. **Rate Limiting**: Add API rate limiting to protect the system from overload
5. **Authentication**: Implement JWT authentication for API security
6. ~~**Graceful Shutdown**: Enhance shutdown logic to properly drain requests~~ (Implemented)
7. **Chaos Testing**: Implement chaos testing to verify system resilience
8. **CI/CD Pipeline**: Create a full CI/CD pipeline for automated deployments

### Deployment Considerations

- For production environments, consider using a managed Redis service
- Adjust resource limits based on actual production workloads
- Consider implementing a service mesh for advanced traffic management
- Set up proper logging aggregation (e.g., ELK stack, Loki)

## Additional Resources

- [RESILIENCE.md](RESILIENCE.md) - Details on resilience patterns implemented
- [DEPLOYMENT.md](DEPLOYMENT.md) - More details on deployment options

