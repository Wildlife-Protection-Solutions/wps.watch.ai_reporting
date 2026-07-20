# Data Perspective — AI Reports (v2, post-discovery rewrite)

**Owner:** Database / data architecture
**Supersedes:** [`docs/perspectives/data.md`](./data.md) (2026-05-05).
**Date:** 2026-05-14.
**Source of truth for the new findings cited here:** the local clones of [`wpswatch.reporting`](C:\Users\Jacob%20WPS\wkspaces\WPSWatch\wpswatch.reporting) and [`wps.watch.functions.scheduledreporting`](C:\Users\Jacob%20WPS\wkspaces\WPSWatch\wps.watch.functions.scheduledreporting), plus the original [`docs/ai-reports-exploration-notes.md`](../ai-reports-exploration-notes.md) and the [`docs/ai-reports-requirements-delta.md`](../ai-reports-requirements-delta.md).

This document is a clean rewrite — not a delta. The v1 doc made the right call given what it knew; what it knew was incomplete. Where the new evidence flips a position, I say so plainly at the relevant point.

---

## 0. Glossary

- **OLTP** — online transaction processing. The schema the API writes to. Normalized, optimized for inserts/updates, terrible for "count grouped by day".
- **Analytics DB** — a separate Azure SQL database, `wps-sql-analytics-prod`, sitting on the same logical Azure SQL Server as `wpswatch-prod`. Holds the `wpsWatch.*` reporting schema (15 views, 7 stored procs, 9 UDFs, 10 aggregation tables) and 29 external tables. Read-only for Looker today.
- **External table** — a SQL Server object that looks like a regular table but actually proxies reads to another database. Defined via `CREATE EXTERNAL TABLE … WITH (DATA_SOURCE = [MyElasticDBQueryDataSrc])`. Every reference to e.g. `dbo.Photo` *in the analytics DB* gets transparently rewritten to a query against `wpswatch-prod.dbo.Photo`. Example file: [`wpswatch.reporting/Tables/ExternalTables/dbo.Photo.Table.sql`](C:\Users\Jacob%20WPS\wkspaces\WPSWatch\wpswatch.reporting\Tables\ExternalTables\dbo.Photo.Table.sql).
- **Elastic DB query** — Microsoft's name for the cross-DB external-table mechanism above. The named data source `MyElasticDBQueryDataSrc` is the credentialled link to `wpswatch-prod`. Behaviorally: live data (sub-second freshness) but every cross-DB read hits the source database, so it isn't free.
- **Aggregation table (or "agg table")** — a normal SQL table in the `wpsWatch.*` schema that is truncated and re-populated by a stored procedure on a schedule. Examples: `wpsWatch.wpsWatchDeployedCameraCount`, `wpsWatch.wpsWatchCameraTrends`, `wpsWatch.wpsWatchSIMDataReport`. Cheap to read; freshness depends on when the populating proc last ran.
- **Azure Automation runbook** — Azure's hosted scheduler/script-runner. WPS has four PowerShell runbooks (in [`wpswatch.reporting/Jobs/`](C:\Users\Jacob%20WPS\wkspaces\WPSWatch\wpswatch.reporting\Jobs)) that each open a SQL connection and call one stored proc. The cadence (e.g. "every 4 hours") lives in the Automation account configuration in the Azure Portal, not in the repo.
- **`wpsWatchLookerUserOrg`** — a small cache table in the analytics DB, populated by `sp_wpsWatchUserOrgUpdate`, listing `(UserId, OrganizationId, OrgIDUUID, Email)` rows. The contract that lets Looker do row-level filtering. Currently re-synced every 2 minutes by the timer-triggered `NewUserOrgSyncFunction` (per `example.settings.json:6` — `NEWUSER_SYNC_TIMER_FREQUENCY = "*/2 * * * *"`).
- **Semantic layer** — a description of tables/columns in human terms (`battery_percent`, not `BatteryLevel decimal(5,2)`) that an LLM consumes instead of (or alongside) raw schema introspection.
- **Read-committed snapshot (RCSI)** — every read statement sees a snapshot; transactions don't. Cheap to enable, dramatically reduces blocking.
- **DENY** — a SQL Server permission that overrides any GRANT. Belt-and-suspenders for sensitive columns.

---

## 1. The three-way comparison — recommendation

**Recommendation: use the existing analytics DB `wps-sql-analytics-prod` as the read source, and extend its `wpsWatch.*` schema for the AI Reports use cases. Do not introduce a read replica. Do not stand up a separate mirror / warehouse.**

The v1 doc framed this as "replica vs. mirror" and recommended a hybrid built on a replica. **That framing was wrong** in two ways: the replica doesn't exist, and the team has already chosen a third option that's working — a separate analytics database, populated through external tables and Azure Automation runbooks. The right call is to fit AI Reports into that pattern rather than introduce a fourth one.

### Option A — keep status quo: live OLTP queries from the AI service against `wpswatch-prod`

Pros:
- Always fresh.
- No schema to maintain in two places.

Cons (these are decisive):
- Sensitive columns are right there in `dbo.Site` (the OLTP `Site` table includes `DasToken`, `SmartIntegrateToken`, `SmartToken`, `SlackWebhookUrl`, `CiscoSustainableImpactToken`, `ZendoApiKey`, and now `ZendoUrl`/`ZendoPath` per `wpswatch.reporting/Tables/ExternalTables/dbo.Site.Table.sql:693–706`). Any read login on `dbo.*` has access to all of them unless individually denied.
- Heavy reads contend with production write workload. The OLTP indexes are tuned for writes; "Photo count grouped by day for an organization" reads tens of millions of rows.
- The OLTP schema evolves with the API team's migration cadence. The AI service inherits every column rename.

### Option B — introduce a true Azure SQL read replica

Pros:
- Sub-second lag.
- No replication code to write.

Cons:
- **The replica doesn't exist today** (per [exploration notes §8](../ai-reports-exploration-notes.md#8-replica-realities) and confirmed by the absence of `wpswatchprodreplica` in any config). Setting it up means a Business Critical tier upgrade or read-scale-out configuration on the existing tier — real money, in an org that runs Wildlife Protection Solutions on charitable funding.
- It mirrors the OLTP schema column-for-column, including all the credential columns. Same security boundary as Option A; we'd still need column-level DENYs.
- The existing analytics DB pattern handles the same problem (separation from writes, dedicated read login, freedom to add curated objects) without the replica cost.
- It would create two parallel reporting substrates for Looker plus AI Reports. The Looker team would not adopt the replica because their existing pipeline is wired to the analytics DB; we'd be paying for a replica that only AI Reports uses.

### Option C — stand up a curated mirror / warehouse (Synapse, separate Azure SQL DB, BigQuery, …)

Pros:
- Total schema freedom.
- Decoupled from OLTP entirely.

Cons:
- We already have one. `wps-sql-analytics-prod` *is* a curated schema in a separate database. Building a second mirror would be duplicating the asset we have.
- Real engineering cost — Data Factory or equivalent ETL, plus the operational surface of a new substrate.
- The existing aggregation-table + runbook pattern is the WPS team's chosen idiom. A second pattern is operational drag.

### Option D — extend the existing analytics DB (the recommendation)

The analytics DB is, in effect, the curated mirror that the v1 doc said we should consider building — already built. It has:

