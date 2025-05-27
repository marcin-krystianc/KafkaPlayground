import threading
from typing import Dict

from .consumer_task import run_consumer_task
from .data import ProducerConsumerData
from .reporter_task import run_reporter_task
from .settings import ProducerConsumerSettings
from .utils import run_tasks


def run_consumer(config: Dict[str, str], settings: ProducerConsumerSettings) -> None:
    data = ProducerConsumerData()
    shutdown = threading.Event()
    threads = [
        threading.Thread(target=run_consumer_task, args=[config, settings, data, shutdown]),
        threading.Thread(target=run_reporter_task, args=[data, settings, shutdown]),
    ]
    run_tasks(threads, shutdown)
