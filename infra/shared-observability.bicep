targetScope = 'resourceGroup'

@description('Azure region for shared observability resources.')
param location string = resourceGroup().location

@secure()
@description('Secret token fal.ai uses to sign provider log drain payloads.')
param falDrainSecret string

@description('Shared Log Analytics workspace name.')
param logAnalyticsWorkspaceName string = 'gifforge-shared-logs'

@description('Globally unique storage account name for the provider drain Function app.')
@minLength(3)
@maxLength(24)
param functionStorageAccountName string = take('gfpdrain${uniqueString(subscription().subscriptionId, resourceGroup().id)}', 24)

@description('Provider drain Function app name.')
param providerDrainFunctionAppName string = take('gifforge-provider-drain-${uniqueString(subscription().subscriptionId, resourceGroup().id)}', 60)

@description('Service principal object ids allowed to wire backend environments to the shared Log Analytics workspace.')
param logAnalyticsContributorPrincipalIds array = []

@description('Tags applied to all shared observability resources.')
param tags object = {
  app: 'gifforge'
  environment: 'shared'
  observabilityScope: 'shared'
}

var observabilityTags = union(tags, {
  app: 'gifforge'
  environment: 'shared'
  observabilityScope: 'shared'
})
var workspaceTags = union(observabilityTags, {
  component: 'observability'
})
var providerDrainTags = union(observabilityTags, {
  component: 'provider-log-drain'
})
var providerLogsTableName = 'ProviderLogs_CL'
var providerLogsStreamName = 'Custom-ProviderLogs'
var hostingPlanName = '${providerDrainFunctionAppName}-plan'
var dataCollectionEndpointName = 'gifforge-provider-logs-dce'
var dataCollectionRuleName = 'gifforge-provider-logs-dcr'
var monitoringMetricsPublisherRoleId = '3913510d-42f4-4e42-8a64-420c390055eb'
var logAnalyticsContributorRoleId = '92aaf0da-9dab-42b6-94a3-d43ce8d16293'
var functionStorageConnectionString = 'DefaultEndpointsProtocol=https;AccountName=${functionStorage.name};AccountKey=${functionStorage.listKeys().keys[0].value};EndpointSuffix=${environment().suffixes.storage}'
var functionContentShareName = toLower(take(providerDrainFunctionAppName, 60))

resource logWorkspace 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: logAnalyticsWorkspaceName
  location: location
  tags: workspaceTags
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
  }
}

resource providerLogsTable 'Microsoft.OperationalInsights/workspaces/tables@2025-02-01' = {
  parent: logWorkspace
  name: providerLogsTableName
  properties: {
    retentionInDays: 30
    totalRetentionInDays: 30
    schema: {
      name: providerLogsTableName
      columns: [
        {
          name: 'TimeGenerated'
          type: 'datetime'
        }
        {
          name: 'ProviderName'
          type: 'string'
        }
        {
          name: 'ReceivedAt'
          type: 'datetime'
        }
        {
          name: 'RawLine'
          type: 'string'
        }
        {
          name: 'ParseError'
          type: 'string'
        }
        {
          name: 'ProviderTimestamp'
          type: 'datetime'
        }
        {
          name: 'Level'
          type: 'string'
        }
        {
          name: 'Message'
          type: 'string'
        }
        {
          name: 'ProviderJobId'
          type: 'string'
        }
        {
          name: 'ProviderRequestId'
          type: 'string'
        }
        {
          name: 'App'
          type: 'string'
        }
        {
          name: 'Revision'
          type: 'string'
        }
        {
          name: 'RunnerId'
          type: 'string'
        }
        {
          name: 'LabelsJson'
          type: 'string'
        }
      ]
    }
  }
}

resource dataCollectionEndpoint 'Microsoft.Insights/dataCollectionEndpoints@2024-03-11' = {
  name: dataCollectionEndpointName
  location: location
  tags: providerDrainTags
  properties: {
    networkAcls: {
      publicNetworkAccess: 'Enabled'
    }
  }
}

