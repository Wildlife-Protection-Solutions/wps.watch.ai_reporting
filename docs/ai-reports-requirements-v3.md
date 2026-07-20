# AI Reports — Requirements Document (v3)

**Status:** Draft — for wpsWatch dev team review.
**Supersedes:** [`ai-reports-requirements-v2.md`](./ai-reports-requirements-v2.md) (v2, 2026-05-14). v1 and v2 preserved unchanged for comparison.
**Authors:** Synthesis of Stage 1 codebase exploration (now [v3 notes](./ai-reports-exploration-notes-v3.md)) + four parallel persona reviews (backend, security, user, data) + Matt Hron's review comments on v1 (19 comments dated 2026-05-08 and 2026-05-11).
**Sources:** [`ai-reports-exploration-notes-v3.md`](./ai-reports-exploration-notes-v3.md), [`ai-reports-requirements-delta.md`](./ai-reports-requirements-delta.md), [`perspectives/backend.md`](./perspectives/backend.md), [`perspectives/security.md`](./perspectives/security.md), [`perspectives/user.md`](./perspectives/user.md), [`perspectives/data-v2.md`](./perspectives/data-v2.md), Eric's "WPS Agentic Conservation Model & Roadmap" deck, Jira ticket text (verbatim), Matt Hron's comments on `ai-reports-requirements.md.docx`.
**Date:** 2026-05-14.

> **How to read this document.** Sections 1–10 are the requirements proper. Section 11 lists open questions the dev team needs to resolve before the design doc. Section 12 is a glossary. File-path citations point to one of four repos: `wps.watch.api`, `wps.watch.web`, `wpswatch.reporting`, and `wps.watch.functions.scheduledreporting`.

---

## What changed from v2 to v3

Twelve changes driven by Matt Hron's review of v1. Summary:

1. **Tracer bullet swap.** Phase 1 demonstrates **Region & Site Totals** end-to-end, not Top 10 Offline. (Comments 6, 10 on the v1 doc.)
2. **Phase reshuffle.** Phase 2 = ad-hoc chat + free-form chat + export, only. **Scheduled email digest moves to Phase 3.** (Comment 17.)
3. **Phase 1 UX scope additions.** Sortable columns, dynamic filtering, summarisation (aggregating totals based on filtered data), flexible date range. (Comment 0.)
4. **Saved queries: relative-date support is a first-class feature.** Not just absolute-date parameterisation. (Comments 9, 3.)
5. **Top 10 Offline becomes "Deployment Health Status" with three tiers.** 24h offline + 12h warning + online. (Comment 14.)
6. **US-04 (phone alert) is non-goal.** That's an alerting feature, not reporting. (Comment 8.)
7. **Cutover sequencing.** New menu side-by-side with old during pilot; **SysAdmin migrates first, regular users second**. (Comments 1, 18.)
8. **Site-level user roles in flight.** New cross-cutting concern: design org-scope mechanism extensible. (Comment 2.)
9. **Saved-query storage: three-way choice.** Analytics DB vs. dedicated SQL DB vs. **Cosmos** (added per Matt). (Comment 5.)
10. **Several open questions resolved.** OQ-1 (seven reports is the SysAdmin scope), OQ-2 (Field Team owns gating promotion, not this project), OQ-V2-3 (SIM data dynamically calculated), OQ-V2-8 (cross-org Reports depends on this project), OQ-V2-9 (digest is Phase 3), OQ-V2-12 (cutover plan). (Comments 11, 12, 13, 15, 17, 1+18.)
11. **GPS: long-term roadmap.** v2 already deferred from MVP. v3 makes explicit that **post-Phase 3 includes GPS exposure with role-gated coarsening** as a planned future feature, not "out of scope forever." (Comment 4.)
12. **Field Team policy explicit.** The existing wpsWatch process for promoting AdminReport → UserReport runs **outside this project** but on the same data; the requirements doc must say so to avoid future scope creep. (Comment 12, also comment 16 surfacing the kick-off action.)

The numbering scheme: requirements that change get a `-V3-` infix. Requirements that carry forward from v2 (or v1) keep their previous IDs. Appendix B maps v2 → v3.

---

## 1. Background and motivation

### What exists today
*Same as v2 §1, plus one v3 clarification on the role-gating policy.*

The Reports stack has four interlocking parts (per [v3 exploration notes §11–§12](./ai-reports-exploration-notes-v3.md)):

- **Frontend:** Reports menu in `wps.watch.web/src/components/common/Header.tsx`. Two role-gated branches: System Admins (resource `AdminReport`) see the seven Looker dashboards; regular users with `UserReport` see three iframe-embedded reports.
- **Analytics database:** `wps-sql-analytics-prod` on the same SQL Server as the OLTP primary. Holds 15 views, 7 stored procs, 9 UDFs, 10 aggregation tables, 29 external tables proxying reads from `wpswatch-prod` via SQL Server's elastic-DB-query mechanism. Owned by the `wpswatch.reporting` repo.
- **Refresh:** four Azure Automation runbook PowerShell scripts in `wpswatch.reporting/Jobs/` run scheduled stored-proc refreshes. Cadence configured in the Azure Automation account.
- **Scheduled emails:** `wps.watch.functions.scheduledreporting` (Azure Functions v4, .NET 8 isolated) sends weekly deployment-status emails via SendGrid (US) / Mailjet (EU) / Brevo (EU alt). Subscriptions are site-level via the `ScheduledReport` table.

### Why change
Same operational reasons as v1/v2: Looker dashboards can't combine filters, can't ask follow-ups, can't be sorted by arbitrary columns, can't aggregate totals based on filtered data, can't use flexible date ranges. The morning sweep at 06:00 is the highest-leverage moment and Looker doesn't serve it well.

**(v3 emphasis)** Matt's comment 0 on the v1 doc surfaces four specific UX gaps in the existing reports: sortability of all table columns, dynamic filtering, summarisation (aggregating totals based on filtered data), and flexible date range. These aren't optional "later" features — they're the basic UX upgrades that make the new reports clearly better than the Looker ones they replace, and they're the entry point for users to trust the new system. They land in Phase 1 alongside parity.

