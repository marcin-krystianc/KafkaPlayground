from concurrent.futures import ThreadPoolExecutor
import threading
from typing import Dict

from .consumer_task import run_consumer_task
from .data import ProducerConsumerData
from .reporter_task import run_reporter_task
from .settings import ProducerConsumerSettings
from .utils import run_tasks


def run_consumer(config: Dict[str, str], settings: ProducerConsumerSettings) -> None:
    data = ProducerConsumerData()
    executor = ThreadPoolExecutor(2)
    shutdown = threading.Event()
    futures = [
        executor.submit(run_consumer_task, config, settings, data, shutdown),
        executor.submit(run_reporter_task, data, shutdown),
    ]
    run_tasks(futures, shutdown)
