# Prompts

## Step 1. Problem Definition

I have an existing system that encounters issues. I need to design a new system that addresses these issues.

The current system:

I have a system that depends on a legacy system. This legacy system provides services via terminals. One terminal can only handle one request at a time. Currently we have 40 terminals.

The system is based on ASP.NET Core 8.

To manage these terminals, my system stores the terminal information in a database, including the terminal ID, username, password, and which pod uses it. My system is deployed on kubernetes with multiple replicas. Each replica manages 2 terminals.

When a service instance is created, it queries the database to find available terminals and assigns them to the instance, then updates the database with the pod name to each terminal. The service will create corresponding instances for each terminal using a concurrent queue, which stores the terminal ID, pod name, availability status, session ID, and other information. When a request comes in, the service checks the availability of the terminals and check if it has a valid session ID. If it does not have a valid session ID, it will create a new session to sign in to the terminal, and then get the session ID and save it in the terminal information in the queue. If the terminal has session ID, it will use the existing session ID to send the request to the terminal. Each session ID can be reused. If the session ID is not used for a certain period of time, it will expire so if the service tries to use an expired session ID on this terminal, it will get an error and will need to create a new session. After the request is processed, the service will return the terminal to the pool of available terminals, and return the response to the client.

To manages these terminals and make sure the concurrent queue is not overloaded, the service uses a Polly bulkhead policy to limit the number of concurrent requests to the terminals. The policy is set to 2, which equals the number of terminals managed by each replica. This means that only 2 requests can be processed at a time, and the rest will be queued until one of the terminals becomes available. If there are more requests than the number of terminals, the requests will be queued until one of the terminals becomes available. If the queue is full, the requests will be rejected. The queue length is set to 100.

The issues with the current system:

The system works for a small number of requests. However, the requests response times vary greatly depending on the query type. For example, one endpoint may respond slower than another endpoint. It also depends on the data size for the query entity. For example, for the same endpoint, one request may take longer time than another request because this entity has more data than the other entity. This causes a problem: If the queue is full, kubernetes should not route the request to this replica because it is busy. However, the current system does not have a way to determine if the replica is busy or not. So if kubernetes uses a round-robin load balancing strategy, sometimes the request will be routed to a busy replica, which will cause long response times, which shows a few pods have less available queue space than others.

To solve these issues, I need to design a new system that can handle the following:

* Ensure each request is routed to the least busy replica, or the replica that can handle the request the fastest.
* Ensure each request is processed in a timely manner, even if there are not enough terminals available. The client can wait for a response, but it should not be rejected directly.
* Ensure that the requests are queued to be processed in the order they are received.
* In the future when I want to add more terminals, it should not break the existing system.

The terminals information is configured like this:

