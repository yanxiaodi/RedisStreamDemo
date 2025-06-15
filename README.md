# Redis Stream-Based Terminal Management System

This system provides a distributed terminal management solution using Redis Streams to handle the communication between request processors and terminal workers.

## Architecture Overview

The system consists of two main services:

### RequestService

- **Purpose**: Receives HTTP requests from clients and coordinates responses
- **Responsibilities**:
  - Initializes Redis streams for responses (`responses-{pod-name}`)
  - Receives client requests via REST API
  - Generates unique request IDs
  - Publishes requests to Redis stream (`requests-stream`)
  - Waits for and returns responses to clients

### WorkerService

- **Purpose**: Processes requests by managing terminal connections
- **Responsibilities**:
  - Manages a pool of 4 terminals per worker instance
  - Reads requests from Redis stream (`requests-stream`)
  - Implements bulkhead pattern for concurrency control
  - Manages terminal sessions with automatic renewal
  - Processes requests using available terminals
  - Publishes responses back to appropriate response streams

## Key Features

### 1. Load Distribution

- Requests are distributed via Redis Streams, ensuring fair load balancing
- No direct coupling between request and worker services
- Workers compete for available requests from the shared stream

### 2. Terminal Session Management

- Automatic session creation and validation
- Session timeout handling (configurable, default 5 minutes)
- Session reuse for efficiency

### 3. Fault Tolerance

- Bulkhead pattern prevents cascading failures
- Automatic retry and error handling
- Health checks for both services

### 4. Scalability

- Independent scaling of request and worker services
- Each worker manages 4 terminals
- Easy horizontal scaling through Kubernetes

## Configuration

### TerminalConfiguration

```json
{
  "TerminalConfiguration": {
    "PodName": "worker-pod-1",
    "Secret": "terminal_password_secret",
    "Scheme": "http",
    "SessionTimeoutSeconds": 300,
    "RedisConnectionString": "localhost:6379",
    "TerminalsData": [
      "192.168.1.10|443|GATEWAY1|*****|4850|1",
      "..."
    ]
  }
}
```

### Terminal Data Format

Each terminal entry follows the pattern: `Host|Port|Username|Password|TerminalID|Branch`

## Deployment

### Local Development with Docker Compose

1. **Start the services**:

   ```bash
   docker-compose up --build
   ```

2. **Test the API**:

   ```bash
   curl -X POST http://localhost:5000/api/terminal/execute \
     -H "Content-Type: application/json" \
     -d '{
       "data": "test request data",
       "type": "query",
       "timeoutSeconds": 30
     }'
   ```

### Kubernetes Deployment

1. **Deploy Redis**:

   ```bash
   kubectl apply -f k8s/redis.yaml
   ```

2. **Deploy Worker Service**:

   ```bash
   kubectl apply -f k8s/worker-service.yaml
   ```

3. **Deploy Request Service**:

   ```bash
   kubectl apply -f k8s/request-service.yaml
   ```

## API Endpoints

### RequestService

#### POST /api/terminal/execute

Execute a terminal request.

**Request Body**:
```json
{
  "data": "request payload",
  "type": "request type",
  "timeoutSeconds": 30
}
```

**Response**:
```json
{
  "requestId": "unique-request-id",
  "data": "response data",
  "timestamp": "2025-06-15T10:30:00Z"
}
```

#### GET /api/terminal/health
Health check endpoint.

### WorkerService

#### GET /health
Health check with terminal availability information.

**Response**:
```json
{
  "status": "healthy",
  "podName": "worker-pod-1",
  "availableTerminals": 4,
  "timestamp": "2025-06-15T10:30:00Z"
}
```

## Monitoring

### Health Checks
- Both services provide health check endpoints
- Kubernetes liveness and readiness probes configured
- Terminal availability tracking in worker service

### Logging
- Structured logging throughout the application
- Request tracing with unique request IDs
- Performance metrics for terminal operations

## Benefits Over Previous System

### 1. Improved Load Distribution
- Redis Streams provide fair request distribution
- No direct pod-to-pod communication required
- Automatic load balancing across workers

### 2. Better Scalability
- Independent scaling of request and worker services
- Easy to add more terminals without code changes
- Horizontal scaling through container orchestration

### 3. Enhanced Reliability
- Fault isolation between services
- Request persistence in Redis Streams
- Automatic session management

### 4. Operational Excellence
- Comprehensive health checks
- Detailed logging and monitoring
- Container-ready with Kubernetes support

## Configuration for Production

### Environment Variables
- `TerminalConfiguration__PodName`: Unique identifier for the pod
- `TerminalConfiguration__RedisConnectionString`: Redis connection string
- `ASPNETCORE_ENVIRONMENT`: Environment (Development/Production)

### Resource Requirements
- **RequestService**: 256Mi memory, 250m CPU (min) / 512Mi memory, 500m CPU (max)
- **WorkerService**: 256Mi memory, 250m CPU (min) / 512Mi memory, 500m CPU (max)
- **Redis**: 512Mi memory, 250m CPU (min) / 1Gi memory, 500m CPU (max)

## Troubleshooting

### Common Issues

1. **Redis Connection Issues**
   - Verify Redis service is running
   - Check connection string configuration
   - Ensure network connectivity between services

2. **Terminal Session Failures**
   - Check terminal credentials in configuration
   - Verify terminal system accessibility
   - Review session timeout settings

3. **Request Timeouts**
   - Monitor worker service availability
   - Check terminal pool status via health endpoints
   - Verify Redis stream processing

### Debugging Commands

```bash
# Check Redis streams
redis-cli XINFO STREAM requests-stream

# Monitor Redis stream messages
redis-cli XREAD STREAMS requests-stream 0

# Check service logs
kubectl logs -f deployment/request-service
kubectl logs -f deployment/worker-service
```

This architecture provides a robust, scalable solution that addresses the original system's limitations while maintaining compatibility with existing terminal infrastructure.
