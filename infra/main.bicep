extension microsoftGraphV1

targetScope = 'subscription'

@description('Required. AQR environment name supplied by azd.')
param environmentName string

@description('Required. Azure region for AQR resources.')
param location string

@description('Required. Object ID of the deploying principal; added to the generated SQL admin group.')
param principalId string

@description('Optional. Comma-separated management group IDs that AQR reports on.')
param managementGroupIds string = ''

@description('Optional. Comma-separated subscription IDs to report on, in addition to (or instead of) management groups.')
param subscriptionIds string = ''

@description('Optional. Comma-separated Azure regions to sync. Empty means all regions returned by the APIs.')
param regions string = ''

@description('Optional. Network isolation mode for SQL and storage.')
@allowed([
  'nsp'
  'privateEndpoint'
])
param sqlNetwork string = 'nsp'

@description('Optional. Network Security Perimeter association access mode. Learning is transition mode.')
@allowed([
  'Learning'
  'Enforced'
])
param nspMode string = 'Learning'

@description('Optional. Azure SQL Database SKU name.')
param sqlSku string = 'S1'

@description('Optional. Azure SQL PITR short-term backup retention in days.')
@minValue(1)
@maxValue(35)
param sqlPitrDays int = 7

@description('Optional. SQL temporal table history retention configured by application migrations.')
param historyRetention string = '1 YEAR'

@description('Optional. Comma-separated extra object IDs to add to the generated SQL admin group.')
param sqlAdminObjectIds string = ''

@description('Optional. Entra group object ID assigned to the AQR.Admin app role.')
param adminGroupId string = ''

@description('Optional. Entra group object ID assigned to the AQR.Reader app role.')
param readerGroupId string = ''

@description('Optional. Grant the AQR managed identity Microsoft Graph GroupMember.Read.All application permission. Requires privileged Graph permissions.')
param grantGraphGroupMemberRead bool = false

@description('Optional. Linux App Service plan SKU name.')
param appServiceSku string = 'P0v3'

var resourceToken = toLower(uniqueString(subscription().id, environmentName, location))
var tags = {
  'azd-env-name': environmentName
}
var resourceGroupName = 'rg-aqr-${environmentName}'
var uamiName = 'id-aqr-${resourceToken}'
var workspaceName = 'log-aqr-${resourceToken}'
var appInsightsName = 'appi-aqr-${resourceToken}'
var planName = 'asp-aqr-${resourceToken}'
var webAppName = 'app-aqr-${resourceToken}'
var sqlServerName = 'sql-aqr-${resourceToken}'
var databaseName = 'sqldb-aqr'
var storageAccountName = 'staqr${resourceToken}'
var vnetName = 'vnet-aqr-${resourceToken}'
var nspName = 'nsp-aqr-${resourceToken}'
var nspProfileName = 'default'
var sqlPrivateDnsZoneName = 'privatelink.${environment().suffixes.sqlServerHostname}'
var blobPrivateDnsZoneName = 'privatelink.blob.${environment().suffixes.storage}'
var appSubnetResourceId = resourceId('Microsoft.Network/virtualNetworks/subnets', vnetName, 'snet-app')
var peSubnetResourceId = resourceId('Microsoft.Network/virtualNetworks/subnets', vnetName, 'snet-pe')
var sqlPrivateDnsZoneResourceId = resourceId('Microsoft.Network/privateDnsZones', sqlPrivateDnsZoneName)
var blobPrivateDnsZoneResourceId = resourceId('Microsoft.Network/privateDnsZones', blobPrivateDnsZoneName)
var sqlAdminExtraObjectIds = filter(map(split(sqlAdminObjectIds, ','), id => trim(id)), id => !empty(id))
var sqlAdminMembers = union([
  identity.outputs.principalId
  principalId
], sqlAdminExtraObjectIds)
var appServiceUrl = 'https://${webAppName}.azurewebsites.net'

module rg 'br/public:avm/res/resources/resource-group:0.4.4' = {
  name: 'rg-${resourceToken}'
  params: {
    name: resourceGroupName
    location: location
    tags: tags
    enableTelemetry: false
  }
}

module identity 'br/public:avm/res/managed-identity/user-assigned-identity:0.6.0' = {
  name: 'uami-${resourceToken}'
  scope: resourceGroup(resourceGroupName)
  params: {
    name: uamiName
    location: location
    tags: tags
    enableTelemetry: false
  }
  dependsOn: [
    rg
  ]
}

