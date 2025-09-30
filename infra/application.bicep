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