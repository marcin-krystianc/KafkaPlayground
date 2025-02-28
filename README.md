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

### Idempotent producer can oocassionaly get stuck when acquiring idempotence PID (https://github.com/confluentinc/librdkafka/issues/3848)

### Producer can fail with "Unknown topic or partition" (https://github.com/confluentinc/librdkafka/issues/4401)

### Exactly once delivery in C#/Python/C++. 

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
/dotnet/dotnet /workspace/KafkaPlayground/dotnet/KafkaTool/bin/Debug/net8.0/KafkaTool.dll \
producer-consumer \
--config allow.auto.create.topics=false \
--config bootstrap.servers=localhost:19092 \
--topics=20 \
--partitions=20 \
--replication-factor=3 \
--min-isr=2 \
--messages-per-second=150 \
--config request.required.acks=-1 \
--config enable.idempotence=true \
--config max.in.flight.requests.per.connection=5 \
--config auto.offset.reset=latest \
--config queue.buffering.max.ms=7 \
--config debug=EOS \
--producers=20 \
--recreate-topics=true \
--config security.protocol=sasl_plaintext \
--config sasl.username=superuser \
--config sasl.password=secretpassword \
--config sasl.mechanism=SCRAM-SHA-256 \
```

```
...
09:46:57 fail: Consumer:[0] Unexpected message value, topic/k [p]=my-topic-163/16 [9], Offset=2004/2005, LeaderEpoch=1/2,  previous value=182, messageValue=182!
09:46:57 fail: Consumer:[0] Unexpected message value, topic/k [p]=my-topic-123/6 [4], Offset=1456/1457, LeaderEpoch=1/2,  previous value=182, messageValue=182!
09:47:03 info: Log[0] Elapsed: 310s, 2880380 (+92820) messages produced, 2871183 (+141581) messages consumed, 2 duplicated, 0 out of sequence.
09:47:13 info: Log[0] Elapsed: 320s, 2972422 (+92042) messages produced, 2972230 (+101047) messages consumed, 2 duplicated, 0 out of sequence.
```

### Stack overflow in C++/C#/Python (librdkafka v2.4.0 (2.3.0 is ok)) (https://github.com/confluentinc/librdkafka/issues/4778, https://github.com/confluentinc/librdkafka/pull/4864):
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

### Java Producer fails with "Error: Local: Inconsistent state" in C++/C#/Python (librdkafka v2.3.0 + rolling restarts):
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

### Producer: expiring messages without delivery error report ?

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

### Assertions in a debug build 
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


### Poor librdkafka performance on test corporate cluster + rolling restarts (java works better)
- librdkafka with `max.in.flight.requests.per.connection=1` appears to be really slow (occasionally I see msg timeout errors) 
- Relevant parameters:
- I think that number of topics plays important role in this problem, with fewer topics that problem dissapeared
```
--config request.required.acks=-1 
--config enable.idempotence=false 
--config max.in.flight.requests.per.connection=1
--replication-factor=3 
--min-isr=2 
--producers=1 
--messages-per-second=10000
```

- .NET
```
dotnet run --project KafkaTool.csproj producer-consumer --config allow.auto.create.topics=false --config bootstrap.servers=cvvkafka-1.g1.ospr-kas-d.wl.vgis.c3.zone:6669,cvvkafka-1.g2.ospr-kas-d.wl.vgis.c3.zone:6669,cvvkafka-1.g3.ospr-kas-d.wl.vgis.c3.zone:6669,cvvkafka-1.g4.ospr-kas-d.wl.vgis.c3.zone:6669 --config request.timeout.ms=180000 --config message.timeout.ms=180000 --config request.required.acks=-1 --config enable.idempotence=false --config max.in.flight.requests.per.connection=1 --config security.protocol=SASL_SSL --config ssl.ca.location=/etc/ssl/certs/ca-bundle.crt --config sasl.mechanism=GSSAPI --config sasl.kerberos.keytab=foo-bar --config sasl.kerberos.min.time.before.relogin=0 --topics=500 --partitions=6 --replication-factor=3 --min-isr=2 --producers=1 --messages-per-second=10000 --recreate-topics-delay=10000 --recreate-topics-batch-size=500 --topic-stem=oss.my-A-topic
Using assembly:Confluent.Kafka, Version=2.1.1.0, Culture=neutral, PublicKeyToken=12c514ca49093d1em location:/persist-shared/KafkaPlayground-master/dotnet/bin/Debug/net8.0/Confluent.Kafka.dll
librdkafka Version: 2.1.1 (20101FF)
Debug Contexts: all, generic, broker, topic, metadata, feature, queue, msg, protocol, cgrp, security, fetch, interceptor, plugin, consumer, admin, eos, mock, assignor, conf
10:35:45 info: Log[0] Removing 500 topics
10:35:55 info: Log[0] Creating 500 topics
10:36:06 info: Producer0:[0] Starting producer task:
10:36:06 info: Consumer:[0] Starting consumer task:
10:36:06 info: Producer0:[0] kafka-log Facility:FAIL, Message[thrd:sasl_ssl://cvvkafka-1.g2.ospr-kas-d.wl.vgis.c3.zone:6669/bootst]: sasl_ssl://cvvkafka-1.g2.ospr-kas-d.wl.vgis.c3.zone:6669/bootstrap: Connect to ipv4#10.4.102.205:6669 failed: Connection refused (after 2ms in state CONNECT)
10:36:06 fail: Producer0:[0] Producer error: reason=sasl_ssl://cvvkafka-1.g2.ospr-kas-d.wl.vgis.c3.zone:6669/bootstrap: Connect to ipv4#10.4.102.205:6669 failed: Connection refused (after 2ms in state CONNECT), IsLocal=True, IsBroker=False, IsFatal=False, IsCode=Local_Transport
10:36:06 info: Consumer:[0] Consumer log: message=[thrd:app]: Configuration property enable.idempotence is a producer property and will be ignored by this consumer instance, name=rdkafka#consumer-3, facility=CONFWARN, level=Warning
10:36:06 info: Consumer:[0] Consumer log: message=[thrd:app]: Configuration property request.required.acks is a producer property and will be ignored by this consumer instance, name=rdkafka#consumer-3, facility=CONFWARN, level=Warning
10:36:06 info: Consumer:[0] Consumer log: message=[thrd:app]: Configuration property request.timeout.ms is a producer property and will be ignored by this consumer instance, name=rdkafka#consumer-3, facility=CONFWARN, level=Warning
10:36:06 info: Consumer:[0] Consumer log: message=[thrd:app]: Configuration property message.timeout.ms is a producer property and will be ignored by this consumer instance, name=rdkafka#consumer-3, facility=CONFWARN, level=Warning
10:36:06 info: Consumer:[0] Consumer log: message=[thrd:sasl_ssl://cvvkafka-1.g2.ospr-kas-d.wl.vgis.c3.zone:6669/bootst]: sasl_ssl://cvvkafka-1.g2.ospr-kas-d.wl.vgis.c3.zone:6669/bootstrap: Connect to ipv4#10.4.102.205:6669 failed: Connection refused (after 10ms in state CONNECT), name=rdkafka#consumer-3, facility=FAIL, level=Error
10:36:06 fail: Consumer:[0] Consumer error: reason=sasl_ssl://cvvkafka-1.g2.ospr-kas-d.wl.vgis.c3.zone:6669/bootstrap: Connect to ipv4#10.4.102.205:6669 failed: Connection refused (after 10ms in state CONNECT), IsLocal=True, IsBroker=False, IsFatal=False, IsCode=Local_Transport
10:36:16 info: Log[0] Elapsed: 10s, 98000 (+98000) messages produced, 3686 (+3686) messages consumed, 0 duplicated, 0 out of sequence.
10:36:26 info: Log[0] Elapsed: 20s, 100000 (+2000) messages produced, 19050 (+15364) messages consumed, 0 duplicated, 0 out of sequence.
10:36:36 info: Log[0] Elapsed: 30s, 100000 (+0) messages produced, 34363 (+15313) messages consumed, 0 duplicated, 0 out of sequence.
10:36:46 info: Log[0] Elapsed: 40s, 100000 (+0) messages produced, 48830 (+14467) messages consumed, 0 duplicated, 0 out of sequence.
10:36:56 info: Log[0] Elapsed: 50s, 100000 (+0) messages produced, 61277 (+12447) messages consumed, 0 duplicated, 0 out of sequence.
10:37:06 info: Log[0] Elapsed: 60s, 100000 (+0) messages produced, 76520 (+15243) messages consumed, 0 duplicated, 0 out of sequence.
10:37:16 info: Log[0] Elapsed: 70s, 100000 (+0) messages produced, 87272 (+10752) messages consumed, 0 duplicated, 0 out of sequence.
10:37:26 info: Log[0] Elapsed: 80s, 100000 (+0) messages produced, 92178 (+4906) messages consumed, 0 duplicated, 0 out of sequence.
10:37:36 info: Log[0] Elapsed: 90s, 100000 (+0) messages produced, 96557 (+4379) messages consumed, 0 duplicated, 0 out of sequence.
10:37:46 info: Log[0] Elapsed: 100s, 112000 (+12000) messages produced, 100440 (+3883) messages consumed, 0 duplicated, 0 out of sequence.
10:37:56 info: Log[0] Elapsed: 110s, 200000 (+88000) messages produced, 110778 (+10338) messages consumed, 0 duplicated, 0 out of sequence.
10:38:06 info: Log[0] Elapsed: 120s, 200000 (+0) messages produced, 126088 (+15310) messages consumed, 0 duplicated, 0 out of sequence.
10:38:16 info: Log[0] Elapsed: 130s, 200000 (+0) messages produced, 141683 (+15595) messages consumed, 0 duplicated, 0 out of sequence.
10:38:26 info: Log[0] Elapsed: 140s, 200000 (+0) messages produced, 157470 (+15787) messages consumed, 0 duplicated, 0 out of sequence.
10:38:36 info: Log[0] Elapsed: 150s, 200000 (+0) messages produced, 172185 (+14715) messages consumed, 0 duplicated, 0 out of sequence.
10:38:46 info: Log[0] Elapsed: 160s, 200000 (+0) messages produced, 184233 (+12048) messages consumed, 0 duplicated, 0 out of sequence.
10:38:56 info: Log[0] Elapsed: 170s, 200000 (+0) messages produced, 188274 (+4041) messages consumed, 0 duplicated, 0 out of sequence.
10:39:06 info: Log[0] Elapsed: 180s, 200000 (+0) messages produced, 192966 (+4692) messages consumed, 0 duplicated, 0 out of sequence.
10:39:16 info: Log[0] Elapsed: 190s, 200000 (+0) messages produced, 197568 (+4602) messages consumed, 0 duplicated, 0 out of sequence.
10:39:26 info: Log[0] Elapsed: 200s, 243000 (+43000) messages produced, 201804 (+4236) messages consumed, 0 duplicated, 0 out of sequence.
10:39:36 info: Log[0] Elapsed: 210s, 300000 (+57000) messages produced, 215221 (+13417) messages consumed, 0 duplicated, 0 out of sequence.
10:39:46 info: Log[0] Elapsed: 220s, 300000 (+0) messages produced, 230312 (+15091) messages consumed, 0 duplicated, 0 out of sequence.
10:39:56 info: Log[0] Elapsed: 230s, 300000 (+0) messages produced, 245048 (+14736) messages consumed, 0 duplicated, 0 out of sequence.
10:40:06 info: Log[0] Elapsed: 240s, 300000 (+0) messages produced, 259808 (+14760) messages consumed, 0 duplicated, 0 out of sequence.
10:40:16 info: Log[0] Elapsed: 250s, 300000 (+0) messages produced, 274065 (+14257) messages consumed, 0 duplicated, 0 out of sequence.
10:40:26 info: Log[0] Elapsed: 260s, 300000 (+0) messages produced, 282557 (+8492) messages consumed, 0 duplicated, 0 out of sequence.
```
- Java
```
mvn package; mvn exec:java -Djava.security.krb5.conf=/etc/krb5.conf "-Dexec.mainClass=kafka.testing.Main" "$(cat <<EOF | tr '\n' ' ' | sed 's/ *$//'
"-Dexec.args=producer-consumer
--config allow.auto.create.topics=false
--config bootstrap.servers=cvvkafka-1.g1.ospr-kas-d.wl.vgis.c3.zone:6669,cvvkafka-1.g2.ospr-kas-d.wl.vgis.c3.zone:6669,cvvkafka-1.g3.ospr-kas-d.wl.vgis.c3.zone:6669,cvvkafka-1.g4.ospr-kas-d.wl.vgis.c3.zone:6669
--config request.timeout.ms=180000
--config message.timeout.ms=180000
--config request.required.acks=-1
--config enable.idempotence=false
--config max.in.flight.requests.per.connection=1
--config security.protocol=SASL_SSL
--config sasl.mechanism=GSSAPI
--config sasl.kerberos.min.time.before.relogin=0
--config sasl.kerberos.service.name=kafka
--config sasl.jaas.config="com.sun.security.auth.module.Krb5LoginModule required useTicketCache=true debug=true;"
--topics=500
--partitions=6
--replication-factor=3
--min-isr=2
--producers=1
--messages-per-second=10000
--recreate-topics-delay=10000
--recreate-topics-batch-size=500
--topic-stem=oss.my-C-topic"
EOF
)"
[INFO] Scanning for projects...
[INFO]
[INFO] ----------------------< kafka.testing:KafkaTool >-----------------------
[INFO] Building KafkaTool 1.0-SNAPSHOT
[INFO] --------------------------------[ jar ]---------------------------------
[INFO]
[INFO] --- maven-resources-plugin:2.6:resources (default-resources) @ KafkaTool ---
[WARNING] Using platform encoding (UTF-8 actually) to copy filtered resources, i.e. build is platform dependent!
[INFO] Copying 2 resources
[INFO]
[INFO] --- maven-compiler-plugin:3.1:compile (default-compile) @ KafkaTool ---
[INFO] Nothing to compile - all classes are up to date
[INFO]
[INFO] --- maven-resources-plugin:2.6:testResources (default-testResources) @ KafkaTool ---
[WARNING] Using platform encoding (UTF-8 actually) to copy filtered resources, i.e. build is platform dependent!
[INFO] skip non existing resourceDirectory /persist-shared/KafkaPlayground-master/java/KafkaTool/src/test/resources
[INFO]
[INFO] --- maven-compiler-plugin:3.1:testCompile (default-testCompile) @ KafkaTool ---
[INFO] No sources to compile
[INFO]
[INFO] --- maven-surefire-plugin:2.12.4:test (default-test) @ KafkaTool ---
[INFO] No tests to run.
[INFO]
[INFO] --- maven-jar-plugin:2.4:jar (default-jar) @ KafkaTool ---
[INFO] ------------------------------------------------------------------------
[INFO] BUILD SUCCESS
[INFO] ------------------------------------------------------------------------
[INFO] Total time:  0.811 s
[INFO] Finished at: 2024-09-26T10:35:49+01:00
[INFO] ------------------------------------------------------------------------
[INFO] Scanning for projects...
[INFO]
[INFO] ----------------------< kafka.testing:KafkaTool >-----------------------
[INFO] Building KafkaTool 1.0-SNAPSHOT
[INFO] --------------------------------[ jar ]---------------------------------
[INFO]
[INFO] --- exec-maven-plugin:3.4.1:java (default-cli) @ KafkaTool ---
Debug is  true storeKey false useTicketCache true useKeyTab false doNotPrompt false ticketCache is null isInitiator true KeyTab is null refreshKrb5Config is false principal is null tryFirstPass is false useFirstPass is false storePass is false clearPass is false
Acquire TGT from Cache
Principal is marcikry320@C3.ZONE
Commit Succeeded

[2024-09-26 10:35:51,664] WARN [AdminClient clientId=client-a8989e13-8518-41d0-9141-f55ed7501b30] Overriding the default value for default.api.timeout.ms (0) with the explicitly configured request timeout 180000 (org.apache.kafka.clients.admin.KafkaAdminClient)
[10:35:52] kafka.testing.Main.main() - Deleted topics
[10:35:52] kafka.testing.Main.main() - Creating 500 topics
[2024-09-26 10:36:02,669] WARN [Principal=null]: TGT renewal thread has been interrupted and will exit. (org.apache.kafka.common.security.kerberos.KerberosLogin)
[2024-09-26 10:36:02,728] WARN [Producer clientId=client-99d334f2-1bd0-4018-8c22-1e7f2a3677da] delivery.timeout.ms should be equal to or larger than linger.ms + request.timeout.ms. Setting it to 180000. (org.apache.kafka.clients.producer.KafkaProducer)
Debug is  true storeKey false useTicketCache true useKeyTab false doNotPrompt false ticketCache is null isInitiator true KeyTab is null refreshKrb5Config is false principal is null tryFirstPass is false useFirstPass is false storePass is false clearPass is false
Acquire TGT from Cache
Principal is marcikry320@C3.ZONE
Commit Succeeded

[2024-09-26 10:36:02,790] WARN [Producer clientId=client-99d334f2-1bd0-4018-8c22-1e7f2a3677da] Connection to node -2 (cvvkafka-1.g2.ospr-kas-d.wl.vgis.c3.zone/10.4.102.205:6669) could not be established. Node may not be available. (org.apache.kafka.clients.NetworkClient)
[2024-09-26 10:36:02,791] WARN [Producer clientId=client-99d334f2-1bd0-4018-8c22-1e7f2a3677da] Bootstrap broker cvvkafka-1.g2.ospr-kas-d.wl.vgis.c3.zone:6669 (id: -2 rack: null) disconnected (org.apache.kafka.clients.NetworkClient)
[2024-09-26 10:36:02,902] WARN [Consumer clientId=client-3a1f744b-252a-4023-9a50-669f79cf008f, groupId=group-cbf231c0-8056-4962-a653-1189f3b69abc] Connection to node -2 (cvvkafka-1.g2.ospr-kas-d.wl.vgis.c3.zone/10.4.102.205:6669) could not be established. Node may not be available. (org.apache.kafka.clients.NetworkClient)
[2024-09-26 10:36:02,902] WARN [Consumer clientId=client-3a1f744b-252a-4023-9a50-669f79cf008f, groupId=group-cbf231c0-8056-4962-a653-1189f3b69abc] Bootstrap broker cvvkafka-1.g2.ospr-kas-d.wl.vgis.c3.zone:6669 (id: -2 rack: null) disconnected (org.apache.kafka.clients.NetworkClient)
[10:36:03] consumer - Assigned partitions: 3000
[10:36:12] reporter - Elapsed: 10s, Produced: 15602 (+15602), Consumed: 10549 (+10549), Duplicated: 0, Out of sequence: 0.
[10:36:22] reporter - Elapsed: 20s, Produced: 75661 (+60059), Consumed: 72719 (+62170), Duplicated: 0, Out of sequence: 0.
[10:36:32] reporter - Elapsed: 30s, Produced: 152021 (+76360), Consumed: 150133 (+77414), Duplicated: 0, Out of sequence: 0.
[10:36:42] reporter - Elapsed: 40s, Produced: 202137 (+50116), Consumed: 199706 (+49573), Duplicated: 0, Out of sequence: 0.
[10:36:52] reporter - Elapsed: 50s, Produced: 256994 (+54857), Consumed: 254946 (+55240), Duplicated: 0, Out of sequence: 0.
[10:37:02] reporter - Elapsed: 60s, Produced: 333262 (+76268), Consumed: 330883 (+75937), Duplicated: 0, Out of sequence: 0.
[10:37:12] reporter - Elapsed: 70s, Produced: 402578 (+69316), Consumed: 400742 (+69859), Duplicated: 0, Out of sequence: 0.
[10:37:22] reporter - Elapsed: 80s, Produced: 480836 (+78258), Consumed: 478382 (+77640), Duplicated: 0, Out of sequence: 0.
[10:37:32] reporter - Elapsed: 90s, Produced: 561819 (+80983), Consumed: 559601 (+81219), Duplicated: 0, Out of sequence: 0.
[10:37:42] reporter - Elapsed: 100s, Produced: 645702 (+83883), Consumed: 643709 (+84108), Duplicated: 0, Out of sequence: 0.
[10:37:52] reporter - Elapsed: 110s, Produced: 728377 (+82675), Consumed: 726806 (+83097), Duplicated: 0, Out of sequence: 0.
[10:38:02] reporter - Elapsed: 120s, Produced: 815865 (+87488), Consumed: 814103 (+87297), Duplicated: 0, Out of sequence: 0.
[10:38:12] reporter - Elapsed: 130s, Produced: 894853 (+78988), Consumed: 891557 (+77454), Duplicated: 0, Out of sequence: 0.
[10:38:22] reporter - Elapsed: 140s, Produced: 983894 (+89041), Consumed: 981350 (+89793), Duplicated: 0, Out of sequence: 0.
[2024-09-26 10:38:32,534] WARN [Producer clientId=client-99d334f2-1bd0-4018-8c22-1e7f2a3677da] Got error produce response with correlation id 3873 on topic-partition oss.my-C-topic-146-1, retrying (2147483646 attempts left). Error: NOT_LEADER_OR_FOLLOWER (org.apache.kafka.clients.producer.internals.Sender)
[2024-09-26 10:38:32,536] WARN [Producer clientId=client-99d334f2-1bd0-4018-8c22-1e7f2a3677da] Received invalid metadata error in produce request on partition oss.my-C-topic-146-1 due to org.apache.kafka.common.errors.NotLeaderOrFollowerException: For requests intended only for the leader, this error indicates that the broker is not the current leader. For requests intended for any replica, this error indicates that the broker is not a replica of the topic partition.. Going to request metadata update now (org.apache.kafka.clients.producer.internals.Sender)
[2024-09-26 10:38:32,543] WARN [Producer clientId=client-99d334f2-1bd0-4018-8c22-1e7f2a3677da] Got error produce response with correlation id 3873 on topic-partition oss.my-C-topic-176-1, retrying (2147483646 attempts left). Error: NOT_LEADER_OR_FOLLOWER (org.apache.kafka.clients.producer.internals.Sender)
[2024-09-26 10:38:32,544] WARN [Producer clientId=client-99d334f2-1bd0-4018-8c22-1e7f2a3677da] Received invalid metadata error in produce request on partition oss.my-C-topic-176-1 due to org.apache.kafka.common.errors.NotLeaderOrFollowerException: For requests intended only for the leader, this error indicates that the broker is not the current leader. For requests intended for any replica, this error indicates that the broker is not a replica of the topic partition.. Going to request metadata update now (org.apache.kafka.clients.producer.internals.Sender)
[2024-09-26 10:38:32,714] WARN [Producer clientId=client-99d334f2-1bd0-4018-8c22-1e7f2a3677da] Got error produce response with correlation id 3874 on topic-partition oss.my-C-topic-193-2, retrying (2147483646 attempts left). Error: NOT_LEADER_OR_FOLLOWER (org.apache.kafka.clients.producer.internals.Sender)
[2024-09-26 10:38:32,714] WARN [Producer clientId=client-99d334f2-1bd0-4018-8c22-1e7f2a3677da] Received invalid metadata error in produce request on partition oss.my-C-topic-193-2 due to org.apache.kafka.common.errors.NotLeaderOrFollowerException: For requests intended only for the leader, this error indicates that the broker is not the current leader. For requests intended for any replica, this error indicates that the broker is not a replica of the topic partition.. Going to request metadata update now (org.apache.kafka.clients.producer.internals.Sender)
[2024-09-26 10:38:32,722] WARN [Producer clientId=client-99d334f2-1bd0-4018-8c22-1e7f2a3677da] Got error produce response with correlation id 3874 on topic-partition oss.my-C-topic-25-3, retrying (2147483646 attempts left). Error: NOT_LEADER_OR_FOLLOWER (org.apache.kafka.clients.producer.internals.Sender)
[10:38:42] reporter - Elapsed: 160s, Produced: 1134106 (+69453), Consumed: 1132437 (+69965), Duplicated: 0, Out of sequence: 0.
[10:38:52] reporter - Elapsed: 170s, Produced: 1203694 (+69588), Consumed: 1201315 (+68878), Duplicated: 0, Out of sequence: 0.
[10:39:02] reporter - Elapsed: 180s, Produced: 1285000 (+81306), Consumed: 1282449 (+81134), Duplicated: 0, Out of sequence: 0.
[10:39:12] reporter - Elapsed: 190s, Produced: 1355996 (+70996), Consumed: 1354363 (+71914), Duplicated: 0, Out of sequence: 0.
[10:39:22] reporter - Elapsed: 200s, Produced: 1430000 (+74004), Consumed: 1427399 (+73036), Duplicated: 0, Out of sequence: 0.
[10:39:32] reporter - Elapsed: 210s, Produced: 1505062 (+75062), Consumed: 1503215 (+75816), Duplicated: 0, Out of sequence: 0.
[10:39:42] reporter - Elapsed: 220s, Produced: 1584475 (+79413), Consumed: 1582379 (+79164), Duplicated: 0, Out of sequence: 0.
[10:39:52] reporter - Elapsed: 230s, Produced: 1665000 (+80525), Consumed: 1662825 (+80446), Duplicated: 0, Out of sequence: 0.
```

### Librdkafka producer-consumer, consumer silently stops consuming
- veresion
```
librdkafka Version: 2.1.1 (20101FF)
```
- symptoms
```
10:05:23 info: Log[0] Elapsed: 434169s, 433970800 (+10000) messages produced, 212215134 (+0) messages consumed, 4142 duplicated, 112 out of sequence.
```
- logs
```
23:56:48 info: Log[0] Elapsed: 224854s, 224760700 (+10000) messages produced, 212213110 (+64) messages consumed, 4142 duplicated, 112 out of sequence.
23:56:54 info: Consumer:[0] Consumer log: message=[thrd:sasl_ssl://cvvkafka-1.g4.ospr-kas-d.wl.vgis.c3.zone:6669/bootst]: sasl_ssl://cvvkafka-1.g4.ospr-kas-d.wl.vgis.c3.zone:6669/14001: Timed out 0 in-flight, 0 retry-queued, 1197 out-queue, 0 partially-sent requests, name=rdkafka#consumer-3, facility=REQTMOUT, level=Warning
23:56:54 info: Consumer:[0] Consumer log: message=[thrd:sasl_ssl://cvvkafka-1.g4.ospr-kas-d.wl.vgis.c3.zone:6669/bootst]: sasl_ssl://cvvkafka-1.g4.ospr-kas-d.wl.vgis.c3.zone:6669/14001: 1197 request(s) timed out: disconnect (after 60900ms in state UP), name=rdkafka#consumer-3, facility=FAIL, level=Error
23:56:54 fail: Consumer:[0] Consumer error: reason=sasl_ssl://cvvkafka-1.g4.ospr-kas-d.wl.vgis.c3.zone:6669/14001: 1197 request(s) timed out: disconnect (after 60900ms in state UP), IsLocal=True, IsBroker=False, IsFatal=False, IsCode=Local_TimedOut
23:56:58 info: Log[0] Elapsed: 224864s, 224770700 (+10000) messages produced, 212213166 (+56) messages consumed, 4142 duplicated, 112 out of sequence.
23:57:08 info: Log[0] Elapsed: 224874s, 224780700 (+10000) messages produced, 212213206 (+40) messages consumed, 4142 duplicated, 112 out of sequence.
23:57:18 info: Log[0] Elapsed: 224884s, 224790700 (+10000) messages produced, 212213214 (+8) messages consumed, 4142 duplicated, 112 out of sequence.
23:57:22 fail: Producer0:[0] Producer error: reason=sasl_ssl://cvvkafka-1.g4.ospr-kas-d.wl.vgis.c3.zone:6669/14001: Disconnected (after 367014ms in state UP, 1 identical error(s) suppressed), IsLocal=True, IsBroker=False, IsFatal=False, IsCode=Local_Transport
23:57:22 info: Consumer:[0] Consumer log: message=[thrd:sasl_ssl://cvvkafka-1.g4.ospr-kas-d.wl.vgis.c3.zone:6669/bootst]: sasl_ssl://cvvkafka-1.g4.ospr-kas-d.wl.vgis.c3.zone:6669/14001: Disconnected (after 27906ms in state UP), name=rdkafka#consumer-3, facility=FAIL, level=Info
23:57:22 fail: Consumer:[0] Consumer error: reason=sasl_ssl://cvvkafka-1.g4.ospr-kas-d.wl.vgis.c3.zone:6669/14001: Disconnected (after 27906ms in state UP), IsLocal=True, IsBroker=False, IsFatal=False, IsCode=Local_Transport
23:57:22 info: Consumer:[0] Consumer log: message=[thrd:sasl_ssl://cvvkafka-1.g4.ospr-kas-d.wl.vgis.c3.zone:6669/bootst]: sasl_ssl://cvvkafka-1.g4.ospr-kas-d.wl.vgis.c3.zone:6669/14001: Connect to ipv4#10.4.102.248:6669 failed: Connection refused (after 2ms in state CONNECT), name=rdkafka#consumer-3, facility=FAIL, level=Error
23:57:22 fail: Consumer:[0] Consumer error: reason=sasl_ssl://cvvkafka-1.g4.ospr-kas-d.wl.vgis.c3.zone:6669/14001: Connect to ipv4#10.4.102.248:6669 failed: Connection refused (after 2ms in state CONNECT), IsLocal=True, IsBroker=False, IsFatal=False, IsCode=Local_Transport
23:57:23 info: Consumer:[0] Consumer log: message=[thrd:sasl_ssl://cvvkafka-1.g4.ospr-kas-d.wl.vgis.c3.zone:6669/bootst]: sasl_ssl://cvvkafka-1.g4.ospr-kas-d.wl.vgis.c3.zone:6669/14001: Connect to ipv4#10.4.102.248:6669 failed: Connection refused (after 0ms in state CONNECT, 1 identical error(s) suppressed), name=rdkafka#consumer-3, facility=FAIL, level=Error
23:57:23 fail: Consumer:[0] Consumer error: reason=sasl_ssl://cvvkafka-1.g4.ospr-kas-d.wl.vgis.c3.zone:6669/14001: Connect to ipv4#10.4.102.248:6669 failed: Connection refused (after 0ms in state CONNECT, 1 identical error(s) suppressed), IsLocal=True, IsBroker=False, IsFatal=False, IsCode=Local_Transport
23:57:28 info: Log[0] Elapsed: 224894s, 224800700 (+10000) messages produced, 212213214 (+0) messages consumed, 4142 duplicated, 112 out of sequence.
23:57:38 info: Log[0] Elapsed: 224904s, 224810700 (+10000) messages produced, 212213342 (+128) messages consumed, 4142 duplicated, 112 out of sequence.
23:57:48 info: Log[0] Elapsed: 224914s, 224820700 (+10000) messages produced, 212213342 (+0) messages consumed, 4142 duplicated, 112 out of sequence.
23:57:56 info: Consumer:[0] Consumer log: message=[thrd:sasl_ssl://cvvkafka-1.g4.ospr-kas-d.wl.vgis.c3.zone:6669/bootst]: sasl_ssl://cvvkafka-1.g4.ospr-kas-d.wl.vgis.c3.zone:6669/14001: Connect to ipv4#10.4.102.248:6669 failed: Connection refused (after 1ms in state CONNECT, 5 identical error(s) suppressed), name=rdkafka#consumer-3, facility=FAIL, level=Error
23:57:56 fail: Consumer:[0] Consumer error: reason=sasl_ssl://cvvkafka-1.g4.ospr-kas-d.wl.vgis.c3.zone:6669/14001: Connect to ipv4#10.4.102.248:6669 failed: Connection refused (after 1ms in state CONNECT, 5 identical error(s) suppressed), IsLocal=True, IsBroker=False, IsFatal=False, IsCode=Local_Transport
23:57:58 info: Log[0] Elapsed: 224924s, 224830700 (+10000) messages produced, 212213342 (+0) messages consumed, 4142 duplicated, 112 out of sequence.
23:58:08 info: Log[0] Elapsed: 224934s, 224840700 (+10000) messages produced, 212213342 (+0) messages consumed, 4142 duplicated, 112 out of sequence.
23:58:18 info: Log[0] Elapsed: 224944s, 224850700 (+10000) messages produced, 212213342 (+0) messages consumed, 4142 duplicated, 112 out of sequence.
23:58:19 info: Consumer:[0] Consumer log: message=[thrd:sasl_ssl://cvvkafka-1.g2.ospr-kas-d.wl.vgis.c3.zone:6669/bootst]: sasl_ssl://cvvkafka-1.g2.ospr-kas-d.wl.vgis.c3.zone:6669/12001: Timed out 0 in-flight, 0 retry-queued, 3746 out-queue, 0 partially-sent requests, name=rdkafka#consumer-3, facility=REQTMOUT, level=Warning
23:58:19 info: Consumer:[0] Consumer log: message=[thrd:sasl_ssl://cvvkafka-1.g2.ospr-kas-d.wl.vgis.c3.zone:6669/bootst]: sasl_ssl://cvvkafka-1.g2.ospr-kas-d.wl.vgis.c3.zone:6669/12001: 3746 request(s) timed out: disconnect (after 170884ms in state UP), name=rdkafka#consumer-3, facility=FAIL, level=Error
23:58:19 fail: Consumer:[0] Consumer error: reason=sasl_ssl://cvvkafka-1.g2.ospr-kas-d.wl.vgis.c3.zone:6669/12001: 3746 request(s) timed out: disconnect (after 170884ms in state UP), IsLocal=True, IsBroker=False, IsFatal=False, IsCode=Local_TimedOut
23:58:22 info: Consumer:[0] Consumer log: message=[thrd:sasl_ssl://cvvkafka-1.g3.ospr-kas-d.wl.vgis.c3.zone:6669/bootst]: sasl_ssl://cvvkafka-1.g3.ospr-kas-d.wl.vgis.c3.zone:6669/13001: Timed out 0 in-flight, 0 retry-queued, 650 out-queue, 0 partially-sent requests, name=rdkafka#consumer-3, facility=REQTMOUT, level=Warning
23:58:22 info: Consumer:[0] Consumer log: message=[thrd:sasl_ssl://cvvkafka-1.g3.ospr-kas-d.wl.vgis.c3.zone:6669/bootst]: sasl_ssl://cvvkafka-1.g3.ospr-kas-d.wl.vgis.c3.zone:6669/13001: 650 request(s) timed out: disconnect (after 75685ms in state UP), name=rdkafka#consumer-3, facility=FAIL, level=Error
23:58:22 fail: Consumer:[0] Consumer error: reason=sasl_ssl://cvvkafka-1.g3.ospr-kas-d.wl.vgis.c3.zone:6669/13001: 650 request(s) timed out: disconnect (after 75685ms in state UP), IsLocal=True, IsBroker=False, IsFatal=False, IsCode=Local_TimedOut
23:58:28 info: Log[0] Elapsed: 224954s, 224860700 (+10000) messages produced, 212213342 (+0) messages consumed, 4142 duplicated, 112 out of sequence.
23:58:36 info: Consumer:[0] Consumer log: message=[thrd:sasl_ssl://cvvkafka-1.g4.ospr-kas-d.wl.vgis.c3.zone:6669/bootst]: sasl_ssl://cvvkafka-1.g4.ospr-kas-d.wl.vgis.c3.zone:6669/14001: Connect to ipv4#10.4.102.248:6669 failed: Connection refused (after 1ms in state CONNECT, 4 identical error(s) suppressed), name=rdkafka#consumer-3, facility=FAIL, level=Error
23:58:36 fail: Consumer:[0] Consumer error: reason=sasl_ssl://cvvkafka-1.g4.ospr-kas-d.wl.vgis.c3.zone:6669/14001: Connect to ipv4#10.4.102.248:6669 failed: Connection refused (after 1ms in state CONNECT, 4 identical error(s) suppressed), IsLocal=True, IsBroker=False, IsFatal=False, IsCode=Local_Transport
23:58:38 info: Log[0] Elapsed: 224964s, 224870700 (+10000) messages produced, 212213342 (+0) messages consumed, 4142 duplicated, 112 out of sequence.
23:58:48 info: Log[0] Elapsed: 224974s, 224880700 (+10000) messages produced, 212213342 (+0) messages consumed, 4142 duplicated, 112 out of sequence.
23:58:58 info: Log[0] Elapsed: 224984s, 224890700 (+10000) messages produced, 212213838 (+496) messages consumed, 4142 duplicated, 112 out of sequence.
23:59:08 info: Log[0] Elapsed: 224994s, 224900700 (+10000) messages produced, 212213838 (+0) messages consumed, 4142 duplicated, 112 out of sequence.
```

# another kafka issue
no kafka parititioning, just manual partitioning
no keys, payload 100 bytes, up to 500 partitions (10 partions on average)
replicate latency issues with c#, java
what is the average latency at 400k msg/s

I think it is caused by low conumption rate in librdkafka:
java:
```
[08:55:33] reporter - Elapsed: 2s, Produced: 0 (+0, p95=-100ms), Consumed: 149379 (+149379, p95=912197ms)), Duplicated: 0, Out of sequence: 0.
[08:55:34] reporter - Elapsed: 3s, Produced: 0 (+0, p95=-100ms), Consumed: 1198953 (+1049574, p95=911855ms)), Duplicated: 0, Out of sequence: 0.
[08:55:35] reporter - Elapsed: 4s, Produced: 0 (+0, p95=-100ms), Consumed: 2547815 (+1348862, p95=910575ms)), Duplicated: 0, Out of sequence: 0.
[08:55:36] reporter - Elapsed: 5s, Produced: 0 (+0, p95=-100ms), Consumed: 3696804 (+1148989, p95=909665ms)), Duplicated: 0, Out of sequence: 0.
[08:55:37] reporter - Elapsed: 6s, Produced: 0 (+0, p95=-100ms), Consumed: 5545188 (+1848384, p95=906255ms)), Duplicated: 0, Out of sequence: 0.
[08:55:38] reporter - Elapsed: 7s, Produced: 0 (+0, p95=-100ms), Consumed: 7570472 (+2025284, p95=900835ms)), Duplicated: 0, Out of sequence: 0.
[08:55:39] reporter - Elapsed: 8s, Produced: 0 (+0, p95=-100ms), Consumed: 9692847 (+2122375, p95=894687ms)), Duplicated: 0, Out of sequence: 0.
[08:55:40] reporter - Elapsed: 9s, Produced: 0 (+0, p95=-100ms), Consumed: 11940705 (+2247858, p95=888493ms)), Duplicated: 0, Out of sequence: 0.
[08:55:41] reporter - Elapsed: 10s, Produced: 0 (+0, p95=-100ms), Consumed: 13947004 (+2006299, p95=881882ms)), Duplicated: 0, Out of sequence: 0.
[08:55:42] reporter - Elapsed: 11s, Produced: 0 (+0, p95=-100ms), Consumed: 16500729 (+2553725, p95=876317ms)), Duplicated: 0, Out of sequence: 0.
[08:55:43] reporter - Elapsed: 12s, Produced: 0 (+0, p95=-100ms), Consumed: 18924809 (+2424080, p95=868967ms)), Duplicated: 0, Out of sequence: 0.
[08:55:44] reporter - Elapsed: 13s, Produced: 0 (+0, p95=-100ms), Consumed: 21498265 (+2573456, p95=861946ms)), Duplicated: 0, Out of sequence: 0.
[08:55:45] reporter - Elapsed: 14s, Produced: 0 (+0, p95=-100ms), Consumed: 23902543 (+2404278, p95=854746ms)), Duplicated: 0, Out of sequence: 0.
[08:55:46] reporter - Elapsed: 15s, Produced: 0 (+0, p95=-100ms), Consumed: 26006798 (+2104255, p95=847899ms)), Duplicated: 0, Out of sequence: 0.
[08:55:47] reporter - Elapsed: 16s, Produced: 0 (+0, p95=-100ms), Consumed: 28275893 (+2269095, p95=841959ms)), Duplicated: 0, Out of sequence: 0.
[08:55:48] reporter - Elapsed: 17s, Produced: 0 (+0, p95=-100ms), Consumed: 30973630 (+2697737, p95=835651ms)), Duplicated: 0, Out of sequence: 0.
[08:55:49] reporter - Elapsed: 18s, Produced: 0 (+0, p95=-100ms), Consumed: 33369606 (+2395976, p95=827498ms)), Duplicated: 0, Out of sequence: 0.
[08:55:50] reporter - Elapsed: 19s, Produced: 0 (+0, p95=-100ms), Consumed: 35907891 (+2538285, p95=820334ms)), Duplicated: 0, Out of sequence: 0.
[08:55:51] reporter - Elapsed: 20s, Produced: 0 (+0, p95=-100ms), Consumed: 38225378 (+2317487, p95=813052ms)), Duplicated: 0, Out of sequence: 0.
```

librdkafka,.NET:
```
08:57:33 info: Consumer:[0] Consumer log: PartitionsAssignedHandler: count=1
08:57:34 info: Log[0] Elapsed: 1s, 0 (+0, p95=-100ms) messages produced, 0 (+0, p95=-100ms) messages consumed, 0 duplicated, 0 out of sequence.
08:57:35 info: Log[0] Elapsed: 2s, 0 (+0, p95=-100ms) messages produced, 114445 (+114445, p95=1033678ms) messages consumed, 0 duplicated, 0 out of sequence.
08:57:36 info: Log[0] Elapsed: 3s, 0 (+0, p95=-100ms) messages produced, 405369 (+290924, p95=1033840ms) messages consumed, 0 duplicated, 0 out of sequence.
08:57:37 info: Log[0] Elapsed: 4s, 0 (+0, p95=-100ms) messages produced, 720853 (+315484, p95=1034127ms) messages consumed, 0 duplicated, 0 out of sequence.
08:57:38 info: Log[0] Elapsed: 5s, 0 (+0, p95=-100ms) messages produced, 1046138 (+325285, p95=1034436ms) messages consumed, 0 duplicated, 0 out of sequence.
08:57:39 info: Log[0] Elapsed: 6s, 0 (+0, p95=-100ms) messages produced, 1371575 (+325437, p95=1034933ms) messages consumed, 0 duplicated, 0 out of sequence.
08:57:40 info: Log[0] Elapsed: 7s, 0 (+0, p95=-100ms) messages produced, 1691479 (+319904, p95=1035523ms) messages consumed, 0 duplicated, 0 out of sequence.
08:57:41 info: Log[0] Elapsed: 8s, 0 (+0, p95=-100ms) messages produced, 1994893 (+303414, p95=1036197ms) messages consumed, 0 duplicated, 0 out of sequence.
08:57:42 info: Log[0] Elapsed: 9s, 0 (+0, p95=-100ms) messages produced, 2295146 (+300253, p95=1036867ms) messages consumed, 0 duplicated, 0 out of sequence.
08:57:43 info: Log[0] Elapsed: 10s, 0 (+0, p95=-100ms) messages produced, 2576475 (+281329, p95=1037289ms) messages consumed, 0 duplicated, 0 out of sequence.
08:57:44 info: Log[0] Elapsed: 11s, 0 (+0, p95=-100ms) messages produced, 2883601 (+307126, p95=1037274ms) messages consumed, 0 duplicated, 0 out of sequence.
08:57:45 info: Log[0] Elapsed: 12s, 0 (+0, p95=-100ms) messages produced, 2987229 (+103628, p95=1037840ms) messages consumed, 0 duplicated, 0 out of sequence.
08:57:46 info: Log[0] Elapsed: 13s, 0 (+0, p95=-100ms) messages produced, 3323026 (+335797, p95=1037820ms) messages consumed, 0 duplicated, 0 out of sequence.
08:57:47 info: Log[0] Elapsed: 14s, 0 (+0, p95=-100ms) messages produced, 3653956 (+330930, p95=1037611ms) messages consumed, 0 duplicated, 0 out of sequence.
```

##############################
on docker

java: 13 mln/s (no message processing, just counting):
```
[14:39:22] reporter - Elapsed: 5s, Produced: 0 (+0, p95=-100ms), Consumed: 3577976 (+3577976, p95=-100ms)), Duplicated: 0, Out of sequence: 0.
[14:39:23] reporter - Elapsed: 6s, Produced: 0 (+0, p95=-100ms), Consumed: 13941848 (+10363872, p95=-100ms)), Duplicated: 0, Out of sequence: 0.
[14:39:24] reporter - Elapsed: 7s, Produced: 0 (+0, p95=-100ms), Consumed: 25623316 (+11681468, p95=-100ms)), Duplicated: 0, Out of sequence: 0.
[14:39:25] reporter - Elapsed: 8s, Produced: 0 (+0, p95=-100ms), Consumed: 38278461 (+12655145, p95=-100ms)), Duplicated: 0, Out of sequence: 0.
[14:39:26] reporter - Elapsed: 9s, Produced: 0 (+0, p95=-100ms), Consumed: 51357652 (+13079191, p95=-100ms)), Duplicated: 0, Out of sequence: 0.
[14:39:27] reporter - Elapsed: 10s, Produced: 0 (+0, p95=-100ms), Consumed: 64317585 (+12959933, p95=-100ms)), Duplicated: 0, Out of sequence: 0.
[14:39:28] reporter - Elapsed: 11s, Produced: 0 (+0, p95=-100ms), Consumed: 77861476 (+13543891, p95=-100ms)), Duplicated: 0, Out of sequence: 0.
[14:39:29] reporter - Elapsed: 12s, Produced: 0 (+0, p95=-100ms), Consumed: 91546915 (+13685439, p95=-100ms)), Duplicated: 0, Out of sequence: 0.
[14:39:30] reporter - Elapsed: 13s, Produced: 0 (+0, p95=-100ms), Consumed: 95456768 (+3909853, p95=-100ms)), Duplicated: 0, Out of sequence: 0.
```

C, librdkafka 3.2 mln/s (no message processing, just counting):
```
elapsed:0.000000, messages: 1
elapsed:1.000000, messages: 2877193
elapsed:2.000000, messages: 5758859
elapsed:3.000000, messages: 8995258
elapsed:4.000000, messages: 12603137
elapsed:5.000000, messages: 16128581
elapsed:6.000000, messages: 19753112
elapsed:7.000000, messages: 23049087
elapsed:8.000000, messages: 25932432
elapsed:9.000000, messages: 29261262
elapsed:10.000000, messages: 32839102
elapsed:11.000000, messages: 36441181
elapsed:12.000000, messages: 39938383
elapsed:13.000000, messages: 43218524
elapsed:14.000000, messages: 46101861
elapsed:15.000000, messages: 49315054
elapsed:16.000000, messages: 53211926
elapsed:17.000000, messages: 56548876
elapsed:18.000000, messages: 59432236
elapsed:19.000000, messages: 62873943
elapsed:20.000000, messages: 66496882
```

C#, librdkafka 1.5 mln/s (no message processing, just counting):
```
:34:40 info: Log[0] Elapsed: 6s, 0 (+0, p95=-100ms) messages produced, 1277309 (+1277309, p95=-100ms) messages consumed, 0 duplicated, 0 out of sequence.
14:34:41 info: Log[0] Elapsed: 7s, 0 (+0, p95=-100ms) messages produced, 2877192 (+1599883, p95=-100ms) messages consumed, 0 duplicated, 0 out of sequence.
14:34:42 info: Log[0] Elapsed: 8s, 0 (+0, p95=-100ms) messages produced, 4352845 (+1475653, p95=-100ms) messages consumed, 0 duplicated, 0 out of sequence.
14:34:43 info: Log[0] Elapsed: 9s, 0 (+0, p95=-100ms) messages produced, 5758858 (+1406013, p95=-100ms) messages consumed, 0 duplicated, 0 out of sequence.
14:34:44 info: Log[0] Elapsed: 10s, 0 (+0, p95=-100ms) messages produced, 7401472 (+1642614, p95=-100ms) messages consumed, 0 duplicated, 0 out of sequence.
14:34:45 info: Log[0] Elapsed: 11s, 0 (+0, p95=-100ms) messages produced, 8669456 (+1267984, p95=-100ms) messages consumed, 0 duplicated, 0 out of sequence.
14:34:46 info: Log[0] Elapsed: 12s, 0 (+0, p95=-100ms) messages produced, 10474059 (+1804603, p95=-100ms) messages consumed, 0 duplicated, 0 out of sequence.
14:34:47 info: Log[0] Elapsed: 13s, 0 (+0, p95=-100ms) messages produced, 11720197 (+1246138, p95=-100ms) messages consumed, 0 duplicated, 0 out of sequence.
14:34:48 info: Log[0] Elapsed: 14s, 0 (+0, p95=-100ms) messages produced, 13535318 (+1815121, p95=-100ms) messages consumed, 0 duplicated, 0 out of sequence.
14:34:49 info: Log[0] Elapsed: 15s, 0 (+0, p95=-100ms) messages produced, 14784051 (+1248733, p95=-100ms) messages consumed, 0 duplicated, 0 out of sequence.
14:34:50 info: Log[0] Elapsed: 16s, 0 (+0, p95=-100ms) messages produced, 16604449 (+1820398, p95=-100ms) messages consumed, 0 duplicated, 0 out of sequence.
14:34:51 info: Log[0] Elapsed: 17s, 0 (+0, p95=-100ms) messages produced, 17920827 (+1316378, p95=-100ms) messages consumed, 0 duplicated, 0 out of sequence.
14:34:52 info: Log[0] Elapsed: 18s, 0 (+0, p95=-100ms) messages produced, 19718113 (+1797286, p95=-100ms) messages consumed, 0 duplicated, 0 out of sequence.
14:34:53 info: Log[0] Elapsed: 19s, 0 (+0, p95=-100ms) messages produced, 21106490 (+1388377, p95=-100ms) messages consumed, 0 duplicated, 0 out of sequence
```

libkafka-asio, (compiled with 03):
```
root@docker-desktop:/workspace/libkafka-asio/examples/build# ./fetch_cxx11
 hello
