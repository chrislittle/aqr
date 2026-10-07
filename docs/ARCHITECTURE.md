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
- *Cost scales with GB ingested and retained.* The main control is **write-on-change** (§9.1): a
  quota row is written only when its usage or limit changes, so unused regions cost almost nothing
  and **no region filter is needed** (§9.2). Also: slim typed columns, and SKU availability stored
  at **family** grain plus only restricted SKU rows (§6.3). The doc deliberately doesn't quote a
  dollar figure. Size it with the Azure Pricing Calculator against the measured row counts (§9.2).

**Retention: 1 year minimum.** Analytics-plan tables can be kept fully queryable for up to 730 days
and in long-term retention for up to 12 years
([Learn: data retention](https://learn.microsoft.com/azure/azure-monitor/logs/data-retention-configure)).
AQR sets **analytics retention = 365 days** on every `AQR*_CL` table (`AQR_RETENTION_DAYS`, default
365, max 730). Optional total retention beyond that (`AQR_TOTAL_RETENTION_DAYS`) uses long-term
retention, which is cheaper but needs search jobs to query. Only the first 31 days of analytics
retention are included in the ingestion price; the rest is billed per GB per month.
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

### 5.1 Store decision reopened — cost evidence (2026-10-07)

> The Log Analytics recommendation above optimized for **build speed** (KQL does the filtering)
> and NSP-GA status. It was **not priced**, and a log analytics service isn't a natural **system of
> record**. Rows can't be corrected in place, and data lifetime is tied to a retention setting.
> This section is the evidence for re-deciding.

**Unit prices.** Azure Retail Prices API, `eastus2`, list price, pulled live 2026-10-07:

| Store | Meter | Price |
|---|---|---|
| Log Analytics (Analytics plan) | Data ingestion | $2.76 / GB (first 5 GB/month per billing account free, shared with all other workspaces in that billing account) |
| | Analytics retention beyond 31 days | $0.12 / GB-month |
| | Long-term retention ("Data Archive") | $0.02 / GB-month; queried via search jobs at $0.005 / GB scanned |
| Blob storage (GPv2, LRS) | Hot data stored | $0.0184 / GB-month; writes $0.05 / 10K |
| | Cool data stored | $0.01 / GB-month; writes $0.10 / 10K |
| Table storage (LRS) | Data stored | $0.045 / GB-month; any operation $0.00036 / 10K |
| Azure SQL Database | Basic (5 DTU, 2 GB max) | $0.161 / day |
| | Standard S0 (10 DTU, 250 GB included) | $0.4839 / day |

**Volume model.** Every input here is an assumption until the sizing query in §9.2 is run against a
real tenant: 200 subscriptions × 40 regions × 80 quota rows = 640,000 keys, ~350 bytes/row,
hourly sync.

| Write strategy | Ingested / month | Stored after 1 year | Log Analytics / month at year end | Blob hot / month | Table / month |
|---|---|---|---|---|---|
| Write-on-change (1 % of keys change per hour) | 1.6 GB | ~20 GB | ~$4.45 ingest + ~$2.35 retention | ~$0.36 | ~$0.88 |
| Hourly full snapshot | 161 GB | ~1.9 TB | ~$445 ingest + ~$232 retention | ~$36 | ~$87 |

Azure SQL S0 is a flat ~$14.70/month (0.4839 × 30.4) for either row, up to 250 GB. Basic's 2 GB
cap is too small for a year of history.

**What the numbers say**
- Log Analytics charges **$2.76 per GB ingested**. Blob has no per-GB ingest charge, only
  per-operation writes, which are negligible when a sync writes a few files. Keeping data
  queryable costs **~6.5× blob hot** per GB-month ($0.12 vs $0.0184).
- With write-on-change, every option is a few dollars a month, so cost doesn't decide between
  them. Without write-on-change, Log Analytics is the most expensive by a wide margin.
- Log Analytics' cost is highly sensitive to the write strategy and the row-size assumption. Blob
  and SQL aren't.

**Candidates for the system of record**

| | Blob (Parquet) + embedded query engine (DuckDB) | Azure SQL Database (temporal tables) | Table storage | Log Analytics |
|---|---|---|---|---|
| NSP | **GA** (storage account) | **Public preview**; GA alternative is a private endpoint + VNet integration | **GA** | **GA** |
| History / "as of" | Change files plus compacted current state; `arg_max` / window queries | **Native**: system-versioned temporal tables, `FOR SYSTEM_TIME AS OF` | Manual; weak | `arg_max` over time |
| Rich filtering | SQL in-process over a small dataset (MBs–GBs) | SQL with indexes | Only PartitionKey/RowKey; everything else in app memory | KQL |
| Durability / control | Versioning, soft delete, immutability, lifecycle to cool | PITR backups, LTR backups | Basic | Retention-bound; purge only |
| Cost at modeled volume | Lowest (cents) | ~$15 / month flat | Low | Low with write-on-change, high without |
| Build effort | Medium: compaction job + query layer | Low–medium: EF Core migrations; same stack as `ghcp-credit-visibility-azure` | High for reporting | Low |

Mapping (D6) and the audit trail are small relational data. They fit SQL natively; with blob they'd
need a small JSON document per grant set.

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

## 8. Data model (Log Analytics custom tables)

All tables carry `TimeGenerated`, `SnapshotId` (the sync cycle that wrote the row), `TenantId`.
Fact tables are **write-on-change** (§9.1). A row means "from this time, this key had these values".
`IsDeleted = true` is a tombstone for a key that disappeared (subscription removed from scope, family
no longer returned).

| Table | Key | Main columns | Write rule |
|---|---|---|---|
| `AQRSubQuota_CL` | Sub, Location, QuotaName | SubscriptionName, MgPath, QuotaLocalizedName, QuotaKind (`Family`/`RegionalTotal`), Family, Usage, Limit, Unit, QuotaGroup, IsDeleted | On change of Usage, Limit, QuotaGroup or name |
| `AQRGroupQuota_CL` | MgId, Group, Location, Family | GroupLimit, AvailableLimit (unallocated), AllocatedTotal, GroupUsage, MemberCount, IsDeleted | On change |
| `AQRGroupAlloc_CL` | MgId, Group, Sub, Location, Family | QuotaAllocated, ShareableQuota, IsDeleted | On change |
| `AQRFamilyZone_CL` | Sub, Location, Family | ZoneStatus, SkusTotal, SkusRegionOpen, OpenZonesLogical, OpenZonesPhysical, RestrictedZonesLogical, OfferedZonesLogical, ReasonCodes, IsDeleted | On change |
| `AQRSkuRestriction_CL` | Sub, Location, Sku | Family, RestrictionType (`Location`/`Zone`), ZonesLogical, ZonesPhysical, ReasonCode, IsDeleted | Restricted SKUs only; on change; tombstone when lifted |
| `AQRZoneMap_CL` | Sub, Location, ZoneLogical | ZonePhysical | On change (effectively once per sub) |
| `AQRFamily_CL` | Family | Category, CpuManufacturer, Architecture, AcceleratorType, AcceleratorVendor, AcceleratorModel, Generation, Features, Lifecycle, MinVcpu, MaxVcpu, Source | On change |
| `AQRSyncRun_CL` | SnapshotId, Stage | StartedAt, EndedAt, Status, SubsExpected, SubsCovered, RowsSeen, RowsChanged, Throttled, Errors | Every run (this is the heartbeat) |

All tables: analytics retention = `AQR_RETENTION_DAYS` (default **365**).

Example — the explorer's base query (current state = latest row per key, tombstones dropped):

```kusto
let asOf = now();                      // or a user-picked point in time, for "as of" reports
AQRSubQuota_CL
| where TimeGenerated <= asOf and QuotaKind == "Family"
| summarize arg_max(TimeGenerated, *) by SubscriptionId, Location, QuotaName
| where not(IsDeleted)
| join kind=leftouter (AQRFamily_CL | summarize arg_max(TimeGenerated, *) by Family) on Family
| join kind=leftouter (AQRFamilyZone_CL | where TimeGenerated <= asOf
                       | summarize arg_max(TimeGenerated, *) by SubscriptionId, Location, Family
                       | where not(IsDeleted)) on SubscriptionId, Location, Family
| where Location in ({regions}) and CpuManufacturer in ({vendors}) and ZoneStatus in ({zoneStatus})
| extend Available = Limit - Usage, UtilPct = iff(Limit > 0, 100.0 * Usage / Limit, real(null))
| order by UtilPct desc
```

The same query with `asOf` set to a past date answers "what did quota look like on 1 March?" for
any date in the retention window. That's why write-on-change is a better fit than hourly full
snapshots for a 1-year history: point-in-time reads stay exact.

User-supplied filter values are passed as **query parameters / allow-listed enums**, never
concatenated into KQL.

## 9. Sync design

```
every AQR_SYNC_INTERVAL (default 1h) — leader holds blob lease
  1. Inventory    ARG ResourceContainers → subs in scope (MG list), names, MG path
  2. SubQuota     ARG QuotaResources (paged, skipToken)            → AQRSubQuota_CL
                  gap check: subs/regions missing → Compute Usages API (S2)
  3. Groups       every 4h: per MG → groups → members → limits/usages per region → AQRGroup*_CL
  4. Zones/SKUs   daily: per sub → locations (zone map) → skus?$filter=location per region
                  → AQRZoneMap_CL, AQRFamilyZone_CL, AQRSkuRestriction_CL
  5. Catalog      on startup/deploy: catalog/vm-families.json ⨝ observed families → AQRFamily_CL
  6. Record       AQRSyncRun_CL per stage (+ App Insights metrics)
```

### 9.1 Write-on-change

Each stage compares what it just read against the **last values written** per key. Those are held
in a compact state blob (`state/{stage}.json.gz`, one hash per key) in the NSP-protected storage
account. Only keys whose values changed are sent to the Logs Ingestion API. Keys that vanished get
a tombstone row. `AQRSyncRun_CL` records `RowsSeen` and `RowsChanged` every run, so "nothing
changed" and "sync didn't run" never look the same.

If the state blob is lost or corrupt, the next run treats every key as changed and writes one full
baseline. That's safe, just one larger ingestion.

### 9.2 Region scope — measured, not guessed

You asked how we'd know which regions matter. **We can't know in advance**, and the original
"only regions with usage" default was a guess. It would also hide exactly what capacity planning
needs: quota sitting in regions you haven't deployed to yet. So:

- **Collect every region the APIs return.** No region filter in v1. ARG `QuotaResources` returns
  the regions it has for each subscription, and AQR stores what's returned.
- **Write-on-change makes that cheap.** A region nobody uses has a static limit and zero usage. It's
  written **once** at baseline, then never again until something changes. Ingestion volume is
  driven by **how often quota actually moves**, not by how many regions exist.
- **The real numbers come from the first sync.** `AQRSyncRun_CL` records keys seen vs. changed per
  run. After a week, that gives a measured daily change rate to put into the Pricing Calculator.
- **Pre-deployment sizing (optional):** this read-only ARG query, run by someone with Reader in the
  target tenant, gives the baseline row count before anything is deployed:

```kusto
QuotaResources
| where type =~ "microsoft.compute/locations/usages"
| mv-expand q = properties.value
| extend name = tostring(q.name.value), usage = tolong(q.currentValue), limit = tolong(q.limit)
| where name =~ "cores" or name endswith "Family"
| summarize Rows = count(), Subs = dcount(subscriptionId), Regions = dcount(location),
            RowsWithUsage = countif(usage > 0)
```

`Rows` is the baseline write. `RowsWithUsage` approximates the keys that can change hourly; the
rest only change on quota increases or allocations. An `AQR_REGIONS` allow-list exists as an
**optional** override for tenants that want to exclude regions by policy. It's off by default.

- **Throttling:** ARG and ARM throttle per principal. The sync uses bounded concurrency
  (`AQR_MAX_PARALLEL`, default 4), honours `Retry-After`, and backs off on `429`. ARG queries batch
  subscriptions in chunks.
- **Completeness is measured, not assumed:** `SubsCovered / SubsExpected` per stage is recorded and
  shown on the Admin page. A stage that covers less than 100 % is a **warning**, not a silent success.
  Subscriptions without `Microsoft.Quota` registration, and MGs the identity can't read, are listed
  by name.
- **Partial failure** never writes tombstones. A subscription or region missing from a failed or
  partial run is treated as "not observed", not "deleted". The UI shows each stage's own "as of" time.
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

`azd env` settings: `AZURE_LOCATION`, `AQR_MANAGEMENT_GROUP_IDS`, `AQR_RETENTION_DAYS` (default 365),
`AQR_REGIONS` (optional allow-list, off by default), `AQR_SYNC_INTERVAL`, `AQR_NSP_MODE`
(`Learning`/`Enforced`), `AQR_ADMIN_GROUP_ID`, `AQR_READER_GROUP_ID`.
Hooks: `preprovision` checks the deployer's az context against `AZURE_SUBSCRIPTION_ID` and fails on
mismatch. `postprovision` prints MG RBAC commands if they weren't applied and triggers the first sync.
The Entra app registration and FIC are created with the Microsoft Graph Bicep extension. If the
deployer lacks Graph permissions, a hook script does it instead.

## 12. Roadmap

| Phase | Content |
|---|---|
| **1 — VM quota MVP** | Sync S1, S3–S7 (family + Total Regional vCPUs); Overview, Explorer, Quota Groups, Zones, Trends (1-year history), Admin pages; Entra auth; NSP; `azd up` |
| 1.5 — API | OpenAPI/Swagger, CSV export, service-principal access |
| 2 — Insight | Spot/low-priority and Dedicated Host quota; forecast to limit; Workbooks + log alerts (threshold, "quota but zone-blocked"); group-rebalance suggestions (read-only); group request history |
| 3 — Beyond compute | `Microsoft.Quota/usages` providers (Network, MachineLearningServices, HPC Cache, Storage, Purview), service-specific usages APIs where the Quota RP doesn't cover them |
| 4 — Actions (optional) | Quota increase / group allocate via Quota API with an approval step; needs the Quota Request Operator roles |

## 13. Decisions and open questions

**Decided (2026-10-07)**

| # | Topic | Decision |
|---|---|---|
| D1 | Tenants | Single tenant for now |
| D2 | Region scope | All regions the APIs return; write-on-change keeps it cheap; measure before tuning (§9.2) |
| D3 | History | **At least 1 year**: analytics retention 365 days on all AQR tables (§5) |
| D4 | MVP quota types | VM family + Total Regional vCPUs only. Spot/low-priority and Dedicated Host → phase 2 |
| D5 | Front door | Public App Service + Entra ID sign-in is acceptable for now |
| D6 | Who sees what | **Admin-maintained mapping (option B below).** Report visibility is deliberately separate from Azure RBAC: someone can see a report without having access to the subscription in Azure, and Azure access doesn't grant report visibility (§4.3) |

**Open**

1. **Data store (system of record).** Under review; see §5.1.

**Resolved — row-level scoping options considered (D6 chose B)**

1. **Who sees which subscriptions (row-level scoping)?** App roles only control *whether* someone
   can use AQR. This question is about *which data* they see once signed in. Options:

   | Option | How it works | Pros | Cons |
   |---|---|---|---|
   | **A. Everyone sees everything** | Anyone with `AQR.Reader` sees every subscription AQR syncs | Simplest; one query path; good for a central platform/capacity team | An app team member sees other teams' subscriptions, quota and usage |
   | **B. Admin-maintained mapping** | Admins map Entra groups → management groups / quota groups / subscriptions in an Admin page (the GHCP-visibility cost-center pattern) | Tailored views; no dependency on Azure RBAC | Someone has to maintain the mapping; it drifts from real access |
   | **C. Mirror Azure RBAC** | AQR shows only subscriptions the signed-in user can already read in Azure (checked with their own token via ARG, cached per user) | No mapping to maintain; never shows more than the user could see in the portal anyway | Needs a delegated Azure Resource Manager permission on the app registration; per-user lookup on sign-in |

   Quota data isn't secret in itself, but usage per family and region reveals what a team runs and
   where. **B was chosen** because it keeps reporting visibility independent of Azure RBAC.
   C would couple the two, which is exactly what we want to avoid. A gives no separation at all.
