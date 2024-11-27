param location string = resourceGroup().location
param tags object = {}

param containerAppsEnvironmentName string
param containerRegistryName string
param name string = ''
param serviceName string = 'wps-server'
param managedIdentityName string = ''
param exists bool = false

@description('Iot Hub Name')
param iothubName string

@description('Service Bus Namespace')
param serviceBusNamespace string

@description('Device Messages Queue Name')
param deviceMessagesQueueName string

@description('Application Insights Connection String')
param appInsightsConnectionString string

@description('Assign role assignments to the managed identity')
param doRoleAssignments bool

@description('API Key')
param apiKey string

resource userIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' existing = if (!empty(managedIdentityName)) {
  name: managedIdentityName
}

module webPubSubServer '../core/host/container-app-upsert.bicep' = {
  name: '${serviceName}-container-app-module'
  params: {
    name: name
    location: location
    tags: union(tags, { 'azd-service-name': 'wps-server' })
    containerAppsEnvironmentName: containerAppsEnvironmentName
    containerRegistryName: containerRegistryName
    containerCpuCoreCount: '0.5'
    containerMemory: '1.0Gi'
    containerName: serviceName
    ingressEnabled: true
    identityType: 'UserAssigned'
    identityName: managedIdentityName
    exists: exists
    targetPort: 7002
    env: [
      {
        name: 'IoT__HubName'
        value: iothubName
      }
      {
        name: 'ServiceBus__DeviceMessagesQueueName'
        value: deviceMessagesQueueName
      }
      {
        name: 'ServiceBus__Namespace'
        value: serviceBusNamespace
      }
      {
        name: 'azureClientId'
        value: userIdentity.properties.clientId
      }
      {
        name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
        value: appInsightsConnectionString
      }
      {
        name: 'ApiKey'
        value: apiKey
      }
    ]
    doRoleAssignments: doRoleAssignments
  }
}


output SERVICE_IOT_MANAGER_IMAGE_NAME string = webPubSubServer.outputs.imageName
output SERVICE_IOT_MANAGER_NAME string = webPubSubServer.outputs.name
output SERVICE_IOT_MANAGER_URI string = webPubSubServer.outputs.uri