module logAnalytics 'br/public:avm/res/operational-insights/workspace:0.16.1' = {
  name: 'law-${resourceToken}'
  scope: resourceGroup(resourceGroupName)
  params: {
    name: workspaceName
    location: location
    dataRetention: 365
    tags: tags
    enableTelemetry: false
  }
  dependsOn: [
    rg
  ]
}

module appInsights 'br/public:avm/res/insights/component:0.8.0' = {
  name: 'appi-${resourceToken}'
  scope: resourceGroup(resourceGroupName)
  params: {
    name: appInsightsName
    location: location
    workspaceResourceId: logAnalytics.outputs.resourceId
    applicationType: 'web'
    tags: tags
    enableTelemetry: false
  }
}

module entra 'modules/entra.bicep' = {
  name: 'entra-${resourceToken}'
  params: {
    environmentName: environmentName
    tenantId: tenant().tenantId
    webAppName: webAppName
    uamiPrincipalId: identity.outputs.principalId
    sqlAdminMemberObjectIds: sqlAdminMembers
    adminGroupId: adminGroupId
    readerGroupId: readerGroupId
    grantGraphGroupMemberRead: grantGraphGroupMemberRead
  }
}

module plan 'br/public:avm/res/web/serverfarm:0.7.0' = {
  name: 'plan-${resourceToken}'
  scope: resourceGroup(resourceGroupName)
  params: {
    name: planName
    location: location
    skuName: appServiceSku
    skuCapacity: 1
    kind: 'linux'
    reserved: true
    zoneRedundant: false
    tags: tags
    enableTelemetry: false
  }
  dependsOn: [
    rg
  ]
}

module vnet 'br/public:avm/res/network/virtual-network:0.10.2' = if (sqlNetwork == 'privateEndpoint') {
  name: 'vnet-${resourceToken}'
  scope: resourceGroup(resourceGroupName)
  params: {
    name: vnetName
    location: location
    addressPrefixes: [
      '10.40.0.0/24'
    ]
    subnets: [
      {
        name: 'snet-app'
        addressPrefix: '10.40.0.0/26'
        delegation: 'Microsoft.Web/serverFarms'
      }
      {
        name: 'snet-pe'
        addressPrefix: '10.40.0.64/26'
        privateEndpointNetworkPolicies: 'Disabled'
      }
    ]
    tags: tags
    enableTelemetry: false
  }
  dependsOn: [
    rg
  ]
}

module sqlDns 'br/public:avm/res/network/private-dns-zone:0.8.1' = if (sqlNetwork == 'privateEndpoint') {
  name: 'pdns-sql-${resourceToken}'
  scope: resourceGroup(resourceGroupName)
  params: {
    name: sqlPrivateDnsZoneName
    virtualNetworkLinks: [
      {
        name: 'link-${vnetName}'
        virtualNetworkResourceId: resourceId('Microsoft.Network/virtualNetworks', vnetName)
        registrationEnabled: false
      }
    ]
    tags: tags
    enableTelemetry: false
  }
  dependsOn: [
    vnet
  ]
}

module blobDns 'br/public:avm/res/network/private-dns-zone:0.8.1' = if (sqlNetwork == 'privateEndpoint') {
  name: 'pdns-blob-${resourceToken}'
  scope: resourceGroup(resourceGroupName)
  params: {
    name: blobPrivateDnsZoneName
    virtualNetworkLinks: [
      {
        name: 'link-${vnetName}'
        virtualNetworkResourceId: resourceId('Microsoft.Network/virtualNetworks', vnetName)
        registrationEnabled: false
      }
    ]
    tags: tags
    enableTelemetry: false
  }
  dependsOn: [
    vnet
  ]
}

