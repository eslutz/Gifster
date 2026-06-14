targetScope = 'subscription'

@description('Gifster environment to deploy.')
@allowed([
  'nonprod'
  'prod'
])
param environmentName string = 'nonprod'

@description('Azure region for the resource group and contained resources.')
param location string = 'eastus'

@description('Resource group that will contain the Gifster backend resources.')
param resourceGroupName string = 'rg-gifster-${environmentName}'

@description('Container image for the Gifster backend. Use a real pushed image before production deployment.')
param containerImage string

@description('Public base URL returned in backend status and download URLs. Leave empty to derive from incoming request host.')
param publicBaseUrl string = ''

@description('Minimum Container Apps replicas. Use 0 for nonprod scale-to-zero.')
@minValue(0)
@maxValue(10)
param minReplicas int = environmentName == 'prod' ? 1 : 0

@description('Maximum Container Apps replicas.')
@minValue(1)
@maxValue(50)
param maxReplicas int = environmentName == 'prod' ? 10 : 5

@description('HTTP concurrency target for Container Apps scale-out.')
@minValue(1)
@maxValue(1000)
param concurrentRequests int = 50

@description('Tags applied to all resources.')
param tags object = {
  app: 'gifster'
  environment: environmentName
  managedBy: 'bicep'
}

resource resourceGroup 'Microsoft.Resources/resourceGroups@2024-03-01' = {
  name: resourceGroupName
  location: location
  tags: tags
}

module backend './main.bicep' = {
  name: 'gifster-${environmentName}-backend'
  scope: resourceGroup
  params: {
    environmentName: environmentName
    location: location
    containerImage: containerImage
    publicBaseUrl: publicBaseUrl
    minReplicas: minReplicas
    maxReplicas: maxReplicas
    concurrentRequests: concurrentRequests
    tags: tags
  }
}

output resourceGroupName string = resourceGroup.name
output containerAppName string = backend.outputs.containerAppName
output containerAppFqdn string = backend.outputs.containerAppFqdn
output storageAccountName string = backend.outputs.storageAccountName
output keyVaultUri string = backend.outputs.keyVaultUri
