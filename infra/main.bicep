targetScope = 'resourceGroup'

@description('Primary location for all resources')
param location string = resourceGroup().location

@description('Name of the environment')
param environmentName string

@description('Optional App Service Plan name')
param appServicePlanName string = ''

@description('Optional App Service name')
param appServiceName string = ''

@description('Optional Service Bus namespace name')
param serviceBusNamespaceName string = ''

@description('Optional Cosmos DB account name')
param cosmosAccountName string = ''

@description('Optional Azure OpenAI name')
param openAiName string = ''

@description('Model name for deployment')
param modelName string = 'gpt-4o'

@description('Model format for deployment')
param modelFormat string = 'OpenAI'

@description('Model version for deployment')
param modelVersion string = '2024-08-06'

@description('Model deployment SKU name')
param modelSkuName string = 'GlobalStandard'

@description('Model deployment capacity')
param modelCapacity int = 50

var abbrs = loadJsonContent('./abbreviations.json')
var tags = { 'azd-env-name': environmentName }
var resourceToken = toLower(uniqueString(subscription().id, environmentName, location))

var actualAppServicePlanName = !empty(appServicePlanName) ? appServicePlanName : '${abbrs.webServerFarms}${resourceToken}'
var actualAppServiceName = !empty(appServiceName) ? appServiceName : '${abbrs.webSitesAppService}${resourceToken}'
var actualServiceBusName = !empty(serviceBusNamespaceName) ? serviceBusNamespaceName : '${abbrs.serviceBusNamespaces}${resourceToken}'
var actualCosmosAccountName = !empty(cosmosAccountName) ? cosmosAccountName : '${abbrs.documentDBDatabaseAccounts}${resourceToken}'
var aiServiceName = !empty(openAiName) ? openAiName : 'ai-${resourceToken}'
var deploymentName = modelName

module appServicePlan './core/host/appserviceplan.bicep' = {
  name: 'appserviceplan'
  params: {
    name: actualAppServicePlanName
    location: location
    tags: tags
    sku: {
      name: 'P0v4'
      tier: 'PremiumV4'
      capacity: 1
    }
    kind: ''
    reserved: false
  }
}

module serviceBus './core/messaging/servicebus.bicep' = {
  name: 'servicebus'
  params: {
    name: actualServiceBusName
    location: location
    tags: tags
    queues: [
      {
        name: 'travel-plans'
        maxDeliveryCount: 3
      }
    ]
  }
}

module cosmos './core/database/cosmos.bicep' = {
  name: 'cosmos'
  params: {
    name: actualCosmosAccountName
    location: location
    tags: tags
  }
}

resource aiFoundryResource 'Microsoft.CognitiveServices/accounts@2025-04-01-preview' = {
  name: aiServiceName
  location: location
  tags: tags
  kind: 'AIServices'
  sku: {
    name: 'S0'
  }
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    customSubDomainName: toLower(aiServiceName)
    publicNetworkAccess: 'Enabled'
    allowProjectManagement: true
  }
}

resource aiProject 'Microsoft.CognitiveServices/accounts/projects@2025-04-01-preview' = {
  parent: aiFoundryResource
  name: 'proj-${resourceToken}'
  location: location
  tags: tags
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    displayName: 'AI Project for Travel Planner'
  }
}

resource modelDeployment 'Microsoft.CognitiveServices/accounts/deployments@2025-04-01-preview' = {
  parent: aiFoundryResource
  name: deploymentName
  sku: {
    name: modelSkuName
    capacity: modelCapacity
  }
  properties: {
    model: {
      format: modelFormat
      name: modelName
      version: modelVersion
    }
  }
}

module api './core/host/appservice.bicep' = {
  name: 'api'
  params: {
    name: actualAppServiceName
    location: location
    appServicePlanId: appServicePlan.outputs.id
    runtimeName: 'dotnet'
    runtimeVersion: '9.0'
    managedIdentity: true
    alwaysOn: true
    tags: union(tags, { 'azd-service-name': 'api' })
    appSettings: {
      ServiceBus__Namespace: '${actualServiceBusName}.servicebus.windows.net'
      ServiceBus__QueueName: 'travel-plans'
      CosmosDb__Endpoint: cosmos.outputs.endpoint
      CosmosDb__DatabaseName: cosmos.outputs.databaseName
      CosmosDb__ContainerName: cosmos.outputs.containerName
      App__BaseUrl: 'https://${actualAppServiceName}.azurewebsites.net'
      Agent__AzureOpenAIEndpoint: '${aiFoundryResource.properties.endpoint}api/projects/${aiProject.name}'
      Agent__ModelDeploymentName: deploymentName
    }
  }
}

