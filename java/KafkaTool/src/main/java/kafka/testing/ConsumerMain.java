package kafka.testing;

import java.util.Arrays;

public class ConsumerMain {

    public static void main(String[] args) {
        try {
            var kafkaProperties = new KafkaProperties(args);

            String[] topicNames = new String[kafkaProperties.getNumberOfTopics()];
            for (int i = 0; i < kafkaProperties.getNumberOfTopics(); i++) {
                topicNames[i] = kafkaProperties.getTopicStem() + "-" + i;
            }

            // stage 3: consume records from topic1
            Consumer consumer = new Consumer(kafkaProperties, topicNames);
            consumer.start();
            
            Reporter reporter = new Reporter(new Producer[0], consumer);
            reporter.start();

            var allThreadsAreAlive = true;
            do {
                allThreadsAreAlive = allThreadsAreAlive && consumer.isAlive();
                allThreadsAreAlive = allThreadsAreAlive && reporter.isAlive();
            } while(allThreadsAreAlive);

        } catch (Throwable e) {
            e.printStackTrace();
        }
    }
}
