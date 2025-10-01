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
              value: format('/subscriptions/{0}/resourceGroups/{1}/providers/Microsoft.DocumentDB/mongoClusters/{2}', subscription().subscriptionId, resourceGroup().name, cosmosDbAccountName)
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
              secretRef: 'git-token'
            }
            {
              name: 'GitSettings__CloneUrl'
              secretRef: 'git-clone-url'
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
