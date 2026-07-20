# AI Reports — Stage 1 Exploration Notes (v2)

**Purpose:** Ground-truth findings across all four wpsWatch reporting-adjacent repos to support the AI Reports requirements doc. Citations are file paths within those repos.

**Supersedes:** [`ai-reports-exploration-notes.md`](./ai-reports-exploration-notes.md) (v1). v1 is preserved unchanged for comparison.

**Date assembled:** 2026-05-09.

**Repos covered (v2 adds two):**
- `wps.watch.api` (ASP.NET Core 8 API) — was in v1
- `wps.watch.web` (React) — was in v1
- `wpswatch.reporting` (pure T-SQL: views, procs, UDFs, runbooks) — **new in v2**
- `wps.watch.functions.scheduledreporting` (Azure Functions v4, .NET 8 isolated) — **new in v2**

---

## What changed from v1

Two retractions and one reframing in the "Surprises" section, plus two new sections (§11 the analytics DB architecture, §12 the existing scheduled-reporting Functions). All file-path citations from v1 still hold; new ones are added.

### Retractions

- **v1 surprise #4** ("No replica connection string is configured anywhere in the codebase") — **technically true but misleading and now retracted.** The replica DB named in the Jira ticket (`wpswatchprodreplica.database.windows.net`) does not appear in any of the four repos. But the team has solved the equivalent problem differently — see §11 — using a separate analytics database `wps-sql-analytics-prod` on the same SQL Server, fed via cross-DB external tables.
- **v1 surprise #5** ("The DB schema has no reporting views, no stored procs, and no pre-aggregation tables. All reporting is OLTP today") — **wrong, retracted.** True only of the primary OLTP database (`wpswatch-prod`). The analytics database (`wps-sql-analytics-prod`, owned by the `wpswatch.reporting` repo) has 15 views, 7 stored procedures, 9 user-defined functions, 10 aggregation tables, and 29 external tables. This is the backend of the existing Looker dashboards. The v1 wording came from looking only at `wps.watch.api` and `wps.watch.web`.

### Reframed surprises (v2 set)

