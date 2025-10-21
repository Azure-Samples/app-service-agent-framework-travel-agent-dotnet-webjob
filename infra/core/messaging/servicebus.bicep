param name string
param location string = resourceGroup().location
param tags object = {}

param queues array = []

resource serviceBusNamespace 'Microsoft.ServiceBus/namespaces@2022-10-01-preview' = {
  name: name
  location: location
  tags: tags
  sku: {
    name: 'Standard'
    tier: 'Standard'
  }
  properties: {
    minimumTlsVersion: '1.2'
  }
}

resource serviceBusQueues 'Microsoft.ServiceBus/namespaces/queues@2022-10-01-preview' = [for queue in queues: {
  parent: serviceBusNamespace
  name: queue.name
  properties: {
    maxDeliveryCount: queue.?maxDeliveryCount ?? 10
    lockDuration: 'PT1M'
    requiresDuplicateDetection: false
    requiresSession: false
    deadLetteringOnMessageExpiration: true
  }
}]

output id string = serviceBusNamespace.id
output name string = serviceBusNamespace.name
output endpoint string = '${serviceBusNamespace.name}.servicebus.windows.net'
