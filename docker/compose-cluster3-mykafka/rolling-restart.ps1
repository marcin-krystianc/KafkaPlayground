
$delayContainerStart=67
$delayClusterRebalance=61
$clusterIteration = 0
while ($true) {

$clusterIteration++
Write-Host (Get-Date).ToString() " ########### Iteration $clusterIteration ########## "

for($i=1; $i -le 3; $i++)
{
	$containerName="kafka-$i"
	$bootstrapServer="$containerName" + ":19092"

	Write-Host (Get-Date).ToString() " -------- Container $containerName ------- "
	Write-Host (Get-Date).ToString() " Checking if cluster is healthy."

	do {
		$unhealthyPartitions = docker exec -it $containerName /opt/kafka/bin/kafka-topics.sh --describe --under-replicated-partitions --at-min-isr-partitions --under-min-isr-partitions --unavailable-partitions --bootstrap-server $bootstrapServer
		Start-Sleep -Seconds 1
	} while (($unhealthyPartitions))

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

    Write-Host (Get-Date).ToString() " Waiting ${delayContainerStart}s before starting the $containerName "
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
		$unhealthyPartitions = docker exec -it $containerName /opt/kafka/bin/kafka-topics.sh --describe --under-replicated-partitions --at-min-isr-partitions --under-min-isr-partitions --unavailable-partitions --bootstrap-server $bootstrapServer
		Start-Sleep -Seconds 1
	} while (($unhealthyPartitions))

	Write-Host (Get-Date).ToString() " Waiting extra ${delayClusterRebalance}s for cluster to rebalance"
	# Wait for cluster to rebalance
	Start-Sleep -Seconds $delayClusterRebalance
}}