- A dedicated database (`wps-sql-analytics-prod`) on the same logical server, so cross-DB external tables work without networking gymnastics.
- A dedicated reporting schema, `wpsWatch.*`, that already excludes most of what we'd hand-curate out (no `User` table replication; no `Key` table; no `ImageRecognitionService` table — see §3 for the full picture).
- Pre-aggregations for several of the reports we need to replace (see §5 — substantial overlap with the v1 doc's `fct_*` proposals).
- A scheduled-refresh story (Azure Automation runbooks in [`wpswatch.reporting/Jobs/`](C:\Users\Jacob%20WPS\wkspaces\WPSWatch\wpswatch.reporting\Jobs)).
- A user-org scoping cache (`wpsWatch.wpsWatchLookerUserOrg`, see §6 of the original [exploration notes](../ai-reports-exploration-notes.md) for the broader context).
- A read-only SQL login (`wpswatch-looker`) with `db_datareader` on the analytics DB and nothing on `wpswatch-prod`.

The honest weaknesses of this pattern, which I'll surface so we go in with eyes open:

- **External tables are not free.** When an analytics-DB query touches `dbo.Photo` or `dbo.Deployment`, it actually executes against `wpswatch-prod`. So a UDF that joins five external tables is doing five cross-DB calls. The cure is the aggregation tables (which materialize the heavy joins) — and the analytics DB already uses them for the expensive reports. The diagnostic: a `vw_wpsWatch*` that selects from a `wpsWatchTbl*` table is cheap; one that selects from a `tvf_wpsWatch*` UDF is live but slow. The discipline already exists.
- **Refresh cadence is opaque from the repo.** The PowerShell jobs in `wpswatch.reporting/Jobs/*.ps1` set `CommandTimeout = 600` seconds but the *schedule* lives in the Azure Automation account configuration in the portal, not in source. We need a freshness-gate column (or table) in the analytics DB to expose "when did the last successful refresh complete" — currently the only way to answer that is to query the agg tables for `MAX(RunDateTime)`. See DR-V2-08.
- **Three versions of `vw_wpsWatchRegionSiteTotals` (V1/V2/V3) and similarly for `vw_wpsWatchRegionSiteLevelTotals` (V2/V3)** are checked in, with no `DEPRECATED` markers. This is a soft signal: schema iterates, the deprecation cycle is informal, and Looker is presumably reading one specific version that we'd have to identify from the dashboard's data-source URL. The Readme even has a `TODO: Figure out which of the SiteTotals and SiteLevelTotals views and User-Defined Functions can be removed.` (`wpswatch.reporting/Readme.md:28`). The AI Reports manifest will have to pin to one specific version.
- **The credentials columns are still replicated** — even though `User` and `Key` are not, the `Site` external table includes every secret on `Site` (per `wpswatch.reporting/Tables/ExternalTables/dbo.Site.Table.sql`). So the "secrets are absent by virtue of being on `User`" argument doesn't hold across the board. §3 has the specific containment plan.

**On balance the analytics-DB path is overwhelmingly the right answer.** The marginal cost of fitting AI Reports into the existing pattern is much smaller than the cost of standing up either a replica or a parallel mirror. The v1 doc's "hybrid: replica + curated reporting schema" recommendation is replaced by "use the curated analytics DB that already exists; treat the `wpsWatch.*` schema as the contract."

---

## 2. Schema exposure to the LLM

**Recommendation:**
1. **For the seven Looker-replacement reports:** call the existing stored procedures and views directly. The AI orchestrator generates a tool call (e.g. "run the SIM Data report for `organization_id = X`"), not SQL.
2. **For ad-hoc questions:** the LLM generates SQL against a small, named, AI-friendly subset of `wpsWatch.*` views that we *introduce* — names like `wpsWatch.vw_ai_camera_inventory`, `wpsWatch.vw_ai_deployment_status`, `wpsWatch.vw_ai_photo_count_daily`. These are thin wrappers (often `SELECT specific_columns FROM the_existing_object WHERE org_filter`) whose purpose is to give the LLM a clean, opinionated schema separate from the Looker-shaped objects.
3. **The semantic manifest** (the JSON the orchestrator hands to the LLM) describes only the `vw_ai_*` objects and the stored procs. Existing Looker objects (`vw_wpsWatchRegionSiteTotalsV3`, etc.) are *not* in the manifest. The LLM never sees them.

This is a refinement of the v1 doc's recommendation. There the new objects sat in a `reporting` schema; here they sit alongside the existing `wpsWatch.*` objects with a naming prefix. Same end state, different packaging, and consistent with the existing convention.

### Why a separate `vw_ai_*` view layer instead of just pointing the LLM at the existing objects

The existing `wpsWatch.*` views were shaped by Looker's needs. They expose three columns called `Online` / `Offline` / `Decommissioned`, all integers, all aggregated to organization-level. They use `'Online'` and `Offline` as quoted aliases with leading capitals. They include columns like `PercentOnline` computed inline with CASE expressions. An LLM asked "show me the deployments that went offline yesterday" against `vw_wpsWatchDeployedCameraBatteryLevel` will sometimes succeed and sometimes return today's deployments because `RunDate = GetDate()` is computed in the view and the LLM treats it as a partition column. **Looker can be patient with surface area like this; an LLM cannot.**

Concretely, the `vw_ai_*` layer:

- Uses snake_case column names (`battery_percent`, `last_event_utc`, `is_active_deployment`).
- Has no embedded `'CONSTANT'` columns. No `RunDate = GetDate()`. The freshness column comes from the underlying agg table's `RunDateTime`, plain.
- Filters out the columns that shouldn't be exposed (§3). The Looker views don't filter sensitive columns by absence — they just don't reference them in `SELECT *` fashion. Defense in depth requires we restate the exclusion list explicitly.
- Parameterized for org-scope via a `WHERE organization_id = …` predicate that is uniform across views. The LLM never has to choose a different filter column for different views.

### Why we don't just generate SQL against the existing views

Three reasons:

1. The existing views project columns the LLM should not see (e.g. `vw_wpsWatchDeployedCameraBatteryLevel` exposes raw `BatteryLevel`, not a percentage; `vw_wpsWatchSIMData` exposes `PhotoTotal` and `EstimatedGBRemaining` whose interpretation is non-obvious — see §8 on SIM data).
2. The existing views use SQL Server column quoting (`[Active]`, `'Online'`) that confuses any LLM trained on Postgres conventions.
3. Looker selects from these views by URL parameter; we'd be coupling AI Reports' contract to whatever Looker happens to need next. Better to give the AI service its own contract surface.

### What's added to the manifest

- The 7 admin Looker reports as tool calls (`run_camera_inventory(organization_id)`, etc.) — each backed by one existing `vw_wpsWatch*` view or one new stored proc that wraps it. For Camera Inventory: `wpsWatch.vw_wpsWatchCameraInv` (which wraps `tvf_wpsWatchCameraInv`) exists today, scoped only by `organization_id` filtering in the WHERE clause.
- The 3 regular-user Looker reports as tool calls.
- A small set of `vw_ai_*` views for ad-hoc questions:
  - `wpsWatch.vw_ai_organization` — names, region, country.
  - `wpsWatch.vw_ai_site` — names, time zone, no GPS, no DAS/SMART/Slack/Zendo/CiscoSustainableImpact credentials.
  - `wpsWatch.vw_ai_deployment` — current deployment status (active/inactive based on `LastEventDateTimeUtc`), battery percent, solar-panel flag, no GPS.
  - `wpsWatch.vw_ai_device` — make, model, serial, decommission status; no `PhoneNumber`, no `ForwarderPhoneNumber`, no `SimCardNumber`.
  - `wpsWatch.vw_ai_photo_count_daily` — backed by a new daily agg table (§5).

### Evolution

Same shape as v1: new question types → engineer adds a `vw_ai_*` view; OLTP schema changes → the `vw_ai_*` layer absorbs them; new canned report → new stored proc + tool registration. The CI gate (§6) enforces that the manifest matches the live schema. The two-repo coordination (`wpswatch.reporting` SQL changes + AI service repo manifest changes) becomes part of the contract-test pipeline (DR-V2-23).

---

## 3. Off-limits data — re-checked against the actual analytics DB

This is the section where I had to retract the most. The v1 doc's "secrets are absent because the curated schema doesn't model them" claim is partly right and partly wrong, depending on the column.

### What's already absent from the analytics DB (confirmed by inspecting `wpswatch.reporting/Tables/ExternalTables/`)

There are **no external tables** for:
- `User` — so `User.PasswordHash`, `User.SecurityStamp`, `User.ConcurrencyStamp`, `User.Email`, `User.PhoneNumber` are **not reachable from the analytics DB at all**. The only place an email appears is `wpsWatchLookerUserOrg.Email` (cached for the Looker handoff).
- `Key` — so API keys are not reachable.
- `ImageRecognitionService` — so the ML-service `ApiKey` and `Url` are not reachable.
- `ApplicationUser`, `AspNetRoles`, etc. — no Identity Framework tables.

This means an AI service that connects with `db_datareader` on `wps-sql-analytics-prod` *cannot* see those secrets even if it tries. The v1 doc's "defense in depth via DENY at the OLTP layer" plan is therefore over-engineered for these columns — they're not in the DB to deny.

### What is replicated and needs containment

The `Site` external table (`wpswatch.reporting/Tables/ExternalTables/dbo.Site.Table.sql`) includes every Site secret:

| Column | Status in analytics DB |
|---|---|
| `Site.DasToken`, `Site.DasBaseUrl` | **Replicated** via external table |
| `Site.SmartToken`, `Site.SmartBaseUrl` | **Replicated** |
| `Site.SmartIntegrateToken`, `Site.SmartIntegrateBaseUrl` | **Replicated** |
| `Site.SlackWebhookUrl` | **Replicated** |
| `Site.CiscoSustainableImpactToken`, `Site.CiscoSustainableImpactBaseUrl` | **Replicated** |
| `Site.ZendoApiKey`, `Site.ZendoUrl`, `Site.ZendoPath` | **Replicated** |
| `Site.Latitude`, `Site.Longitude` | **Replicated as `decimal(12, 9)`** |

Similarly the `Deployment` external table (`wpswatch.reporting/Tables/ExternalTables/dbo.Deployment.Table.sql`) replicates `Deployment.Latitude` and `Deployment.Longitude` as `decimal(12, 9)`.

The `Device` external table (`wpswatch.reporting/Tables/ExternalTables/dbo.Device.Table.sql`) replicates `Device.PhoneNumber`, `Device.SimCardNumber`, `Device.ImeiNumber`, `Device.CarrierName`, and the SIM-billing columns (`PrepaidDataPlanRenewal`, `PrepaidDataLimitGb`, `PrepaidDataLimitTermId`). It does *not* include the v1 doc's `ForwarderPhoneNumber` (which doesn't exist in the OLTP schema — I had that wrong).

