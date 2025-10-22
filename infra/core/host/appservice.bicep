param name string
param location string = resourceGroup().location
param tags object = {}

param appServicePlanId string
param runtimeName string
param runtimeVersion string
param managedIdentity bool = false
param alwaysOn bool = false

param appSettings object = {}

resource webApp 'Microsoft.Web/sites@2022-09-01' = {
  name: name
  location: location
  tags: tags
  kind: 'app'
  identity: managedIdentity ? {
    type: 'SystemAssigned'
  } : null
  properties: {
    serverFarmId: appServicePlanId
    httpsOnly: true
    siteConfig: {
      netFrameworkVersion: runtimeName == 'dotnet' ? 'v${runtimeVersion}' : null
      alwaysOn: alwaysOn
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      appSettings: [for key in objectKeys(appSettings): {
        name: key
        value: appSettings[key]
      }]
    }
  }
}

output id string = webApp.id
output name string = webApp.name
output uri string = 'https://${webApp.properties.defaultHostName}'
output identityPrincipalId string = managedIdentity ? webApp.identity.principalId : ''
