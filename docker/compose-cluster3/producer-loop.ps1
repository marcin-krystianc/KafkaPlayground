
while ($true) {
	Write-Host (Get-Date).ToString() " Starting producer"
	dotnet run --project  "D:\workspace\KafkaPlayground\dotnet\KafkaTool.csproj" producer --ini-file="D:\workspace\KafkaPlayground\dotnet\kafka.ini" --topics=150 --partitions=100  --replication-factor=2 --producers=100 --messages-per-second=1000
	Start-Sleep -Seconds 30
}