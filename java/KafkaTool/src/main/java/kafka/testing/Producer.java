/*
 * Licensed to the Apache Software Foundation (ASF) under one or more
 * contributor license agreements. See the NOTICE file distributed with
 * this work for additional information regarding copyright ownership.
 * The ASF licenses this file to You under the Apache License, Version 2.0
 * (the "License"); you may not use this file except in compliance with
 * the License. You may obtain a copy of the License at
 *
 *    http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */
package kafka.testing;

import org.apache.kafka.clients.producer.Callback;
import org.apache.kafka.clients.producer.KafkaProducer;
import org.apache.kafka.clients.producer.ProducerConfig;
import org.apache.kafka.clients.producer.ProducerRecord;
import org.apache.kafka.clients.producer.RecordMetadata;
import org.apache.kafka.common.KafkaException;
import org.apache.kafka.common.errors.AuthorizationException;
import org.apache.kafka.common.errors.FencedInstanceIdException;
import org.apache.kafka.common.errors.OutOfOrderSequenceException;
import org.apache.kafka.common.errors.ProducerFencedException;
import org.apache.kafka.common.errors.RetriableException;
import org.apache.kafka.common.errors.SerializationException;
import org.apache.kafka.common.errors.UnsupportedVersionException;
import org.apache.kafka.common.serialization.IntegerSerializer;
import org.apache.kafka.common.serialization.StringSerializer;

import java.util.Properties;
import java.util.UUID;
import java.util.concurrent.CountDownLatch;
import java.util.concurrent.ExecutionException;

/**
 * A simple producer thread supporting two send modes:
 * - Async mode (default): records are sent without waiting for the response.
 * - Sync mode: each send operation blocks waiting for the response.
 */
public class Producer extends Thread {
    private final String bootstrapServers;
    private final String topic;
    private final boolean isAsync;
    private final String transactionalId;
    private final boolean enableIdempotency;
    private final int numRecords;
    private final int transactionTimeoutMs;
    private final CountDownLatch latch;
    private volatile boolean closed;
    private final int messagesPerSecond;
    
    public Producer(String threadName,
                    String bootstrapServers,
                    String topic,
                    boolean isAsync,
                    String transactionalId,
                    boolean enableIdempotency,
                    int numRecords,
                    int transactionTimeoutMs,
                    CountDownLatch latch) {
        super(threadName);
        this.bootstrapServers = bootstrapServers;
        this.topic = topic;
        this.isAsync = isAsync;
        this.transactionalId = transactionalId;
        this.enableIdempotency = enableIdempotency;
        this.numRecords = numRecords;
        this.transactionTimeoutMs = transactionTimeoutMs;
        this.latch = latch;
        this.messagesPerSecond = 1000;
    }

    @Override
    public void run() {
        int key = 0;
        int sentRecords = 0;
        int messagesToSend = 0;
        long startTime = System.currentTimeMillis();
        
        // the producer instance is thread safe
        try (KafkaProducer<Integer, Integer> producer = createKafkaProducer()) {
            while (!closed ) {
                
                if (messagesToSend == 0)
                {
                    var currentTime = System.currentTimeMillis();
                    var elapsedTime = currentTime - startTime;
                    if (elapsedTime < 100)
                    {
                        Thread.sleep(100 - elapsedTime); // Sleep for 1 second
                    }
                    messagesToSend = messagesPerSecond / 10;
                }
                messagesToSend--;
                
                var value = 1;
                asyncSend(producer, key, value);
                key++;
                sentRecords++;
            }
        } catch (Throwable e) {
            Utils.printErr("Unhandled exception");
            e.printStackTrace();
        }
        Utils.printOut("Sent %d records", sentRecords);
        shutdown();
    }

    public void shutdown() {
        if (!closed) {
            closed = true;
            latch.countDown();
        }
    }

    public KafkaProducer<Integer, Integer> createKafkaProducer() {
        Properties props = new Properties();
        // bootstrap server config is required for producer to connect to brokers
        props.put(ProducerConfig.BOOTSTRAP_SERVERS_CONFIG, bootstrapServers);
        // client id is not required, but it's good to track the source of requests beyond just ip/port
        // by allowing a logical application name to be included in server-side request logging
        props.put(ProducerConfig.CLIENT_ID_CONFIG, "client-" + UUID.randomUUID());
        // key and value are just byte arrays, so we need to set appropriate serializers
        props.put(ProducerConfig.KEY_SERIALIZER_CLASS_CONFIG, IntegerSerializer.class);
        props.put(ProducerConfig.VALUE_SERIALIZER_CLASS_CONFIG, IntegerSerializer.class);
        props.put(ProducerConfig.MAX_BLOCK_MS_CONFIG, 180000); 

        if (transactionTimeoutMs > 0) {
            // max time before the transaction coordinator proactively aborts the ongoing transaction
            props.put(ProducerConfig.TRANSACTION_TIMEOUT_CONFIG, transactionTimeoutMs);
        }
        if (transactionalId != null) {
            // the transactional id must be static and unique
            // it is used to identify the same producer instance across process restarts
            props.put(ProducerConfig.TRANSACTIONAL_ID_CONFIG, transactionalId);
        }
        // enable duplicates protection at the partition level
        props.put(ProducerConfig.ENABLE_IDEMPOTENCE_CONFIG, enableIdempotency);
        return new KafkaProducer<>(props);
    }

    private void asyncSend(KafkaProducer<Integer, Integer> producer, int key, int value) {
        // send the record asynchronously, setting a callback to be notified of the result
        // note that, even if you set a small batch.size with linger.ms=0, the send operation
        // will still be blocked when buffer.memory is full or metadata are not available
        producer.send(new ProducerRecord<>(topic, key, value), new ProducerCallback(key, value));
    }

    class ProducerCallback implements Callback {
        private final int key;
        private final int value;

        public ProducerCallback(int key, Integer value) {
            this.key = key;
            this.value = value;
        }

        /**
         * A callback method the user can implement to provide asynchronous handling of request completion. This method will
         * be called when the record sent to the server has been acknowledged. When exception is not null in the callback,
         * metadata will contain the special -1 value for all fields except for topicPartition, which will be valid.
         *
         * @param metadata The metadata for the record that was sent (i.e. the partition and offset). An empty metadata
         *                 with -1 value for all fields except for topicPartition will be returned if an error occurred.
         * @param exception The exception thrown during processing of this record. Null if no error occurred.
         */
        public void onCompletion(RecordMetadata metadata, Exception exception) {
            if (exception != null) {
                Utils.printErr(exception.getMessage());
                if (!(exception instanceof RetriableException)) {
                    // we can't recover from these exceptions
                    shutdown();
                }
            }
        }
    }
}