elapsed:1.000000, messages: 496710
elapsed:2.000000, messages: 1821270
elapsed:3.000000, messages: 3063045
elapsed:4.000000, messages: 4304820
elapsed:5.000000, messages: 5519000
elapsed:6.000000, messages: 6733180
elapsed:7.000000, messages: 7947360
```

# cannot produce to newly created topics
```
Using assembly:Confluent.Kafka, Version=2.6.0.0, Culture=neutral, PublicKeyToken=12c514ca49093d1em location:/workspace/KafkaPlayground/dotnet/bin/Debug/net8.0/Confluent.Kafka.dll
librdkafka Version: 2.6.0-RC1-7-g6e1f62-dirty-devel-O0 (20600FF)
Debug Contexts: all, generic, broker, topic, metadata, feature, queue, msg, protocol, cgrp, security, fetch, interceptor, plugin, consumer, admin, eos, mock, assignor, conf
13:57:58 info: Log[0] Admin log: message=[thrd:localhost:40003/bootstrap]: localhost:40003/bootstrap: Connect to ipv6#[::1]:40003 failed: Connection refused (after 0ms in state CONNECT), name=rdkafka#producer-1, facility=FAIL, level=Error
13:57:58 info: Log[0] Admin log: message=[thrd:app]: Configuration property enable.auto.commit is a consumer property and will be ignored by this producer instance, name=rdkafka#producer-1, facility=CONFWARN, level=Warning
13:57:58 info: Log[0] Admin log: message=[thrd:app]: Configuration property enable.auto.offset.store is a consumer property and will be ignored by this producer instance, name=rdkafka#producer-1, facility=CONFWARN, level=Warning
13:57:58 info: Log[0] Admin log: message=[thrd:app]: Configuration property fetch.wait.max.ms is a consumer property and will be ignored by this producer instance, name=rdkafka#producer-1, facility=CONFWARN, level=Warning
13:57:58 info: Log[0] Admin log: message=[thrd:app]: Configuration property fetch.queue.backoff.ms is a consumer property and will be ignored by this producer instance, name=rdkafka#producer-1, facility=CONFWARN, level=Warning
13:57:58 info: Log[0] Admin log: message=[thrd:app]: Configuration property fetch.message.max.bytes is a consumer property and will be ignored by this producer instance, name=rdkafka#producer-1, facility=CONFWARN, level=Warning
13:57:58 info: Log[0] Admin log: message=[thrd:app]: Configuration property fetch.max.bytes is a consumer property and will be ignored by this producer instance, name=rdkafka#producer-1, facility=CONFWARN, level=Warning
13:57:58 info: Log[0] Admin log: message=[thrd:app]: Configuration property fetch.error.backoff.ms is a consumer property and will be ignored by this producer instance, name=rdkafka#producer-1, facility=CONFWARN, level=Warning
13:57:58 info: Log[0] Admin log: message=[thrd:app]: Configuration property auto.offset.reset is a consumer property and will be ignored by this producer instance, name=rdkafka#producer-1, facility=CONFWARN, level=Warning
13:57:58 fail: Log[0] Admin error: reason=localhost:40003/bootstrap: Connect to ipv6#[::1]:40003 failed: Connection refused (after 0ms in state CONNECT), IsLocal=True, IsBroker=False, IsFatal=False, IsCode=Local_Transport
13:57:58 info: Log[0] Admin log: message=[thrd:main]: Failed to acquire idempotence PID from broker localhost:40002/2: Broker: Coordinator load in progress: retrying, name=rdkafka#producer-1, facility=GETPID, level=Warning
13:57:58 info: Log[0] Creating 500 topics
13:57:59 info: Log[0] Creating 500 topics
13:57:59 info: Log[0] Creating 500 topics
13:57:59 info: Log[0] Creating 500 topics
13:57:59 info: Log[0] Creating 500 topics
13:57:59 info: Log[0] Creating 500 topics
13:58:00 info: Producer0:[0] Starting producer task:
13:58:00 info: Producer0:[0] kafka-log Facility:CONFWARN, Message[thrd:app]: Configuration property enable.auto.commit is a consumer property and will be ignored by this producer instance
13:58:00 info: Producer0:[0] kafka-log Facility:CONFWARN, Message[thrd:app]: Configuration property enable.auto.offset.store is a consumer property and will be ignored by this producer instance
13:58:00 info: Producer0:[0] kafka-log Facility:CONFWARN, Message[thrd:app]: Configuration property fetch.wait.max.ms is a consumer property and will be ignored by this producer instance
13:58:00 info: Producer0:[0] kafka-log Facility:CONFWARN, Message[thrd:app]: Configuration property fetch.queue.backoff.ms is a consumer property and will be ignored by this producer instance
13:58:00 info: Producer0:[0] kafka-log Facility:CONFWARN, Message[thrd:app]: Configuration property fetch.message.max.bytes is a consumer property and will be ignored by this producer instance
13:58:00 info: Producer0:[0] kafka-log Facility:CONFWARN, Message[thrd:app]: Configuration property fetch.max.bytes is a consumer property and will be ignored by this producer instance
13:58:00 info: Producer0:[0] kafka-log Facility:CONFWARN, Message[thrd:app]: Configuration property fetch.error.backoff.ms is a consumer property and will be ignored by this producer instance
13:58:00 info: Producer0:[0] kafka-log Facility:CONFWARN, Message[thrd:app]: Configuration property auto.offset.reset is a consumer property and will be ignored by this producer instance
13:58:00 info: Producer0:[0] kafka-log Facility:GETPID, Message[thrd:main]: Failed to acquire idempotence PID from broker localhost:40003/3: Broker: Coordinator load in progress: retrying
13:58:01 info: Log[0] Elapsed: 1s, 9829 (+9829, p95=-100ms) messages produced, 0 (+0, p95=-100ms) messages consumed, 0 duplicated, 0 out of sequence.
13:58:01 info: Producer0:[0] kafka-log Facility:FAIL, Message[thrd:localhost:40002/bootstrap]: localhost:40002/2: Connect to ipv6#[::1]:40002 failed: Connection refused (after 0ms in state CONNECT)
13:58:01 fail: Producer0:[0] Producer error: reason=localhost:40002/2: Connect to ipv6#[::1]:40002 failed: Connection refused (after 0ms in state CONNECT), IsLocal=True, IsBroker=False, IsFatal=False, IsCode=Local_Transport
13:58:02 info: Log[0] Elapsed: 2s, 19886 (+10057, p95=-100ms) messages produced, 0 (+0, p95=-100ms) messages consumed, 0 duplicated, 0 out of sequence.
13:58:03 info: Log[0] Elapsed: 3s, 29870 (+9984, p95=-100ms) messages produced, 0 (+0, p95=-100ms) messages consumed, 0 duplicated, 0 out of sequence.
13:58:04 info: Log[0] Elapsed: 4s, 39884 (+10014, p95=-100ms) messages produced, 0 (+0, p95=-100ms) messages consumed, 0 duplicated, 0 out of sequence.
13:58:05 info: Log[0] Elapsed: 5s, 49868 (+9984, p95=-100ms) messages produced, 0 (+0, p95=-100ms) messages consumed, 0 duplicated, 0 out of sequence.
13:58:06 info: Log[0] Elapsed: 6s, 59762 (+9894, p95=5680ms) messages produced, 0 (+0, p95=-100ms) messages consumed, 0 duplicated, 0 out of sequence.
13:58:07 info: Producer0:[0] kafka-log Facility:PARTCNT, Message[thrd:main]: Topic my-topic-1987 partition count changed from 1 to 0
13:58:07 info: Producer0:[0] kafka-log Facility:PARTCNT, Message[thrd:main]: Topic my-topic-1403 partition count changed from 1 to 0
13:58:07 info: Producer0:[0] kafka-log Facility:PARTCNT, Message[thrd:main]: Topic my-topic-2791 partition count changed from 1 to 0
13:58:07 info: Producer0:[0] kafka-log Facility:PARTCNT, Message[thrd:main]: Topic my-topic-2311 partition count changed from 1 to 0
13:58:07 info: Producer0:[0] kafka-log Facility:PARTCNT, Message[thrd:main]: Topic my-topic-1608 partition count changed from 1 to 0
13:58:07 info: Producer0:[0] kafka-log Facility:PARTCNT, Message[thrd:main]: Topic my-topic-2806 partition count changed from 1 to 0
13:58:07 info: Producer0:[0] kafka-log Facility:PARTCNT, Message[thrd:main]: Topic my-topic-1276 partition count changed from 1 to 0
13:58:07 info: Producer0:[0] kafka-log Facility:PARTCNT, Message[thrd:main]: Topic my-topic-2219 partition count changed from 1 to 0
13:58:07 info: Producer0:[0] kafka-log Facility:PARTCNT, Message[thrd:main]: Topic my-topic-1585 partition count changed from 1 to 0
13:58:07 info: Producer0:[0] kafka-log Facility:PARTCNT, Message[thrd:main]: Topic my-topic-1196 partition count changed from 1 to 0
13:58:07 info: Producer0:[0] kafka-log Facility:PARTCNT, Message[thrd:main]: Topic my-topic-1067 partition count changed from 1 to 0
13:58:07 info: Producer0:[0] kafka-log Facility:PARTCNT, Message[thrd:main]: Topic my-topic-2328 partition count changed from 1 to 0
13:58:07 info: Producer0:[0] kafka-log Facility:PARTCNT, Message[thrd:main]: Topic my-topic-1215 partition count changed from 1 to 0
13:58:07 info: Producer0:[0] kafka-log Facility:PARTCNT, Message[thrd:main]: Topic my-topic-1323 partition count changed from 1 to 0
13:58:07 info: Producer0:[0] kafka-log Facility:PARTCNT, Message[thrd:main]: Topic my-topic-2190 partition count changed from 1 to 0
13:58:07 info: Producer0:[0] kafka-log Facility:PARTCNT, Message[thrd:main]: Topic my-topic-1959 partition count changed from 1 to 0
13:58:07 info: Producer0:[0] kafka-log Facility:PARTCNT, Message[thrd:main]: Topic my-topic-1499 partition count changed from 1 to 0
13:58:07 info: Producer0:[0] kafka-log Facility:PARTCNT, Message[thrd:main]: Topic my-topic-1037 partition count changed from 1 to 0
13:58:07 info: Producer0:[0] kafka-log Facility:PARTCNT, Message[thrd:main]: Topic my-topic-1666 partition count changed from 1 to 0
13:58:07 info: Producer0:[0] kafka-log Facility:PARTCNT, Message[thrd:main]: Topic my-topic-2209 partition count changed from 1 to 0
13:58:07 info: Producer0:[0] kafka-log Facility:PARTCNT, Message[thrd:main]: Topic my-topic-2689 partition count changed from 1 to 0
13:58:07 info: Producer0:[0] kafka-log Facility:PARTCNT, Message[thrd:main]: Topic my-topic-2068 partition count changed from 1 to 0
13:58:07 info: Producer0:[0] kafka-log Facility:PARTCNT, Message[thrd:main]: Topic my-topic-1041 partition count changed from 1 to 0
13:58:07 info: Log[0] Admin log: message=[thrd:app]: Configuration property enable.auto.commit is a consumer property and will be ignored by this producer instance, name=rdkafka#producer-3, facility=CONFWARN, level=Warning
13:58:07 info: Log[0] Admin log: message=[thrd:app]: Configuration property enable.auto.offset.store is a consumer property and will be ignored by this producer instance, name=rdkafka#producer-3, facility=CONFWARN, level=Warning
13:58:07 info: Log[0] Admin log: message=[thrd:app]: Configuration property fetch.wait.max.ms is a consumer property and will be ignored by this producer instance, name=rdkafka#producer-3, facility=CONFWARN, level=Warning
13:58:07 info: Log[0] Admin log: message=[thrd:app]: Configuration property fetch.queue.backoff.ms is a consumer property and will be ignored by this producer instance, name=rdkafka#producer-3, facility=CONFWARN, level=Warning
13:58:07 info: Log[0] Admin log: message=[thrd:app]: Configuration property fetch.message.max.bytes is a consumer property and will be ignored by this producer instance, name=rdkafka#producer-3, facility=CONFWARN, level=Warning
13:58:07 info: Producer0:[0] kafka-log Facility:PARTCNT, Message[thrd:main]: Topic my-topic-2822 partition count changed from 1 to 0
13:58:07 info: Log[0] Admin log: message=[thrd:app]: Configuration property fetch.max.bytes is a consumer property and will be ignored by this producer instance, name=rdkafka#producer-3, facility=CONFWARN, level=Warning
13:58:07 info: Log[0] Admin log: message=[thrd:app]: Configuration property fetch.error.backoff.ms is a consumer property and will be ignored by this producer instance, name=rdkafka#producer-3, facility=CONFWARN, level=Warning
13:58:07 info: Log[0] Admin log: message=[thrd:app]: Configuration property auto.offset.reset is a consumer property and will be ignored by this producer instance, name=rdkafka#producer-3, facility=CONFWARN, level=Warning
13:58:07 info: Log[0] Admin log: message=[thrd:localhost:40001/bootstrap]: localhost:40001/bootstrap: Connect to ipv6#[::1]:40001 failed: Connection refused (after 0ms in state CONNECT), name=rdkafka#producer-3, facility=FAIL, level=Error
13:58:07 info: Producer0:[0] kafka-log Facility:PARTCNT, Message[thrd:main]: Topic my-topic-1619 partition count changed from 1 to 0
13:58:07 info: Producer0:[0] kafka-log Facility:PARTCNT, Message[thrd:main]: Topic my-topic-2519 partition count changed from 1 to 0
13:58:07 info: Producer0:[0] kafka-log Facility:PARTCNT, Message[thrd:main]: Topic my-topic-1011 partition count changed from 1 to 0
13:58:07 info: Producer0:[0] kafka-log Facility:PARTCNT, Message[thrd:main]: Topic my-topic-2169 partition count changed from 1 to 0
13:58:07 info: Producer0:[0] kafka-log Facility:PARTCNT, Message[thrd:main]: Topic my-topic-2587 partition count changed from 1 to 0
13:58:07 info: Producer0:[0] kafka-log Facility:PARTCNT, Message[thrd:main]: Topic my-topic-1655 partition count changed from 1 to 0
13:58:07 info: Producer0:[0] kafka-log Facility:PARTCNT, Message[thrd:main]: Topic my-topic-2381 partition count changed from 1 to 0
13:58:07 info: Producer0:[0] kafka-log Facility:PARTCNT, Message[thrd:main]: Topic my-topic-2300 partition count changed from 1 to 0
13:58:07 info: Producer0:[0] kafka-log Facility:PARTCNT, Message[thrd:main]: Topic my-topic-1976 partition count changed from 1 to 0
13:58:07 fail: Log[0] Admin error: reason=localhost:40001/bootstrap: Connect to ipv6#[::1]:40001 failed: Connection refused (after 0ms in state CONNECT), IsLocal=True, IsBroker=False, IsFatal=False, IsCode=Local_Transport
13:58:07 info: Producer0:[0] kafka-log Facility:PARTCNT, Message[thrd:main]: Topic my-topic-2508 partition count changed from 1 to 0
13:58:07 info: Producer0:[0] kafka-log Facility:PARTCNT, Message[thrd:main]: Topic my-topic-1022 partition count changed from 1 to 0
13:58:07 info: Producer0:[0] kafka-log Facility:PARTCNT, Message[thrd:main]: Topic my-topic-2429 partition count changed from 1 to 0
13:58:07 info: Producer0:[0] kafka-log Facility:PARTCNT, Message[thrd:main]: Topic my-topic-2780 partition count changed from 1 to 0
13:58:07 info: Producer0:[0] kafka-log Facility:PARTCNT, Message[thrd:main]: Topic my-topic-1435 partition count changed from 1 to 0
13:58:07 info: Producer0:[0] kafka-log Facility:PARTCNT, Message[thrd:main]: Topic my-topic-1366 partition count changed from 1 to 0
13:58:07 info: Producer0:[0] kafka-log Facility:PARTCNT, Message[thrd:main]: Topic my-topic-1153 partition count changed from 1 to 0
13:58:07 info: Producer0:[0] kafka-log Facility:PARTCNT, Message[thrd:main]: Topic my-topic-1716 partition count changed from 1 to 0
13:58:07 info: Producer0:[0] kafka-log Facility:PARTCNT, Message[thrd:main]: Topic my-topic-2959 partition count changed from 1 to 0
13:58:07 info: Producer0:[0] kafka-log Facility:PARTCNT, Message[thrd:main]: Topic my-topic-2392 partition count changed from 1 to 0
13:58:07 info: Producer0:[0] kafka-log Facility:PARTCNT, Message[thrd:main]: Topic my-topic-1886 partition count changed from 1 to 0
13:58:07 info: Producer0:[0] kafka-log Facility:PARTCNT, Message[thrd:main]: Topic my-topic-1812 partition count changed from 1 to 0
13:58:07 info: Producer0:[0] kafka-log Facility:PARTCNT, Message[thrd:main]: Topic my-topic-2908 partition count changed from 1 to 0
```

# Consumption rate vs message size 

- size=10, 73MiB:
```
12:14:09 info: Log[0] Elapsed: 30s, 0 (+0, p95=-100ms) messages produced, 35932262 (+1592044, p95=107031ms) messages consumed, 0 duplicated, 0 out of sequence.
12:14:10 info: Log[0] Elapsed: 31s, 0 (+0, p95=-100ms) messages produced, 37160522 (+1228260, p95=104864ms) messages consumed, 0 duplicated, 0 out of sequence.
12:14:11 info: Log[0] Elapsed: 32s, 0 (+0, p95=-100ms) messages produced, 38736377 (+1575855, p95=103374ms) messages consumed, 0 duplicated, 0 out of sequence.
12:14:12 info: Log[0] Elapsed: 33s, 0 (+0, p95=-100ms) messages produced, 39974989 (+1238612, p95=101252ms) messages consumed, 0 duplicated, 0 out of sequence.
```
- size=100, 300MiB:
```
12:10:00 info: Log[0] Elapsed: 14s, 0 (+0, p95=-100ms) messages produced, 13044331 (+1215957, p95=48946ms) messages consumed, 0 duplicated, 0 out of sequence.
12:10:01 info: Log[0] Elapsed: 15s, 0 (+0, p95=-100ms) messages produced, 14388488 (+1344157, p95=47111ms) messages consumed, 0 duplicated, 0 out of sequence.
12:10:02 info: Log[0] Elapsed: 16s, 0 (+0, p95=-100ms) messages produced, 15711604 (+1323116, p95=44752ms) messages consumed, 0 duplicated, 0 out of sequence.
12:10:03 info: Log[0] Elapsed: 17s, 0 (+0, p95=-100ms) messages produced, 17060528 (+1348924, p95=42366ms) messages consumed, 0 duplicated, 0 out of sequence.
```

- size=1000, 1.2 GiB:
```
12:06:47 info: Log[0] Elapsed: 14s, 0 (+0, p95=-100ms) messages produced, 6910319 (+664462, p95=243851ms) messages consumed, 0 duplicated, 0 out of sequence.
12:06:48 info: Log[0] Elapsed: 15s, 0 (+0, p95=-100ms) messages produced, 7574858 (+664539, p95=221939ms) messages consumed, 0 duplicated, 0 out of sequence.
12:06:49 info: Log[0] Elapsed: 16s, 0 (+0, p95=-100ms) messages produced, 8249267 (+674409, p95=207326ms) messages consumed, 0 duplicated, 0 out of sequence.
12:06:50 info: Log[0] Elapsed: 17s, 0 (+0, p95=-100ms) messages produced, 8835914 (+586647, p95=199292ms) messages consumed, 0 duplicated, 0 out of sequence.
```

- size=10000, 1.2 GiB:
```
12:20:39 info: Log[0] Elapsed: 7s, 0 (+0, p95=-100ms) messages produced, 248688 (+66924, p95=296904ms) messages consumed, 0 duplicated, 0 out of sequence.
12:20:40 info: Log[0] Elapsed: 8s, 0 (+0, p95=-100ms) messages produced, 317592 (+68904, p95=284632ms) messages consumed, 0 duplicated, 0 out of sequence.
12:20:41 info: Log[0] Elapsed: 9s, 0 (+0, p95=-100ms) messages produced, 388911 (+71319, p95=262088ms) messages consumed, 0 duplicated, 0 out of sequence.
```

# Producer fails to update its metadata when topic id changes (https://github.com/confluentinc/librdkafka/issues/4898)


# Unecessary delay of messages in librdkafka

Roundtrip time is ~30ms, but librdkafka delays messages so the measured producer latency is 2.5 * RTT (should be 1.5 * RTT)
```
>  ping 52.59.208.196 (kafka cluster on AWS using docker containers)
64 bytes from 52.59.208.196: icmp_seq=1 ttl=64 time=26.9 ms
64 bytes from 52.59.208.196: icmp_seq=2 ttl=64 time=28.3 ms
64 bytes from 52.59.208.196: icmp_seq=3 ttl=64 time=26.7 ms
64 bytes from 52.59.208.196: icmp_seq=4 ttl=64 time=35.2 ms
```

```
dotnet run -c Release --project /workspace/KafkaPlayground/dotnet/KafkaTool/KafkaTool.csproj \
producer \
--config allow.auto.create.topics=false \
--config bootstrap.servers=52.59.208.196:40001 \
--topics=1 \
--partitions=1 \
--replication-factor=3 \
--min-isr=2 \
--topic-stem=my-topic \
--messages-per-second=50 \
--burst-messages-per-second=1500000 \
--burst-cycle=0 \
--burst-duration=5000 \
\
--config request.required.acks=-1 \
--config enable.idempotence=true \
--config max.in.flight.requests.per.connection=1 \
\
--config queue.buffering.max.ms=3 \
--config queue.buffering.max.messages=10000000 \
--config queue.buffering.max.kbytes=10000000 \
\
--reporting-cycle=1000 \
--producers=1 \
--recreate-topics=true \
--recreate-topics-delay=5000 \
--recreate-topics-batch-size=500
```

```
Using assembly:Confluent.Kafka, Version=2.6.1.0, Culture=neutral, PublicKeyToken=12c514ca49093d1em location:/workspace/KafkaPlayground/dotnet/KafkaTool/bin/Release/net8.0/Confluent.Kafka.dll
librdkafka Version: 2.6.1 (20601FF)
Debug Contexts: all, generic, broker, topic, metadata, feature, queue, msg, protocol, cgrp, security, fetch, interceptor, plugin, consumer, admin, eos, mock, assignor, conf
14:28:00 info: Log[0] Removing 1 topics
14:28:06 info: Log[0] Creating 1 topics
14:28:11 info: Producer0:[0] Starting producer task:
14:28:12 info: Log[0] Elapsed: 1s, 49 (+49, p95=79ms) messages produced, 0 (+0, p95=-100ms) messages consumed, 0 duplicated, 0 out of sequence.
14:28:13 info: Log[0] Elapsed: 2s, 99 (+50, p95=77ms) messages produced, 0 (+0, p95=-100ms) messages consumed, 0 duplicated, 0 out of sequence.
14:28:14 info: Log[0] Elapsed: 3s, 149 (+50, p95=78ms) messages produced, 0 (+0, p95=-100ms) messages consumed, 0 duplicated, 0 out of sequence.
14:28:15 info: Log[0] Elapsed: 4s, 199 (+50, p95=78ms) messages produced, 0 (+0, p95=-100ms) messages consumed, 0 duplicated, 0 out of sequence.
14:28:16 info: Log[0] Elapsed: 5s, 249 (+50, p95=78ms) messages produced, 0 (+0, p95=-100ms) messages consumed, 0 duplicated, 0 out of sequence.
14:28:17 info: Log[0] Elapsed: 6s, 299 (+50, p95=78ms) messages produced, 0 (+0, p95=-100ms) messages consumed, 0 duplicated, 0 out of sequence.
14:28:18 info: Log[0] Elapsed: 7s, 349 (+50, p95=78ms) messages produced, 0 (+0, p95=-100ms) messages consumed, 0 duplicated, 0 out of sequence.
14:28:19 info: Log[0] Elapsed: 8s, 399 (+50, p95=212ms) messages produced, 0 (+0, p95=-100ms) messages consumed, 0 duplicated, 0 out of sequence.
14:28:20 info: Log[0] Elapsed: 9s, 449 (+50, p95=82ms) messages produced, 0 (+0, p95=-100ms) messages consumed, 0 duplicated, 0 out of sequence.
14:28:21 info: Log[0] Elapsed: 10s, 499 (+50, p95=77ms) messages produced, 0 (+0, p95=-100ms) messages consumed, 0 duplicated, 0 out of sequence.
14:28:22 info: Log[0] Elapsed: 11s, 549 (+50, p95=78ms) messages produced, 0 (+0, p95=-100ms) messages consumed, 0 duplicated, 0 out of sequence.
14:28:23 info: Log[0] Elapsed: 12s, 599 (+50, p95=78ms) messages produced, 0 (+0, p95=-100ms) messages consumed, 0 duplicated, 0 out of sequence.
14:28:24 info: Log[0] Elapsed: 13s, 649 (+50, p95=80ms) messages produced, 0 (+0, p95=-100ms) messages consumed, 0 duplicated, 0 out of sequence.
14:28:25 info: Log[0] Elapsed: 14s, 699 (+50, p95=80ms) messages produced, 0 (+0, p95=-100ms) messages consumed, 0 duplicated, 0 out of sequence.
14:28:26 info: Log[0] Elapsed: 15s, 749 (+50, p95=79ms) messages produced, 0 (+0, p95=-100ms) messages consumed, 0 duplicated, 0 out of sequence.
```

### Extra 100ms delay on docker
- docker exec client tc qdisc add dev eth0 root netem delay 100ms