### Role-gating policy (v3 clarification)
The split between `AdminReport` (7 reports) and `UserReport` (3 reports) is not historical accident; it's an active wpsWatch policy. **Reports are promoted from `AdminReport` to `UserReport` via a Field Team review process** that runs separately from any specific dev project. AI Reports MVP inherits today's gating exactly; the four reports currently SysAdmin-only stay SysAdmin-only in this project. Promotion decisions are out of scope. See OQ-V3-23 for documenting the Field Team process.

### What the ticket asks for
*Same verbatim quote as v2.* The ticket's `wpswatchprodreplica` target does not exist in production; v3 reads from the existing analytics DB instead (clarification, not contradiction).

### Where this fits in Eric's roadmap
Unchanged. Foundation phase of the three-phase MCP vision. "Augment, do not replace."

---

## 2. Goals and non-goals

### Goals

**Phase 1 (MVP).**
- Replace the existing canned Looker reports with an in-product page in wpsWatch that runs the equivalent reports through a new, separate service.
- Mirror today's role gating exactly: `AdminReport` sees the seven SysAdmin reports; `UserReport` sees the three iframe reports today; Volunteers, PhotoUploaders, PhotoTaggers excluded.
- Keep MVP single-org, matching today's Reports behaviour.
- Cover the existing report set with measurable parity (same numbers as Looker for the same parameters).
- **(v3 addition) Four UX upgrades over today's Looker:**
  - **Sortable columns** on every table.
  - **Dynamic filtering** — interactive filter controls per column without leaving the report.
  - **Summarisation** — aggregate totals (count, sum, avg) update live based on applied filters.
  - **Flexible date range** — user-pickable date ranges, not the fixed "previous month" some current reports use.
- **(v3 addition) Extend, don't duplicate.** AI Reports' new database objects live in the existing `wpsWatch.*` schema in `wps-sql-analytics-prod`. New runbooks follow the existing PowerShell-in-Azure-Automation pattern.

**Phase 2.** *(Narrower than v2 — only ad-hoc chat and export.)*
- Free-form chat ("ask wpsWatch a question") gated to `AdminReport` initially, opened to `UserReport` after Phase 2 stabilises.
- File export (PDF, CSV, DOCX, XLSX, KML, GeoJSON, PNG) of report and chat results.
- Cross-org reporting (the new feature becomes the first cross-org reporting surface in wpsWatch).

**Phase 3.** *(Now includes the scheduled-digest workstream, plus saved queries.)*
- Save and re-run custom SQL queries with manifest-pinned schema validation.
- **Saved queries support relative date parameters** ("last 30 days," "this month," "since last Monday") as well as absolute-date parameters.
- Share saved queries within an org (and, possibly, across orgs).
- Diff-against-last-run on saved queries so silent answer drift is caught early.
- **(moved from v2 Phase 2) Scheduled email digest of named/saved queries.** Both a global-timer variant (low effort) and a per-org / per-user local-time variant (larger).
- **(v3 addition) Subscription-to-saved-query** as a unified replacement for site-level `ScheduledReport`. Per-user, per-saved-query, per-cadence subscriptions. May fold into `ScheduledReport` or replace it — design decision deferred.

**Longer-term (post-Phase 3).**
- **GPS data exposure with role-gated coarsening.** Per Matt's comment 4: *"users will want to be able to query geospatial data and get locations (if their permissions allow) in the long term."* This was the v1 security perspective's recommendation, deferred from MVP. Roadmap item with explicit security gating (the original security MUSTs around lat/lon coarsening apply).
- **Composite "camera check" report** — Matt's comment 10 describes a single-pane report that surfaces offline status, low battery, low SIM data, high image volume, and active issues in one view. Higher-leverage than any individual canned report; not Phase 1. Likely Phase 2 or post-Phase 3.

### Non-goals
- No write paths to the wpsWatch DB. The service is read-only.
- No direct calls to the wpsWatch API for end-user data. Back-channel `Wps-Api-Key` calls only if a clear need arises (none identified for MVP).
- No live-operational answers. "Is anyone in zone 7 right now?" type questions are out of scope; the LLM only reports captured photos / past events.
- No GPS coordinates exposed to the LLM in MVP. *(Long-term: see roadmap above.)*
- **(v3 new) No phone / WhatsApp / SMS alerts triggered by report thresholds.** Per Matt's comment 8: that's the wpsWatch alerting subsystem's responsibility, not reporting. US-04 from v2 is removed accordingly. Custom alerting connectors are tracked separately, and are likely beyond Phase 3 if pursued at all.
- No sharing of saved queries across orgs in MVP. Single-org saved queries only.
- No mobile native app. Responsive React surface only.
- No live ground truth features (presence detection, surveillance of staff, APU patrol recommendations).
- **(v3 new) No re-tiering of existing reports.** AdminReport-gated reports stay AdminReport-gated in this project. The Field Team review process is the path for any re-tiering; it runs separately. Action item: kick off the Field Team conversation about which (if any) admin reports they want to promote post-Phase 1 (OQ-V3-24).
- No rewriting of existing Looker dashboards in place.
- No new separate mirror / warehouse DB.
- No Azure SQL read replica.
- No BigQuery in the AI Reports critical path.
- No replacement of the existing weekly deployment-status email. AI Reports' Phase 3 digest is additive.

---

## 3. User stories

Mostly carried from v2. **US-04 removed** (it was an alert, not a report). Some user stories re-tagged to reflect the v3 phasing.

### Device health (the morning sweep) — *highest priority*
- **US-01 — Phase 1 (canned report).** As an OrgAdmin, I want to see which cameras went offline overnight so I can dispatch the field team. Daily, 06:00, list with site name, last-seen time, last battery, region. *(Backed by the v3 Deployment Health Status report — see FR-V3-11.)*
- **US-02 — Phase 1.** As an OrgAdmin, I want to see all cameras below 30% battery sorted worst-first, with site name and access notes. *(Covered by existing battery level report per Matt comment 7. "Access notes" come along when the report joins to Site.)*
- **US-03 — Phase 2 (chat).** As an OrgAdmin, I want to ask "any camera that hasn't sent a photo in 48 hours but was healthy 3 days ago?" — the "something just broke" question Looker can't ask.
- ~~**US-04 — Phase 2 (chat + scheduled).** As an OrgAdmin, I want a phone alert if any camera in the western fence line drops below 20% before 14:00.~~ **Removed in v3** — alerting feature, not reporting. (Comment 8.)

