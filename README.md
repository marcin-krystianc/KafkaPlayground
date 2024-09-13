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
mvn package; mvn exec:java "-Dexec.mainClass=kafka.testing.Main" "-Dexec.args=producer-consumer --config allow.auto.create.topics=false --config bootstrap.servers=localhost:40001,localhost:40002,localhost:40003 --topics=150 --partitions=10 --replication-factor=3 --min-isr=2 --messages-per-second=10000 --config request.timeout.ms=180000 --config message.timeout.ms=180000 --config request.required.acks=-1 --config enable.idempotence=false --config max.in.flight.requests.per.connection=1"
```

# Message order guarantee

Message ordering is an important guarantee of Kafka. 
Each topic may have several partitions, but message order is only preserved within individual partitions (but not for entire topics). 
Messages are written to the same partitions when they have the same message key.
To guarantee the order of messages, it is critical to set the `max.in.flight.requests.per.connection` to 1 (mind that default is 5).
If the number of concurrent requests is greater than 1, then the later requests can succeed first - resulting in unordered messages (Applies to C# and Java libraries when `enable.idempotence=false)`.

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
I've also found out empirically, that it is necessary to limit the number of concurrent requests to 1 (`max.in.flight.requests.per.connection=1`) to make sure no duplicates are ever created (We are sure that it applies to C#, but not confirmed if it applies to Java as well).
- `request.required.acks=-1`
- `enable.idempotence=true`
- `max.in.flight.requests.per.connection=1`

# TODO
Several issues have been identified with the librdkafka library. Further investigation and solutions for these issues are planned as part of our efforts.

- Idempotent producer can oocassionaly get stuck when acquiring idempotence PID (https://github.com/confluentinc/librdkafka/issues/3848)

- Producer can fail with "Unknown topic or partition" (https://github.com/confluentinc/librdkafka/issues/4401)

- Exactly once delivery in C#/Python/C++. 

To our understanding it shouldn't be required to set `max.in.flight.requests.per.connection=1` for the "exactly once delivery", but it seems it is the case for librdkafka:
```
dotnet run -c Release --project KafkaTool.csproj `
>> producer-consumer `
>> --config allow.auto.create.topics=false `
>> --config bootstrap.servers=localhost:40001,localhost:40002,localhost:40003 `
>> --topics=200 `
>> --partitions=10 `
>> --replication-factor=3 `
>> --min-isr=2 `
>> --messages-per-second=200 `
>> --config request.timeout.ms=180000 `
>> --config message.timeout.ms=180000 `
>> --config request.required.acks=-1 `
>> --config enable.idempotence=true `
>> --config max.in.flight.requests.per.connection=5 `
>> --producers=50 `
>> --recreate-topics-delay=10000 `
>> --recreate-topics-batch-size=100 `
>>
```
```
...
09:46:57 fail: Consumer:[0] Unexpected message value, topic/k [p]=my-topic-163/16 [9], Offset=2004/2005, LeaderEpoch=1/2,  previous value=182, messageValue=182!
09:46:57 fail: Consumer:[0] Unexpected message value, topic/k [p]=my-topic-123/6 [4], Offset=1456/1457, LeaderEpoch=1/2,  previous value=182, messageValue=182!
09:47:03 info: Log[0] Elapsed: 310s, 2880380 (+92820) messages produced, 2871183 (+141581) messages consumed, 2 duplicated, 0 out of sequence.
09:47:13 info: Log[0] Elapsed: 320s, 2972422 (+92042) messages produced, 2972230 (+101047) messages consumed, 2 duplicated, 0 out of sequence.
```

