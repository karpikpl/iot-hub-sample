targetScope = 'subscription'

@minLength(1)
@maxLength(64)
@description('Name of the the environment which is used to generate a short unique hash used in all resources.')
param environmentName string

@minLength(1)
@description('Primary location for all resources')
param location string

@description('Assign role assignments to the managed identity')
param doRoleAssignments bool = true

param principalId string

// App based params

// Subsciber
param solverContainerAppName string = ''
param solverServiceName string = 'solver'
param solverAppExists bool = false

// IOT Manager App
param iotManagerContainerAppName string = ''
param iotManagerServiceName string = 'iot-manager'
param iotManagerAppExists bool = false

param applicationInsightsDashboardName string = ''
param applicationInsightsName string = ''
param logAnalyticsName string = ''

param containerAppsEnvironmentName string = ''
param containerRegistryName string = ''

param resourceGroupName string = ''
// Optional parameters to override the default azd resource naming conventions. Update the main.parameters.json file to provide values. e.g.,:
// "resourceGroupName": {
//      "value": "myGroupName"
// }

var abbrs = loadJsonContent('./abbreviations.json')
var resourceToken = toLower(uniqueString(subscription().id, environmentName, location))
var tags = { 'azd-env-name': environmentName, 'role-assignments-set': '${doRoleAssignments}' }

// IOT Hub Name Resource
var iotHubName = '${abbrs.devicesIotHubs}${resourceToken}'
var iotHubHostName = '${abbrs.devicesIotHubs}${resourceToken}.azure-devices.net'

// Organize resources in a resource group
resource rg 'Microsoft.Resources/resourceGroups@2021-04-01' = {
  name: !empty(resourceGroupName) ? resourceGroupName : '${abbrs.resourcesResourceGroups}${environmentName}'
  location: location
  tags: tags
}

var apiKey = uniqueString(subscription().id, rg.name, environmentName)

// Monitor application with Azure Monitor
module monitoring './core/monitor/monitoring.bicep' = {
  name: 'monitoring'
  scope: rg
  params: {
    location: location
    tags: tags
    logAnalyticsName: !empty(logAnalyticsName) ? logAnalyticsName : '${abbrs.operationalInsightsWorkspaces}${resourceToken}'
    applicationInsightsName: !empty(applicationInsightsName) ? applicationInsightsName : '${abbrs.insightsComponents}${resourceToken}'
    applicationInsightsDashboardName: !empty(applicationInsightsDashboardName) ? applicationInsightsDashboardName : '${abbrs.portalDashboards}${resourceToken}'
  }
}

module serviceBusResources './app/servicebus.bicep' = {
  name: 'sb-resources'
  scope: rg
  params: {
    location: location
    tags: tags
    resourceToken: resourceToken
    skuName: 'Standard'
  }
}

module serviceBusAccess './app/access.bicep' = {
  name: 'sb-access'
  scope: rg
  params: {
    location: location
    serviceBusName: serviceBusResources.outputs.serviceBusName
    managedIdentityName: '${abbrs.managedIdentityUserAssignedIdentities}${resourceToken}'
    doRoleAssignments: doRoleAssignments
  }
}

// Shared App Env with Dapr configuration for db
module appEnv './app/app-env.bicep' = {
  name: 'app-env'
  scope: rg
  params: {
    containerAppsEnvName: !empty(containerAppsEnvironmentName) ? containerAppsEnvironmentName : '${abbrs.appManagedEnvironments}${resourceToken}'
    containerRegistryName: !empty(containerRegistryName) ? containerRegistryName : '${abbrs.containerRegistryRegistries}${resourceToken}'
    location: location
    logAnalyticsWorkspaceName: monitoring.outputs.logAnalyticsWorkspaceName
    serviceBusName: serviceBusResources.outputs.serviceBusName
    applicationInsightsName: monitoring.outputs.applicationInsightsName
    daprEnabled: true
    managedIdentityClientId: serviceBusAccess.outputs.managedIdentityClientlId
  }
}


module solverApp './app/solver.bicep' = {
  name: 'web-resources'
  scope: rg
  params: {
    name: !empty(solverContainerAppName) ? solverContainerAppName : '${abbrs.appContainerApps}${solverServiceName}-${resourceToken}'
    location: location
    containerRegistryName: appEnv.outputs.registryName
    containerAppsEnvironmentName: appEnv.outputs.environmentName
    serviceName: solverServiceName
    managedIdentityName: serviceBusAccess.outputs.managedIdentityName
    exists: solverAppExists
    iotManagerUrl: iotManagerApp.outputs.SERVICE_IOT_MANAGER_URI
    doRoleAssignments: doRoleAssignments
    apiKey: apiKey
  }
}

