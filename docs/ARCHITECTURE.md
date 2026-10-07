# Azure Quota Reporting (AQR) — Architecture Design

> **Status:** Draft v0.1 · 2026-10-07 · initial design for review
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
  generation, features, lifecycle, utilization thresholds, spot vs on-demand.
- History/trends from scheduled snapshots.
- Entra ID sign-in, role-based access.
- Data store behind a **Network Security Perimeter (NSP)**.
- Everything API-driven (no portal scraping, no CSV uploads). Deploy with **`azd up`**.

**Non-goals (v1)**
- Requesting/transferring quota (read-only first; write actions are a later phase).
- Non-compute services (Network, ML, HPC Cache, Storage, Purview via `Microsoft.Quota/usages`) —
  planned for phase 3 (§12).
- Real-time capacity guarantees. Zonal *access* (subscription restriction) is reportable; physical
  datacenter *capacity* is not exposed by any API, and AQR will not imply it is.

## 3. Data sources (all API-based)

Every source below is a documented, GA Azure Resource Manager API. AQR calls them with a managed
identity; nothing is scraped.

| # | Source | What AQR takes from it | Call shape | Cadence (default) |
|---|---|---|---|---|
| S1 | **Azure Resource Graph — `QuotaResources`** table, type `microsoft.compute/locations/usages` | Per-sub, per-region usage and limit for every compute quota (`cores`, `lowPriorityCores`, `virtualMachines`, every `standard*Family`) | One paged KQL query across all in-scope subscriptions (`mv-expand properties.value`) | Hourly |
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

## 4. Recommended architecture

![architecture](images/architecture.png)
*(Source: [`docs/images/src/architecture.html`](images/src/architecture.html).)*

```
 Browser ──HTTPS──► App Service (Linux, .NET 10)  ◄── Easy Auth (Entra ID, app roles)
                     │  Razor Pages UI + /api/v1 (OpenAPI)
                     │  SyncWorker (BackgroundService, blob-lease leader election)
                     │
                     │ user-assigned managed identity (UAMI)
                     ├──► ARM: Resource Graph, Microsoft.Quota, Microsoft.Compute/skus, Subscriptions
                     │
                     ├──► Logs Ingestion API (DCE + DCR) ──► Log Analytics workspace  ┐
                     ├──► Log Analytics Query API (KQL)  ◄────────────────────────── │  Network Security
                     └──► Storage account (blob lease, raw API archive, sync state)  ┘  Perimeter (Enforced)
                                                                                         inbound rule: AQR subscription (MI)
 App Insights (workspace-based) ◄── telemetry + sync-health metrics
```

### 4.1 Components

