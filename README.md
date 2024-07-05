# Rolling restart of Kafka

https://www.cloudkarafka.com/blog/rolling-restart-of-apache-kafka.html#:~:text=A%20rolling%20restart%20means%20that,and%20with%20no%20message%20lost.
"min.insync.replicas decide how many brokers that must acknowledge a producer when a message is sent with acks=all."


## Ordered, at least once delivery (duplicates possible)

### .NET
dotnet run --project KafkaTool.csproj `
producer-sequential `
--config allow.auto.create.topics=false `
--config bootstrap.servers=localhost:40001,localhost:40002,localhost:40003 `
--topics=150 --partitions=10 --replication-factor=3 --min-isr=2 --messages-per-second=10000 `
--config request.timeout.ms=180000 `
--config message.timeout.ms=180000 `
--config request.required.acks=-1 `
--config enable.idempotence=false `
--config max.in.flight.requests.per.connection=1

### JAVA

mvn package; mvn exec:java "-Dexec.mainClass=kafka.testing.Main" "-Dexec.args=--config allow.auto.create.topics=false --config bootstrap.servers=localhost:40001,localhost:40002,localhost:40003 --topics=150 --partitions=10 --replication-factor=3 --min-isr=2 --messages-per-second=10000 --config request.timeout.ms=180000 --config message.timeout.ms=180000 --config request.required.acks=-1 --config enable.idempotence=false --config max.in.flight.requests.per.connection=1"

## Ordered, exactly once delivery

### .NET
dotnet run --project KafkaTool.csproj `
producer-consumer `
--config allow.auto.create.topics=false `
--config bootstrap.servers=localhost:40001,localhost:40002,localhost:40003 `
--topics=150 --partitions=10 --replication-factor=3 --min-isr=2 --messages-per-second=100 `
--config request.timeout.ms=180000 `
--config message.timeout.ms=180000 `
--config request.required.acks=-1 `
--config enable.idempotence=true `
--config max.in.flight.requests.per.connection=1 `
--producers=75

### JAVA

mvn package; mvn exec:java "-Dexec.mainClass=kafka.testing.Main" "-Dexec.args=--config allow.auto.create.topics=false --config bootstrap.servers=localhost:40001,localhost:40002,localhost:40003 --topics=150 --partitions=10 --replication-factor=3 --min-isr=2 --messages-per-second=10000 --config request.timeout.ms=180000 --config message.timeout.ms=180000 --config request.required.acks=-1 --config enable.idempotence=true"

## Ordered, at most once delivery (no duplicates possible, messages might be missing)

### .NET
dotnet run --project KafkaTool.csproj `
producer-sequential `
--config allow.auto.create.topics=false `
--config bootstrap.servers=localhost:40001,localhost:40002,localhost:40003 `
--topics=150 --partitions=10 --replication-factor=3 --min-isr=2 --messages-per-second=10000 `
--config request.timeout.ms=180000 `
--config message.timeout.ms=180000 `
--config request.required.acks=1 `
--config enable.idempotence=false `
--config max.in.flight.requests.per.connection=1


## Unordered
dotnet run --project KafkaTool.csproj `
producer-sequential `
--config allow.auto.create.topics=false `
--config bootstrap.servers=localhost:40001,localhost:40002,localhost:40003 `
--topics=150 --partitions=10 --replication-factor=3 --min-isr=2 --messages-per-second=10000 `
--config request.timeout.ms=180000 `
--config message.timeout.ms=180000 `
--config request.required.acks=-1 `
--config enable.idempotence=false





#############################
--config debug=metadata
 