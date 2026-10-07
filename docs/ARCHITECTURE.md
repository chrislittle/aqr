# Azure Quota Reporting (AQR) — Architecture Design

> **Status:** v0.3 · 2026-10-07 · decisions D1–D9 applied (§13) · phase-1 code scaffolded (`src/`, `infra/`)
> **Scope of v1:** Virtual Machine (Compute) quota — subscription quota **and** Azure Quota Groups,
> with zonal SKU access reported separately from regional vCPU quota.
> **Mockup:** [`docs/mockups/aqr-mockup.html`](mockups/aqr-mockup.html) (open in a browser — filters work on synthetic data)
> **Diagram:** [`docs/images/src/architecture.html`](images/src/architecture.html)

---

## 1. Problem

Azure compute capacity planning needs three answers that the portal spreads across separate blades,
one subscription at a time:

| Question | Where Azure keeps the answer | Granularity |
|---|---|---|
| How many vCPUs of family X may I run in region R? | Compute usages / Quota API / ARG `QuotaResources` | **Subscription × region × VM family** (regional) |
| How much shared quota does my Quota Group hold, and where is it allocated? | `Microsoft.Quota/groupQuotas` | **Quota group × region × VM family**, plus per-subscription allocations |
| Can this subscription actually deploy SKU S of that family, and **in which zones**? | Compute Resource SKUs API (`restrictions`, `locationInfo.zones`) | **Subscription × region × SKU × zone** |

A family can have 1,000 vCPUs of quota in `eastus2` and still be undeployable in zone 2 for that
subscription. AQR stores all three, keeps them as separate facts, and lets users filter across them
by region, zone, VM family, CPU manufacturer, accelerator, generation, lifecycle and more.

## 2. Goals / non-goals

**Goals (v1)**
- Org-wide VM quota reporting across all subscriptions under one or more management groups.
- Quota Group reporting: group limit, unallocated pool, per-subscription allocations, group usage.
- **Regional quota vs zonal access are separate columns and separate filters** (see §6).
- Rich filtering: region/geography, zone, VM family, category, CPU manufacturer (Intel / AMD /
  Microsoft Cobalt / Ampere), architecture (x64 / Arm64), accelerator (GPU / FPGA, vendor, model),
  generation, features, lifecycle, utilization thresholds.
- **1-year history** with point-in-time ("as of") reporting.
- Entra ID sign-in; report visibility mapped by admins, independent of Azure RBAC (§4.3).
- Data store behind a **Network Security Perimeter (NSP)**.
- Everything API-driven (no portal scraping, no CSV uploads). Deploy with **`azd up`**.

**Non-goals (v1)**
- Requesting/transferring quota (read-only first; write actions are a later phase).
- Spot / low-priority quota (`lowPriorityCores`) and Dedicated Host quota. Phase 2 (§12).
- Multiple tenants. v1 is **single tenant** (one Entra tenant, one set of management groups).
- Non-compute services (Network, ML, HPC Cache, Storage, Purview via `Microsoft.Quota/usages`) —
  planned for phase 3 (§12).
- Real-time capacity guarantees. Zonal *access* (subscription restriction) is reportable; physical
  datacenter *capacity* is not exposed by any API, and AQR will not imply it is.

## 3. Data sources (all API-based)

Every source below is a documented, GA Azure Resource Manager API. AQR calls them with a managed
identity; nothing is scraped.

| # | Source | What AQR takes from it | Call shape | Cadence (default) |
|---|---|---|---|---|
| S1 | **Azure Resource Graph — `QuotaResources`** table, type `microsoft.compute/locations/usages` | Per-sub, per-region usage and limit for `cores` (Total Regional vCPUs) and every `standard*Family`. Other rows in the payload (`lowPriorityCores`, dedicated hosts, VM/VMSS counts) are dropped in v1 | One paged KQL query across all in-scope subscriptions (`mv-expand properties.value`) | Hourly |
| S2 | **Compute Usages API** `GET /subscriptions/{id}/providers/Microsoft.Compute/locations/{loc}/usages` | Fallback / reconciliation for any subscription-region missing from S1 | Per sub × region | Only on gap |
| S3 | **Quota Groups API** `Microsoft.Quota` **2025-09-01** (GA) | Groups, members, group limits, unallocated pool, per-sub allocations, group usage | MG-scoped REST (§3.1) | Every 4 h |
| S4 | **Compute Resource SKUs API** `GET /subscriptions/{id}/providers/Microsoft.Compute/skus?$filter=location eq '{loc}'` | SKU → family mapping, vCPUs, memory, GPUs, `CpuArchitectureType`, RDMA, offered zones, **per-subscription region and zone restrictions** | Per sub × in-scope region | Daily |
| S5 | **Subscriptions — List Locations** `GET /subscriptions/{id}/locations` | `availabilityZoneMappings` (logical ↔ physical zone per subscription) | Per sub | Daily |
| S6 | **Management Groups / ARG `ResourceContainers`** | Subscription inventory, names, MG path, state | One ARG query | Hourly (with S1) |
| S7 | **VM family catalog** (in repo, `catalog/vm-families.json`) | CPU manufacturer, accelerator vendor/model, category, lifecycle — each row cites its Learn page | Static file, versioned in git | On deploy |

Sources verified against Microsoft Learn on 2026-10-07:
- `QuotaResources` + sample queries: <https://learn.microsoft.com/azure/quotas/how-to-guide-monitoring-alerting>
  ("Currently, Compute is the only supported resource for NRT limit/quota data").