- Stack overflow in C++/C#/Python (librdkafka v2.4.0 (2.3.0 is ok)):
```
dotnet run --project KafkaTool.csproj `
producer `
--config allow.auto.create.topics=false `
--config bootstrap.servers=localhost:40001,localhost:40002,localhost:40003 `
--topics=3000 --partitions=1 --replication-factor=3 --min-isr=2 --messages-per-second=10000 `
--config request.timeout.ms=180000 `
--config message.timeout.ms=180000 `
--config request.required.acks=-1 `
--config enable.idempotence=true `
--config max.in.flight.requests.per.connection=1 `
--config topic.metadata.propagation.max.ms=60000 `
--producers=1 `
--recreate-topics-delay=100 `
--recreate-topics-batch-size=500
```

Fails with this stack trace:
```
centos8-librdkafka.so!rd_avl_insert_node[localalias] (Unknown Source:0)
centos8-librdkafka.so!rd_avl_insert_node[localalias] (Unknown Source:0)
centos8-librdkafka.so!rd_avl_insert_node[localalias] (Unknown Source:0)
...
centos8-librdkafka.so!rd_avl_insert_node[localalias] (Unknown Source:0)
centos8-librdkafka.so!rd_avl_insert_node[localalias] (Unknown Source:0)
centos8-librdkafka.so!rd_avl_insert_node[localalias] (Unknown Source:0)
```

- Java Producer fails with "Error: Local: Inconsistent state" in C++/C#/Python (librdkafka v2.3.0 + rolling restarts):
```
dotnet run --project KafkaTool.csproj -- \
producer-consumer \
--config allow.auto.create.topics=false \
--config bootstrap.servers=localhost:40001,localhost:40002,localhost:40003 \
--config request.timeout.ms=180000 \
--config message.timeout.ms=180000 \
--config request.required.acks=-1 \
--config enable.idempotence=true \
--config max.in.flight.requests.per.connection=5 \
--topics=100 \
--partitions=1 \
--replication-factor=3 \
--min-isr=2 \
--producers=1 \
--messages-per-second=7777 \
--recreate-topics-delay=1000 \
--recreate-topics-batch-size=100 \
--topic-stem=oss.my-topic
```
Logs:
```
11:48:27 info: Log[0] Elapsed: 260s, 2024085 (+77700) messages produced, 2023672 (+77553) messages consumed, 0 duplicated, 0 out of sequence.
11:48:37 info: Log[0] Elapsed: 270s, 2102562 (+78477) messages produced, 2100980 (+77308) messages consumed, 0 duplicated, 0 out of sequence.
11:48:38 info: Consumer:[0] Consumer log: message=[thrd:localhost:40002/bootstrap]: localhost:40002/2: Connect to ipv6#[::1]:40002 failed: Connection refused (after 0ms in state CONNECT), name=rdkafka#consumer-3, facility=FAIL, level=Error
11:48:38 fail: Consumer:[0] Consumer error: reason=localhost:40002/2: Connect to ipv6#[::1]:40002 failed: Connection refused (after 0ms in state CONNECT), IsLocal=True, IsBroker=False, IsFatal=False, IsCode=Local_Transport
11:48:39 info: Producer0:[0] kafka-log Facility:FATAL, Message[thrd:localhost:40002/bootstrap]: Fatal error: Local: Inconsistent state: Unable to reconstruct MessageSet (currently with 18 message(s)) with msgid range 21106..21147: last message added has msgid 21123: unable to guarantee consistency
11:48:39 fail: Producer0:[0] Producer error: reason=Unable to reconstruct MessageSet (currently with 18 message(s)) with msgid range 21106..21147: last message added has msgid 21123: unable to guarantee consistency, IsLocal=True, IsBroker=False, IsFatal=True, IsCode=Local_Inconsistent
11:48:39 info: Log[0] Admin log: message=[thrd:localhost:40002/bootstrap]: localhost:40002/bootstrap: Connect to ipv6#[::1]:40002 failed: Connection refused (after 0ms in state CONNECT), name=rdkafka#producer-4, facility=FAIL, level=Error
11:48:39 fail: Log[0] Admin error: reason=localhost:40002/bootstrap: Connect to ipv6#[::1]:40002 failed: Connection refused (after 0ms in state CONNECT), IsLocal=True, IsBroker=False, IsFatal=False, IsCode=Local_Transport

```

- Producer: expiring messages without delivery error report ?

```
mvn package; mvn exec:java "-Dexec.mainClass=kafka.testing.Main" "$(cat <<EOF | tr '\n' ' ' | sed 's/ *$//'
"-Dexec.args=producer-consumer 
--config allow.auto.create.topics=false
--config bootstrap.servers=localhost:40001,localhost:400
--topics=200
--partitions=10
--replication-factor=3
--min-isr=2
--messages-per-second=200
--config request.timeout.ms=180000
--config message.timeout.ms=180000
--config request.required.acks=-1
--config enable.idempotence=true
--config max.in.flight.requests.per.connection=5
--producers=50
--recreate-topics-delay=10000
--recreate-topics-batch-size=100
--topic-stem=oss.my-topic
EOF
)"
```
```
[16:30:35] kafka-producer-network-thread | client-9b1e09e5-5181-449f-a01c-2fa23f2aa2e1 - Expiring 6 record(s) for oss.my-topic-55-5:180000 ms has passed since batch creation
[16:30:35] kafka-producer-network-thread | client-9b1e09e5-5181-449f-a01c-2fa23f2aa2e1 - Expiring 6 record(s) for oss.my-topic-55-5:180000 ms has passed since batch creation
[16:30:35] kafka-producer-network-thread | client-9b1e09e5-5181-449f-a01c-2fa23f2aa2e1 - Expiring 6 record(s) for oss.my-topic-55-5:180000 ms has passed since batch creation
[16:30:35] kafka-producer-network-thread | client-9b1e09e5-5181-449f-a01c-2fa23f2aa2e1 - Expiring 6 record(s) for oss.my-topic-55-5:180000 ms has passed since batch creation
[16:30:35] kafka-producer-network-thread | client-9b1e09e5-5181-449f-a01c-2fa23f2aa2e1 - Expiring 6 record(s) for oss.my-topic-55-5:180000 ms has passed since batch creation
[16:30:35] kafka-producer-network-thread | client-9b1e09e5-5181-449f-a01c-2fa23f2aa2e1 - Expiring 6 record(s) for oss.my-topic-55-5:180000 ms has passed since batch creation
[16:30:35] kafka-producer-network-thread | client-4e6b400f-e9c7-480d-a326-de6c4b5eeb6c - Expiring 1 record(s) for oss.my-topic-91-5:180000 ms has passed since batch creation
```