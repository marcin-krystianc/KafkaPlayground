package kafka.testing;

import java.time.Duration;
import java.net.InetSocketAddress;
import java.util.concurrent.*;
import java.util.concurrent.atomic.*;

public class Main {
    private static boolean isRunning = true;

    public static void main(String[] args) throws Exception {

        KafkaConsumerProducerDemo demo = new KafkaConsumerProducerDemo();
        demo.main(args);
    }
}
