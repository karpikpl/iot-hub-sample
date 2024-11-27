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
    "WebPubSub": {
        "Name": "'"$AZURE_WEBPUBSUB_NAME"'",
        "Hostname": "'"$AZURE_WEBPUBSUB_HOSTNAME"'",
        "HubName": "'"$AZURE_WEBPUBSUB_HUB_NAME"'",
        "ServerUrl": "'"$SERVICE_IOT_MANAGER_URI"'
    },
    "ApiKey": "'"$API_KEY"'"
}'

echo "$json_content" > ./console-subscriber/appsettings.local.json
echo "$json_content" > ./console-scheduler/appsettings.local.json
echo "$json_content" > ./console-publisher/appsettings.local.json