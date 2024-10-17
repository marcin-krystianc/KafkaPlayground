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
import com.tdunning.math.stats.TDigest;
import com.tdunning.math.stats.MergingDigest;
import java.util.concurrent.ConcurrentLinkedQueue;
import java.util.concurrent.atomic.AtomicLong;
import java.util.concurrent.locks.Lock;
import java.util.concurrent.locks.ReentrantLock;

public class KafkaData {
    private final Lock lock = new ReentrantLock();
    private final AtomicLong numConsumed = new AtomicLong();
    private final AtomicLong numProduced = new AtomicLong();
    private final AtomicLong numDuplicated = new AtomicLong();
    private final AtomicLong numOutOfOrder = new AtomicLong();
    private TDigest consumerLatencyStats = new MergingDigest(100);
    private TDigest producerLatencyStats = new MergingDigest(100);
    private final ConcurrentLinkedQueue<Double> consumerLatencyQueue = new ConcurrentLinkedQueue<>();
    private final ConcurrentLinkedQueue<Double> producerLatencyQueue = new ConcurrentLinkedQueue<>();
    private Thread backgroundProcessingThread;

    public void incrementConsumed() {
        numConsumed.addAndGet(1);
    }

    public void incrementProduced() {
        numProduced.addAndGet(1);
    }

    public void incrementDuplicated() {
        numDuplicated.addAndGet(1);
    }

    public void incrementOutOfOrder() {
        numOutOfOrder.addAndGet(1);
    }

    public void digestConsumerLatency(double latency) {
        consumerLatencyQueue.offer(latency);
    }

    public void digestProducerLatency(double latency) {
        producerLatencyQueue.offer(latency);
    }

    public long getConsumed() {
        return numConsumed.get();
    }

    public long getProduced() {
        return numProduced.get();
    }

    public long getDuplicated() {
        return numDuplicated.get();
    }

    public long getOutOfOrder() {
        return numOutOfOrder.get();
    }

    public TDigest getConsumerLatency() {
        lock.lock();
        try {
            TDigest result = consumerLatencyStats;
            consumerLatencyStats = new MergingDigest(100);
            if (result.size() == 0) result.add(-100);
            return result;
        } finally {
            lock.unlock();
        }
    }

    public TDigest getProducerLatency() {
        lock.lock();
        try {
            TDigest result = producerLatencyStats;
            producerLatencyStats = new MergingDigest(100);
            if (result.size() == 0) result.add(-100);
            return result;
        } finally {
            lock.unlock();
        }
    }

    public KafkaData() {
        backgroundProcessingThread = new Thread(() -> {
            while (!Thread.currentThread().isInterrupted()) {
                Double consumerLatency = consumerLatencyQueue.poll();
                if (consumerLatency != null) {
                    lock.lock();
                    try {
                        consumerLatencyStats.add(consumerLatency);
                    } finally {
                        lock.unlock();
                    }
                }

                Double producerLatency = producerLatencyQueue.poll();
                if (producerLatency != null) {
                    lock.lock();
                    try {
                        producerLatencyStats.add(producerLatency);
                    } finally {
                        lock.unlock();
                    }
                }

                if (consumerLatency == null && producerLatency == null) {
                    try {
                        Thread.sleep(1);
                    } catch (InterruptedException e) {
                        Thread.currentThread().interrupt();
                    }
                }
            }
        });
        backgroundProcessingThread.start();
    }

    public void shutdown() {
        backgroundProcessingThread.interrupt();
    }
}
