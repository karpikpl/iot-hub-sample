param userObjectId string
param serviceBusName string
param iotHubName string

// See https://learn.microsoft.com/en-us/azure/role-based-access-control/built-in-roles#azure-service-bus-data-sender
var roleIdS = '69a216fc-b8fb-44d8-bc22-1f3c2cd27a39' // Azure Service Bus Data Sender
// See https://learn.microsoft.com/en-us/azure/role-based-access-control/built-in-roles#azure-service-bus-data-receiver
var roleIdR = '4f6d3b9b-027b-4f4c-9142-0e5a2a2247e0' // Azure Service Bus Data Receiver

// See https://learn.microsoft.com/en-us/azure/role-based-access-control/built-in-roles#iot-hub-data-contributor
// Allows for full access to IoT Hub data plane operations.
var iotHubDataContributor = '4fc6c259-987e-4a07-842e-c321cc9d413f' // IoT Hub Data Contributor

// See https://learn.microsoft.com/en-us/azure/role-based-access-control/built-in-roles#iot-hub-registry-contributor
// Allows for full access to IoT Hub device registry.
var iotHubRegistryContributor = '4ea46cd5-c1b2-4a8e-910b-273211f9ce47' // IoT Hub Registry Contributor

resource serviceBus 'Microsoft.ServiceBus/namespaces@2021-11-01' existing = {
  name: serviceBusName
}

resource ioTHub 'Microsoft.Devices/IotHubs@2023-06-30' existing = {
  name: iotHubName
}

// Grant permissions to the current user to specific role to servicebus
resource roleAssignmentUserReceiver 'Microsoft.Authorization/roleAssignments@2020-04-01-preview' = {
  name: guid(serviceBus.id, roleIdR, userObjectId)
  scope: serviceBus
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleIdR)
    principalId: userObjectId
    principalType: 'User'
  }
  dependsOn: [
    serviceBus
  ]
}

// Grant permissions to the current user to specific role to servicebus
resource roleAssignmentUserSender 'Microsoft.Authorization/roleAssignments@2020-04-01-preview' = {
  name: guid(serviceBus.id, roleIdS, userObjectId)
  scope: serviceBus
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleIdS)
    principalId: userObjectId
    principalType: 'User'
  }
  dependsOn: [
    serviceBus
  ]
}

// Grant permissions to the current user to specific role to iot hub
resource roleAssignmentDataContributor 'Microsoft.Authorization/roleAssignments@2020-04-01-preview' = {
  name: guid(ioTHub.id, iotHubDataContributor, userObjectId)
  scope: ioTHub
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', iotHubDataContributor)
    principalId: userObjectId
    principalType: 'User'
  }
  dependsOn: [
    ioTHub
  ]
}

// Grant permissions to the current user to specific role to iot hub
resource roleAssignmentRegistryContributor 'Microsoft.Authorization/roleAssignments@2020-04-01-preview' = {
  name: guid(ioTHub.id, iotHubRegistryContributor, userObjectId)
  scope: ioTHub
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', iotHubRegistryContributor)
    principalId: userObjectId
    principalType: 'User'
  }
  dependsOn: [
    ioTHub
  ]
}
