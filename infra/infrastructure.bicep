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

// Container Apps Environment
resource containerAppsEnvironment 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: containerAppsEnvironmentName
  location: location
  properties: {}
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

// Outputs
output environmentId string = containerAppsEnvironment.id
output cosmosMongoClusterName string = cosmosMongoCluster.name
output cosmosDbAccountName string = cosmosDbAccountName