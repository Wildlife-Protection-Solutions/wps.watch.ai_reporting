# AI Reports — Requirements Document

**Status:** Draft — for wpsWatch dev team review.
**Authors:** Synthesis of Stage 1 codebase exploration + four parallel persona reviews (backend, security, user, data).
**Sources:** [`ai-reports-exploration-notes.md`](./ai-reports-exploration-notes.md), [`perspectives/backend.md`](./perspectives/backend.md), [`perspectives/security.md`](./perspectives/security.md), [`perspectives/user.md`](./perspectives/user.md), [`perspectives/data.md`](./perspectives/data.md), Eric's "WPS Agentic Conservation Model & Roadmap" deck, Jira ticket text (verbatim).
**Date:** 2026-05-05.

> **How to read this document.** Sections 1–10 are the requirements proper. Section 11 lists open questions the dev team needs to resolve before the design doc. Section 12 is a glossary written for the design council and any reader who is new to MCP / RAG / text-to-SQL terminology. File-path citations point to either the wpsWatch API repo (`wps.watch.api`) or the wpsWatch web repo (`wps.watch.web`).

---

## 1. Background and motivation

### What exists today
wpsWatch surfaces canned reports through a Reports menu in the React app. The menu has two role-gated branches:

- **System Admins** (resource `AdminReport`) see seven Looker Studio dashboards opened in new tabs: Camera Inventory, Deployed Camera Battery Level, Photo Count by Camera, Region & Site Totals with Deployment, SIM Contract Renewal, SIM Prepaid Data, Top 10 Offline. (`Header.tsx:582–630`.)
- **Regular users** (resource `UserReport` — `OrgAdmin`, `IncidentManager`, `Viewer`, `GpsViewer`) see **three** iframe-embedded reports loaded through `IFrameComponent.tsx`: Camera Battery Level, Camera Deployment Detail, Photo Count By Camera. (`Header.tsx:731–816`, `IFrameComponent.tsx:14–70`.)
- **Volunteers** see no Reports menu and the API's `UserReport` operation excludes them. (`OperationRoles.cs:446–463`.)

Looker filtering today is a query-string handshake: `IFrameComponent.tsx` calls `getOrganizationUuid()` (`usersApi.ts:206`) and constructs an embed URL with `ds*.orgiduuid` and `ds*.userid` parameters. There is no `ReportsController`, no signed token, no Looker-side row-level security delegated by wpsWatch. The Reports menus are also disabled in multi-org mode, by design — Reports is currently single-org and is listed in `cross-org-overview.md:14–25` as out-of-scope for cross-org.

### Why change
The Looker dashboards are useful but rigid. Operators cannot combine filters across tables, cannot ask follow-up questions, cannot anchor data to operational context (site access notes, last visit, contact info), and cannot reach the four SysAdmin-only reports they actually want (especially Top 10 Offline — see [`user.md` §2](./perspectives/user.md)). The morning sweep at 06:00 — "what broke overnight, what needs a battery swap before the heat" — is the highest-leverage moment in the day, and Looker doesn't serve it well.

### What the ticket asks for (verbatim, lightly trimmed)
> We should explore implementing MCP to be able to bring forward things like chat to SQL to be able to explore wpsWatch data with natural language. ... a layer above wpsWatch that allows users to explore data with natural language queries. ... This service would be using our replica DB (or even a separate mirror DB) ... `wpswatchprodreplica.database.windows.net`. ... Initial scope could be setting it up as a replacement for the canned Looker Studio reports we have available from the Reports menu. ... A longer term goal would be to allow users to save their own custom queries (which would be saving the underlying SQL query, not the Claude text prompt).

### Where this fits in Eric's roadmap
Eric's "WPS Agentic Conservation Model & Roadmap" deck positions chat-to-SQL as **Foundation** — the first phase of a three-phase MCP-based AI orchestration vision (Foundation → Mission Control UX → bounded physical agency). The principle that survives across all phases is **"augment, do not replace"**: classical CV and rules continue sensing and classifying; the agent layer interprets, packages, and proposes — but never decides on the operator's behalf. Translated to AI Reports: the LLM must never be an authorization decision-maker, the operator must always be able to see what query ran, and saved queries must remain deterministic SQL that doesn't change unless their owner edits them.

---

## 2. Goals and non-goals

### Goals

**Phase 1 (MVP).**
- Replace the existing canned Looker reports with an in-product page in wpsWatch that runs the equivalent reports through a new, separate service.
- Mirror today's role gating exactly: `AdminReport` sees the seven SysAdmin reports; `UserReport` sees the three iframe reports today; Volunteers excluded.
- Keep MVP single-org, matching today's Reports behaviour.
- Cover the existing report set with measurable parity (same numbers as Looker for the same parameters).

**Phase 2.**
- Free-form chat ("ask wpsWatch a question") gated to `AdminReport` initially, opened to `UserReport` once the eval set is mature.
- File export (PDF, CSV, DOCX, XLSX, KML, GeoJSON, PNG) of report and chat results.
- Cross-org reporting (the new feature becomes the first cross-org reporting surface in wpsWatch).
- Scheduled email digest of named queries (the morning sweep at 05:45 local time).

**Phase 3.**
- Save and re-run custom SQL queries with manifest-pinned schema validation.
- Share saved queries within an org (and, possibly, across orgs).
- Diff-against-last-run on saved queries so silent answer drift is caught early.

### Non-goals
- **No write paths to the wpsWatch DB.** The service is read-only.
- **No direct calls to the wpsWatch API.** Per the ticket. The service holds its own auth and reads the replica directly.
- **No live-operational answers.** "Is anyone in zone 7 right now?" type questions are out of scope; the LLM only reports captured photos / past events. (See [`user.md` §3](./perspectives/user.md) "Question types I'd never trust an LLM with".)
- **No GPS coordinates exposed to the LLM**, even via coarsened views, in MVP. (See §6 and [`data.md` req 9](./perspectives/data.md).)
- **No sharing of saved queries across orgs in MVP.** Single-org saved queries only.
- **No mobile native app.** The new page is a responsive React surface; the field experience is browser-based.
- **No "live ground truth" features** (presence detection, surveillance of staff, APU patrol recommendations). (See [`user.md` §3](./perspectives/user.md).)
- **No rewriting of existing Looker dashboards in place.** The existing iframe surface stays until the new page is at parity; cutover is a separate decision.

---

## 3. User stories

Drawn from [`user.md` §1](./perspectives/user.md), tagged by phase. Each is the kind of question an ops-room operator at a 60,000-hectare reserve actually asks.

