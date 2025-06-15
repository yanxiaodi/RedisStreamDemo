# System Design Summary: Redis Stream-Based Terminal Management

## Problem Statement

The original system had several critical issues:
- **Uneven load distribution**: Round-robin routing to busy replicas caused long response times
- **Request rejection**: Full queues led to direct request rejection instead of queuing
- **Poor scalability**: Tight coupling between request handling and terminal management
- **Limited visibility**: No way to determine replica busy status for proper load balancing

## Solution Architecture

### Core Design Principles

1. **Decoupling**: Separate request reception from request processing
2. **Queue-based Communication**: Use Redis Streams for reliable message passing
3. **Fair Load Distribution**: Let workers compete for requests naturally
4. **Scalable Design**: Independent scaling of request handlers and workers

### System Components

#### RequestService (API Gateway)
```
┌─────────────────┐    ┌─────────────────┐    ┌─────────────────┐
│   Client        │───▶│  RequestService │───▶│  Redis Stream   │
│   HTTP Request  │    │  - Generate ID  │    │  requests-stream│
└─────────────────┘    │  - Queue Request│    └─────────────────┘
                       │  - Wait Response│              │
                       └─────────────────┘              │
                                ▲                       │
                                │                       ▼
┌─────────────────┐    ┌─────────────────┐    ┌─────────────────┐
│   Client        │◀───│  Redis Stream   │◀───│  WorkerService  │
│   HTTP Response │    │responses-{pod}  │    │  - Process Req  │
└─────────────────┘    └─────────────────┘    │  - Use Terminal │
                                              │  - Send Response│
                                              └─────────────────┘
```

#### WorkerService (Terminal Processor)
- Manages 4 terminals per worker instance
- Competes for requests from shared Redis Stream
- Maintains terminal sessions with automatic renewal
- Implements bulkhead pattern for concurrency control

### Key Improvements

#### 1. Natural Load Balancing
```
Request Flow:
1. Client → RequestService (any replica)
2. RequestService → Redis Stream (shared queue)
3. Available WorkerService ← Redis Stream (competitive consumption)
4. WorkerService → Terminal System
5. WorkerService → Redis Response Stream
6. RequestService ← Redis Response Stream
7. RequestService → Client
```

#### 2. Session Management
```
Terminal Session Lifecycle:
┌─────────────┐    ┌─────────────┐    ┌─────────────┐
│   Acquire   │───▶│   Validate  │───▶│   Execute   │
│   Terminal  │    │   Session   │    │   Request   │
└─────────────┘    └─────────────┘    └─────────────┘
       ▲                  │                   │
       │                  ▼                   ▼
┌─────────────┐    ┌─────────────┐    ┌─────────────┐
│   Release   │◀───│   Create    │    │   Update    │
│   Terminal  │    │   Session   │    │  LastUsed   │
└─────────────┘    └─────────────┘    └─────────────┘
```

#### 3. Fault Tolerance
- **Bulkhead Pattern**: Prevents one slow request from blocking others
- **Stream Persistence**: Requests survive service restarts
- **Session Recovery**: Automatic session renewal on expiration
- **Health Monitoring**: Comprehensive health checks with terminal availability

#### 4. Scalability Features
- **Independent Scaling**: Request and Worker services scale separately
- **Terminal Distribution**: Each worker gets 4 terminals from pool
- **Horizontal Growth**: Easy to add more workers for additional terminals
- **Configuration Driven**: Terminal assignments via configuration, not code

### Redis Stream Architecture

#### Request Stream (`requests-stream`)
```json
{
  "requestId": "unique-guid",
  "requestData": "payload",
  "requestType": "query|update|procedure",
  "timestamp": "2025-06-15T10:30:00Z",
  "sourcePod": "request-pod-name"
}
```

#### Response Streams (`responses-{pod-name}`)
```json
{
  "requestId": "matching-request-id",
  "responseData": "result-payload",
  "success": true,
  "errorMessage": null,
  "timestamp": "2025-06-15T10:30:15Z"
}
```

### Configuration Strategy

#### Terminal Assignment Algorithm
```csharp
// Deterministic terminal selection based on pod name
var random = new Random(podName.GetHashCode());
var assignedTerminals = allTerminals
    .OrderBy(x => random.Next())
    .Take(terminalsPerWorker)
    .ToList();
```

This ensures:
- Consistent terminal assignment across restarts
- Even distribution across workers
- No terminal conflicts between workers

### Performance Characteristics

#### Before (Original System)
- **Load Balancing**: Round-robin (unaware of actual load)
- **Queue Management**: Per-replica queues (uneven utilization)
- **Failure Mode**: Request rejection when queue full
- **Scaling**: Coupled request/terminal management

#### After (New System)
- **Load Balancing**: Natural competition for shared work queue
- **Queue Management**: Centralized Redis Stream (even utilization)
- **Failure Mode**: Request queuing with eventual processing
- **Scaling**: Independent service scaling based on actual needs

### Deployment Strategy

#### Development
```bash
docker-compose up --build
# 2 RequestService replicas + 3 WorkerService replicas = 12 terminals active
```

#### Production (Kubernetes)
```bash
kubectl apply -f k8s/
# 3 RequestService replicas + 10 WorkerService replicas = 40 terminals active
```

### Monitoring and Observability

#### Health Endpoints
- **RequestService**: `/api/terminal/health` - Service status
- **WorkerService**: `/health` - Service status + available terminals

#### Key Metrics
- Request processing time
- Terminal utilization rate
- Session creation/reuse ratio  
- Queue depth in Redis Streams
- Worker availability per pod

#### Alerting Scenarios
- High queue depth (add more workers)
- Low terminal availability (investigate terminal system)
- Session creation failures (check credentials/connectivity)
- Response timeouts (check terminal system performance)

This architecture solves all the original problems while providing a foundation for future growth and operational excellence.
