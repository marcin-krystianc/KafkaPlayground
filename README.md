# Rolling restart of Kafka

## Ordered, at least once delivery
- Acks=All


### .NET
dotnet run --project KafkaTool.csproj `
producer-sequential `
--ini-file="D:\workspace\KafkaPlayground\dotnet\kafka.ini" `
--topics=150 --partitions=10 --replication-factor=3 --min-isr=2 --messages-per-second=10000 `
--config request.timeout.ms=180000 `
--config message.timeout.ms=180000 `
--acks=All `
--config enable.idempotence=false `
--config max.in.flight.requests.per.connection=1

 

## Ordered, exactly once delivery

## Ordered, at most once delivery

## Unordered




--config debug=metadata
 