| Component | Choice | Why |
|---|---|---|
| Web + API | **Azure App Service (Linux), .NET 10, Razor Pages + minimal APIs** | Same stack as `ghcp-credit-visibility-azure`, so the UI style and auth patterns carry over. One deployable for `azd`. |
| Auth | **App Service Easy Auth → Entra ID**, app roles `AQR.Reader`, `AQR.Admin` | No auth code in the app. **Secretless**: Easy Auth uses the UAMI as a federated credential (`OVERRIDE_USE_MI_FIC_ASSERTION_CLIENTID`) per [Learn](https://learn.microsoft.com/azure/app-service/configure-authentication-provider-aad#use-a-managed-identity-instead-of-a-secret), so there's no client secret and no Key Vault in v1. |
| Sync | **In-process `BackgroundService`** with a **blob lease** so only one instance runs a cycle | Fewest moving parts. If the web tier scales out, the lease still guarantees a single writer. Alternative in §9. |
| Data store | **Log Analytics workspace (custom `_CL` tables, Analytics plan)** | See §5. NSP **GA**, KQL is the filter engine, history and retention are built in. |
| Ingestion | **Logs Ingestion API** via DCE + DCR, authenticated by UAMI | API-based, typed schema in the DCR, no shared keys. |
| Aux storage | **Storage account (StorageV2)**, shared-key disabled | Blob lease for leader election, last-good raw API payloads for audit/replay, sync watermarks. NSP **GA**. |
| Observability | Workspace-based Application Insights | Sync-health metrics: snapshot age, rows written, subscriptions covered vs expected, throttling. |
| IaC | **Bicep + `azure.yaml`** → `azd up` | azd-native. `azd provision` creates everything, `azd deploy` publishes the app. |

### 4.2 Identity and RBAC (least privilege, read-only)

| Principal | Role | Scope | Purpose |
|---|---|---|---|
| AQR UAMI | **Reader** | Each in-scope **management group** (or the tenant root MG) | ARG `QuotaResources`/`ResourceContainers`, Resource SKUs, Subscriptions locations, and `Microsoft.Quota/groupQuotas/*/read` (Reader includes `*/read`) |
| AQR UAMI | **Monitoring Metrics Publisher** | DCR | Logs Ingestion API |
| AQR UAMI | **Log Analytics Reader** | Workspace | KQL queries for the UI/API |
| AQR UAMI | **Storage Blob Data Contributor** | Storage account | Lease + archive |
| Users | App role `AQR.Reader` / `AQR.Admin` (assigned to Entra groups) | Enterprise app | UI/API access |

Management-group role assignments live outside the app's resource group. `azd up` can only create
them if the deployer has rights at that MG, so the Bicep makes them conditional
(`AQR_MANAGEMENT_GROUP_IDS`). Otherwise a `postprovision` hook prints the exact `az role assignment
create` commands for an MG admin to run. Prerequisite from the Quota docs: `Microsoft.Quota` and
`Microsoft.Compute` registered on member subscriptions; the sync reports any that aren't.

No write roles (Quota Request Operator, GroupQuota Request Operator) in v1.

## 5. Data store decision

You asked for the easiest and fastest store **with an NSP in front of it**. NSP support decides
most of this (Learn, *Onboarded private link resources*,
<https://learn.microsoft.com/azure/private-link/network-security-perimeter-concepts#onboarded-private-link-resources>,
checked 2026-10-07):

| Option | NSP status | Filtering / history | Effort | Verdict |
|---|---|---|---|---|
| **Log Analytics (Azure Monitor)** | **GA** | KQL: arbitrary filters, joins, `arg_max` latest-state, time series, `make-series`; retention per table | Low: no schema migrations, the DCR defines columns | **Recommended** |
| Storage Tables / Parquet in Blob + in-app DuckDB | **GA** | Filtering is in the app; you build the query layer | Medium–high | Fallback if ingestion cost matters more than build time |
| Azure SQL Database | **Public preview** | Excellent (SQL, EF Core, indexes) | Medium (migrations) | Revisit once SQL NSP is GA |
| Cosmos DB | **Public preview** | Good for point reads, weaker for ad-hoc aggregation | Medium | Not a fit |

**Why Log Analytics is the fastest path**
- The data is **append-only snapshots**, which is exactly what LA is built for. "Current state" is
  `arg_max(TimeGenerated, *) by key` over the last cycle. "Trend" is the same query over N days.
- Every filter in §7 becomes a `where` clause; every chart is a `summarize`. No ORM, no migrations.
- The same tables power **Azure Workbooks** and **log alerts** for free (for example, "family >
  85 % in any region" → action group).
- NSP is **GA** for the workspace, DCE and alert rules.

**Trade-offs to accept (and how they're handled)**
- *Ingestion latency* (minutes): the UI shows "as of" from `AQRSyncRun_CL`, not wall-clock.
- *Query latency* (~1–3 s): the app caches each query result keyed by `SnapshotId`. Data only changes
  once per sync cycle, so cache hit rates are high.
- *Cost scales with GB ingested.* Volume controls: (a) slim typed columns; (b) region scope
  (`AQR_REGIONS`, default = regions with any usage or non-zero group quota); (c) SKU availability
  stored at **family** grain plus **only restricted SKU rows** (§6.3); (d) per-table retention. The
  doc deliberately doesn't quote a dollar figure. Size it with the Azure Pricing Calculator against
  the row counts the first sync logs in `AQRSyncRun_CL`.
- *Query API limits* (rows/size per query): every UI query aggregates server-side and pages.

**NSP configuration**
- One perimeter, one profile, associating: Log Analytics workspace, DCE, Storage account (and
  scheduled query rules and action groups if alerts are enabled).
- Access mode: start in **Transition (Learning)** for the first `azd up` so you can validate the access
  logs, then flip to **Enforced** (`AQR_NSP_MODE=Enforced`).
- Inbound rule: **subscription-based**, allowing managed identities from the AQR subscription
  (documented NSP behavior: "allows inbound access authenticated using any managed identity from
  the subscription"). No IP rules. Optional admin IP rule for break-glass portal queries.
- NSP access logs are sent to the same workspace for auditing.

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
| Quota type | Family · Total regional vCPUs (`cores`) · Spot/low-priority (`lowPriorityCores`) · VM count · other | Quota name |
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

## 8. Data model (Log Analytics custom tables)

All tables carry `TimeGenerated`, `SnapshotId` (guid per sync cycle), `TenantId`.

| Table | Key | Main columns | Volume control |
|---|---|---|---|
| `AQRSubQuota_CL` | Sub, Location, QuotaName | SubscriptionName, MgPath, QuotaLocalizedName, QuotaKind (`Family`/`RegionalTotal`/`LowPriority`/`Other`), Family, Usage, Limit, Unit, QuotaGroup | Regions in scope; skip rows with Limit = 0 and Usage = 0 except `cores` |
| `AQRGroupQuota_CL` | MgId, Group, Location, Family | GroupLimit, AvailableLimit (unallocated), AllocatedTotal, GroupUsage, MemberCount | — |
| `AQRGroupAlloc_CL` | MgId, Group, Sub, Location, Family | QuotaAllocated, ShareableQuota | — |
| `AQRFamilyZone_CL` | Sub, Location, Family | ZoneStatus, SkusTotal, SkusRegionOpen, OpenZonesLogical, OpenZonesPhysical, RestrictedZonesLogical, OfferedZonesLogical, ReasonCodes | Daily; regions in scope |
| `AQRSkuRestriction_CL` | Sub, Location, Sku | Family, RestrictionType (`Location`/`Zone`), ZonesLogical, ZonesPhysical, ReasonCode | Restricted SKUs only |
| `AQRZoneMap_CL` | Sub, Location, ZoneLogical | ZonePhysical | Daily |
| `AQRFamily_CL` | Family | Category, CpuManufacturer, Architecture, AcceleratorType, AcceleratorVendor, AcceleratorModel, Generation, Features, Lifecycle, MinVcpu, MaxVcpu, Source | Upsert on change |
| `AQRSyncRun_CL` | SnapshotId | Stage, StartedAt, EndedAt, Status, SubsExpected, SubsCovered, RowsWritten, Throttled, Errors | Per stage |

Example — the explorer's base query (current state, joined):

```kusto
let snap = toscalar(AQRSyncRun_CL | where Stage == "SubQuota" and Status == "Succeeded" | top 1 by EndedAt | project SnapshotId);
AQRSubQuota_CL
| where SnapshotId == snap and QuotaKind == "Family"
| join kind=leftouter (AQRFamily_CL | summarize arg_max(TimeGenerated, *) by Family) on Family
| join kind=leftouter (AQRFamilyZone_CL | summarize arg_max(TimeGenerated, *) by SubscriptionId, Location, Family) on SubscriptionId, Location, Family
| where Location in ({regions}) and CpuManufacturer in ({vendors}) and ZoneStatus in ({zoneStatus})
| extend Available = Limit - Usage, UtilPct = iff(Limit > 0, 100.0 * Usage / Limit, real(null))
| order by UtilPct desc
```

User-supplied filter values are passed as **query parameters / allow-listed enums**, never
concatenated into KQL.

## 9. Sync design

```
every AQR_SYNC_INTERVAL (default 1h) — leader holds blob lease
  1. Inventory    ARG ResourceContainers → subs in scope (MG list), names, MG path
  2. SubQuota     ARG QuotaResources (paged, skipToken)            → AQRSubQuota_CL
                  gap check: subs/regions missing → Compute Usages API (S2)
  3. Groups       every 4h: per MG → groups → members → limits/usages per region → AQRGroup*_CL
  4. Zones/SKUs   daily: per sub → locations (zone map) → skus?$filter=location per region in scope
                  → AQRZoneMap_CL, AQRFamilyZone_CL, AQRSkuRestriction_CL
  5. Catalog      on startup/deploy: catalog/vm-families.json ⨝ observed families → AQRFamily_CL
  6. Record       AQRSyncRun_CL per stage (+ App Insights metrics)
```

- **Throttling:** ARG and ARM throttle per principal. The sync uses bounded concurrency
  (`AQR_MAX_PARALLEL`, default 4), honours `Retry-After`, and backs off on `429`. ARG queries batch
  subscriptions in chunks.
- **Completeness is measured, not assumed:** `SubsCovered / SubsExpected` per stage is recorded and
  shown on the Admin page. A stage that covers less than 100 % is a **warning**, not a silent success.
  Subscriptions without `Microsoft.Quota` registration, and MGs the identity can't read, are listed
  by name.
- **Partial failure** never overwrites last-good. The UI reads the latest **succeeded** snapshot per
  stage and shows each stage's own "as of" time.
- **Raw archive:** each API response page is gzipped to blob (`raw/{date}/{stage}/…`), with a retention
  policy, for audit and replay.
- **Manual refresh:** Admins can trigger a cycle (`POST /api/v1/sync`), still lease-guarded.

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
├─ azure.yaml                 # service "web" → src/Aqr.Web (appservice, dotnet)
├─ infra/
│  ├─ main.bicep              # subscription-scope: RG + modules
│  ├─ main.parameters.json    # maps AZD env vars
│  └─ modules/ appservice.bicep, identity.bicep, loganalytics.bicep (tables+DCR+DCE),
│              storage.bicep, nsp.bicep, appinsights.bicep, entra-app.bicep (Microsoft.Graph Bicep ext),
│              rbac-mg.bicep (conditional)
├─ src/ Aqr.Web (UI+API+SyncWorker) · Aqr.Core (clients, model, KQL) · Aqr.Tests
├─ catalog/vm-families.json
└─ docs/
```

`azd env` settings: `AZURE_LOCATION`, `AQR_MANAGEMENT_GROUP_IDS`, `AQR_REGIONS` (optional),
`AQR_SYNC_INTERVAL`, `AQR_NSP_MODE` (`Learning`/`Enforced`), `AQR_ADMIN_GROUP_ID`,
`AQR_READER_GROUP_ID`.
Hooks: `preprovision` checks the deployer's az context against `AZURE_SUBSCRIPTION_ID` and fails on
mismatch. `postprovision` prints MG RBAC commands if they weren't applied and triggers the first sync.
The Entra app registration and FIC are created with the Microsoft Graph Bicep extension. If the
deployer lacks Graph permissions, a hook script does it instead.

## 12. Roadmap

| Phase | Content |
|---|---|
| **1 — VM quota MVP** | Sync S1, S3–S7; Overview, Explorer, Quota Groups, Zones, Admin pages; Entra auth; NSP; `azd up` |
| 1.5 — API | OpenAPI/Swagger, CSV export, service-principal access |
| 2 — Insight | Trends/forecast to limit, Workbooks + log alerts (threshold, "quota but zone-blocked"), group-rebalance suggestions (read-only), group request history |
| 3 — Beyond compute | `Microsoft.Quota/usages` providers (Network, MachineLearningServices, HPC Cache, Storage, Purview), service-specific usages APIs where the Quota RP doesn't cover them |
| 4 — Actions (optional) | Quota increase / group allocate via Quota API with an approval step; needs the Quota Request Operator roles |

## 13. Open questions

1. **Row-level scoping:** should a reader see all subscriptions, or only those under MGs or quota
   groups mapped to their Entra group (the GHCP-visibility pattern)? v1 assumes app-role-only.
2. **Tenant count:** single tenant only, or multiple tenants (Lighthouse / multi-tenant app)? v1
   assumes **single tenant**.
3. **Region scope default:** all regions, or only regions with usage or quota allocations? Affects
   ingestion volume.
4. **History retention:** 90 days interactive is assumed. Is longer needed (archive tier)?
5. **Spot/low-priority and Dedicated Host quota:** include in v1 or phase 2?
6. **App front door:** is a public App Service with Entra auth acceptable, or is a private endpoint
   plus internal access required? NSP doesn't cover App Service.
