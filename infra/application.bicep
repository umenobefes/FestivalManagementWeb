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
          name: 'ASPNETCORE_ENVIRONMENT'
          value: 'Production'
        }
        {
          name: 'MongoDbSettings__ConnectionString'
          value: mongoConnectionString
        }
        {
          name: 'MongoDbSettings__DatabaseName'
          value: 'FestivalManagement'
        }
        {
          name: 'Authentication__Google__ClientId'
          value: googleClientId
        }
        {
          name: 'Authentication__Google__ClientSecret'
          value: googleClientSecret
        }
        {
          name: 'InitialUser__Email'
          value: initialUserEmail
        }
        {
          name: 'FreeTier__EnableBanner'
          value: 'true'
        }
        {
          name: 'FreeTier__BudgetVcpuSeconds'
          value: '180000'
        }
        {
          name: 'FreeTier__BudgetGiBSeconds'
          value: '360000'
        }
        {
          name: 'FreeTier__Resource__VcpuPerReplica'
          value: '0.25'
        }
        {
          name: 'FreeTier__Resource__MemoryGiBPerReplica'
          value: '0.5'
        }
        {
          name: 'FreeTier__Resource__ReplicaFactor'
          value: '1'
        }
        {
          name: 'FreeTier__EnforceRequestDailyCap'
          value: 'false'
        }
        {
          name: 'FreeTier__Data__BudgetGb'
          value: '100'
        }
        {
          name: 'FreeTier__Requests__Budget'
          value: '2000000'
        }
        {
          name: 'FreeTier__Cosmos__Enabled'
          value: 'true'
        }
        {
          name: 'FreeTier__Cosmos__SubscriptionId'
          value: subscription().subscriptionId
        }
        {
          name: 'FreeTier__Cosmos__ResourceGroup'
          value: resourceGroup().name
        }
        {
          name: 'FreeTier__Cosmos__AccountName'
          value: cosmosDbAccountName
        }
        {
          name: 'FreeTier__Cosmos__DatabaseName'
          value: 'FestivalManagement'
        }
        {
          name: 'FreeTier__Cosmos__AccountResourceId'
          value: cosmosMongoCluster.id
        }
        {
          name: 'FreeTier__Cosmos__Provisioning'
          value: 'vCore'
        }
        {
          name: 'FreeTier__Cosmos__FreeTierStorageGb'
          value: '32'
        }
        {
          name: 'FreeTier__Cosmos__FreeTierVCoreStorageGb'
          value: '32'
        }
        {
          name: 'FreeTier__Cosmos__WarnRuPercent'
          value: '90'
        }
        {
          name: 'FreeTier__Cosmos__WarnStoragePercent'
          value: '90'
        }
        {
          name: 'FreeTier__Cosmos__RefreshMinutes'
          value: '60'
        }
        {
          name: 'FreeTier__Cosmos__CollectionNames__0'
          value: 'TextKeyValues'
        }
        {
          name: 'FreeTier__Cosmos__CollectionNames__1'
          value: 'ImageKeyValues'
        }
        {
          name: 'AzureUsage__Enabled'
          value: 'true'
        }
        {
          name: 'AzureUsage__ContainerAppName'
          value: containerAppName
        }
        {
          name: 'AzureUsage__ResourceGroup'
          value: resourceGroup().name
        }
        {
          name: 'AzureUsage__SubscriptionId'
          value: subscription().subscriptionId
        }
        {
          name: 'AzureUsage__MetricsRefreshMinutes'
          value: '10'
        }
        {
          name: 'AzureUsage__CostRefreshMinutes'
          value: '360'
        }
        {
          name: 'GitSettings__RemoteName'
          value: 'origin'
        }
        {
          name: 'GitSettings__AuthorName'
          value: gitAuthorName
        }
        {
          name: 'GitSettings__AuthorEmail'
          value: gitAuthorEmail
        }
        {
          name: 'GitSettings__Token'
          value: gitToken
        }
        {
          name: 'GitSettings__CloneUrl'
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
              secretRef: 'ASPNETCORE_ENVIRONMENT'
            }
            {
              name: 'MongoDbSettings__ConnectionString'
              secretRef: 'MongoDbSettings__ConnectionString'
            }
            {
              name: 'MongoDbSettings__DatabaseName'
              secretRef: 'MongoDbSettings__DatabaseName'
            }
            {
              name: 'Authentication__Google__ClientId'
              secretRef: 'Authentication__Google__ClientId'
            }
            {
              name: 'Authentication__Google__ClientSecret'
              secretRef: 'Authentication__Google__ClientSecret'
            }
            {
              name: 'InitialUser__Email'
              secretRef: 'InitialUser__Email'
            }
            {
              name: 'FreeTier__EnableBanner'
              secretRef: 'FreeTier__EnableBanner'
            }
            {
              name: 'FreeTier__BudgetVcpuSeconds'
              secretRef: 'FreeTier__BudgetVcpuSeconds'
            }
            {
              name: 'FreeTier__BudgetGiBSeconds'
              secretRef: 'FreeTier__BudgetGiBSeconds'
            }
            {
              name: 'FreeTier__Resource__VcpuPerReplica'
              secretRef: 'FreeTier__Resource__VcpuPerReplica'
            }
            {
              name: 'FreeTier__Resource__MemoryGiBPerReplica'
              secretRef: 'FreeTier__Resource__MemoryGiBPerReplica'
            }
            {
              name: 'FreeTier__Resource__ReplicaFactor'
              secretRef: 'FreeTier__Resource__ReplicaFactor'
            }
            {
              name: 'FreeTier__EnforceRequestDailyCap'
              secretRef: 'FreeTier__EnforceRequestDailyCap'
            }
            {
              name: 'FreeTier__Data__BudgetGb'
              secretRef: 'FreeTier__Data__BudgetGb'
            }
            {
              name: 'FreeTier__Requests__Budget'
              secretRef: 'FreeTier__Requests__Budget'
            }
            {
              name: 'FreeTier__Cosmos__Enabled'
              secretRef: 'FreeTier__Cosmos__Enabled'
            }
            {
              name: 'FreeTier__Cosmos__SubscriptionId'
              secretRef: 'FreeTier__Cosmos__SubscriptionId'
            }
            {
              name: 'FreeTier__Cosmos__ResourceGroup'
              secretRef: 'FreeTier__Cosmos__ResourceGroup'
            }
            {
              name: 'FreeTier__Cosmos__AccountName'
              secretRef: 'FreeTier__Cosmos__AccountName'
            }
            {
              name: 'FreeTier__Cosmos__DatabaseName'
              secretRef: 'FreeTier__Cosmos__DatabaseName'
            }
            {
              name: 'FreeTier__Cosmos__AccountResourceId'
              secretRef: 'FreeTier__Cosmos__AccountResourceId'
            }
            {
              name: 'FreeTier__Cosmos__Provisioning'
              secretRef: 'FreeTier__Cosmos__Provisioning'
            }
            {
              name: 'FreeTier__Cosmos__FreeTierStorageGb'
              secretRef: 'FreeTier__Cosmos__FreeTierStorageGb'
            }
            {
              name: 'FreeTier__Cosmos__FreeTierVCoreStorageGb'
              secretRef: 'FreeTier__Cosmos__FreeTierVCoreStorageGb'
            }
            {
              name: 'FreeTier__Cosmos__WarnRuPercent'
              secretRef: 'FreeTier__Cosmos__WarnRuPercent'
            }
            {
              name: 'FreeTier__Cosmos__WarnStoragePercent'
              secretRef: 'FreeTier__Cosmos__WarnStoragePercent'
            }
            {
              name: 'FreeTier__Cosmos__RefreshMinutes'
              secretRef: 'FreeTier__Cosmos__RefreshMinutes'
            }
            {
              name: 'FreeTier__Cosmos__CollectionNames__0'
              secretRef: 'FreeTier__Cosmos__CollectionNames__0'
            }
            {
              name: 'FreeTier__Cosmos__CollectionNames__1'
              secretRef: 'FreeTier__Cosmos__CollectionNames__1'
            }
            {
              name: 'AzureUsage__Enabled'
              secretRef: 'AzureUsage__Enabled'
            }
            {
              name: 'AzureUsage__ContainerAppName'
              secretRef: 'AzureUsage__ContainerAppName'
            }
            {
              name: 'AzureUsage__ResourceGroup'
              secretRef: 'AzureUsage__ResourceGroup'
            }
            {
              name: 'AzureUsage__SubscriptionId'
              secretRef: 'AzureUsage__SubscriptionId'
            }
            {
              name: 'AzureUsage__MetricsRefreshMinutes'
              secretRef: 'AzureUsage__MetricsRefreshMinutes'
            }
            {
              name: 'AzureUsage__CostRefreshMinutes'
              secretRef: 'AzureUsage__CostRefreshMinutes'
            }
            {
              name: 'GitSettings__RemoteName'
              secretRef: 'GitSettings__RemoteName'
            }
            {
              name: 'GitSettings__AuthorName'
              secretRef: 'GitSettings__AuthorName'
            }
            {
              name: 'GitSettings__AuthorEmail'
              secretRef: 'GitSettings__AuthorEmail'
            }
            {
              name: 'GitSettings__Token'
              secretRef: 'GitSettings__Token'
            }
            {
              name: 'GitSettings__CloneUrl'
              secretRef: 'GitSettings__CloneUrl'
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
