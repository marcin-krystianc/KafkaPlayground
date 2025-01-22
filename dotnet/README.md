# KafkaTool 

KafkaTool is a testing utility designed to validate Kafka cluster behavior, particularly during rolling restarts and other operational procedures. It's available in both .NET and Java implementations.


## Core Features

- Producer-consumer validation
- Message ordering verification
- Duplicate message detection
- Performance monitoring
- Support for various delivery semantics
- Multi-topic and multi-partition testing

## Usage Modes

### Producer-Consumer Mode
Tests both producing and consuming messages:

```bash
dotnet run --project KafkaTool.csproj \
  producer-consumer \
  --config bootstrap.servers=localhost:40001,localhost:40002,localhost:40003 \
  --topics=1 \
  --partitions=1 \
  --replication-factor=3 \
  --min-isr=2 \
  --producers=1 \
  --messages-per-second=1000
```

### Producer-Only Mode
Tests only message production:

```bash
dotnet run --project KafkaTool.csproj \
  producer \
  --config bootstrap.servers=localhost:40001,localhost:40002,localhost:40003 \
  --topics=1 \
  --partitions=1 \
  --replication-factor=3
  --messages-per-second=1000
```

### Producer-Only Mode
Tests only message consumption:

```bash
dotnet run --project KafkaTool.csproj \
  consumer \
  --config bootstrap.servers=localhost:40001,localhost:40002,localhost:40003 \
  --topics=1 \
  --partitions=1 \
  --replication-factor=3
  --messages-per-second=1000
```

## Configuration Parameters

### Core Parameters
- `--config <VALUE>`: Extra configuration in key=value format. Can be specified multiple times
- `--producers`: Number of concurrent producers
- `--topics`: Number of topics to create
- `--topic-stem`: Prefix for topic names
- `--partitions`: Number of partitions per topic
- `--replication-factor`: Number of replicas
- `--min-isr`: Minimum in-sync replicas for created topics
- `--messages-per-second`: Number of messages per second
- `--burst-messages-per-second`: Message rate during burst periods
- `--burst-cycle`: Burst cycle duration in milliseconds
- `--burst-duration`: Length of burst period in milliseconds
- `--reporting-cycle`: Metrics reporting interval in milliseconds
- `--statistics-path`: Path to write statistics JSON
- `--extra-payload-bytes`: Additional payload size in bytes
- `--recreate-topics`: Whether to recreate topics before testing
- `--recreate-topics-delay`: Delay between topic creation batches (ms)
- `--recreate-topics-batch-size`: Number of topics to create in each batch

### Example invocation
```
dotnet run --project KafkaTool.csproj \
  producer-consumer \
--config allow.auto.create.topics=false \
--config bootstrap.servers=localhost:40001,localhost:40002,localhost:40003 \
--topics=10 \
--partitions=5 \
--replication-factor=3 \
--min-isr=2 \
--topic-stem=my-topic \
--messages-per-second=5000000 \
--burst-messages-per-second=500000 \
--burst-cycle=0 \
--burst-duration=10000 \
\
--config request.timeout.ms=380000 \
--config message.timeout.ms=380000 \
--config request.required.acks=-1 \
--config enable.idempotence=true \
--config max.in.flight.requests.per.connection=5 \
--config topic.metadata.refresh.interval.ms=7 \
--config topic.metadata.propagation.max.ms=60000 \
\
--config auto.offset.reset=earliest \
--config enable.auto.offset.store=false \
--config enable.auto.commit=false \
--config fetch.wait.max.ms=1 \
--config fetch.queue.backoff.ms=1 \
--config fetch.error.backoff.ms=1 \
--config fetch.max.bytes=100000000 \
--config fetch.message.max.bytes=100000000 \
\
--config queue.buffering.max.ms=1 \
--config queue.buffering.max.messages=1000000 \
--config queue.buffering.max.kbytes=1000000 \
\
--config statistics.interval.ms=0 \
--statistics-path=my.txt \
\
--reporting-cycle=1000 \
--producers=1 \
--recreate-topics=true \
--recreate-topics-delay=5000 \
--recreate-topics-batch-size=500
```

### Example output
```
Using assembly:Confluent.Kafka, Version=2.6.1.0, Culture=neutral, PublicKeyToken=12c514ca49093d1em location:/workspace/KafkaPlayground/dotnet/KafkaTool/bin/Debug/net8.0/Confluent.Kafka.dll
librdkafka Version: 2.6.1 (20601FF)
Debug Contexts: all, generic, broker, topic, metadata, feature, queue, msg, protocol, cgrp, security, fetch, interceptor, plugin, consumer, admin, eos, mock, assignor, conf
16:01:40 info: Log[0] Removing 1 topics
16:01:41 info: Log[0] Creating 1 topics
16:01:42 info: Producer0:[0] Starting producer task:
16:01:42 info: Consumer:[0] Starting consumer task:
16:01:42 info: Consumer:[0] Consumer log: message=[thrd:localhost:40002/bootstrap]: localhost:40002/bootstrap: Connect to ipv6#[::1]:40002 failed: Connection refused (after 0ms in state CONNECT), name=rdkafka#consumer-3, facility=FAIL, level=Error
16:01:42 fail: Consumer:[0] Consumer error: reason=localhost:40002/bootstrap: Connect to ipv6#[::1]:40002 failed: Connection refused (after 0ms in state CONNECT), IsLocal=True, IsBroker=False, IsFatal=False, IsCode=Local_Transport
16:01:46 info: Consumer:[0] Consumer log: PartitionsAssignedHandler: count=1
16:01:52 info: Log[0] Elapsed: 10s, 9999 (+9999, p95=522ms) messages produced, 9995 (+9995, p95=3660ms) messages consumed, 0 duplicated, 0 out of sequence.
16:02:02 info: Log[0] Elapsed: 20s, 20001 (+10002, p95=6ms) messages produced, 19997 (+10002, p95=6ms) messages consumed, 0 duplicated, 0 out of sequence.
16:02:12 info: Log[0] Elapsed: 30s, 30001 (+10000, p95=6ms) messages produced, 29998 (+10001, p95=6ms) messages consumed, 0 duplicated, 0 out of sequence.
16:02:22 info: Log[0] Elapsed: 40s, 40001 (+10000, p95=6ms) messages produced, 39999 (+10001, p95=6ms) messages consumed, 0 duplicated, 0 out of sequence.
16:02:32 info: Log[0] Elapsed: 50s, 50002 (+10001, p95=6ms) messages produced, 49995 (+9996, p95=6ms) messages consumed, 0 duplicated, 0 out of sequence.
```
