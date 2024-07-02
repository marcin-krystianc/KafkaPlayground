package kafka.testing;

public class Main {
    private static boolean isRunning = true;

    public static void main(String[] args) throws Exception {

        KafkaConsumerProducerDemo demo = new KafkaConsumerProducerDemo();
        demo.main(args);
    }
}
