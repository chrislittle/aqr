extension microsoftGraphV1

targetScope = 'subscription'

@description('Required. AQR environment name.')
param environmentName string

@description('Required. Tenant ID for issuer URLs and unique names.')
param tenantId string

@description('Required. Predictable App Service name used for redirect URI.')
param webAppName string

@description('Required. Principal ID of the AQR user-assigned managed identity.')
param uamiPrincipalId string

@description('Required. Object IDs to add to the generated SQL admin security group.')
param sqlAdminMemberObjectIds array

@description('Optional. Entra group object ID assigned to AQR.Admin.')
param adminGroupId string = ''

@description('Optional. Entra group object ID assigned to AQR.Reader.')
param readerGroupId string = ''

@description('Optional. Grant GroupMember.Read.All application permission to the AQR managed identity service principal.')
param grantGraphGroupMemberRead bool = false

var appUniqueName = 'aqr-${tenantId}-${environmentName}'
var sqlAdminGroupUniqueName = 'aqr-sql-admins-${tenantId}-${environmentName}'
var appServiceUrl = 'https://${webAppName}.azurewebsites.net'
var readerRoleId = guid(tenantId, environmentName, 'AQR.Reader')
var adminRoleId = guid(tenantId, environmentName, 'AQR.Admin')
var graphAppId = '00000003-0000-0000-c000-000000000000'
var graphUserReadScopeId = 'e1fe6dd8-ba31-4d61-89e7-88639da4683d'
var groupMemberReadAllRoleId = '98830695-27a2-44f7-8c18-0c3ebc9c56cd'

resource app 'Microsoft.Graph/applications@v1.0' = {
  uniqueName: appUniqueName
  displayName: 'AQR (${environmentName})'
  signInAudience: 'AzureADMyOrg'
  groupMembershipClaims: 'SecurityGroup'
  web: {
    homePageUrl: appServiceUrl
    redirectUris: [
      '${appServiceUrl}/.auth/login/aad/callback'
    ]
    implicitGrantSettings: {
      enableIdTokenIssuance: true
      enableAccessTokenIssuance: false
    }
  }
  api: {
    requestedAccessTokenVersion: 2
  }
  appRoles: [
    {
      id: readerRoleId
      allowedMemberTypes: [
        'User'
        'Application'
      ]
      description: 'Can view AQR reports scoped by admin mappings'
      displayName: 'AQR.Reader'
      isEnabled: true
      value: 'AQR.Reader'
    }
    {
      id: adminRoleId
      allowedMemberTypes: [
        'User'
      ]
      description: 'Full access including access mappings and sync'
      displayName: 'AQR.Admin'
      isEnabled: true
      value: 'AQR.Admin'
    }
  ]
  requiredResourceAccess: [
    {
      resourceAppId: graphAppId
      resourceAccess: [
        {
          id: graphUserReadScopeId
          type: 'Scope'
        }
      ]
    }
  ]

  resource uamiFic 'federatedIdentityCredentials@v1.0' = {
    name: '${app.uniqueName}/aqr-uami'
    description: 'Trust the AQR user-assigned managed identity for App Service Easy Auth.'
    issuer: '${environment().authentication.loginEndpoint}${tenantId}/v2.0'
    subject: uamiPrincipalId
    audiences: [
      'api://AzureADTokenExchange'
    ]
  }
}

resource servicePrincipal 'Microsoft.Graph/servicePrincipals@v1.0' = {
  appId: app.appId
}

resource sqlAdminGroup 'Microsoft.Graph/groups@v1.0' = {
  uniqueName: sqlAdminGroupUniqueName
  displayName: 'AQR SQL Admins (${environmentName})'
  mailEnabled: false
  mailNickname: 'aqr-sql-admins-${uniqueString(tenantId, environmentName)}'
  securityEnabled: true
  members: {
    relationshipSemantics: 'replace'
    relationships: sqlAdminMemberObjectIds
  }
}

resource adminRoleAssignment 'Microsoft.Graph/appRoleAssignedTo@v1.0' = if (!empty(adminGroupId)) {
  appRoleId: adminRoleId
  principalId: adminGroupId
  resourceId: servicePrincipal.id
}

resource readerRoleAssignment 'Microsoft.Graph/appRoleAssignedTo@v1.0' = if (!empty(readerGroupId)) {
  appRoleId: readerRoleId
  principalId: readerGroupId
  resourceId: servicePrincipal.id
}

resource microsoftGraph 'Microsoft.Graph/servicePrincipals@v1.0' existing = {
  appId: graphAppId
}

resource graphGroupMemberReadAssignment 'Microsoft.Graph/appRoleAssignedTo@v1.0' = if (grantGraphGroupMemberRead) {
  appRoleId: groupMemberReadAllRoleId
  principalId: uamiPrincipalId
  resourceId: microsoftGraph.id
}

@description('The AQR application client ID.')
output appId string = app.appId

@description('The AQR service principal object ID.')
output servicePrincipalId string = servicePrincipal.id

@description('The generated SQL admin group object ID.')
output sqlAdminGroupId string = sqlAdminGroup.id

@description('The generated SQL admin group display name.')
output sqlAdminGroupDisplayName string = sqlAdminGroup.displayName
