# About

How this is supposed to work.

## Scheduler App - console app

Scheduler app sends a service bus message to schedule a job with unique correlation-id.
It then subscribes to all events (using a filter (?)) 
When result is received via pub sub - app is done.
When app is closed before the result -> it sends cancellation via pub-sub.

## Job App - container app (DAPR)

Starts processing job received on service bus topic.
It connects to WebPubSub instance to report progress and listen for the cancellation message. 