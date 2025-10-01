@description('Location for all resources')
param location string = resourceGroup().location

@description('Container App name')
param containerAppName string

@description('Environment ID')
param environmentId string

@description('Cosmos DB account name')
param cosmosDbAccountName string

@description('Container image tag')
param imageTag string = 'latest'

@description('Container registry server hostname')
param containerRegistryServer string = 'ghcr.io'

@description('Container registry repository')
param containerRegistryRepository string

@description('MongoDB connection string for the application')
@secure()
param mongoConnectionString string

@description('Google OAuth client ID')
@secure()
param googleClientId string

@description('Google OAuth client secret')
@secure()
param googleClientSecret string

@description('Initial administrator email address')
param initialUserEmail string

@description('Git author name used by the app')
param gitAuthorName string

@description('Git author email used by the app')
param gitAuthorEmail string

@description('Git access token for deployment metadata sync')
@secure()
param gitToken string

@description('Git clone URL for deployment metadata sync')
@secure()
param gitCloneUrl string

// Reference to existing Cosmos DB cluster
resource cosmosMongoCluster 'Microsoft.DocumentDB/mongoClusters@2024-07-01' existing = {
  name: cosmosDbAccountName
}

// Container App - configured with secrets and usage monitoring environment variables
resource containerApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: containerAppName
  location: location
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    managedEnvironmentId: environmentId
    configuration: {
      secrets: [
        {
          name: 'aspnetcore-environment'
          value: 'Production'
        }
        {
          name: 'mongo-connection-string'
          value: mongoConnectionString
        }
        {
          name: 'mongo-database-name'
          value: 'FestivalManagement'
        }
        {
          name: 'google-client-id'
          value: googleClientId
        }
        {
          name: 'google-client-secret'
          value: googleClientSecret
        }
        {
          name: 'initial-user-email'
          value: initialUserEmail
        }
        {
          name: 'freetier-enable-banner'
          value: 'true'
        }
        {
          name: 'freetier-budget-vcpu-seconds'
          value: '180000'
        }
        {
          name: 'freetier-budget-gib-seconds'
          value: '360000'
        }
        {
          name: 'freetier-resource-vcpu-per-replica'
          value: '0.25'
        }
        {
          name: 'freetier-resource-memory-gib-per-replica'
          value: '0.5'
        }
        {
          name: 'freetier-resource-replica-factor'
          value: '1'
        }
        {
          name: 'freetier-enforce-request-daily-cap'
          value: 'false'
        }
        {
          name: 'freetier-data-budget-gb'
          value: '100'
        }
        {
          name: 'freetier-requests-budget'
          value: '2000000'
        }
        {
          name: 'freetier-cosmos-enabled'
          value: 'true'
        }
        {
          name: 'freetier-cosmos-subscription-id'
          value: subscription().subscriptionId
        }
        {
          name: 'freetier-cosmos-resource-group'
          value: resourceGroup().name
        }
        {
          name: 'freetier-cosmos-account-name'
          value: cosmosDbAccountName
        }
        {
          name: 'freetier-cosmos-database-name'
          value: 'FestivalManagement'
        }
        {
          name: 'freetier-cosmos-account-resource-id'
          value: cosmosMongoCluster.id
        }
        {
          name: 'freetier-cosmos-provisioning'
          value: 'vCore'
        }
        {
          name: 'freetier-cosmos-free-tier-storage-gb'
          value: '32'
        }
        {
          name: 'freetier-cosmos-free-tier-vcore-storage-gb'
          value: '32'
        }
        {
          name: 'freetier-cosmos-warn-ru-percent'
          value: '90'
        }
        {
          name: 'freetier-cosmos-warn-storage-percent'
          value: '90'
        }
        {
          name: 'freetier-cosmos-refresh-minutes'
          value: '60'
        }
        {
          name: 'azureusage-enabled'
          value: 'true'
        }
        {
          name: 'azureusage-containerapp-name'
          value: containerAppName
        }
        {
          name: 'azureusage-resource-group'
          value: resourceGroup().name
        }
        {
          name: 'azureusage-subscription-id'
          value: subscription().subscriptionId
        }
        {
          name: 'azureusage-metrics-refresh-minutes'
          value: '10'
        }
        {
          name: 'azureusage-cost-refresh-minutes'
          value: '360'
        }
        {
          name: 'gitsettings-remote-name'
          value: 'origin'
        }
        {
          name: 'freetier-cosmos-collection-name-0'
          value: 'TextKeyValues'
        }
        {
          name: 'freetier-cosmos-collection-name-1'
          value: 'ImageKeyValues'
        }
        {
          name: 'gitsettings-author-name'
          value: gitAuthorName
        }
        {
          name: 'gitsettings-author-email'
          value: gitAuthorEmail
        }
        {
          name: 'git-token'
          value: gitToken
        }
        {
          name: 'git-clone-url'
          #disable-next-line use-secure-value-for-secure-inputs
          value: gitCloneUrl
        }
      ]
      ingress: {
        external: true
        targetPort: 8080
        allowInsecure: false
        traffic: [
          {
            weight: 100
            latestRevision: true
          }
        ]
      }
    }
    template: {
      containers: [
        {
          name: 'festivalManagementWeb'
          image: '${containerRegistryServer}/${containerRegistryRepository}:${imageTag}'
          env: [
            {
              name: 'ASPNETCORE_ENVIRONMENT'
              secretRef: 'aspnetcore-environment'
            }
            {
              name: 'MongoDbSettings__ConnectionString'
              secretRef: 'mongo-connection-string'
            }
            {
              name: 'MongoDbSettings__DatabaseName'
              secretRef: 'mongo-database-name'
            }
            {
              name: 'Authentication__Google__ClientId'
              secretRef: 'google-client-id'
            }
            {
              name: 'Authentication__Google__ClientSecret'
              secretRef: 'google-client-secret'
            }
            {
              name: 'InitialUser__Email'
              secretRef: 'initial-user-email'
            }
            {
              name: 'FreeTier__EnableBanner'
              secretRef: 'freetier-enable-banner'
            }
            {
              name: 'FreeTier__BudgetVcpuSeconds'
              secretRef: 'freetier-budget-vcpu-seconds'
            }
            {
              name: 'FreeTier__BudgetGiBSeconds'
              secretRef: 'freetier-budget-gib-seconds'
            }
            {
              name: 'FreeTier__Resource__VcpuPerReplica'
              secretRef: 'freetier-resource-vcpu-per-replica'
            }
            {
              name: 'FreeTier__Resource__MemoryGiBPerReplica'
              secretRef: 'freetier-resource-memory-gib-per-replica'
            }
            {
              name: 'FreeTier__Resource__ReplicaFactor'
              secretRef: 'freetier-resource-replica-factor'
            }
            {
              name: 'FreeTier__EnforceRequestDailyCap'
              secretRef: 'freetier-enforce-request-daily-cap'
            }
            {
              name: 'FreeTier__Data__BudgetGb'
              secretRef: 'freetier-data-budget-gb'
            }
            {
              name: 'FreeTier__Requests__Budget'
              secretRef: 'freetier-requests-budget'
            }
            {
              name: 'FreeTier__Cosmos__Enabled'
              secretRef: 'freetier-cosmos-enabled'
            }
            {
              name: 'FreeTier__Cosmos__SubscriptionId'
              secretRef: 'freetier-cosmos-subscription-id'
            }
            {
              name: 'FreeTier__Cosmos__ResourceGroup'
              secretRef: 'freetier-cosmos-resource-group'
            }
            {
              name: 'FreeTier__Cosmos__AccountName'
              secretRef: 'freetier-cosmos-account-name'
            }
            {
              name: 'FreeTier__Cosmos__DatabaseName'
              secretRef: 'freetier-cosmos-database-name'
            }
            {
              name: 'FreeTier__Cosmos__AccountResourceId'
              secretRef: 'freetier-cosmos-account-resource-id'
            }
            {
              name: 'FreeTier__Cosmos__Provisioning'
              secretRef: 'freetier-cosmos-provisioning'
            }
            {
              name: 'FreeTier__Cosmos__FreeTierStorageGb'
              secretRef: 'freetier-cosmos-free-tier-storage-gb'
            }
            {
              name: 'FreeTier__Cosmos__FreeTierVCoreStorageGb'
              secretRef: 'freetier-cosmos-free-tier-vcore-storage-gb'
            }
            {
              name: 'FreeTier__Cosmos__WarnRuPercent'
              secretRef: 'freetier-cosmos-warn-ru-percent'
            }
            {
              name: 'FreeTier__Cosmos__WarnStoragePercent'
              secretRef: 'freetier-cosmos-warn-storage-percent'
            }
            {
              name: 'FreeTier__Cosmos__RefreshMinutes'
              secretRef: 'freetier-cosmos-refresh-minutes'
            }
            {
              name: 'AzureUsage__Enabled'
              secretRef: 'azureusage-enabled'
            }
            {
              name: 'AzureUsage__ContainerAppName'
              secretRef: 'azureusage-containerapp-name'
            }
            {
              name: 'AzureUsage__ResourceGroup'
              secretRef: 'azureusage-resource-group'
            }
            {
              name: 'AzureUsage__SubscriptionId'
              secretRef: 'azureusage-subscription-id'
            }
            {
              name: 'AzureUsage__MetricsRefreshMinutes'
              secretRef: 'azureusage-metrics-refresh-minutes'
            }
            {
              name: 'AzureUsage__CostRefreshMinutes'
              secretRef: 'azureusage-cost-refresh-minutes'
            }
            {
              name: 'FreeTier__Cosmos__CollectionNames__0'
              secretRef: 'freetier-cosmos-collection-name-0'
            }
            {
              name: 'FreeTier__Cosmos__CollectionNames__1'
              secretRef: 'freetier-cosmos-collection-name-1'
            }
            {
              name: 'GitSettings__RemoteName'
              secretRef: 'gitsettings-remote-name'
            }
            {
              name: 'GitSettings__AuthorName'
              secretRef: 'gitsettings-author-name'
            }
            {
              name: 'GitSettings__AuthorEmail'
              secretRef: 'gitsettings-author-email'
            }
            {
              name: 'GitSettings__Token'
              secretRef: 'git-token'
            }
            {
              name: 'GitSettings__CloneUrl'
              secretRef: 'git-clone-url'
            }
          ]
        }
      ]
      scale: {
        minReplicas: 0
        maxReplicas: 1
        rules: [
          {
            name: 'http-rule'
            http: {
              metadata: {
                concurrentRequests: '30'
              }
            }
          }
        ]
      }
    }
  }
}

// Outputs
output containerAppUrl string = 'https://${containerApp.properties.configuration.ingress.fqdn}'
output containerAppPrincipalId string = containerApp.identity.principalId
output containerAppName string = containerApp.name
