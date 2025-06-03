# Redis Stream Demo

This project demonstrates a decoupled service architecture using Redis Streams for communication between services.

## Getting Started

You can run this application in three different ways:

1. **[Local Development](#local-development)**: Run services directly with .NET CLI
2. **[Docker Compose](#docker-compose)**: Run all services in Docker containers
3. **[Kubernetes Deployment](#kubernetes-deployment)**: Deploy to a Kubernetes cluster

### Prerequisites

- .NET 9.0 SDK
- Docker Desktop for Windows
- Kubernetes enabled in Docker Desktop (for Kubernetes deployment)

## Architecture

The solution consists of two ASP.NET Core Web API services:

1. **IngressService** - Receives requests from clients, sends them to a Redis stream, and waits for responses.
2. **ProxyService** - Reads requests from the Redis stream, processes them, and sends responses back.

This architecture allows for:
- Loose coupling between services
- Scaling each service independently
- Resilience to service outages
- Controlled concurrency and backpressure

## Resilience Features

The application implements several resilience patterns for production readiness:

- **Retry Pattern** - Automatically retries failed Redis operations with exponential backoff
- **Circuit Breaker** - Prevents cascading failures when Redis is unavailable
- **Graceful Shutdown** - Ensures in-flight requests are completed before termination
- **Enhanced Health Checks** - Provides visibility into system health and circuit state
- **Metrics Tracking** - Offers real-time visibility into queue depths and performance

See [RESILIENCE.md](RESILIENCE.md) for detailed implementation information.

The solution consists of two ASP.NET Core Web API services:

1. **IngressService** - Receives requests from clients, sends them to a Redis stream, and waits for responses.
2. **ProxyService** - Reads requests from the Redis stream, processes them, and sends responses back.

This architecture allows for:
- Loose coupling between services
- Scaling each service independently
- Resilience to service outages
- Controlled concurrency and backpressure

## Concurrency Control

The ProxyService implements a concurrency control mechanism to limit the number of concurrent requests it processes:

- Each instance of the ProxyService manages 5 "terminals" (concurrent workers)
- Each terminal can handle one request at a time
- Requests are queued using a bounded channel to handle backpressure
- When all terminals are busy, new requests are queued until a terminal becomes available

This design allows for:
1. Protecting legacy services that can only handle a limited number of concurrent requests
2. Efficient resource utilization across multiple instances
3. Preventing overload during traffic spikes

## Redis Streams

The services communicate using two Redis streams:
- `weather-requests` - For requests from IngressService to ProxyService
- `weather-responses` - For responses from ProxyService back to IngressService

## Prerequisites

- .NET 9.0 SDK
- Docker Desktop for Windows

## Running Locally (Local Development)

### 1. Start Redis

Run the following command in PowerShell to start Redis in a Docker container:

```powershell
docker run --name redis-stream -p 6379:6379 -d redis:latest
```

### 2. Run the Applications

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

### 3. Testing with Swagger UI

- IngressService Swagger UI: [http://localhost:5009/swagger](http://localhost:5009/swagger)
- ProxyService Swagger UI: [http://localhost:5010/swagger](http://localhost:5010/swagger)

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

Use the provided load testing scripts to verify the application behavior:

```powershell
# For sequential requests
./simple-load-test.ps1

# For concurrent requests
./concurrent-load-test.ps1

# For full load test with parallel processing
./load-test.ps1
```

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

## Additional Resources

- [RESILIENCE.md](RESILIENCE.md) - Details on resilience patterns implemented
- [DEPLOYMENT.md](DEPLOYMENT.md) - More details on deployment options
- [UPDATES.md](UPDATES.md) - Recent updates and changes

