# AI Reports — Requirements Document (v5)

**Status:** Draft — for wpsWatch dev team review.
**Supersedes:** [`ai-reports-requirements-v4.md`](./ai-reports-requirements-v4.md) (v4, 2026-05-14). v1, v2, v3, v4 preserved unchanged for comparison.
**Authors:** Synthesis of Stage 1 codebase exploration ([v3 notes](./ai-reports-exploration-notes-v3.md)) + four parallel persona reviews (backend, security, user, data-v2) + Matt Hron's v1 review comments + the post-v4 substrate decision.
**Date:** 2026-05-14.

> **How to read this document.** Sections 1–10 are the requirements proper. Section 11 lists open questions the dev team needs to resolve before the design doc. Section 12 is a glossary. File-path citations point to one of four repos: `wps.watch.api`, `wps.watch.web`, `wpswatch.reporting`, and `wps.watch.functions.scheduledreporting`.

---

## What changed from v4 to v5

**One decision drives all of v5's changes: the team has chosen a fully mirrored read-only DB at `wpswatchprodreplica.database.windows.net` (Azure SQL geo-replication of the OLTP primary) as the runtime data source for AI Reports.** This resolves OQ-V3-26 (the Phase 2 substrate checkpoint) — it's no longer a checkpoint, the answer is the replica, decided in Phase 1.

This decision overrides the v2 data-architect's "extend the existing analytics DB" recommendation. The data-v2 perspective is preserved as a historical artifact but no longer reflects the implemented architecture.

Substantive ripple effects:

