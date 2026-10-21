// All Azure resources for the app, deployed into one resource group.

param appLocation string = resourceGroup().location
param sqlLocation string = resourceGroup().location
param storageLocation string = resourceGroup().location

param planName string
param appName string
param sqlServerName string
param sqlDatabaseName string
param storageAccountName string
param emailServiceName string
param communicationServiceName string

@description('Email of the account that gets the Admin role once registered and confirmed.')
param adminEmail string

// Built-in Azure role IDs (the same in every subscription).
var storageBlobDataContributorRole = 'ba92f5b4-2d11-453d-a403-e96b0029c9fe'
var communicationEmailOwnerRole = '09976791-48a7-449e-bb21-39d1a415f350'

// Web app

resource plan 'Microsoft.Web/serverfarms@2024-04-01' = {
  name: planName
  location: appLocation
  kind: 'linux'
  sku: {
    name: 'F1'
  }
  properties: {
    reserved: true // required for Linux plans
  }
}

resource app 'Microsoft.Web/sites@2024-04-01' = {
  name: appName
  location: appLocation
  kind: 'app,linux'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'
      alwaysOn: false // not available on the free tier
      ftpsState: 'FtpsOnly'
      minTlsVersion: '1.2'
      // These replace all existing app settings, so every setting the app needs is listed here.
      appSettings: [
        { name: 'Admin__Email', value: adminEmail }
        { name: 'Email__Endpoint', value: 'https://${communicationService.properties.hostName}' }
        { name: 'Email__SenderAddress', value: 'DoNotReply@${emailDomain.properties.mailFromSenderDomain}' }
        { name: 'Storage__BlobEndpoint', value: storage.properties.primaryEndpoints.blob }
      ]
      connectionStrings: [
        {
          name: 'DefaultConnection'
          type: 'SQLAzure'
          // No password: the app signs in with its managed identity.
          connectionString: 'Server=tcp:${sqlServer.properties.fullyQualifiedDomainName},1433;Database=${sqlDatabaseName};Authentication=Active Directory Managed Identity;Encrypt=True'
        }
      ]
    }
  }
}

// Database

resource sqlServer 'Microsoft.Sql/servers@2023-08-01' = {
  name: sqlServerName
  location: sqlLocation
  properties: {
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
    // Only used when the server is first created; whoever deploys becomes the SQL admin.
    administrators: {
      administratorType: 'ActiveDirectory'
      principalType: 'User'
      login: deployer().userPrincipalName
      sid: deployer().objectId
      tenantId: tenant().tenantId
      azureADOnlyAuthentication: true
    }
  }
}

// Turns off SQL passwords on an existing server too, so only Microsoft Entra sign-ins work.
resource sqlEntraOnly 'Microsoft.Sql/servers/azureADOnlyAuthentications@2023-08-01' = {
  parent: sqlServer
  name: 'Default'
  properties: {
    azureADOnlyAuthentication: true
  }
}

// 0.0.0.0 is a special rule meaning "allow connections from Azure services".
resource sqlAllowAzure 'Microsoft.Sql/servers/firewallRules@2023-08-01' = {
  parent: sqlServer
  name: 'AllowAllWindowsAzureIps'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

resource sqlDatabase 'Microsoft.Sql/servers/databases@2023-08-01' = {
  parent: sqlServer
  name: sqlDatabaseName
  location: sqlLocation
  sku: {
    name: 'GP_S_Gen5'
    tier: 'GeneralPurpose'
    family: 'Gen5'
    capacity: 2
  }
  properties: {
    // Azure SQL free offer: pauses instead of billing when the monthly allowance runs out.
    useFreeLimit: true
    freeLimitExhaustionBehavior: 'AutoPause'
    autoPauseDelay: 60
    minCapacity: json('0.5')
    maxSizeBytes: 34359738368
  }
}

// Image storage

resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: storageAccountName
  location: storageLocation
  kind: 'StorageV2'
  sku: {
    name: 'Standard_LRS'
  }
  properties: {
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
    allowBlobPublicAccess: true // event images are shown to anyone
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: storage
  name: 'default'
  properties: {
    // Deleted images and containers can be restored for 7 days.
    deleteRetentionPolicy: {
      enabled: true
      days: 7
    }
    containerDeleteRetentionPolicy: {
      enabled: true
      days: 7
    }
  }
}

// Name must match Storage:ImagesContainer in appsettings.json.
resource imagesContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: 'event-images'
  properties: {
    publicAccess: 'Blob' // anyone can read an image by its URL, but can't list them
  }
}

// Email

resource emailService 'Microsoft.Communication/emailServices@2023-04-01' = {
  name: emailServiceName
  location: 'global'
  properties: {
    dataLocation: 'UK'
  }
}

// A free *.azurecomm.net sender domain, so no custom domain or DNS setup is needed.
resource emailDomain 'Microsoft.Communication/emailServices/domains@2023-04-01' = {
  parent: emailService
  name: 'AzureManagedDomain'
  location: 'global'
  properties: {
    domainManagement: 'AzureManaged'
    userEngagementTracking: 'Disabled'
  }
}

resource communicationService 'Microsoft.Communication/communicationServices@2023-04-01' = {
  name: communicationServiceName
  location: 'global'
  properties: {
    dataLocation: 'UK'
    linkedDomains: [emailDomain.id]
  }
}

// Permissions for the app's managed identity

resource appBlobAccess 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storage.id, app.id, storageBlobDataContributorRole)
  scope: storage
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', storageBlobDataContributorRole)
    principalId: app.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

resource appEmailAccess 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(communicationService.id, app.id, communicationEmailOwnerRole)
  scope: communicationService
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', communicationEmailOwnerRole)
    principalId: app.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

output appUrl string = 'https://${app.properties.defaultHostName}'