module sql 'br/public:avm/res/sql/server:0.22.1' = {
  name: 'sql-${resourceToken}'
  scope: resourceGroup(resourceGroupName)
  params: {
    name: sqlServerName
    location: location
    administrators: {
      administratorType: 'ActiveDirectory'
      azureADOnlyAuthentication: true
      login: entra.outputs.sqlAdminGroupDisplayName
      principalType: 'Group'
      sid: entra.outputs.sqlAdminGroupId
      tenantId: tenant().tenantId
    }
    minimalTlsVersion: '1.2'
    publicNetworkAccess: sqlNetwork == 'nsp' ? 'SecuredByPerimeter' : 'Disabled'
    databases: [
      {
        name: databaseName
        sku: {
          name: sqlSku
          tier: 'Standard'
        }
        maxSizeBytes: 268435456000
        availabilityZone: -1
        zoneRedundant: false
        backupShortTermRetentionPolicy: {
          retentionDays: sqlPitrDays
        }
        tags: tags
      }
    ]
    privateEndpoints: sqlNetwork == 'privateEndpoint' ? [
      {
        name: 'pep-${sqlServerName}-sql'
        subnetResourceId: peSubnetResourceId
        service: 'sqlServer'
        privateDnsZoneGroup: {
          privateDnsZoneGroupConfigs: [
            {
              name: 'sql'
              privateDnsZoneResourceId: sqlPrivateDnsZoneResourceId
            }
          ]
        }
      }
    ] : []
    tags: tags
    enableTelemetry: false
  }
  dependsOn: [
    rg
    sqlDns
  ]
}

module storage 'br/public:avm/res/storage/storage-account:0.33.1' = {
  name: 'stg-${resourceToken}'
  scope: resourceGroup(resourceGroupName)
  params: {
    name: storageAccountName
    location: location
    kind: 'StorageV2'
    skuName: 'Standard_LRS'
    allowSharedKeyAccess: false
    allowBlobPublicAccess: false
    minimumTlsVersion: 'TLS1_2'
    publicNetworkAccess: sqlNetwork == 'nsp' ? 'SecuredByPerimeter' : 'Disabled'
    blobServices: {
      containers: [
        {
          name: 'raw'
          publicAccess: 'None'
        }
      ]
    }
    managementPolicyRules: [
      {
        enabled: true
        name: 'delete-raw-after-400-days'
        type: 'Lifecycle'
        definition: {
          actions: {
            baseBlob: {
              delete: {
                daysAfterModificationGreaterThan: 400
              }
            }
          }
          filters: {
            blobTypes: [
              'blockBlob'
            ]
            prefixMatch: [
              'raw/'
            ]
          }
        }
      }
    ]
    roleAssignments: [
      {
        name: guid(storageAccountName, identity.outputs.principalId, 'Storage Blob Data Contributor')
        principalId: identity.outputs.principalId
        principalType: 'ServicePrincipal'
        roleDefinitionIdOrName: 'Storage Blob Data Contributor'
      }
    ]
    privateEndpoints: sqlNetwork == 'privateEndpoint' ? [
      {
        name: 'pep-${storageAccountName}-blob'
        subnetResourceId: peSubnetResourceId
        service: 'blob'
        privateDnsZoneGroup: {
          privateDnsZoneGroupConfigs: [
            {
              name: 'blob'
              privateDnsZoneResourceId: blobPrivateDnsZoneResourceId
            }
          ]
        }
      }
    ] : []
    tags: tags
    enableTelemetry: false
  }
  dependsOn: [
    rg
    blobDns
  ]
}

module nsp 'br/public:avm/res/network/network-security-perimeter:0.1.4' = if (sqlNetwork == 'nsp') {
  name: 'nsp-${resourceToken}'
  scope: resourceGroup(resourceGroupName)
  params: {
    name: nspName
    location: location
    profiles: [
      {
        name: nspProfileName
        accessRules: [
          {
            name: 'allow-current-subscription-inbound'
            direction: 'Inbound'
            subscriptions: [
              {
                id: subscription().id
              }
            ]
          }
        ]
      }
    ]
    resourceAssociations: [
      {
        privateLinkResource: sql.outputs.resourceId
        profile: nspProfileName
        accessMode: nspMode
      }
      {
        privateLinkResource: storage.outputs.resourceId
        profile: nspProfileName
        accessMode: nspMode
      }
    ]
    diagnosticSettings: [
      {
        name: 'send-to-log-analytics'
        workspaceResourceId: logAnalytics.outputs.resourceId
        logCategoriesAndGroups: [
          {
            categoryGroup: 'allLogs'
          }
        ]
      }
    ]
    tags: tags
    enableTelemetry: false
  }
}

