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

@description('Google OAuth Client ID')
@secure()
param googleClientId string

@description('Google OAuth Client Secret')
@secure()
param googleClientSecret string

@description('Initial user email address')
param initialUserEmail string

@description('MongoDB admin password')
@secure()
param mongoAdminPassword string

@description('Git author name')
param gitAuthorName string

@description('Git author email')
param gitAuthorEmail string

@description('Git access token')
@secure()
param gitToken string

@description('Git clone URL')
param gitCloneUrl string

@description('Subscription ID')
param subscriptionId string

@description('Resource group name')
param resourceGroupName string

// Variables
var mongoConnectionString = 'mongodb+srv://mongoAdmin:${mongoAdminPassword}@${cosmosDbAccountName}.mongocluster.cosmos.azure.com/?tls=true&authMechanism=SCRAM-SHA-256&retrywrites=false&maxIdleTimeMS=120000'

// Container App
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
          name: 'mongo-connection-string'
          value: mongoConnectionString
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
          name: 'git-token'
          value: gitToken
        }
        {
          name: 'git-clone-url'
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
              value: 'Production'
            }
            {
              name: 'MongoDbSettings__ConnectionString'
              secretRef: 'mongo-connection-string'
            }
            {
              name: 'MongoDbSettings__DatabaseName'
              value: 'FestivalManagement'
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
              name: 'AzureUsage__Enabled'
              value: 'true'
            }
            {
              name: 'AzureUsage__ContainerAppName'
              value: containerAppName
            }
            {
              name: 'AzureUsage__ResourceGroup'
              value: resourceGroupName
            }
            {
              name: 'AzureUsage__SubscriptionId'
              value: subscriptionId
            }
            {
              name: 'AzureUsage__MetricsRefreshMinutes'
              value: '10'
            }
            {
              name: 'FreeTier__Cosmos__Enabled'
              value: 'true'
            }
            {
              name: 'FreeTier__Cosmos__SubscriptionId'
              value: subscriptionId
            }
            {
              name: 'FreeTier__Cosmos__ResourceGroup'
              value: resourceGroupName
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
              value: '/subscriptions/${subscriptionId}/resourceGroups/${resourceGroupName}/providers/Microsoft.DocumentDB/mongoClusters/${cosmosDbAccountName}'
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
              name: 'AzureUsage__CostRefreshMinutes'
              value: '360'
            }
            {
              name: 'GitSettings__RemoteName'
              value: 'origin'
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
              name: 'GitSettings__AuthorName'
              value: gitAuthorName
            }
            {
              name: 'GitSettings__AuthorEmail'
              value: gitAuthorEmail
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
          resources: {
            cpu: json('0.25')
            memory: '0.5Gi'
          }
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