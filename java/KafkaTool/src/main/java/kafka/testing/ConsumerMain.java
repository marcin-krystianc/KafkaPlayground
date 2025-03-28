package kafka.testing;

import java.util.Arrays;

public class ConsumerMain {

    public static void main(String[] args) {
        try {
            var kafkaProperties = new KafkaProperties(args);
            var kafkaData = new KafkaData();
            
            String[] topicNames = new String[kafkaProperties.getNumberOfTopics()];
            for (int i = 0; i < kafkaProperties.getNumberOfTopics(); i++) {
                topicNames[i] = Utils.GetTopicName(kafkaProperties.getTopicStem(), i);
            }

            // stage 3: consume records from topic1
            Consumer consumer = new Consumer(kafkaProperties, kafkaData, topicNames);
            consumer.start();
            
            Reporter reporter = new Reporter(kafkaProperties, kafkaData);
            reporter.start();

            var allThreadsAreAlive = true;
            do {
                allThreadsAreAlive = allThreadsAreAlive && consumer.isAlive();
                allThreadsAreAlive = allThreadsAreAlive && reporter.isAlive();
                Thread.sleep(1000);
            } while(allThreadsAreAlive);

        } catch (Throwable e) {
            e.printStackTrace();
        }
    }
}
