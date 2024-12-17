package kafka.testing;

import org.apache.kafka.common.errors.SerializationException;
import org.apache.kafka.common.header.Headers;
import org.apache.kafka.common.serialization.Deserializer;

import java.nio.ByteBuffer;

public class MyLongDeserializer implements Deserializer<Long> {
    @Override
    public Long deserialize(String topic, byte[] data) {
        if (data == null)
            return null;

        if (data.length < 8) {
            throw new SerializationException("Size of data received by LongDeserializer is not 8");
        }

        long value = 0;
        for (byte b : data) {
            value <<= 8;
            value |= b & 0xFF;
        }
        return value;
    }

    @Override
    public Long deserialize(String topic, Headers headers, ByteBuffer data) {
        if (data == null) {
            return null;
        }

        if (data.remaining() < 8) {
            throw new SerializationException("Size of data received by LongDeserializer is less than 8");
        }
        return data.getLong(data.position());
    }
}
