from confluent_kafka import Producer
from threading import Thread
import weakref
import time

class PollingProducer (Producer):
    def __init__(self, configs):
        super().__init__(configs)
        # Polling thread is needed to trigger custom OAUTH callback (when they are configured) and also to receive produce events (produce confirmations)
        poll_thread = Thread(target=self._poll_loop, args=(weakref.ref(self), ), daemon=True)
        poll_thread.start()

    @staticmethod
    def _poll_loop(self_ref):

        while True:
            # Sleep is essential here.
            # Effectively acts as a cooperative multitasking yield point,
            # allowing the GIL to be relinquished and other Python threads (or the interpreter's internal garbage collection mechanisms) to run.
            time.sleep(0.1);
            inst = self_ref()
            if inst is None:
                print("inst is None")
                break
            inst.poll(0.1)

