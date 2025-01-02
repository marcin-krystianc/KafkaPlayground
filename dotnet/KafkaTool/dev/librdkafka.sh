#!/bin/sh
set -eu

dotnet build ../KafkaTool.csproj
#(cd ../../../../librdkafka && ./dev-conf.sh)
(cd ../../../../librdkafka && ./dev-conf.sh asan)
#(cd ../../../../librdkafka && ./dev-conf.sh clean) # Non-development clean build
cp ../../../../librdkafka/src/librdkafka.so ../bin/Debug/net8.0/runtimes/linux-x64/native/
cp ../../../../librdkafka/src/librdkafka.so ../bin/Release/net8.0/runtimes/linux-x64/native/
