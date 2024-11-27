@description('The name of the Web PubSub resource')
param webPubSubName string

@description('The name of the hub')
param hubName string

@description('The location of the Web PubSub resource')
param location string = resourceGroup().location

@description('The SKU of the Web PubSub resource')
param skuName string = 'Standard_S1'

@description('URL of the event handler')
param eventHandlerUrl string

param managedIdentityName string

resource userIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' existing = if (!empty(managedIdentityName)) {
  name: managedIdentityName
}

resource webPubSub 'Microsoft.SignalRService/webPubSub@2024-04-01-preview' = {
  name: webPubSubName
  location: location
  kind: 'WebPubSub'
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${userIdentity.id}': {}
    }
  }
  sku: {
    name: skuName
    capacity: 1
  }
  properties: {
    tls: {
      clientCertEnabled: false
    }
    publicNetworkAccess: 'Enabled'
  }
}

resource hub 'Microsoft.SignalRService/WebPubSub/hubs@2023-03-01-preview' = {
  parent: webPubSub
  name: hubName
  properties: {
    eventHandlers: [
      {
        urlTemplate: eventHandlerUrl
        userEventPattern: '*'
        systemEvents: [
          'connect'
        ]
        // this needs to be enabled with a valid app registration "api://<client_id>"
        // auth: {
        //   type: 'ManagedIdentity'
        //   managedIdentity: {
        //     resource: userIdentity.properties.clientId
        //   }
        // }
      }
    ]
    anonymousConnectPolicy: 'deny'
  }
}

output webPubSubResourceId string = webPubSub.id
output webPubSubResourceName string = webPubSub.name
output webPubSubHostName string = webPubSub.properties.hostName
