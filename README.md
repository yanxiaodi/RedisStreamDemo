# Redis Stream Demo

This project demonstrates a message queue system using Redis Streams with two ASP.NET Core Web API services:

1. **IngressService**: Receives requests, sends them to Redis stream, and waits for responses
2. **ProxyService**: Processes requests from Redis stream and returns mock weather data

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
