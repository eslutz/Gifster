targetScope = 'resourceGroup'

@description('Short environment name used in resource names.')
@allowed([
  'nonprod'
  'prod'
])
param environmentName string = 'nonprod'

@description('Azure region for all resources.')
param location string = resourceGroup().location

@description('Container image for the GifForge backend. Use a real pushed image before production deployment.')
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

@description('Minimum Container Apps replicas. Use 0 for scale-to-zero in lower environments.')
@minValue(0)
@maxValue(10)
param minReplicas int = 0

@description('Maximum Container Apps replicas.')
@minValue(1)
@maxValue(50)
param maxReplicas int = 5

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

@description('Globally unique storage account name. Lowercase letters and numbers only.')
@minLength(3)
@maxLength(24)
param storageAccountName string = take('gifforge${environmentName}${uniqueString(subscription().subscriptionId, resourceGroup().id, environmentName)}', 24)

@description('Tags applied to all resources.')
param tags object = {
  app: 'gifforge'
  environment: environmentName
}

var nameSeed = toLower(uniqueString(subscription().subscriptionId, resourceGroup().id, environmentName))
var prefix = 'gifforge-${environmentName}-${nameSeed}'
var containerAppPrefix = 'gifforge-${environmentName}-${take(nameSeed, 7)}'
var keyVaultName = take('gkv-${environmentName}-${nameSeed}', 24)

var generationQueueName = 'generation-jobs'
var providerCallbackQueueName = 'provider-callbacks'
var deletionQueueName = 'media-deletions'
var resultContainerName = 'provider-results'
var sourceContainerName = 'source-images'
var jobTableName = 'GenerationJobs'
var appAttestStateTableName = 'AppAttestState'

var storageBlobDataContributorRoleId = 'ba92f5b4-2d11-453d-a403-e96b0029c9fe'
var storageQueueDataContributorRoleId = '974c5e8b-45b9-4653-ba55-5f855dd0fb88'
var storageTableDataContributorRoleId = '0a9a7e1f-b9d0-4cc4-a60d-0319b160aaa3'
var keyVaultSecretsUserRoleId = '4633458b-17de-408a-b874-0445c86b69e6'
var externalProviderAuthorizationSecretName = 'external-provider-authorization'

resource logWorkspace 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: '${prefix}-logs'
  location: location
  tags: tags
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
  }
}

resource appIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: '${prefix}-id'
  location: location
  tags: tags
}

resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: storageAccountName
  location: location
  tags: tags
  sku: {
    name: 'Standard_LRS'
  }
  kind: 'StorageV2'
  properties: {
    allowBlobPublicAccess: false
    allowSharedKeyAccess: false
    defaultToOAuthAuthentication: true
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
    accessTier: 'Hot'
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: storage
  name: 'default'
  properties: {
    deleteRetentionPolicy: {
      enabled: true
      days: 7
    }
    containerDeleteRetentionPolicy: {
      enabled: true
      days: 7
    }
  }
}

resource resultContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: resultContainerName
  properties: {
    publicAccess: 'None'
  }
}

resource sourceContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: sourceContainerName
  properties: {
    publicAccess: 'None'
  }
}

resource temporaryMediaLifecyclePolicy 'Microsoft.Storage/storageAccounts/managementPolicies@2023-05-01' = {
  parent: storage
  name: 'default'
  properties: {
    policy: {
      rules: [
        {
          enabled: true
          name: 'deleteTemporaryMedia'
          type: 'Lifecycle'
          definition: {
            actions: {
              baseBlob: {
                delete: {
                  daysAfterModificationGreaterThan: temporaryBlobRetentionDays
                }
              }
            }
            filters: {
              blobTypes: [
                'blockBlob'
              ]
              prefixMatch: [
                '${resultContainerName}/'
                '${sourceContainerName}/'
              ]
            }
          }
        }
      ]
    }
  }
  dependsOn: [
    resultContainer
    sourceContainer
  ]
}

resource queueService 'Microsoft.Storage/storageAccounts/queueServices@2023-05-01' = {
  parent: storage
  name: 'default'
}

resource generationQueue 'Microsoft.Storage/storageAccounts/queueServices/queues@2023-05-01' = {
  parent: queueService
  name: generationQueueName
}