### Photo / detection volume (the rhythm check)
- **US-05 — Phase 1.** As an OrgAdmin, I want to see how many photos came in last night by site, compared to the same night last week (delta).
- **US-06 — Phase 1.** As an OrgAdmin, I want top 10 / bottom 10 cameras by photo volume this week.
- **US-07 — Phase 2 (chat).** As an OrgAdmin, I want to find sites that had a sudden spike in captures yesterday between 22:00 and 04:00, with sparkline of the previous 14 nights.
- **US-08 — Phase 2 (chat).** As an OrgAdmin, I want to slice last week's photos by tag (humans-on-foot vs vehicles vs livestock vs nothing-of-interest).
- **US-09 — Phase 2 (chat + map).** As an IncidentManager, I want every photo capture in the eastern block in the last 24h on a map, animated by time. *(GPS-dependent — see longer-term roadmap in §2.)*

### Deployment coverage
- **US-10 — Phase 1.** As an OrgAdmin, I want to see which sites in my region don't currently have an active deployment.
- **US-11 — Phase 1 (today SysAdmin-only).** As an OrgAdmin, I want active-deployments-per-region-per-site so I can plan re-deployments. *(See [§7 permission model](#7-permission-model); MVP keeps SysAdmin gating, Field Team owns re-tiering.)*

### SIM / connectivity
- **US-12 — Phase 1 (today SysAdmin-only).** As an OrgAdmin, I want to see which SIMs are renewing in the next 30 days, by carrier.
- **US-13 — Phase 2 (chat).** As an OrgAdmin, I want to find prepaid cameras likely to run out of data in the next week based on burn rate.
- **US-14 — Phase 2 (chat).** As an OrgAdmin, I want to slice cameras that went offline this week by failure cause (SIM/battery/hardware/unknown).

### Anomalies / one-offs
- **US-15 — Phase 2 (chat).** As an OrgAdmin, I want to find "ghost cameras": active deployments showing online but with zero photos in 30 days.
- **US-16 — Phase 2 (chat).** As an OrgAdmin, I want to find cameras that were redeployed within 14 days (volunteer swaps without telling ops).
- **US-17 — Phase 2 (chat + export).** As an OrgAdmin, I want to compare this month's detections vs the same month last year, by region — for the donor report.
- **US-18 — Phase 2 (chat).** As an IncidentManager, on the night of an incident at site 23, I want activity at the three nearest cameras in the 6h before/after. *(GPS-dependent.)*
- **US-19 — Phase 2 (chat).** As an OrgAdmin, I want every photo captured between 02:00 and 04:30 last Tuesday across the southern boundary cameras.

### Saved queries (phase 3) — *(v3: relative-date support added)*
- **US-20 — Phase 3.** As an OrgAdmin, I want to save "the morning sweep" (cameras < 30% OR offline > 24h with site name, region, access notes, 7-day photo sparkline) and run it with one click every morning.
- **US-21 — Phase 3.** As an OrgAdmin, I want to save "quiet cameras" (active deployments with photo count < 50% of 30-day average over the last 7 days) and run weekly.
- **US-22 — Phase 3.** As an OrgAdmin, I want a re-run of a saved query to show me a diff against the last run if the count changed materially.
- **US-23 — Phase 3.** As an OrgAdmin, I want to share saved queries with my colleague on the other shift.
- **US-V3-24 — Phase 3.** As an OrgAdmin, I want my saved query for "last 30 days" to **always mean the 30 days ending today** when re-run, not the calendar dates that were live when I saved it. *(New — Matt comment 9. Drives FR-V3-31 and DR-V3-25.)*

### Scheduled email digest (phase 3) — *moved from Phase 2*
- **US-V3-25 — Phase 3.** As an OrgAdmin, I want a daily 05:45 email digest of my saved morning-sweep query so I can see what broke overnight before I'm at my desk.

### Cross-cutting
- **US-24 — Phase 2.** Every chat answer shows rows, count, time window, filters applied.
- **US-25 — Phase 1.** Answers are readable aloud over a radio (lists, not paragraphs).

---

## 4. Functional requirements

### 4.1 Service architecture and surface (phase 1)

- **FR-V2-01 (carries forward).** A new ASP.NET Core 8 service `Wps.Watch.AiReports` deployed as a sibling to `wps.watch.functions.scheduledreporting`, sharing the analytics-DB connection and Key Vault.
- **FR-02.** REST API: `/api/reports`, `/api/reports/{id}/run`, `/api/saved-queries` (Phase 3), `/api/chat` (Phase 2), `/api/exports/{token}` (Phase 2), `/api/subscriptions` (Phase 3).
- **FR-03.** MCP server endpoint (`/mcp`, SSE transport) for the LLM tool-use loop, sharing handlers with REST.
- **FR-V3-04.** A new page added to the wpsWatch React app at `/ai-reports`. Cutover sequence (per Matt comments 1, 18):
  1. **Pilot period:** new menu lives side-by-side with the existing Reports menu in `Header.tsx`.
  2. **SysAdmin first:** once the seven SysAdmin reports are at parity, the SysAdmin-only old menu entries are removed; SysAdmins use the new page exclusively.
  3. **Regular users second:** once the three `UserReport`-tier reports are at parity, the `UserReport` menu entries are also migrated; regular users use the new page exclusively.
  Replaces v2 FR-04.

### 4.2 Authentication and authorization (phase 1)

- **FR-05.** Service validates wpsWatch JWTs locally with the shared HS256 `JwtKey` from Azure Key Vault. Fallback: `/api/auth/introspect` on the wpsWatch API.
- **FR-V3-06.** The service computes the user's authorized organization list **per request** by reading `wpsWatch.wpsWatchLookerUserOrg` from the analytics DB. **The scope mechanism must be designed so it can be extended from `@organization_id` to `@organization_id + @site_id_list` when site-level user roles ship** (per Matt comment 2 / OQ-V3-21). v2's `@organization_id` parameter remains MVP-correct; the manifest's parameter schema and the orchestrator's scope-validation logic should anticipate a second parameter.
- **FR-07.** Role checks via the shared `Wps.Watch.Authorization` package.
- **FR-08.** Volunteers → HTTP 403 from every endpoint except `/health`.
- **FR-09.** `PhotoUploader` and `PhotoTagger` → HTTP 403. Mirror `OperationRoles.cs:446–463`.
- **FR-10.** Service refuses queries with HTTP 503 when measured analytics-DB freshness exceeds the configured threshold (per `wpsWatch_job_run_log`).

### 4.3 Canned reports — MVP (phase 1)

- **FR-V3-11.** The seven SysAdmin Looker reports and the three regular-user reports are exposed as parameterized tool calls **wrapping the existing `wpsWatch.*` views and procs**. Specific mapping:

  | Report | Backing object | Notes |
  |---|---|---|
  | Camera Inventory | `wpsWatch.vw_wpsWatchCameraInv` (existing) | — |
  | Deployed Camera Battery Level | `wpsWatch.vw_wpsWatchDeployedCameraBatteryLevel` (existing) | Covers US-02 per Matt comment 7. |
  | Photo Count By Camera (last month) | `wpsWatch.vw_wpsWatchDeployedCameraCount` (existing) | — |
  | Photo Count By Camera (custom range) | New `wpsWatch.fct_ai_photo_count_daily` + view | Net-new (DR-V2-18). |
  | **Region & Site Totals with Deployment** | `wpsWatch.vw_wpsWatchRegionSiteTotalsV3` (existing, V3 is current) | **MVP tracer bullet** per §10. |
  | SIM Contract Renewal | `wpsWatch.vw_wpsWatchSIMData` (existing) | — |
  | SIM Prepaid Data | `wpsWatch.vw_wpsWatchSIMData` (existing — `EstimatedGBRemaining` via the 0.0001 GB/photo heuristic) | Confirmed dynamically calculated per Matt comment 13. Label in manifest as "Estimated, based on photo volume." |
  | **Deployment Health Status** *(formerly "Top 10 Offline")* | New `wpsWatch.sp_ai_DeploymentHealthStatus(@OrganizationId, @OfflineHours = 24, @WarningHours = 12)` | Returns three tiers: `online`, `warning` (≥12h, <24h), `offline` (≥24h). Per Matt comment 14. See FR-V3-11a. |

  Replaces v2 FR-V2-11.

- **FR-V3-11a (new).** The Deployment Health Status report (and its underlying proc) returns a `health_state` column with values `online` / `warning` / `offline`, plus the raw `last_event_utc` and computed `hours_since_last_event`. Both threshold parameters are exposed in the UI as editable controls; defaults 24/12 per Matt comment 14. The report's parity test compares against the existing org-rollup `wpsWatchCameraTrends` plus dev-team-sourced examples (the 12h warning threshold needs locating — OQ-V3-22).

- **FR-12.** The three regular-user reports exposed via the same mechanism. Same restrictions as today (`UserReport` only).

- **FR-V3-13.** Each canned report is presented in the React page as a card with name, description, parameter form, and Run button. **The result view supports the four UX upgrades** (per Matt comment 0):
  - **Sortable columns** (every column header click-sortable).
  - **Dynamic filtering** (per-column filter inputs that update results in place without re-running the underlying query unless server-side filtering is needed).
  - **Summarisation** (a footer row showing count/sum/avg of visible — i.e., filtered — rows).
  - **Flexible date range** (a date-range picker in the parameter form for any report with a date parameter; defaults to a sensible window per report, but always overridable).
  Replaces v2 FR-13.

- **FR-V3-14.** Output of a canned report includes: parameters used, timestamp of execution, the data-source's last-refresh time, **plus the four UX-upgrade affordances above**. No LLM in this flow. Replaces v2 FR-14.

- **FR-15.** Parity-tested against the equivalent Looker dashboard. Release-gate test asserts row-count and key-aggregate parity for representative parameter sets. *(v3 caveat:* the V3 version of `vw_wpsWatchRegionSiteTotals` is the target; confirm Looker is using V3 before the parity test — OQ-V2-15.*)*

### 4.4 Free-form chat (phase 2)

*(v3 — narrower than v2: digest moved to Phase 3.)*

- **FR-16.** Chat panel gated to `AdminReport` initially; expansion decided after the eval set is mature.
- **FR-17.** LLM hosted server-side (Anthropic Messages API). Browser holds no Anthropic key.
- **FR-V2-18.** Defined MCP tool surface: `list_canned_reports`, `run_canned_report`, `describe_ai_view`, `query_ai_views`, `save_query` (Phase 3), `run_saved_query` (Phase 3).
- **FR-V2-19.** AST validator on generated SQL — see v2 FR-V2-19 for full rule list.
- **FR-V2-20.** Org-scope via explicit `@organization_id` parameter, anticipating the site-level addition (FR-V3-06).
- **FR-21.** DB string content in LLM context wrapped in `<data>...</data>` markers.
- **FR-22.** No follow-up tool call from previous tool output alone.
- **FR-23.** Nightly regression suite of golden question→expected-row-count pairs.
- **FR-24.** Every chat answer shows: question, executed SQL (collapsed/expandable), row count, time window, filters applied, data-source last-refresh time, views referenced, latency. Plain text or sanitised markdown only.

### 4.5 File export (phase 2)

- **FR-25 to FR-29.** Unchanged from v2. CSV / PDF / DOCX / XLSX / KML / GeoJSON / PNG.

### 4.6 Saved queries (phase 3)

- **FR-V3-30.** Saved-query storage location is an open architectural decision. Three options, all viable, deferred to design council:

  | Option | Pro | Con |
  |---|---|---|
  | **Analytics DB** (`wpsWatch.saved_query` table) | SQL-native; co-located with the queried-thing; easy joins to audit tables | Mixes read-only workload with write |
  | **Dedicated SQL DB** | Hard isolation | More infrastructure; coupling-style operational drag |
  | **Cosmos** | Matches existing wpsWatch precedent (`localStorage.savedQueries` is a precedent on the client; Cosmos already holds user preferences and dashboard state — see `wps.watch.api/CLAUDE.md` and Matt comment 5) | SQL queries-as-JSON-strings in a document DB feels off |

  No pre-commit in v3. See OQ-V3-7. Replaces v2 FR-V2-30 / DR-V2-25's single recommendation.

- **FR-V3-31.** Saving a query **does not** call the LLM. The save path:
  1. Strips concrete values to parameters (`WHERE date > '2026-05-01'` → `WHERE date > @startDate`).
  2. **Detects relative-time literals and prompts the user to relativise them.** "I noticed you used `2026-05-01` which is exactly 30 days before today — should I make this 'last 30 days' instead so it stays current?" Per Matt comment 9.
  3. Persists the SQL, the parameter schema (including relative-date parameter types — see FR-V3-31a), and the pinned `manifest_version`.
  Replaces v2 FR-31.

- **FR-V3-31a (new).** The parameter schema supports **two parameter shapes for dates**:
  - **Absolute date** (`@startDate: date`). Value frozen at query-design time.
  - **Relative date / range** (`@lookback: relative_date_range`). Resolved at run time. Allowed values include: `last_N_days`, `last_N_hours`, `since_last_monday`, `this_month`, `last_month`, `this_quarter`, `year_to_date`, `since_<named_event>` (future).
  The save UX guides users toward relative when the literal looks like a recent date. Per Matt comment 9.

- **FR-32.** Re-running a saved query does not call the LLM. The user fills the parameter form (with relative dates pre-bound to their current values) and the runner binds + executes.

- **FR-33.** Saved queries cannot contain hardcoded organization IDs.

- **FR-34.** On every save and run, three layers of validation: syntax, permission, manifest.

- **FR-35.** Deprecated objects → warning; deleted objects → hard error with replacement pointer.

- **FR-36.** Shared saved queries use the running user's `OrganizationUserRole`-derived org scope, not the author's.

- **FR-37.** A saved-query run-history table records every execution.

- **FR-38.** A nightly smoke-runner executes every saved query and flags syntactic / semantic breakage to the owner.

- **FR-39.** Re-running a saved query surfaces a "last run on date X returned N rows; this run returned M rows" diff banner.

### 4.7 Scheduled email digest (phase 3) — *(moved from Phase 2)*

*(v3 — entire section moved to Phase 3 per Matt comment 17.)*

- **FR-V3-48a.** A new timer-triggered Function added to `wps.watch.functions.scheduledreporting` runs on a global timer (env-var-controlled), iterates a new `ai_report_subscription` table, executes each subscription's saved query, renders HTML email, dispatches via the existing SendGrid/Mailjet/Brevo plumbing. Subscriptions are per-user-per-saved-query. Global timer means all subscribers get their digest at the same UTC time; per-org local-time is FR-V3-48b. Replaces v2 FR-V2-48a.

- **FR-V3-48b.** Per-org / per-user local-time delivery. Requires per-subscription `schedule_cron` plus a 24/hr timer that dispatches subscriptions whose local-time-now matches. Larger; may slip beyond Phase 3 to follow-up work. Replaces v2 FR-V2-48b.

### 4.8 Behavioural and trust requirements (phase 1+)

- **FR-40 to FR-45.** Unchanged from v2.

### 4.9 Connectivity and graceful degradation (phase 2+)

- **FR-46.** Timeouts surfaced explicitly.
- **FR-47.** Saved queries cache their last result client-side for use during outages.

---

## 5. Non-functional requirements

*Same as v2.* NFR-01 through NFR-V2-08.

---

## 6. Security and privacy requirements

*Same as v2 §6.* All MUSTs from [`perspectives/security.md` §10](./perspectives/security.md) carry forward with the v2 revisions (`S-V2-MUST-02`, `S-V2-MUST-03`, `S-V2-MUST-09–11`, `S-V2-MUST-14`, `S-V2-MUST-15`, `S-V2-MUST-28`).

**(v3 addition)** When site-level user roles ship (OQ-V3-21), `S-V2-MUST-09` through `S-V2-MUST-11` (org-scope mechanism) need to be re-expressed as `(org_id, site_id_list)` scope. The MVP `@organization_id` parameter is forward-compatible: the scope check expands additively, not destructively.

**(v3 addition)** When GPS data exposure is added to the longer-term roadmap (§2), the v1 security perspective's GPS-coarsening recommendation is reactivated. Specifically:
- Role-gated views: `vReport_Deployment_Coarse` for `Viewer`, `_Fine` for `GpsViewer` and above.
- The "sensitive combination" rule (S-MUST-29: species + GPS + recent timestamp) becomes load-bearing again.
- Anthropic context logging of GPS data needs to be reviewed under the threat model (the v2 doc's rejection of GPS exposure was partly based on "the LLM context is a logging surface").

These are not MVP requirements; they're parking-lot items for the GPS roadmap.

---

## 7. Permission model

### Phase 1 (MVP) — *role-gating policy explicit in v3*
- System Admins (`AdminReport`) see the seven reports.
- `UserReport` users (`OrgAdmin`, `IncidentManager`, `Viewer`, `GpsViewer`) see the three.
- `Volunteer`, `PhotoUploader`, `PhotoTagger` see no AI Reports page.
- Single-org behaviour — current org from JWT.
- **(v3 explicit)** Re-tiering of any individual report from AdminReport → UserReport is **out of scope for this project**. wpsWatch operates a Field Team review process for promoting reports to general availability; AI Reports inherits whatever gating exists at the time of any given report's manifest entry. If the Field Team approves a re-tiering during or after MVP, the change is a content-only edit to the AI Reports manifest. Per Matt comments 11, 12.
- **(v3 action item — OQ-V3-24)** Kick off a conversation with the Field Team about which (if any) of the four currently-AdminReport-only reports should be promoted to UserReport. Per Matt comment 16. This conversation runs in parallel with AI Reports development; its outcome is a manifest edit, not a code change.

### How the service learns and enforces (revised in v3 only for forward-compatibility)
- JWT validated locally with shared `JwtKey`.
- User's organization list looked up per request from `wpsWatchLookerUserOrg` (cache, ~2-min stale) with fallback to a live cross-DB read of `dbo.OrganizationUserRole` if cache age > 5 minutes.
- Role checks via shared `Wps.Watch.Authorization` package.
- **(v3 forward-compat)** When site-level user roles ship, the bootstrap query also reads `wpsWatchLookerUserSite` (or equivalent) for the per-site access list. The orchestrator's scope-validation logic must be designed today so a `@site_id_list` parameter can be added to `vw_ai_*` views without rewriting the auth glue. Per FR-V3-06.

### Phase 2+
- **Cross-org reporting.** Per Matt comment 15: cross-org Reports is dependent on this project landing — AI Reports is the wedge for getting Reports out of `cross-org-overview.md`'s out-of-scope list. AI Reports becomes the **first cross-org reporting surface** in wpsWatch.
- **Site-level roles.** Once they ship in wpsWatch core, AI Reports' scope parameter extends. Coordinate with the wpsWatch core-team's site-level-roles workstream (OQ-V3-21).

---

## 8. Architecture options considered

Same options-comparison as v2 §8 — Hybrid REST + MCP transport (recommended); Hybrid stored-proc-catalogue + gated text-to-SQL query strategy (recommended); shared HS256 `JwtKey` for auth (recommended primary, introspection endpoint as fallback, RS256/Entra as longer-term posture per the May 12 conversation summarized in [`perspectives/auth-options.md`](./perspectives/auth-options.md) — TBD or read existing); extend the analytics DB for data-source (recommended).

**(v3 addition — saved-query storage)** v2 pre-committed to "analytics DB"; v3 reopens this as a three-way choice (analytics DB / dedicated SQL DB / Cosmos) per Matt comment 5. See FR-V3-30 and OQ-V3-7.

**(v3 addition — site-level roles forward-compatibility)** The architecture must accommodate site-level scope in addition to org-level when wpsWatch core ships it. Design the manifest parameter schema, the `@organization_id` parameter pattern, and the auth-bootstrap query so adding `@site_id_list` is additive. Per Matt comment 2 / OQ-V3-21.

---

## 9. Data layer requirements

*Same as v2 §9.* DR-V2-01 through DR-V2-30 from [`perspectives/data-v2.md`](./perspectives/data-v2.md).

**(v3 additions)**

- **DR-V3-31.** Deployment Health Status proc (`sp_ai_DeploymentHealthStatus`) takes two threshold parameters (`@OfflineHours`, `@WarningHours`) with defaults 24 and 12. Returns a `health_state` column. Locating the 12h warning threshold in the existing codebase (OQ-V3-22) is a prerequisite to confirming this is the right defaults pair.
- **DR-V3-32.** Saved-query parameter schema supports the absolute and relative date shapes from FR-V3-31a. Manifest documents the relative date types. Run-time binding of relative dates happens in the orchestrator, not in SQL.
- **DR-V3-33.** Saved-query storage location is undecided in v3 (FR-V3-30, OQ-V3-7). DR-V2-24 (analytics DB-only) is superseded.
- **DR-V3-34.** Org-scope parameter is designed to extend to `(org_id, site_id_list)` when site-level roles ship. The `vw_ai_*` views' `WHERE organization_id = @org_id` clause becomes `WHERE organization_id = @org_id AND site_id IN (@site_id_list)` (or null-list-means-all). MVP omits the second predicate; the manifest's parameter schema is forward-extensible.

---

## 10. Phasing proposal

### Phase 1 (MVP) — *(v3 changes: new tracer bullet + four UX upgrades + Deployment Health Status revision)*

**The tracer bullet (v3 — changed from v2's Top 10 Offline).** Replace **Region & Site Totals with Deployment** end-to-end. Reasoning (per Matt comments 6, 10):

- Top 10 Offline (v2's tracer pick) is partially covered by existing org-level rollup procs; demonstrating it doesn't show clear new value beyond what's already there.
- Region & Site Totals is the higher-value tracer because:
  - It's currently SysAdmin-only (so the new system gives SysAdmins a meaningful UX upgrade, not just a swap).
  - It backs onto `vw_wpsWatchRegionSiteTotalsV3` — well-established, current version.
  - It's the report field-ops teams reach for during the monthly planning meeting (per [`perspectives/user.md` §2 US-11](./perspectives/user.md)) — a clear "the warden cares about this" leverage point.
- The composite "camera check" report Matt describes in comment 10 is a longer-term goal, not Phase 1.

**Tracer bullet scope:**
- `sp_ai_RegionSiteTotals(@OrganizationId)` wrapping `vw_wpsWatchRegionSiteTotalsV3`.
- React page card for Region & Site Totals with the four UX upgrades (sortable columns, dynamic filtering, summarisation, flexible date range).
- New service deployed with REST endpoints `/api/reports`, `/api/reports/region-site-totals/run`, `/api/health`.
- Auth wiring via shared `Wps.Watch.Authorization` package, `wpsWatchLookerUserOrg` lookup, role check against `AdminReport`.
- Replica/analytics-DB connection. (Net-new — first time the connection string is configured in code.)
- `Volunteer` exclusion test, role-gating tests, parity test against the Looker dashboard.
- App Insights instrumentation, per-request audit log.
- New page link added to wpsWatch Reports menu **side-by-side with the existing Looker link** for Region & Site Totals — this is the pilot period.

**Once the tracer is in production:**
- Wrap the remaining six SysAdmin reports + the three regular-user reports as tool calls. Includes the Deployment Health Status revision (FR-V3-11a — 24h offline / 12h warning).
- Implement the four UX upgrades on every report card.
- Build the two new aggregation tables (`fct_ai_photo_count_daily`, `fct_ai_device_event_hourly`).
- Build `wpsWatch_job_run_log` table and the freshness gate.
- Parity-test against all 10 Looker dashboards.
- **Cutover for SysAdmin** — remove the AdminReport-tier Looker menu entries once parity confirmed.

**Revised effort estimate (engineer-weeks):**

| Piece | v2 estimate | v3 estimate | Why |
|---|---|---|---|
| New service skeleton, replica wiring, auth re-impl | ~1.5 weeks | ~1.5 weeks | Unchanged. |
| `Wps.Watch.Authorization` extraction | ~0.5 weeks | ~0.5 weeks | Unchanged. |
| `reporting` SQL changes (10 wrapping procs + 2 new agg tables + Deployment Health Status with dual threshold) | ~1.5–3 weeks | **~2–3 weeks** | Slightly higher: Deployment Health Status dual-tier logic adds a half-week; sourcing the 12h threshold (OQ-V3-22) may add another. |
| Parity tests, observability, audit, deploy | ~2 weeks | ~2 weeks | Unchanged. |
| React page, parameter forms, table render, **four UX upgrades**, CSV export | ~2 weeks | **~3–4 weeks** | The four UX upgrades from Matt comment 0 are real net-new front-end work. Sortable columns and the date-range picker are cheap; dynamic filtering and live summarisation require a table component with real filter state. |
| Cutover sequencing (SysAdmin first, then regular) | n/a | ~0.5 weeks | Coordination overhead. |
| **Phase 1 total** | **7.5–10 weeks** | **~9.5–13 weeks** | Up from v2: the UX upgrades and dual-threshold work eat the savings the smaller tracer bullet creates. |

### Phase 2 — *(narrower than v2: chat + export + cross-org only)*

**Substrate checkpoint — gate before implementation begins.** Phase 1 leverages the existing analytics-DB-plus-external-tables pattern that works well for canned reports. Phase 2's free-form chat workload has materially different shape (unpredictable queries, no guarantee of pre-aggregation coverage, potential to put unbounded load on production via cross-DB external-table reads). Before Phase 2 implementation begins, the team must re-evaluate the substrate decision using real Phase 1 usage data. Three options to compare: (a) continue on the existing pattern with heavier investment in pre-aggregation tables; (b) introduce an Azure SQL read replica as a dedicated ad-hoc substrate; (c) stand up a small analytical warehouse. See OQ-V3-26.

- Free-form chat panel gated to `AdminReport`.
- MCP tool catalogue + `query_ai_views` against the `vw_ai_*` set.
- AST validator + parameter-injection middleware.
- Anthropic host loop with streaming, tool-use orchestration, audit.
- Eval suite of golden questions (FR-23).
- File export pipeline (all formats; net-new).
- Cross-org reporting (the first cross-org reporting surface in wpsWatch).

**Revised effort: ~10–18 engineer-weeks** (down from v2's 12–22, since the digest workstream has moved to Phase 3). **Substrate-decision-dependent** — option (a) fits in this range, options (b) and (c) add 4–8 weeks and real infrastructure cost.

### Phase 3 — *(now includes scheduled email digest + saved queries with relative dates)*

- `wpsWatch.saved_query` / `saved_query_run` storage (location per FR-V3-30 / OQ-V3-7).
- Save / list / edit / delete UI with **relative-date affordance** (FR-V3-31, FR-V3-31a).
- Three-layer validation, deprecation handling, smoke runner, diff banner.
- Sharing within an org with running-user scope.
- **Scheduled email digest** (FR-V3-48a global-timer variant; FR-V3-48b per-org local-time variant — may slip beyond Phase 3).
- **Subscription model unification** decision: extend `ScheduledReport` table vs build new `ai_report_subscription` table. Owner: design council.

**Revised effort: ~10–16 engineer-weeks** (up from v2's 6–10, since the digest workstream is now here).

### Longer-term (post-Phase 3)

- GPS data exposure with role-gated coarsening.
- Composite "camera check" report.
- Cross-org saved queries.
- Migration of the API's JWT signing from HS256 to RS256/Entra (per the auth options discussion; tracked as S-SHOULD-05).

---

## 11. Open questions for the human owner

The v2 list, with v3 status updates and new items.

### Resolved in v3 (no longer open)

- ~~**OQ-V2-1** — The SysAdmin-only report list~~ — **resolved (Matt comment 11).** Seven reports is the full scope; additional ones are future work.
- ~~**OQ-V2-2** — Should regular users gain access to the four currently-SysAdmin reports?~~ — **resolved (Matt comment 12).** Not in this project. Field Team owns re-tiering via an existing review process. AI Reports inherits today's gating.
- ~~**OQ-V2-3** — SIM Prepaid Data source of truth~~ — **resolved (Matt comment 13).** Dynamically calculated as the v2/data-v2 finding said. Match the existing 0.0001 GB/photo heuristic; label honestly.
- ~~**OQ-V2-4** — "Top 10 Offline" threshold~~ — **partly resolved (Matt comment 14).** 24h offline, 12h warning. The 12h warning threshold's source-of-truth in code is not yet located — see OQ-V3-22.
- ~~**OQ-V2-5** — Mirror DB beyond the replica~~ — resolved in v2.
- ~~**OQ-V2-8** — Cross-org reporting timing~~ — **resolved (Matt comment 15).** Cross-org Reports depends on this project; timing flexible.
- ~~**OQ-V2-9** — Scheduled email digest cadence~~ — **resolved (Matt comment 17).** Phase 3.
- ~~**OQ-V2-12** — iframe-based Reports cutover plan~~ — **resolved (Matt comments 1, 18).** Side-by-side during pilot; SysAdmin first, then regular users.

### Still open from v2

- **OQ-V2-6.** JWT migration to RS256/Entra. Auth-options conversation (May 12) summarised separately; awaiting decision.
- **OQ-V2-10.** Anthropic vs. another model provider.
- **OQ-V2-11.** Existing Looker SQL extraction. Partially mitigated by reading `wpswatch.reporting`; specific Looker data-source URLs still useful for parity testing.
- **OQ-V2-13.** AI Reports repo layout. Recommendation: new repo as sibling.
- **OQ-V2-14.** Runbook refresh cadence. Needed to calibrate the freshness gate.
- **OQ-V2-15.** Which `vw_wpsWatchRegionSiteTotals` version is in Looker. **More urgent in v3** since Region & Site Totals is the tracer bullet.
- **OQ-V2-16.** BigQuery sync deprecation.
- **OQ-V2-17.** Subscription model unification (`ScheduledReport` extension vs new table).
- **OQ-V2-18.** `DeploymentReportController` relationship — confirm AI Reports doesn't call it.
- **OQ-V2-19.** Runbook plain-text credentials (separate ticket).
- **OQ-V2-20.** Analytics-DB deployment automation.

### New in v3

- **OQ-V3-7.** Saved-query storage location: **analytics DB vs dedicated SQL DB vs Cosmos**. Matt's comment 5 floats Cosmos. v2's "use the analytics DB" pre-commit is withdrawn. Design-council call.
- **OQ-V3-21.** **Site-level user roles timeline.** Per Matt comment 2. Needs a date or quarter from the wpsWatch core team. If site-level roles ship during the AI Reports MVP window, FR-V3-06 / DR-V3-34 needs to be implemented in Phase 1 rather than left forward-compatible.
- **OQ-V3-22.** **Where is the 12h warning threshold encoded?** The 24h threshold is in `sp_wpsWatchDeployedCameraTrends`. The 12h tier Matt named is not visible in that proc; possibilities are `tvf_wpsWatchRegionSiteTotalsV3`, a Looker computed dimension, or operational convention. Needs locating before Phase 1's Deployment Health Status report ships.
- **OQ-V3-23.** **Field Team review process.** What does it look like? Documented? Triggered by what? Does AI Reports need to integrate with it (e.g. surface a "report under field-team review" affordance), or is it entirely out-of-band? Per Matt comment 12.
- **OQ-V3-24.** **Action item: kick off Field Team conversation about admin-report promotions.** Per Matt comment 16. Runs in parallel with AI Reports development; not a tech decision but a process step the dev team owes the Field Team.
- **OQ-V3-25.** **Saved-query share-with-other-orgs in MVP?** v2 said no (single-org only). Matt didn't comment on this directly, but if shared queries cross orgs at all, the security perspective's "running user's scope applies" rule (S-MUST-36) is the answer; if not, the MVP shape is simpler. Confirm.
- **OQ-V3-26.** **Phase 2 substrate checkpoint.** The current architecture builds Phase 1 on the existing analytics-DB pattern (external tables proxying to production, plus scheduled aggregation tables on the same Azure SQL Server as production). This pattern fits canned reports cleanly but has known limitations for free-form ad-hoc queries: (i) cross-DB external-table reads can be slow on large source tables like `Photo`; (ii) unbounded ad-hoc workload puts unpredictable load on production via those external-table reads; (iii) pre-aggregation only helps for query shapes we anticipate. Phase 2's chat feature should not be assumed to fit the same substrate. Before Phase 2 implementation begins, the team should review Phase 1 usage and decide between: (a) **continue on the existing pattern** with heavier investment in pre-aggregation tables — cheapest, but ad-hoc questions outside the pre-aggregated set will be slow; (b) **introduce an Azure SQL read replica** as a dedicated ad-hoc substrate — sub-second lag, no production-load risk, but real money for the tier upgrade and every sensitive column gets replicated (need column-level DENYs); (c) **stand up an analytical warehouse** (e.g. Synapse Serverless) — designed for ad-hoc analytical workload, but real engineering investment and a new substrate to operate. Decision affects budget, Phase 2 timeline, and the AI Reports service's data-source configuration. Mitigation: design the Phase 1 service to be substrate-portable so this choice is reversible without code rewrite.

---

## 12. Glossary

*Same as v2 §12*, plus:

- **Field Team review process:** wpsWatch's existing mechanism for promoting `AdminReport`-only reports to `UserReport` (general availability). Runs separately from any specific dev project. AI Reports inherits report gating at the time of manifest authoring; the Field Team can flip a report's tier later via a manifest edit.
- **Health state (Deployment Health Status):** Three tiers — `online` (event within 12 hours), `warning` (event 12–24 hours ago), `offline` (no event in 24 hours or more). Replaces the binary online/offline model in v1/v2.
- **Relative date / range parameter:** A saved-query parameter whose value is computed at run time relative to the current moment (e.g., `last_30_days`, `this_quarter`, `since_last_monday`). Distinct from absolute-date parameters whose values are stored at save time.
- **Site-level user role:** A wpsWatch role tier below organization-level, currently in active development. Will allow users to be granted access to specific sites within an organization rather than the whole org. Not yet in the codebase. Affects AI Reports' scope parameter.

---

## Appendix A — v2 → v3 requirement mapping

| v2 ID | v3 ID | Change |
|---|---|---|
| FR-V2-01 | (unchanged, kept v2 ID) | — |
| FR-02–FR-03 | (unchanged) | — |
| FR-04 | FR-V3-04 | Adds explicit SysAdmin-first cutover sequence (Matt comments 1, 18). |
| FR-05 | (unchanged) | — |
| FR-V2-06 | FR-V3-06 | Adds forward-compatibility for site-level roles. |
| FR-07–FR-10 | (unchanged) | — |
| FR-V2-11 | FR-V3-11 + FR-V3-11a | Mapping table revised; Top 10 Offline → Deployment Health Status with dual threshold. |
| FR-12 | (unchanged) | — |
| FR-13 | FR-V3-13 | Adds four UX upgrades (sortable, dynamic filter, summarisation, flexible date range). |
| FR-14 | FR-V3-14 | Same. |
| FR-15–FR-24 | (unchanged) | — |
| FR-25–FR-29 | (unchanged) | — |
| FR-V2-30 | FR-V3-30 | Storage decision reopened (analytics DB / dedicated / Cosmos). |
| FR-31 | FR-V3-31 + FR-V3-31a | Adds relative-date parameter support. |
| FR-32–FR-39 | (unchanged) | — |
| FR-44 | (unchanged from v2; kept v2 ID) | — |
| FR-46–FR-47 | (unchanged) | — |
| FR-V2-48a | FR-V3-48a | Moved from Phase 2 to Phase 3. |
| FR-V2-48b | FR-V3-48b | Moved from Phase 2 to Phase 3. |
| **(new)** US-V3-24 | — | Relative-date saved query user story. |
| **(new)** US-V3-25 | — | Phase-3 scheduled digest user story. |
| **(removed)** US-04 | — | Phone-alert story moved to non-goal. |
| NFR-01–NFR-V2-08 | (unchanged) | — |
| S-MUST-01–S-NICE-06 | (unchanged from v2; site-level extensibility note added in §6) | — |
| DR-V2-01–DR-V2-30 | (unchanged from v2) | — |
| **(new)** DR-V3-31 | — | Deployment Health Status dual-threshold proc. |
| **(new)** DR-V3-32 | — | Saved-query relative-date parameter schema. |
| **(new)** DR-V3-33 | — | Saved-query storage location undecided. |
| **(new)** DR-V3-34 | — | Org-scope extensibility for site-level roles. |

---

*End of v3 requirements document.*
