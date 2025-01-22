# KafkaTool (Java)

KafkaTool is a testing utility designed to validate Kafka cluster behavior, particularly during rolling restarts and other operational procedures.

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
mvn package; mvn exec:java "-Dexec.mainClass=kafka.testing.Main" "$(cat <<EOF | tr '\n' ' ' | sed 's/ *$//'
"-Dexec.args=producer-consumer
--config bootstrap.servers=localhost:40001,localhost:40002,localhost:40003
--topics=1
--partitions=1
--replication-factor=3
--min-isr=2
--producers=1
--messages-per-second=1000"
EOF
)"
```

### Producer-Only Mode
Tests only message production:

```bash
mvn package; mvn exec:java "-Dexec.mainClass=kafka.testing.Main" "$(cat <<EOF | tr '\n' ' ' | sed 's/ *$//'
"-Dexec.args=producer
--config bootstrap.servers=localhost:40001,localhost:40002,localhost:40003
--topics=1
--partitions=1
--replication-factor=3
--messages-per-second=1000"
EOF
)"
```

### Consumer-Only Mode
Tests only message consumption:

```bash
mvn package; mvn exec:java "-Dexec.mainClass=kafka.testing.Main" "$(cat <<EOF | tr '\n' ' ' | sed 's/ *$//'
"-Dexec.args=consumer
--config bootstrap.servers=localhost:40001,localhost:40002,localhost:40003
--topics=1
--partitions=1
--replication-factor=3
--messages-per-second=1000"
EOF
)"
```

## Configuration Parameters

### Core Parameters
- `--config <VALUE>`: Extra configuration in key=value format
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

### Java-Specific Parameters
- `--config max.poll.records`: Maximum number of records returned in a single call to poll()
- `--config max.partition.fetch.bytes`: Maximum amount of data per partition returned by the server

### Example invocation
```bash
mvn package; mvn exec:java "-Dexec.mainClass=kafka.testing.Main" "$(cat <<EOF | tr '\n' ' ' | sed 's/ *$//'
"-Dexec.args=producer-consumer
--config allow.auto.create.topics=false
--config bootstrap.servers=localhost:40001,localhost:40002,localhost:40003
--topics=1
--partitions=1
--replication-factor=3
--min-isr=2
--topic-stem=my-topic
--messages-per-second=5000
--burst-messages-per-second=5000
--burst-cycle=0
--burst-duration=10000

--config request.timeout.ms=180000
--config message.timeout.ms=180000
--config request.required.acks=-1
--config enable.idempotence=false
--config max.in.flight.requests.per.connection=1
--config topic.metadata.propagation.max.ms=60000

--config auto.offset.reset=earliest
--config enable.auto.offset.store=false
--config enable.auto.commit=false
--config fetch.wait.max.ms=5
--config fetch.queue.backoff.ms=5
--config fetch.error.backoff.ms=5
--config fetch.max.bytes=100000000
--config fetch.message.max.bytes=100000000

--config queue.buffering.max.ms=5
--config queue.buffering.max.messages=1000000
--config queue.buffering.max.kbytes=1048576

