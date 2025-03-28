package kafka.testing;

public class Main {
    public static void main(String[] args) throws Exception {

        var kafkaProperties = new KafkaProperties(args);
        var cliArgs = kafkaProperties.getArgs();
        if (cliArgs.length > 0)
        {
            if (cliArgs[0].equals("producer-consumer"))
            {
                var app = new ProducerConsumerMain();
                app.main(args);
            }
            else if (cliArgs[0].equals("consumer"))
            {
                var app = new ConsumerMain();
                app.main(args);
            } 
            else if (cliArgs[0].equals("producer"))
            {
                var app = new ProducerMain();
                app.main(args);
            }
        }

        System.exit(0);
    }
}