**The Photo external table is already pre-filtered** (per `wpswatch.reporting/Tables/ExternalTables/dbo.Photo.Table.sql:230–248`). It includes `PhotoId`, `Guid`, `CaptureDateTimeUtc`, `DeploymentId`, `IsFavorited`, `IsReported`, `DeviceName`, `IsCleared`, `IsReviewed`, `IngestDateTimeUtc`, `CaptureDateTimeLocal`, `HasVideo`, `IncidentId`, `DeviceId`, `Notes`. It explicitly **does not include `IsRemoved`** — a deliberate omission, presumably so reports don't accidentally show photos the user has soft-deleted. Good. (Note the column-set above also confirms there's no blob URL on the external Photo table — the SAS URLs are constructed at API time, not stored.)

### The containment plan

Primary mechanism is **non-projection in the `vw_ai_*` layer** — the new AI-facing views simply don't select the sensitive columns. Combined with the LLM never being told about the underlying `dbo.*` external tables via the manifest, the AI cannot ask for what it doesn't know about.

Secondary mechanism is `DENY` at the analytics-DB level on the AI login (`ai_reports_reader`), applied to the specific columns in the external tables. Specifically:

```sql
DENY SELECT ON dbo.Site (DasToken, DasBaseUrl, SmartToken, SmartBaseUrl,
                        SmartIntegrateToken, SmartIntegrateBaseUrl,
                        SlackWebhookUrl, CiscoSustainableImpactToken,
                        CiscoSustainableImpactBaseUrl, ZendoApiKey, ZendoUrl,
                        ZendoPath, Latitude, Longitude) TO ai_reports_reader;
DENY SELECT ON dbo.Deployment (Latitude, Longitude) TO ai_reports_reader;
DENY SELECT ON dbo.Device (PhoneNumber, SimCardNumber, ImeiNumber) TO ai_reports_reader;
```

Whether these DENYs work across external-table reads is worth verifying — Azure SQL elastic-query has historically had quirks with column-level permissions on external tables. If they don't (open question DR-V2-29), fall back to *only granting the AI login access to the `vw_ai_*` views*, never to `dbo.*` directly. The view layer is the security boundary.

The output filter from the v1 doc (drop columns matching `*token*`, `*hash*`, `*secret*`, `*password*`, `*apikey*`) is still worth keeping as a tertiary mechanism. It costs almost nothing in code and catches the case where a future view inadvertently surfaces a sensitive column.

### GPS specifically

The v1 doc said GPS columns should be absent from the views. Same conclusion holds, with one nuance the v1 doc missed: **`vw_wpsWatchSIMData`, `vw_wpsWatchDeployedCameraCount`, and `vw_wpsWatchCameraInv` already don't project GPS** (they're not in any of those views' `SELECT` clauses per the SQL I've read). So for canned reports, GPS exposure is already absent. The `vw_ai_*` views must enforce the same. The `vw_wpsWatchUjungKolonDetectionReport` view's underlying aggregation table *does* store `Latitude` and `Longitude` (`wpsWatch.wpsWatchTblUjungKulonDetectionReport`, lines 531–532 of the table DDL) — that's a Ujung Kulon-specific detection report and is out of scope for AI Reports MVP, but it's worth flagging that the convention "GPS is absent" is not universal in the existing schema.

---

## 4. Query cost controls

Most of the v1 recommendations carry over, with the refresh-cadence story adapted to the runbook reality.