resource dataCollectionRule 'Microsoft.Insights/dataCollectionRules@2024-03-11' = {
  name: dataCollectionRuleName
  location: location
  tags: providerDrainTags
  properties: {
    dataCollectionEndpointId: dataCollectionEndpoint.id
    streamDeclarations: {
      '${providerLogsStreamName}': {
        columns: [
          {
            name: 'TimeGenerated'
            type: 'datetime'
          }
          {
            name: 'ProviderName'
            type: 'string'
          }
          {
            name: 'ReceivedAt'
            type: 'datetime'
          }
          {
            name: 'RawLine'
            type: 'string'
          }
          {
            name: 'ParseError'
            type: 'string'
          }
          {
            name: 'ProviderTimestamp'
            type: 'datetime'
          }
          {
            name: 'Level'
            type: 'string'
          }
          {
            name: 'Message'
            type: 'string'
          }
          {
            name: 'ProviderJobId'
            type: 'string'
          }
          {
            name: 'ProviderRequestId'
            type: 'string'
          }
          {
            name: 'App'
            type: 'string'
          }
          {
            name: 'Revision'
            type: 'string'
          }
          {
            name: 'RunnerId'
            type: 'string'
          }
          {
            name: 'LabelsJson'
            type: 'string'
          }
        ]
      }
    }
    destinations: {
      logAnalytics: [
        {
          name: 'sharedProviderLogs'
          workspaceResourceId: logWorkspace.id
        }
      ]
    }
    dataFlows: [
      {
        streams: [
          providerLogsStreamName
        ]
        destinations: [
          'sharedProviderLogs'
        ]
        transformKql: 'source'
        outputStream: 'Custom-${providerLogsTableName}'
      }
    ]
  }
  dependsOn: [
    providerLogsTable
  ]
}

resource functionStorage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: functionStorageAccountName
  location: location
  tags: providerDrainTags
  sku: {
    name: 'Standard_LRS'
  }
  kind: 'StorageV2'
  properties: {
    allowBlobPublicAccess: false
    allowSharedKeyAccess: true
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
    accessTier: 'Hot'
  }
}

resource hostingPlan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: hostingPlanName
  location: location
  tags: providerDrainTags
  kind: 'functionapp'
  sku: {
    name: 'Y1'
    tier: 'Dynamic'
  }
  properties: {}
}

resource providerDrainFunctionApp 'Microsoft.Web/sites@2023-12-01' = {
  name: providerDrainFunctionAppName
  location: location
  tags: providerDrainTags
  kind: 'functionapp'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: hostingPlan.id
    httpsOnly: true
    publicNetworkAccess: 'Enabled'
    siteConfig: {
      appSettings: [
        {
          name: 'FUNCTIONS_EXTENSION_VERSION'
          value: '~4'
        }
        {
          name: 'FUNCTIONS_WORKER_RUNTIME'
          value: 'dotnet-isolated'
        }
        {
          name: 'AzureWebJobsStorage'
          value: functionStorageConnectionString
        }
        {
          name: 'WEBSITE_CONTENTAZUREFILECONNECTIONSTRING'
          value: functionStorageConnectionString
        }
        {
          name: 'WEBSITE_CONTENTSHARE'
          value: functionContentShareName
        }
        {
          name: 'FAL_DRAIN_SECRET'
          value: falDrainSecret
        }
        {
          name: 'AZURE_MONITOR_DCR_ENDPOINT'
          value: dataCollectionEndpoint.properties.logsIngestion.endpoint
        }
        {
          name: 'AZURE_MONITOR_DCR_IMMUTABLE_ID'
          value: dataCollectionRule.properties.immutableId
        }
        {
          name: 'AZURE_MONITOR_DCR_STREAM_NAME'
          value: providerLogsStreamName
        }
      ]
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
    }
  }
}

resource dataCollectionRuleRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(dataCollectionRule.id, providerDrainFunctionApp.id, monitoringMetricsPublisherRoleId)
  scope: dataCollectionRule
  properties: {
    principalId: providerDrainFunctionApp.identity.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', monitoringMetricsPublisherRoleId)
  }
}

resource logAnalyticsContributorRoleAssignments 'Microsoft.Authorization/roleAssignments@2022-04-01' = [
  for principalId in logAnalyticsContributorPrincipalIds: {
    name: guid(logWorkspace.id, principalId, logAnalyticsContributorRoleId)
    scope: logWorkspace
    properties: {
      principalId: principalId
      principalType: 'ServicePrincipal'
      roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', logAnalyticsContributorRoleId)
    }
  }
]

output logAnalyticsWorkspaceName string = logWorkspace.name
output providerDrainFunctionAppName string = providerDrainFunctionApp.name
output falDrainEndpointUrl string = 'https://${providerDrainFunctionApp.properties.defaultHostName}/api/provider-drains/fal'
output dataCollectionEndpoint string = dataCollectionEndpoint.properties.logsIngestion.endpoint
output dataCollectionRuleImmutableId string = dataCollectionRule.properties.immutableId
