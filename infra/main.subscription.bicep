targetScope = 'subscription'

@description('GifForge environment to deploy.')
@allowed([
  'nonprod'
  'prod'
])
param environmentName string = 'nonprod'

@description('Azure region for the resource group and contained resources.')
param location string = 'eastus'

@description('Resource group that will contain the GifForge backend resources.')
param resourceGroupName string = 'rg-gifforge-${environmentName}'

@description('Container image for the GifForge backend. Use a real pushed image before production deployment.')
param containerImage string

@description('Public base URL returned in backend status and download URLs. Leave empty to derive from incoming request host.')
param publicBaseUrl string = ''

@description('Azure SQL server FQDN used for GifForge account, auth, purchase, and credit state. Leave empty for local/in-memory development.')
param sqlServer string = ''

@description('Azure SQL database name used for GifForge account, auth, purchase, and credit state. Leave empty for local/in-memory development.')
param sqlDatabase string = ''

@description('Comma-separated Apple Sign in audience/client ids accepted in identity tokens.')
param appleIdTokenAudiences string = 'dev.ericslutz.gifforge'

@description('Apple bundle id expected in StoreKit transaction JWS payloads.')
param appStoreBundleId string = 'dev.ericslutz.gifforge'

@description('PEM-encoded Apple root certificate for StoreKit and App Store Server Notification JWS chain validation. Required when GIFFORGE_IAP_DEMO_BYPASS=false; empty fails closed.')
param appStoreJwsRootCertificatePem string = ''

@description('Apple App Attest app identifier in TeamID.BundleID form. Required for real App Attest verification.')
param appAttestAppIdentifier string = ''

@description('PEM-encoded Apple App Attest root certificate. Public CA material; leave empty to fail closed until configured.')
param appAttestRootCertificatePem string = ''

@description('Enable demo App Attest session bypass for direct lower-environment experiments. GitHub deploy workflows pass false, and the value is ignored for prod.')
param appAttestDemoBypassEnabled bool = false

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

@description('Days before temporary provider result blobs are deleted by Azure Storage lifecycle policy.')
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
  app: 'gifforge'
  environment: environmentName
}

resource resourceGroup 'Microsoft.Resources/resourceGroups@2024-03-01' = {
  name: resourceGroupName
  location: location
  tags: tags
}

module backend './main.bicep' = {
  name: 'gifforge-${environmentName}-backend'
  scope: resourceGroup
  params: {
    environmentName: environmentName
    location: location
    containerImage: containerImage
    publicBaseUrl: publicBaseUrl
    sqlServer: sqlServer
    sqlDatabase: sqlDatabase
    appleIdTokenAudiences: appleIdTokenAudiences
    appStoreBundleId: appStoreBundleId
    appStoreJwsRootCertificatePem: appStoreJwsRootCertificatePem
    appAttestAppIdentifier: appAttestAppIdentifier
    appAttestRootCertificatePem: appAttestRootCertificatePem
    appAttestDemoBypassEnabled: appAttestDemoBypassEnabled
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
output sqlServer string = backend.outputs.sqlServer
output sqlDatabase string = backend.outputs.sqlDatabase
