import logging
import time
import threading

from .data import ProducerConsumerData
from .utils import get_admin_client
from typing import Dict

log = logging.getLogger(__name__)


def run_reporter_task(
        config: Dict[str, str],
        data: ProducerConsumerData,
        shutdown: threading.Event):
    start = time.monotonic()
    prev_produced = 0
    prev_consumed = 0

    log.info("Running reporter task")
    admin_client = get_admin_client(config);

    while True:
        for _ in range(10):
            if not shutdown.is_set():
                time.sleep(1.0)
            else:
                return
        consumed, produced, duplicated, out_of_order = data.get_stats()
        newly_produced = produced - prev_produced
        newly_consumed = consumed - prev_consumed
        prev_produced = produced
        prev_consumed = consumed
        admin_client.admin_client.list_topics(timeout=30).topics;

        elapsed_s = int(time.monotonic() - start)
        log.info(
            "Elapsed: %d s, %d (+%d) messages produced, %d (+%d) messages consumed, %d duplicated, %d out of sequence.",
            elapsed_s, produced, newly_produced, consumed, newly_consumed, duplicated, out_of_order
        )
