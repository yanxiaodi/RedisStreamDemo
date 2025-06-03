# Redis Stream Demo

This project demonstrates a decoupled service architecture using Redis Streams for communication between services.

## Architecture

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

## Running Redis

Run the following command in PowerShell to start Redis in a Docker container:

```powershell
docker run --name redis-stream -p 6379:6379 -d redis:latest
```

## Running the Applications

1. Start the ProxyService first:

```powershell
cd ProxyService
dotnet run
```

2. In a separate terminal, start the IngressService:

```powershell
cd IngressService
dotnet run
```

## Testing with Swagger UI

1. IngressService Swagger UI: [http://localhost:5009/swagger](http://localhost:5009/swagger)
2. ProxyService Swagger UI: [http://localhost:5010/swagger](http://localhost:5010/swagger)

## Flow

1. Send a POST request to `http://localhost:5009/WeatherForecast/city` with a JSON body:
```json
{
  "city": "New York",
  "location": "Downtown"
}
```

2. IngressService writes this request to Redis stream
3. ProxyService reads the request, processes it, and sends a response back via Redis stream
4. IngressService receives the response and returns it to the client

## Redis Commands for Monitoring (Optional)

You can use Redis CLI to monitor the streams:

```powershell
# Connect to Redis CLI in the container
docker exec -it redis-stream redis-cli

# View all entries in the request stream
XRANGE weather-requests - +

# View all entries in the response stream
XRANGE weather-responses - +
```

## Kubernetes Deployment

For a production deployment, you would:

1. Create Docker images for each service
2. Deploy to Kubernetes with:
   - Deployments for IngressService and ProxyService
   - A Redis StatefulSet or use a managed Redis service
   - Services to expose the APIs
   - Horizontal Pod Autoscalers to scale based on load

With 10 ProxyService pods, each handling 5 concurrent requests, the system can process up to 50 concurrent requests.

## Load Testing

Use the provided load testing scripts to verify the concurrent processing:
- `simple-load-test.ps1` - Sequential requests
- `concurrent-load-test.ps1` - Concurrent requests
- `load-test.ps1` - Full load test with parallel processing

## Monitoring

Monitor the system's performance by:
1. Watching the Redis stream lengths to detect bottlenecks
2. Tracking terminal utilization across ProxyService instances
3. Measuring response times for different load levels
