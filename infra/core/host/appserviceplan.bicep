param name string
param location string = resourceGroup().location
param tags object = {}

param sku object = {
  name: 'P0v4'
  tier: 'PremiumV4'
  capacity: 1
}

param kind string = 'linux'
param reserved bool = true

resource appServicePlan 'Microsoft.Web/serverfarms@2022-09-01' = {
  name: name
  location: location
  tags: tags
  sku: sku
  kind: kind
  properties: {
    reserved: reserved
  }
}

output id string = appServicePlan.id
output name string = appServicePlan.name
