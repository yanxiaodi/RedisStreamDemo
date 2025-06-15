#!/bin/bash

# Test script for the Redis Stream Terminal Management System

echo "🚀 Starting Redis Stream Terminal Management System Test"

# Function to test API endpoint
test_api() {
    local endpoint=$1
    local data=$2
    
    echo "📡 Testing endpoint: $endpoint"
    
    response=$(curl -s -w "\n%{http_code}" -X POST "$endpoint" \
        -H "Content-Type: application/json" \
        -d "$data")
    
    http_code=$(echo "$response" | tail -n1)
    body=$(echo "$response" | head -n -1)
    
    if [ "$http_code" -eq 200 ]; then
        echo "✅ Success: $body"
    else
        echo "❌ Failed with status $http_code: $body"
    fi
    
    echo "---"
}

# Wait for services to be ready
echo "⏳ Waiting for services to start..."
sleep 10

# Test health endpoints
echo "🔍 Testing health endpoints..."

# Test RequestService health
curl -s http://localhost:5000/api/terminal/health | jq '.' 2>/dev/null || echo "RequestService health check failed"

# Test multiple requests
echo "📝 Testing terminal execution..."

# Test request 1
test_api "http://localhost:5000/api/terminal/execute" '{
    "data": "SELECT * FROM users WHERE id = 1",
    "type": "query",
    "timeoutSeconds": 15
}'

# Test request 2
test_api "http://localhost:5000/api/terminal/execute" '{
    "data": "UPDATE inventory SET quantity = 100 WHERE product_id = 42",
    "type": "update",
    "timeoutSeconds": 20
}'

# Test request 3
test_api "http://localhost:5000/api/terminal/execute" '{
    "data": "CALL generate_report(2025, 6)",
    "type": "procedure",
    "timeoutSeconds": 30
}'

# Test concurrent requests
echo "🔄 Testing concurrent requests..."

for i in {1..5}; do
    test_api "http://localhost:5000/api/terminal/execute" "{
        \"data\": \"Concurrent request $i - $(date)\",
        \"type\": \"concurrent-test\",
        \"timeoutSeconds\": 25
    }" &
done

wait

echo "✨ Test completed!"
