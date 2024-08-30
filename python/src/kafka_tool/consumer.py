from concurrent.futures import ThreadPoolExecutor
from typing import Dict

from .consumer_task import run_consumer_task
from .data import ProducerConsumerData
from .reporter_task import run_reporter_task
from .settings import ProducerConsumerSettings


def run_consumer(config: Dict[str, str], settings: ProducerConsumerSettings) -> None:
    data = ProducerConsumerData()
    executor = ThreadPoolExecutor(2)
    futures = [
        executor.submit(run_consumer_task, config, settings, data),
        executor.submit(run_reporter_task, data),
    ]
    for future in futures:
        future.result()