> ⚠️ **Surprises that change the shape of the project** — read first. Detail follows.
>
> 1. **The seven Looker reports the prompt listed (Camera Inventory, Battery Level, Photo Count, Region & Site Totals, SIM Contract Renewal, SIM Prepaid Data, Top 10 Offline) are *all* gated by the `AdminReport` operation — i.e. they are the System Admin set.** Regular users (`UserReport` operation) see only **three** iframe-embedded reports: Camera Battery Level, Camera Deployment Detail, Photo Count By Camera. ([`Header.tsx:582–730` vs `731–816`](#-5-reports-menu--frontend-wiring-the-seam-being-replaced))
> 2. **JWT validation uses a symmetric `JwtKey` (HS256), not Azure AD/Entra (RS256).** A separate service can only validate tokens by sharing the same secret key, or by routing through the API. ([`Startup.cs:242–261`](#-3-auth-surface-for-an-external-service))
> 3. **Reports today is single-org by design.** Both Reports menu items are disabled with a tooltip in multi-org mode. The new feature inherits this constraint for MVP per the human owner's call. ([`cross-org-overview.md:14–25`](#-5-reports-cross-org-status))
> 4. **(v2) The Jira ticket's `wpswatchprodreplica` target appears not to exist in production.** No code in any of the four repos references it. The team's actual reporting story is a **separate analytics database** `wps-sql-analytics-prod` on the same SQL Server, fed by cross-DB external tables. (§11.)
> 5. **(v2) An extensive `wpsWatch.*` reporting schema already exists** in `wps-sql-analytics-prod` — 15 views, 7 stored procs, 9 UDFs, 10 aggregation tables. It backs the Looker dashboards today. (§11.) Several of the canned reports the new feature would replace already have most of their data shape produced server-side.
> 6. **(v2) Scheduled deployment-status emails are already in production.** `wps.watch.functions.scheduledreporting` is an Azure Functions v4 (.NET 8 isolated) app that sends weekly HTML emails via SendGrid (US) / Mailjet (EU) / Brevo (EU alt), pulling data via `Wps-Api-Key` from the wpsWatch API's `DeploymentReportController`. Subscriptions are site-level via the `ScheduledReport` table. (§12.)
> 7. **(v2) Looker's row-level user-org filtering is fed by a SQL → BigQuery sync.** `OrganizationUserRole (OLTP)` → `sp_wpsWatchUserOrgUpdate` → `wpsWatchLookerUserOrg (analytics DB)` → `NewUserOrgSyncFunction` (in the Functions app) → BigQuery `wpsWatchUserOrgId.wpsUserOrg` → Looker. The original "iframe + URL params" framing in v1 §5 was incomplete.
> 8. **Volunteers are excluded from Reports at the API layer (`UserReport` role list omits them) and at the UI layer (no Reports menu render path).** Confirmed. ([`OperationRoles.cs:446–463`](#-2-roles-and-report-visibility))
> 9. **(v2 — security)** Four runbook PowerShell scripts in `wpswatch.reporting/Jobs/` contain a **plain-text SQL password** committed to the repo (`Password=J6J^XVPS9-h@a&#SS`). Mixed-casing on the user (`wpswatchLooker` / `wpswatchlooker`). This is a separate issue from AI Reports — flagged for its own ticket. (§13.)

---

## 1. Permission and org-scoping model

*Unchanged from v1.* Re-read the v1 §1 — `IUserContextService`, the `OrganizationUserRole` table as the source of truth, the `X-MultiOrgMode` header read at `UserAuthorizationFilter.cs:44`. No changes in v2.

---

## 2. Roles and report visibility

*Unchanged from v1.* `RoleNames.cs:7–52`, `OperationRoles.cs:446–463`, the frontend role-derivation in `reducer.ts:83–85`.

---

## 3. Auth surface for an external service

*Unchanged for end-user JWT auth.* v1's recommendations (share `JwtKey`, or add `/api/auth/introspect`, or migrate to RS256) all still apply.

**(v2 addition)** The existing **service-to-service** auth pattern in production for the reporting stack is the `Wps-Api-Key` header (`Wps.Watch.Api/ApiKeyMiddleware.cs:52–106`). The Functions app uses it to call `DeploymentReportController` (see §12). API keys are always single-org (`UserAuthorizationFilter.cs:86`) and not user-aware, so this pattern is only relevant for AI Reports' back-channel calls (if any), not for end-user requests.

---

## 4. Code-sharing potential between the API and a new .NET service

*Unchanged from v1.* `Wps.Watch.Data` is importable. `Wps.Watch.Business` is importable. `Wps.Watch.Api` is not. Authorization classes should be extracted to a shared `Wps.Watch.Authorization` package.

**(v2 addition)** The Functions app `wps.watch.functions.scheduledreporting` chose **not** to import shared projects. It is "Standalone solution with no project references to `Wps.Watch.Api`, `Wps.Watch.Data`, etc." This is a precedent worth noting: a sibling service in the same Azure tenancy without code-coupling is a workable pattern at WPS. The AI Reports service could follow the same shape (no shared code) or take the data-architect's recommendation to extract `Wps.Watch.Authorization`. **The Functions app's choice was likely informed by the fact that it only does back-channel work** — it doesn't need the typed `Operation<T>` machinery because it doesn't act on behalf of users. AI Reports does act on behalf of users, so the case for the shared package is stronger.

---

## 5. Reports menu — frontend wiring (the seam being replaced)

*Unchanged from v1 for the menu structure.* `Header.tsx:582–630` for the AdminReport set (7 reports), `731–816` for the UserReport set (3 reports). The `IFrameComponent.tsx:14–70` filter-by-query-param mechanism for the embed.

**(v2 addition)** The Looker filter story extends past the iframe — see §11 (the analytics DB is the data source the iframe loads) and §12 (the BigQuery sync that feeds Looker's row-level user-org filtering). The v1 phrase "no signed token" remains correct in the sense that no JWT or HMAC is signed *for Looker* by wpsWatch — but the per-user filtering is enforced via the BigQuery sync, not just the URL params.

---

## 6. Schema surface for the seven reports (primary OLTP)

*Unchanged from v1 for the OLTP table inventory.* `Device`, `Deployment`, `Photo`, `Site`, `Organization`, `Region`, `SimCardDataPlan`, `PrepaidDataLimitTerm`. The join chains. The sensitive columns.

**(v2 retraction)** v1 §6 ended with "**Gap:** No `DataUsedGb` / `DataRemainingGb` column on `Device`." Correct as a statement about the EF model, but the data architect's v2 reading of `wpswatch.reporting/StoredProcedures/wpsWatch.sp_wpsWatchSIMDataReport.StoredProcedure.sql` reveals **the existing Looker SIM report estimates usage as `PrepaidDataLimitGb - (0.0001 × photo_count_in_billing_cycle)`** — i.e. a heuristic from photo volume, no carrier API involved. The original "Twilio Super SIM API" hypothesis was wrong. See [`perspectives/data-v2.md` §8.1](./perspectives/data-v2.md) for the corrected recommendation.

**(v2 retraction)** v1 §6 said the "Top 10 Offline" report "relies on `Deployment.LastEventDateTimeUtc` ... no explicit `IsOnline` column" with the threshold "undefined." Still true at the OLTP level, but in the analytics DB the actual report is produced by `sp_wpsWatchDeployedCameraTrends` (note: name is `DeployedCameraTrends`, not `CameraTrends`) — read that proc to find the operational threshold.

---

## 7. Stored procedures and views (PRIMARY OLTP database only)

*v1 §7 retracted.* The original wording — "Searched `Wps.Watch.Sql/Migration/` and `Wps.Watch.Sql/` ... No `CREATE VIEW`. No `CREATE PROCEDURE`. No pre-aggregation tables." — was only true of the primary OLTP database (`wpswatch-prod`). It missed the entire reporting layer in the analytics DB. See §11.

The corrected v2 statement: **the primary OLTP DB has no reporting views or stored procedures**; the separate analytics database does.

---

## 8. Replica realities

**(v2 reframing.)** v1's "zero hits in source code" for `wpswatchprodreplica` / `replica` / `ReadOnly` / `ApplicationIntent` is still accurate across all four repos — including the two added in v2. **The ticket's replica appears not to exist in production.** The team's actual reporting architecture (§11) uses a different mechanism (a sibling analytics DB with cross-DB external tables) that achieves the same goal — offloading read load from the primary.

For the AI Reports requirements doc this means: do not write the requirements as if the replica exists. The requirements should target the analytics DB or, if a true replica is desired, the doc needs to call that out as net-new Azure infrastructure work.

---

## 9. Open questions surfaced during exploration (v2 set)

Replaces v1 §9.

1. **`SIM Prepaid Data` source-of-truth: resolved (v2).** The existing `sp_wpsWatchSIMDataReport` estimates usage from photo volume, no carrier API involved. The AI Reports MVP should match the existing approach and label it honestly in the LLM-facing manifest ("approximate, based on photo volume").
2. **"Offline" threshold: partly resolved (v2).** The existing `sp_wpsWatchDeployedCameraTrends` encodes some threshold logic. Read the proc body to extract the actual value. (Once known, propagate to the requirements doc's FR for Top 10 Offline.)
3. **Regular-user report set vs SysAdmin set:** unchanged from v1 — the seven reports the prompt named are SysAdmin-only. The three regular-user reports are a strict subset. Should MVP replace both sets, or only one to start?
4. **Legacy `UserOrganization` references in Looker:** still relevant. v1 noted the cross-org migration switched the source of truth from `UserOrganization` to `OrganizationUserRole`. The `sp_wpsWatchUserOrgUpdate` proc (in `wpswatch.reporting`) reads `OrganizationUserRole` directly — confirmed by the data-architect's reading. No legacy `UserOrganization` reference observed in the analytics DB's user-org sync.
5. **JWT migration to RS256/Entra:** unchanged from v1.
6. **(v2 new) Runbook refresh cadence:** the four PowerShell scripts in `wpswatch.reporting/Jobs/` set a 10-minute command timeout but do not encode a schedule. Cadence is configured in Azure Automation outside the script. **The team needs to confirm:** what is the actual refresh cadence today (hours? daily?), and is it the same for all four procs? This number is needed to set the AI Reports freshness gate threshold.
7. **(v2 new) Which `vw_wpsWatchRegionSiteTotals` version is in Looker?** Three versions exist (V1, V2, V3). The parity test in the MVP tracer bullet needs to target the one Looker actually points at.
8. **(v2 new) AI Reports repo layout:** new repo (option A), extend Functions app (option B, not recommended — wrong shape for MCP/SSE/long-running chat), or new repo positioned explicitly as sibling to the existing two (option C, recommended). Decision needed before phase 1 starts.
9. **(v2 new — security-tangent) The runbook scripts commit a plain-text SQL password.** See §13. Needs a separate ticket; doesn't block AI Reports.

---

## 10. File-citation index (quick reference, v2)

API:
- `Wps.Watch.Api/Authorization/IUserContextService.cs` (interface)
- `Wps.Watch.Api/Authorization/UserContextService.cs` (impl, single-vs-multi-org logic)
- `Wps.Watch.Api/Authorization/UserAuthorizationFilter.cs` (per-request hydration, X-MultiOrgMode read)
- `Wps.Watch.Api/Authorization/OperationRoles.cs` (UserReport / AdminReport mapping at 446–463)
- `Wps.Watch.Api/Authorization/Operations/RoleNames.cs` (role constants)
- `Wps.Watch.Api/ApiKeyMiddleware.cs` (`Wps-Api-Key` handling)
- `Wps.Watch.Api/Startup.cs:135–168, 242–261` (DbContext + JWT setup)
- **(v2 new)** `Wps.Watch.Api/Controllers/DeploymentReportController.cs` — the single Reports controller. `GET /api/deploymentReport` returns enabled scheduled reports across all orgs; called by `ReportingFunction`. `[Authorize]` but no `UserAuthorizationFilter` — auth is by `Wps-Api-Key` from the Functions app.
- **(v2 new)** `Wps.Watch.Api/Dtos/ScheduledReportDto.cs`, `ScheduledReportEmailAddressDto.cs` — DTOs returned by the controller.
- `wps.watch.api/docs/business-rules/cross-org-data-model.md`
- `Wps.Watch.Data/Context/WpsDbContext.cs`
- `Wps.Watch.Data/Models/Device.cs`, `Deployment.cs`, `Photo.cs`, `Site.cs`, `Organization.cs`, `Region.cs`, `SimCardDataPlan.cs`, `PrepaidDataLimitTerm.cs`
- **(v2 new)** `Wps.Watch.Data/Models/ScheduledReport.cs` — one-to-one with `Site`; fields: `ScheduledReportId`, `ReportType` (int, unused), `ReportInterval` (int, unused), `Enabled`, `EmailAddresses` (CSV string), `MaxRows`, `RowVersion`. **No user-level subscription model.**
- `Wps.Watch.Data.csproj`, `Wps.Watch.Business.csproj` (deps)
- `Wps.Watch.Sql/Migration/WpsWatch-2.1.9.sql`, `WpsWatch-2.1.10.sql`

Web:
- `wps.watch.web/src/components/common/Header.tsx:582–816` (Reports menus)
- `wps.watch.web/src/components/common/IFrameComponent.tsx:14–70` (Looker iframe + filter passing)
- `wps.watch.web/src/App.tsx:72` (`/reports/:id` route)
- `wps.watch.web/src/context/reducer.ts:6–21, 83–85` (UserState + role derivation)
- `wps.watch.web/src/api/users/usersApi.ts:206` (`getOrganizationUuid`)
- `wps.watch.web/src/components/common/DashboardComponent.tsx:354–358, 835` (savedQueries pattern)
- `wps.watch.web/docs/business-rules/cross-org-overview.md:14–25` (Reports out-of-scope for cross-org)

**(v2 new repo)** `wpswatch.reporting/` — pure T-SQL, owned by Looker analytics. Highlights:
- `wpswatch.reporting/Readme.md` — describes the analytics DB and external-tables architecture.
- `wpswatch.reporting/StoredProcedures/wpsWatch.sp_wpsWatchDeployedCameraCount.StoredProcedure.sql`
- `wpswatch.reporting/StoredProcedures/wpsWatch.sp_wpsWatchDeployedCameraTrends.StoredProcedure.sql`
- `wpswatch.reporting/StoredProcedures/wpsWatch.sp_wpsWatchSIMDataReport.StoredProcedure.sql` (heuristic SIM estimate — see §9.1)
- `wpswatch.reporting/StoredProcedures/wpsWatch.sp_wpsWatchUserOrgUpdate.StoredProcedure.sql` (maintains `wpsWatchLookerUserOrg`)
- `wpswatch.reporting/Views/` — 15 views including `vw_wpsWatchCameraInv`, `vw_wpsWatchRegionSiteTotals` (V1/V2/V3), `vw_wpsWatchSIMData`, `vw_wpsWatchDeployedCameraCount`, …
- `wpswatch.reporting/UserDefinedFunctions/wpsWatch.tvf_wpsWatchRegionSiteTotals.UserDefinedFunction.sql` (~273 lines of multi-CTE hierarchy)
- `wpswatch.reporting/Tables/wpsWatch.wpsWatchDeployedCameraCount.Table.sql` (the aggregation-table schema)
- `wpswatch.reporting/Tables/ExternalTables/` — 29 external tables pointing at `wpswatch-prod` via data source `MyElasticDBQueryDataSrc`.
- `wpswatch.reporting/Jobs/wpsWatchDeployedCamera.ps1`, `wpsWatchCameraTrends.ps1`, `wpsWatchProdAnalSql.ps1`, `wpsWatchSimData.ps1` — Azure Automation runbook scripts. See §13 for the credential issue.

**(v2 new repo)** `wps.watch.functions.scheduledreporting/` — Azure Functions v4, .NET 8 isolated worker, containerized. Highlights:
- `Wps.Watch.Functions.Reporting.csproj` (runtime + deps)
- `Program.cs` — DI, HttpClient with `Wps-Api-Key`, EF `WpsAnalyticsDbContext` registration, App Insights wiring.
- `ReportingFunction.cs:66–322` — timer-triggered, pulls `/api/deploymentReport`, builds HTML email, sends via SendGrid (US) / Mailjet (EU) / Brevo (EU alt).
- `NewUserOrgsFunction.cs:58` — timer-triggered, calls `sp_wpsWatchUserOrgUpdate`, syncs to BigQuery `wpsWatchUserOrgId.wpsUserOrg`.
- `example.settings.json` — env vars including `TIMER_FREQUENCY`, `NEWUSER_SYNC_TIMER_FREQUENCY`, `WpsApi`, `WpsApiKey`, `SendGridApiKey`, `MJ_APIKEY_PUBLIC/PRIVATE`, `BrevoKey`, `EU_Service`, `BigQueryJsonSecret`.
- `Readme.md:24–29` — describes the two functions and the deliberate use of `Wps-Api-Key` for the API call.

---

## 11. The analytics-DB architecture (new in v2)

This section captures the architecture that v1 missed. It supersedes what v1 §7 ("no stored procs, no views") and v1 §8 ("no replica") said about the reporting story.

### Servers and databases

```
Azure SQL Server: wpswatch-prod.database.windows.net
├── wpswatch-prod                ← primary OLTP, written by wps.watch.api
└── wps-sql-analytics-prod       ← analytics DB, schema owned by wpswatch.reporting repo
```

The two databases live on the same SQL Server but are logically separate. Both are managed inside the same Azure resource and share the server-level managed identity.

### How the analytics DB sees the OLTP data

Via **SQL Server elastic database query** — a built-in feature that lets one Azure SQL database query another as if its tables were local. The mechanism:

- An "external data source" `MyElasticDBQueryDataSrc` (defined in `wpswatch.reporting`) points at `wpswatch-prod`.
- 29 "external tables" in the analytics DB (under `wpswatch.reporting/Tables/ExternalTables/`) are declared with `CREATE EXTERNAL TABLE … WITH (DATA_SOURCE = [MyElasticDBQueryDataSrc])`. Each external table mirrors a primary-DB table's schema and acts as a read-through proxy.
- Querying an external table issues a live cross-DB call to the primary. **No replication, no lag, no replica.** The price is per-query overhead — joins across many external tables can be expensive.

This is meaningfully different from:
- **Azure SQL geo-replication / read scale-out** (what the ticket assumed): asynchronous physical replication of the entire database to a different endpoint.
- **A separate mirror or warehouse** (what the v1 data perspective recommended evaluating): denormalized fact/dim tables fed by ETL.

It's a third pattern, and it's what's in production today.

### What lives in the analytics DB

| Kind | Count | Examples |
|---|---|---|
| Views (`Views/`) | 15 | `vw_wpsWatchCameraInv`, `vw_wpsWatchRegionSiteTotals` (V1/V2/V3), `vw_wpsWatchSIMData`, `vw_wpsWatchDeployedCameraCount`, `vw_wpsWatchOrphanedPhotos`, `vw_wpsWatchUjungKolonDetectionReport` |
| Stored procs (`StoredProcedures/`) | 7 | `sp_wpsWatchDeployedCameraCount`, `sp_wpsWatchDeployedCameraTrends`, `sp_wpsWatchSIMDataReport`, `sp_wpsWatchUserOrgUpdate`, plus site-specific ones for Ujung Kulon |
| User-defined functions (`UserDefinedFunctions/`) | 9 | `tvf_wpsWatchRegionSiteTotals` (V1/V2/V3, ~273 lines of CTEs), `tvf_wpsWatchDeployedCameraCount`, `tvf_wpsWatchOrphanedPhotos` |
| Aggregation tables (`Tables/`) | 10 | `wpsWatchDeployedCameraCount` (denormalised: OrganizationName, SiteName, DeviceName, counts, RunDateTime), `wpsWatchLookerUserOrg`, site-specific Ujung Kulon tables |
| External tables (`Tables/ExternalTables/`) | 29 | Mirror primary tables: Device, Deployment, Photo, Site, Organization, Region, OrganizationUserRole, … |

### The Looker user-org filter chain

This is the piece v1 §5 missed. Full chain:

1. The OLTP primary holds `OrganizationUserRole(UserId, OrganizationId, Role)` — the source of truth.
2. `sp_wpsWatchUserOrgUpdate` (in the analytics DB) reads `OrganizationUserRole` via external table and refreshes a denormalized cache: `wpsWatchLookerUserOrg`.
3. The Functions app's `NewUserOrgSyncFunction` runs `sp_wpsWatchUserOrgUpdate` on a timer (default cadence configured outside source — see open question 6), then **syncs the result to BigQuery** at `wpsWatchUserOrgId.wpsUserOrg`.
4. Looker data sources are configured to filter on the BigQuery table, enforcing per-user, per-org row visibility on top of the SQL data sources.

So when a wpsWatch user opens a Looker dashboard, the URL carries their org UUID + user ID (`IFrameComponent.tsx`), and Looker's runtime applies row-level filtering using the BigQuery sync as its access table.

### How this affects AI Reports

- The "build a new `reporting` schema on a replica" framing in the original v1 doc / v1 data perspective is wrong — the right shape is "extend the existing `wpsWatch.*` schema in `wps-sql-analytics-prod`" or add a parallel `wpsWatch_ai.*` / `ai_*` namespace. See [`perspectives/data-v2.md`](./perspectives/data-v2.md) for the recommendation.
- The user-org scoping for AI Reports can either reuse `wpsWatchLookerUserOrg` (cache, slightly stale) or read `OrganizationUserRole` live via external table. Both work; trade-offs are in [`perspectives/data-v2.md`](./perspectives/data-v2.md).
- AI Reports does **not** need to touch BigQuery. BigQuery's role is the Looker access table; AI Reports reads SQL directly.

---

## 12. The scheduled-reporting Functions app (new in v2)

This section captures what `wps.watch.functions.scheduledreporting` already does today.

### Shape

- Azure Functions v4, .NET 8 isolated worker, Docker-containerized (`Dockerfile` in repo, deployed via `.github/workflows/azure-deploy.yaml` to Azure App Service).
- Standalone solution (`Wps.Watch.Functions.Reporting.sln`, one project). **No project references** to `Wps.Watch.Api`, `Wps.Watch.Data`, or `Wps.Watch.Authorization` — loose coupling via HTTP and SQL only.
- DI configured in `Program.cs:22–54`: named HttpClient with `Wps-Api-Key`, `WpsAnalyticsDbContext` with retry policy, Serilog → App Insights.

### Functions

#### `ReportingFunction` (`ReportingFunction.cs:66`)
- Trigger: `[Function("ReportingFunction")]` + `TimerTrigger("%TIMER_FREQUENCY%")`. Schedule is an env var; dev default `*/2 * * * *` (every 2 minutes for testing). Production cadence not visible in this repo.
- What it does: HTTP `GET` to `${WpsApi}/api/deploymentReport` (Wps-Api-Key header). For each enabled scheduled report returned, builds an HTML table (deployment status per site — online/offline, battery %, last event, active issues, optional last-photo thumbnails as SAS URLs), then dispatches via SendGrid (default US) / Mailjet (EU) / Brevo (EU alt). EU routing toggled by `EU_Service` env var and `IsEu` flag on the report config (`ReportingFunction.cs:283–295`).
- Subscriber model: site-level (one `ScheduledReport` per `Site`, comma-separated `EmailAddresses`). Validates each email against the `Users` table to set `IsValidWpsUser` for template selection (separate HTML templates for wpsWatch users vs. external recipients, `ReportingFunction.cs:212–279`).
- **No file attachments.** No PDF, CSV, DOCX. Pure HTML email body.

#### `NewUserOrgSyncFunction` (`NewUserOrgsFunction.cs:58`)
- Trigger: `[Function("NewUserOrgSyncFunction")]` + `TimerTrigger("%NEWUSER_SYNC_TIMER_FREQUENCY%")`. Schedule env-var-controlled; dev default `*/2 * * * *`.
- What it does: opens a SQL transaction, calls `[wpsWatch].[sp_wpsWatchUserOrgUpdate]`, then iterates new rows into BigQuery `wpsWatchUserOrgId.wpsUserOrg` (project `wpswatch-looker-studio`). BigQuery auth: managed identity → Azure Key Vault → `BigQueryJsonSecret` (`NewUserOrgsFunction.cs:45–50`).

### What this means for AI Reports (FR-48 etc.)

The Functions app is well-suited to *adding* an AI Reports email digest as a new timer + template, **if** the digest schedule can be global (the existing `TIMER_FREQUENCY` env-var model). It is **not** well-suited to per-org local-time delivery without redesign. The current model is "one timer, fan-out to all enabled subscribers."

The HTML rendering / multi-provider mail dispatch / EU regional routing / `IsValidWpsUser` template branching are all wins to reuse. The export-to-PDF/CSV/DOCX pipeline is not present and would be net-new code.

### The "Reports controller" v1 missed

`DeploymentReportController.cs` (in the API repo) is the only Reports controller. Single endpoint:

```csharp
[HttpGet]
[ProducesResponseType(typeof(IEnumerable<ScheduledReportDto>), 200)]
public async Task<IActionResult> Search()
```

- Auth: `[Authorize]` but **no `UserAuthorizationFilter`** — i.e. not org-scoped. The Functions app calls it with a service-level `Wps-Api-Key`, gets all enabled reports across all orgs (`DeploymentReportController.cs:81–85`). This works because it's a back-channel data-fetch for system-internal use, not a user-facing endpoint.
- Reads from primary OLTP, not the analytics DB — queries `_wpsDbContext.Deployments` with `.Include(...)` on Site, Organization, Device, etc.
- Validates the report's email list against the `Users` table to flag valid vs. external recipients (lines 99–106).
- Returns up to `ScheduledReport.MaxRows` per site (line 67).

AI Reports does **not** need to call this controller. The shape of the data is different (deployment status snapshot vs. on-demand reporting). But the existence of this controller is the precedent the dev team should know about when discussing "the API has no reports endpoints" — it has one, just narrowly scoped.

---

## 13. Findings tangential to AI Reports (new in v2)

These don't block or shape AI Reports but should not be silently swallowed.

### Plain-text SQL credentials in the runbook scripts

All four scripts in `wpswatch.reporting/Jobs/` contain a hard-coded connection string with a plain-text password:

```
Data Source=wpswatch-prod.database.windows.net;Initial Catalog=wps-sql-analytics-prod;
Integrated Security=False;User ID=wpswatchLooker;Password=J6J^XVPS9-h@a&#SS;
Connect Timeout=60;Encrypt=False;TrustServerCertificate=False
```

(`wpswatch.reporting/Jobs/wpsWatchDeployedCamera.ps1:4`, `wpsWatchCameraTrends.ps1:4`, `wpsWatchProdAnalSql.ps1:4`, `wpsWatchSimData.ps1:4`.)

Issues:
1. The password is committed to source control.
2. The user is `wpswatchLooker` in some scripts, `wpswatchlooker` in others — mixed-casing on what is meant to be the same SQL principal. Likely harmless on case-insensitive SQL collation but a smell.
3. `Encrypt=False;TrustServerCertificate=False` is not the secure default on Azure SQL.

Recommendation (not for the AI Reports doc; for a separate ticket):
- Move credentials to Azure Automation variables or Key Vault.
- Standardise on managed identity where possible (the Azure Automation account can hold a managed identity that's granted `db_datareader` on the analytics DB).
- Rotate the password as part of the migration.
- Set `Encrypt=True` once cert validation is in place.

### Three versions of `vw_wpsWatchRegionSiteTotals`

V1, V2, V3 exist. Suggests historical iteration without a documented deprecation cycle. Not blocking, but worth a note for the data architect: the AI Reports manifest should pin to whichever version Looker is currently configured against, and the team should know that future schema evolution will likely create more such versions unless a manifest contract is adopted (see [`perspectives/data-v2.md`](./perspectives/data-v2.md) §6).

### `ScheduledReport.ReportType` and `ReportInterval` are unused

The `ScheduledReport.cs` model exposes both as ints but the Functions app reads neither. The site-level model is effectively "yes/no, with N max rows, sent to this email list, on the global timer." A future "different reports at different cadences" feature already has database columns waiting.

---

*End of v2 exploration notes.*
