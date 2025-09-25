@description('Location for all resources')
param location string = resourceGroup().location

@description('Name prefix for all resources')
param namePrefix string

@description('Container image tag')
param imageTag string = 'latest'

@description('Cosmos DB account name')
param cosmosDbAccountName string = '${namePrefix}-cosmos'

@description('Container Apps environment name')
param containerAppsEnvironmentName string = '${namePrefix}-env'

@description('Container App name')
param containerAppName string = '${namePrefix}-app'

@description('Log Analytics workspace name')
param logAnalyticsWorkspaceName string = '${namePrefix}-logs'

@description('Container registry name')
param containerRegistryName string = replace('${namePrefix}acr', '-', '')

@description('Path to secrets JSON file')
param secretsFile string = 'secrets.json'

// Load secrets from JSON file
var secrets = json(loadTextContent(secretsFile))
var googleClientId = secrets.googleClientId
var googleClientSecret = secrets.googleClientSecret
var initialUserEmail = secrets.initialUserEmail
var gitAuthorName = secrets.gitSettings.authorName
var gitAuthorEmail = secrets.gitSettings.authorEmail
var gitToken = secrets.gitSettings.token
var gitCloneUrl = secrets.gitSettings.cloneUrl
var mongoAdminPassword = secrets.mongoAdminPassword

// Log Analytics Workspace
resource logAnalyticsWorkspace 'Microsoft.OperationalInsights/workspaces@2022-10-01' = {
  name: logAnalyticsWorkspaceName
  location: location
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
  }
}

// Container Apps Environment
resource containerAppsEnvironment 'Microsoft.App/managedEnvironments@2023-05-01' = {
  name: containerAppsEnvironmentName
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logAnalyticsWorkspace.properties.customerId
        sharedKey: logAnalyticsWorkspace.listKeys().primarySharedKey
      }
    }
  }
}

// Container Registry (Free tier)
resource containerRegistry 'Microsoft.ContainerRegistry/registries@2023-07-01' = {
  name: containerRegistryName
  location: location
  sku: {
    name: 'Basic'
  }
  properties: {
    adminUserEnabled: true
    publicNetworkAccess: 'Enabled'
  }
}

// Cosmos DB for MongoDB (vCore) - Free Tier
resource cosmosMongoCluster 'Microsoft.DocumentDB/mongoClusters@2025-04-01-preview' = {
  name: cosmosDbAccountName
  location: location
  properties: {
    administrator: {
      userName: 'mongoAdmin'
      password: secrets.mongoAdminPassword
    }
    compute: {
      tier: 'Free'
    }
    storage: {
      sizeGb: 32
    }
    serverVersion: '6.0'
    highAvailability: {
      targetMode: 'Disabled'
    }
    backup: {
      earliestRestoreTime: null
    }
  }
}

// Firewall rule to allow Azure services
resource mongoClusterFirewallRule 'Microsoft.DocumentDB/mongoClusters/firewallRules@2025-04-01-preview' = {
  parent: cosmosMongoCluster
  name: 'AllowAllAzureServices'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

// Container App
resource containerApp 'Microsoft.App/containerApps@2023-05-01' = {
  name: containerAppName
  location: location
  properties: {
    managedEnvironmentId: containerAppsEnvironment.id
    configuration: {
      secrets: [
        {
          name: 'mongo-connection-string'
          value: cosmosMongoCluster.listConnectionStrings().connectionStrings[0].connectionString
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
          name: 'registry-password'
          value: containerRegistry.listCredentials().passwords[0].value
        }
      ]
      registries: [
        {
          server: containerRegistry.properties.loginServer
          username: containerRegistry.name
          passwordSecretRef: 'registry-password'
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
          name: containerAppName
          image: '${containerRegistry.properties.loginServer}/${containerAppName}:${imageTag}'
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
              name: 'AzureUsage__ContainerAppResourceId'
              value: containerApp.id
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
              value: gitToken
            }
            {
              name: 'GitSettings__CloneUrl'
              value: gitCloneUrl
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
output mongoConnectionString string = cosmosMongoCluster.listConnectionStrings().connectionStrings[0].connectionString
output cosmosMongoClusterName string = cosmosMongoCluster.name
output containerRegistryLoginServer string = containerRegistry.properties.loginServer
output containerRegistryName string = containerRegistry.name