resource providerCallbackQueue 'Microsoft.Storage/storageAccounts/queueServices/queues@2023-05-01' = {
  parent: queueService
  name: providerCallbackQueueName
}

resource deletionQueue 'Microsoft.Storage/storageAccounts/queueServices/queues@2023-05-01' = {
  parent: queueService
  name: deletionQueueName
}

resource tableService 'Microsoft.Storage/storageAccounts/tableServices@2023-05-01' = {
  parent: storage
  name: 'default'
}

resource jobsTable 'Microsoft.Storage/storageAccounts/tableServices/tables@2023-05-01' = {
  parent: tableService
  name: jobTableName
}

resource appAttestStateTable 'Microsoft.Storage/storageAccounts/tableServices/tables@2023-05-01' = {
  parent: tableService
  name: appAttestStateTableName
}

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: keyVaultName
  location: location
  tags: tags
  properties: {
    tenantId: tenant().tenantId
    sku: {
      family: 'A'
      name: 'standard'
    }
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 7
    publicNetworkAccess: 'Enabled'
    accessPolicies: []
    ...(environmentName == 'prod' ? {
      enablePurgeProtection: true
    } : {})
  }
}

resource containerEnvironment 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: '${prefix}-env'
  location: location
  tags: tags
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logWorkspace.properties.customerId
        sharedKey: logWorkspace.listKeys().primarySharedKey
      }
    }
    workloadProfiles: [
      {
        name: 'Consumption'
        workloadProfileType: 'Consumption'
      }
    ]
  }
}

resource containerApp 'Microsoft.App/containerApps@2025-02-02-preview' = {
  name: '${containerAppPrefix}-api'
  location: location
  tags: tags
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${appIdentity.id}': {}
    }
  }
  properties: {
    managedEnvironmentId: containerEnvironment.id
    workloadProfileName: 'Consumption'
    configuration: {
      activeRevisionsMode: 'Single'
      secrets: empty(externalProviderAuthorization) ? [] : [
        {
          name: externalProviderAuthorizationSecretName
          value: externalProviderAuthorization
        }
      ]
      ingress: {
        external: true
        targetPort: 8080
        transport: 'auto'
        allowInsecure: false
        traffic: [
          {
            latestRevision: true
            weight: 100
          }
        ]
      }
    }
    template: {
      containers: [
        {
          name: 'api'
          image: containerImage
          env: concat([
            {
              name: 'ASPNETCORE_HTTP_PORTS'
              value: '8080'
            }
            {
              name: 'GIFFORGE_PUBLIC_BASE_URL'
              value: publicBaseUrl
            }
            {
              name: 'GIFFORGE_STORAGE_ACCOUNT_NAME'
              value: storage.name
            }
            {
              name: 'GIFFORGE_GENERATION_QUEUE_NAME'
              value: generationQueueName
            }
            {
              name: 'GIFFORGE_PROVIDER_CALLBACK_QUEUE_NAME'
              value: providerCallbackQueueName
            }
            {
              name: 'GIFFORGE_DELETION_QUEUE_NAME'
              value: deletionQueueName
            }
            {
              name: 'GIFFORGE_RESULTS_CONTAINER_NAME'
              value: resultContainerName
            }
            {
              name: 'GIFFORGE_SOURCE_IMAGES_CONTAINER_NAME'
              value: sourceContainerName
            }
            {
              name: 'GIFFORGE_JOBS_TABLE_NAME'
              value: jobTableName
            }
            {
              name: 'GIFFORGE_APP_ATTEST_STATE_TABLE_NAME'
              value: appAttestStateTable.name
            }
            {
              name: 'GIFFORGE_KEY_VAULT_URI'
              value: keyVault.properties.vaultUri
            }
            {
              name: 'GIFFORGE_PROVIDER_ADAPTER'
              value: providerAdapter
            }
            {
              name: 'GIFFORGE_EXTERNAL_PROVIDER_NAME'
              value: externalProviderName
            }
            {
              name: 'GIFFORGE_EXTERNAL_PROVIDER_SUBMIT_URL'
              value: externalProviderSubmitUrl
            }
            {
              name: 'GIFFORGE_EXTERNAL_PROVIDER_RESULT_URL_TEMPLATE'
              value: externalProviderResultUrlTemplate
            }
            {
              name: 'GIFFORGE_APP_ATTEST_REQUIRED'
              value: 'true'
            }
            {
              name: 'GIFFORGE_APP_ATTEST_DEMO_BYPASS'
              value: appAttestDemoBypassEnabled && environmentName != 'prod' ? 'true' : 'false'
            }
            {
              name: 'GIFFORGE_APP_ATTEST_APP_IDENTIFIER'
              value: appAttestAppIdentifier
            }
            {
              name: 'GIFFORGE_APP_ATTEST_ROOT_CERTIFICATE_PEM'
              value: appAttestRootCertificatePem
            }
            {
              name: 'GIFFORGE_GENERATION_JOB_RETENTION_HOURS'
              value: string(generationJobRetentionHours)
            }
            {
              name: 'GIFFORGE_RETENTION_CLEANUP_ENABLED'
              value: 'true'
            }
            {
              name: 'GIFFORGE_RETENTION_CLEANUP_INTERVAL_MINUTES'
              value: string(retentionCleanupIntervalMinutes)
            }
            {
              name: 'GIFFORGE_RETENTION_CLEANUP_BATCH_SIZE'
              value: string(retentionCleanupBatchSize)
            }
            {
              name: 'AZURE_CLIENT_ID'
              value: appIdentity.properties.clientId
            }
          ], empty(externalProviderAuthorization) ? [] : [
            {
              name: 'GIFFORGE_EXTERNAL_PROVIDER_AUTHORIZATION'
              secretRef: externalProviderAuthorizationSecretName
            }
          ])
          resources: {
            cpu: json('0.25')
            memory: '0.5Gi'
          }
          probes: [
            {
              type: 'Liveness'
              httpGet: {
                path: '/health'
                port: 8080
              }
              initialDelaySeconds: 10
              periodSeconds: 30
            }
            {
              type: 'Readiness'
              httpGet: {
                path: '/health'
                port: 8080
              }
              initialDelaySeconds: 5
              periodSeconds: 10
            }
          ]
        }
      ]
      scale: {
        minReplicas: minReplicas
        maxReplicas: maxReplicas
        rules: [
          {
            name: 'http-scale'
            custom: {
              type: 'http'
              metadata: {
                concurrentRequests: string(concurrentRequests)
              }
            }
          }
        ]
      }
    }
  }
}

