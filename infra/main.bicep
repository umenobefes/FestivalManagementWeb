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

@description('Container registry server hostname; use ghcr.io for GitHub Container Registry')
param containerRegistryServer string = 'ghcr.io'

@description('Container registry repository in the form owner/repository')
param containerRegistryRepository string

@description('Google OAuth Client ID')
@secure()
param googleClientId string

@description('Google OAuth Client Secret')
@secure()
param googleClientSecret string

@description('Initial user email address')
param initialUserEmail string

@description('Git author name')
param gitAuthorName string

@description('Git author email')
param gitAuthorEmail string

@description('Git access token')
@secure()
param gitToken string

@description('Git clone URL')
@secure()
param gitCloneUrl string

@description('MongoDB admin password')
@secure()
param mongoAdminPassword string

// Log Analytics Workspace - Disabled to save costs

// Container Apps Environment
resource containerAppsEnvironment 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: containerAppsEnvironmentName
  location: location
  properties: {
    // Logging disabled to save costs
    // appLogsConfiguration: {
    //   destination: 'log-analytics'
    //   logAnalyticsConfiguration: {
    //     customerId: logAnalyticsWorkspace.properties.customerId
    //     sharedKey: logAnalyticsWorkspace.listKeys().primarySharedKey
    //   }
    // }
  }
}

// Cosmos DB for MongoDB (vCore) - Free Tier
resource cosmosMongoCluster 'Microsoft.DocumentDB/mongoClusters@2024-07-01' = {
  name: cosmosDbAccountName
  location: location
  properties: {
    administrator: {
      userName: 'mongoAdmin'
      password: mongoAdminPassword
    }
    compute: {
      tier: 'Free'
    }
    storage: {
      sizeGb: 32
    }
    sharding: {
      shardCount: 1
    }
    serverVersion: '7.0'
    highAvailability: {
      targetMode: 'Disabled'
    }
  }
}

// Firewall rule to allow Azure services
resource mongoClusterFirewallRule 'Microsoft.DocumentDB/mongoClusters/firewallRules@2024-07-01' = {
  parent: cosmosMongoCluster
  name: 'AllowAllAzureServices'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

// Container App
resource containerApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: containerAppName
  location: location
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    managedEnvironmentId: containerAppsEnvironment.id
    configuration: {
      secrets: [
        {
          name: 'mongo-connection-string'
          value: 'mongodb+srv://mongoAdmin:${mongoAdminPassword}@${cosmosDbAccountName}.mongocluster.cosmos.azure.com/?tls=true&authMechanism=SCRAM-SHA-256&retrywrites=false&maxIdleTimeMS=120000'
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

// Role Assignments for Managed Identity
var readerRoleId = 'acdd72a7-3385-48ef-bd42-f606fba81ae7'
var monitoringReaderRoleId = '43d0d8ad-25c7-4714-9337-8ba259a9fe05'
var costManagementReaderRoleId = '72fafb9e-0641-4937-9268-a91bfd8191a3'

// Assign Reader role to resource group
resource readerRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(resourceGroup().id, containerApp.id, readerRoleId)
  scope: resourceGroup()
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', readerRoleId)
    principalId: containerApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

// Assign Monitoring Reader role to resource group
resource monitoringReaderRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(resourceGroup().id, containerApp.id, monitoringReaderRoleId)
  scope: resourceGroup()
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', monitoringReaderRoleId)
    principalId: containerApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

// Assign Cost Management Reader role to subscription
resource costManagementReaderRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(subscription().id, containerApp.id, costManagementReaderRoleId)
  scope: subscription()
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', costManagementReaderRoleId)
    principalId: containerApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

// Assign Monitoring Reader role to Cosmos DB
resource cosmosMonitoringReaderRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(cosmosMongoCluster.id, containerApp.id, monitoringReaderRoleId)
  scope: cosmosMongoCluster
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', monitoringReaderRoleId)
    principalId: containerApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

// Outputs
output containerAppUrl string = 'https://${containerApp.properties.configuration.ingress.fqdn}'
output cosmosMongoClusterName string = cosmosMongoCluster.name
output containerAppPrincipalId string = containerApp.identity.principalId
output containerAppName string = containerApp.name


