# Rolling restart of Kafka

## Ordered, at least once delivery
- Acks=All


### .NET
dotnet run --project KafkaTool.csproj `
producer-sequential `
--config allow.auto.create.topics=false `
--config bootstrap.servers=localhost:40001,localhost:40002,localhost:40003 `
--topics=150 --partitions=10 --replication-factor=3 --min-isr=2 --messages-per-second=10000 `
--config request.timeout.ms=180000 `
--config message.timeout.ms=180000 `
--config max.block.ms=180000 `
--acks=All `
--config enable.idempotence=false `
--config max.in.flight.requests.per.connection=1


## Ordered, exactly once delivery

## Ordered, at most once delivery

## Unordered




--config debug=metadata
 