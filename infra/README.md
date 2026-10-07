# AQR infrastructure

> [!WARNING]
> **Experimental. Not working.** Work-in-progress prototype; see the [README](../README.md) before using anything here.

Deploys the approved Azure Quota Reporting (AQR) design with `azd up`. The template is subscription-scoped and creates `rg-aqr-<environment>`, then deploys Azure-native resources into it.

## What gets deployed

- User-assigned managed identity used by App Service, Easy Auth federated credential, SQL, and Storage.
- Workspace-based Application Insights and Log Analytics for telemetry only.
- Linux App Service plan and .NET 10 App Service with Easy Auth v2.
- Entra application, service principal, app roles, federated identity credential, app-role assignments, and generated SQL admin security group using the Microsoft Graph Bicep extension.
- Azure SQL logical server with Entra-only authentication and `sqldb-aqr`.
- Storage account with `raw` container, shared key disabled, and a 400-day raw archive lifecycle rule.
- Either Network Security Perimeter (`nsp`) or VNet/private endpoint networking.

## AVM modules

| Resource | Module | Version |
| --- | --- | --- |
| Resource group | `br/public:avm/res/resources/resource-group` | `0.4.4` |
| User-assigned managed identity | `br/public:avm/res/managed-identity/user-assigned-identity` | `0.6.0` |
| Log Analytics workspace | `br/public:avm/res/operational-insights/workspace` | `0.16.1` |
| Application Insights | `br/public:avm/res/insights/component` | `0.8.0` |
| App Service plan | `br/public:avm/res/web/serverfarm` | `0.7.0` |
| App Service | `br/public:avm/res/web/site` | `0.24.0` |
| Azure SQL server/database | `br/public:avm/res/sql/server` | `0.22.1` |
| Storage account | `br/public:avm/res/storage/storage-account` | `0.33.1` |
| Network Security Perimeter | `br/public:avm/res/network/network-security-perimeter` | `0.1.4` |
| Virtual network | `br/public:avm/res/network/virtual-network` | `0.10.2` |
| Private DNS zone | `br/public:avm/res/network/private-dns-zone` | `0.8.1` |

Microsoft Graph uses the Bicep extension `br:mcr.microsoft.com/bicep/extensions/microsoftgraph/v1.0:1.0.0`; no AVM module exists for those tenant objects.

## Parameters

| Bicep parameter | azd env var | Default | Notes |
| --- | --- | --- | --- |
| `environmentName` | `AZURE_ENV_NAME` | required | Names all AQR resources. |
| `location` | `AZURE_LOCATION` | required | Azure region. |
| `principalId` | `AZURE_PRINCIPAL_ID` | required | Added to generated SQL admin group. |
| `managementGroupIds` | `AQR_MANAGEMENT_GROUP_IDS` | `''` | Comma-separated MG IDs to sync. |
| `regions` | `AQR_REGIONS` | `''` | Optional comma-separated region allow-list. |
| `sqlNetwork` | `AQR_SQL_NETWORK` | `nsp` | `nsp` or `privateEndpoint`. |
| `nspMode` | `AQR_NSP_MODE` | `Learning` | `Learning` transition mode or `Enforced`. |
| `sqlSku` | `AQR_SQL_SKU` | `S1` | SQL DB SKU name. |
| `sqlPitrDays` | `AQR_SQL_PITR_DAYS` | `7` | 1-35 days. |
| `historyRetention` | `AQR_HISTORY_RETENTION` | `1 YEAR` | Consumed by app migrations for temporal tables. |
| `sqlAdminObjectIds` | `AQR_SQL_ADMIN_OBJECT_IDS` | `''` | Comma-separated extra SQL admin group members. |
| `adminGroupId` | `AQR_ADMIN_GROUP_ID` | `''` | Optional group assigned `AQR.Admin`. |
| `readerGroupId` | `AQR_READER_GROUP_ID` | `''` | Optional group assigned `AQR.Reader`. |
| `grantGraphGroupMemberRead` | `AQR_GRANT_GRAPH_GROUPMEMBER_READ` | `false` | Grants Graph `GroupMember.Read.All` app role to the UAMI; privileged admin required. |
| `appServiceSku` | `AQR_APP_SERVICE_SKU` | `P0v3` | Linux App Service plan SKU. |

## Network modes

### `AQR_SQL_NETWORK=nsp` (default)

Creates a Network Security Perimeter profile and associates SQL and Storage. The inbound access rule allows the current subscription. SQL Database NSP is public preview and Storage NSP is GA. SQL `publicNetworkAccess` is set to `SecuredByPerimeter`; the Azure SQL 2023-08-01+ REST specs list `Enabled`, `Disabled`, and `SecuredByPerimeter`, and the SQL NSP Learn article documents transition/learning and enforced behavior.

### `AQR_SQL_NETWORK=privateEndpoint`

Creates `10.40.0.0/24` VNet, delegated App Service integration subnet, private endpoint subnet, SQL/blob private DNS zones linked to the VNet, and private endpoints from the SQL and Storage AVM modules. SQL and Storage public network access are disabled.

## Prerequisites and permissions

- Azure CLI, Azure Developer CLI, PowerShell 7, and Bicep/`az bicep` with internet access to restore `br/public` modules.
- The deploying principal needs subscription permissions to create the resource group/resources and RBAC role assignments, plus Microsoft Graph permissions to create applications, service principals, groups, federated identity credentials, and optional app-role assignments.
- If `AQR_GRANT_GRAPH_GROUPMEMBER_READ=true`, a privileged admin must grant Graph application permission assignment.
- Management-group Reader assignments are outside subscription-scope Bicep. `hooks/postprovision.ps1` prints exact commands and only runs them when `AQR_ASSIGN_MG_RBAC=true`.
- Microsoft.Quota and Microsoft.Compute must be registered in member subscriptions.

## Known limitations

- SQL NSP is preview and public cloud only. Use `privateEndpoint` if preview terms are not acceptable.
- With NSP enforced, the deployer might not be able to reach SQL to create contained users. The generated SQL admin group includes the app identity so it can administer its own database and apply migrations; the trade-off is that the app is dbo for its database.
- Entra tenant resources are not deleted automatically with the resource group; clean up the app registration/service principal and generated group when permanently deleting an environment.
