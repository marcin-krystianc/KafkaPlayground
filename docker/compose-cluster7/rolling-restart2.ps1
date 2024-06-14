
while ($true) {
for($i=1; $i -le 7; $i++)
{
	$containerName="kafka-$i"
	$bootstrapServer="$containerName" + ":19092"

	Write-Host (Get-Date).ToString() " Stopping container: $containerName"
	# Loop to continuously execute the command, wait for the container to stop, and then start it again
	# Run the command inside the container
	docker exec -it $containerName kill 1

	Write-Host (Get-Date).ToString() " Container has stopped."

	# Start the container again
	Write-Host (Get-Date).ToString() " Starting container: $containerName"
	docker start $containerName

	Write-Host (Get-Date).ToString() " Waiting 5s"
	# Wait for cluster to rebalance
	Start-Sleep -Seconds 5
}}