resource workerContainerApp 'Microsoft.App/containerApps@2025-02-02-preview' = {
  name: '${containerAppPrefix}-worker'
  location: location
  tags: tags
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${appIdentity.id}': {}
    }
  }
  properties: {
    managedEnvironmentId: containerEnvironment.id
    workloadProfileName: 'Consumption'
    configuration: {
      activeRevisionsMode: 'Single'
      secrets: empty(externalProviderAuthorization) ? [] : [
        {
          name: externalProviderAuthorizationSecretName
          value: externalProviderAuthorization
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'worker'
          image: containerImage
          env: concat([
            {
              name: 'ASPNETCORE_HTTP_PORTS'
              value: '8080'
            }
            {
              name: 'GIFFORGE_WORKER_ENABLED'
              value: 'true'
            }
            {
              name: 'GIFFORGE_PUBLIC_BASE_URL'
              value: publicBaseUrl
            }
            {
              name: 'GIFFORGE_STORAGE_ACCOUNT_NAME'
              value: storage.name
            }
            {
              name: 'GIFFORGE_GENERATION_QUEUE_NAME'
              value: generationQueueName
            }
            {
              name: 'GIFFORGE_PROVIDER_CALLBACK_QUEUE_NAME'
              value: providerCallbackQueueName
            }
            {
              name: 'GIFFORGE_DELETION_QUEUE_NAME'
              value: deletionQueueName
            }
            {
              name: 'GIFFORGE_RESULTS_CONTAINER_NAME'
              value: resultContainerName
            }
            {
              name: 'GIFFORGE_SOURCE_IMAGES_CONTAINER_NAME'
              value: sourceContainerName
            }
            {
              name: 'GIFFORGE_JOBS_TABLE_NAME'
              value: jobTableName
            }
            {
              name: 'GIFFORGE_APP_ATTEST_STATE_TABLE_NAME'
              value: appAttestStateTable.name
            }
            {
              name: 'GIFFORGE_KEY_VAULT_URI'
              value: keyVault.properties.vaultUri
            }
            {
              name: 'GIFFORGE_PROVIDER_ADAPTER'
              value: providerAdapter
            }
            {
              name: 'GIFFORGE_EXTERNAL_PROVIDER_NAME'
              value: externalProviderName
            }
            {
              name: 'GIFFORGE_EXTERNAL_PROVIDER_SUBMIT_URL'
              value: externalProviderSubmitUrl
            }
            {
              name: 'GIFFORGE_EXTERNAL_PROVIDER_RESULT_URL_TEMPLATE'
              value: externalProviderResultUrlTemplate
            }
            {
              name: 'GIFFORGE_APP_ATTEST_REQUIRED'
              value: 'true'
            }
            {
              name: 'GIFFORGE_APP_ATTEST_DEMO_BYPASS'
              value: appAttestDemoBypassEnabled && environmentName != 'prod' ? 'true' : 'false'
            }
            {
              name: 'GIFFORGE_APP_ATTEST_APP_IDENTIFIER'
              value: appAttestAppIdentifier
            }
            {
              name: 'GIFFORGE_APP_ATTEST_ROOT_CERTIFICATE_PEM'
              value: appAttestRootCertificatePem
            }
            {
              name: 'GIFFORGE_GENERATION_JOB_RETENTION_HOURS'
              value: string(generationJobRetentionHours)
            }
            {
              name: 'GIFFORGE_RETENTION_CLEANUP_ENABLED'
              value: 'true'
            }
            {
              name: 'GIFFORGE_RETENTION_CLEANUP_INTERVAL_MINUTES'
              value: string(retentionCleanupIntervalMinutes)
            }
            {
              name: 'GIFFORGE_RETENTION_CLEANUP_BATCH_SIZE'
              value: string(retentionCleanupBatchSize)
            }
            {
              name: 'AZURE_CLIENT_ID'
              value: appIdentity.properties.clientId
            }
          ], empty(externalProviderAuthorization) ? [] : [
            {
              name: 'GIFFORGE_EXTERNAL_PROVIDER_AUTHORIZATION'
              secretRef: externalProviderAuthorizationSecretName
            }
          ])
          resources: {
            cpu: json('0.25')
            memory: '0.5Gi'
          }
          probes: [
            {
              type: 'Liveness'
              httpGet: {
                path: '/health'
                port: 8080
              }
              initialDelaySeconds: 10
              periodSeconds: 30
            }
            {
              type: 'Readiness'
              httpGet: {
                path: '/health'
                port: 8080
              }
              initialDelaySeconds: 5
              periodSeconds: 10
            }
          ]
        }
      ]
      scale: {
        minReplicas: workerMinReplicas
        maxReplicas: maxReplicas
        rules: [
          {
            name: 'generation-queue'
            custom: {
              type: 'azure-queue'
              metadata: {
                accountName: storage.name
                queueName: generationQueueName
                queueLength: '1'
              }
              identity: appIdentity.id
            }
          }
        ]
      }
    }
  }
}

