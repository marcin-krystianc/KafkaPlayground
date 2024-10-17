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

import org.apache.kafka.clients.consumer.internals.ConsumerDelegate;
import org.apache.kafka.clients.producer.*;
import org.apache.kafka.common.errors.RetriableException;
import org.apache.kafka.common.serialization.IntegerSerializer;

import java.util.Map;
import java.util.Properties;
import java.util.UUID;
import java.util.concurrent.atomic.AtomicInteger;

/**
 * A simple producer thread supporting two send modes:
 * - Async mode (default): records are sent without waiting for the response.
 * - Sync mode: each send operation blocks waiting for the response.
 */
public class Reporter extends Thread {
    private final KafkaProperties kafkaProperties;
    private final KafkaData kafkaData;

    public Reporter(KafkaProperties kafkaProperties, KafkaData kafkaData
    ) {
        super("reporter");
        this.kafkaProperties = kafkaProperties;
        this.kafkaData = kafkaData;
    }

    @Override
    public void run() {

        var startTime = System.currentTimeMillis();
        var logTime = System.currentTimeMillis();
        long loggedProduced = 0;
        long loggedConsumed = 0;

        while (true) {
            var currentTime = System.currentTimeMillis();
            if (currentTime - logTime > kafkaProperties.getReportingCycle()) {
                long produced = kafkaData.getProduced();

                long consumed = kafkaData.getConsumed();
                long duplicated = kafkaData.getDuplicated();
                long outOfSequence = kafkaData.getOutOfOrder();

                var elapsed = (currentTime - startTime) / 1000;
                Utils.printOut("Elapsed: %ds, Produced: %d (+%d), Consumed: %d (+%d), Duplicated: %d, Out of sequence: %d."
                        , elapsed, produced, (produced - loggedProduced)
                        , consumed, (consumed - loggedConsumed)
                        , duplicated, outOfSequence);
    
                logTime = currentTime;
                loggedProduced = produced;
                loggedConsumed = consumed;
            }

            try {
                Thread.sleep(1);
            } catch (InterruptedException e) {
            }
        }
    }
}
