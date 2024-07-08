# Rolling restart of Apache Kafka

- Blog post about rolling restarts (https://www.cloudkarafka.com/blog/rolling-restart-of-apache-kafka.html#:~:text=A%20rolling%20restart%20means%20that,and%20with%20no%20message%20lost)
- A sample script to perform rolling restart (PowerShell + docker) - https://github.com/marcin-krystianc/KafkaPlayground/blob/master/docker/compose-cluster3/rolling-restart.ps1
- How to check if a cluster is healthy?
We can use `kafka-topics.sh` script to look for any partition that is not fully healthy:
    - `--under-replicated-partitions`
    - `--under-min-isr-partitions`
    - `--unavailable-partitions`
    - `--at-min-isr-partitions`

E.g.: `kafka-topics.sh --describe --under-replicated-partitions --unavailable-partitions --under-min-isr-partitions --at-min-isr-partitions --bootstrap-server ... `
To guarantee uninterrupted service, brokers can be stopped only when there are no unhealthy partitions!

To avoid any data loss, it is also important to disable the unclean leader election in the cluster(`KAFKA_UNCLEAN_LEADER_ELECTION_ENABLE=FALSE`).

Another configuration to consider is the minimum number of in-sync replicas (`min.insync.replicas`). During the rolling restart at least one broker will be down and another one (or more) can be down due to random failure. Therefore it is highly recommended to have the replication factor at least 3 and the minimum number of in-sync replicas set to at least 2.

# KafkaTool 

To test the robustness of the Kafka cluster during the rolling restart procedure, We've implemented a test tool (`kafkaTool`) in .NET and Java.
This tool was used to validate, the guarantees during the continuous rolling restart procedure.

- Example of usage of .NET version:
```
dotnet run --project KafkaTool.csproj `
producer-consumer `
--config allow.auto.create.topics=false `
--config bootstrap.servers=localhost:40001,localhost:40002,localhost:40003 `
--topics=150 --partitions=10 --replication-factor=3 --min-isr=2 `
--producers=75 --messages-per-second=1000 `
--topic-stem=my-topic `
--config request.timeout.ms=180000 `
--config message.timeout.ms=180000 `
--config request.required.acks=-1 `
--config enable.idempotence=false `
--config max.in.flight.requests.per.connection=1 `
--config debug=all
```

- JAVA:
```
mvn package; mvn exec:java "-Dexec.mainClass=kafka.testing.Main" "-Dexec.args=--config allow.auto.create.topics=false --config bootstrap.servers=localhost:40001,localhost:40002,localhost:40003 --topics=150 --partitions=10 --replication-factor=3 --min-isr=2 --messages-per-second=10000 --config request.timeout.ms=180000 --config message.timeout.ms=180000 --config request.required.acks=-1 --config enable.idempotence=false --config max.in.flight.requests.per.connection=1"
```

# Message order guarantee

Message ordering is an important guarantee of Kafka. 
Each topic may have several partitions, but message order is only preserved within individual partitions (but not for entire topics). 
Messages are written to the same partitions when they have the same message key.
To guarantee the order of messages, it is critical to set the `max.in.flight.requests.per.connection` to 1 (default is 5, this applies to C# and Java libraries).
If the number of concurrent requests is greater than 1, then the later requests can succeed first - resulting in unordered messages.

# Kafka delivery semantics and rolling restarts

Kafka supports three different delivery semantics:
- At least once delivery (duplicates possible)
- At most once delivery (No duplicates but messages might be lost)
- Exactly once delivery (Each message is stored and consumed exactly once)

### At least once delivery (duplicates possible)

It is necessary to set the number of required acks (`request.required.acks`) for the producer to `-1` (`All`).
Such configuration ensures that the producer considers the write request to be complete only after receiving at least `min.insync.replicas` confirmations.
Thus any controlled or uncontrolled shutdown of a broker will not lead to lost messages if the value of `min.insync.replicas` is at least 2:
- `min.insync.replicas=2`
- `request.required.acks=-1`

### At most once delivery (no duplicates, but messages might be lost)

If it is not necessary to guarantee delivery of all messages, then it is ok to use a less strict number of acks `0` (`None`) or `1` (`Leader`).
- `request.required.acks=1`

### Exactly once delivery
To achieve, the "Exactly once deliver" semantic it is necessary to set the number of required acks (`request.required.acks`) for the producer to `-1` (`All`) and we need to use idempotent producer (`enable.idempotence=true`) to prevent from duplicates
I've also found out empirically, that it is necessary to limit the number of concurrent requests to 1 (`max.in.flight.requests.per.connection=1`) to make sure no duplicates are ever created.
- `request.required.acks=-1`
- `enable.idempotence=true`
- `max.in.flight.requests.per.connection=1`