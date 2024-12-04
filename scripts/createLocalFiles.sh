echo "Loading azd .env file from current environment"

# Use the `get-values` azd command to retrieve environment variables from the `.env` file
while IFS='=' read -r key value; do
    value=$(echo "$value" | sed 's/^"//' | sed 's/"$//')
    export "$key=$value"
done <<EOF
$(azd env get-values) 
EOF

json_content='{
    "ServiceBus": {
        "Namespace": "'"$SERVICEBUS_NAME".servicebus.windows.net'",
        "TopicName": "'"$SERVICEBUS_TOPIC_NAME"'"
    },
    "IoT": {
        "HubHostName": "'"$AZURE_IOTHUB_HOSTNAME"'",
        "ManagerUrl": "'"$SERVICE_IOT_MANAGER_URI"'"
    },
    "ApiKey": "'"$API_KEY"'"
}'

echo "$json_content" > ./console-subscriber/appsettings.local.json
echo "$json_content" > ./console-scheduler/appsettings.local.json
echo "$json_content" > ./console-device/appsettings.local.json