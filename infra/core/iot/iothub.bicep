@minLength(1)
@maxLength(20)
param iotHubName string

@description('The datacenter to use for the deployment.')
param location string = resourceGroup().location

@description('The SKU to use for the IoT Hub.')
param skuName string = 'S1'

@description('The number of IoT Hub units.')
param skuUnits int = 1

@description('Partitions used for the event stream.')
param d2cPartitions int = 4

param managedIdentityName string

@description('The name of the service bus queue for device messages.')
param deviceMessagesQueueName string

@description('The name of the service bus namespace.')
param serviceBusNamespace string

resource userIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' existing = if (!empty(managedIdentityName)) {
  name: managedIdentityName
}

resource iotHub 'Microsoft.Devices/IotHubs@2023-06-30' = {
  name: iotHubName
  location: location
  sku: {
    name: skuName
    capacity: skuUnits
  }
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${userIdentity.id}': {}
    }
  }
  properties: {
    eventHubEndpoints: {
      events: {
        retentionTimeInDays: 1
        partitionCount: d2cPartitions
      }
    }
    routing: {
      endpoints: {
        serviceBusQueues: [
          {
            id: 'device-messages'
            name: 'service-bus-device-messages'
            authenticationType: 'identityBased'
            subscriptionId: subscription().subscriptionId
            resourceGroup: resourceGroup().name
            // The url of the service bus topic endpoint. It must include the protocol sb://
            endpointUri: 'sb://${serviceBusNamespace}.servicebus.windows.net/'
            entityPath: deviceMessagesQueueName
            identity: {
              userAssignedIdentity: userIdentity.id
            }
          }
        ]
      }
      routes: [
        {
          endpointNames: ['service-bus-device-messages']
          condition: 'true'
          isEnabled: true
          name: 'device-messages-to-service-bus'
          source: 'DeviceMessages'
        }
        {
          endpointNames: ['events']
          condition: 'true'
          isEnabled: true
          name: 'device-messages-to-builtin-events-endpoint'
          source: 'DeviceMessages'
        }
      ]
      fallbackRoute: {
        name: '$fallback'
        source: 'DeviceMessages'
        condition: 'true'
        endpointNames: [
          'events'
        ]
        isEnabled: true
      }
    }
    messagingEndpoints: {
      fileNotifications: {
        lockDurationAsIso8601: 'PT1M'
        ttlAsIso8601: 'PT1H'
        maxDeliveryCount: 10
      }
    }
    enableFileUploadNotifications: false
    cloudToDevice: {
      maxDeliveryCount: 10
      defaultTtlAsIso8601: 'PT1H'
      feedback: {
        lockDurationAsIso8601: 'PT1M'
        ttlAsIso8601: 'PT1H'
        maxDeliveryCount: 10
      }
    }
  }
}

output name string = iotHub.name
output resourceId string = iotHub.id
output resourceGroupName string = resourceGroup().name
output location string = location
output hostname string = iotHub.properties.hostName
output eventHubEndpoint string = iotHub.properties.eventHubEndpoints.events.endpoint
