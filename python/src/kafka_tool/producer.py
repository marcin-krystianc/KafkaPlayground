import threading
from concurrent.futures import ThreadPoolExecutor
from typing import Dict

from .data import ProducerConsumerData
from .producer_task import run_producer_task
from .reporter_task import run_reporter_task
from .settings import ProducerConsumerSettings
from .utils import recreate_topics, run_tasks


def run_producer(config: Dict[str, str], settings: ProducerConsumerSettings) -> None:
    recreate_topics(config, settings)
    data = ProducerConsumerData()
    # Make sure we have enough threads to run all producers and reporter:
    executor = ThreadPoolExecutor(settings.producers + 1)
    shutdown = threading.Event()
    futures = [
        executor.submit(run_producer_task, config, settings, data, producer_index, shutdown)
        for producer_index in range(settings.producers)]
    futures.append(executor.submit(run_reporter_task, data, shutdown))

    run_tasks(futures, shutdown)
