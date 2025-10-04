@description('Location for all resources')
param location string = resourceGroup().location

@description('Name prefix for all resources')
param namePrefix string

@description('MongoDB admin password')
@secure()
param mongoAdminPassword string

@description('Cosmos DB account name')
param cosmosDbAccountName string = '${namePrefix}-cosmos'

@description('Container Apps environment name')
param containerAppsEnvironmentName string = '${namePrefix}-env'

@description('Whether to create new Container Apps Environment')
param createNewEnvironment bool = false

@description('Whether to create new Cosmos DB')
param createNewCosmosDb bool = false

// Container Apps Environment - create or reference existing
resource containerAppsEnvironment 'Microsoft.App/managedEnvironments@2024-03-01' = if (createNewEnvironment) {
  name: containerAppsEnvironmentName
  location: location
  properties: {
    workloadProfiles: [
      {
        name: 'Consumption'
        workloadProfileType: 'Consumption'
      }
    ]
  }
}

resource existingContainerAppsEnvironment 'Microsoft.App/managedEnvironments@2024-03-01' existing = if (!createNewEnvironment) {
  name: containerAppsEnvironmentName
}

// Cosmos DB for MongoDB (vCore) - Free Tier - create or reference existing
resource cosmosMongoCluster 'Microsoft.DocumentDB/mongoClusters@2024-07-01' = if (createNewCosmosDb) {
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

resource existingCosmosMongoCluster 'Microsoft.DocumentDB/mongoClusters@2024-07-01' existing = if (!createNewCosmosDb) {
  name: cosmosDbAccountName
}

// Firewall rule to allow Azure services - only create if Cosmos DB is new
resource mongoClusterFirewallRule 'Microsoft.DocumentDB/mongoClusters/firewallRules@2024-07-01' = if (createNewCosmosDb) {
  parent: cosmosMongoCluster
  name: 'AllowAllAzureServices'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

// Outputs
output environmentId string = createNewEnvironment ? containerAppsEnvironment.id : existingContainerAppsEnvironment.id
output cosmosMongoClusterName string = createNewCosmosDb ? cosmosMongoCluster.name : existingCosmosMongoCluster.name
output cosmosDbAccountName string = cosmosDbAccountName