resource serviceBusDataSenderRole 'Microsoft.Authorization/roleDefinitions@2022-04-01' existing = {
  name: '69a216fc-b8fb-44d8-bc22-1f3c2cd27a39'
  scope: subscription()
}

resource serviceBusDataSenderAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(resourceGroup().id, serviceBusDataSenderRole.id, api.name, serviceBus.name)
  properties: {
    principalId: api.outputs.identityPrincipalId
    roleDefinitionId: serviceBusDataSenderRole.id
    principalType: 'ServicePrincipal'
  }
}

resource serviceBusDataReceiverRole 'Microsoft.Authorization/roleDefinitions@2022-04-01' existing = {
  name: '4f6d3b9b-027b-4f4c-9142-0e5a2a2247e0'
  scope: subscription()
}

resource serviceBusDataReceiverAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(resourceGroup().id, serviceBusDataReceiverRole.id, api.name, serviceBus.name)
  properties: {
    principalId: api.outputs.identityPrincipalId
    roleDefinitionId: serviceBusDataReceiverRole.id
    principalType: 'ServicePrincipal'
  }
}

resource existingCosmosAccount 'Microsoft.DocumentDB/databaseAccounts@2024-05-15' existing = {
  name: actualCosmosAccountName
}

// Cosmos DB Data Contributor role assignment using SQL role definitions
resource cosmosDbDataContributorAssignment 'Microsoft.DocumentDB/databaseAccounts/sqlRoleAssignments@2024-05-15' = {
  parent: existingCosmosAccount
  name: guid(existingCosmosAccount.id, api.name, 'sql-role')
  properties: {
    roleDefinitionId: '${existingCosmosAccount.id}/sqlRoleDefinitions/00000000-0000-0000-0000-000000000002'
    principalId: api.outputs.identityPrincipalId
    scope: existingCosmosAccount.id
  }
}

resource cognitiveServicesContributorRole 'Microsoft.Authorization/roleDefinitions@2022-04-01' existing = {
  name: '25fbc0a9-bd7c-42a3-aa1a-3b75d497ee68'
  scope: subscription()
}

resource cognitiveServicesContributorAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(aiFoundryResource.id, cognitiveServicesContributorRole.id, api.name)
  scope: aiFoundryResource
  properties: {
    principalId: api.outputs.identityPrincipalId
    roleDefinitionId: cognitiveServicesContributorRole.id
    principalType: 'ServicePrincipal'
  }
}

resource cognitiveServicesOpenAIUserRole 'Microsoft.Authorization/roleDefinitions@2022-04-01' existing = {
  name: '5e0bd9bd-7b93-4f28-af87-19fc36ad61bd'
  scope: subscription()
}

resource cognitiveServicesOpenAIUserAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(aiFoundryResource.id, cognitiveServicesOpenAIUserRole.id, api.name)
  scope: aiFoundryResource
  properties: {
    principalId: api.outputs.identityPrincipalId
    roleDefinitionId: cognitiveServicesOpenAIUserRole.id
    principalType: 'ServicePrincipal'
  }
}

resource cognitiveServicesUserRole 'Microsoft.Authorization/roleDefinitions@2022-04-01' existing = {
  name: 'a97b65f3-24c7-4388-baec-2e87135dc908'
  scope: subscription()
}

resource cognitiveServicesUserAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(aiFoundryResource.id, cognitiveServicesUserRole.id, api.name)
  scope: aiFoundryResource
  properties: {
    principalId: api.outputs.identityPrincipalId
    roleDefinitionId: cognitiveServicesUserRole.id
    principalType: 'ServicePrincipal'
  }
}

output AZURE_LOCATION string = location
output AZURE_TENANT_ID string = tenant().tenantId
output AZURE_RESOURCE_GROUP string = resourceGroup().name
output SERVICE_BUS_NAMESPACE string = serviceBus.outputs.name
output SERVICE_BUS_ENDPOINT string = serviceBus.outputs.endpoint
output COSMOS_DB_ACCOUNT_NAME string = cosmos.outputs.name
output COSMOS_DB_ENDPOINT string = cosmos.outputs.endpoint
output COSMOS_DB_DATABASE_NAME string = cosmos.outputs.databaseName
output COSMOS_DB_CONTAINER_NAME string = cosmos.outputs.containerName
output AI_PROJECT_ENDPOINT string = 'https://${aiServiceName}.services.ai.azure.com/api/projects/${aiProject.name}'
output AI_SERVICES_ENDPOINT string = aiFoundryResource.properties.endpoint
output AI_MODEL_DEPLOYMENT_NAME string = deploymentName
output SERVICE_API_IDENTITY_PRINCIPAL_ID string = api.outputs.identityPrincipalId
output SERVICE_API_NAME string = api.outputs.name
output SERVICE_API_URI string = api.outputs.uri
