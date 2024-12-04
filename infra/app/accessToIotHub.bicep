param managedIdentityName string
param iotHubName string
@description('Assign role assignments to the managed identity')
param doRoleAssignments bool

// See https://learn.microsoft.com/en-us/azure/role-based-access-control/built-in-roles#iot-hub-data-contributor
// Allows for full access to IoT Hub data plane operations.
var iotHubDataContributor = '4fc6c259-987e-4a07-842e-c321cc9d413f' // IoT Hub Data Contributor

// See https://learn.microsoft.com/en-us/azure/role-based-access-control/built-in-roles#iot-hub-registry-contributor
// Allows for full access to IoT Hub device registry.
var iotHubRegistryContributor = '4ea46cd5-c1b2-4a8e-910b-273211f9ce47' // IoT Hub Registry Contributor

// user assigned managed identity to use throughout
resource userIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' existing = if (!empty(managedIdentityName)) {
  name: managedIdentityName
}

resource ioTHub 'Microsoft.Devices/IotHubs@2023-06-30' existing = {
  name: iotHubName
}

// Grant permissions to the current user to specific role to iot hub
resource roleAssignmentDataContributor 'Microsoft.Authorization/roleAssignments@2020-04-01-preview' = if (doRoleAssignments) {
  name: guid(ioTHub.id, iotHubDataContributor, managedIdentityName)
  scope: ioTHub
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', iotHubDataContributor)
    principalId: userIdentity.properties.principalId
    principalType: 'ServicePrincipal' // managed identity is a form of service principal
  }
  dependsOn: [
    ioTHub
  ]
}

// Grant permissions to the current user to specific role to iot hub
resource roleAssignmentRegistryContributor 'Microsoft.Authorization/roleAssignments@2020-04-01-preview' = if (doRoleAssignments) {
  name: guid(ioTHub.id, iotHubRegistryContributor, managedIdentityName)
  scope: ioTHub
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', iotHubRegistryContributor)
    principalId: userIdentity.properties.principalId
    principalType: 'ServicePrincipal' // managed identity is a form of service principal
  }
  dependsOn: [
    ioTHub
  ]
}

output missingRoleAssignments string = doRoleAssignments ? '' : 'Assignment for ${managedIdentityName} to ${iotHubName} is not enabled. Add service "IoT Hub Registry Contributor" and "IoT Hub Data Contributor" role to ${userIdentity.properties.principalId}'