module iothub './core/iot/iothub.bicep' = {
  name: 'iothub'
  scope: rg
  params: {
    location: location
    iotHubName: iotHubName
    managedIdentityName: serviceBusAccess.outputs.managedIdentityName
    serviceBusNamespace: serviceBusResources.outputs.serviceBusName
    deviceMessagesQueueName: serviceBusResources.outputs.queueName
  }
}

module AccessForUser './app/accessForUser.bicep' = if (doRoleAssignments) {
  name: 'access-for-user'
  scope: rg
  params: {
    serviceBusName: serviceBusResources.outputs.serviceBusName
    userObjectId: principalId
    iotHubName: iotHubName
  }
  dependsOn: [
    iothub
  ]
}

module iotManagerApp './app/iotmanager.bicep' = {
  name: 'iot-manager'
  scope: rg
  params: {
    name: !empty(iotManagerContainerAppName) ? iotManagerContainerAppName : '${abbrs.appContainerApps}${iotManagerServiceName}-${resourceToken}'
    location: location
    containerRegistryName: appEnv.outputs.registryName
    containerAppsEnvironmentName: appEnv.outputs.environmentName
    serviceName: iotManagerServiceName
    managedIdentityName: serviceBusAccess.outputs.managedIdentityName
    exists: iotManagerAppExists
    iothubHostName: iotHubHostName
    deviceMessagesQueueName: serviceBusResources.outputs.queueName
    serviceBusNamespace: '${serviceBusResources.outputs.serviceBusName}.servicebus.windows.net'
    appInsightsConnectionString: monitoring.outputs.applicationInsightsConnectionString
    doRoleAssignments: doRoleAssignments
    apiKey: apiKey
  }
}

module accessToIoTHub './app/accessToIotHub.bicep' = {
  name: 'access-to-iot'
  scope: rg
  params: {
    managedIdentityName: serviceBusAccess.outputs.managedIdentityName
    iotHubName: iotHubName
    doRoleAssignments: doRoleAssignments
  }
}

output SERVICE_SOLVER_NAME string = solverApp.outputs.SERVICE_SOLVER_NAME
output SERVICE_SOLVER_IMAGE_NAME string = solverApp.outputs.SERVICE_SOLVER_IMAGE_NAME
output SERVICEBUS_ENDPOINT string = serviceBusResources.outputs.SERVICEBUS_ENDPOINT
output SERVICEBUS_NAME string = serviceBusResources.outputs.serviceBusName
output SERVICEBUS_TOPIC_NAME string = serviceBusResources.outputs.topicName
output APPINSIGHTS_INSTRUMENTATIONKEY string = monitoring.outputs.applicationInsightsInstrumentationKey
output APPINSIGHTS_CONNECTIONSTRING string = monitoring.outputs.applicationInsightsConnectionString
output APPLICATIONINSIGHTS_NAME string = monitoring.outputs.applicationInsightsName
output AZURE_CONTAINER_ENVIRONMENT_NAME string = appEnv.outputs.environmentName
output AZURE_CONTAINER_REGISTRY_ENDPOINT string = appEnv.outputs.registryLoginServer
output AZURE_CONTAINER_REGISTRY_NAME string = appEnv.outputs.registryName
output AZURE_MANAGED_IDENTITY_NAME string = serviceBusAccess.outputs.managedIdentityName
output AZURE_IOTHUB_NAME string = iothub.outputs.name
output AZURE_IOTHUB_ENDPOINT string = iothub.outputs.eventHubEndpoint
output AZURE_IOTHUB_HOSTNAME string = iothub.outputs.hostname
output SERVICE_IOT_MANAGER_IMAGE_NAME string = iotManagerApp.outputs.SERVICE_IOT_MANAGER_IMAGE_NAME
output SERVICE_IOT_MANAGER_NAME string = iotManagerApp.outputs.SERVICE_IOT_MANAGER_NAME
output SERVICE_IOT_MANAGER_URI string = iotManagerApp.outputs.SERVICE_IOT_MANAGER_URI
output API_KEY string = apiKey
output ROLE_ASSIGNMENTS_TO_ADD string = '${serviceBusAccess.outputs.missingRoleAssignments} \n${accessToIoTHub.outputs.missingRoleAssignments} \n${solverApp.outputs.missingRoleAssignments}'