```json
"TerminalConfiguration": {
  "PodName": "local-pod",
  "Secret": "<terminals_password>",
  "Scheme": "http",
  "SessionTimeoutSeconds": 300
},
"TerminalsData": [
  "192.168.1.10|443|GATEWAY1|*****|4850|1",
  "192.168.1.10|443|GATEWAY2|*****|4851|1",
  "192.168.1.10|443|GATEWAY3|*****|4852|1",
  "192.168.1.10|443|GATEWAY4|*****|4853|1",
  "192.168.1.10|443|GATEWAY5|*****|4854|1",
  "192.168.1.10|443|GATEWAY6|*****|4855|1",
  "192.168.1.10|443|GATEWAY7|*****|4856|1",
  "192.168.1.10|443|GATEWAY8|*****|4857|1",
  "192.168.1.10|443|GATEWAY9|*****|4858|1",
  "192.168.1.10|443|GATEWAY10|*****|4859|1",
  "192.168.1.10|443|GATEWAY11|*****|4860|1",
  "192.168.1.10|443|GATEWAY12|*****|4861|1",
  "192.168.1.10|443|GATEWAY13|*****|4862|1",
  "192.168.1.10|443|GATEWAY14|*****|4863|1",
  "192.168.1.10|443|GATEWAY15|*****|4864|1",
  "192.168.1.10|443|GATEWAY16|*****|4865|1",
  "192.168.1.10|443|GATEWAY17|*****|4866|1",
  "192.168.1.10|443|GATEWAY18|*****|4867|1",
  "192.168.1.10|443|GATEWAY19|*****|4868|1",
  "192.168.1.10|443|GATEWAY20|*****|4869|1",
  "192.168.1.10|443|GATEWAY21|*****|4870|1",
  "192.168.1.10|443|GATEWAY22|*****|4871|1",
  "192.168.1.10|443|GATEWAY23|*****|4872|1",
  "192.168.1.10|443|GATEWAY24|*****|4873|1",
  "192.168.1.10|443|GATEWAY25|*****|4874|1",
  "192.168.1.10|443|GATEWAY26|*****|4875|1",
  "192.168.1.10|443|GATEWAY27|*****|4876|1",
  "192.168.1.10|443|GATEWAY28|*****|4877|1",
  "192.168.1.10|443|GATEWAY29|*****|4878|1",
  "192.168.1.10|443|GATEWAY30|*****|4879|1",
  "192.168.1.10|443|GATEWAY31|*****|4880|1",
  "192.168.1.10|443|GATEWAY32|*****|4881|1",
  "192.168.1.10|443|GATEWAY33|*****|4882|1",
  "192.168.1.10|443|GATEWAY34|*****|4883|1",
  "192.168.1.10|443|GATEWAY35|*****|4884|1",
  "192.168.1.10|443|GATEWAY36|*****|4885|1",
  "192.168.1.10|443|GATEWAY37|*****|4886|1",
  "192.168.1.10|443|GATEWAY38|*****|4887|1",
  "192.168.1.10|443|GATEWAY39|*****|4888|1",
  "192.168.1.10|443|GATEWAY40|*****|4889|1"
]
```

`192.168.1.10|443|GATEWAY1|*****|4850|1` means: `Host|Port|Username|Password|TerminalID|Branch`. All terminals are on the same host and they use the same port and http scheme.

To support the new requirements, a Redis cache is available.

Design a new system that can handle the requirements mentioned above.

## Step 2. Design the New System

Design a new system that can handle the requirements mentioned above. The new system should:

* There are two services: RequestService and WorkerService. Each service is deployed on kubernetes with multiple replicas.
* RequestService has the following responsibilities:
  * Initialise a Redis stream to store responses. The stream should be named `responses-{pod-name}`.
  * Receive requests from clients.
  * Create a request ID for each request.
  * Write the request to a Redis stream with the request ID, request data, and timestamp. The request should support generic data types. All requests from all pods should be written to the same Redis stream named `requests-stream`.
  * When receiving a response from `responses-{pod-name}`, it should acknowledge the response from the stream, and return the response to the client.
* WorkerService has the following responsibilities:
  * When the service starts, create a concurrent queue that stores 4 terminals with their information: terminal ID, session ID, and last used timestamp.
  * Read requests from the Redis stream `requests-stream`.
  * Set a bulkhead policy to limit the number of concurrent requests to 4, which equals the number of terminals managed by each replica.
  * For each request, dequeue one available terminal from the in-memory queue.
  * Find the terminal information.
    * If no terminals are available, it should wait for a terminal to become available.
    * If it can find an available terminal, it should check if the terminal has a valid session ID.
      * If the session ID is valid (it was used in the last 5 minutes), use it to process the request. Update the last used timestamp for the terminal in the queue.
      * If the session ID is not valid or does not exist, create a new session and save the session ID in the concurrent queue with a TTL. Then use the new session ID to process the request.
  * Process the request using the assigned terminal.
  * Write the response to the Redis stream `responses-{pod-name}` with the request ID and response data.
  * Acknowledge the request from the Redis stream `requests-stream`.
  * Return the terminal to the pool of available terminals in the in-memory queue. Update the last used timestamp in the queue.
