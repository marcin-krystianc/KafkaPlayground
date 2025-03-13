# Use the specified base image
FROM apache/kafka
USER root
RUN apk update \
 && apk add htop kcat sysstat atop iotop nano jq iproute2 \
 && tc qdisc add dev eth0 root netem delay 100ms
 
 
