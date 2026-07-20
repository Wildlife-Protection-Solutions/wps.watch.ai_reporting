# AI Reports — Requirements Document (v2)

**Status:** Draft — for wpsWatch dev team review.
**Supersedes:** [`ai-reports-requirements.md`](./ai-reports-requirements.md) (v1, 2026-05-05). v1 is preserved unchanged for comparison.
**Authors:** Synthesis of Stage 1 codebase exploration (now v2 — [`ai-reports-exploration-notes-v2.md`](./ai-reports-exploration-notes-v2.md)) + four parallel persona reviews (backend, security, user, data — data has a v2 at [`perspectives/data-v2.md`](./perspectives/data-v2.md)).
**Sources:** [`ai-reports-exploration-notes-v2.md`](./ai-reports-exploration-notes-v2.md), [`ai-reports-requirements-delta.md`](./ai-reports-requirements-delta.md), [`perspectives/backend.md`](./perspectives/backend.md), [`perspectives/security.md`](./perspectives/security.md), [`perspectives/user.md`](./perspectives/user.md), [`perspectives/data-v2.md`](./perspectives/data-v2.md), Eric's "WPS Agentic Conservation Model & Roadmap" deck, Jira ticket text (verbatim).
**Date:** 2026-05-14.

> **How to read this document.** Sections 1–10 are the requirements proper. Section 11 lists open questions the dev team needs to resolve before the design doc. Section 12 is a glossary written for the design council and any reader who is new to MCP / RAG / text-to-SQL terminology. File-path citations point to one of four repos: `wps.watch.api`, `wps.watch.web`, `wpswatch.reporting` (the analytics-DB schema), and `wps.watch.functions.scheduledreporting` (the Azure Functions app).

> **What changed from v1.** v1 was written before two repos (`wpswatch.reporting` and `wps.watch.functions.scheduledreporting`) were known to the doc author. The discovery materially changes the architecture story: there is no read replica; there is an existing reporting database; there is an existing scheduled-email Functions app. v2 corrects the affected sections. Numbered requirements are renumbered with a `-V2-` infix where they replace a v1 item; new requirements use the same scheme. A mapping appendix is included at the end.

---

## 1. Background and motivation

### What exists today (corrected)
wpsWatch's reporting story has more moving parts than v1 captured. The full picture:

**Frontend (unchanged from v1).** A Reports menu in `wps.watch.web/src/components/common/Header.tsx` with two role-gated branches:
- System Admins (resource `AdminReport`) see seven Looker Studio dashboards opened in new tabs: Camera Inventory, Deployed Camera Battery Level, Photo Count by Camera, Region & Site Totals with Deployment, SIM Contract Renewal, SIM Prepaid Data, Top 10 Offline (`Header.tsx:582–630`).
- Regular users (`UserReport` — `OrgAdmin`, `IncidentManager`, `Viewer`, `GpsViewer`) see three iframe-embedded reports (`Header.tsx:731–816`, `IFrameComponent.tsx:14–70`).
- Volunteers see no Reports menu, and the API's `UserReport` operation excludes them (`OperationRoles.cs:446–463`).
- The menu is disabled in multi-org mode (`cross-org-overview.md:14–25`).

**Backend (revised in v2).** The Looker dashboards are not powered by the wpsWatch API. They're powered by a **separate analytics database** `wps-sql-analytics-prod` on the same Azure SQL Server as the OLTP primary `wpswatch-prod`. The analytics DB is owned by the `wpswatch.reporting` repo and contains:
- **15 reporting views** including `vw_wpsWatchCameraInv`, `vw_wpsWatchRegionSiteTotals` (V1/V2/V3), `vw_wpsWatchSIMData`, `vw_wpsWatchDeployedCameraCount`, `vw_wpsWatchDeployedCameraBatteryLevel`.
- **7 stored procedures**: `sp_wpsWatchDeployedCameraCount`, `sp_wpsWatchDeployedCameraTrends`, `sp_wpsWatchSIMDataReport`, `sp_wpsWatchUserOrgUpdate`, plus three site-specific procs for Ujung Kulon National Park.
- **9 user-defined functions** including `tvf_wpsWatchRegionSiteTotals` (V1/V2/V3) and `tvf_wpsWatchCameraInv`.
- **10 aggregation tables** including `wpsWatchDeployedCameraCount`, `wpsWatchCameraTrends`, `wpsWatchSIMDataReport`, and the cache table `wpsWatchLookerUserOrg`.
- **29 external tables** that proxy reads to `wpswatch-prod` via SQL Server's elastic-DB-query mechanism (data source `MyElasticDBQueryDataSrc`). Each external table is selectively projected — for example the `dbo.Photo` external table omits `IsRemoved` so soft-deleted photos can't accidentally appear in reports.

**Refresh.** Four Azure Automation runbook PowerShell scripts (`wpswatch.reporting/Jobs/*.ps1`) connect as SQL user `wpswatch-looker` and call one stored proc each. Cadence is configured in the Azure Automation account, not in source — an open question (§11).

**Existing scheduled reporting (new in v2).** `wps.watch.functions.scheduledreporting` is an Azure Functions v4 (.NET 8 isolated) app running two timer-triggered functions:
- `ReportingFunction` — calls the wpsWatch API's `DeploymentReportController` (with the `Wps-Api-Key` header, `ApiKeyMiddleware.cs:52–106`), builds an HTML email body, and dispatches via SendGrid (US) / Mailjet (EU) / Brevo (EU alt). Subscriptions are site-level via `ScheduledReport` (one row per `Site`, comma-separated `EmailAddresses`, see `Wps.Watch.Data/Models/ScheduledReport.cs`).
- `NewUserOrgSyncFunction` — runs `sp_wpsWatchUserOrgUpdate`, then syncs deltas to BigQuery `wpsWatchUserOrgId.wpsUserOrg`. BigQuery is what Looker reads for row-level user-org filtering.

**The Reports controller v1 missed.** `wps.watch.api/Wps.Watch.Api/Controllers/DeploymentReportController.cs` exists. It exposes `GET /api/deploymentReport`, returning all sites with `ScheduledReport.Enabled == true` across **all** organizations (no per-user org filter). Auth is `[Authorize]` without `UserAuthorizationFilter` — i.e. service-to-service via `Wps-Api-Key`, not user-acting. Reads primary OLTP, not the analytics DB.

