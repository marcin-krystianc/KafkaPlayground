EXAMPLES ?= producer 

all: $(EXAMPLES)

CXXFLAGS += -I./librdkafka.redist/2.8.0-gr/build/native/include/librdkafka/

# librdkafka must be compiled with -gstrict-dwarf, but rdkafka_example must not,
# due to some clang bug on OSX 10.9

producer_cpp: ./librdkafka.redist/2.8.0-gr/runtimes/linux-x64/native/ producer.cpp
	$(CXX) $(CPPFLAGS) $(CXXFLAGS) producer.cpp -o $@ $(LDFLAGS) \
		./librdkafka.redist/2.8.0-gr/runtimes/linux-x64/native/librdkafka.so $(LIBS)

clean:
	rm -f $(EXAMPLES)

