# AI Reports — Requirements Delta (post-discovery of `wpswatch.reporting` and `wps.watch.functions.scheduledreporting`)

**Status:** Findings only — recommendations for changes to [`ai-reports-requirements.md`](./ai-reports-requirements.md). Nothing in the main doc has been modified.
**Date:** 2026-05-08.

The existing requirements doc was written without knowledge of two repos that materially change the architecture story:

- `wpswatch.reporting` — a pure-T-SQL repository (views, procs, UDFs, aggregation tables) that backs the Looker dashboards today.
- `wps.watch.functions.scheduledreporting` — an Azure Functions app (v4, .NET 8 isolated) that sends weekly deployment-status emails and syncs user-org mappings to BigQuery for Looker.

The short version: **the "no reporting views, no replica configured, no scheduling primitive" finding from the exploration notes was wrong.** A reporting-data architecture exists; the scheduled-reporting plumbing exists; Looker's user-org filtering is not just URL params. The MVP needs to extend existing infrastructure, not invent parallel infrastructure.

---

## 1. TL;DR — what the doc gets wrong

| Existing requirement | What's actually true | Action |
|---|---|---|
| **DR-01:** read from `wpswatchprodreplica` (read replica) | WPS does not use a read replica. The team chose a **separate analytics database** (`wps-sql-analytics-prod`) on the same SQL Server, populated from `wpswatch-prod` via **external tables** (cross-DB elastic query). | Replace the "replica" framing with "the existing analytics DB." Re-evaluate §1 (replica vs mirror) — the actual answer is a third option my doc didn't compare. |
| **DR-02:** create a new `reporting` schema | An analytics schema already exists with 15 views, 7 stored procs, 9 UDFs, 10 aggregation tables, 29 external tables. | Extend the existing `wpsWatch.*` schema in `wps-sql-analytics-prod`, don't create a parallel `reporting` schema. |
| **DR-16/17/18:** build `fct_photo_count_daily`, `fct_device_event_hourly`, `fct_battery_snapshot_daily` from scratch | Analogues already exist: `wpsWatchDeployedCameraCount`, `wpsWatchCameraTrends`, `wpsWatchSIMDataReport`, `vw_wpsWatchRegionSiteTotals` (with V2 and V3 hierarchies), etc. | Re-scope the pre-aggregation work as **extend, not build**. The shape and refresh cadence are decided already. |
| **DR-20:** Azure SQL elastic jobs for refresh | The existing refresh mechanism is **Azure Automation runbooks (PowerShell)**, not elastic jobs. Four runbook scripts call the stored procs on a schedule. | Use the existing runbook pattern, not a different scheduling primitive. |
| **DR-27:** Add `Device.PrepaidDataUsedGb` + daily sync to OLTP | Worth checking — `sp_wpsWatchSIMDataReport` may already compute this against an external source. The stored proc name suggests so; needs reading. | Open question — does the SIM data already get pulled here? |
| **§8 "no signed Looker URLs, security relies on Looker's access controls"** | Partially true. Looker's row-level filtering is fed by a SQL cache table (`wpsWatchLookerUserOrg`) refreshed every ~2 minutes by `NewUserOrgSyncFunction` syncing into a BigQuery table `wpsWatchUserOrgId.wpsUserOrg`. There IS a server-side filter — it's just three hops away. | Update §3 "Looker handoff" to describe this pipeline accurately. The seam being replaced is larger than "iframe + query params." |
| **FR-48 (phase 2):** scheduled email digest at 05:45 local time | The existing `ReportingFunction` already sends weekly deployment-status emails via SendGrid (US) / Mailjet (EU) / Brevo (EU alt), with regional routing already wired up. The HTML rendering + multi-provider sending is **done**. **But**: the schedule is a single global timer (`TIMER_FREQUENCY` env var); per-org local-time delivery is **not** supported. Subscriptions are site-level (`ScheduledReport` table, comma-separated `EmailAddresses`). | FR-48 splits in two: (a) reuse existing email plumbing — small win, and (b) build per-org / per-user local-time scheduling — net-new, real work. Effort estimate changes. |
| **FR-25 (phase 2):** file export in PDF/CSV/DOCX/XLSX/KML/GeoJSON/PNG | Zero export plumbing in either existing repo. Today's "report" is an HTML email body only. | Estimate stands. This is fully net-new. |
| **§7 "How the new service learns and enforces \[org scope\]"** | A precedent exists: `wpsWatchLookerUserOrg` cache table maintained by `sp_wpsWatchUserOrgUpdate`. Currently invoked by both a stored proc job and the `NewUserOrgSyncFunction`. | Option to consider: reuse this cache table for the AI Reports service's org-scoping (with a freshness gate matching today's cadence). May avoid the `SESSION_CONTEXT` complexity. |
| **§8 (architecture) auth recommendation:** share HS256 `JwtKey` via Key Vault | The Functions app's pattern is **`Wps-Api-Key` to call the wpsWatch API**, not JWT. It does not import `Wps.Watch.Data` or `Wps.Watch.Authorization`. | The "service-to-service via API key" pattern is in production today — but it's not user-aware (API keys are always single-org per [exploration notes §3](./ai-reports-exploration-notes.md#3-auth-surface-for-an-external-service)). The shared-JwtKey recommendation for AI Reports still stands; API key is for back-channel only. |
| **NFR-04 / S-MUST-14:** replica-lag freshness gate (60s threshold) | No replica → no replica lag. The freshness story is "how stale is the analytics DB?" The analytics DB is refreshed by Azure Runbook stored procs on a schedule we should read. External tables are live (immediate) but slow. | Replace "replica lag" with "analytics-DB refresh lag." Threshold becomes "older than N runbook runs." Different telemetry surface. |

---

## 2. What the existing reporting architecture actually looks like

A simplified flow, since the exploration notes had this partly wrong:

```
+-------------------------+                     +-------------------------+
|     wpswatch-prod       |   external tables   |  wps-sql-analytics-prod |
|     (OLTP primary)      | <-----------------> |  (analytics database)   |
|                         |  elastic DB query   |                         |
|  Tables: Device,        |                     |  Schema: wpsWatch.*     |
|  Deployment, Photo,     |                     |  - 10 agg tables        |
|  Site, Organization,    |                     |  - 15 views             |
|  OrganizationUserRole,  |                     |  - 7 stored procs       |
|  ScheduledReport...     |                     |  - 9 UDFs               |
+-------------------------+                     |  - 29 external tables   |
        ^                                       |  - wpsWatchLookerUserOrg|
        | API calls (Wps-Api-Key)               +-------------------------+
        |                                             ^             ^
        |                                             |             |
+--------------+                                      |             | Azure Runbooks
|  wpsWatch.   |                          Looker      |             | (PowerShell)
|  Api         |                          Studio -----+             | every N hours
+--------------+                          datasources               |
        ^                                                            |
        |                                                       +--------+
        | Wps-Api-Key                                           | refresh|
        |                                                       | procs  |
+--------------+                                                +--------+
|  Functions:  |
|  Scheduled-  |---> SendGrid / Mailjet / Brevo (HTML email out)
|  Reporting   |
|              |---> BigQuery (wpsWatchUserOrgId.wpsUserOrg sync)
+--------------+      ^
                      |
                  Looker uses BigQuery to enforce user-org
                  filtering on top of the SQL data sources
```

Three things to internalise from this picture:

1. **There's no Azure SQL read replica.** The team's chosen pattern is "separate analytics DB on the same server, cross-DB queries via external tables." This is what my data perspective should have evaluated as option D in the "replica vs mirror" comparison.
2. **The analytics DB is the natural home for AI Reports.** It already has a refresh cadence, a denormalised schema, a read-only SQL user (`wpswatch-looker`), and a user-org cache (`wpsWatchLookerUserOrg`). Building a parallel `reporting` schema on a replica that doesn't exist is the wrong shape.
3. **Looker's row-level security is fed by a SQL → BigQuery sync.** The chain is `OrganizationUserRole (OLTP)` → `sp_wpsWatchUserOrgUpdate` → `wpsWatchLookerUserOrg (analytics DB)` → `NewUserOrgSyncFunction` → BigQuery → Looker. AI Reports can shortcut this by reading directly from `wpsWatchLookerUserOrg` (or the OLTP table); we do not need BigQuery in the AI flow.

---

## 3. Section-by-section recommended changes to the main doc

### §1 Background and motivation
- **Add a paragraph** describing the existing reporting architecture (analytics DB + external tables + runbook refresh + Functions for email + BigQuery for Looker user-org sync). Today's reader of the doc believes there's no server-side reporting story; there is.
- **Correct the "no `ReportsController`" framing** in the exploration notes summary. There's no Reports controller in `wps.watch.api`, but there IS a `DeploymentReportController` (called by the Functions app — referenced in the Functions repo at `example.settings.json:14`). Worth checking in `wps.watch.api` directly.

### §2 Goals / non-goals
- **Goals:** add "extend (not replace) the existing analytics DB schema and scheduled-reporting Functions app where they overlap with AI Reports features."
- **Non-goals:** add "no replacement of the existing weekly deployment-status email (which the Functions app already sends). The AI Reports email digest is additive."
- **Non-goals:** add "no BigQuery in the AI Reports critical path. BigQuery's role is the Looker user-org sync; AI Reports reads SQL directly."

### §4 Functional requirements

- **FR-01** (new ASP.NET Core 8 service) — *unchanged in spirit* but the service is now positioned as **one new C# service alongside the existing Functions app and analytics DB**, not a greenfield third project that knows nothing of them. Reword to reflect.

- **FR-05** (auth via shared `JwtKey`) — *unchanged*. The Functions app uses `Wps-Api-Key` because it's a back-channel service, not user-acting. AI Reports is user-acting; JWT is right.

- **FR-06** (read `OrganizationUserRole` per-request from the replica) — **change to read from `wpsWatchLookerUserOrg` (analytics DB) OR `OrganizationUserRole` (primary DB)** depending on the freshness story. Add a sub-decision to the open-questions section: read live OLTP (always fresh, primary load) vs. read the cache table (already there, slightly stale).

- **FR-10 / NFR-04** (replica lag freshness gate, 60s threshold) — **replace** with "analytics-DB refresh-age gate" measured against `reporting.job_run_log` (or equivalent table that records when each refresh procedure last completed). Threshold likely needs to be hours, not seconds, because the existing refresh cadence is hours-scale.

- **FR-11** (canned reports as new stored procs `usp_CameraInventory`, …) — **change to**: extend or wrap the existing `sp_wpsWatch*` procs and `vw_wpsWatch*` views. Specifically:
  - Camera Inventory → already exists as `vw_wpsWatchCameraInv` and feeds Looker today.
  - Battery Level → check `wpsWatchCameraTrends` for coverage.
  - Photo Count by Camera → `wpsWatchDeployedCameraCount` covers part of this.
  - Region & Site Totals → `vw_wpsWatchRegionSiteTotals` (V1/V2/V3) — three versions exist, suggesting iteration.
  - SIM Contract Renewal / Prepaid Data → `vw_wpsWatchSIMData` + `sp_wpsWatchSIMDataReport`.
  - Top 10 Offline → check `wpsWatchCameraTrends` and the runbook scripts.
  - The MVP tracer-bullet ("replace Top 10 Offline end-to-end") changes: it's now "expose the *existing* trend data through the new service with proper auth," which is *less work*, not more.

- **FR-48** (scheduled email digest at 05:45 local) — **split into FR-48a (reuse existing pipeline)** and **FR-48b (per-org local-time scheduling — net-new)**:
  - FR-48a: when the digest content matches today's weekly-deployment shape, add it as a new timer + template in the Functions app. Low effort.
  - FR-48b: per-org/per-user local-time delivery requires either (1) one Function timer per hour, with each run reading a `ScheduledReport` table that has per-org local-time, or (2) a redesign to a queue-driven model. Larger.

- **FR-25 to FR-29** (file export PDF/CSV/DOCX/XLSX/KML/GeoJSON/PNG) — unchanged. The existing Functions app sends HTML only; export plumbing is net-new wherever it lands.

### §6 Security
- **S-MUST-02** (`ApplicationIntent=ReadOnly`) — irrelevant if we connect to the analytics DB (always read-only by design). Replace with "AI Reports uses the existing `wpswatch-looker` SQL user (or a new equivalent) with `db_datareader` on `wps-sql-analytics-prod` only." This is *simpler* than the original.
- **S-MUST-09 to S-MUST-11** (`SESSION_CONTEXT`-based row filtering) — re-evaluate. If we reuse `wpsWatchLookerUserOrg` as the org-scope source, we can wrap every view in a `JOIN wpsWatchLookerUserOrg ON …` clause that scopes by the `(UserId, OrganizationId)` table content — a different mechanism that's already in use. `SESSION_CONTEXT` is still viable if we want belt-and-braces.
- **S-MUST-28** (sensitive column exclusion) — verify against `wpswatch.reporting`'s actual schema. Some sensitive columns may already not be projected into views, which is the right shape; others may need to be denied at the SQL level.

### §9 Data layer
- **DR-01** — **rewrite**. Read from `wps-sql-analytics-prod`, not `wpswatchprodreplica`. The replica framing was incorrect.
- **DR-02** — **rewrite**. Extend the existing `wpsWatch.*` schema rather than create a new `reporting` schema. Net-new objects use the same schema for consistency.
- **DR-03** — adapt: use `wpswatch-looker` SQL user (existing) or a new `ai_reports_reader` with the same grants pattern.
- **DR-08** (semantic manifest `data-manifest.json`) — generate it from the existing analytics DB, not from a new schema we invent.
- **DR-09** (GPS columns absent) — verify against existing views. `vw_wpsWatchSIMData` and `vw_wpsWatchDeployedCameraCount` likely don't include GPS today (because Looker doesn't display GPS); confirm before claiming.
- **DR-16 to DR-19** (pre-aggregations + dimensions) — **rewrite as "extend existing aggregations where coverage is partial; new ones only if the gap is real."** Adds a discovery step at the start of MVP work.
- **DR-20** (refresh mechanism) — change "Azure SQL elastic jobs" to "Azure Automation runbooks" to match the existing pattern. Or argue for migration if the existing approach is painful.
- **DR-27** (`SIM Prepaid Data` source) — re-investigate against `sp_wpsWatchSIMDataReport`. The data may already be sourced.
- **DR-30** (deployment ordering) — adapt to two-repo coordination (analytics SQL repo + AI Reports service repo) instead of just one.

### §10 Phasing
- **Phase 1 effort estimate (10–14 engineer-weeks)** — likely *decreases* because the analytics DB heavy lift is partly done. New estimate range: ~6–10 weeks for the equivalent scope. The dominating piece becomes the new service (REST + MCP) and the parity testing against the existing Looker dashboards, not the SQL.
- **Phase 2 effort (14–24 engineer-weeks)** — the email-digest piece *decreases* if we extend the Functions app (FR-48a). Per-org local-time scheduling (FR-48b) is its own line item.
- **The tracer bullet** ("replace Top 10 Offline end-to-end") gets cheaper because the data shape is already produced by an existing procedure. The new work is just: read it via a parameterised stored proc, authenticate the caller, scope by org via the existing cache table, render in the new page.

### §11 Open questions
The list grows. Add these:

- **OQ-13.** Should the AI Reports service be deployed as **a new project in `wpswatch.reporting`/`wps.watch.functions.scheduledreporting`**, or as a third repo as the doc currently assumes? If the AI Reports service has a meaningfully different lifecycle (web service vs. SQL-only / Functions-only), a new repo is justified. If it's similar in size and ops to the Functions app, putting it alongside may be simpler.
- **OQ-14.** Does `sp_wpsWatchSIMDataReport` already pull SIM-usage data from Twilio or the carrier API? If yes, DR-27 (add `Device.PrepaidDataUsedGb` columns) may be unnecessary work.
- **OQ-15.** Three versions of `vw_wpsWatchRegionSiteTotals` (V1/V2/V3) suggest historical iteration. Which version is the current Looker dashboard reading? The MVP parity check needs to target the same one.
- **OQ-16.** The user-org sync to BigQuery (`NewUserOrgSyncFunction`) maintains Looker's row-level security. If we cut over from Looker to AI Reports for the canned reports, do we still need the BigQuery sync? (Yes for any remaining Looker usage; potentially deprecate when fully migrated.)
- **OQ-17.** The existing scheduled email subscription is **site-level**, not user-level (one `ScheduledReport` row per Site, with `EmailAddresses` as a comma-separated string). AI Reports' "save my query, email me the result every Monday at 06:00" model implies **per-user-per-query** subscriptions. Is the data team OK with redesigning the subscription model, or should AI Reports build its own subscription table and leave the existing one alone?
- **OQ-18.** The `DeploymentReportController` on the wpsWatch API (referenced by `ReportingFunction` at `example.settings.json:14`) is a Reports controller my exploration notes claimed didn't exist. What endpoints does it expose? Could AI Reports reuse it? *(I would normally read the API repo now to find this, but you asked me not to change anything yet — flagging instead.)*

---

## 4. New open question for you specifically

The biggest decision the doc currently makes that I want your input on before I rewrite it:

> **Where should the AI Reports service actually live?**
>
> The current doc says "new C# service, new repo, new Azure resource." With the existing repos in scope, three options:
>
> 1. **New repo, new Azure resource** (today's doc). Cleanest separation, but you now own three reporting-adjacent codebases (`wpswatch.reporting`, `wps.watch.functions.scheduledreporting`, and the new one).
> 2. **Extend `wps.watch.functions.scheduledreporting`** to include the AI Reports endpoints as new Functions. Pro: reuses email, scheduling, Key Vault, BigQuery pieces. Con: Azure Functions is awkward for an MCP/SSE host loop and for a long-running LLM chat conversation. *Probably not the right fit.*
> 3. **New ASP.NET Core service** (as the doc says) but explicitly positioned as **a sibling to the Functions app**: shares the analytics DB connection, shares the `Wps-Api-Key` pattern for any back-channel calls, shares Key Vault. Three repos but a clear architectural relationship between them.
>
> My recommendation is **option 3**, with a paragraph in the doc making the relationships explicit and adding the existing repos to the file-citation index. The MCP/SSE/chat-streaming shape doesn't fit Functions well; the analytics DB pattern fits everything.

---

## 5. What I'd do next

Pending your approval:

1. Read the `DeploymentReportController` in `wps.watch.api` (the existing reports controller I missed) — answers OQ-18 and may unearth more findings.
2. Read `ScheduledReport.cs` in `wps.watch.api` — needed to update FR-30 (saved-query storage) properly.
3. Read the four PowerShell runbook scripts in `wpswatch.reporting/Jobs/` — to know the actual refresh cadence so the freshness-gate threshold has a number, not a guess.
4. Update [`ai-reports-exploration-notes.md`](./ai-reports-exploration-notes.md) — at minimum, surprise #4 ("no replica connection string configured") needs to become "no replica — the team chose a separate analytics DB instead" and surprise #5 ("no reporting views, no stored procs") needs to be retracted entirely.
5. Update [`ai-reports-requirements.md`](./ai-reports-requirements.md) per §3 of this delta.
6. Re-run the data-architect perspective with the new information — that doc's "replica vs mirror" recommendation is the section most invalidated by the discovery, and it deserves a fresh take.

Step 6 is optional — the delta above captures the changes, but a clean rewrite of the data perspective would read better than the current "recommendation + delta" combination.
