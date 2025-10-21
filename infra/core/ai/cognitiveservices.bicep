param name string
param location string = resourceGroup().location
param tags object = {}

param kind string = 'OpenAI'
param sku object = {
  name: 'S0'
}

param deployments array = []

resource cognitiveService 'Microsoft.CognitiveServices/accounts@2023-05-01' = {
  name: name
  location: location
  tags: tags
  kind: kind
  sku: sku
  properties: {
    customSubDomainName: name
    publicNetworkAccess: 'Enabled'
  }
}

resource deployment 'Microsoft.CognitiveServices/accounts/deployments@2023-05-01' = [for deployment in deployments: {
  parent: cognitiveService
  name: deployment.name
  sku: deployment.?sku ?? {
    name: 'Standard'
    capacity: 20
  }
  properties: {
    model: deployment.model
  }
}]

output id string = cognitiveService.id
output name string = cognitiveService.name
output endpoint string = cognitiveService.properties.endpoint