### Device health (the morning sweep) — **highest priority**
- **US-01 — Phase 1 (saved-template).** As an OrgAdmin, I want to see which cameras went offline overnight so I can dispatch the field team before the heat. Daily, 06:00, list with site name, last-seen time, last battery, region.
- **US-02 — Phase 1.** As an OrgAdmin, I want to see all cameras below 30% battery sorted worst-first, with site name and access notes.
- **US-03 — Phase 2 (chat).** As an OrgAdmin, I want to ask "any camera that hasn't sent a photo in 48 hours but was healthy 3 days ago?" — the "something just broke" question Looker can't ask.
- **US-04 — Phase 2 (chat + scheduled).** As an OrgAdmin, I want a phone alert if any camera in the western fence line drops below 20% before 14:00.

### Photo / detection volume (the rhythm check)
- **US-05 — Phase 1.** As an OrgAdmin, I want to see how many photos came in last night by site, compared to the same night last week (delta).
- **US-06 — Phase 1.** As an OrgAdmin, I want top 10 / bottom 10 cameras by photo volume this week.
- **US-07 — Phase 2 (chat).** As an OrgAdmin, I want to find sites that had a sudden spike in captures yesterday between 22:00 and 04:00, with sparkline of the previous 14 nights.
- **US-08 — Phase 2 (chat).** As an OrgAdmin, I want to slice last week's photos by tag (humans-on-foot vs vehicles vs livestock vs nothing-of-interest).
- **US-09 — Phase 2 (chat + map).** As an IncidentManager, I want every photo capture in the eastern block in the last 24h on a map, animated by time.

