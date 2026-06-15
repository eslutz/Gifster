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

@description('Apple App Attest app identifier in TeamID.BundleID form. Required for real App Attest verification.')
param appAttestAppIdentifier string = ''

@description('PEM-encoded Apple App Attest root certificate. Public CA material; leave empty to fail closed until configured.')
param appAttestRootCertificatePem string = ''

@description('Enable demo App Attest session bypass for controlled nonprod smoke tests. Ignored for prod.')
param appAttestDemoBypassEnabled bool = false

@description('Generation provider adapter. Use fake for demo/nonprod or external-http for a provider-compatible backend adapter.')
@allowed([
  'fake'
  'external-http'
])
param providerAdapter string = 'fake'

@description('Display name for the external HTTP provider adapter.')
param externalProviderName string = 'external-http'

@description('External HTTP provider job submission URL.')
param externalProviderSubmitUrl string = ''

@description('External HTTP provider result URL template. Supports {providerJobId} and {jobId}.')
param externalProviderResultUrlTemplate string = ''

@secure()
@description('Optional Authorization header value for the external HTTP provider, such as "Bearer <token>". Stored as a Container Apps secret.')
param externalProviderAuthorization string = ''

@description('Minimum Container Apps replicas. Use 0 for scale-to-zero, or 1+ when warm API capacity is required.')
@minValue(0)
@maxValue(10)
param minReplicas int = 0

@description('Maximum Container Apps replicas.')
@minValue(1)
@maxValue(50)
param maxReplicas int = environmentName == 'prod' ? 10 : 5

@description('Minimum worker Container Apps replicas. Use 0 for queue-driven scale-to-zero, or 1+ when a warm worker is required.')
@minValue(0)
@maxValue(10)
param workerMinReplicas int = 0

@description('HTTP concurrency target for Container Apps scale-out.')
@minValue(1)
@maxValue(1000)
param concurrentRequests int = 50

@description('Hours before generated job metadata, prompts, selected source-image payloads, and result links expire.')
@minValue(1)
@maxValue(168)
param generationJobRetentionHours int = 24

@description('Days before temporary provider result and source-image blobs are deleted by Azure Storage lifecycle policy.')
@minValue(1)
@maxValue(30)
param temporaryBlobRetentionDays int = 2

@description('Minutes between backend cleanup passes for expired generation job rows.')
@minValue(5)
@maxValue(1440)
param retentionCleanupIntervalMinutes int = 360

@description('Maximum expired generation job rows deleted in one backend cleanup pass.')
@minValue(1)
@maxValue(1000)
param retentionCleanupBatchSize int = 100

@description('Tags applied to all resources.')
param tags object = {
  app: 'gifster'
  environment: environmentName
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
    appAttestAppIdentifier: appAttestAppIdentifier
    appAttestRootCertificatePem: appAttestRootCertificatePem
    appAttestDemoBypassEnabled: appAttestDemoBypassEnabled
    providerAdapter: providerAdapter
    externalProviderName: externalProviderName
    externalProviderSubmitUrl: externalProviderSubmitUrl
    externalProviderResultUrlTemplate: externalProviderResultUrlTemplate
    externalProviderAuthorization: externalProviderAuthorization
    minReplicas: minReplicas
    maxReplicas: maxReplicas
    workerMinReplicas: workerMinReplicas
    concurrentRequests: concurrentRequests
    generationJobRetentionHours: generationJobRetentionHours
    temporaryBlobRetentionDays: temporaryBlobRetentionDays
    retentionCleanupIntervalMinutes: retentionCleanupIntervalMinutes
    retentionCleanupBatchSize: retentionCleanupBatchSize
    tags: tags
  }
}

output resourceGroupName string = resourceGroup.name
output containerAppName string = backend.outputs.containerAppName
output containerAppFqdn string = backend.outputs.containerAppFqdn
output storageAccountName string = backend.outputs.storageAccountName
output keyVaultUri string = backend.outputs.keyVaultUri
