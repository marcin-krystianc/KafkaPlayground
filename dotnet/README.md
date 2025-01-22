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

### common librdkafka configuration
- `--recreate-topics`: Recreate topics before testing
- `--recreate-topics-delay`: Delay between topic creation batches
- `--recreate-topics-batch-size`: Number of topics to create in each batch
