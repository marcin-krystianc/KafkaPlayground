- Do you perform rebalancing (consumer group rebalancing which is automatic I guess and broker/partitions rebalancing)
- do you yuse KRAFT
- what Rolling restart  really means?
- namespaces, thousands of topics, millions of partitions, ACLs


william Roberts:
- message size in kafka is too small (increase from 1mb to 128mb)
- consumer groups and rebalancing is unstable


Jos Hickson (on Slack, #help-kafka)
As a follow-up: even after these changes 
I was still seeing odd things happening such as not receiving any messages or receiving messages on one topic but no another. 
I upgraded to Confluent.Kafka 1.6.3 (from 1.5.3) and after a bit of docker/C3 shenanigans all is now well.

# https://github.com/confluentinc/librdkafka/issues/4401

Confluent.Kafka = 1.7.0

docker network create my-kafka
docker exec -it kafka-1 /bin/bash
watch kcat -L -b kafka-1
kcat -C -b kafka-2 -t topic1 -o -2000
docker exec -it kafka-1 watch kcat -L -b kafka-1
docker exec -it kafka-1 htop
docker exec -it kafka-1 watch "ls -al /tmp/kraft-combined-logs | wc"
docker compose up > log.txt
iconv -f UTF-16 -t UTF-8 /workspace/KafkaPlayground/docker/compose-cluster3/cluster-log.txt | grep -i "topic88-5"
docker.exe run -t -i --rm -v d:/workspace/:/workspace ubuntu
sed -n '/2024-06-25 07:33:29/,/2024-06-25 07:42:29/p' /workspace/KafkaPlayground/docker/compose-cluster3/log-copy-txt > filtered_log.txt	

kafka-1:/opt/kafka/bin# ./kafka-metadata-quorum.sh --bootstrap-server kafka-1:19092 describe --status
ClusterId:              Some(4L6g3nShT-eMCtK--X86sw)
LeaderId:               1
LeaderEpoch:            1
HighWatermark:          32951
MaxFollowerLag:         0
MaxFollowerLagTimeMs:   0
CurrentVoters:          [1]
CurrentObservers:       []

# https://strimzi.io/blog/2022/09/16/reassign-partitions/
kafka-1:/opt/kafka/bin# ./kafka-reassign-partitions.sh --bootstrap-server kafka-1:19092 --generate --topics-to-move-json-file ~/topics.json  --broker-list "1"




/// <summary>Permanent: Partition does not exist in cluster.</summary>
    Local_UnknownPartition = -190, // 0xFFFFFF42
    
/// <summary>Permanent: Topic does not exist in cluster.</summary>
Local_UnknownTopic = -188, // 0xFFFFFF44
	
	
/// <summary>Unknown topic or partition</summary>
UnknownTopicOrPart = 3,
RD_KAFKA_RESP_ERR_UNKNOWN_TOPIC_OR_PART
/* Normalize error codes, unknown topic may be
                 * reported by the broker, or the lack of a topic in
                 * metadata response is figured out by the client.
                 * Make sure the application only sees one error code
                 * for both these cases. */
                if (topic->err == RD_KAFKA_RESP_ERR__UNKNOWN_TOPIC)
                        topic->err = RD_KAFKA_RESP_ERR_UNKNOWN_TOPIC_OR_PART;
						
https://access.redhat.com/documentation/en-us/red_hat_amq_streams/2.5/html/using_amq_streams_on_rhel/assembly-reassign-tool-str

https://github.com/confluentinc/librdkafka/commit/28b08c60958b9f11e766c5c2ebd0b2f677508e2e#diff-17c2af7f93fd5ac3f7afc7993bcfaa03bf3cb614e522a8dae82e2b077bcfd3beR328-R335
/* Cache unknown topics for a short while (100ms) to allow the cgrp
 * logic to find negative cache hits. */
if (mdt->err == RD_KAFKA_RESP_ERR_UNKNOWN_TOPIC_OR_PART)
        ts_expires = RD_MIN(ts_expires, now + (100 * 1000));

if (!mdt->err ||
    mdt->err == RD_KAFKA_RESP_ERR_TOPIC_AUTHORIZATION_FAILED ||
    mdt->err == RD_KAFKA_RESP_ERR_UNKNOWN_TOPIC_OR_PART)
        rd_kafka_metadata_cache_insert(rk, mdt, now, ts_expires);
else
        changed = rd_kafka_metadata_cache_delete_by_name(rk,
                                                                 mdt->topic);
		
# Confluent_Kafka_Definitive-Guide_Complete.pdf

- "By default, Kafka is
configured with auto.leader.rebalance.enable=true, which will check if the pre‐
ferred leader replica is not the current leader but is in-sync and trigger leader election
to make the preferred leader the current leader.
"

- "Automatic Leader Rebalancing
There is a broker configuration for automatic leader rebalancing,
but it is not recommended for production use. There are signifi‐
cant performance impacts caused by the automatic balancing mod‐
ule, and it can cause a lengthy pause in client traffic for larger
clusters."

####
 info: Producer1[0] kafka-log Facility:PARTCNT, Message[thrd:main]: Topic topic26 partition count changed from 1000 to 0
 Error: DeliveryReport.Error, Code = Local_UnknownPartition, Reason = Local: Unknown partition, IsFatal = False, IsError = True, IsLocalError = True, IsBrokerError = False

####
13:51:43 info: Producer[0] kafka-log Facility:PARTCNT, Message[thrd:main]: Topic topic181 partition count changed from 100 to 0
13:51:43 info: Producer[0] kafka-log Facility:PARTCNT, Message[thrd:main]: Topic topic181 partition count changed from 100 to 0
%5|1718365903.660|PARTCNT|rdkafka#producer-1| [thrd:main]: Topic topic181 partition count changed from 100 to 0
13:51:43 info: Producer[0] Exception: DeliveryReport.Error, Code = Local_UnknownPartition, Reason = Local: Unknown partition, IsFatal = False, IsError = True, IsLocalError = True, IsBrokerError = False, topic = topic181, partitionsCount = 0
13:51:43 info: Producer[0] Exception: DeliveryReport.Error, Code = Local_UnknownPartition, Reason = Local: Unknown partition, IsFatal = False, IsError = True, IsLocalError = True, IsBrokerError = False, topic = topic181, partitionsCount = 0
Error: DeliveryReport.Error, Code = Local_UnknownPartition, Reason = Local: Unknown partition, IsFatal = False, IsError = True, IsLocalError = True, IsBrokerError = False, topic = topic181, partitionsCount = 0

####
17:36:33 info: Producer[0] Exception: DeliveryReport.Error, Code = Local_MsgTimedOut, Reason = Local: Message timed out, IsFatal = False, IsError = True, IsLocalError = True, IsBrokerError = False, topic = topic37, partitionsCount = 100
17:36:33 info: Producer[0] kafka-log Facility:REQTMOUT, Message[thrd:localhost:40001/bootstrap]: localhost:40001/1: Timed out ProduceRequest in flight (after 13386ms, timeout #0): possibly held back by preceeding MetadataRequest with timeout in 43027ms
17:36:33 info: Producer[0] kafka-log Facility:REQTMOUT, Message[thrd:localhost:40001/bootstrap]: localhost:40001/1: Timed out ProduceRequest in flight (after 7616ms, timeout #1)
17:36:33 info: Producer[0] kafka-log Facility:REQTMOUT, Message[thrd:localhost:40001/bootstrap]: localhost:40001/1: Timed out 2 in-flight, 0 retry-queued, 0 out-queue, 0 partially-sent requests
17:36:33 info: Producer[0] kafka-log Facility:FAIL, Message[thrd:localhost:40001/bootstrap]: localhost:40001/1: 2 request(s) timed out: disconnect (after 319000ms in state UP)
17:36:33 info: Producer[0] kafka-log Facility:ERROR, Message[thrd:app]: rdkafka#producer-72: localhost:40001/1: 2 request(s) timed out: disconnect (after 319000ms in state UP)
Error: DeliveryReport.Error, Code = Local_MsgTimedOut, Reason = Local: Message timed out, IsFatal = False, IsError = True, IsLocalError = True, IsBrokerError = False, topic = topic37, partitionsCount = 100
17:36:33 info: Producer[0] Exception: DeliveryReport.Error, Code = Local_MsgTimedOut, Reason = Local: Message timed out, IsFatal = False, IsError = True, IsLocalError = True, IsBrokerError = False, topic = topic142, partitionsCount = 100





 
 
# Logs after --config debug=metadata
 
 : message=[thrd:main]: localhost:40001/1:   Topic topic-125 with 0 partitions: Broker: Unknown topic or partition, name=rdkafka#consumer-6, facility=METADATA, level=Debug
	Line  80118: 16:28:01 info: Consumer:[0] Consumer log: message=[thrd:main]: Error in metadata reply for topic topic-125 (PartCnt 0): Broker: Unknown topic or partition, name=rdkafka#consumer-6, facility=METADATA, level=Debug
	Line  80119: 16:28:01 info: Consumer:[0] Consumer log: message=[thrd:main]: Topic topic-125 partition count changed from 10 to 0, name=rdkafka#consumer-6, facility=PARTCNT, level=Notice
	
	