- Quota Groups concepts and limits: <https://learn.microsoft.com/azure/quotas/quota-groups>
- Quota REST (GA, 2025-09-01 is the latest stable spec in `azure-rest-api-specs`): <https://learn.microsoft.com/rest/api/quota/>
- Resource SKUs restrictions/zones: <https://learn.microsoft.com/rest/api/compute/resourceskus/list>
- Logical vs physical zones: <https://learn.microsoft.com/azure/reliability/availability-zones-overview#physical-and-logical-availability-zones>
- VM naming (`a` = AMD, `p` = Arm): <https://learn.microsoft.com/azure/virtual-machines/vm-naming-conventions>
- Capacity-restricted series: <https://learn.microsoft.com/azure/virtual-machines/sizes/lifecycle/retirements-and-capacity-restrictions>

### 3.1 Quota Group calls (S3)

All under `/providers/Microsoft.Management/managementGroups/{mgId}/providers/Microsoft.Quota/groupQuotas`:

| Operation | Path suffix | Gives |
|---|---|---|
| `GroupQuotas_List` | `` | Groups owned by the MG |
| `GroupQuotaSubscriptions_List` | `/{group}/subscriptions` | Member subscriptions |
| `GroupQuotaLimits_List` | `/{group}/resourceProviders/Microsoft.Compute/groupQuotaLimits/{location}` | Per family: `limit` (group), `availableLimit` (**unallocated pool**), `allocatedToSubscriptions[]` (sub → `quotaAllocated`) |
| `GroupQuotaUsages_List` | `/{group}/resourceProviders/Microsoft.Compute/locationUsages/{location}` | Per family: group `limit`, `usages` across members |
| `GroupQuotaSubscriptionAllocation_List` *(optional)* | `…/subscriptions/{sub}/…/quotaAllocations/{location}` | Per-sub `limit` and `shareableQuota` |
| `GroupQuotaLimitsRequest_List` *(phase 2)* | `/{group}/resourceProviders/Microsoft.Compute/groupQuotaRequests` | Pending/approved/rejected group increase requests |

`allocatedToSubscriptions` on the limits call already gives per-sub allocations, so the per-sub
allocation call is only needed for `shareableQuota`. That keeps the call count at
**groups × regions × 2** rather than groups × regions × subscriptions.

Facts from the Quota Groups doc that the report must reflect (not hide):
- **Deployments are checked against the subscription quota, not the group.** Group pool vCPUs are
  only usable after they're allocated to a subscription. The UI labels the pool "unallocated — not
  deployable until allocated".
- A subscription belongs to at most one group. Groups are MG-scoped ARM objects but **don't
  auto-sync membership** from the MG.
- Quota Groups don't grant regional or zonal access. A sub can hold allocated quota and still be
  restricted in a region or zone. That's exactly what §6 surfaces.
- EA / MCA / Internal subscriptions only; IaaS compute only; public cloud only.

## 4. Architecture

![architecture](images/architecture.png)
*(Source: [`docs/images/src/architecture.html`](images/src/architecture.html).)*

```
 Browser ──HTTPS──► App Service (Linux, .NET 10)  ◄── Easy Auth (Entra ID, app roles)
                     │  Razor Pages UI + /api/v1 (OpenAPI)
                     │  SyncWorker (BackgroundService · single writer via SQL app lock)
                     │
                     │ user-assigned managed identity (UAMI)
                     ├──► ARM: Resource Graph, Microsoft.Quota, Microsoft.Compute/skus, Subscriptions
                     │
                     ├──► Azure SQL Database  (Entra-only auth · temporal tables)  ┐ Network Security Perimeter
                     └──► Storage account     (raw API archive · blob)             ┘ (or private endpoint, §5.2)

 Application Insights (+ its Log Analytics workspace) ◄── app telemetry and sync-health metrics only, never report data
```

### 4.1 Components