| Control | Recommendation | Notes |
|---|---|---|
| Connection target | `wps-sql-analytics-prod` (analytics DB), not the OLTP primary | Same logical server, different DB. No `ApplicationIntent` needed; the analytics DB is read-only by convention. |
| Login | New `ai_reports_reader` with `db_datareader` on `wps-sql-analytics-prod` only, no permissions on `wpswatch-prod` | Distinct from `wpswatch-looker` to keep audit trails separate. |
| Statement timeout (default) | **15 seconds** | Same as v1. Most canned reports hit an agg table and complete sub-second; ad-hoc queries against the `vw_ai_*` views need a ceiling. |
| Statement timeout (export path) | 120 seconds | Same as v1. Separate connection. |
| Default row cap | **5,000 rows** (`TOP 5000` injected into ad-hoc queries) | Same as v1. |
| Hard row cap | 100,000 rows | Same as v1. |
| Read isolation | **RCSI on the analytics DB**, if it isn't already | Verify before assuming. Worth confirming via `SELECT DATABASEPROPERTYEX('wps-sql-analytics-prod', 'IsReadCommittedSnapshotOn')`. |
| Query Store | Enabled on the analytics DB with 7-day retention and `WAIT_STATS_CAPTURE_MODE = ON` | Already enabled? Worth verifying. The DB team has been observing this DB for some time. |
| Cost-profile metadata | Each stored proc, each named view, each agg table carries an `expected_cost_class` (`fast` < 1s, `medium` < 5s, `slow` < 15s, `agg` ≈ 0.1s) in the manifest | Same as v1, with the addition of an `agg` class for "queries against pre-materialized aggregation tables." |
| Concurrency cap | Max 5 concurrent queries per user, max 25 per service instance | Same as v1. |
| External-table awareness | Manifest flags each `vw_ai_*` view with `data_source = "agg_table"` or `data_source = "external_table_live"` so the orchestrator can predict performance | New addition. The cost difference between "this view reads from `wpsWatchDeployedCameraCount` agg table" (sub-100ms) and "this view reads from `tvf_wpsWatchRegionSiteTotalsV3` UDF over external tables" (5–30 seconds depending on data volume) is large enough that we want to expose it to both the LLM and the user. |
| Resource governor (future) | Worth setting up if we observe contention with Looker; not required for MVP | Same as v1. |

### Refresh-cadence story

The v1 doc said "Azure SQL elastic jobs." Wrong. **The existing scheduler is Azure Automation runbooks**, four of them in [`wpswatch.reporting/Jobs/`](C:\Users\Jacob%20WPS\wkspaces\WPSWatch\wpswatch.reporting\Jobs):

- `wpsWatchCameraTrends.ps1` → `exec [wpsWatch].[sp_wpsWatchDeployedCameraTrends]`
- `wpsWatchDeployedCamera.ps1` → `exec [wpsWatch].[sp_wpsWatchDeployedCameraCount]`
- `wpsWatchProdAnalSql.ps1` → `exec [wpsWatch].[sp_wpsWatchUjungKulonDetectionReport]`
- `wpsWatchSimData.ps1` → `exec [wpsWatch].[sp_wpsWatchSIMDataReport]`

Each opens a SQL connection as `wpswatch-looker`, sets `CommandTimeout = 600` (10 minutes), runs one stored proc, closes. The schedule for each lives in the Azure Automation account configuration. **For AI Reports' new agg tables (§5), use the same pattern.** Write a PS script per agg table, add it to the runbook account, set a schedule. We don't migrate from runbooks to elastic jobs unless we have a reason; "consistency with existing pattern" beats "new option seems cleaner."

That said: the runbook pattern has two weaknesses worth tracking:

- **No job-completion telemetry in source.** Whether a runbook ran today, whether it succeeded, how long it took — all of that lives in the Azure Automation logs, not in the database. For AI Reports' freshness story we need this surfaced in the DB. Recommend a new table `wpsWatch.wpsWatch_job_run_log (job_name, started_utc, completed_utc, status, row_count, notes)` written to from each refresh proc as its last action. Easy add. The Looker side can also benefit.
- **Hard-coded credentials in the runbook scripts.** Every `wpswatch*.ps1` has the connection string with `User ID=wpswatchLooker; Password=J6J^XVPS9-h@a&#SS` literally embedded (see `wpswatch.reporting/Jobs/wpsWatchDeployedCamera.ps1:8`). This is technically secrets in source control; should be migrated to Automation account variables / Key Vault references. Out of scope for AI Reports but worth flagging.

---

## 5. Time-series / pre-aggregation — what's already done, what's still needed

The v1 doc proposed building three fact tables from scratch (`fct_photo_count_daily`, `fct_device_event_hourly`, `fct_battery_snapshot_daily`). Re-evaluated against what exists:

### Already covered (or close to it)

#### Camera inventory totals — DONE

`wpsWatch.tvf_wpsWatchCameraInv` (per `wpswatch.reporting/UserDefinedFunctions/wpsWatch.tvf_wpsWatchCameraInv.UserDefinedFunction.sql`) returns per-org `TotalInventory`, `CurrentInv`, `Decommissioned`, `Undeployed`, `TotalDeployments`, `SiteOwned`, `Donated`, `Sitecount`. Backed by external tables — live, not pre-aggregated, but the join shape is fixed and cheap because the underlying tables are small (`Device` ~10k rows, `Site` ~hundreds).

Verdict: AI Reports' Camera Inventory canned report calls this. No new infrastructure.

#### Deployed Camera Count (per-deployment with photo count last month) — DONE

`wpsWatch.wpsWatchDeployedCameraCount` (agg table, populated by `sp_wpsWatchDeployedCameraCount`) holds: `OrganizationName`, `OrganizationId`, `SiteName`, `DeviceName`, `DeploymentName`, `DeviceMakeName`, `DeviceModelName`, `PhotoCount`, `RunDateTime`. The proc filters to the *previous month's* photos (per `sp_wpsWatchDeployedCameraCount`, computing `@StartDate = DATEADD(m, -1, DATEADD(mm, DATEDIFF(m, 0, GETDATE()), 0))` and `@EndDate` = start of the current month — i.e. "last full month").

Verdict: For the canned "Photo Count By Camera" report this is sufficient. For ad-hoc questions like "photo count by camera for the last 14 days" this is **insufficient** because the agg table only stores last month. **We still want a finer-grained agg table** — see "Still needed" below.

#### Deployed Camera Trends (org-level rollup of online / offline / power) — DONE

`wpsWatch.wpsWatchCameraTrends` agg table, populated by `sp_wpsWatchDeployedCameraTrends`. Holds totals: `TotalCurrentInventory`, `Undeployed`, `Deployed`, `Online`, `Offline`, `OfflinePower`, `OfflineConnectivityIssue`, `Decommissioned`, `ReportDataDate`. **Important: this proc already encodes the offline-threshold as 1440 minutes (24 hours)** at `wpswatch.reporting/StoredProcedures/wpsWatch.sp_wpsWatchDeployedCameraTrends.StoredProcedure.sql:66, 77, 224`. The v1 doc's "Top 10 Offline: recommend 24-hour default threshold" is consistent with what the existing system uses — see §8.2.

Verdict: AI Reports' canned "Top 10 Offline" and similar trend questions can call this. The 24-hour threshold is the de facto standard.

#### SIM Data Report — DONE (but with an important detail) — see §8.1

`wpsWatch.wpsWatchSIMDataReport` agg table, populated by `sp_wpsWatchSIMDataReport`. The v1 doc proposed adding `Device.PrepaidDataUsedGb` columns to the OLTP and syncing from Twilio. **That recommendation is wrong**; see §8.1 for the new position.