1. **AI Reports is decoupled from the existing analytics DB and `wpswatch.reporting` repo at runtime.** The analytics DB continues to exist for Looker; AI Reports doesn't read from it. The relationship is informational: the analytics DB's reports are useful as parity references during Phase 1 testing, but they're not in the AI Reports data path.
2. **User-org bootstrap source.** v4 read `wpsWatchLookerUserOrg` from the analytics DB. v5 reads `dbo.OrganizationUserRole` directly from the replica — same data, fresher, no cache layer.
3. **Freshness model.** v4 measured "analytics-DB refresh age" via `wpsWatch_job_run_log`. v5 measures **replica lag** — typically sub-second for Azure SQL geo-replication, but bursty under load.
4. **Sensitive columns.** A fully mirrored DB replicates everything in the primary, including `Site.DasToken`, `User.PasswordHash`, every secret on `Site`, `Device`, `Key`, `ImageRecognitionService`. Column-level access controls go from "secondary defense" to **the primary defense**.
5. **Pre-aggregation.** Read replicas are inherently read-only. The aggregation tables in the analytics DB (which made some queries fast in v4) can't be replicated to AI Reports' replica. v5 accepts raw-OLTP query performance for Phase 1 (it's fine for the canned reports) and flags a separate writable substrate for AI-Reports-owned aggregations as a Phase 2 / Phase 3 consideration if performance demands it.
6. **Cost.** Azure SQL geo-replication requires the primary on Business Critical, Premium, or Hyperscale tier. This is real money the team has accepted as part of the decision.

Numbering: requirements that change in v5 get a `-V5-` infix. v4 requirements not affected by the substrate decision (which includes nearly all of v4's telemetry and catalog-growth work) carry forward unchanged.

---

## 1. Background and motivation

*Same as v4 §1.*

The Jira ticket's original `wpswatchprodreplica.database.windows.net` reference (which v3 / v4 read as not-in-production) is now the actual deployment target. The replica didn't exist in code yet at the time of Stage 1 exploration; the v5 decision provisions it.

---

## 2. Goals and non-goals

### Goals

**Phase 1 (MVP).** *Same as v4*, with the substrate change:
- Replace the existing canned Looker reports with an in-product page in wpsWatch that runs the equivalent reports through a new, separate service that reads from `wpswatchprodreplica.database.windows.net`.
- Four UX upgrades (sortable columns, dynamic filtering, summarisation, flexible date range).
- Role gating mirrors today exactly: `AdminReport` sees seven SysAdmin reports; `UserReport` sees three; Volunteers excluded.
- Single-org behaviour, matching today's Reports.
- Cutover sequence: pilot side-by-side, SysAdmin first, regular users second.

**Phase 2.** *Same as v4* — chat, export, cross-org, telemetry, catalog growth, promotion workflow (the latter may slip to Phase 3 if capacity is tight).

**Phase 3.** *Same as v4* — saved queries with relative-date support, scheduled email digest, subscription model unification, promotion workflow if it slipped.

**Longer-term.** *Same as v4* — GPS exposure with role-gated coarsening, composite camera-health report, the broader Mission Control roadmap.

### Non-goals
*Same as v4*, with two v5 clarifications:

- **No extension of the existing `wpsWatch.*` analytics-DB schema as part of AI Reports.** The `wpswatch.reporting` repo and its analytics DB continue to be owned by the Looker side of the house. AI Reports reads from the replica, not from the analytics DB. No `vw_ai_*` views are added to `wpswatch.reporting`; no new `fct_ai_*` aggregation tables either.
- **No writes to the replica.** The replica is read-only by definition. Any aggregation tables or AI-Reports-owned data goes in a separate writable database (see OQ-V5-29 about pre-aggregation strategy).

---

## 3. User stories

*Same as v4* (US-01 through US-V4-28; US-04 still removed per v3).

---

## 4. Functional requirements

### 4.1 Service architecture and surface (phase 1)

- **FR-V2-01.** *(carries forward)* New ASP.NET Core 8 service `Wps.Watch.AiReports`, deployed as a sibling to `wps.watch.functions.scheduledreporting`. Shares Key Vault.
- **FR-02.** REST API: `/api/reports`, `/api/reports/{id}/run`, `/api/saved-queries` (Phase 3), `/api/chat` (Phase 2), `/api/exports/{token}` (Phase 2).
- **FR-03.** MCP server endpoint (`/mcp`, SSE transport).
- **FR-V3-04.** New page at `/ai-reports` with the SysAdmin-first cutover.

### 4.2 Authentication and authorization (phase 1)

- **FR-05.** JWT validation locally with shared HS256 `JwtKey` from Key Vault.
- **FR-V5-06.** **The service computes the user's authorized organization list per request by reading `dbo.OrganizationUserRole` directly from the replica.** No cache table, no BigQuery sync involvement. The replica is a full mirror of the OLTP primary, so `OrganizationUserRole` is present and current. Replica lag (sub-second typical) is the only staleness. This is *fresher* than v4's `wpsWatchLookerUserOrg`-via-analytics-DB approach. The scope mechanism is still designed to extend to `(@organization_id, @site_id_list)` per OQ-V3-21. Replaces v3/v4 FR-V3-06.
- **FR-07.** Role checks via shared `Wps.Watch.Authorization` package.
- **FR-08.** Volunteers → HTTP 403.
- **FR-09.** `PhotoUploader` / `PhotoTagger` → HTTP 403.
- **FR-V5-10.** **The service refuses queries with HTTP 503 when measured replica lag exceeds a configured threshold.** Defaults: warn at 5 seconds, hard-fail at 60 seconds. Replica lag is measured via `sys.dm_database_replica_states` (or equivalent) on the replica connection. The "warn" tier surfaces a freshness badge to the user (per FR-V5-44); the "hard-fail" tier rejects the request entirely. Replaces v4 FR-10 and v4 NFR-V4-04.

### 4.3 Canned reports — MVP (phase 1)

- **FR-V5-11.** The seven SysAdmin Looker reports and the three regular-user reports are exposed as parameterized tool calls. **The backing objects live in the replica's `dbo` schema (the same tables the OLTP primary uses) and are accessed via a curated view layer** — see DR-V5-02. The view layer is defined in the OLTP primary (`wpswatch-prod`) so it replicates to the replica; the views exclude sensitive columns and apply the org-scope filter. Specific mapping:

  | Report | Backing object on the replica | Notes |
  |---|---|---|
  | Camera Inventory | `dbo.vw_ai_camera_inventory` | New view in primary, replicates. |
  | Deployed Camera Battery Level | `dbo.vw_ai_deployment_battery` | New view in primary. |
  | Photo Count By Camera (last month) | `dbo.vw_ai_photo_count_summary` | New view, may aggregate against raw `Photo`. |
  | Photo Count By Camera (custom range) | New `dbo.sp_ai_photo_count` stored proc | Net-new. Takes `@org_id, @start_date, @end_date`. |
  | Region & Site Totals with Deployment | `dbo.vw_ai_region_site_totals` | New view. Mirrors the shape of the existing `vw_wpsWatchRegionSiteTotalsV3` in the analytics DB, but defined on raw OLTP. |
  | SIM Contract Renewal | `dbo.vw_ai_sim_contracts` | New view. |
  | SIM Prepaid Data | `dbo.vw_ai_sim_data` | New view. Uses the same `PrepaidDataLimitGb - (0.0001 × photo_count_in_billing_cycle)` heuristic as the existing report — label honestly in the manifest. |
  | **Deployment Health Status** (replaces Top 10 Offline) | New `dbo.sp_ai_deployment_health_status(@org_id, @offline_hours = 24, @warning_hours = 12)` | Returns `online` / `warning` / `offline` tiers. |

  The view layer being defined in the *primary* (not in the analytics DB and not on the replica) is the key shift: views in the primary replicate automatically; AI Reports' SQL principal on the replica has `SELECT` on only the `vw_ai_*` set. This is functionally equivalent to v4's plan but uses the replica's natural object model.

  Replaces v4 FR-V3-11.

- **FR-12.** The three regular-user reports exposed via the same mechanism.
- **FR-V3-13.** Each canned report card supports the four UX upgrades (sortable columns, dynamic filtering, summarisation, flexible date range).
- **FR-V5-14.** Output includes parameters used, timestamp of execution, **the replica's lag at execution time** (per FR-V5-10), plus the four UX-upgrade affordances. Replaces v4 FR-V3-14.
- **FR-15.** Parity-tested against the equivalent Looker dashboard. *(v5 caveat: the Looker dashboards still hit the analytics DB, so the parity test is comparing two different data paths to the same OLTP source. Differences > the replica-lag tolerance should be investigated as bugs.)*

### 4.4 Free-form chat (phase 2)

*Same as v4.* Tool surface: `list_canned_reports`, `run_canned_report`, `describe_ai_view`, `query_ai_views`, `save_query`, `run_saved_query`. AST validator scoped to `dbo.vw_ai_*`.

### 4.5 File export (phase 2)

*Same as v4.*

### 4.6 Saved queries (phase 3)

*Same as v4*, with one update to storage location (OQ-V3-7 / OQ-V4-26):

- **FR-V5-30.** Saved-query storage. **Cannot live on the replica** (read-only). Three options remain: (a) a new dedicated Azure SQL DB owned by AI Reports; (b) Cosmos (Matt's option from comment 5); (c) a new schema on the OLTP primary (writable, controlled by AI Reports). Design-council call — see OQ-V5-7. Replaces v4 FR-V3-30.

### 4.7 Behavioural and trust requirements

*Same as v4 (FR-40 to FR-V3-44 / FR-V5-44).*

- **FR-V5-44.** **Data-freshness badge** based on replica lag, not analytics-DB refresh age. When measured replica lag exceeds the warn threshold (default 5 seconds, per FR-V5-10), the answer carries a "data approximately N seconds behind primary" badge. Replaces v4 FR-V3-44.

### 4.8 Connectivity and graceful degradation (phase 2+)

*Same as v4.*

### 4.9 Usage telemetry and catalog growth (phase 2)

*Same as v4 §4.10.* FR-V4-49 through FR-V4-56 carry forward. v5 clarification: **the telemetry tables themselves do not live on the replica.** They live on the writable substrate that ends up holding saved queries (see FR-V5-30 / OQ-V5-7), or on a separate small SQL DB. The AI Reports service writes telemetry to that substrate; the dashboard queries it; the replica is unaffected.

---

## 5. Non-functional requirements

*Same as v4*, with:

- **NFR-V5-04.** Replaces v4 NFR-V4-04 (analytics-DB refresh-age) with **replica-lag freshness gate**: warn at 5 seconds of measured lag, hard-fail at 60 seconds. Azure SQL geo-replication is typically sub-second; the warn threshold gives margin for bursty backlog.
- **NFR-V5-10.** New. **Azure SQL tier on the OLTP primary.** Geo-replication requires Business Critical, Premium, or Hyperscale tier. The team has agreed to absorb the cost (see OQ-V5-30 for the explicit acknowledgment). The replica itself is billed alongside the primary.
- **NFR-V5-11.** New. **Read-only enforcement.** Connection string from the AI service includes `ApplicationIntent=ReadOnly`; the Azure SQL routing layer ensures all reads go to the replica even when the connection nominally targets the failover-group name. Writes to the replica are physically impossible; this requirement is belt-and-suspenders against connection-string misconfiguration.

---

## 6. Security and privacy requirements

*Same as v4 §6*, with the following v5 revisions and additions. The substrate change makes column-level access controls **the primary defense** rather than a secondary one (because there's no curated reporting schema to "exclude by absence" anymore — everything in the OLTP primary replicates to the replica).

### Revised MUSTs

- **S-V5-MUST-02.** Replaces v4 S-V2-MUST-02. The AI service's connection string targets `wpswatchprodreplica.database.windows.net` with `ApplicationIntent=ReadOnly`. The replica is physically read-only; the connection string is enforcement.

- **S-V5-MUST-03.** Replaces v4 S-V2-MUST-03. The AI service uses a dedicated SQL principal (`ai_reports_reader`) with the following grant set:
  - **GRANT `SELECT` on the `dbo.vw_ai_*` view set only.** No `SELECT` on any base `dbo` table.
  - **DENY `SELECT` on every base `dbo` table** (defense in depth: even if a future view inadvertently selects from a sensitive column, the DENY blocks the underlying table read for this principal).
  - **DENY `SELECT` on `sys.*` and `INFORMATION_SCHEMA.*`** for the AI principal (so the LLM can never enumerate the schema).
  The view set is defined in the OLTP primary and replicates to the replica; permissions are also replicated.

- **S-V5-MUST-28.** Replaces v4 S-V2-MUST-28. Sensitive columns (enumerated in [data-v2 §3](./perspectives/data-v2.md) — `Site.DasToken`, `SmartIntegrateToken`, `SlackWebhookUrl`, `CiscoSustainableImpactToken`, `ZendoApiKey`, `Site.Latitude/Longitude`, `Deployment.Latitude/Longitude`, `Device.PhoneNumber/SimCardNumber/ImeiNumber`, `User.PasswordHash/SecurityStamp/Email/PhoneNumber`, `Key.ApiKey`, `ImageRecognitionService.ApiKey/Url`) **are not projected in any `dbo.vw_ai_*` view.** Combined with S-V5-MUST-03's DENY on base tables, this is two layers between the AI principal and the secrets.

### New v5 MUSTs

- **S-V5-MUST-40.** **View-set integrity is enforced at deploy time.** A CI check on the `wpswatch-prod` schema migration repo asserts that no `dbo.vw_ai_*` view references any of the sensitive columns enumerated in S-V5-MUST-28. Built using `sys.sql_dependencies` or equivalent introspection. Replaces what was previously implicit ("don't add sensitive columns to views").

- **S-V5-MUST-41.** **No AI service code may execute raw SQL strings constructed from user input outside the AST-validator path.** The replica is a richer surface than the analytics DB was; the discipline of "all SQL goes through the validator" is more important. Existing requirement S-V2-MUST-17 covers the AST validator; this is the explicit no-bypass clause.

### Carried forward unchanged

S-MUST-01, S-MUST-04 through S-MUST-08, S-MUST-09 through S-MUST-13 (org-scope via explicit `@organization_id` parameter), S-MUST-15 through S-MUST-27, S-MUST-29 through S-MUST-37, S-V4-MUST-38 / 39 (telemetry prompt-text protection), all SHOULDs and NICE-TO-HAVEs.

### Notable consequence

The v3/v4 §6 plan was to exclude sensitive columns by absence in the `vw_ai_*` views, with column-level DENYs as belt-and-suspenders. v5's plan is the same shape — but the DENYs become non-optional because there's no "curated schema separation" between the AI principal and the production data.

---

## 7. Permission model

### Phase 1 (MVP) — *same gating as v4, different bootstrap source*

- System Admins (`AdminReport`) see the seven reports.
- `UserReport` users (`OrgAdmin`, `IncidentManager`, `Viewer`, `GpsViewer`) see the three.
- `Volunteer`, `PhotoUploader`, `PhotoTagger` see no AI Reports page.
- Single-org behaviour.
- **(v5 update)** Field Team review process remains out of scope; AI Reports inherits today's gating exactly.

### How the new service learns and enforces (revised in v5)

- JWT validated locally with shared `JwtKey`.
- **User's organization list looked up per request by querying `dbo.OrganizationUserRole` on the replica.** No analytics-DB hop, no `wpsWatchLookerUserOrg` cache, no BigQuery involvement. Replica lag is the only staleness; per FR-V5-10 the request fails closed if lag exceeds 60 seconds.
- Role checks via the shared `Wps.Watch.Authorization` package, same operations (`UserReport`, `AdminReport`) as today.

### Phase 2+

- **Cross-org reporting.** Per Matt comment 15 (v3): cross-org Reports is dependent on this project landing. AI Reports becomes the first cross-org reporting surface in wpsWatch.
- **Site-level roles** (OQ-V3-21). When wpsWatch core ships site-level roles, the bootstrap query reads the additional `(@user, @site)` mappings from the replica. The scope parameter extends from `@organization_id` to `@organization_id + @site_id_list`. Forward-compatibility design from v3/v4 applies unchanged.

---

## 8. Architecture options considered

*v5 closes the substrate decision that was open through v4.*

### Transport / protocol — unchanged
Hybrid REST + MCP, single handler core.

### Query generation — unchanged
Hybrid catalogue + gated text-to-SQL (Phase 2).

### Authentication — unchanged (Question 1 of the design brief)
Shared HS256 `JwtKey` recommended primary, introspection endpoint as fallback, RS256/Entra as longer-term posture (S-SHOULD-05).

### Data source — **decided in v5: Azure SQL geo-replica**

The team decision: **`wpswatchprodreplica.database.windows.net`**, a fully mirrored Azure SQL geo-replica of the OLTP primary. The data-v2 perspective recommended against this in v3 ("the team has already chosen a third option that's working — a separate analytics database"); the team has reviewed and decided differently. v5 implements the decision.

The implications captured in this doc:
- Phase 1 architecture simpler — no analytics-DB extension work needed.
- Sensitive-column controls become primary defense (§6).
- Pre-aggregation deferred (OQ-V5-29).
- Replica-lag freshness model (FR-V5-10, NFR-V5-04).
- Cost: primary on Business Critical / Premium / Hyperscale tier (NFR-V5-10).
- The existing analytics DB and `wpswatch.reporting` repo continue to exist for Looker, unchanged by AI Reports.

---

## 9. Data layer requirements

The v3/v4 data-layer plan (DR-V2-01 through DR-V4-37) is **substantially superseded by v5**. The v3/v4 plan extended the existing `wpswatch.*` schema on the analytics DB; v5 reads from a geo-replica instead. The following is the v5 set, designed for the replica architecture. Where a v3/v4 requirement remains valid (e.g. cost controls), it carries forward.

- **DR-V5-01.** **Read source: `wpswatchprodreplica.database.windows.net`**, Azure SQL geo-replica of the OLTP primary `wpswatch-prod.database.windows.net`. Connection string includes `ApplicationIntent=ReadOnly`.
- **DR-V5-02.** **Curated view layer in OLTP primary that replicates to the replica.** New views in the `dbo` schema of `wpswatch-prod`, named `dbo.vw_ai_*` and `dbo.sp_ai_*`. The CI check (S-V5-MUST-40) ensures these never project sensitive columns. The views replicate automatically as part of geo-replication.
- **DR-V5-03.** **AI principal `ai_reports_reader`** with `SELECT` on `dbo.vw_ai_*` and `dbo.sp_ai_*` only, DENY on every `dbo` base table, DENY on system catalogs. Permissions live on the primary and replicate.
- **DR-V5-04.** **No direct OLTP exposure** to the LLM beyond the curated view set. AST validator (FR-V2-19 carried forward) restricts SQL to `dbo.vw_ai_*` references only.
- **DR-V5-05.** **Sensitive columns absent from `dbo.vw_ai_*` views** (see S-V5-MUST-28 for the column list). Verified by the CI check in S-V5-MUST-40.
- **DR-V5-06.** **Canned reports backed by `dbo.vw_ai_*` views and `dbo.sp_ai_*` procs** per the FR-V5-11 mapping table.
- **DR-V5-07.** **Ad-hoc chat views** (`vw_ai_organization`, `vw_ai_site`, `vw_ai_deployment`, `vw_ai_device`) added to the primary as part of Phase 2 chat work. Same `dbo.vw_ai_*` namespace, same security model.
- **DR-V5-08.** **Semantic manifest** `data-manifest.json` in the AI Reports service repo describes every `dbo.vw_ai_*` / `dbo.sp_ai_*` object with name, purpose, columns, types, parameters, `expected_cost_class`, version. Generated from the live primary schema; CI gate (DR-V5-23) keeps them in sync.
- **DR-V5-09.** **GPS columns never surfaced.** No `Latitude` / `Longitude` in any `dbo.vw_ai_*` view in MVP.
- **DR-V5-10.** **Statement timeout: 15 seconds default; 120 seconds export path.**
- **DR-V5-11.** **Row caps: 5,000 default chat / 100,000 hard ceiling on export.**
- **DR-V5-12.** **`READ COMMITTED SNAPSHOT` isolation** on the OLTP primary (it's already enabled on `wpswatch-prod` per current schema — verify before deploy).
- **DR-V5-13.** **Query Store enabled** on the replica for AI principal's query-shape monitoring.
- **DR-V5-14.** **Cost-profile metadata** in the manifest — `fast` / `medium` / `slow` per object, surfaced to the user before execution.
- **DR-V5-15.** **Concurrency caps.** 5 per user, 25 per service instance.
- **DR-V5-16.** **Replica-lag monitoring.** `sys.dm_database_replica_states` (or equivalent) sampled per request; lag emitted as telemetry; lag-exceeded events alert (per FR-V5-10).
- **DR-V5-17.** **Org-scope via explicit `@organization_id` parameter**, sourced from the JWT, validated against `dbo.OrganizationUserRole` on the replica. No `SESSION_CONTEXT`. Forward-compatible to `(@organization_id, @site_id_list)` per OQ-V3-21.
- **DR-V5-18.** **Schema-evolution contract tests.** Two-repo coordination: the OLTP primary schema repo's CI diffs the live `dbo.vw_ai_*` set against the committed `data-manifest.json` in the AI Reports service repo. Disagreement fails CI.
- **DR-V5-19.** **Versioned objects.** Backward-incompatible changes follow the V2/V3 deprecation pattern the team already uses for `vw_wpsWatchRegionSiteTotals*` — 90-day overlap, deprecation warnings.
- **DR-V5-20.** **Output filter** drops columns matching `*token*`, `*hash*`, `*secret*`, `*password*`, `*apikey*` before returning rows. Same as v4.
- **DR-V5-21.** **Telemetry tables** live on a separate writable substrate (see DR-V5-22 / FR-V5-30 / OQ-V5-7), not on the replica. Same shape as v4 DR-V4-35 (`report_run_log`, `chat_interaction_log`).
- **DR-V5-22.** **AI Reports writable substrate.** A small Azure SQL database (or Cosmos DB, per OQ-V5-7) owned by AI Reports holds: saved queries (FR-V5-30), telemetry tables (DR-V5-21), saved-query audit log. Separate from the replica and from the analytics DB.
- **DR-V5-23.** **Pre-aggregation strategy: deferred for Phase 1.** For Phase 1's canned reports, the `dbo.vw_ai_*` views run against raw OLTP tables on the replica. This is fine for queries like Camera Inventory or SIM data; potentially slow for "Photo Count By Camera" over long date ranges. If Phase 1 measurements show real performance pain, a pre-aggregation strategy (a writable substrate with daily/hourly summary tables, fed from the replica) is added in Phase 2. OQ-V5-29.

### Notable removals from v3/v4

- Anything referencing the analytics DB (`wps-sql-analytics-prod`) at runtime (v4 DR-V2-01, DR-V2-02, etc.).
- The `wpsWatch_job_run_log` freshness table (v4 DR-V2-08) — replaced by replica-lag monitoring.
- The `fct_ai_*` aggregation tables (v4 DR-V2-18, DR-V2-19) — replaced by raw OLTP queries on the replica, with pre-aggregation deferred per DR-V5-23.
- The dual-database deployment ordering (v4 DR-V2-28) — only one database changes for AI Reports' SQL work now (the OLTP primary, for view definitions).

### Notable kept

- The semantic manifest pattern.
- The contract-test CI gate.
- The versioned-object deprecation cycle.
- The cost-profile metadata.
- The output filter as a tertiary safety net.

---

## 10. Phasing proposal

### Phase 1 (MVP)

**Substrate is decided** — `wpswatchprodreplica.database.windows.net` (v5). No checkpoint, no decision deferred.

**Tracer bullet** — unchanged from v3/v4: Region & Site Totals end-to-end.

**Phase 1 deliverables (revised in v5):**
- Provision and configure the geo-replica (if not yet provisioned). Azure SQL tier upgrade on the primary if currently on a tier below Business Critical.
- Define the new `dbo.vw_ai_*` view set in the OLTP primary schema. Roughly 10 views + 2 stored procs (one for `sp_ai_photo_count`, one for `sp_ai_deployment_health_status`).
- Create the `ai_reports_reader` principal with the v5 grant set (DR-V5-03). CI check for sensitive-column non-projection (S-V5-MUST-40).
- New ASP.NET Core 8 service with REST endpoints, MCP server, JWT validation, replica connection string.
- Auth wiring via shared `Wps.Watch.Authorization` package, `dbo.OrganizationUserRole` lookup on the replica.
- Replica-lag freshness gate (FR-V5-10).
- React page with the four UX upgrades (sortable columns, dynamic filtering, summarisation, flexible date range).
- Parity tests against the existing Looker dashboards (cross-checking two different data paths).
- App Insights instrumentation, per-request audit log.
- Cutover: new menu side-by-side with Looker, SysAdmin migrates first.

**Revised effort estimate (engineer-weeks):**

| Piece | v4 estimate | v5 estimate | Why |
|---|---|---|---|
| New service skeleton, replica wiring, auth re-impl | ~1.5 | ~2 | Replica wiring is net-new — provision config, connection string, lag monitoring. |
| `Wps.Watch.Authorization` extraction | ~0.5 | ~0.5 | Unchanged. |
| OLTP schema changes (10 `dbo.vw_ai_*` views, 2 `dbo.sp_ai_*` procs, principal + grants, CI check) | ~2–3 (was in analytics DB) | **~2.5–3** | Slightly more sensitive than adding to the analytics DB — these changes go into the production OLTP schema repo and need careful review. View shapes themselves aren't more complex. |
| Parity tests, observability, audit, deploy | ~2 | ~2 | Unchanged. |
| React page, parameter forms, table render, **four UX upgrades**, CSV export | ~3–4 | ~3–4 | Unchanged. |
| Cutover sequencing | ~0.5 | ~0.5 | Unchanged. |
| **(New in v5)** Azure SQL primary tier upgrade + geo-replica provisioning | n/a | ~1 (mostly DevOps / Azure team) | Real configuration work; light development. |
| **Phase 1 total** | **~9.5–13 weeks** | **~11.5–14 weeks** | Slightly higher than v4 due to replica provisioning and primary-schema-change overhead. |

### Phase 2

*Same as v4.* Chat, export, cross-org, telemetry, catalog growth, promotion workflow (potentially slipping to Phase 3). 

**v5 modification:** the Phase 2 substrate checkpoint from v4 is **removed** — substrate is decided. If pre-aggregation performance becomes a problem during Phase 2 (per DR-V5-23 / OQ-V5-29), the team adds an AI-Reports-owned writable substrate with summary tables, fed from the replica. This is contained Phase 2 work, not a substrate re-decision.

**Phase 2 effort:** unchanged from v4 (~19.5–29 weeks if workflow ships in Phase 2; ~16.5–26 if workflow slips).

### Phase 3

*Same as v4.* Saved queries, scheduled email digest, subscription model unification, promotion workflow (if slipped).

**v5 modification:** FR-V5-30 storage decision (replaces v4 FR-V3-30) — saved queries live on the AI Reports writable substrate (DR-V5-22), not on the replica.

### Longer-term

*Same as v4.*

---

## 11. Open questions for the human owner

### Resolved in v5

- ~~**OQ-V3-26** — Phase 2 substrate checkpoint~~ — **resolved.** Substrate is `wpswatchprodreplica.database.windows.net`, decided in Phase 1.
- ~~**OQ-V2-10** — Anthropic vs. other model provider~~ — **resolved.** Anthropic is the model provider for Phase 2 chat. The architecture stays vendor-portable (tool-use formats differ slightly across providers, but the host-loop pattern is shared) so the choice remains reversible if needed in the future.

### Still open from v3 / v4

- **OQ-V2-6.** JWT migration to RS256/Entra.
- **OQ-V2-11.** Existing Looker SQL — partially mitigated by reading `wpswatch.reporting`. The relationship between AI Reports and the analytics DB is now informational (parity reference only), so the urgency to extract every Looker SQL drops.
- **OQ-V2-13.** AI Reports repo layout — recommendation: new repo as sibling to existing two.
- **OQ-V2-14.** Runbook refresh cadence — **no longer relevant to AI Reports.** AI Reports doesn't read from the analytics DB at runtime. This question remains an issue for the Looker side of the house but doesn't gate AI Reports. Closing on the AI Reports side.
- **OQ-V2-15.** Which `vw_wpsWatchRegionSiteTotals` version is in Looker — still useful for parity testing in Phase 1, but the AI Reports view is defined independently from scratch.
- **OQ-V2-16.** BigQuery sync deprecation — Looker concern, not AI Reports.
- **OQ-V2-17.** Subscription model unification.
- **OQ-V2-18.** `DeploymentReportController` relationship — confirm AI Reports doesn't call it.
- **OQ-V2-19.** Runbook plain-text credentials — separate ticket, unrelated to AI Reports' substrate decision.
- **OQ-V2-20.** Analytics-DB deployment automation — Looker concern.
- **OQ-V3-7.** Saved-query storage. v5 elevates this: now applies to **both saved queries and telemetry tables** (both need a writable substrate that the replica can't provide). Three-way choice: dedicated Azure SQL DB / Cosmos / new schema on OLTP primary.
- **OQ-V3-21.** Site-level user roles timeline.
- **OQ-V3-22.** Where is the 12h warning threshold encoded?
- **OQ-V3-23.** Field Team review process.
- **OQ-V3-24.** Field Team conversation kickoff.
- **OQ-V3-25.** Saved-query share-with-other-orgs in MVP?
- **OQ-V4-26.** Telemetry storage location — folds into OQ-V3-7 / OQ-V5-7 in v5 since both need the same substrate.
- **OQ-V4-27.** Telemetry retention beyond 13 months.
- **OQ-V4-28.** Whether the promotion workflow ships in Phase 2 or Phase 3.

### New in v5

- **OQ-V5-7.** **Writable substrate for saved queries and telemetry tables.** The replica can't hold these. Decide between: (a) a new dedicated Azure SQL database owned by AI Reports — clean isolation but extra cost and operations; (b) Cosmos — fits the "user preferences + dashboards" precedent in wpsWatch, but SQL queries as JSON in a document DB has friction; (c) a new schema in the OLTP primary (writable) — operational consolidation but couples AI Reports' write workload to the primary. Replaces v4's OQ-V3-7 and OQ-V4-26 with a clearer scope (both saved queries and telemetry need this substrate).
- **OQ-V5-29.** **Pre-aggregation strategy.** v5 Phase 1 accepts raw-OLTP query performance on the replica (the canned reports aren't usually heavy). If Phase 1 measurements show real pain points — particularly on "Photo Count By Camera" over long date ranges — Phase 2 adds AI-Reports-owned summary tables on the writable substrate from OQ-V5-7. Decision: monitor in Phase 1 and react in Phase 2 rather than pre-build. Open: what's the threshold for "real pain" — P50 > 5 seconds for a canned report?
- **OQ-V5-30.** **Azure SQL tier upgrade confirmation.** Geo-replication requires Business Critical, Premium, or Hyperscale tier on the primary. What tier is `wpswatch-prod` on today, and is the cost of the upgrade (if needed) approved? Confirm before Phase 1 starts.
- **OQ-V5-31.** **Replica-write attempts.** Geo-replicas are physically read-only; a write attempt fails at the DB engine. But AI Reports' code shouldn't *attempt* writes — that suggests a bug. Decision: in production, log replica-write attempts as errors and alert? Or treat them as expected to be caught by tests?

---

## 12. Glossary

*Same as v4 §12*, plus:

- **Azure SQL geo-replica:** a fully mirrored, read-only secondary copy of an Azure SQL database, maintained by Azure-managed asynchronous physical replication. Typical lag is sub-second. Requires the primary to be on Business Critical, Premium, or Hyperscale tier. The replica is queryable but not writable.
- **AI Reports writable substrate:** a small writable database (Azure SQL DB or Cosmos) owned by AI Reports that holds saved queries, telemetry tables, and the saved-query audit log — data that needs writes and can't live on the read-only replica. See OQ-V5-7 for the substrate-choice decision.
- **`dbo.vw_ai_*` namespace:** a set of curated views defined in the OLTP primary schema (`wpswatch-prod.dbo.*`) specifically for AI Reports. The views exclude sensitive columns, replicate to the geo-replica, and are the only objects the AI principal can query. Replaces the v3/v4 `wpsWatch.vw_ai_*` plan that placed views in the analytics DB.

---

## Appendix A — v4 → v5 requirement mapping

| v4 ID | v5 ID | Change |
|---|---|---|
| FR-V3-06 | FR-V5-06 | Bootstrap query reads `OrganizationUserRole` from the replica, not `wpsWatchLookerUserOrg` from the analytics DB. |
| FR-10 | FR-V5-10 | Freshness gate measures replica lag, not analytics-DB refresh age. |
| FR-V3-11 | FR-V5-11 | Backing objects are `dbo.vw_ai_*` views in OLTP primary, not `vw_wpsWatch*` / `fct_ai_*` in analytics DB. |
| FR-V3-14 | FR-V5-14 | Replica lag shown in metadata. |
| FR-V3-30 | FR-V5-30 | Saved-query storage cannot be on replica; substrate undecided (OQ-V5-7). |
| FR-V3-44 | FR-V5-44 | Freshness badge based on replica lag. |
| NFR-V4-04 | NFR-V5-04 | Replica-lag freshness model. |
| **(new)** NFR-V5-10 | — | Azure SQL tier requirement for primary. |
| **(new)** NFR-V5-11 | — | `ApplicationIntent=ReadOnly` enforcement. |
| S-V2-MUST-02 | S-V5-MUST-02 | Connection string targets replica with read-only intent. |
| S-V2-MUST-03 | S-V5-MUST-03 | DENY on base tables + GRANT only on `dbo.vw_ai_*`. |
| S-V2-MUST-28 | S-V5-MUST-28 | Sensitive columns enumerated; absent from `dbo.vw_ai_*` views. |
| **(new)** S-V5-MUST-40 | — | CI check enforces sensitive-column non-projection. |
| **(new)** S-V5-MUST-41 | — | No raw-SQL execution outside AST-validator path. |
| DR-V2-01 to DR-V4-37 | DR-V5-01 to DR-V5-23 | Substantially rewritten for the replica architecture. v4 data-layer plan retired. |
| **(new)** OQ-V5-7 / 29 / 30 / 31 | — | Writable substrate, pre-aggregation, tier confirmation, replica-write alerting. |
| OQ-V3-26 | (resolved) | Phase 2 substrate checkpoint resolved in v5 — substrate is the replica. |
| OQ-V2-14 / 16 / 20 | (closed for AI Reports) | These are analytics-DB-side concerns; AI Reports doesn't read from analytics DB anymore. |
| All v4 telemetry / catalog-growth requirements (FR-V4-49 to FR-V4-56, US-V4-26 to US-V4-28, S-V4-MUST-38/39, DR-V4-35–37, OQ-V4-26/27/28) | (carried forward) | Telemetry tables live on the writable substrate (DR-V5-21 / DR-V5-22). Otherwise unchanged. |
| All other v4 requirements | (unchanged) | — |

---

*End of v5 requirements document.*