| Component | Choice | Why |
|---|---|---|
| Web + API | **Azure App Service (Linux), .NET 10, Razor Pages + minimal APIs** | Same stack as `ghcp-credit-visibility-azure`, so the UI style and auth patterns carry over. One deployable for `azd`. |
| Auth | **App Service Easy Auth → Entra ID**, app roles `AQR.Reader`, `AQR.Admin` | No auth code in the app. **Secretless**: Easy Auth uses the UAMI as a federated credential (`OVERRIDE_USE_MI_FIC_ASSERTION_CLIENTID`) per [Learn](https://learn.microsoft.com/azure/app-service/configure-authentication-provider-aad#use-a-managed-identity-instead-of-a-secret), so there's no client secret and no Key Vault in v1. |
| Sync | **In-process `BackgroundService`**; a single writer is guaranteed by a SQL application lock (`sp_getapplock`) | Fewest moving parts. Same pattern as `SqlDistributedLease` in `ghcp-credit-visibility-azure`. Alternative in §9. |
| Data store (system of record) | **Azure SQL Database**, **system-versioned temporal tables**, Entra-only authentication | Decision D7, §5. Current state and 1-year history in one service, with native point-in-time queries. |
| Raw archive | **Storage account (blob)**, shared-key disabled | Gzipped API responses for audit and replay. NSP **GA**. |
| Observability | Workspace-based Application Insights | Sync health: snapshot age, keys seen/changed, subscriptions covered vs expected, throttling. Holds telemetry only. |
| IaC | **Bicep + `azure.yaml`** → `azd up` | azd-native. `azd provision` creates everything, `azd deploy` publishes the app. |

All services are Azure-native (decision D8).

### 4.2 Identity and RBAC (least privilege, read-only to Azure)

| Principal | Role | Scope | Purpose |
|---|---|---|---|
| AQR UAMI | **Reader** | Each in-scope **management group** (or the tenant root MG) | ARG `QuotaResources`/`ResourceContainers`, Resource SKUs, Subscriptions locations, and `Microsoft.Quota/groupQuotas/*/read` (Reader includes `*/read`) |
| Entra group **AQR SQL Admins (env)**, created by `azd up` | Microsoft Entra admin of the logical server | SQL logical server | Members: the AQR UAMI, the deployer, plus `AQR_SQL_ADMIN_OBJECT_IDS`. **Entra-only authentication**: no SQL logins or passwords exist. The app gets database access through this group instead of a `CREATE USER` step, because with the NSP enforced the deployer's machine can't reach SQL to run one. Trade-off: the app is `dbo` of its own database (it also applies EF Core migrations). |
| AQR UAMI | **Storage Blob Data Contributor** | Storage account | Raw archive |
| AQR UAMI | Microsoft Graph `GroupMember.Read.All` (application) | Tenant | Group-overage resolution for report visibility (§4.3) |
| Users | App role `AQR.Reader` / `AQR.Admin` (assigned to Entra groups) | Enterprise app | UI/API access |

Management-group role assignments live outside the app's resource group. `azd up` can only create
them if the deployer has rights at that MG, so the Bicep makes them conditional
(`AQR_MANAGEMENT_GROUP_IDS`). Otherwise a `postprovision` hook prints the exact `az role assignment
create` commands for an MG admin to run. Prerequisite from the Quota docs: `Microsoft.Quota` and
`Microsoft.Compute` registered on member subscriptions; the sync reports any that aren't.

No write roles (Quota Request Operator, GroupQuota Request Operator) in v1.

### 4.3 Report visibility (decision D6 — admin-maintained mapping)

Report visibility is **separate from Azure RBAC**. The sync identity reads everything; what a
signed-in user sees is decided by AQR's own mapping, maintained by `AQR.Admin`s:

| Grant | Target | Effect |
|---|---|---|
| Entra group (or user) → **management group** | MG ID | All subscriptions under that MG, resolved **at query time** from the stored MG path, so subscriptions moved in or out of the MG are picked up automatically |
| Entra group → **quota group** | MG + group name | The group's pool, allocations and its current member subscriptions |
| Entra group → **subscription** | Subscription ID | That subscription only |

Rules:
- **Deny by default.** A signed-in `AQR.Reader` with no mapping sees an empty state that names the
  admin contact. They never see "everything".
- `AQR.Admin` sees all data.
- A user's scope is the **union** of all grants from their groups.
- Scope is applied **server-side** as a subscription-ID filter on every query and API call. It's
  never a UI-only filter, and it also applies to CSV export.
- Group membership comes from the token's `groups` claim. If the user is in too many groups for the
  token to list them (the group overage case), AQR resolves transitive membership through Microsoft
  Graph with a cached lookup (requires the `GroupMember.Read.All` application permission), so large
  tenants don't silently lose access.
- Every mapping change is audited (who, when, before/after) in the data store.


## 5. Data store — Azure SQL Database with temporal tables (decision D7)

### 5.1 Why

- **It's a real system of record.** Transactional and correctable (rows can be updated or deleted
  properly). Protected by automated point-in-time-restore backups, with long-term backup retention
  available.
- **History is native.** Fact tables are system-versioned temporal tables. When a current row
  changes, SQL Server moves the old version to the history table with its validity period. No
  history code in the app.
- **Point-in-time reports are one clause.** `FOR SYSTEM_TIME AS OF @t` returns exactly what quota
  looked like at any moment in the retention window. `FOR SYSTEM_TIME FROM @a TO @b` feeds trends.
