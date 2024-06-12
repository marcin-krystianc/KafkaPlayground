
for($i=2; $i -le 3; $i++)
{
	Write-Host $i
	$containerName="kafka-$i"
	Write-Host $containerName

	Write-Host "Stopping container: $containerName"
	# Loop to continuously execute the command, wait for the container to stop, and then start it again
	# Run the command inside the container
	docker exec -it $containerName kill 1
	
	# Wait for the container to stop
	Write-Host "Waiting for container to stop..."
	
	do {
		$containerStatus = docker inspect --format '{{.State.Status}}' $containerName
		Start-Sleep -Seconds 1
	} while ($containerStatus -eq "running") 

	Write-Host "Container has stopped."

	# Start the container again
	Write-Host "Starting container: $containerName"
	docker start $containerName

	Write-Host "Container started. Waiting for cluster to catch-up"
	do {
		$underReplicatedPartitions = docker exec -it kafka-1 /opt/kafka/bin/kafka-topics.sh --describe --under-replicated-partitions --bootstrap-server kafka-1:19092
		Start-Sleep -Seconds 1
	} while ($underReplicatedPartitions) 
}