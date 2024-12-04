param location string = resourceGroup().location
param tags object = {}

@description('Url for the web pub sub server')
param iotManagerUrl string

@description('Assign role assignments to the managed identity')
param doRoleAssignments bool

@description('API Key')
param apiKey string

param containerAppsEnvironmentName string
param containerRegistryName string
param name string = ''
param serviceName string = 'solver'
param managedIdentityName string = ''
param exists bool = false
module solver '../core/host/container-app-upsert.bicep' = {
  name: '${serviceName}-container-app-module'
  params: {
    name: name
    location: location
    tags: union(tags, { 'azd-service-name': 'solver' })
    containerAppsEnvironmentName: containerAppsEnvironmentName
    containerRegistryName: containerRegistryName
    containerCpuCoreCount: '0.5'
    containerMemory: '1.0Gi'
    exists: exists
    daprEnabled: true
    containerName: serviceName
    daprAppId: serviceName
    targetPort: 7001
    identityType: 'UserAssigned'
    identityName: managedIdentityName
    env: [
      {
        name: 'IOT_MANAGER_URL'
        value: iotManagerUrl
      }
      {
        name: 'ApiKey'
        value: apiKey
      }
    ]
    doRoleAssignments: doRoleAssignments
  }
}


output SOLVER_URI string = solver.outputs.uri
output SERVICE_SOLVER_IMAGE_NAME string = solver.outputs.imageName
output SERVICE_SOLVER_NAME string = solver.outputs.name
output missingRoleAssignments string = doRoleAssignments ? '' : 'Assignment for ${managedIdentityName} to ${containerRegistryName} is not enabled. Add service "ACR Pull permissions" role to ${managedIdentityName}'