#### Region & Site Totals — V3 IS THE CURRENT, V1/V2 ARE LEGACY

`wpsWatch.tvf_wpsWatchRegionSiteTotalsV3` returns per-region/per-org/per-site: `RegionName`, `OrganizationName`, `HistoricalInventory`, `Decommissioned`, `Undeployed`, `Deployments`, `CurrentInv`, `Sitecount`, `Online`, `Offline`, `ActiveIssues`, `Total`, `SolarPower`. V2 has the same shape minus `SolarPower`. V1 has different columns (a flat `OnlinePercent` instead of `SolarPower`, and no `Decommissioned`). The corresponding views (`vw_wpsWatchRegionSiteTotals`, `…V2`, `…V3`) are all checked in.

Verdict: AI Reports' "Region & Site Totals" canned report calls V3 (because it's most complete). The manifest pins to V3. If Looker turns out to be using V2 today (open question DR-V2-30), the parity test against the Looker dashboard might surface differences — but going forward V3 is the right target.

### Still needed — net-new agg tables

#### `wpsWatch.fct_ai_photo_count_daily` — **must-build**

The existing `wpsWatchDeployedCameraCount` aggregates to the per-deployment level with a single fixed window (previous month). The new fact is:

- **Grain:** one row per (`deployment_id`, `device_id`, `site_id`, `organization_id`, `capture_date`).
- **Columns:** `photo_count`, `video_count` (count where `HasVideo = 1`), `cleared_count` (count where `IsCleared = 1`), `favorited_count`, `reported_count`, `first_capture_utc`, `last_capture_utc`.
- **Source:** `dbo.Photo` external table.
- **Note on `IsRemoved`:** the external `dbo.Photo` table doesn't project `IsRemoved` — it's already filtered out at the elastic-query level. So we get the same effective behavior as the v1 doc's "`WHERE IsRemoved = false`" without writing it.
- **Update:** runbook every 6 hours, recomputes trailing 3 days, leaves older days immutable.
- **Storage cost:** order-of-magnitude estimate ~10k active deployments × 365 days × ~120 bytes ≈ **430 MB/year**. Trivial.
- **Win:** "photo count by camera for the last 14 days" goes from a `GROUP BY` over the external `Photo` table (which is millions of rows, fetched cross-DB) to a sum over a tiny agg.

#### `wpsWatch.fct_ai_device_event_hourly` — **should-build**

The OLTP `Deployment` row has `LastEventDateTimeUtc` and `EventCount` but no history. To answer "show me which deployments went offline within the last 48 hours" the analytics DB has the trend data only at org level (in `wpsWatchCameraTrends`), not at the per-deployment level.

- **Grain:** one row per (`deployment_id`, `device_id`, `event_hour`).
- **Columns:** `event_count_delta` (computed as the difference between `Deployment.EventCount` between snapshots), `last_event_utc`, `battery_level_at_snapshot` (the `Deployment.BatteryLevel` value at snapshot time).
- **Update mechanism:** hourly runbook that snapshots `Deployment.EventCount` and `BatteryLevel` for active deployments. This is the only way to get historical event-rate data because OLTP doesn't keep it.
- **Storage cost:** 10k deployments × 24 × 365 = 87.6M rows/year × ~80 bytes ≈ **7 GB/year**. Use `CLUSTERED COLUMNSTORE` index — same recommendation as v1.
- **Win:** "Top 10 offline" with a custom threshold becomes a max-per-deployment query against a small dataset rather than a snapshot of the live `Deployment` table.

This is the same fact table the v1 doc proposed as `fct_device_event_hourly`. The recommendation hasn't changed, just the schema name. (Note: I'm using the `fct_ai_*` prefix to mark "introduced by AI Reports," analogous to the `vw_ai_*` view convention. If the SQL team prefers a flat naming convention, the prefix is negotiable.)

#### `wpsWatch.fct_ai_battery_snapshot_daily` — **nice-to-have**

