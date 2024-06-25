
$delayContainerStart=75
$delayClusterRebalance=75
$i = 0
while ($true) {

$i++
Write-Host (Get-Date).ToString() " ########### Iteration $i ########## "

for($i=1; $i -le 3; $i++)
{
	$containerName="kafka-$i"
	$bootstrapServer="$containerName" + ":19092"

	Write-Host (Get-Date).ToString() " -------- Container $containerName ------- "
	Write-Host (Get-Date).ToString() " Stopping container: $containerName"
	# Loop to continuously execute the command, wait for the container to stop, and then start it again
	# Run the command inside the container	
	docker exec -it $containerName kill -s TERM 1
	
	# Wait for the container to stop
	Write-Host (Get-Date).ToString() " Waiting for $containerName to stop..."
	
	do {
		$containerStatus = docker inspect --format '{{.State.Status}}' $containerName
		Start-Sleep -Seconds 1
	} while ($containerStatus -eq "running") 

	Write-Host (Get-Date).ToString() " Container $containerName has stopped."

    Write-Host (Get-Date).ToString() " Waiting $delayContainerStart s before starting again the $containerName "
	Start-Sleep -Seconds $delayContainerStart
	
	# Start the container again
	Write-Host (Get-Date).ToString() " Starting $containerName"
	docker start $containerName | Out-Null

	do {
		$containerStatus = docker inspect --format '{{.State.Status}}' $containerName
		Start-Sleep -Seconds 1
	} while ($containerStatus -ne "running") 
	
	Write-Host (Get-Date).ToString() " Container $containerName started. Waiting for cluster to catch-up"
	
	do {
		$underReplicatedPartitions = docker exec -it $containerName /opt/kafka/bin/kafka-topics.sh --describe --under-replicated-partitions --bootstrap-server $bootstrapServer
		$underMinIsrPartitions = docker exec -it $containerName /opt/kafka/bin/kafka-topics.sh --describe --under-min-isr-partitions --bootstrap-server $bootstrapServer
		$unavailablePartitions = docker exec -it $containerName /opt/kafka/bin/kafka-topics.sh --describe --unavailable-partitions --bootstrap-server $bootstrapServer
		$atMinIsrPartitions = docker exec -it $containerName /opt/kafka/bin/kafka-topics.sh --describe --at-min-isr-partitions --bootstrap-server $bootstrapServer
		Start-Sleep -Seconds 1
	} while (($underReplicatedPartitions) -or ($underMinIsrPartitions) -or ($unavailablePartitions) -or ($atMinIsrPartitions)) 

	Write-Host (Get-Date).ToString() " Waiting $delayClusterRebalance s for cluster to rebalance"
	# Wait for cluster to rebalance
	Start-Sleep -Seconds $delayClusterRebalance
}}