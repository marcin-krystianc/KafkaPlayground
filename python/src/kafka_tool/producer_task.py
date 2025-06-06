import logging
import time
import threading
from typing import Dict

from confluent_kafka import Producer, KafkaError

from .data import ProducerConsumerData
from .settings import ProducerConsumerSettings
from .utils import get_admin_client, get_topic_name
from threading import Thread
import functools
from .polling_producer import PollingProducer

log = logging.getLogger(__name__)

def run_producer_task(
        config: Dict[str, str],
        settings: ProducerConsumerSettings,
        data: ProducerConsumerData,
        producer_index: int,
        shutdown: threading.Event):

    if settings.topics % settings.producers != 0:
        raise Exception(
            f"Cannot evenly schedule {settings.topics} topics on {settings.producers} producers")

    topics_per_producer = settings.topics // settings.producers

    producer = PollingProducer(config)
    log.info("Running producer task %d", producer_index)

    exception = None

    def delivery_report(err, msg):
        nonlocal exception

        if err is not None:
            log.error(f"Delivery failed for {msg.topic()}: {err}")
            if exception is None:
                admin_client = get_admin_client(config)
                topic_metadata = admin_client.list_topics(topic=msg.topic(), timeout=30)
                partitions_count = len(topic_metadata.topics[msg.topic()].partitions)
                exception = Exception(
                    f"DeliveryReport.Error, Code = {err.code()}, Reason = {err.str()}"
                    f", IsFatal = {err.fatal()}, IsError = {err.code() != KafkaError.NO_ERROR}"
                    f", IsLocalError = {err.retriable()}"
                    f", topic = {msg.topic()}, partition = {msg.partition()}, partitionsCount = {partitions_count}"
                )
        else:
            data.increment_produced()

    messages_until_sleep = 0
    time_since_sleep = time.monotonic()

    current_value = 0
    while not shutdown.is_set():
        for topic_index in range(topics_per_producer):
            topic_name = get_topic_name(
                settings.topic_stem, topic_index + producer_index * topics_per_producer)
            for k in range(settings.partitions * 7):
                if exception is not None:
                    raise exception

                if messages_until_sleep <= 0:
                    elapsed = time.monotonic() - time_since_sleep
                    if elapsed < 0.1:
                        time.sleep(0.1 - elapsed)
                    time_since_sleep = time.monotonic()
                    messages_until_sleep = settings.messages_per_second // 10

                messages_until_sleep -= 1

                while True:
                    try:
                        producer.produce(topic_name, key=str(k), value=str(current_value), on_delivery=delivery_report)
                        break
                    except BufferError as e:
                        producer.flush()

        current_value += 1