resource blobRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storage.id, appIdentity.id, storageBlobDataContributorRoleId)
  scope: storage
  properties: {
    principalId: appIdentity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', storageBlobDataContributorRoleId)
  }
}

resource queueRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storage.id, appIdentity.id, storageQueueDataContributorRoleId)
  scope: storage
  properties: {
    principalId: appIdentity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', storageQueueDataContributorRoleId)
  }
}

resource tableRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storage.id, appIdentity.id, storageTableDataContributorRoleId)
  scope: storage
  properties: {
    principalId: appIdentity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', storageTableDataContributorRoleId)
  }
}

resource keyVaultRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyVault.id, appIdentity.id, keyVaultSecretsUserRoleId)
  scope: keyVault
  properties: {
    principalId: appIdentity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', keyVaultSecretsUserRoleId)
  }
}

output containerAppName string = containerApp.name
output containerAppFqdn string = containerApp.properties.configuration.ingress.fqdn
output workerContainerAppName string = workerContainerApp.name
output managedIdentityClientId string = appIdentity.properties.clientId
output storageAccountName string = storage.name
output keyVaultUri string = keyVault.properties.vaultUri
output generationQueueName string = generationQueueName
output resultsContainerName string = resultContainerName
output jobsTableName string = jobTableName
output appAttestStateTableName string = appAttestStateTable.name
