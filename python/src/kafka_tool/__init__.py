from __future__ import annotations

import argparse
import logging
from typing import Dict

import confluent_kafka
import functools

from .consumer import run_consumer
from .producer import run_producer
from .producer_consumer import run_producer_consumer
from .settings import ProducerConsumerSettings
from .utils import oauth_cb
