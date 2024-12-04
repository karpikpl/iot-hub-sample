Write-Host "Loading azd .env file from current environment"

# Use the `get-values` azd command to retrieve environment variables from the `.env` file
$envValues = azd env get-values

$envDict = @{}

foreach ($line in $envValues -split "`n") {
    if ($line -match '^(.*?)=(.*)$') {
        $key = $Matches[1]
        $value = $Matches[2].Trim('"') # Remove surrounding quotes
        $envDict[$key] = $value
    }
}

$json_content = @{
    ServiceBus = @{
        Namespace = "$($envDict['SERVICEBUS_NAME']).servicebus.windows.net"
        TopicName = $envDict['SERVICEBUS_TOPIC_NAME']
    }
    IoT = @{
        HubHostName = $envDict['AZURE_IOTHUB_HOSTNAME']
        ManagerUrl = $envDict['SERVICE_IOT_MANAGER_URI']
    }
    ApiKey = $envDict['API_KEY']
} | ConvertTo-Json -Depth 5

$json_content | Set-Content './console-cloud/appsettings.local.json'
$json_content | Set-Content './console-scheduler/appsettings.local.json'
$json_content | Set-Content './console-device/appsettings.local.json'