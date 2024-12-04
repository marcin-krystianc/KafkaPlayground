## Feature Comparison Matrix

| Feature \ Library        | librdkafka | modern-cpp-kafka (librdkafka)   | libkafka-asio        | kafka-rust | samsa       | RSKafka     |
|--------------------------|------------|---------------------------------|----------------------|------------|-------------|-------------|
| Admin API                | ✅         | ✅                               | ❌                   | ❌         | ✅          |  ✅          | 
| SSL/SASL                 | ✅         | ✅                               | ❌                   | ❌         | ✅          |  ✅          | 
| Kerberos                 | ✅         | ✅                               | ❌                   | ❌         | ❌          |  ❌          | 
| Exactly Once Semantics   | ✅         | ✅                               | ?                    | ?         | ?           |  ?           | 
| Compression Support      | ✅         | ✅                               | ?                    | ?         | ✅          |  ✅          |
| Transaction Support      | ✅         | ✅                               | ?                    | ?         | ?           |  ?           | 
| Stars                    | 306        | 362                              | 76                  | 1.3k       | 108         | 298         |
| Forks                    | 3.2k       | 90                               | 40                  | 133        | 5           | 36           |
| Contributors             | 238        | 14                               | 4                   | 24         | 4           | 17            |
| Last-commit              | 2 weeks ago| 5 months ago                     | 7 years ago         | last month | 4 months ago| 4 days ago |   

### C/C++
- librdkafka - https://github.com/confluentinc/librdkafka
- modern-cpp-kafka - https://github.com/morganstanley/modern-cpp-kafka
- libkafka-asio - https://github.com/danieljoos/libkafka-asio (c++ header only)

### Rust
- kafka-rust - https://github.com/kafka-rust/kafka-rust
-- https://github.com/kafka-rust/kafka-rust/issues/51
- samsa - https://github.com/CallistoLabsNYC/samsa
- rskafka - https://github.com/influxdata/rskafka/