### Why change
Same operational reasons as v1: Looker dashboards can't combine filters, can't ask follow-ups, can't reach the four SysAdmin-only reports that field operators want, can't anchor data to operational context (site access notes, last visit). The morning sweep at 06:00 — "what broke overnight" — is the highest-leverage moment and Looker doesn't serve it well (see [`perspectives/user.md`](./perspectives/user.md)).

What's different in v2 is the **shape of the work**. v1 framed AI Reports as a greenfield service against a replica DB. v2 frames it as **a sibling service to the existing analytics DB and Functions app**, extending the same patterns where they apply. The MVP gets cheaper because much of the SQL is done.

### What the ticket asks for (verbatim, lightly trimmed)
> We should explore implementing MCP to be able to bring forward things like chat to SQL to be able to explore wpsWatch data with natural language. ... a layer above wpsWatch that allows users to explore data with natural language queries. ... This service would be using our replica DB (or even a separate mirror DB) ... `wpswatchprodreplica.database.windows.net`. ... Initial scope could be setting it up as a replacement for the canned Looker Studio reports we have available from the Reports menu. ... A longer term goal would be to allow users to save their own custom queries (which would be saving the underlying SQL query, not the Claude text prompt).

> **Note for the design council:** the ticket's `wpswatchprodreplica` target does not exist in production. v2 reads from the existing analytics DB `wps-sql-analytics-prod` instead. This is a clarification of the ticket's intent — same goal (offload reads from the OLTP), different mechanism (the team's existing pattern).

### Where this fits in Eric's roadmap
Unchanged from v1. Eric's deck positions chat-to-SQL as **Foundation** of a three-phase MCP vision (Foundation → Mission Control UX → bounded physical agency). "Augment, do not replace" remains the core principle. Translated to AI Reports: the LLM is never an authorization decision-maker, the operator always sees what query ran, saved queries remain deterministic SQL.

---

## 2. Goals and non-goals

### Goals

**Phase 1 (MVP).**
- Replace the existing canned Looker reports with an in-product page in wpsWatch that runs the equivalent reports through a new, separate service.
- Mirror today's role gating exactly: `AdminReport` sees the seven SysAdmin reports; `UserReport` sees the three iframe reports today; Volunteers excluded.
- Keep MVP single-org, matching today's Reports behaviour.
- Cover the existing report set with measurable parity (same numbers as Looker for the same parameters).
- **(v2 addition) Extend, don't duplicate.** AI Reports' new database objects live in the existing `wpsWatch.*` schema in `wps-sql-analytics-prod`, alongside the Looker-facing objects. New runbooks follow the existing PowerShell-in-Azure-Automation pattern.

**Phase 2.**
- Free-form chat ("ask wpsWatch a question") gated to `AdminReport` initially, opened to `UserReport` once the eval set is mature.
- File export (PDF, CSV, DOCX, XLSX, KML, GeoJSON, PNG) of report and chat results.
- Cross-org reporting (the new feature becomes the first cross-org reporting surface in wpsWatch).
- **(v2 split) Scheduled email digest of named queries.** Two sub-goals:
  - **FR-48a:** add a new timer + email template inside `wps.watch.functions.scheduledreporting`, reusing the SendGrid/Mailjet/Brevo plumbing. Global schedule (the existing `TIMER_FREQUENCY` model). Low effort.
  - **FR-48b:** per-org / per-user local-time delivery (e.g. 05:45 in each org's timezone). Requires a redesign of the schedule model beyond a single global timer. Larger effort, may slip to phase 3.

**Phase 3.**
- Save and re-run custom SQL queries with manifest-pinned schema validation.
- Share saved queries within an org (and, possibly, across orgs).
- Diff-against-last-run on saved queries so silent answer drift is caught early.
- **(v2 addition) Subscription-to-saved-query** as a unified replacement for site-level `ScheduledReport`. Per-user, per-saved-query, per-cadence subscriptions. May fold into the existing `ScheduledReport` table or replace it — design decision deferred.

### Non-goals
- No write paths to the wpsWatch DB. The service is read-only.
- No direct calls to the wpsWatch API for end-user data. (Back-channel `Wps-Api-Key` calls are fine — see §7.)
- No live-operational answers. "Is anyone in zone 7 right now?" type questions are out of scope; the LLM only reports captured photos / past events.
- No GPS coordinates exposed to the LLM, even via coarsened views, in MVP. (See §6 and [`perspectives/data-v2.md` §3](./perspectives/data-v2.md).)
- No sharing of saved queries across orgs in MVP. Single-org saved queries only.
- No mobile native app. The new page is a responsive React surface.
- No live ground truth features (presence detection, surveillance of staff, APU patrol recommendations).
- No rewriting of existing Looker dashboards in place. The existing iframe surface stays until the new page is at parity; cutover is a separate decision.
- **(v2 addition) No new separate mirror / warehouse DB.** Extend the existing analytics DB.
- **(v2 addition) No Azure SQL read replica.** Doesn't exist in production today; not justified by AI Reports alone.
- **(v2 addition) No BigQuery in the AI Reports critical path.** BigQuery's role is Looker user-org filtering. AI Reports reads from the analytics DB directly.
- **(v2 addition) No replacement of the existing weekly deployment-status email.** That feature continues to ship from `ReportingFunction`. AI Reports' digest is additive.

---

## 3. User stories

Unchanged from v1. Drawn from [`perspectives/user.md` §1](./perspectives/user.md), tagged by phase. See [v1 §3](./ai-reports-requirements.md#3-user-stories) for the full list (US-01 through US-25). The most important call-out from the user perspective is unchanged: the morning sweep at 06:00 is the highest-leverage moment of the day, and the SysAdmin-only `Top 10 Offline` report should plausibly be opened to `UserReport` — a permission-model question for the design council.

---

## 4. Functional requirements

Numbered for traceability. Where a v1 requirement is materially revised, the v2 number includes `-V2-`. Where a v1 requirement carries forward unchanged, I list it under the v1 number. A mapping appendix is at the end.

### 4.1 Service architecture and surface (phase 1)

- **FR-V2-01.** A new ASP.NET Core 8 service `Wps.Watch.AiReports` is deployed as a sibling to the existing `wps.watch.functions.scheduledreporting` app, in the same Azure tenancy, sharing the analytics-DB connection and Key Vault. **It does not import any code from the Functions app.** It does not make outbound HTTP calls to the wpsWatch API for end-user data flow. Back-channel `Wps-Api-Key` calls are permitted only if a clear need arises and is approved in the design doc. (Supersedes v1 FR-01.)
- **FR-02.** The service exposes a REST API for the React app: `/api/reports`, `/api/reports/{id}/run`, `/api/saved-queries`, `/api/chat` (phase 2), `/api/exports/{token}` (phase 2).
- **FR-03.** The service additionally exposes an MCP server (`/mcp`, SSE transport) for use by the service's own LLM host loop. The MCP tool catalogue and the REST handlers share a single underlying handler core.
- **FR-04.** A new page is added to the wpsWatch React app at `/ai-reports`. The Reports menu in `Header.tsx` is updated to link to this page **alongside** the existing Looker links during the parity period; the existing Looker links are removed once parity is achieved (decision deferred to a separate switchover review).

### 4.2 Authentication and authorization (phase 1)

- **FR-05.** Service validates wpsWatch JWTs locally with the shared HS256 `JwtKey` from Azure Key Vault. Fallback: a new `/api/auth/introspect` endpoint on the wpsWatch API.
- **FR-V2-06.** The service computes the user's authorized organization list **per request** by reading `wpsWatch.wpsWatchLookerUserOrg` from the analytics DB. (Falls back to a live cross-DB read of `OrganizationUserRole` if the cache is older than 5 minutes.) The cache is maintained every 2 minutes by `NewUserOrgSyncFunction` (per `example.settings.json:6`). Replaces v1 FR-06.
- **FR-07.** Role checks go through a shared `Wps.Watch.Authorization` package extracted from `Wps.Watch.Api/Authorization/`. The package contains `Operation<T>`, `OperationRoleMapping<T>`, `RoleNames`, and the operation definitions. No constants duplicated.
- **FR-08.** Volunteers receive HTTP 403 from every endpoint except `/health`. Automated test required.
- **FR-09.** Roles `PhotoUploader` and `PhotoTagger` receive HTTP 403 — the new service mirrors the existing `UserReport` membership in `OperationRoles.cs:446–463`.
- **FR-V2-10.** The service refuses queries with HTTP 503 when measured **analytics-DB freshness** exceeds a per-object threshold. The freshness metric is `now - MAX(completed_utc)` from a new table `wpsWatch.wpsWatch_job_run_log` (DR-V2-08). Threshold defaults: 6 hours for "warn," 48 hours for "hard fail," configurable per object in the manifest. Replaces v1 FR-10 (which used "replica lag").

### 4.3 Canned reports — MVP (phase 1)

- **FR-V2-11.** The seven SysAdmin Looker reports and the three regular-user reports are exposed as parameterized tool calls **wrapping the existing `wpsWatch.*` views and procs**. Specific mapping:

  | Report | Backing object |
  |---|---|
  | Camera Inventory | `wpsWatch.vw_wpsWatchCameraInv` (existing) |
  | Deployed Camera Battery Level | `wpsWatch.vw_wpsWatchDeployedCameraBatteryLevel` (existing) |
  | Photo Count By Camera (last month) | `wpsWatch.vw_wpsWatchDeployedCameraCount` (existing) |
  | Photo Count By Camera (custom range) | New `wpsWatch.fct_ai_photo_count_daily` + view (DR-V2-18) |
  | Region & Site Totals with Deployment | `wpsWatch.vw_wpsWatchRegionSiteTotalsV3` (existing, V3 is current per [data-v2 §5](./perspectives/data-v2.md)) |
  | SIM Contract Renewal | `wpsWatch.vw_wpsWatchSIMData` (existing) |
  | SIM Prepaid Data | `wpsWatch.vw_wpsWatchSIMData` (existing — surfaces `EstimatedGBRemaining` per the existing 0.0001 GB/photo heuristic) |
  | Top 10 Offline | New `wpsWatch.sp_ai_TopOfflineDeployments(@OrganizationId, @ThresholdHours)` — the existing `wpsWatchCameraTrends` is org-level, not device-level |

  Replaces v1 FR-11.
- **FR-12.** (Carries forward.) The three regular-user reports are exposed via the same mechanism.
- **FR-13.** Each canned report is presented in the React page as a card with name, description, parameter form, and Run button. No LLM in this flow.
- **FR-14.** Output is a tabular result set with sortable/filterable table, accompanied by parameters used, timestamp of execution, and **the data-source's last-refresh time** (read from `wpsWatch_job_run_log`).
- **FR-15.** Parity-tested against the equivalent Looker dashboard. Release-gate test asserts row-count and key-aggregate parity for representative parameter sets. **(v2 caveat)** The Looker dashboard's current data-source version is an open question for Region & Site Totals — confirm V1/V2/V3 before parity-testing (DR-V2-30).

### 4.4 Free-form chat (phase 2)

- **FR-16.** Chat panel gated to `AdminReport` initially; expansion decided after the eval set (FR-23) is mature.
- **FR-17.** LLM hosted server-side (Anthropic Messages API). Browser does not hold an Anthropic key. Key in Azure Key Vault.
- **FR-V2-18.** LLM has access to a defined MCP tool surface: `list_canned_reports`, `run_canned_report(report_id, parameters)`, `describe_ai_view(view_name)`, `query_ai_views(sql)`, `save_query(name, sql, parameters_template)`, `run_saved_query(query_id, parameters)`. **The `query_ai_views` tool only accepts SQL referencing `wpsWatch.vw_ai_*` objects** — not the Looker-facing `vw_wpsWatch*` objects, and not `dbo.*` at all. Manifest entries cover only the `vw_ai_*` set (per [data-v2 §2](./perspectives/data-v2.md)). Replaces v1 FR-18.
- **FR-V2-19.** Generated SQL passes a parsed-AST validator that rejects: anything other than a single `SELECT`; references to anything outside the `wpsWatch.vw_ai_*` allowlist; system schemas (`sys.*`, `INFORMATION_SCHEMA.*`); `EXEC`, `xp_*`, `sp_OA*`, `OPENROWSET`, `OPENDATASOURCE`, `BULK INSERT`, `DBCC`, `WAITFOR`; `READ UNCOMMITTED` or `NOLOCK` hints. Replaces v1 FR-19.
- **FR-V2-20.** Every `vw_ai_*` view requires an `organization_id` filter. The orchestrator injects `WHERE organization_id = @org_id` (or asserts the LLM did) before execution. **No `SESSION_CONTEXT` mechanism.** Per [data-v2 §7](./perspectives/data-v2.md), explicit parameters are simpler and match the existing pattern. Replaces v1 FR-20 and v1 S-MUST-09 through S-MUST-11.
- **FR-21.** Database string content in LLM context wrapped in `<data>...</data>` markers; system prompt explicitly says content inside these markers is data, not instructions.
- **FR-22.** No follow-up tool call may be triggered solely by content from a previous tool's output.
- **FR-23.** Nightly regression suite of golden question→expected-row-count pairs. Coverage: all canned reports plus 20 representative ad-hoc questions chosen from §3.
- **FR-24.** Every chat answer renders with: question, executed SQL (collapsed/expandable), row count, time window, filters applied, **data-source last-refresh time**, views referenced, latency. Plain text or sanitised markdown only.

### 4.5 File export (phase 2)

- **FR-25 to FR-29.** Unchanged from v1. CSV / PDF / DOCX / XLSX / KML / GeoJSON / PNG. Export pipeline is net-new — **not** an extension of the Functions app's email-rendering (the Functions app sends HTML email bodies only, no file attachments today).

### 4.6 Saved queries (phase 3)

- **FR-V2-30.** `wpsWatch.saved_query` and `wpsWatch.saved_query_run` tables added to the analytics DB (per [data-v2 §7](./perspectives/data-v2.md)). Schema:

  ```sql
  CREATE TABLE wpsWatch.saved_query (
      saved_query_id     INT IDENTITY(1,1) PRIMARY KEY,
      organization_id    INT NOT NULL,
      user_id            NVARCHAR(128) NOT NULL,
      name               NVARCHAR(256) NOT NULL,
      description        NVARCHAR(2000) NULL,
      sql_text           NVARCHAR(MAX) NOT NULL,
      manifest_version   NVARCHAR(64) NOT NULL,
      objects_referenced NVARCHAR(MAX) NULL,  -- JSON array
      created_utc        DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
      last_run_utc       DATETIME2 NULL,
      last_run_status    NVARCHAR(32) NULL,
      INDEX IX_saved_query_org_user (organization_id, user_id)
  );
  ```

  Saved queries are single-org for MVP. Replaces v1 FR-30, DR-24, DR-26.
- **FR-31 to FR-39.** Unchanged from v1 in spirit, but every reference to a "new dedicated saved-query DB" becomes "the `wpsWatch.saved_query` table in the analytics DB."

### 4.7 Behavioural and trust requirements

- **FR-40 to FR-45.** Unchanged from v1.
- **FR-V2-44.** Replaces v1 FR-44: data-freshness badge. When the latest refresh of the data source backing a report is older than the warning threshold (default 6 hours per FR-V2-10), the answer carries a "data refreshed N hours ago" badge.

### 4.8 Connectivity and graceful degradation (phase 2)

- **FR-46.** Timeouts surfaced explicitly.
- **FR-47.** Saved queries cache their last result client-side for use during outages.
- **FR-V2-48a.** **Scheduled email digest — Functions-app extension.** A new timer-triggered Function is added to `wps.watch.functions.scheduledreporting` that runs on a global timer (env-var-controlled), iterates a new `wpsWatch.ai_report_subscription` table, executes each subscription's saved query, renders an HTML email, and dispatches via the existing SendGrid/Mailjet/Brevo plumbing. Subscriptions are per-user-per-saved-query in MVP. Splits v1 FR-48 (low-effort piece).
- **FR-V2-48b.** **Per-org local-time delivery.** Optional later enhancement. Requires per-subscription `schedule_cron` plus a 24/hr timer that dispatches subscriptions whose local-time-now matches `schedule_cron`. Larger work; may slip to phase 3. Splits v1 FR-48 (higher-effort piece).

---

## 5. Non-functional requirements

- **NFR-01 (latency).** P50 chat response time < 8 seconds; P95 < 20 seconds. Canned reports P50 < 1 second, P95 < 5 seconds.
- **NFR-02 (throughput).** ≤25 concurrent queries across service, ≤5 per user.
- **NFR-03 (availability).** 99.5% during reserve operating hours (05:00–22:00 local per user). Best-effort outside. Hard dependencies: analytics DB and Anthropic API availability (canned reports degrade gracefully if Anthropic is down — they don't use it).
- **NFR-V2-04 (data-source freshness).** No replica → no replica lag. The freshness metric is `wpsWatch_job_run_log.completed_utc` per refresh proc. Per-object thresholds: warn at 6 hours, hard fail at 48 hours (configurable in manifest). Replaces v1 NFR-04.
- **NFR-05 (cost ceiling).** Anthropic API spend capped via Anthropic-side limits and per-user query rate-limiting (60 queries/min/user). Cost-profile metadata in manifest flags expensive queries before they run.
- **NFR-V2-06 (observability).** Standard App Insights `RequestTelemetry` / `DependencyTelemetry`, plus custom events `AiReportQuery`, `AiReportDenied`, `AiReportExport`. **(v2 addition)** Analytics-DB Query Store dashboards (already enabled? verify per [data-v2 DR-V2-14](./perspectives/data-v2.md)) include the `ai_reports_reader` SQL principal. Audit data goes to a separate restricted store (see §6).
- **NFR-07 (deployability).** Containerized, deployed alongside the existing wpsWatch infrastructure (dev → QA → prod, Azure App Service or AKS).
- **NFR-V2-08 (key/secret management).** All secrets (`JwtKey`, Anthropic API key, DB connection string for `ai_reports_reader`) in Azure Key Vault. Service authenticates to Key Vault and the analytics DB via managed identity (`DefaultAzureCredential`). **(v2 specific)** AI Reports does **not** inherit the existing runbook scripts' bad credential hygiene (plain-text passwords in source). The runbook credential issue is flagged as a tangential ticket (§11 OQ-V2-19).

---

## 6. Security and privacy requirements

The v1 MUST/SHOULD/NICE-TO-HAVE list from [`perspectives/security.md` §10](./perspectives/security.md) **mostly carries forward unchanged**. Specific revisions for v2:

### Revised MUSTs

- **S-V2-MUST-02.** Replaces v1 S-MUST-02 (`ApplicationIntent=ReadOnly`). The connection target is the analytics DB `wps-sql-analytics-prod`, which is read-only by convention. The connection string does not need `ApplicationIntent=ReadOnly`. Instead: the `ai_reports_reader` SQL principal has `db_datareader` on `wps-sql-analytics-prod` only, with explicit `DENY SELECT` on the sensitive columns enumerated in [data-v2 §3](./perspectives/data-v2.md).
- **S-V2-MUST-03.** Replaces v1 S-MUST-03. The principal `ai_reports_reader` has `db_datareader` on `wps-sql-analytics-prod` only, with `DENY` on: `dbo.Site (DasToken, DasBaseUrl, SmartToken, SmartBaseUrl, SmartIntegrateToken, SmartIntegrateBaseUrl, SlackWebhookUrl, CiscoSustainableImpactToken, CiscoSustainableImpactBaseUrl, ZendoApiKey, ZendoUrl, ZendoPath, Latitude, Longitude)`; `dbo.Deployment (Latitude, Longitude)`; `dbo.Device (PhoneNumber, SimCardNumber, ImeiNumber)`. **Caveat: column-level DENY on elastic-query external tables has historical quirks** ([data-v2 DR-V2-29](./perspectives/data-v2.md)). Verify before relying solely on `DENY`; fall back to "AI principal has no access to `dbo.*` at all, only `wpsWatch.vw_ai_*` views" if needed.
- **S-V2-MUST-09 through S-V2-MUST-11.** Replaces v1 S-MUST-09 through S-MUST-11 (`SESSION_CONTEXT`). The org-scope mechanism is **explicit `@organization_id` parameters** threaded by the orchestrator from the user's JWT, validated against `wpsWatchLookerUserOrg`. Reporting views require the `organization_id` filter; the orchestrator/middleware asserts it before execution. (Per [data-v2 §7](./perspectives/data-v2.md).)
- **S-V2-MUST-14.** Replaces v1 S-MUST-14 (`replica lag exceeds threshold → HTTP 503`). The new freshness gate is on `wpsWatch_job_run_log`: HTTP 503 when last refresh of any backing object older than `hard_fail_after_hours` (default 48). Warn when older than `stale_after_hours` (default 6).
- **S-V2-MUST-15.** Replaces v1 S-MUST-15. Refresh-age telemetry replaces replica-lag telemetry. Emit `data_source_freshness_hours` per request.
- **S-V2-MUST-28.** Replaces v1 S-MUST-28. Sensitive credential / PII columns explicitly enumerated in [data-v2 §3](./perspectives/data-v2.md) are absent from every `wpsWatch.vw_ai_*` view. Verified by inspection of existing views: `vw_wpsWatchCameraInv`, `vw_wpsWatchDeployedCameraCount`, `vw_wpsWatchSIMData`, `vw_wpsWatchDeployedCameraBatteryLevel` already do not project GPS. The pattern continues for the new `vw_ai_*` set.

### Carried forward unchanged

S-MUST-01, S-MUST-04 through S-MUST-08, S-MUST-12, S-MUST-13, S-MUST-16 through S-MUST-27, S-MUST-29 through S-MUST-37. Read these in [`perspectives/security.md` §10](./perspectives/security.md).

### SHOULD and NICE-TO-HAVE

All v1 SHOULDs and NICE-TO-HAVEs carry forward unchanged. Read in [`perspectives/security.md`](./perspectives/security.md).

### Security-tangential finding (new in v2)

Plain-text SQL passwords are committed in the four runbook scripts (`wpswatch.reporting/Jobs/*.ps1` line 4 in each). Mixed casing on the user (`wpswatchLooker` vs. `wpswatchlooker`). Out of scope for AI Reports but flagged as a separate ticket: move credentials to Azure Automation variables or Key Vault, standardize the username, set `Encrypt=True`. The AI Reports service must not inherit this pattern — see NFR-V2-08.

---

## 7. Permission model

### Phase 1 (MVP) — unchanged
- System Admins (`AdminReport`) see the seven reports.
- `UserReport` users (`OrgAdmin`, `IncidentManager`, `Viewer`, `GpsViewer`) see the three.
- `Volunteer`, `PhotoUploader`, `PhotoTagger` see no AI Reports page.
- Single-org behaviour — current org from JWT.

### How the new service learns and enforces this (revised in v2)
- JWT validated locally with shared `JwtKey` (FR-05).
- User's organization list looked up per request from `wpsWatch.wpsWatchLookerUserOrg` (the existing cache table, [data-v2 §0 glossary](./perspectives/data-v2.md)) on the analytics DB. Falls back to a live cross-DB read of `dbo.OrganizationUserRole` (external table) if the cache row's age exceeds 5 minutes. Replaces v1's recommendation to query `OrganizationUserRole` directly on every request — the existing cache is sufficient for the freshness budget.
- Role checks via the shared `Wps.Watch.Authorization` package, consuming `UserReport` and `AdminReport` operations from `OperationRoles.cs:446–463`.

### Existing service-to-service pattern (new context for v2)
The wpsWatch reporting stack already uses the `Wps-Api-Key` header for service-to-service auth (between `ReportingFunction` and `DeploymentReportController`). This pattern is **not used by AI Reports** for end-user data flow — AI Reports is user-acting, so JWT is right — but it is the established pattern if AI Reports ever needs to make a back-channel call to the wpsWatch API. Use Wps-Api-Key for that case (if it arises), not invented auth.

### Phase 2+
- Cross-org reporting: AI Reports becomes the first cross-org reporting surface in wpsWatch. Saved queries become potentially multi-org; design decision deferred.
- The SysAdmin/UserReport split question (should `Top 10 Offline` etc. be opened to OrgAdmin?) remains an open design-council question; MVP does not change today's gating.

---

## 8. Architecture options considered

### Transport / protocol layer — unchanged
Hybrid REST + MCP, single handler core. Browser POSTs `/api/chat`; service holds the conversation, calls Anthropic with MCP tools attached, executes tools against the analytics DB, streams the answer back via SSE. See [`perspectives/backend.md` §1](./perspectives/backend.md) for full reasoning.

### Query generation strategy — unchanged
Hybrid: predefined stored-proc / view-call catalogue for canned reports; gated text-to-SQL for chat (phase 2). See [`perspectives/backend.md` §2](./perspectives/backend.md).

### Resolving "save the SQL not the prompt" — unchanged
Catalogue saves store `(report_id, parameters)`; generated saves store the literal SQL string with values templatised to parameters. The LLM is not consulted on re-run.

### Authentication option — unchanged
Shared HS256 `JwtKey` via Azure Key Vault. Fallback: `/api/auth/introspect` endpoint on the wpsWatch API. RS256/Entra migration tracked as longer-term posture (S-SHOULD-05).

### Data-source option (revised in v2)
v1 framed this as "replica vs. mirror vs. hybrid." v2 reframes against actual production:

1. **Status quo (live OLTP)** — sensitive columns directly exposed, heavy reads compete with writes. **Rejected.**
2. **Azure SQL read replica** — doesn't exist; would require a Business Critical tier upgrade or read-scale-out config; would duplicate the existing analytics-DB pattern; would only be used by AI Reports. **Rejected.**
3. **Curated mirror / warehouse** — already have one (`wps-sql-analytics-prod`); building a second is duplication. **Rejected.**
4. **Extend the existing analytics DB** — **recommended.**

The analytics DB already has the curated schema, the external-tables mechanism for OLTP isolation, the scheduled-refresh pattern (runbooks), the user-org scoping cache (`wpsWatchLookerUserOrg`), and a dedicated read-only login pattern. AI Reports adds new `vw_ai_*` views, two new `fct_ai_*` agg tables, one new `sp_ai_*` proc, and the saved-query storage tables — all in the same `wpsWatch.*` schema. The new objects use a `*_ai_*` infix to distinguish them from Looker-facing objects. See [`perspectives/data-v2.md` §1](./perspectives/data-v2.md) for full reasoning.

### Service location (new in v2)
v1 implicitly assumed the AI Reports service was a fresh repo. With four reporting-adjacent repos now in scope, the location question matters:

- **Option A — new repo, new Azure resource.** Clean separation, three reporting-adjacent codebases to maintain.
- **Option B — extend `wps.watch.functions.scheduledreporting`.** Azure Functions doesn't fit MCP/SSE/long-running chat well. **Rejected.**
- **Option C — new ASP.NET Core service repo, positioned explicitly as a sibling to the Functions app**. Shares the analytics-DB connection, shares Key Vault, shares the `Wps-Api-Key` pattern for any back-channel needs. **Recommended.**

C and A are nearly identical operationally; the difference is whether the doc and the deployment story make the architectural relationship explicit. C is better for that reason. The new repo would be named `wps.watch.aireports` (or similar) to fit the existing naming convention.

---

## 9. Data layer requirements

The v1 data-layer list is superseded by [`perspectives/data-v2.md` §9](./perspectives/data-v2.md). DR-V2-01 through DR-V2-30. Read that document for the full numbered list; the requirements doc references them by number.

Highlights:

- **DR-V2-01.** Read source: `wps-sql-analytics-prod` (not a replica, not a mirror).
- **DR-V2-02.** Extend the existing `wpsWatch.*` schema; no new schema.
- **DR-V2-06.** Canned reports wrap existing `vw_wpsWatch*` / `sp_wpsWatch*` objects where they exist; build `sp_ai_*` only where there's a gap.
- **DR-V2-08.** New `wpsWatch.wpsWatch_job_run_log` table powers the freshness gate.
- **DR-V2-18 / DR-V2-19.** Two new aggregation tables: `fct_ai_photo_count_daily` (must-build) and `fct_ai_device_event_hourly` (should-build, columnstore-indexed).
- **DR-V2-21.** Refresh via Azure Automation runbooks (existing pattern), not elastic jobs.
- **DR-V2-22.** Org-scoping via explicit `@organization_id` parameters, not `SESSION_CONTEXT`.
- **DR-V2-25.** Saved-query storage in the analytics DB, not a separate dedicated DB.
- **DR-V2-29.** Verify column-level DENY behaviour on external tables before assuming it as the security boundary.
- **DR-V2-30.** Confirm which `vw_wpsWatchRegionSiteTotals` version Looker is using (V1/V2/V3) for parity testing.

### What v1 got wrong about the data, captured by data-v2:
- **SIM Prepaid Data source** — v1 recommended adding `Device.PrepaidDataUsedGb` to OLTP and syncing from Twilio. v2 recommendation flipped: the existing `sp_wpsWatchSIMDataReport` uses a `PrepaidDataLimitGb - (0.0001 × photo_count_in_billing_cycle)` heuristic. Match the existing approach; label it honestly in the LLM manifest as "approximate, based on photo volume." See [data-v2 §8.1](./perspectives/data-v2.md).
- **Top 10 Offline threshold** — v1 recommended 24 hours as a guess. v2 confirms 24 hours is correct: `sp_wpsWatchDeployedCameraTrends` uses 1440 minutes (i.e. 24h) as the offline threshold today. See [data-v2 §8.2](./perspectives/data-v2.md).

---

## 10. Phasing proposal

### Phase 1 (MVP) — *smallest credible end-to-end slice*

**The tracer bullet (revised in v2):** still "replace Top 10 Offline end-to-end." But the data shape is closer than v1 thought:

- The existing `sp_wpsWatchDeployedCameraTrends` already encodes the 24-hour threshold (1440 minutes) at org-rollup level.
- A new device-level proc `sp_ai_TopOfflineDeployments(@OrganizationId, @ThresholdHours)` reads from the external `Deployment` table directly with a `LastEventDateTimeUtc` filter. ~30-line stored proc.
- New page card, parameter form (org from JWT, threshold defaulting to 24), table render.
- Auth wiring via shared `Wps.Watch.Authorization` package, `wpsWatchLookerUserOrg` lookup, role check against `UserReport`/`AdminReport`.

**Once the tracer is in:**
- Wrap the remaining six SysAdmin reports + the three regular-user reports as tool calls against existing views (FR-V2-11).
- Build `fct_ai_photo_count_daily` for finer-grained Photo Count questions.
- Build `fct_ai_device_event_hourly` for trend questions beyond the org-rollup.
- Add the new `vw_ai_*` views for ad-hoc readiness (preps phase 2 surface).
- Parity-test against the live Looker dashboards.
- Confirm `vw_wpsWatchRegionSiteTotals` version in production (DR-V2-30).

**Revised effort estimate (engineer-weeks):**

| Piece | v1 estimate | v2 estimate | Why |
|---|---|---|---|
| New service skeleton, replica wiring, auth re-impl | ~2 weeks | ~1.5 weeks | Analytics DB connection is simpler than wiring up a non-existent replica. |
| `Wps.Watch.Authorization` extraction | ~0.5 weeks | ~0.5 weeks | Unchanged. |
| `reporting` SQL schema with `SESSION_CONTEXT` org filter for all ten reports | 3–6 weeks (dominating) | **~1.5–3 weeks** | Most reports already have a backing object. The new work is wrapping, plus 1 new proc and 2 new agg tables. No `SESSION_CONTEXT`. |
| Parity tests, observability, audit, deploy | ~2 weeks | ~2 weeks | Unchanged. |
| React page, parameter forms, table render, CSV export | ~2 weeks | ~2 weeks | Unchanged. |
| **Phase 1 total** | **10–14 weeks** | **~7.5–10 weeks** | ~30% reduction driven by the data layer. |

### Phase 2 — chat, full export, cross-org

- Free-form chat panel gated to `AdminReport`.
- MCP tool catalogue + `query_ai_views` against the new `vw_ai_*` set.
- AST validator + parameter-injection middleware.
- Anthropic host loop with streaming, tool-use orchestration, audit.
- Eval suite (FR-23).
- File export pipeline (all formats; net-new work).
- **FR-V2-48a** scheduled email digest as a Functions-app extension. *Effort drops* because SendGrid/Mailjet/Brevo plumbing exists.
- **FR-V2-48b** per-org local-time delivery. *Net-new work*, may slip to phase 3.
- Cross-org reporting.

**Revised effort: ~12–22 engineer-weeks** (vs. v1's 14–24), with the FR-48a piece doing the heavy lifting on the digest side.

### Phase 3 — saved custom queries

- `wpsWatch.saved_query` / `saved_query_run` storage (DR-V2-25).
- Save / list / edit / delete UI, three-layer validation, deprecation handling, smoke runner, diff banner.
- Sharing within an org with running-user scope.
- **Subscription model unification (v2 addition):** decide whether the existing `ScheduledReport` table is replaced by a per-user `ai_report_subscription` table or extended. Owner: design council.

**Revised effort: ~6–10 engineer-weeks.** Unchanged from v1.

---

## 11. Open questions for the human owner

The v1 list mostly stands. Items revised, resolved, or added below.

### Resolved in v2 (no longer open)

- ~~v1 OQ-3 — SIM Prepaid Data source~~ — **resolved.** The existing `sp_wpsWatchSIMDataReport` uses a heuristic estimation; no Twilio sync is needed. See [data-v2 §8.1](./perspectives/data-v2.md). MVP matches the existing approach and labels the column honestly in the manifest.
- ~~v1 OQ-4 — "Top 10 Offline" threshold~~ — **resolved.** 24 hours (1440 minutes) is the existing convention. See [data-v2 §8.2](./perspectives/data-v2.md).
- ~~v1 OQ-5 — Mirror DB beyond the replica~~ — **resolved.** No mirror, no replica; extend the existing analytics DB.
- ~~v1 OQ-7 — Primary store for saved queries~~ — **resolved.** Analytics DB, `wpsWatch.saved_query` table.

### Still open from v1

- **OQ-1.** The SysAdmin-only report list — are the seven reports identified the complete `AdminReport` set, or are there additional reports not surfaced in `Header.tsx`?
- **OQ-2.** Should regular users (`UserReport`) gain access to the four currently-SysAdmin reports? Field-ops case is strong for at least `Top 10 Offline`. Design-council question.
- **OQ-6.** JWT migration to RS256/Entra — is this on the roadmap?
- **OQ-8.** Cross-org reporting timing — when does Reports come into scope for the wpsWatch cross-org migration?
- **OQ-9.** Scheduled email digest cadence and deliverability — phase 2 or phase 3?
- **OQ-10.** Anthropic vs. another model provider.
- **OQ-11.** Existing Looker SQL — who has Looker admin access to extract the underlying queries? *(Partially resolved by reading the `wpswatch.reporting` schema, but the Looker side's specific data-source URLs and parameter mappings are still useful for parity testing.)*
- **OQ-12.** Existing iframe-based Reports cutover plan — when does the existing menu get removed?

### New in v2

- **OQ-V2-13.** **Repo layout for the AI Reports service.** Option C recommended (new repo, sibling to `wps.watch.functions.scheduledreporting`). Confirm with the dev team.
- **OQ-V2-14.** **Runbook refresh cadence.** What is the actual configured cadence for each of the four runbooks today (the scripts set `CommandTimeout = 600s` but the schedule lives in Azure Automation)? Needed to calibrate the freshness gate (DR-V2-15) and to inform the default `stale_after_hours` per object.
- **OQ-V2-15.** **Which `vw_wpsWatchRegionSiteTotals` version is in Looker?** V1, V2, or V3? Needs a click-through in the Looker UI to confirm before the parity test runs.
- **OQ-V2-16.** **BigQuery sync deprecation.** If AI Reports replaces Looker for the canned reports, do we still need `NewUserOrgSyncFunction`'s sync to BigQuery? *(Yes, for any remaining Looker usage; potentially deprecate when fully migrated. Not a phase-1 decision.)*
- **OQ-V2-17.** **Subscription model unification.** The existing `ScheduledReport` table is site-level (one row per site, comma-separated emails). AI Reports' saved-query subscription model is per-user-per-query. Should we extend `ScheduledReport` or build a new `ai_report_subscription` table? Owner: design council.
- **OQ-V2-18.** **`DeploymentReportController` and AI Reports relationship.** The existing endpoint returns all enabled scheduled reports across all orgs (no per-user filter, service-to-service auth via `Wps-Api-Key`). AI Reports does not need to call it. Confirm this in the design doc so no one connects them later.
- **OQ-V2-19.** **(Security-tangential)** The runbook PowerShell scripts contain plain-text SQL passwords (`wpswatch.reporting/Jobs/*.ps1`). Out of scope for AI Reports but should be ticketed independently.
- **OQ-V2-20.** **Analytics-DB deployment automation.** The `wpswatch.reporting` repo appears to be SSMS-generated SQL files with no obvious CI deploy pipeline (per [data-v2 §6](./perspectives/data-v2.md)). The contract-test gate (DR-V2-23) presupposes CI-driven deployment. Is automating the analytics-DB deployment a prerequisite for the AI Reports work, or can the gate run advisory-only?

---

## 12. Glossary

(For the design council and any reader new to the terminology.)

- **MCP (Model Context Protocol):** an Anthropic-published protocol for exposing tools and resources to an LLM client over stdio or SSE. Standardisation on top of tool-use.
- **Tool use:** an LLM API pattern where the model emits a structured request to invoke a named tool; the host executes it; the model continues.
- **RAG (Retrieval-Augmented Generation):** at query time, retrieve relevant context (e.g. schema snippets) and inject into the LLM prompt. Used here for ad-hoc text-to-SQL.
- **Text-to-SQL:** LLM-generated SQL from a natural-language question.
- **Predefined parameterised queries:** fixed catalogue of SQL templates the LLM picks from and parameterises.
- **OLTP:** online transaction processing — the primary `wpswatch-prod` database, normalised for inserts/updates.
- **Analytics DB:** the separate `wps-sql-analytics-prod` database on the same Azure SQL Server. Holds the `wpsWatch.*` reporting schema.
- **External table (elastic DB query):** a SQL Server object in the analytics DB that proxies reads to a table in `wpswatch-prod`. Defined via `CREATE EXTERNAL TABLE … WITH (DATA_SOURCE = [MyElasticDBQueryDataSrc])`. Behaviorally: live data, cross-DB latency on every read.
- **Aggregation table (agg table):** a normal SQL table in `wpsWatch.*` populated by a stored procedure on a schedule. Cheap to read.
- **Azure Automation runbook:** Azure's hosted scheduler/script-runner. Four PowerShell runbooks in `wpswatch.reporting/Jobs/` refresh the existing agg tables.
- **`wpsWatchLookerUserOrg`:** a cache table mapping `(UserId, OrganizationId, OrgIDUUID, Email)`. Maintained by `sp_wpsWatchUserOrgUpdate`, synced to BigQuery every 2 minutes by `NewUserOrgSyncFunction`. AI Reports reads it for user-org bootstrap.
- **Semantic layer / data manifest:** the JSON description of tables/columns the LLM consumes. Versioned in source. Defines what the model "knows."
- **`SESSION_CONTEXT`:** a SQL Server key/value store scoped to a session. v1 proposed using it for row-level org filtering; v2 rejected in favour of explicit parameters.
- **Snapshot isolation / RCSI:** SQL Server isolation levels that let readers see a consistent point-in-time view without locks. v2 uses RCSI on the analytics DB.
- **`Wps.Watch.Authorization`:** the new shared .NET project (proposed) extracting role/operation constants and `OrganizationUserRole` projection from `Wps.Watch.Api/Authorization/`.
- **`Operation<TEntity>` / `Can<TEntity>(...)`:** the typed authorization API used by wpsWatch. Reused unchanged by AI Reports.
- **`AdminReport` / `UserReport`:** the two `Operation<ScheduledReport>` operations defined in `OperationRoles.cs:446–463`. Volunteers, PhotoUploaders, PhotoTaggers excluded.
- **Cross-org / multi-org:** wpsWatch's mode where a user can see data across all the organizations they belong to. Out of scope for AI Reports MVP.
- **Looker Studio:** Google's BI tool. The current Reports menu opens Looker Studio dashboards. The dashboards read from the analytics DB and apply per-user row-level filtering via the BigQuery sync.
- **BigQuery:** Google Cloud's analytics database. Used here only for the Looker user-org filter table (`wpsWatchUserOrgId.wpsUserOrg`). AI Reports does not read or write BigQuery.
- **`Wps-Api-Key`:** the service-to-service auth header used by `ReportingFunction` to call `DeploymentReportController`. Not used by AI Reports for end-user data flow.
- **Anthropic Messages API:** Anthropic's HTTP API for chat completions with tool-use support. Hosted server-side in AI Reports, not in the browser.

---

## Appendix A — v1 → v2 requirement mapping

For traceability against tickets and design-council references that already cite v1 numbers.

| v1 ID | v2 ID | Change |
|---|---|---|
| FR-01 | FR-V2-01 | Service is positioned as sibling to existing repos. |
| FR-02–FR-05 | (unchanged) | — |
| FR-06 | FR-V2-06 | Read `wpsWatchLookerUserOrg` cache instead of `OrganizationUserRole` directly. |
| FR-07–FR-09 | (unchanged) | — |
| FR-10 | FR-V2-10 | Freshness gate based on `wpsWatch_job_run_log`, not replica lag. |
| FR-11 | FR-V2-11 | Wrap existing views/procs; specific mapping table added. |
| FR-12–FR-17 | (unchanged) | — |
| FR-18 | FR-V2-18 | Tool surface targets `vw_ai_*` only. |
| FR-19 | FR-V2-19 | AST validator updated for `vw_ai_*` allowlist. |
| FR-20 | FR-V2-20 | Org-scope via explicit parameter, not `SESSION_CONTEXT`. |
| FR-21–FR-29 | (unchanged) | — |
| FR-30 | FR-V2-30 | Saved-query storage in analytics DB. |
| FR-31–FR-43 | (unchanged) | — |
| FR-44 | FR-V2-44 | Freshness badge based on refresh age. |
| FR-45–FR-47 | (unchanged) | — |
| FR-48 | FR-V2-48a + FR-V2-48b | Split: Functions-app extension vs. per-org local-time delivery. |
| NFR-01–NFR-03 | (unchanged) | — |
| NFR-04 | NFR-V2-04 | Refresh-age, not replica lag. |
| NFR-05 | (unchanged) | — |
| NFR-06 | NFR-V2-06 | Analytics-DB Query Store added. |
| NFR-07 | (unchanged) | — |
| NFR-08 | NFR-V2-08 | Explicit note that runbook-script credential pattern is not inherited. |
| S-MUST-02 | S-V2-MUST-02 | `ApplicationIntent=ReadOnly` dropped; DENY-based instead. |
| S-MUST-03 | S-V2-MUST-03 | DENY column list explicit. |
| S-MUST-09–11 | S-V2-MUST-09–11 | Explicit parameter, not `SESSION_CONTEXT`. |
| S-MUST-14 | S-V2-MUST-14 | Refresh-age threshold. |
| S-MUST-15 | S-V2-MUST-15 | Refresh-age telemetry. |
| S-MUST-28 | S-V2-MUST-28 | Verified against actual views; new sensitive columns added (Cisco, Zendo). |
| All other S-MUST / S-SHOULD / S-NICE | (unchanged) | — |
| DR-01–DR-30 | DR-V2-01–DR-V2-30 | Renumbered and revised — see [`perspectives/data-v2.md` §9](./perspectives/data-v2.md). |

---

*End of v2 requirements document.*