--reporting-cycle=1000
--producers=1
--recreate-topics=true
--recreate-topics-delay=5000
--recreate-topics-batch-size=500
--config max.poll.records=50000
--config max.partition.fetch.bytes=100000000
--extra-payload-bytes=1000"
EOF
)"
```

### Example output
```
[2025-01-22 16:27:20,472] INFO Kafka version: 3.9.0 (org.apache.kafka.common.utils.AppInfoParser)
[2025-01-22 16:27:20,473] INFO Kafka commitId: 84caaa6e9da06435 (org.apache.kafka.common.utils.AppInfoParser)
[2025-01-22 16:27:20,473] INFO Kafka startTimeMs: 1737563240472 (org.apache.kafka.common.utils.AppInfoParser)
[2025-01-22 16:27:20,488] INFO [Producer clientId=client-7891c66a-788f-4627-aac8-e9330dbadf55] Cluster ID: 4L6g3nShT-eMCtK--X86sw (org.apache.kafka.clients.Metadata)
[2025-01-22 16:27:20,539] INFO These configurations '[topic.metadata.propagation.max.ms, enable.idempotence, queue.buffering.max.ms, enable.auto.offset.store, max.in.flight.requests.per.connection, message.timeout.ms, queue.buffering.max.kbytes, fetch.wait.max.ms, fetch.queue.backoff.ms, queue.buffering.max.messages, fetch.message.max.bytes, fetch.error.backoff.ms, request.required.acks]' were supplied but are not used yet. (org.apache.kafka.clients.consumer.ConsumerConfig)
[2025-01-22 16:27:20,539] INFO Kafka version: 3.9.0 (org.apache.kafka.common.utils.AppInfoParser)
[2025-01-22 16:27:20,539] INFO Kafka commitId: 84caaa6e9da06435 (org.apache.kafka.common.utils.AppInfoParser)
[2025-01-22 16:27:20,539] INFO Kafka startTimeMs: 1737563240539 (org.apache.kafka.common.utils.AppInfoParser)
[2025-01-22 16:27:20,541] INFO [Consumer clientId=client-fb6a4855-dd67-43c8-8349-3047a860516a, groupId=group-fbe503ca-98c4-4a04-8c1a-b56512a43bba] Subscribed to topic(s): my-topic-0000 (org.apache.kafka.clients.consumer.internals.ClassicKafkaConsumer)
[2025-01-22 16:27:20,554] INFO [Consumer clientId=client-fb6a4855-dd67-43c8-8349-3047a860516a, groupId=group-fbe503ca-98c4-4a04-8c1a-b56512a43bba] Cluster ID: 4L6g3nShT-eMCtK--X86sw (org.apache.kafka.clients.Metadata)
[2025-01-22 16:27:20,556] INFO [Consumer clientId=client-fb6a4855-dd67-43c8-8349-3047a860516a, groupId=group-fbe503ca-98c4-4a04-8c1a-b56512a43bba] Discovered group coordinator localhost:40001 (id: 2147483646 rack: null) (org.apache.kafka.clients.consumer.internals.ConsumerCoordinator)
[2025-01-22 16:27:20,560] INFO [Consumer clientId=client-fb6a4855-dd67-43c8-8349-3047a860516a, groupId=group-fbe503ca-98c4-4a04-8c1a-b56512a43bba] (Re-)joining group (org.apache.kafka.clients.consumer.internals.ConsumerCoordinator)
[2025-01-22 16:27:20,589] INFO [Consumer clientId=client-fb6a4855-dd67-43c8-8349-3047a860516a, groupId=group-fbe503ca-98c4-4a04-8c1a-b56512a43bba] Request joining group due to: need to re-join with the given member-id: client-fb6a4855-dd67-43c8-8349-3047a860516a-2e8542b0-6daa-4d58-9c4a-5ee6d1122795 (org.apache.kafka.clients.consumer.internals.ConsumerCoordinator)
[2025-01-22 16:27:20,589] INFO [Consumer clientId=client-fb6a4855-dd67-43c8-8349-3047a860516a, groupId=group-fbe503ca-98c4-4a04-8c1a-b56512a43bba] (Re-)joining group (org.apache.kafka.clients.consumer.internals.ConsumerCoordinator)
[16:27:21] reporter - Elapsed: 1s, Produced: 5040 (+5040, p95=113ms), Consumed: 0 (+0, p95=-100ms)), Duplicated: 0, Out of sequence: 0.
[16:27:22] reporter - Elapsed: 2s, Produced: 10045 (+5005, p95=1ms), Consumed: 0 (+0, p95=-100ms)), Duplicated: 0, Out of sequence: 0.
[16:27:23] reporter - Elapsed: 3s, Produced: 15050 (+5005, p95=1ms), Consumed: 0 (+0, p95=-100ms)), Duplicated: 0, Out of sequence: 0.
...
```