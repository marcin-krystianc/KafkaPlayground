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
dotnet run --project KafkaTool.csproj \
producer-consumer \
--config allow.auto.create.topics=false \
--config bootstrap.servers=localhost:40001,localhost:40002,localhost:40003 
--config request.timeout.ms=180000 \
--config message.timeout.ms=180000 \
--config request.required.acks=-1 \
--config enable.idempotence=false \
--config max.in.flight.requests.per.connection=1 \
--topics=1 \
--partitions=1 \
--replication-factor=3 \
--min-isr=2 \
--producers=1 \
--messages-per-second=1000 \
--recreate-topics-delay=10000 \
--recreate-topics-batch-size=500 \
--topic-stem=oss.my-topic
```

- JAVA:
```
mvn package; mvn exec:java "-Dexec.mainClass=kafka.testing.Main" "$(cat <<EOF | tr '\n' ' ' | sed 's/ *$//'
"-Dexec.args=producer-consumer 
--config allow.auto.create.topics=false 
--config bootstrap.servers=localhost:40001,localhost:40002,localhost:40003 
--config request.timeout.ms=180000 
--config message.timeout.ms=180000 
--config request.required.acks=-1 
--config enable.idempotence=false 
--config max.in.flight.requests.per.connection=5
--topics=100
--partitions=1
--replication-factor=3 
--min-isr=2 
--producers=1
--messages-per-second=7777
--recreate-topics-delay=1000 
--recreate-topics-batch-size=100 
--topic-stem=oss.my-topic"
EOF
)"
```

## Message Order Guarantee

In Kafka, message ordering is preserved within individual partitions of a topic but not across the topic as a whole. For messages with identical keys, the assignment to the same partition is guaranteed.

To ensure message order, it is essential to:

- Either Set `max.in.flight.requests.per.connection=1` (default is 5).
- Or enable idempotence by setting `enable.idempotence=true` (default is `false`).

When idempotence is not enabled and the number of concurrent requests exceeds one, messages may not arrive in order, as subsequent requests might complete before earlier ones.

### Examples of out of order messages

<details>
  <summary>Java</summary>

- Start the [test cluster](https://github.com/marcin-krystianc/KafkaPlayground/tree/master/docker/compose-cluster3)
- Run the test tool
```
  mvn package; mvn exec:java "-Dexec.mainClass=kafka.testing.Main" "$(cat <<EOF | tr '\n' ' ' | sed 's/ *$//'
	"-Dexec.args=producer-consumer 
	--config allow.auto.create.topics=false 
	--config bootstrap.servers=localhost:40001,localhost:40002,localhost:40003 
	--config request.timeout.ms=180000 
	--config message.timeout.ms=180000 
	--config request.required.acks=-1 
	--config enable.idempotence=false 
	--config max.in.flight.requests.per.connection=5
	--topics=100
	--partitions=1
	--replication-factor=3 
	--min-isr=2 
	--producers=1
	--messages-per-second=7777
	--recreate-topics-delay=1000 
	--recreate-topics-batch-size=100 
	--topic-stem=oss.my-topic"
	EOF
	)"
```
- Start the [rolling restart procedure](https://github.com/marcin-krystianc/KafkaPlayground/blob/master/docker/compose-cluster3/rolling-restart.ps1)
- Log:
```
[11:20:15] consumer - Unexpected message value topic oss.my-topic-15/6 [0], Offset=7830/7839, LeaderEpoch=1/1 Value=1117/1119
```

</details>

<details>
  <summary>C#/Python/C++</summary>

- Start the [test cluster](https://github.com/marcin-krystianc/KafkaPlayground/tree/master/docker/compose-cluster3)
- Run the [KafkaTool](https://github.com/marcin-krystianc/KafkaPlayground/tree/master/java/KafkaTool):
	```
	dotnet run --project KafkaTool.csproj -- \
	producer-consumer \
	--config allow.auto.create.topics=false \
	--config bootstrap.servers=localhost:40001,localhost:40002,localhost:40003 \
	--config request.timeout.ms=180000 \
	--config message.timeout.ms=180000 \
	--config request.required.acks=-1 \
	--config enable.idempotence=false \
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
- Start the [rolling restart procedure](https://github.com/marcin-krystianc/KafkaPlayground/blob/master/docker/compose-cluster3/rolling-restart.ps1)
- Log:
```
11:24:48 fail: Consumer:[0] Unexpected message value, topic/k [p]=oss.my-topic-66/6 [0], Offset=10604/10611, LeaderEpoch=1/2,  previous value=1514, messageValue=1513!
```

</details>

## Kafka Delivery Semantics

### At least once: Messages are delivered once or more; duplicates may occur. 

To achieve at least once delivery, which may include duplicate messages, it is essential to configure the producer to require acknowledgments from all replicas. This is set by specifying `request.required.acks=-1` (All). This configuration ensures that the producer only considers a write request complete after receiving acknowledgment from at least the minimum number of in-sync replicas (`min.insync.replicas`). To safeguard against message loss during either controlled or uncontrolled broker shutdowns, it is recommended to set `min.insync.replicas` to at least two. This ensures redundancy and reliability in message delivery under varied network conditions.

- `min.insync.replicas=2` (or more)

### At most once: No duplicates, but messages may be lost. 

For scenarios where the complete delivery of all messages is not critical, it is ok to use a less strict number of acks `0` (`None`) or `1` (`Leader`).
- `request.required.acks=1`

### Exactly once: Each message is delivered and processed exactly once
To guarantee, the "Exactly once" semantic it is necessary to set `request.required.acks=-1` (`All`) and we need to set `enable.idempotence=true` to prevent from any message duplicates.

There is also a known issue with librdkafka (C#/Python/C++), where message duplicates are still possible until `max.in.flight.requests.per.connection=1` is set.
- `request.required.acks=-1`
- `enable.idempotence=true`
- `max.in.flight.requests.per.connection=1` (librdkafka)

## Known librdkafka (C#/Python/C++) issues
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

- Assertions in a debug build 
```
dotnet run --project KafkaTool.csproj \
producer \
--config allow.auto.create.topics=false \
--config bootstrap.servers=localhost:40001,localhost:40002,localhost:40003 \
--topics=3000 \
--partitions=1 \
--replication-factor=3 \
--min-isr=2 \
--messages-per-second=10000 \
--config request.timeout.ms=180000 \
--config message.timeout.ms=180000 \
--config request.required.acks=-1 \
--config enable.idempotence=true \
--config max.in.flight.requests.per.connection=1 \
--config topic.metadata.propagation.max.ms=60000 \
--producers=1 \
--recreate-topics-delay=500 \
--recreate-topics-batch-size=500
```
```
KafkaTool: rdkafka_queue.h:509: rd_kafka_q_deq0: Assertion `rkq->rkq_qlen > 0 && rkq->rkq_qsize >= (int64_t)rko->rko_len' failed.
```


- Ocasional memory corruption (debug build, ASAN)
```
 Producer0:[0] kafka-log Facility:METADATAUPDATE, Message[thrd:main]: Partition my-topic-1723(AAAAAAAAAAAAAAAAAAAAAA)[0]: not found in cache
```

- https://github.com/confluentinc/confluent-kafka-dotnet/issues/2157 (https://gr-oss.slack.com/archives/CT1CLERMX/p1726822842501039)

