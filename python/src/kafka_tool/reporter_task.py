import logging
import time
import threading

from .settings import ProducerConsumerSettings
from .data import ProducerConsumerData
from .utils import get_admin_client
from typing import Dict

log = logging.getLogger(__name__)


def run_reporter_task(
        config: Dict[str, object],
        settings: ProducerConsumerSettings,
        data: ProducerConsumerData,
        shutdown: threading.Event):
    start = time.monotonic()
    prev_produced = 0
    prev_consumed = 0

    log.info("Running reporter task")
    admin_client = get_admin_client(config);
    # Start background polling thread to make sure the OIDC token is refreshed on time
    poll_thread = threading.Thread(
        target=lambda: [
            admin_client.poll(1)
            for _ in iter(lambda: not shutdown.is_set(), False)
        ],
        daemon=True,
    )
    poll_thread.start()

    try:
        while True:
            for _ in range(10):
                if not shutdown.is_set():
                    time.sleep(settings.reporting_cycle / 1000.0 / 10)
                else:
                    return
            consumed, produced, duplicated, out_of_order = data.get_stats()
            newly_produced = produced - prev_produced
            newly_consumed = consumed - prev_consumed
            prev_produced = produced
            prev_consumed = consumed
            topics_count = len(admin_client.list_topics(timeout=30).topics)

            elapsed_s = int(time.monotonic() - start)
            log.info(
                "Elapsed: %d s, %d (+%d) messages produced, %d (+%d) messages consumed, %d duplicated, %d out of sequence, topics_count=%d",
                elapsed_s, produced, newly_produced, consumed, newly_consumed, duplicated, out_of_order, topics_count
            )

    finally:
        poll_thread.join()