- **Retention is native.** `HISTORY_RETENTION_PERIOD = 1 YEAR` per table, with automatic background
  cleanup of aged rows. This requires a clustered rowstore or clustered columnstore index on the
  history table
  ([Learn: manage temporal history retention](https://learn.microsoft.com/sql/relational-databases/tables/manage-retention-of-historical-data-in-system-versioned-temporal-tables)).
  Applies to Azure SQL Database.
- **Filtering is plain, indexed SQL** across every §7 dimension. The access mapping and its audit
  trail (§4.3) are relational data too, and EF Core matches the `ghcp-credit-visibility-azure` stack.

Rejected:
- **Log Analytics**: not a system of record, and $2.76/GB ingestion (Appendix A).
- **Table / Blob storage**: no server-side filtering or aggregation, so the app would load the
  dataset into memory for every report.
- **Cosmos DB**: NSP is preview there too, and it's weak at ad-hoc aggregation.

### 5.2 Network

| `AQR_SQL_NETWORK` | What gets deployed | Status |
|---|---|---|
| **`nsp`** (default — decision D9) | SQL logical server and storage account associated with one Network Security Perimeter profile. Inbound rule: **subscription-based**, allowing managed identities from the AQR subscription. No IP rules. Start in **Transition** mode, then switch to **Enforced** (`AQR_NSP_MODE`). In Enforced mode a denied login fails with error 42118. | **SQL Database NSP is public preview** (Supplemental Terms of Use for Azure Previews apply; Azure public cloud only). Storage NSP is GA. [Learn](https://learn.microsoft.com/azure/azure-sql/database/network-security-perimeter) |
| `privateEndpoint` | VNet, App Service regional VNet integration, private endpoints for SQL and storage, `privatelink.database.windows.net` / `privatelink.blob.core.windows.net` private DNS zones, public network access disabled | GA |

Switching between the two is an azd parameter. The app code doesn't change. If preview terms aren't
acceptable for production, deploy `privateEndpoint` now and move to `nsp` when SQL NSP is GA.

Either way: Entra-only authentication, minimum TLS 1.2, no SQL logins, and Microsoft Defender for
SQL optional (`AQR_SQL_DEFENDER`).

### 5.3 Sizing and cost

Prices are list prices for `eastus2`, pulled live from the Azure Retail Prices API on 2026-10-07.

| Option | Price | Fit |
|---|---|---|
| **Standard S1** (20 DTU, 250 GB included) — **default** | $0.9677/day ≈ **$29.42/month** | Headroom for the first full baseline load and report aggregations |
| Standard S0 (10 DTU, 250 GB included) | $0.4839/day ≈ $14.71/month | Viable after measurement if DTU stays low |
| Standard S3 (100 DTU) | $4.8387/day ≈ $147.10/month | Only if reports need columnstore. Columnstore isn't available below S3 |
| General Purpose **serverless** (Gen5) | $0.521758 per vCore-hour + $0.115/GB-month storage | Not a fit. The hourly sync keeps auto-pause from saving much, a paused database adds about a minute of resume delay on the first page view, and never pausing at the 0.5 vCore minimum is ≈ $190/month |
| PITR backup storage (LRS) beyond the included amount | $0.10/GB-month | — |

Monthly figures use 30.4 days (730 hours).

**Volume model.** These are assumptions until the §9.2 sizing query is run against your tenant:
640,000 current quota keys, 1 % changing per hourly sync ≈ 154,000 history rows/day ≈ 56 M rows
per year. With narrow typed columns and integer surrogate keys (~120 bytes/row) plus page
compression and indexes, that's roughly 10–15 GB after a year, well inside the 250 GB included
with S0/S1. `sync.Run` records keys seen/changed every run (§9), so the real numbers replace these
within days. Scale the tier from the measured DTU %, not from this estimate.

### 5.4 Retention

- Every temporal table: `HISTORY_RETENTION_PERIOD = 1 YEAR` (`AQR_HISTORY_RETENTION`, can be raised
  to longer periods or `INFINITE`). This meets "at least a year".
- Temporal history cleanup must be enabled at database level (`TEMPORAL_HISTORY_RETENTION ON`). The
  migration sets it and the Admin page verifies it.
- Backups: point-in-time restore window 7 days by default (`AQR_SQL_PITR_DAYS`, up to 35). Optional
  long-term backup retention for compliance. History lives inside the database, so backups protect it.

## 6. Regional quota vs zonal access (first-class requirement)

### 6.1 The three different facts

| Fact | Grain | Source | Shown as |
|---|---|---|---|
| **vCPU quota** (limit/usage) | Subscription × **region** × family. Quota is **regional only**; there's no per-zone vCPU quota. | S1 / S3 | Regional meters, "Quota (regional)" column |
| **SKU offered in zone** | Region × SKU × zone | S4 `locationInfo[].zones` / `zoneDetails` | Zone pills: grey = not offered |
| **Subscription restricted** | Subscription × region (type `Location`) **or** × zone (type `Zone`) × SKU | S4 `restrictions[]` with `reasonCode` (`NotAvailableForSubscription`, `QuotaId`) and `restrictionInfo.zones` | Zone pills: red = restricted for this sub; region badge "Region blocked" |

**Zone open** = offered in zone **and** not restricted for the subscription.
**Usable vCPUs in zone z** (reported, not invented) = regional available quota **if** zone z is open,
else 0. AQR never splits regional quota into per-zone numbers.

### 6.2 Logical vs physical zones

Logical zone numbers are **per subscription**. Zone "1" in sub A can be a different physical
datacenter than zone "1" in sub B (Learn: *Physical and logical availability zones*). AQR therefore:
- stores both `ZoneLogical` (what you put in a template for *that* sub) and `ZonePhysical`
  (for example, `eastus2-az1`) from S5 `availabilityZoneMappings`;
- by default shows **logical** zones on any single-subscription view and **physical** zones on any
  cross-subscription view (Quota Groups, multi-sub explorer), with a toggle;
- shows a "zone mapping differs across selected subscriptions" banner when it applies.

### 6.3 Family-level rollup and SKU drill-down

Quota is per **family**, access is per **SKU**. Per (sub, region, family) AQR stores:
`SkusTotal`, `SkusRegionOpen`, and per zone `SkusOpenZ1..Z3` (+ physical names), so the family row
can say "zone 2: 3 of 7 SKUs open". The SKU-level table keeps **only restricted or partially
restricted SKUs**, which keeps volume small and still supports drill-down.

Status values used everywhere:
- `Regional` — region has no AZs, or SKU not offered zonally (non-zonal deployment only)
- `AllZones` — open in every offered zone
- `PartialZones` — open in some zones (lists which)
- `NoZones` — offered zonally but restricted in all zones (regional/non-zonal may still work)
- `RegionBlocked` — `Location` restriction for this subscription

## 7. Filtering dimensions

Vocabulary follows Microsoft's own VM attribute taxonomy (Azure Compute Fleet `VMAttributes`
enums in `azure-rest-api-specs` `Microsoft.AzureFleet` 2026-08-01): `CpuManufacturer` = Intel | AMD |
Microsoft | Ampere; `ArchitectureType` = X64 | ARM64; `AcceleratorType` = GPU | FPGA;
`AcceleratorManufacturer` = Nvidia | AMD | Xilinx; `VMCategory` = GeneralPurpose |
ComputeOptimized | MemoryOptimized | StorageOptimized | GpuAccelerated | FpgaAccelerated |
HighPerformanceCompute.

| Filter | Values | Derived from |
|---|---|---|
| Scope | Tenant / management group / quota group / subscription(s) | S3, S6 |
| Geography → Region | e.g. United States → eastus, eastus2… | S5 region metadata (`geographyGroup`, `physicalLocation`) |
| Zone access | Regional · AllZones · PartialZones · NoZones · RegionBlocked; "must be open in zone(s) …" (logical or physical) | §6 |
| Quota type | Family · Total regional vCPUs (`cores`) | Quota name. Spot/low-priority and Dedicated Host are phase 2 |
| VM category | `VMCategory` values above | Catalog S7 |
| VM family / series | standardDSv5Family, standardNCADSH100v5Family, … | S1 name ↔ S4 `family` |
| CPU manufacturer | Intel · AMD · Microsoft (Cobalt) · Ampere | Catalog S7 (name rule `a`/`p` as fallback, never sole source) |
| Architecture | x64 · Arm64 | S4 `CpuArchitectureType` |
| Accelerator | None · GPU · FPGA; vendor Nvidia/AMD/Xilinx; model (H100, H200, A100, A10, T4, V100, MI300X…) | S4 `GPUs` + catalog |
| Generation | v1…v7 | Parsed from family + catalog |
| Features | Premium storage (`s`), local temp disk (`d`), RDMA/InfiniBand, confidential, burstable, constrained vCPU | S4 capabilities + naming convention |
| Lifecycle | Current · Capacity-restricted (per Learn retirements page) · Retired | Catalog S7 |
| Quota group membership | In group X · not in any group | S3 |
| Utilization | ≥ N % · at limit · zero limit · unused quota (limit > 0, usage = 0) | S1/S3 |
| Headroom | available vCPUs ≥ N | computed |

Why the catalog file exists: Resource SKUs has no CPU-vendor or GPU-model field. The naming
convention (`a` = AMD, `p` = Arm) is the documented rule, but it doesn't cover everything (for example,
HB-series is AMD without an `a`). Every catalog row carries a `source` Learn URL. Unknown families
show as `Unclassified` rather than being guessed.

## 8. Data model (Azure SQL Database)

Schemas: `dim` (reference data), `quota` and `zone` (facts), `access` (report visibility), `sync`
(operations). Every table marked **temporal** is
`SYSTEM_VERSIONING = ON (HISTORY_TABLE = …, HISTORY_RETENTION_PERIOD = 1 YEAR)`, with a clustered
rowstore index and page compression on the history table.

| Table | Temporal | Key | Columns |
|---|---|---|---|
| `dim.Subscription` | ✔ | SubscriptionId | Name, ManagementGroupPath, State, QuotaGroupKey |
| `dim.Region` | — | Region | Geography, PhysicalLocation, HasZones |
| `dim.VmFamily` | ✔ | FamilyId (quota name, e.g. `standardDSv5Family`) | Series, Category, CpuManufacturer, Architecture, AcceleratorType, AcceleratorVendor, AcceleratorModel, Generation, Features, Lifecycle, MinVcpu, MaxVcpu, SourceUrl |
| `quota.SubscriptionQuota` | ✔ | SubscriptionId, Region, QuotaName | Kind (`Family`/`RegionalTotal`), FamilyId, Usage, Limit |
| `quota.QuotaGroup` | ✔ | ManagementGroupId, GroupName | DisplayName, ProvisioningState |
| `quota.QuotaGroupMember` | ✔ | ManagementGroupId, GroupName, SubscriptionId | — |
| `quota.GroupQuota` | ✔ | ManagementGroupId, GroupName, Region, FamilyId | GroupLimit, AvailableLimit (unallocated), AllocatedTotal, GroupUsage |
| `quota.GroupAllocation` | ✔ | ManagementGroupId, GroupName, SubscriptionId, Region, FamilyId | QuotaAllocated (from `allocatedToSubscriptions`; `shareableQuota` is a phase-2 addition) |
| `zone.FamilyZoneAccess` | ✔ | SubscriptionId, Region, FamilyId | ZoneStatus, SkusTotal, SkusRegionOpen, OpenZonesLogical, OpenZonesPhysical, RestrictedZonesLogical, OfferedZonesLogical, ReasonCodes |
| `zone.SkuRestriction` | ✔ | SubscriptionId, Region, Sku | FamilyId, RestrictionType (`Location`/`Zone`), ZonesLogical, ZonesPhysical, ReasonCode (restricted SKUs only) |
| `zone.ZoneMapping` | ✔ | SubscriptionId, Region, ZoneLogical | ZonePhysical |
| `access.Grant` | ✔ | GrantId | PrincipalObjectId, PrincipalType (`Group`/`User`), TargetType (`ManagementGroup`/`QuotaGroup`/`Subscription`), TargetId, ChangedBy. The temporal history **is** the audit trail of who changed what, and when. |
| `sync.Run` | — | RunId, Stage | StartedUtc, EndedUtc, Status, SubsExpected, SubsCovered, KeysSeen, KeysChanged, Throttled, Errors |
| `sync.Coverage` | — | RunId, SubscriptionId, Stage | Status, Error, ObservedUtc |

**Design rules that keep history honest and small**
1. **Never touch an unchanged row.** A temporal table writes a history row for **every** `UPDATE`,
   even one that sets the same values. The sync's `MERGE` therefore updates only when a value
   actually differs (`WHEN MATCHED AND (s.Usage <> t.Usage OR s.Limit <> t.Limit …)`).
2. **No volatile columns in temporal tables.** "Last seen" timestamps would change every hour and
   flood history. Freshness lives in `sync.Run` / `sync.Coverage` instead.
3. **Deletes are real deletes.** When a key disappears, the row is `DELETE`d. SQL moves it to
   history with its end time, so "as of" queries before the delete still see it. A key missing from
   a **failed or partial** run is never deleted (§9).

**Example — explorer query, current or "as of" any past moment**

```sql
DECLARE @asOf datetime2(0) = COALESCE(@requestedAsOf, SYSUTCDATETIME());

SELECT q.SubscriptionId, s.Name, q.Region, f.Series, f.CpuManufacturer, f.Architecture,
       q.Usage, q.Limit, q.Limit - q.Usage AS Available,
       CAST(100.0 * q.Usage / NULLIF(q.Limit, 0) AS decimal(5,1)) AS UtilPct,
       z.ZoneStatus, z.OpenZonesLogical, z.OpenZonesPhysical
FROM quota.SubscriptionQuota FOR SYSTEM_TIME AS OF @asOf AS q
JOIN dim.Subscription        FOR SYSTEM_TIME AS OF @asOf AS s ON s.SubscriptionId = q.SubscriptionId
JOIN dim.VmFamily            FOR SYSTEM_TIME AS OF @asOf AS f ON f.FamilyId = q.FamilyId
LEFT JOIN zone.FamilyZoneAccess FOR SYSTEM_TIME AS OF @asOf AS z
       ON z.SubscriptionId = q.SubscriptionId AND z.Region = q.Region AND z.FamilyId = q.FamilyId
WHERE q.Kind = 'Family'
  AND q.SubscriptionId IN (SELECT SubscriptionId FROM access.VisibleSubscriptions(@userObjectId, @groupIdsJson))
  AND (@regionsJson IS NULL OR q.Region IN (SELECT value FROM OPENJSON(@regionsJson)))
  AND (@cpuJson     IS NULL OR f.CpuManufacturer IN (SELECT value FROM OPENJSON(@cpuJson)))
ORDER BY UtilPct DESC;
```

Every filter value is a **bound parameter** (JSON arrays read with `OPENJSON`) or an allow-listed
enum, never concatenated into SQL. `access.VisibleSubscriptions` applies the §4.3 grants against
**current** management-group paths. Admins bypass it.

## 9. Sync design

```
every AQR_SYNC_INTERVAL (default 1h) — single writer via sp_getapplock (session lock on the AQR database)
  1. Inventory    ARG ResourceContainers → dim.Subscription (names, MG path, state)
  2. SubQuota     ARG QuotaResources (paged, skipToken) → quota.SubscriptionQuota
                  gap check: subs/regions missing → Compute Usages API (S2)
  3. Groups       every 4h: per MG → groups → members → limits/usages per region
                  → quota.QuotaGroup, QuotaGroupMember, GroupQuota, GroupAllocation
  4. Zones/SKUs   daily: per sub → locations (zone map) → skus?$filter=location for each region where the
                  sub holds family quota (limit or usage > 0); access rows only for those families
                  → zone.ZoneMapping, zone.FamilyZoneAccess, zone.SkuRestriction (restricted SKUs only)
  5. Catalog      on startup: catalog/vm-families.json ⨝ observed families → dim.VmFamily
  6. Record       sync.Run + sync.Coverage per stage (+ App Insights metrics)
```

### 9.1 Write-on-change

Each stage bulk-loads what it just read into a session temp table (`SqlBulkCopy`), then runs one
`MERGE` per target inside a transaction:
- **insert** new keys;
- **update only rows whose values differ** (rule 1 in §8). Unchanged keys aren't touched, so they
  create no history;
- **delete** keys that are gone, but only for subscriptions the stage **fully covered** in this run.

SQL Server's temporal versioning turns those updates and deletes into history automatically. The
database itself holds the "last written values", so no separate state store is needed.
`sync.Run.KeysSeen` / `KeysChanged` are recorded every run, so "nothing changed" and "sync didn't
run" never look the same.

### 9.2 Region scope — measured, not guessed

We can't know in advance which regions matter, and filtering by "regions with usage" would hide
the quota sitting in regions you haven't deployed to yet. So:

- **Collect every region the APIs return.** No region filter in v1.
- **Write-on-change makes that cheap.** An unused region has a static limit and zero usage. It's
  written once at baseline and creates no history until something changes. Growth is driven by
  **how often quota actually moves**, not by how many regions exist.
- **Measure.** `sync.Run` records keys seen vs. changed per run, so after a week the history growth
  rate is a measured number (§5.3).
- **Pre-deployment sizing (optional):** this read-only Resource Graph query, run by someone with
  Reader in the target tenant, gives the baseline key count before anything is deployed:

```kusto
QuotaResources
| where type =~ "microsoft.compute/locations/usages"
| mv-expand q = properties.value
| extend name = tostring(q.name.value), usage = tolong(q.currentValue), limit = tolong(q.limit)
| where name =~ "cores" or name endswith "Family"
| summarize Rows = count(), Subs = dcount(subscriptionId), Regions = dcount(location),
            RowsWithUsage = countif(usage > 0)
```

`Rows` is the baseline row count. `RowsWithUsage` approximates the keys that can change hourly;
the rest only change on quota increases or allocations. An `AQR_REGIONS` allow-list exists as an
**optional** policy override. It's off by default.

### 9.3 Operational rules

- **Throttling:** ARG and ARM throttle per principal. The sync uses bounded concurrency
  (`AQR_MAX_PARALLEL`, default 4), honours `Retry-After`, and backs off on `429`. ARG queries batch
  subscriptions in chunks.
- **Completeness is measured, not assumed:** `SubsCovered / SubsExpected` per stage is recorded and
  shown on the Admin page. A stage that covers less than 100 % is a **warning**, not a silent success.
  Subscriptions without `Microsoft.Quota` registration, and MGs the identity can't read, are listed
  by name.
- **Partial failure never deletes.** A subscription or region missing from a failed or partial run
  is "not observed", not "deleted". The UI shows each stage's own "as of" time.
- **Raw archive:** each API response page is gzipped to blob (`raw/{date}/{stage}/…`), with a
  lifecycle policy, for audit and replay.
- **Manual refresh:** Admins can trigger a cycle (`POST /api/v1/sync`). It takes the same lock.

*Alternative:* move the sync into an Azure Functions (Flex Consumption) timer app when you want
separate scaling or deploys. The code stays the same (shared class library); `azure.yaml` gains a
second service.

## 10. API (phase 1.5)

Minimal APIs under `/api/v1`, documented with built-in ASP.NET Core OpenAPI (`/openapi/v1.json`) plus
a Swagger/Scalar UI at `/api/docs`. Callers authenticate with Entra bearer tokens (Easy Auth validates
them). Service principals need the `AQR.Reader` app role.

| Endpoint | Purpose |
|---|---|
| `GET /api/v1/quota/subscriptions` | Explorer query (all §7 filters as query params; paged; `format=csv`) |
| `GET /api/v1/quota/groups` / `{mg}/{group}` | Group pool, allocations, usage |
| `GET /api/v1/zones` | Family/SKU zonal access for sub(s)/region(s) |
| `GET /api/v1/families` | Catalog with attributes and sources |
| `GET /api/v1/trends` | Time series for a filter |
| `GET /api/v1/sync/runs` · `POST /api/v1/sync` (Admin) | Sync health and manual trigger |

The UI calls the same endpoints, so the API is exercised from day one.

## 11. Deployment (`azd up`)

```
aqr/
├─ azure.yaml                 # service "web" → src/Aqr.Web (appservice, dotnet); hooks
├─ infra/
│  ├─ main.bicep              # subscription-scope: RG + modules
│  ├─ main.parameters.json    # maps AZD env vars
│  └─ modules/entra.bicep    # app registration, app roles, MI federated credential, SQL admin group
│                            # (Microsoft Graph Bicep extension; no AVM exists for Graph)
│     everything else is Azure Verified Modules (br/public:avm/res/...), see infra/README.md
├─ src/ Aqr.Web (UI + API + SyncWorker host) · Aqr.Core (ARM clients, model, EF Core + migrations, sync, reports)
├─ tests/Aqr.Tests           # unit, SQL-translation, web/auth, and SQL integration tests (Azure SQL Database container)
├─ scripts/dev-sql.ps1       # local Azure SQL Database container, see docs/LOCAL_DEV.md
├─ catalog/vm-families.json
└─ docs/
```

`azd env` settings:

| Setting | Default | Purpose |
|---|---|---|
| `AZURE_LOCATION` | — | Deployment region |
| `AQR_MANAGEMENT_GROUP_IDS` | — | Management groups to report on |
| `AQR_SQL_NETWORK` | `nsp` | `nsp` (SQL NSP preview) or `privateEndpoint` (GA), §5.2 |
| `AQR_NSP_MODE` | `Learning` | NSP Transition (Learning) mode, then `Enforced` |
| `AQR_SQL_SKU` | `S1` | Database tier, §5.3 |
| `AQR_HISTORY_RETENTION` | `1 YEAR` | Temporal history retention, §5.4 |
| `AQR_SQL_PITR_DAYS` | `7` | Point-in-time restore window |
| `AQR_SQL_ADMIN_OBJECT_IDS` | — | Extra object IDs added to the generated SQL admin group |
| `AQR_GRANT_GRAPH_GROUPMEMBER_READ` | `false` | Grant the UAMI Graph `GroupMember.Read.All` for group-overage lookups (needs a privileged admin) |
| `AQR_ASSIGN_MG_RBAC` | `false` | Let the postprovision hook run the MG Reader assignments |
| `AQR_APP_SERVICE_SKU` | `P0v3` | App Service plan SKU |
| `AQR_ADMIN_GROUP_ID` / `AQR_READER_GROUP_ID` | — | App role assignments |
| `AQR_SYNC_INTERVAL` | `1h` | Sync cadence |
| `AQR_REGIONS` | off | Optional region allow-list |

Hooks:
- `preprovision` checks the deployer's az context against `AZURE_SUBSCRIPTION_ID` and fails on a
  mismatch.
- `postprovision`: prints the Reader role-assignment command for each management group (runs them when
  `AQR_ASSIGN_MG_RBAC=true` and the deployer has rights there), plus the `Microsoft.Quota` registration
  reminder. The app runs its first sync on startup.

The Entra app registration and FIC are created with the Microsoft Graph Bicep extension. If the
deployer lacks Graph permissions, a hook script does it instead.

## 12. Roadmap

| Phase | Content |
|---|---|
| **1 — VM quota MVP** | Sync S1, S3–S7 (family + Total Regional vCPUs); Overview, Explorer, Quota Groups, Zones, Trends (1-year history, "as of"), Admin and access-mapping pages; Entra auth; Azure SQL temporal store behind NSP; `azd up` |
| 1.5 — API | OpenAPI/Swagger, CSV export, service-principal access |
| 2 — Insight | Spot/low-priority and Dedicated Host quota; forecast to limit; threshold alerts ("family > N %", "quota but zone-blocked") via Azure Monitor from sync metrics; group-rebalance suggestions (read-only); group request history |
| 3 — Beyond compute | `Microsoft.Quota/usages` providers (Network, MachineLearningServices, HPC Cache, Storage, Purview), service-specific usages APIs where the Quota RP doesn't cover them |
| 4 — Actions (optional) | Quota increase / group allocate via Quota API with an approval step; needs the Quota Request Operator roles |

## 13. Decisions and open questions

**Decided (2026-10-07)**

| # | Topic | Decision |
|---|---|---|
| D1 | Tenants | Single tenant for now |
| D2 | Region scope | All regions the APIs return; write-on-change keeps it cheap; measure before tuning (§9.2) |
| D3 | History | **At least 1 year**: temporal `HISTORY_RETENTION_PERIOD = 1 YEAR`, raisable (§5.4) |
| D4 | MVP quota types | VM family + Total Regional vCPUs only. Spot/low-priority and Dedicated Host → phase 2 |
| D5 | Front door | Public App Service + Entra ID sign-in is acceptable for now |
| D6 | Who sees what | **Admin-maintained mapping (option B below).** Report visibility is deliberately separate from Azure RBAC (§4.3) |
| D7 | Data store | **Azure SQL Database with system-versioned temporal tables** (§5). Log Analytics holds app telemetry only |
| D8 | Services | **Azure-native services only.** No third-party or open-source engines |
| D9 | SQL network | **Network Security Perimeter** for production (`AQR_SQL_NETWORK=nsp`), accepting SQL NSP public-preview terms. `privateEndpoint` stays available as the GA fallback (§5.2) |

**Open design questions:** none.

**Implementation status and open items (2026-10-07)**

Phase 1 code, infra and tests are committed (69 tests pass). **Nothing has been deployed yet.**

| # | Item | Owner |
|---|---|---|
| O1 | ~~Sign up for the Azure SQL Database container Private Preview~~ (done 2026-10-07, **awaiting approval**). Then install WSL (`wsl --install`, admin + reboot), sign in to the registry, run `scripts/dev-sql.ps1` | Chris |
| O2 | Run the 4 SQL integration tests against the container (`AQR_TEST_SQL`). Validate once against cloud Azure SQL too (the container has known restriction-enforcement gaps) | next session |
| O3 | Choose the target subscription and management groups. Confirm Entra rights to create the app registration and groups. Get someone with management-group rights to assign Reader | Chris |
| O4 | First `azd up` to a test subscription: Graph extension, secretless Easy Auth sign-in, SQL access through the NSP (Learning → Enforced), first live sync | next session |
| O5 | Check the live API shapes against real data (QuotaResources `mv-expand` limit, quota group paging, SKU zone fields). Run the §9.2 sizing query and pick the SQL tier from measured DTU | next session |
| O6 | Confirm `GroupMember.Read.All` is enough for the Graph group-overage lookup (`transitiveMemberOf`) | next session |
| O7 | Not built yet: Swagger UI page (the OpenAPI JSON exists), `shareableQuota`, forecast to limit, alerts, spot / Dedicated Host (phase 2), non-compute providers (phase 3) | backlog |

**Resolved — row-level scoping options considered (D6 chose B)**

| Option | How it works | Pros | Cons |
|---|---|---|---|
| A. Everyone sees everything | Anyone with `AQR.Reader` sees every subscription AQR syncs | Simplest | No separation: an app team member sees other teams' quota and usage |
| **B. Admin-maintained mapping** | Admins map Entra groups → management groups / quota groups / subscriptions | Report visibility independent of Azure RBAC | Someone maintains it. Mitigated by mapping to MGs and quota groups, which resolve their subscriptions at query time |
| C. Mirror Azure RBAC | Show only subscriptions the user can already read in Azure | No mapping to maintain | Couples reporting to Azure access, which is what we want to avoid; users need Azure access to see reports |

## Appendix A — store cost evidence that informed D7 (2026-10-07)

**Unit prices.** Azure Retail Prices API, `eastus2`, list price, pulled live 2026-10-07:

| Store | Meter | Price |
|---|---|---|
| Log Analytics (Analytics plan) | Data ingestion | $2.76 / GB (first 5 GB/month per billing account free, shared with all other workspaces in that billing account) |
| | Analytics retention beyond 31 days | $0.12 / GB-month |
| | Long-term retention ("Data Archive") | $0.02 / GB-month; queried via search jobs at $0.005 / GB scanned |
| Blob storage (GPv2, LRS) | Hot data stored | $0.0184 / GB-month; writes $0.05 / 10K |
| Table storage (LRS) | Data stored | $0.045 / GB-month; any operation $0.00036 / 10K |
| Azure SQL Database | Standard S0 / S1 / S3 | $0.4839 / $0.9677 / $4.8387 per day (250 GB included) |
| | General Purpose serverless Gen5 compute | $0.521758 / vCore-hour; storage $0.115 / GB-month |

**Volume model** (assumptions, see §9.2): 640,000 keys, ~350 bytes/row as ingested log records, hourly sync.

| Write strategy | Ingested / month | Stored after 1 year | Log Analytics / month at year end | Blob hot / month | Table / month |
|---|---|---|---|---|---|
| Write-on-change (1 % of keys change per hour) | 1.6 GB | ~20 GB | ~$4.45 ingest + ~$2.35 retention | ~$0.36 | ~$0.88 |
| Hourly full snapshot | 161 GB | ~1.9 TB | ~$445 ingest + ~$232 retention | ~$36 | ~$87 |

Conclusions:
- With write-on-change, storage cost doesn't decide between the options; every one is a few
  dollars a month. Function does: system of record, native history and server-side filtering.
- Log Analytics' cost is highly sensitive to the write strategy. SQL is a flat tier price up to its
  included storage.
