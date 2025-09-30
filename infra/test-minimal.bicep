@description('Location for all resources')
param location string = resourceGroup().location

@description('Name prefix for all resources')
param namePrefix string

// Container Apps Environment only - minimal test
resource containerAppsEnvironment 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: '${namePrefix}-env-test'
  location: location
  properties: {}
}

output environmentId string = containerAppsEnvironment.id