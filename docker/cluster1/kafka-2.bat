docker run -it --rm ^
--env-file kafka-2.env ^
--privileged ^
--name=kafka-2 ^
--hostname=kafka-2 ^
--publish 39092:9092 ^
mykafka