module web 'br/public:avm/res/web/site:0.24.0' = {
  name: 'web-${resourceToken}'
  scope: resourceGroup(resourceGroupName)
  params: {
    name: webAppName
    location: location
    kind: 'app,linux'
    serverFarmResourceId: plan.outputs.resourceId
    managedIdentities: {
      userAssignedResourceIds: [
        identity.outputs.resourceId
      ]
    }
    keyVaultAccessIdentityResourceId: identity.outputs.resourceId
    httpsOnly: true
    storageAccountRequired: false
    virtualNetworkSubnetResourceId: sqlNetwork == 'privateEndpoint' ? appSubnetResourceId : null
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'
      minTlsVersion: '1.2'
      ftpsState: 'Disabled'
      alwaysOn: true
      healthCheckPath: '/health/ready'
      vnetRouteAllEnabled: sqlNetwork == 'privateEndpoint'
    }
    configs: [
      {
        name: 'appsettings'
        properties: {
          ConnectionStrings__AqrDb: 'Server=tcp:${sql.outputs.fullyQualifiedDomainName},1433;Database=${databaseName};Authentication=Active Directory Managed Identity;User Id=${identity.outputs.clientId};Encrypt=True;TrustServerCertificate=False;Connection Timeout=60;'
          AZURE_CLIENT_ID: identity.outputs.clientId
          OVERRIDE_USE_MI_FIC_ASSERTION_CLIENTID: identity.outputs.clientId
          APPLICATIONINSIGHTS_CONNECTION_STRING: appInsights.outputs.connectionString
          Auth__EasyAuthEnabled: 'true'
          Aqr__UseMock: 'false'
          Aqr__ManagementGroupIds: managementGroupIds
          Aqr__SubscriptionIds: subscriptionIds
          Aqr__Regions: regions
          Aqr__Sync__QuotaInterval: '01:00:00'
          Aqr__Sync__GroupInterval: '04:00:00'
          Aqr__Sync__ZoneInterval: '1.00:00:00'
          Aqr__HistoryRetention: historyRetention
          Aqr__RawArchive__BlobServiceUri: storage.outputs.primaryBlobEndpoint
          SCM_DO_BUILD_DURING_DEPLOYMENT: 'false'
        }
      }
      {
        name: 'authsettingsV2'
        properties: {
          platform: {
            enabled: true
          }
          globalValidation: {
            requireAuthentication: true
            unauthenticatedClientAction: 'RedirectToLoginPage'
            redirectToProvider: 'azureActiveDirectory'
            excludedPaths: [
              '/health/live'
              '/health/ready'
            ]
          }
          identityProviders: {
            azureActiveDirectory: {
              enabled: true
              registration: {
                clientId: entra.outputs.appId
                clientSecretSettingName: 'OVERRIDE_USE_MI_FIC_ASSERTION_CLIENTID'
                openIdIssuer: '${environment().authentication.loginEndpoint}${tenant().tenantId}/v2.0'
              }
              validation: {
                allowedAudiences: [
                  entra.outputs.appId
                  'api://${entra.outputs.appId}'
                ]
              }
            }
          }
          login: {
            tokenStore: {
              enabled: true
            }
          }
          httpSettings: {
            requireHttps: true
          }
        }
      }
    ]
    basicPublishingCredentialsPolicies: [
      {
        name: 'ftp'
        allow: false
      }
      {
        name: 'scm'
        allow: false
      }
    ]
    tags: union(tags, {
      'azd-service-name': 'web'
    })
    enableTelemetry: false
  }
}

@description('The Azure location used for deployment.')
output AZURE_LOCATION string = location

@description('The Azure tenant ID.')
output AZURE_TENANT_ID string = tenant().tenantId

@description('The AQR resource group name.')
output AZURE_RESOURCE_GROUP string = resourceGroupName

@description('The App Service name for azd deploy.')
output WEB_APP_NAME string = web.outputs.name

@description('The App Service URL.')
output WEB_APP_URL string = appServiceUrl

@description('The user-assigned managed identity client ID.')
output UAMI_CLIENT_ID string = identity.outputs.clientId

@description('The user-assigned managed identity principal ID.')
output UAMI_PRINCIPAL_ID string = identity.outputs.principalId

@description('The SQL server fully qualified domain name.')
output SQL_SERVER_FQDN string = sql.outputs.fullyQualifiedDomainName

@description('The SQL database name.')
output SQL_DATABASE_NAME string = databaseName

@description('The Storage blob service endpoint.')
output STORAGE_BLOB_ENDPOINT string = storage.outputs.primaryBlobEndpoint

@description('The Entra application client ID.')
output ENTRA_APP_ID string = entra.outputs.appId

@description('The management group IDs configured for AQR.')
output AQR_MANAGEMENT_GROUP_IDS string = managementGroupIds
