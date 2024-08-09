package kafka.testing;

public class Main {
    public static void main(String[] args) throws Exception {

        var kafkaProperties = new KafkaProperties(args);
        var cliArgs = kafkaProperties.getArgs();
        if (cliArgs.length > 0)
        {
            if (cliArgs[0].equals("producer-consumer"))
            {
                var demo = new ProducerConsumerMain();
                demo.main(args);
            }
            else if (cliArgs[0].equals("consumer"))
            {
                var demo = new ConsumerMain();
                demo.main(args);
            } 
            else if (cliArgs[0].equals("producer"))
            {
                var demo = new ProducerMain();
                demo.main(args);
            }
        }
        
    }
}
