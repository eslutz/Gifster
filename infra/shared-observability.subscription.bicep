targetScope = 'subscription'

@description('Azure region for the shared observability resource group and resources.')
param location string = 'eastus2'

@description('Resource group that contains shared GifForge observability resources.')
param resourceGroupName string = 'rg-gifforge-shared'

@secure()
@description('Secret token fal.ai uses to sign provider log drain payloads.')
param falDrainSecret string

@description('Tags applied to all shared observability resources.')
param tags object = {
  app: 'gifforge'
  environment: 'shared'
  observabilityScope: 'shared'
}

@description('Service principal object ids allowed to wire backend environments to the shared Log Analytics workspace.')
param logAnalyticsContributorPrincipalIds array = []

resource resourceGroup 'Microsoft.Resources/resourceGroups@2024-03-01' = {
  name: resourceGroupName
  location: location
  tags: tags
}

module sharedObservability './shared-observability.bicep' = {
  name: 'gifforge-shared-observability'
  scope: resourceGroup
  params: {
    location: location
    falDrainSecret: falDrainSecret
    logAnalyticsContributorPrincipalIds: logAnalyticsContributorPrincipalIds
    tags: tags
  }
}

output resourceGroupName string = resourceGroup.name
output logAnalyticsWorkspaceName string = sharedObservability.outputs.logAnalyticsWorkspaceName
output providerDrainFunctionAppName string = sharedObservability.outputs.providerDrainFunctionAppName
output falDrainEndpointUrl string = sharedObservability.outputs.falDrainEndpointUrl
output dataCollectionEndpoint string = sharedObservability.outputs.dataCollectionEndpoint
