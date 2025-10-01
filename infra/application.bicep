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

@description('Scale down to zero cooldown in seconds (default: 300 = 5 minutes)')
param scaleDownToZeroCooldownInSeconds int = 300

// Container App - Minimal version (secrets and env vars configured post-deployment)
resource containerApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: containerAppName
  location: location
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    managedEnvironmentId: environmentId
    configuration: {
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
        cooldownPeriod: scaleDownToZeroCooldownInSeconds
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
