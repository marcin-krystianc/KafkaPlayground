from concurrent.futures import ThreadPoolExecutor
from typing import Dict

from .consumer_task import run_consumer_task
from .data import ProducerConsumerData
from .producer_task import run_producer_task
from .reporter_task import run_reporter_task
from .settings import ProducerConsumerSettings
from .utils import recreate_topics


def run_producer_consumer(config: Dict[str, str], settings: ProducerConsumerSettings) -> None:
    recreate_topics(config, settings)
    data = ProducerConsumerData()
    # Make sure we have enough threads to run all producers, consumer and reporter:
    executor = ThreadPoolExecutor(settings.producers + 2)
    futures = [
        executor.submit(run_producer_task, config, settings, data, producer_index)
        for producer_index in range(settings.producers)]
    futures.append(executor.submit(run_consumer_task, config, settings, data))
    futures.append(executor.submit(run_reporter_task, data))

    for future in futures:
        future.result()
