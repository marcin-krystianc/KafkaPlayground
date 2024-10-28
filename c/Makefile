CC = gcc
CFLAGS = -Wall -Wextra

# Replace these paths with your local librdkafka build paths
RDKAFKA_PATH = /workspace/librdkafka
RDKAFKA_BUILD_PATH = $(RDKAFKA_PATH)

# Include and library paths
CFLAGS += -I$(RDKAFKA_PATH)/src
CFLAGS += -I$(RDKAFKA_BUILD_PATH)/src

# Link flags
LDFLAGS = -L$(RDKAFKA_BUILD_PATH)/src
LDFLAGS += -lrdkafka
LDFLAGS += -Wl,-rpath,$(RDKAFKA_BUILD_PATH)/src

TARGET = kafka_consumer
SRCS = consumer.c
OBJS = $(SRCS:.c=.o)

# Add pkg-config if available (optional)
PKG_CONFIG = $(shell which pkg-config)
ifneq ($(strip $(PKG_CONFIG)),)
    CFLAGS += $(shell pkg-config --cflags rdkafka 2>/dev/null || true)
    LDFLAGS += $(shell pkg-config --libs rdkafka 2>/dev/null || true)
endif

.PHONY: all clean

all: $(TARGET)

$(TARGET): $(OBJS)
	$(CC) $(OBJS) -o $(TARGET) $(LDFLAGS)

%.o: %.c
	$(CC) $(CFLAGS) -c $< -o $@

clean:
	rm -f $(OBJS) $(TARGET)