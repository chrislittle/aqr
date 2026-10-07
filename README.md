# Azure Quota Reporting (AQR)

Org-wide reporting on **Azure VM quota**: subscription quota, **Azure Quota Groups**, and **zonal SKU access**, with
a year of point-in-time history. It's deployed into your own subscription with **`azd up`**. Users sign in with
**Microsoft Entra ID**, and the data store sits behind a **Network Security Perimeter**.

![Architecture](docs/images/architecture.png)

## What it answers

| Question | Source (all read-only ARM APIs) |
|---|---|
| How much vCPU quota do I have per subscription × region × VM family, and how much is used? | Resource Graph `QuotaResources` |
| What does each Quota Group hold, what's unallocated, and who got it? | `Microsoft.Quota` 2025-09-01 |
| Can this subscription actually deploy that family's SKUs, and **in which zones**? | Compute Resource SKUs + `availabilityZoneMappings` |
| What did all of this look like on any date in the last year? | Azure SQL temporal tables (`FOR SYSTEM_TIME AS OF`) |

Vocabulary for every filter (CPU manufacturer Intel / AMD / Microsoft Cobalt / Ampere, Arm64, GPU vendor and model,
generation, features, lifecycle) comes from Microsoft's own taxonomy. [`catalog/vm-families.json`](catalog/vm-families.json)
cites a Microsoft Learn page for every family it describes.

## Screenshots

Real app, synthetic data (`Aqr:UseMock=true`).

| Overview | Quota explorer |
|---|---|
| ![Overview](docs/images/app-overview.png) | ![Explorer](docs/images/app-explorer.png) |

| Quota groups | Zones & SKU access |
|---|---|
| ![Quota groups](docs/images/app-groups.png) | ![Zones](docs/images/app-zones.png) |

| VM families | API docs (`/api/docs`) |
|---|---|
| ![Families](docs/images/app-families.png) | ![API docs](docs/images/app-apidocs.png) |

## Quickstart (local, synthetic data)

```powershell
dotnet run --project src/Aqr.Web          # in-memory, current state only
pwsh ./scripts/dev-sql.ps1                # or: the real Azure SQL engine locally (temporal history)
dotnet test                               # unit, SQL-translation, web/auth tests (+ SQL tests with the container)
```

See [docs/LOCAL_DEV.md](docs/LOCAL_DEV.md) for the Azure SQL Database container (Private Preview) setup.

## Deploy

```powershell
az login; az account set --subscription <id>; az account show     # confirm the target subscription first
azd env new aqr-prod
azd env set AZURE_SUBSCRIPTION_ID <id>
azd env set AQR_MANAGEMENT_GROUP_IDS <mg-id>[,<mg-id>]                   # and/or:
azd env set AQR_SUBSCRIPTION_IDS <sub-id>[,<sub-id>]                  # explicit subscriptions (no MG needed)
azd env set AQR_ADMIN_GROUP_ID <entra-group-object-id>                # AQR.Admin
azd env set AQR_READER_GROUP_ID <entra-group-object-id>               # AQR.Reader (optional)
azd up
```

- `preprovision` refuses to run if your az CLI context isn't `AZURE_SUBSCRIPTION_ID`.
- `postprovision` prints the management-group **Reader** assignments the app's identity needs (it runs them only with
  `AQR_ASSIGN_MG_RBAC=true`).
- The network defaults to `AQR_SQL_NETWORK=nsp` with `AQR_NSP_MODE=Learning`. Switch the mode to `Enforced` once the
  NSP access logs look right.

Infrastructure details, parameters and permissions are in [infra/README.md](infra/README.md). Everything uses
**Azure Verified Modules** except the Entra app registration, which uses the Microsoft Graph Bicep extension.

## Who sees what

App roles decide *whether* someone can use AQR. Admin-maintained grants decide *which subscriptions* they see, by
management group, quota group or subscription (**Admin → Report access**). Report visibility is deliberately separate
from Azure RBAC. Readers with no grant see nothing, and every grant change is audited through temporal history.

## Docs

| Doc | Contents |
|---|---|
| [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) | Design: data sources, regional vs zonal model, SQL temporal data model, sync, decisions D1–D9 |
| [docs/LOCAL_DEV.md](docs/LOCAL_DEV.md) | Local development with the Azure SQL Database container |
| [infra/README.md](infra/README.md) | AVM-based infrastructure, network modes, permissions |
| [docs/mockups/aqr-mockup.html](docs/mockups/aqr-mockup.html) | Original clickable UI mockup (superseded by the app) |

## Repository layout

```
src/Aqr.Core      sync pipeline, ARM data sources, EF Core model + migrations (temporal), reports, visibility
src/Aqr.Web       Razor Pages UI, /api/v1 (OpenAPI at /openapi/v1.json, Swagger UI at /api/docs), Easy Auth, sync worker host
tests/Aqr.Tests   xUnit tests
catalog/          VM family catalog (cited to Microsoft Learn)
infra/            Bicep (AVM) for azd
hooks/            azd pre/post-provision hooks
scripts/          local dev helpers
.agents/skills/   Microsoft's Azure SQL Database container agent skills
```
