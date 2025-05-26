import threading
from typing import Dict

from .consumer_task import run_consumer_task
from .data import ProducerConsumerData
from .producer_task import run_producer_task
from .reporter_task import run_reporter_task
from .settings import ProducerConsumerSettings
from .utils import recreate_topics, run_tasks


def run_producer_consumer(config: Dict[str, str], settings: ProducerConsumerSettings) -> None:
    if settings.recreate_topics:
        recreate_topics(config, settings)

    data = ProducerConsumerData()
    # Make sure we have enough threads to run all producers, consumer and reporter:
    shutdown = threading.Event()
    threads = [
        threading.Thread(target=run_producer_task, args=[config, settings, data, producer_index, shutdown])
        for producer_index in range(settings.producers)]
    threads.append(threading.Thread(target=run_consumer_task, args=[config, settings, data, shutdown]))
    threads.append(threading.Thread(target=run_reporter_task, args=[config, data, shutdown]))

    run_tasks(threads, shutdown)
