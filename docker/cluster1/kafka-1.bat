docker run -it --rm ^
--env-file kafka-1.env ^
--privileged ^
--name=kafka-1 ^
--hostname=kafka-1 ^
--publish 19092:9092 ^
mykafka
