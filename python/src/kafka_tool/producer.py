import threading
from typing import Dict

from .data import ProducerConsumerData
from .producer_task import run_producer_task
from .reporter_task import run_reporter_task
from .settings import ProducerConsumerSettings
from .utils import recreate_topics, run_tasks


def run_producer(config: Dict[str, str], settings: ProducerConsumerSettings) -> None:
    if settings.recreate_topics: recreate_topics(config, settings)

    data = ProducerConsumerData()
    shutdown = threading.Event()
    threads = [
        threading.Thread(target=run_producer_task, args=[config, settings, data, producer_index, shutdown])
        for producer_index in range(settings.producers)]
    threads.append(threading.Thread(target=run_reporter_task, args=[config, data, shutdown]))

    run_tasks(threads, shutdown)
