
while ($true) {
for($i=1; $i -le 7; $i++)
{
	$containerName="kafka-$i"
	$bootstrapServer="$containerName" + ":19092"

	Write-Host (Get-Date).ToString() " Stopping container: $containerName"
	# Loop to continuously execute the command, wait for the container to stop, and then start it again
	# Run the command inside the container
	docker exec -it $containerName kill 1
	
	# Wait for the container to stop
	Write-Host (Get-Date).ToString() " Waiting for container to stop..."
	
	do {
		$containerStatus = docker inspect --format '{{.State.Status}}' $containerName
		Start-Sleep -Seconds 1
	} while ($containerStatus -eq "running") 

	Write-Host (Get-Date).ToString() " Container has stopped."

	# Start the container again
	Write-Host (Get-Date).ToString() " Starting container: $containerName"
	docker start $containerName

	Write-Host (Get-Date).ToString() " Container started. Waiting for cluster to catch-up"
	do {
		$underReplicatedPartitions = docker exec -it $containerName /opt/kafka/bin/kafka-topics.sh --describe --under-replicated-partitions --bootstrap-server $bootstrapServer
		$underMinIsrPartitions = docker exec -it $containerName /opt/kafka/bin/kafka-topics.sh --describe --under-min-isr-partitions --bootstrap-server $bootstrapServer
		$unavailablePartitions = docker exec -it $containerName /opt/kafka/bin/kafka-topics.sh --describe --unavailable-partitions --bootstrap-server $bootstrapServer
		$atMinIsrPartitions = docker exec -it $containerName /opt/kafka/bin/kafka-topics.sh --describe --at-min-isr-partitions --bootstrap-server $bootstrapServer
		Start-Sleep -Seconds 1
	} while ($underReplicatedPartitions || $underMinIsrPartitions || $unavailablePartitions || $atMinIsrPartitions) 

	Write-Host (Get-Date).ToString() " Waiting 30s for cluster to rebalance"
	# Wait for cluster to rebalance
	Start-Sleep -Seconds 30
}}