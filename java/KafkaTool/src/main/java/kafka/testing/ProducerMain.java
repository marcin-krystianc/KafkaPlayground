
package kafka.testing;

import java.util.Arrays;

public class ProducerMain {

    public static void main(String[] args) {
        try {
            var kafkaProperties = new KafkaProperties(args);

            String[] topicNames = new String[kafkaProperties.getNumberOfTopics()];
            for (int i = 0; i < kafkaProperties.getNumberOfTopics(); i++) {
                topicNames[i] = kafkaProperties.getTopicStem() + "-" + i;
            }

            // stage 1: clean any topics left from previous runs
            Utils.recreateTopics(kafkaProperties.getConfigs(), kafkaProperties.getNumberOfPartitions()
                , kafkaProperties.getReplicationFactor(), kafkaProperties.getMinIsr()
                , kafkaProperties.getRecreateTopicsDelay(), kafkaProperties.getRecreateTopicsBatchSize()
                , topicNames);

            if (kafkaProperties.getNumberOfTopics() % kafkaProperties.getNumberOfProducers() != 0) {
                throw new Exception(String.format("Cannot evenly schedule %d topics on a %d producers!", kafkaProperties.getNumberOfTopics(), kafkaProperties.getNumberOfProducers()));
            }

            var topicsPerProducer = kafkaProperties.getNumberOfTopics() / kafkaProperties.getNumberOfProducers();
            Producer[] producers = new Producer[kafkaProperties.getNumberOfProducers()];
            for (int i = 0; i < kafkaProperties.getNumberOfProducers(); i++) {
                producers[i] = new Producer(kafkaProperties, Arrays.copyOfRange(topicNames, i * topicsPerProducer, (i+1) * topicsPerProducer));
                producers[i].start();
            }
            
            Reporter reporter = new Reporter(producers, null);
            reporter.start();

            var allThreadsAreAlive = true;
            do {
                allThreadsAreAlive = allThreadsAreAlive && reporter.isAlive();
                for (var producer : producers) {
                    allThreadsAreAlive = allThreadsAreAlive && producer.isAlive();
                }
            } while(allThreadsAreAlive);

        } catch (Throwable e) {
            e.printStackTrace();
        }
    }
}
