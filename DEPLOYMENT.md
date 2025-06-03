# Redis Stream Demo - Deployment Architecture

## System Architecture

The Redis Stream Demo consists of three main components:

1. **IngressService** - Receives client requests, forwards them to Redis, and waits for responses
2. **Redis** - Acts as a message broker using Redis Streams
3. **ProxyService** - Processes requests from Redis and sends responses back

## Kubernetes Architecture

The application is deployed to Kubernetes with the following components:

### Namespaces
- `redis-stream-demo` - Contains application components
- `monitoring` - Contains monitoring components (Prometheus, Grafana)

### Deployments
- **Redis**: 1 replica with persistence
- **IngressService**: 3 replicas (auto-scales up to 10)
- **ProxyService**: 15 replicas (auto-scales up to 30)

### Services
- **Redis**: ClusterIP service for internal communication
- **IngressService**: ClusterIP service exposed via Ingress
- **ProxyService**: ClusterIP service for internal use only

### Network Policies
- Redis only accepts connections from IngressService and ProxyService
- IngressService accepts external traffic through Ingress

### Monitoring
- Prometheus for metrics collection
- Grafana for metrics visualization

## Performance Capacity

- Each ProxyService pod handles 5 concurrent requests (via terminals)
- Initial deployment: 15 pods × 5 terminals = 75 concurrent requests
- Maximum capacity: 30 pods × 5 terminals = 150 concurrent requests
- Estimated throughput: ~30,000 requests/minute with 150ms processing time

## Deployment Process

1. Create namespaces:
   ```bash
   kubectl apply -f k8s/namespaces.yaml
   ```

2. Deploy Redis:
   ```bash
   kubectl apply -f k8s/redis.yaml
   ```

3. Deploy application services:
   ```bash
   kubectl apply -f k8s/config-map.yaml
   kubectl apply -f k8s/ingress-service.yaml
   kubectl apply -f k8s/proxy-service.yaml
   ```

4. Configure auto-scaling:
   ```bash
   kubectl apply -f k8s/ingress-service-hpa.yaml
   kubectl apply -f k8s/proxy-service-hpa.yaml
   ```

5. Configure networking:
   ```bash
   kubectl apply -f k8s/ingress.yaml
   kubectl apply -f k8s/network-policies.yaml
   ```

6. Set up monitoring:
   ```bash
   kubectl apply -f k8s/prometheus-config.yaml
   kubectl apply -f k8s/prometheus.yaml
   kubectl apply -f k8s/grafana.yaml
   ```

## Monitoring and Operations

### Key Metrics
- Request rate and latency
- Active terminals in each ProxyService pod
- Redis memory usage
- Pod CPU and memory utilization

### Scaling Considerations
- Horizontal Pod Autoscaler scales based on CPU utilization
- Consider Redis scaling for very high loads
- Monitor backpressure in the bounded channels

### Disaster Recovery
- Redis data is ephemeral in this implementation
- For production, consider Redis persistence or a managed Redis service

## Local Development

For local development, use Docker Compose:
```bash
docker-compose up -d
```

## Load Testing

Use the unified load testing script:
```bash
./load-test-unified.ps1 [-Mode <sequential|concurrent>] [-Url <baseUrl>] [-Count <requestCount>] [-Timeout <timeoutSeconds>]
```

Examples:
```bash
# Run default test (sequential, 10 requests)
./load-test-unified.ps1

# Run concurrent test 
./load-test-unified.ps1 -Mode concurrent

# Test against Kubernetes deployment
./load-test-unified.ps1 -Url http://redis-stream-demo.local -Mode concurrent
```