Same shape and same caveat as the v1 doc's `fct_battery_snapshot_daily`: OLTP doesn't store battery history, so this fact table starts from the day we add it and never has historical data. If we add the hourly event-snapshot table above, `fct_ai_battery_snapshot_daily` can be derived from it (daily aggregate of hourly snapshots' battery values) rather than being a separate refresh job. Recommend deriving rather than independently capturing.

### Supporting dimensions

The v1 doc proposed `dim_deployment` and `dim_organization` to flatten the `Photo → Deployment → Site → Organization` join chain. Re-evaluated: the existing aggregation tables (e.g. `wpsWatchDeployedCameraCount`) already include `OrganizationId`, `OrganizationName`, `SiteName`, etc. denormalized onto every row. So for the canned reports this is done. For the new `fct_ai_*` tables, build them with the same denormalization pattern: `organization_id`, `organization_name`, `site_id`, `site_name` columns on every row. Don't add separate `dim_*` tables unless we hit a clear need for one — premature normalization.

### What I'm not pre-aggregating

- No per-tag aggregates (the `Tag` and `PhotoTag` external tables exist but tag-based reporting isn't in MVP scope).
- No incident aggregates beyond what already exists (`wpsWatchTblUjungKulonIncidentReport` is org 36-specific and not generalizable).
- No cross-organization rollups for MVP (single-org constraint inherited from existing reports).

### Maintenance mechanism

For the two new fact tables: Azure Automation runbooks, modeled on the existing four. PowerShell scripts checked into `wpswatch.reporting/Jobs/`, schedule configured in the Automation account. Each new proc writes a row to `wpsWatch.wpsWatch_job_run_log` on success. The freshness gate (DR-V2-08) reads that table.

---

## 6. Schema evolution — keeping the AI's understanding in sync

The mechanism is the same as v1 (manifest + contract tests + versioned objects). The coordination story changes because there are now two SQL-side repos to think about.

**Recommendation: a contract-test CI gate that runs against the live analytics DB; manifest pinned to specific object versions; deprecation cycles for V3 → V4 transitions.**

### Repos involved

1. **`wpswatch.reporting`** — owns the `wpsWatch.*` schema. SQL changes flow through here. Today's deployment is via Azure DevOps / SSDT projects (judging from the SSMS-generated `.sql` files in the repo).
2. **(New) AI Reports service repo** — owns the JSON manifest, the orchestrator, the API endpoints.
3. **`wps.watch.functions.scheduledreporting`** — owns the Functions app. Doesn't touch the AI Reports schema directly, but coordinates via the `wpsWatchLookerUserOrg` table — which AI Reports will also read.

### Pipeline

1. A `data-manifest.json` lives in the AI Reports service repo. It describes each `vw_ai_*` view, each `wpsWatch.*` stored proc and view exposed as a tool, each `fct_ai_*` table, with: name, type, purpose, columns (name, type, description, nullable, example, sensitivity), parameters (for procs), `expected_cost_class`, `data_source` (`agg_table` / `external_table_live` / `udf_over_external`), `version`.
2. A nightly CI job in **`wpswatch.reporting`** (or in the AI Reports repo, whichever owns the manifest) connects to the analytics DB with a read-only login, queries `INFORMATION_SCHEMA` for the objects named in the manifest, and diffs columns/types/parameter signatures. Disagreement → CI failure with a specific error message ("`wpsWatch.vw_ai_camera_inventory` has 12 columns in the manifest, 13 in the DB — add column `device_model_id` to the manifest or remove it from the view").
3. **Deployment ordering** mirrors v1: the analytics DB is always updated *before* the AI service consumes new objects. Backward-incompatible removals always happen *after* the AI service has stopped consuming them. The contract-test CI gate is the enforcement.
4. **Versioned objects** (V2 → V3 pattern that the team already uses for `vw_wpsWatchRegionSiteTotals*`) become the official deprecation cycle. When a `vw_ai_*` shape changes incompatibly, publish `vw_ai_X_v2` alongside the old one, manifest points at v2, old one emits a deprecation `RAISERROR` with severity 0 (so it's logged but doesn't fail the query) when invoked. After a 90-day overlap, drop the old version.
5. **Monitoring.** A new column `analytics_db_object_used` on the AI Reports `saved_query_run` audit table records which objects each saved-query run touched. A weekly digest highlights "saved queries scheduled for breakage in <30 days" — i.e. queries against `vw_ai_X_v1` while `vw_ai_X_v2` is the current.

The two-repo angle adds one wrinkle compared to v1: the SQL changes are batched and shipped through the existing `wpswatch.reporting` deployment story, which (judging from the Readme's mention of pre-September 2024 history in Confluence and the SSMS-generated files) is a manual or semi-manual flow. **Automating the analytics-DB deployment is a precondition for the contract-test gate to be useful.** If schema changes ship by hand without CI, the gate runs in CI but isn't backed by the deployment flow — i.e. we'd catch the drift but couldn't enforce it. This is a real open question for the SQL team's roadmap (DR-V2-28).

---

## 7. Saved-SQL implications (phase 3)

Mostly unchanged from v1. The one change is *where* saved queries are stored.

### Storage location

The v1 doc proposed a new dedicated Azure SQL DB. With the analytics DB already present, two natural homes:

1. **A new `wpsWatch.saved_query` and `wpsWatch.saved_query_run` table in `wps-sql-analytics-prod`.** Lives alongside the other `wpsWatch.*` objects. Same backup story, same connection, same login (with `db_datawriter` on the saved-query schema only).
2. **A new database entirely**, owned by the AI Reports service.

**Recommendation: option 1.** The analytics DB is the natural home — the saved-query *contents* are SQL against `wpsWatch.*`, so the query and the queried-thing should be co-located. The downside is mixing read-only Looker / AI Reports workload with the saved-query writes; in practice the write volume is tiny (one row per save, one row per execution) and the audit table will be the dominant grower. Use a separate filegroup if we care about isolation.

The audit table `wpsWatch.saved_query_run` records every execution (user, org, manifest version, runtime, status, row count, list of objects touched). It's the source of the deprecation-warning digest from §6.

### What was on `wpsWatchLookerUserOrg`

The v1 doc proposed `SESSION_CONTEXT(N'AllowedOrgIds')` for row-level filtering. **Reconsidered now that `wpsWatchLookerUserOrg` exists**:

**Recommendation: use both, in different roles.**

- **For canned reports and `vw_ai_*` views: filter by `WHERE organization_id = @org_id` on a parameter the AI orchestrator passes explicitly**, derived from the user's JWT. Don't rely on session context or RLS in the DB. The orchestrator already knows the user's allowed orgs from the JWT validation step; threading the right `@org_id` through is no harder than threading `SESSION_CONTEXT` and it's much more debuggable.
- **For the *bootstrap*** (i.e. "tell me which orgs this user has access to, when their JWT just arrived"): query `wpsWatch.wpsWatchLookerUserOrg` for the user's row(s). It's already in the analytics DB. It's already maintained. It's already used by Looker. The freshness is ~2 minutes (per `example.settings.json:6`), which is fine for a session-bootstrap concern — even if a user's org membership changed in the last 2 minutes, they get the right access on the next bootstrap.
  - Alternative for fresher data: hit the OLTP `dbo.OrganizationUserRole` external table directly, paying the per-request cross-DB latency cost. Probably not worth it for MVP.

This sidesteps `SESSION_CONTEXT` entirely. SESSION_CONTEXT has been useful in Azure SQL row-level security designs but it adds runtime complexity (have to set it per connection, can be missed in cross-statement contexts) and the team here doesn't use it elsewhere. Using an explicit parameter is more legible, easier to test, and matches the existing pattern.

### Validation on load

Three layers, in order — identical to v1:

1. **Syntax validation** via `sp_describe_first_result_set` or `EXPLAIN`-style sandbox.
2. **Permission validation** against the current user's org-scope (from the JWT, possibly cross-checked against the freshly-loaded `wpsWatchLookerUserOrg` row).
3. **Manifest validation** — does every referenced object exist in the current manifest with a compatible signature?

Deprecation warnings → run with warning; deletions → fail with a specific message + replacement pointer.

### Saved-query schema (concrete)

```sql
CREATE TABLE wpsWatch.saved_query (
    saved_query_id  INT IDENTITY(1,1) PRIMARY KEY,
    organization_id INT NOT NULL,
    user_id         NVARCHAR(128) NOT NULL,
    name            NVARCHAR(256) NOT NULL,
    description     NVARCHAR(2000) NULL,
    sql_text        NVARCHAR(MAX) NOT NULL,
    manifest_version NVARCHAR(64) NOT NULL,
    objects_referenced NVARCHAR(MAX) NULL, -- JSON array of object names
    created_utc     DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    last_run_utc    DATETIME2 NULL,
    last_run_status NVARCHAR(32) NULL,
    INDEX IX_saved_query_org_user (organization_id, user_id)
);
```

Saved queries are single-org for MVP. Cross-org saved queries are out of scope until cross-org reporting lands generally.

---

## 8. The unresolved-data questions — re-examined against the actual procs

### 8.1 SIM Prepaid Data — **the v1 recommendation flips**

The v1 doc said: "Add `Device.PrepaidDataUsedGb` to OLTP, populate from Twilio via a daily sync." That was based on an assumption that the existing report needed real carrier data and just didn't have it yet.

**Reading `sp_wpsWatchSIMDataReport`** (`wpswatch.reporting/StoredProcedures/wpsWatch.sp_wpsWatchSIMDataReport.StoredProcedure.sql`) shows the actual approach. **The current report does not pull from Twilio at all**. It computes:

```
EstimatedGBRemaining = de.PrepaidDataLimitGb - (0.0001 * COUNT(photos in billing cycle))
```

That is, **the current report estimates data used as 0.0001 GB (~100 KB) times the number of photos captured this billing cycle**. The billing cycle start is computed from `Device.PrepaidDataPlanRenewal` adjusted by the term length (monthly = 1 month back; yearly = 1 year back, via `PrepaidDataLimitTermID`). Number of photos comes from `dbo.Photo` joined to the device and deployment, filtered to `BETWEEN BillingCycleStart AND @Today`.

This is an *approximation*, not a measurement. It's not measuring actual cellular data used; it's predicting it based on photo upload count. The accuracy depends entirely on the 0.0001 GB-per-photo assumption holding.

**Revised recommendation:**

1. **Don't add `Device.PrepaidDataUsedGb` to OLTP and don't sync from Twilio.** The org's chosen approach is the estimation model and it has been good enough for the existing Looker dashboard. Match the existing report; don't reinvent the data source.
2. **Surface the estimation honestly in the AI Reports UI** by labeling the column as "Estimated GB remaining" (the existing proc already names it `EstimatedGBRemaining`) and documenting the approximation in the manifest's column description: `"Estimated as PrepaidDataLimitGb - (0.0001 * photos in billing cycle). Not based on actual cellular data usage."` This lets the LLM truthfully answer "is this an estimate?" with "yes."
3. **Open it as a future improvement.** If the org wants real Twilio-sourced data later, the schema change (add `Device.PrepaidDataUsedGb` + a daily sync) is the same as the v1 recommendation. But that's a future-state choice, not an MVP requirement. The data team should make that call when the estimation's accuracy becomes a real problem.

This is the biggest recommendation-flip from v1. The v1 doc's reasoning was right *if* the existing report were pulling carrier data badly; reading the proc shows it's pulling no carrier data at all and the report shape is "best-effort estimation, deliberately so."

### 8.2 "Top 10 Offline" threshold — confirms the v1 default

**24 hours.** Confirmed by reading the existing trend stored procs. `sp_wpsWatchDeployedCameraTrends` uses `DATEDIFF(Minute, d.LastEventDateTimeUtc, GETUTCDATE()) < 1440` as the "active" filter and `>= 1440 OR NULL` as the "inactive" filter (at e.g. `wpswatch.reporting/StoredProcedures/wpsWatch.sp_wpsWatchDeployedCameraTrends.StoredProcedure.sql:66, 77, 188–189, 224`). 1440 minutes is exactly 24 hours.

So:
- **Default for AI Reports: 24 hours.** Matches the established convention.
- **Overridable** via a parameter on the new tool call (`top_10_offline(organization_id, threshold_hours = 24)`).
- **The future "expected event interval per device" enhancement** the v1 doc mentioned is still a good idea but is phase-2.

Worth flagging: the existing trend rollup is **organization-level**, not device-level. To answer "show me the top 10 offline deployments" you need device-level data joined to "deployment is currently offline." The `vw_ai_deployment_status` view will need to provide this — it doesn't drop out of the existing trend infrastructure. Probably means a new stored proc `wpsWatch.sp_ai_TopOfflineDeployments(@OrganizationId, @ThresholdHours)` reading from the existing external `Deployment` table directly.

---

## 9. Data-layer requirements (DR-V2-01 … DR-V2-30)

These are the requirements that the synthesis doc and downstream personas can reference by number. Renumbered to make clear they supersede the v1 list.

1. **DR-V2-01 — Read source.** The AI Reports service reads from `wps-sql-analytics-prod` (the existing analytics database on the `wpswatch-prod` Azure SQL Server). Not the OLTP primary, not a replica, not a separate mirror. This replaces v1 DR-01.

2. **DR-V2-02 — Schema location.** All AI-facing reporting objects live in the existing `wpsWatch.*` schema in the analytics DB, alongside the existing 15 views / 7 procs / 9 UDFs / 10 agg tables. New objects use a `vw_ai_*` / `sp_ai_*` / `fct_ai_*` prefix to distinguish them from Looker-shaped objects. Replaces v1 DR-02.

3. **DR-V2-03 — Read-only login.** A new SQL login `ai_reports_reader` is created with `db_datareader` on `wps-sql-analytics-prod` only, and explicit `DENY SELECT` on the sensitive columns enumerated in §3 (DAS/SMART/Slack/Zendo/Cisco tokens on `Site`; lat/lon on `Site` and `Deployment`; `PhoneNumber`/`SimCardNumber`/`ImeiNumber` on `Device`). Auth via managed identity where possible; fallback to Key Vault secret. Distinct from the existing `wpswatch-looker` login to keep audit trails separate.

4. **DR-V2-04 — No direct OLTP exposure.** The AI orchestrator may not, under any circumstance, query `wpswatch-prod` directly. All data access goes through the analytics DB.

5. **DR-V2-05 — Sensitive columns absent from `vw_ai_*` views.** The new AI-facing views must not project any of the columns enumerated in §3. Primary protection is non-projection; secondary protection is the `DENY` on the AI login.

6. **DR-V2-06 — Canned reports as tool calls.** The seven admin Looker reports (Camera Inventory, Battery Level, Photo Count, Region & Site Totals, SIM Contract Renewal, SIM Prepaid Data, Top 10 Offline) and the three regular-user reports are exposed as parameterized tool calls. Each backed by an existing `wpsWatch.*` view/proc *or* a new `sp_ai_*` proc that wraps one. Specifically:
   - Camera Inventory → `wpsWatch.vw_wpsWatchCameraInv` (existing)
   - Battery Level → `wpsWatch.vw_wpsWatchDeployedCameraBatteryLevel` (existing)
   - Photo Count → `wpsWatch.vw_wpsWatchDeployedCameraCount` (existing; previous month) + new `vw_ai_photo_count_daily` for finer ranges (new — see DR-V2-12)
   - Region & Site Totals → `wpsWatch.vw_wpsWatchRegionSiteTotalsV3` (existing, V3 is current)
   - SIM Contract Renewal / Prepaid Data → `wpsWatch.vw_wpsWatchSIMData` (existing)
   - Top 10 Offline → new `sp_ai_TopOfflineDeployments(@OrganizationId, @ThresholdHours)` because the existing trend agg is org-level, not device-level

7. **DR-V2-07 — `vw_ai_*` view catalog.** A new set of views (`vw_ai_organization`, `vw_ai_site`, `vw_ai_deployment`, `vw_ai_device`, `vw_ai_photo_count_daily`) is added to the `wpsWatch.*` schema with snake_case columns, sensitive columns excluded, and `WHERE organization_id = @org_id` filtering. The LLM generates ad-hoc SQL against these only.

8. **DR-V2-08 — Job run log.** A new table `wpsWatch.wpsWatch_job_run_log (job_name, started_utc, completed_utc, status, row_count, notes)` records every refresh-proc run. Each existing proc and each new proc writes a row on completion. The AI service's freshness gate (DR-V2-15) reads from this.

9. **DR-V2-09 — Semantic manifest.** `data-manifest.json` in the AI Reports service repo describes every exposed object: name, type, purpose, columns (name, type, description, nullable, sensitivity), parameters, `expected_cost_class`, `data_source` (`agg_table` / `external_table_live` / `udf_over_external` / `saved_query`), `version`. Generated from the live analytics DB and committed to source.

10. **DR-V2-10 — GPS columns never surfaced.** Confirmed by inspection that `vw_wpsWatchCameraInv`, `vw_wpsWatchDeployedCameraCount`, `vw_wpsWatchSIMData`, and `vw_wpsWatchDeployedCameraBatteryLevel` already don't project GPS. The new `vw_ai_*` views maintain this. The Ujung Kulon-specific objects (which do contain GPS) are not exposed.

11. **DR-V2-11 — Statement timeout: 15 seconds default; 120 seconds for export path.** Same as v1.

12. **DR-V2-12 — Row caps.** Default `TOP 5000` injected into ad-hoc queries; hard ceiling of 100,000 rows even on export. Same as v1.

13. **DR-V2-13 — RCSI verified on the analytics DB.** Confirm enabled before deploying the AI service.

14. **DR-V2-14 — Query Store enabled** on the analytics DB with 7-day retention and `WAIT_STATS_CAPTURE_MODE = ON`. Verify before assuming.

15. **DR-V2-15 — Freshness gate.** Before running any tool call that reads from a `wpsWatch*` agg table, the orchestrator checks the corresponding `wpsWatch_job_run_log` row. If the last successful refresh is older than a per-object threshold (`stale_after_hours` in the manifest, default 6 hours), the response warns the user "Data may be stale (last refresh: …)." If older than `hard_fail_after_hours` (default 48), the tool returns an error rather than misleading data. Replaces v1 NFR-04 / S-MUST-14.

16. **DR-V2-16 — Cost-profile metadata.** Every exposed object carries an `expected_cost_class` and a `data_source` in the manifest. Replaces v1 DR-14.

17. **DR-V2-17 — Concurrency caps.** 5 per user, 25 per service instance. Same as v1.

18. **DR-V2-18 — Photo-count daily fact table.** `wpsWatch.fct_ai_photo_count_daily` is built. Refreshed every 6 hours via Azure Automation runbook, 3-day trailing recompute window. (§5.) Replaces v1 DR-16.

19. **DR-V2-19 — Device-event hourly fact table.** `wpsWatch.fct_ai_device_event_hourly` is built with a clustered columnstore index. Refreshed hourly via runbook. (§5.) Replaces v1 DR-17.

20. **DR-V2-20 — Battery-snapshot fact derived from the event-hourly table.** No separate refresh job. (§5.) Replaces v1 DR-18.

21. **DR-V2-21 — Refresh mechanism: Azure Automation runbooks.** New refresh procs use the existing PowerShell-runbook pattern in [`wpswatch.reporting/Jobs/`](C:\Users\Jacob%20WPS\wkspaces\WPSWatch\wpswatch.reporting\Jobs). Each new `.ps1` lives alongside the four existing scripts. Schedule configured in the Azure Automation account. Replaces v1 DR-20 ("elastic jobs"). The hard-coded credentials in the existing scripts should be migrated to Automation account variables out-of-band — not a blocker for AI Reports but flagged for the SQL team.

22. **DR-V2-22 — Org-scoping mechanism.** Every `vw_ai_*` view and every `sp_ai_*` proc accepts a single `@organization_id` parameter and rejects multi-org access in MVP. The orchestrator passes the parameter derived from the user's JWT. `wpsWatch.wpsWatchLookerUserOrg` is the bootstrap source for "which orgs does this user have access to" (§7). No `SESSION_CONTEXT`, no Row-Level Security policies. Replaces v1 S-MUST-09–11.

23. **DR-V2-23 — Schema-evolution contract tests.** Two-repo coordination: a CI job (in either `wpswatch.reporting` or the AI Reports service repo, owner TBD) diffs the live `wpsWatch.*` schema against the committed `data-manifest.json` and fails on disagreement. Coordinated against the Functions repo where the analytics DB is also accessed. Replaces v1 DR-21.

24. **DR-V2-24 — Versioned objects.** Backward-incompatible changes to `vw_ai_*` or `sp_ai_*` objects follow the V2/V3 deprecation pattern the team already uses (per `vw_wpsWatchRegionSiteTotals*`). 90-day overlap; deprecation warnings logged via `RAISERROR` severity 0 during the overlap. Replaces v1 DR-22.

25. **DR-V2-25 — Saved-query storage.** `wpsWatch.saved_query` and `wpsWatch.saved_query_run` tables added to the analytics DB (§7). Schema specified in §7. Single-org for MVP. Replaces v1 DR-24 + DR-26.

26. **DR-V2-26 — Saved-query validation.** Three layers — syntax, permission, manifest — same as v1 DR-25.

27. **DR-V2-27 — Output filter.** Before returning rows to the UI, drop any column whose name matches the sensitive-pattern allowlist (`*token*`, `*hash*`, `*secret*`, `*password*`, `*apikey*`). Replaces v1 DR-23.

28. **DR-V2-28 — Deployment ordering enforced by CI.** Analytics-DB changes deploy *before* the AI service consumes them; backward-incompatible removals happen *after* consumption ends. The contract-test gate is the enforcement; this implies the `wpswatch.reporting` deployment story has to be CI-driven (today it's SSMS-generated files, deployment mechanism unclear from the repo). Open question.

29. **DR-V2-29 — Verify column-level DENY works on external tables.** Test that `DENY SELECT ON dbo.Site (DasToken) TO ai_reports_reader` actually blocks the column when read via the elastic-query external table. If it doesn't (known historical quirk), fall back to "AI login has no access to `dbo.*`, only to `wpsWatch.vw_ai_*` views" as the sole boundary. Either way the user-facing result is "AI cannot see secrets," but the mechanism affects ops.

30. **DR-V2-30 — Confirm which `vw_wpsWatchRegionSiteTotals*` version is current.** Read the Looker dashboard's data-source URL (a manual check via the Looker UI) to know whether the live Looker report is on V1, V2, or V3. The MVP parity test for "Region & Site Totals" must target the same version. Recommendation independent of that: AI Reports manifest pins to **V3** because it's the most complete.

The notable removals from the v1 list:
- v1 DR-01 (read replica) — replica doesn't exist; replaced by DR-V2-01.
- v1 DR-02 (new `reporting` schema) — replaced by DR-V2-02 (extend `wpsWatch.*`).
- v1 DR-20 (elastic jobs) — replaced by DR-V2-21 (runbooks).
- v1 DR-27 (Twilio sync) — withdrawn; existing estimation model is the design.

---

## 10. What I'm not recommending and why

- **No separate mirror DB / warehouse for MVP.** The analytics DB *is* the curated mirror; building a parallel one is duplication.
- **No Azure SQL read replica.** Doesn't exist, has real cost, doesn't solve a problem the analytics DB doesn't already solve. The v1 recommendation that started with the replica was wrong; explicitly retracted.
- **No BigQuery in the AI Reports path.** BigQuery's role is the Looker user-org sync (`NewUserOrgSyncFunction` writes to `wpsWatchUserOrgId.wpsUserOrg`). AI Reports reads `wpsWatchLookerUserOrg` from the analytics DB directly. The BigQuery sync is Looker-specific and stays Looker-specific.
- **No migration from runbooks to elastic jobs.** The existing pattern works; switching schedulers is unnecessary scope.
- **No `WITH (NOLOCK)`.** Same as v1: RCSI is the correct answer.
- **No raw `INFORMATION_SCHEMA` introspection by the LLM.** The manifest is the contract; introspection invites drift.
- **No `SESSION_CONTEXT`-based row filtering.** Reconsidered; explicit `@organization_id` parameters are simpler.
- **No SQL Server `CREATE SECURITY POLICY` row-level security.** Same as v1: per-row predicates hurt plan caching and add operational complexity for no marginal benefit beyond explicit org filtering.
- **No new OLTP columns for Twilio data.** §8.1.
- **No reuse of `wps.watch.functions.scheduledreporting` for AI Reports.** The Functions app is a back-channel service with API-key auth; AI Reports is a user-facing service with JWT auth. Different shape, different host. (The Functions app does inform our scheduling and email-sending patterns, but it's not the right runtime for the AI chat workload.)

The AI Reports project is fundamentally about *making the right answer easy and the wrong answer hard*. The recommendations above flow from that principle, now grounded in what the existing analytics DB actually looks like rather than what I'd previously imagined we'd build from scratch.