### Deployment coverage
- **US-10 — Phase 1.** As an OrgAdmin, I want to see which sites in my region don't currently have an active deployment.
- **US-11 — Phase 1 (today SysAdmin-only).** As an OrgAdmin, I want active-deployments-per-region-per-site so I can plan re-deployments. (See [§7 permission model](#7-permission-model) — this is the "give me access to the SysAdmin reports" ask.)

### SIM / connectivity
- **US-12 — Phase 1 (today SysAdmin-only).** As an OrgAdmin, I want to see which SIMs are renewing in the next 30 days, by carrier.
- **US-13 — Phase 2 (chat).** As an OrgAdmin, I want to find prepaid cameras likely to run out of data in the next week based on burn rate.
- **US-14 — Phase 2 (chat).** As an OrgAdmin, I want to slice cameras that went offline this week by failure cause (SIM/battery/hardware/unknown).

### Anomalies / one-offs
- **US-15 — Phase 2 (chat).** As an OrgAdmin, I want to find "ghost cameras": active deployments showing online but with zero photos in 30 days.
- **US-16 — Phase 2 (chat).** As an OrgAdmin, I want to find cameras that were redeployed within 14 days (volunteer swaps without telling ops).
- **US-17 — Phase 2 (chat + export).** As an OrgAdmin, I want to compare this month's detections vs the same month last year, by region — for the donor report.
- **US-18 — Phase 2 (chat).** As an IncidentManager, on the night of an incident at site 23, I want activity at the three nearest cameras in the 6h before/after.
- **US-19 — Phase 2 (chat).** As an OrgAdmin, I want every photo captured between 02:00 and 04:30 last Tuesday across the southern boundary cameras.

### Saved queries (phase 3)
- **US-20 — Phase 3.** As an OrgAdmin, I want to save "the morning sweep" (cameras < 30% OR offline > 24h with site name, region, access notes, 7-day photo sparkline) and run it with one click every morning.
- **US-21 — Phase 3.** As an OrgAdmin, I want to save "quiet cameras" (active deployments with photo count < 50% of 30-day average over the last 7 days) and run weekly.
- **US-22 — Phase 3.** As an OrgAdmin, I want a re-run of a saved query to show me a diff against the last run if the count changed materially.
- **US-23 — Phase 3.** As an OrgAdmin, I want to share saved queries with my colleague on the other shift (no silent reinventing).

### Cross-cutting
- **US-24 — Phase 2.** As any user, I want every chat answer to show the rows, the count, the time window, and the filters applied — so I can verify before I act on it.
- **US-25 — Phase 1.** As any user, I want the answer to be readable aloud over a radio (lists, not paragraphs).

---

## 4. Functional requirements

Numbered for traceability. All requirements are testable. Phase tags indicate when each must ship.

### 4.1 Service architecture and surface (phase 1)
- **FR-01.** A new ASP.NET Core 8 service `Wps.Watch.AiReports` is deployed independently of the wpsWatch API. The service makes no outbound HTTP calls to the wpsWatch API in production.
- **FR-02.** The service exposes a REST API for the React app (`/api/reports`, `/api/reports/{id}/run`, `/api/saved-queries`, `/api/chat` in phase 2, `/api/exports/{token}`).
- **FR-03.** The service additionally exposes an MCP server (`/mcp`, SSE transport) for use by the service's own LLM host loop. The MCP tool catalogue and the REST handlers share a single underlying handler core.
- **FR-04.** A new page is added to the wpsWatch React app at `/ai-reports` (or similar). The Reports menu in `Header.tsx` is updated to link to this page **alongside** the existing Looker links during the parity period; the existing Looker links are removed once parity is achieved (decision deferred to a separate switchover review).

### 4.2 Authentication and authorization (phase 1)
- **FR-05.** The service validates wpsWatch JWTs locally. The primary auth path is **shared HS256 `JwtKey` via Azure Key Vault** with the same configuration the wpsWatch API uses (`Wps.Watch.Api/Startup.cs:242–261`). Fallback: a new `/api/auth/introspect` endpoint on the wpsWatch API. (See [§8 architecture options](#8-architecture-options-considered).)
- **FR-06.** The service computes the user's authorized organization list **per request** by querying `OrganizationUserRole` on the replica (the same source `UserAuthorizationFilter.cs:36–38` uses). It does not cache org membership across requests.
- **FR-07.** Role checks go through a shared `Wps.Watch.Authorization` package extracted from `Wps.Watch.Api/Authorization/`. The package contains `Operation<T>`, `OperationRoleMapping<T>`, `RoleNames`, and the operation definitions (currently `OperationRoles.cs:446–463` for `UserReport` / `AdminReport`). No constants are duplicated.
- **FR-08.** Volunteers (`Volunteer` global role) receive HTTP 403 from every endpoint except `/health`. Verified by an automated test.
- **FR-09.** Roles `PhotoUploader` and `PhotoTagger` receive HTTP 403 — the new service mirrors the existing `UserReport` membership in `OperationRoles.cs:446–463` (`OrgAdmin`, `IncidentManager`, `Viewer`, `GpsViewer`).
- **FR-10.** The service refuses to serve queries with HTTP 503 when measured replica lag exceeds a configurable threshold (default 60 seconds).

### 4.3 Canned reports — MVP (phase 1)
- **FR-11.** All seven SysAdmin Looker reports are reimplemented as parameterized stored procedures in a new `reporting` schema on the replica: `usp_CameraInventory`, `usp_DeployedCameraBatteryLevel`, `usp_PhotoCountByCamera`, `usp_RegionAndSiteTotalsWithDeployment`, `usp_SimContractRenewal`, `usp_SimPrepaidData`, `usp_Top10Offline`. Source-of-truth tables are documented in [exploration notes §6](./ai-reports-exploration-notes.md#6-schema-surface-for-the-seven-reports).
- **FR-12.** The three regular-user reports (Camera Battery Level, Camera Deployment Detail, Photo Count By Camera) are reimplemented as parameterized stored procedures in `reporting`. Some are already covered by FR-11; the others are net-new.
- **FR-13.** Each canned report is presented in the React page as a card with a name, description, parameter form (date range, site filter, etc.), and a Run button. No LLM is in this flow.
- **FR-14.** Output of a canned report is a tabular result set rendered in the page, with a sortable/filterable table component, accompanied by the parameter values used and the timestamp of execution.
- **FR-15.** Each canned report execution is parity-tested against the equivalent Looker dashboard during MVP development; a release-gate test asserts row-count and key-aggregate parity for representative parameter sets.

### 4.4 Free-form chat (phase 2)
- **FR-16.** The page exposes a chat panel ("Ask a question") gated to users with `AdminReport` initially. Expansion to `UserReport` is decided after the eval set (FR-23) is mature.
- **FR-17.** The LLM runs server-side via the Anthropic Messages API. The browser does not hold an Anthropic API key. The Anthropic key is stored in Azure Key Vault.
- **FR-18.** The LLM has access to a defined MCP tool surface: `list_canned_reports`, `run_canned_report(report_id, parameters)`, `describe_view(view_name)`, `query_database(sql)`, `save_query(name, sql, parameters_template)`, `run_saved_query(query_id, parameters)`. No other tools.
- **FR-19.** The LLM may not query `dbo.*`. The `query_database` tool only accepts SQL referencing `reporting.*` views. Generated SQL passes a parsed-AST validator that rejects: anything that is not a single `SELECT`; references to system schemas (`sys.*`, `INFORMATION_SCHEMA.*`, `master.*`); `EXEC`, `xp_*`, `sp_OA*`, `OPENROWSET`, `OPENDATASOURCE`, `BULK INSERT`, `DBCC`, `WAITFOR`; `READ UNCOMMITTED` or `NOLOCK` hints. (See [`security.md` S-MUST-17](./perspectives/security.md), [`data.md` req 5](./perspectives/data.md).)
- **FR-20.** The user's authorized organization-id list is set on the database session via `sp_set_session_context @key=N'AllowedOrgIds', @value=..., @read_only=1` before any query runs. Reporting views reference `SESSION_CONTEXT(N'AllowedOrgIds')` in their `WHERE` clause. Connection-pool checkout always sets a fresh session context. (See [`security.md` S-MUST-09 to S-MUST-11](./perspectives/security.md).)
- **FR-21.** Database string content (e.g. `Site.Notes`, `Device.Name`) included in LLM context is wrapped in `<data>...</data>` markers. The system prompt explicitly states content inside these markers is data, not instructions. (Defence against prompt injection — see [`security.md` §4](./perspectives/security.md).)
- **FR-22.** No follow-up tool call may be triggered solely by content from a previous tool's output. Tool selection requires user input.
- **FR-23.** A regression suite of "golden" question→expected-row-count pairs runs nightly. A breakage is a release-blocking incident. Coverage targets: all canned reports plus 20 representative ad-hoc questions chosen from §3 user stories.
- **FR-24.** Every chat answer renders **with**: the question, the executed SQL (collapsed by default, expandable), the row count, the time window applied, the filters applied, the views referenced, and the latency. The answer is rendered as plain text or sanitised markdown — never raw HTML, never with `<script>`, `<iframe>`, remote-src `<img>`, or event handlers.

### 4.5 File export (phase 2)
- **FR-25.** Each report and chat result can be exported in: **CSV**, **PDF**, **DOCX**, **XLSX**, **KML**, **GeoJSON**, **PNG**. Formats marked as "must" by users are CSV (analyst), PDF (warden/donor), XLSX (analyst-preferred over CSV), PNG (WhatsApp coordination). KML/GeoJSON are required for incident analysis overlays in QGIS / Google Earth. PowerPoint is **not** required. (See [`user.md` §4](./perspectives/user.md).)
- **FR-26.** CSV exports prefix any cell beginning with `=`, `+`, `-`, `@`, tab, or carriage return with `'` (formula-injection defence). UTF-8 with BOM.
- **FR-27.** PDF and DOCX exports include the question, parameters, timestamp generated, page numbers, the table/chart, and a "generated by wpsWatch AI Reports" footer.
- **FR-28.** Exports run on a separate code path with a **120-second** statement timeout (vs **15 seconds** for chat queries) and a **100,000-row hard ceiling** (vs **5,000-row default chat cap**). Exceeding the ceiling requires an admin-only flag.
- **FR-29.** Every export is audited (who, when, what query, what row count, what format).

### 4.6 Saved queries (phase 3)
- **FR-30.** A new `reporting.saved_query` table on the AI Reports service's storage holds: `id`, `organization_id`, `user_id`, `name`, `description`, `sql_text`, `parameter_schema`, `manifest_version`, `created_utc`, `last_run_utc`, `last_run_status`. Saved queries are scoped to a single org in MVP.
- **FR-31.** Saving a query does **not** call the LLM. The save path strips concrete values to parameters (`WHERE date > '2026-05-01'` → `WHERE date > @startDate`), infers a parameter schema, and persists the SQL.
- **FR-32.** Re-running a saved query does **not** call the LLM. The user fills the parameter form and the runner binds + executes. The result is deterministic.
- **FR-33.** Saved queries cannot contain hardcoded organization IDs. The save path rejects queries with literal `OrganizationId` values; org scope is supplied at run time from the runner's authorized org list. (See [`security.md` S-MUST-35](./perspectives/security.md).)
- **FR-34.** On every save and run, three layers of validation: syntax (parses, references existing objects), permission (user is allowed to query the underlying data), manifest (every `reporting.*` object exists at a compatible signature in the current `data-manifest.json`).
- **FR-35.** Deprecated objects produce a warning to the user; deleted objects produce a hard error pointing at the replacement (per the v1/v2 reporting-schema deprecation cycle in [`data.md` §6](./perspectives/data.md)).
- **FR-36.** Saved queries can be **shared within an org** (visible to other users in the org). When run by a different user, that user's `OrganizationUserRole`-derived org scope applies — not the author's. (See [`security.md` S-MUST-36](./perspectives/security.md).)
- **FR-37.** A saved-query run-history table (`reporting.saved_query_run`) records every execution with user, org, manifest version, runtime, status, row count.
- **FR-38.** A nightly smoke-runner executes every saved query (with parameters defaulted to a representative slice) and flags syntactic / semantic breakage to the owner.
- **FR-39.** When re-running a saved query, the UI surfaces "last run on date X returned N rows; this run returned M rows" as a one-line diff banner.

### 4.7 Behavioural and trust requirements (phase 1+, refined in phase 2)
- **FR-40.** Every quantitative answer states the count, the population it was drawn from, and the time window. ("5 cameras out of 142 active, last seen before 2026-05-08 06:00 UTC.")
- **FR-41.** Site/device names are returned verbatim from the database. The LLM may not paraphrase entity names.
- **FR-42.** When the model cannot answer with the data it has access to, it returns a clear "I can't answer that with your access" message, not a plausible fabrication.
- **FR-43.** A "show me what query ran" affordance is available on every chat answer, exposing the executed SQL.
- **FR-44.** When data freshness is suspect (replica lag above a notification threshold of, e.g., 10 seconds), the answer carries a "data is approximately X seconds behind primary" badge.
- **FR-45.** Lists in chat answers are rendered as plain bulleted lists or compact tables, not as prose paragraphs (radio-readability requirement, [`user.md` §7](./perspectives/user.md)).

### 4.8 Connectivity and graceful degradation (phase 2)
- **FR-46.** Request timeouts are surfaced to the user as explicit timeouts ("the request timed out, retry?"), never as silent failures or infinite spinners.
- **FR-47.** Saved queries cache their last result client-side (with a clear "cached from <timestamp>" stamp) for use during connectivity outages and load-shedding gaps.
- **FR-48.** A "morning sweep" scheduled email digest delivers the user's named saved queries to their inbox at a configurable time (default 05:45 local) before they are at the desk.

---

## 5. Non-functional requirements

- **NFR-01 (latency).** P50 chat response time < 8 seconds; P95 < 20 seconds. Canned reports P50 < 1 second, P95 < 5 seconds. (Per [`data.md` req 14 cost-class targets](./perspectives/data.md).)
- **NFR-02 (throughput).** Initial sizing assumes ≤25 concurrent queries across the service and ≤5 per user. Configurable.
- **NFR-03 (availability).** Service availability target: 99.5% during reserve operating hours (05:00–22:00 in each user's timezone). Outside hours, best-effort. Hard dependency: replica availability and Anthropic API availability for chat features (canned reports degrade gracefully if Anthropic is down).
- **NFR-04 (replica lag tolerance).** The service refuses to serve when replica lag exceeds 60 seconds (FR-10). It surfaces a freshness badge when lag exceeds 10 seconds (FR-44).
- **NFR-05 (cost ceiling).** Anthropic API spend is capped via Anthropic-side limits and per-user query rate-limiting (FR-22, 60 queries/min/user). The cost-profile metadata in the manifest ([`data.md` req 14](./perspectives/data.md)) flags expensive queries before they run.
- **NFR-06 (observability).** App Insights instrumentation includes `RequestTelemetry`, `DependencyTelemetry`, plus custom events `AiReportQuery`, `AiReportDenied`, `AiReportExport`. Audit data goes to a separate restricted store (see §6). The DB team has a Query Store dashboard for the AI principal's actual query workload.
- **NFR-07 (deployability).** Service is deployed as a containerised app, with the same environment-promotion rules the existing wpsWatch infrastructure uses (dev → QA → prod, Azure App Service or AKS).
- **NFR-08 (key/secret management).** All secrets (`JwtKey`, Anthropic API key, DB connection strings) live in Azure Key Vault. The service authenticates to Key Vault and the replica via managed identity (`DefaultAzureCredential`, matching `Wps.Watch.Api/Startup.cs:147`).

---

## 6. Security and privacy requirements

These are reproduced from [`security.md` §10](./perspectives/security.md) with phase tagging. The MUSTs are MVP-binding (phase 1) **except where explicitly tagged for a later phase** because the surface they protect doesn't exist until then.

### MUST (phase 1 unless noted)

- **S-MUST-01.** No outbound HTTP calls from the AI Reports service to the wpsWatch API in production.
- **S-MUST-02.** All DB connections include `ApplicationIntent=ReadOnly`.
- **S-MUST-03.** A dedicated SQL principal `ai_reports_reader` has `SELECT` only on `reporting.*` and `db_datareader` on `reporting`. No grants on `dbo.*`.
- **S-MUST-04.** Authorization constants and `OrganizationUserRole` projection are consumed from a shared `Wps.Watch.Authorization` package — not duplicated.
- **S-MUST-05.** JWT validation matches the existing `JwtIssuer` / `JwtKey` configuration. Expired, malformed, wrong-issuer tokens → HTTP 401.
- **S-MUST-06.** Volunteers → HTTP 403 from every endpoint except `/health`.
- **S-MUST-07.** Only `OrgAdmin`, `IncidentManager`, `Viewer`, `GpsViewer`, `SystemAdmin` can access the service. (Mirror `OperationRoles.cs:446–463`.)
- **S-MUST-08.** The user's authorized org-id list is computed per-request from `OrganizationUserRole`.
- **S-MUST-09.** `SESSION_CONTEXT(N'AllowedOrgIds')` is set with `read_only=1` on every query session.
- **S-MUST-10.** Reporting views reference `SESSION_CONTEXT(N'AllowedOrgIds')` in their `WHERE` clause; views without this filter are not in the AI principal's allowlist.
- **S-MUST-11.** Connection-pool checkout always sets a fresh `SESSION_CONTEXT`.
- **S-MUST-12.** `OrganizationUserRole` is re-read per request, not cached across requests.
- **S-MUST-13.** A query rewriter additionally injects an org-scope `WHERE` clause as defence in depth (phase 2, when free-text SQL ships).
- **S-MUST-14.** The service refuses queries when replica lag exceeds the configured threshold.
- **S-MUST-15.** Replica lag is measured per request (e.g. `sys.dm_database_replica_states`) and emitted as telemetry.
- **S-MUST-16.** **Free-text SQL is not in MVP.** MVP exposes only allowlisted parameterised procedures and views.
- **S-MUST-17.** When free-text SQL ships (phase 2), all generated SQL passes a parsed-AST validator (see FR-19 for the full rule list).
- **S-MUST-18.** The DB principal lacks `EXECUTE` on extended stored procedures.
- **S-MUST-19.** Statement timeout: 15 seconds default (chat); 120 seconds export path.
- **S-MUST-20.** Row caps: 5,000 default chat, 100,000 hard ceiling on export.
- **S-MUST-21.** Default isolation: `READ COMMITTED SNAPSHOT`. `READ UNCOMMITTED`/`NOLOCK` are rejected by the AST validator.
- **S-MUST-22.** LLM output rendered as plain text or sanitised markdown only.
- **S-MUST-23.** CSV / PDF / DOCX exports apply formula-injection defence per format.
- **S-MUST-24.** DB string content in LLM context is wrapped in `<data>...</data>` markers (phase 2).
- **S-MUST-25.** Tool selection requires user input; no follow-up tool call from previous tool output alone (phase 2).
- **S-MUST-26.** Per-user rate limit: 60 queries/min (configurable).
- **S-MUST-27.** **GPS coordinates are not surfaced to the LLM at all in MVP.** `Site.Latitude`/`Longitude` and `Deployment.Latitude`/`Longitude` are absent from every reporting view. (See §6 for the disagreement resolution.)
- **S-MUST-28.** Sensitive credential and PII columns enumerated in [exploration notes §6](./ai-reports-exploration-notes.md#6-schema-surface-for-the-seven-reports) are absent from every reporting view and `DENY`-d at the column level for the AI principal.
- **S-MUST-29.** Chat answers do not return species + GPS + recent timestamp (`LastEventDateTimeUtc` within 24 hours) in the same row for any role below `IncidentManager`. When this combination is returned (to higher roles), the request is flagged in audit. (Phase 2.)
- **S-MUST-30.** Volunteer-exclusion is enforced as middleware with an automated test asserting 403.
- **S-MUST-31.** A test suite verifies role-by-role allowed/forbidden endpoints.
- **S-MUST-32.** Audit retention: 13 months in a store separate from App Insights, restricted to `SystemAdmin` and a named security-ops group.
- **S-MUST-33.** Audit-log read access is itself logged.
- **S-MUST-34.** Saved queries store `manifest_version` at save time; mismatches at run time fail closed (phase 3).
- **S-MUST-35.** Saved queries do not contain hardcoded org IDs (phase 3).
- **S-MUST-36.** Shared saved queries use the running user's org scope, not the author's (phase 3).
- **S-MUST-37.** Every request emits an audit record (user, role, org scope, prompt, generated SQL, views referenced, row count, latency, replica lag, denial reason). Row values are not logged.

### SHOULD

- **S-SHOULD-01.** `Wps.Watch.Authorization` package is versioned; CI fails when API and AI Reports versions diverge by >1 minor.
- **S-SHOULD-02.** Real-time alerting on AST denials, rate-limit hits, and "sensitive combination" flags.
- **S-SHOULD-03.** OrgAdmins can mark a user as audit-locked, failing saved-query execution closed (phase 3).
- **S-SHOULD-04.** GPS coarsening (if introduced post-MVP) is configurable per org.
- **S-SHOULD-05.** Migration from HS256 to RS256/Entra is planned; JWT validation supports both with a config flip.
- **S-SHOULD-06.** Prompt content length-limited (≤8 KB).
- **S-SHOULD-07.** DB string fields in LLM context truncated (e.g. `Site.Notes` to 500 chars).
- **S-SHOULD-08.** Query Store enabled and reviewed weekly for AI principal's anomalous shapes.
- **S-SHOULD-09.** Hourly canary: known-good prompt + known-malicious prompt feed alerting.
- **S-SHOULD-10.** Chat sessions idle-timeout at 15 minutes.
- **S-SHOULD-11.** "Sensitive combination" answers carry an explicit user-facing warning before display (phase 2).
- **S-SHOULD-12.** Saved-query UX shows "this query was created when you had access to orgs X, Y, Z; you currently have access to X" (phase 3).
- **S-SHOULD-13.** Failed authentication rate-limited per IP and per user-id claim.

### NICE-TO-HAVE

- **S-NICE-01.** Differential-privacy noise on small-count aggregates over sensitive species.
- **S-NICE-02.** A "dry run" option on chat — show the SQL without executing.
- **S-NICE-03.** SystemAdmin audit dashboard with per-user query timelines.
- **S-NICE-04.** A read-only "explainer" endpoint that decodes generated SQL into plain English so the user can verify before running. (Eric's "human in the loop first" principle made concrete.)
- **S-NICE-05.** Watermarking on PDF/CSV exports (user id + timestamp embedded in metadata).
- **S-NICE-06.** Periodic red-team prompt-injection testing using a corpus of malicious DB-content payloads.

### Disagreement resolved: GPS handling

The security perspective recommended **role-gated coarsened views** (`vReport_Deployment_Coarse` for `Viewer`, `_Fine` for `GpsViewer`+). The data perspective recommended **never expose GPS to the LLM at all**. Resolution: **adopt the data perspective for MVP** (S-MUST-27 above). Rationale:
- The LLM context is a logging surface (Anthropic API requests are logged for debugging; even with retention controls, GPS data flowing through the model is a residual risk).
- The user perspective supports it: "Anything involving GPS coordinates of the rhinos themselves... no. Even if the data exists." ([`user.md` §3](./perspectives/user.md).)
- Aggregate geographic answers ("3 sites in this region went offline this week") work without raw GPS.
- If a future use case needs map rendering, that becomes a deliberate phase-3 feature with role-gated coarsened views, evaluated against the threat model at that time. This decision is reversible; the inverse (re-securing data the LLM has already had in context) is not.

---

## 7. Permission model

### Phase 1 (MVP)
- **System Admins (`AdminReport` resource)** see the seven reports they see today.
- **`UserReport` users (`OrgAdmin`, `IncidentManager`, `Viewer`, `GpsViewer`)** see the three reports they see today.
- **`Volunteer`, `PhotoUploader`, `PhotoTagger`** see no AI Reports page.
- **Single-org behaviour** — the new page respects the user's current organisation context (read from the JWT's org claim), exactly as today's Reports menu does. The `X-MultiOrgMode` header is not used in MVP.

### How the new service learns and enforces this without hitting the API
- JWT validated locally with the shared `JwtKey` (FR-05).
- `OrganizationUserRole` queried per request from the replica (FR-06).
- Role checks routed through the shared `Wps.Watch.Authorization` package (FR-07), using the same `Operation<TEntity>` / `Can<TEntity>(...)` patterns the API uses today. The new service consumes `UserReport` and `AdminReport` operations from `OperationRoles.cs:446–463`.

### Phase 2+
- **Cross-org reporting.** When the cross-org migration extends to Reports (currently out-of-scope per `cross-org-overview.md:14–25`), the AI Reports service becomes the **first cross-org reporting surface** in wpsWatch. Saved queries become potentially multi-org (FR-30 currently constrains to single-org); this is a design decision deferred until cross-org is reached.
- **Open question on the SysAdmin/UserReport split** — see §11. The user perspective ([`user.md` §2](./perspectives/user.md)) argues that operationally-critical reports like Top 10 Offline are gated to SysAdmin in a way the field doesn't agree with. The MVP does **not** change the gating; the gating decision is escalated as a design-council question.

### What about wpsWatch UI gating
The Reports menu in `Header.tsx` gates by `currentUser.resources.includes("AdminReport")` and `currentUser.resources.includes("UserReport")` — these are the existing wiring. The new page link is added under both gates. The new page itself does not rely on UI gating; the service enforces all access decisions independently.

---

## 8. Architecture options considered

### Transport / protocol layer
Three positions, evaluated in [`backend.md` §1](./perspectives/backend.md):
1. **MCP server only** — standardised tool catalogue, well-aligned with Eric's roadmap, but young SDK and SSE plumbing complications.
2. **REST + `web_fetch`-style tool use** — every backend engineer knows REST, but generic HTTP tools push validation into handlers and don't compose well as the surface grows.
3. **Hybrid: REST for the React boundary, MCP for the model boundary, one handler core.** **Recommended.**

**Recommendation: Hybrid (option 3).** The browser POSTs `/api/chat` to the new service; the service holds the conversation, calls Anthropic's Messages API with MCP tools attached, executes tools against the database with org-scope predicates injected, and streams the answer back as SSE. The browser never holds an Anthropic key. The MCP tool surface is reusable in phase 2 (Mission Control) and phase 3 (bounded physical agency) per Eric's roadmap. This is what FR-02 + FR-03 instantiate.

### Query generation strategy
Three positions, evaluated in [`backend.md` §2](./perspectives/backend.md) and [`data.md` §2](./perspectives/data.md):
1. **Predefined parameterised queries only** — safest, freezes users into the catalogue.
2. **Free text-to-SQL with guardrails** — most flexible, every shortcut is a future incident.
3. **Hybrid: predefined catalogue is the main surface; text-to-SQL is the chat escape hatch, gated and added in phase 2.** **Recommended.**

**Recommendation: Hybrid (option 3).** Canned-report MVP uses parameterised stored procedures only — no LLM in the flow. Phase-2 chat adds free-text SQL gated to `AdminReport`, with a parsed-AST validator and `SESSION_CONTEXT`-based org filtering. Phase 3 saves the resulting SQL (templated with parameters), re-running it without consulting the LLM. This squares the "save the SQL not the prompt" requirement: the saved artefact is the SQL string + parameter schema, deterministic on re-run.

### Resolving the "save the SQL not the prompt" tension
This requirement is tautological for predefined queries (you save `(report_id, parameters)` and the SQL is the catalogue's) and load-bearing for text-to-SQL (you save the literal SQL produced by the model with values templatised to parameters). Both shapes live in the same `reporting.saved_query` table with a `source` flag (`catalogue` vs `generated`). On re-run, the LLM is not consulted; the SQL runs verbatim against the current schema, with manifest-version pinning catching drift. (Per FR-30 to FR-39 and [`backend.md` §3](./perspectives/backend.md).)

### Authentication option
Three positions, evaluated in [`backend.md` §5](./perspectives/backend.md):
1. **Share the symmetric `JwtKey`** with the new service via Key Vault. **Recommended primary path.**
2. **New `/api/auth/introspect` endpoint** on the wpsWatch API. Documented fallback if security review rejects key sharing.
3. **Migrate JWT to RS256 / Entra.** Out of scope for this feature; tracked as a longer-term posture improvement (S-SHOULD-05).

The "no API calls" constraint in the ticket is preserved by option 1: the new service validates JWTs locally with a shared key (the key is in Key Vault, not in code). It does not call the API to validate. Option 2 would re-introduce API dependency, which is why it's the fallback rather than the primary.

### Data-source option
Three positions, evaluated in [`data.md` §1](./perspectives/data.md):
1. **Replica only, query OLTP tables directly.** Pulls in sensitive columns; OLTP indexes don't fit reporting shape; LLM hallucinates joins.
2. **Curated reporting mirror / warehouse.** Real cost, real ETL, no payback at MVP scale.
3. **Hybrid: existing replica + curated `reporting` schema (views and a small set of summary tables) on top of it.** **Recommended.**

**Recommendation: Hybrid (option 3).** Reporting views and stored procedures live in a `reporting` schema on the replica; the AI principal has access to `reporting.*` only. Sensitive columns are absent (not denied — absent). The view layer absorbs OLTP schema changes. Pre-aggregation summary tables (photo-count daily, device-event hourly, battery-snapshot daily — [`data.md` §5](./perspectives/data.md)) handle the time-series shapes that are expensive on raw OLTP.

---

## 9. Data layer requirements

Reproduced and lightly edited from [`data.md` §9](./perspectives/data.md). Full rationale in that document.

- **DR-01.** Read source: `wpswatchprodreplica.database.windows.net` with `ApplicationIntent=ReadOnly`. (Net-new: no replica connection string is configured anywhere in the codebase today — see [exploration notes §8](./ai-reports-exploration-notes.md#8-replica-realities).)
- **DR-02.** A new `reporting` schema is created on the replica (replicated from primary). The AI service has access to `reporting.*` only.
- **DR-03.** Dedicated read-only login `ai_reports_reader` with `SELECT` on `reporting` and `DENY` on sensitive `dbo` columns.
- **DR-04.** Sensitive credential / PII columns enumerated in [exploration notes §6](./ai-reports-exploration-notes.md#6-schema-surface-for-the-seven-reports) are absent from `reporting.*` views and `DENY`-d on the AI login.
- **DR-05.** No raw `dbo.*` exposure to the LLM under any circumstance.
- **DR-06.** Stored-procedure catalogue for canned reports (FR-11, FR-12).
- **DR-07.** View catalogue for ad-hoc queries (phase 2).
- **DR-08.** Semantic manifest `data-manifest.json` in the AI service repo describes every exposed object: name, purpose, columns (name, type, description, examples), parameters, `expected_cost_class`, version. **The LLM's only source of schema knowledge.**
- **DR-09.** GPS columns (`Site.Latitude/Longitude`, `Deployment.Latitude/Longitude`) are absent from every `reporting.*` view in MVP.
- **DR-10.** Statement timeout: 15s chat / 120s export.
- **DR-11.** Row caps: 5,000 default / 100,000 export ceiling.
- **DR-12.** `READ COMMITTED SNAPSHOT` isolation enabled.
- **DR-13.** Query Store enabled on the replica, 7-day retention, `WAIT_STATS_CAPTURE_MODE = ON`.
- **DR-14.** `expected_cost_class` per object in the manifest (`fast` / `medium` / `slow`).
- **DR-15.** Concurrency: 5/user, 25/service-instance.
- **DR-16.** Pre-aggregation: `reporting.fct_photo_count_daily` (must-build for MVP). Refreshed nightly with a 3-day trailing recompute.
- **DR-17.** Pre-aggregation: `reporting.fct_device_event_hourly` (should-build for MVP, columnstore index). Refreshed hourly with 6-hour trailing recompute.
- **DR-18.** Pre-aggregation: `reporting.fct_battery_snapshot_daily` (nice-to-have, prospective only — OLTP only stores current values).
- **DR-19.** Denormalised dimensions: `reporting.dim_deployment`, `reporting.dim_organization`. Org scope is collapsed to a single column on every fact table.
- **DR-20.** Refresh mechanism: Azure SQL elastic jobs running stored procedures in `reporting`. Failures alert via App Insights; freshness recorded in `reporting.job_run_log`.
- **DR-21.** Schema-evolution contract tests in CI: live `reporting` schema diffed against committed `data-manifest.json`; build fails on disagreement.
- **DR-22.** Versioned reporting objects: backward-incompatible changes go through a v1/v2 cycle with 90-day overlap, deprecation warnings emitted.
- **DR-23.** Output filter: result columns matching `*token*`, `*hash*`, `*secret*`, `*password*`, `*apikey*` are dropped with a logged warning before being returned to the UI.
- **DR-24.** Saved-query storage: `reporting.saved_query` (single-org in MVP).
- **DR-25.** Saved-query validation on load: syntax + permission + manifest layers.
- **DR-26.** Saved-query audit: `reporting.saved_query_run` row per execution.
- **DR-27.** SIM Prepaid Data: a new `Device.PrepaidDataUsedGb` (and `PrepaidDataLastSyncedUtc`) column is added to the OLTP schema (owned by the API team) and populated by a daily carrier-API sync. AI service reads it through `reporting.vw_DeviceSimStatus`. If this work cannot ship for MVP, the report displays "not tracked in WPS Watch" rather than calling Twilio at query time. **(Open question — see §11.)**
- **DR-28.** Top 10 Offline default threshold: **24 hours**, exposed as a parameter `@OfflineThresholdHours` overridable by the user. **(Open question — see §11.)**
- **DR-29.** Single-org MVP: all `reporting.*` objects accept a single `@OrganizationId` parameter. Multi-org queries are rejected. Cross-org is phase 2+.
- **DR-30.** Deployment ordering invariant: the `reporting` schema is always deployed before the AI service is updated to consume new objects. Backward-incompatible removals always come after the AI service has stopped consuming them. Enforced by the contract-test CI gate (DR-21).

---

## 10. Phasing proposal

### Phase 1 (MVP) — *the smallest credible end-to-end slice*
**Goal:** demonstrably replace **one** of the existing canned reports through the new service, end-to-end, with correct permission scoping. Then expand to the remaining nine.

**The first credible vertical slice (the "tracer bullet"):**
- Replace **Top 10 Offline** end-to-end. It's the highest-leverage report (per [`user.md` §2](./perspectives/user.md), the morning sweep) and the simplest data shape (a single ordered query with a threshold parameter).
- New service deployed with REST endpoints `/api/reports`, `/api/reports/top10offline/run`, `/api/health`.
- Replica connection wired up (DR-01) — net-new work.
- `Wps.Watch.Authorization` package extracted and consumed by the new service (FR-07).
- `reporting.usp_Top10Offline` stored procedure with `SESSION_CONTEXT`-based org filter and `@OfflineThresholdHours` parameter (DR-28, FR-20).
- New page `/ai-reports` in the wpsWatch React app, single report card, parameter form, Run button, table render.
- `Volunteer` exclusion test, `UserReport`/`AdminReport` role tests, parity test against current Looker dashboard (FR-15).
- App Insights instrumentation, per-request audit log (S-MUST-37), replica-lag freshness gate (FR-10).

**Once the tracer bullet is in production:**
- Add the remaining six SysAdmin reports + the two not-yet-covered regular-user reports (FR-11, FR-12).
- Resolve the SIM Prepaid Data data-source question (DR-27, §11).
- Build pre-aggregation `fct_photo_count_daily` (DR-16) once `Photo Count by Camera` is on the critical path.
- Settle the cross-org / SysAdmin-only-reports permission question (§11).

**Out of MVP:** free-form chat, file export beyond CSV, saved queries, scheduled email digest, cross-org.

**Estimated effort (focused engineer-weeks, per [`backend.md` §7](./perspectives/backend.md)):**
- New service skeleton + replica wiring + auth re-impl: ~2 weeks.
- `Wps.Watch.Authorization` extraction (coordination with API team): ~0.5 weeks.
- The `reporting` schema with `SESSION_CONTEXT` org filter for all ten reports: **~3–6 weeks** (the dominating piece — semantics for SIM Prepaid Data and Top 10 Offline are open).
- Parity tests, observability, audit, deploy: ~2 weeks.
- React page + parameter forms + table render + CSV export: ~2 weeks.
- **Phase 1 total: ~10–14 engineer-weeks.**

### Phase 2 — chat, full export, cross-org
- Free-form chat panel gated to `AdminReport` (FR-16, FR-17).
- MCP tool catalogue with `query_database`, `describe_view`, `list_canned_reports`, `run_canned_report` (FR-18, FR-19).
- AST validator + query rewriter (FR-19, S-MUST-13, S-MUST-17).
- Anthropic host loop with streaming, tool-use orchestration, audit (FR-21 to FR-24).
- Eval suite of golden questions (FR-23).
- All file export formats (FR-25 to FR-29).
- Scheduled email digest (FR-48) — likely on its own thread of work.
- Cross-org reporting (the wpsWatch cross-org migration extending to Reports).

**Estimated effort: ~14–24 engineer-weeks**, dominated by AST validation / org-scope rewriter and the eval suite.

### Phase 3 — saved custom queries
- `reporting.saved_query` table, save / list / edit / delete UI (FR-30 to FR-39).
- Three-layer validation, deprecation handling, smoke runner, diff banner.
- Sharing within an org with running-user scope semantics (FR-36).
- Saved-query UX for "you used to have access to orgs X, Y, Z" (S-SHOULD-12).

**Estimated effort: ~6–10 engineer-weeks.**

---

## 11. Open questions for the human owner

These are concrete decisions the wpsWatch dev team and the design council need to resolve before the formal design doc is written.

1. **The SysAdmin-only report list** — the prompt's seven reports are *all* gated to `AdminReport` (System Admin) per the exploration notes. Is this list complete, or are there additional SysAdmin-only reports not yet documented? (See [exploration notes §10](./ai-reports-exploration-notes.md#10-file-citation-index-quick-reference) for the AdminReport URL block in `Header.tsx:582–730`.) If complete, the answer to the "what extra reports exist" question is "there is no broader set."
2. **Should regular users (`UserReport`) get access to the four currently-SysAdmin reports?** The user perspective ([`user.md` §2](./perspectives/user.md)) makes a strong field-ops case for at least Top 10 Offline, Region & Site Totals, and SIM Contract Renewal. The MVP, as specified, mirrors today's gating exactly. Changing the gating is a design-council question, not a tech question, and the answer changes the MVP scope.
3. **`SIM Prepaid Data` source of truth.** No `DataUsedGb` / `DataRemainingGb` columns exist in the EF model. The current Looker report likely fetches from Twilio Super SIM API or a carrier portal. The data perspective recommends adding a column to OLTP, populated by a daily sync. **Who owns this — the API team, the AI Reports team, or a separate platform team? Can it ship in MVP?**
4. **"Top 10 Offline" threshold** — the exploration notes flag that "offline" is computed from `Deployment.LastEventDateTimeUtc` with no defined threshold. The data perspective recommends 24 hours, parameterised. **Confirm 24h is the right default for WPS as a whole, or whether per-reserve / per-device-make defaults are needed at MVP.**
5. **Mirror DB beyond the replica?** The data perspective recommends sticking with the replica + curated `reporting` schema for MVP and re-evaluating only if cost or query-shape pain emerges. **Does the dev team agree, or does any planned scale change the calculus?**
6. **JWT migration to RS256/Entra.** The MVP uses the existing HS256 + shared `JwtKey` setup. Is there an active push to migrate to asymmetric/Entra? If yes, this changes the auth path and the new service should be architected to support both with a config flip (S-SHOULD-05). If no, the symmetric-key sharing is the long-term answer for now.
7. **Primary store for saved queries** — should `reporting.saved_query` live on the new service's own dedicated SQL database, or as a new schema on the existing primary wpsWatch database? Trade-off is feature isolation vs operational simplicity.
8. **Cross-org reporting timing.** The cross-org migration in `cross-org-overview.md` lists Reports as out-of-scope. When is Reports planned to come into scope, and does AI Reports become the first cross-org reporting surface, or does the existing Looker integration also need to change?
9. **Scheduled email digest cadence and deliverability.** Per the user perspective ([`user.md` §6](./perspectives/user.md)), this is potentially the highest-leverage feature — but it requires SendGrid (or equivalent) integration, deliverability monitoring, and template design. Is this in phase 2, or is it phase 3, or is it a separate project?
10. **Anthropic vs. another model provider.** Eric's deck says "frontier models." The architecture is portable across providers (tool-use formats differ but the host-loop pattern is shared), but a primary-vendor decision is needed before phase 2 ships.
11. **Existing Looker SQL.** The Looker queries are not available in shareable form ([plan §"Open questions resolved"](#open-questions-resolved-with-the-human-owner-before-stage-1)). The `reporting` schema views need to be reverse-engineered against side-by-side comparison with the live dashboards. **Who has Looker admin access to extract the underlying queries / data sources during MVP development?**
12. **Existing iframe-based Reports cutover plan.** The new page initially lives alongside the existing Reports menu (FR-04). When does the existing menu get removed? What does the user experience look like during the parity period?

---

## 12. Glossary

For the design council and any reader who is new to the terminology used above.

- **MCP (Model Context Protocol):** an Anthropic-published protocol for exposing **tools** (named functions with JSON-schema arguments) and **resources** (read-only blobs) to an LLM client over a stdio or SSE transport. Standardisation, not novel capability — the underlying mechanic is "tool use."
- **Tool use:** an LLM API pattern where the model emits a structured request to invoke a named tool (e.g. `run_canned_report(report_id="top10offline", parameters={...})`); the host executes the tool and returns the result; the model then continues its turn. Anthropic, OpenAI, and others implement variants of this.
- **RAG (Retrieval-Augmented Generation):** at query time, retrieve relevant context (e.g. schema snippets, sample rows, column descriptions) and inject it into the LLM prompt. Used here for ad-hoc text-to-SQL: the LLM gets the manifest entries for relevant views, not the whole database.
- **Text-to-SQL:** LLM-generated SQL from a natural-language question. Most flexible, hardest to make safe; gated to `AdminReport` and behind a feature flag in our design.
- **Predefined parameterised queries:** a fixed catalogue of SQL templates the LLM picks from and parameterises. Safer than text-to-SQL; less flexible.
- **Replica vs. mirror:** *Replica* = Azure SQL's built-in read-only secondary, same schema as primary. *Mirror* = a separate database we own and shape ourselves (could be a curated subset, a denormalised warehouse, or anything in between). MVP uses the replica with a curated `reporting` schema layered on top.
- **Semantic layer / data manifest:** a description of tables and columns in human terms — the LLM's source of schema truth, version-controlled in source. Defines what the model "knows" the schema looks like.
- **Materialized / pre-aggregated views:** precomputed result sets stored as physical tables, refreshed on a schedule. Used here for photo counts, device event counts, battery snapshots — the time-series shapes that are expensive on raw OLTP.
- **OLTP:** online transaction processing — the primary database schema, normalised for inserts/updates, slow for reporting.
- **`SESSION_CONTEXT`:** a SQL Server key/value store scoped to a session, set per-connection. We use it to bind the user's allowed organisation IDs at request time, with `read_only=1` so they cannot be reset within the request.
- **Snapshot isolation / Read-Committed Snapshot (RCSI):** SQL Server isolation levels that let readers see a consistent point-in-time view without taking shared locks. Avoids blocking. RCSI is the milder, cheaper variant; we use it.
- **`SP_set_session_context`:** the stored procedure that sets `SESSION_CONTEXT` values.
- **`Wps.Watch.Authorization`:** the new shared .NET project (proposed) extracting the role/operation constants and `OrganizationUserRole` projection from `Wps.Watch.Api/Authorization/`. Consumed by both the API and the AI Reports service so they cannot drift.
- **`Operation<TEntity>` / `Can<TEntity>(...)`:** the typed authorization API used by wpsWatch. `_userContextService.Can(PhotoOperationRoles.RolesByOperation, PhotoOperations.Names.Search)` checks whether the user can search photos in their current org context. Reused unchanged by the AI Reports service.
- **`AdminReport` / `UserReport`:** the two `Operation<ScheduledReport>` operations defined in `OperationRoles.cs:446–463`. `AdminReport` is empty-list (System Admin only via the global bypass); `UserReport` includes `OrgAdmin`, `IncidentManager`, `Viewer`, `GpsViewer`. Volunteers, PhotoUploaders, PhotoTaggers are not in either.
- **Cross-org / multi-org:** wpsWatch's mode where a user can see data across all the organizations they are a member of, not just their currently-selected org. Out of scope today for Reports; potentially the AI Reports service's first cross-org reporting surface in phase 2+.
- **Looker Studio:** Google's free BI tool. The current Reports menu opens dashboards hosted in Looker Studio with `?params=...` query strings carrying the user's org UUID.
- **Anthropic Messages API:** Anthropic's HTTP API for chat completions with tool-use support. Hosted by us server-side (in the new service), not in the browser.

---

*End of requirements document.*
