# Data Perspective — AI Reports

**Owner:** Database / data architecture
**Source of truth for the schema findings cited here:** `docs/ai-reports-exploration-notes.md` (the "exploration notes" hereafter).
**Date:** 2026-05-05.

This document is opinionated. The Jira ticket asks us to read from `wpswatchprodreplica.database.windows.net` (or "even a separate mirror DB") for an AI Reports service that replaces the canned Looker reports and lets users save the **SQL** for ad-hoc queries (not the natural-language prompt). The human owner explicitly asked for a real recommendation rather than a list of options. That's what follows.

---

## 0. Glossary (so we stop tripping over jargon)

- **OLTP** — online transaction processing. The schema the API writes to. Normalized, optimized for inserts/updates, terrible for "count grouped by day".
- **Replica** — Azure SQL's built-in read-only secondary. Same schema as primary. Lag is usually sub-second. We pay for it whether we use it or not (bundled with the Business Critical / Premium tier).
- **Mirror / reporting DB** — a separate database we own and shape ourselves. Could be a curated subset, a denormalized warehouse, or anything in between. Not free.
- **Semantic layer** — a description of tables/columns in human terms ("battery_percent" not "BatteryLevel decimal(5,2)") that an LLM consumes instead of (or alongside) raw schema introspection.
- **Materialized view** — a view whose result set is physically stored and refreshed on a schedule (or on write). SQL Server's closest native equivalent is an **indexed view**; for anything more flexible we build summary tables ourselves and keep them current with jobs.
- **Snapshot isolation** — readers see a consistent point-in-time snapshot without taking shared locks. Writers don't block readers and vice versa. Costs tempdb space.
- **Read-committed snapshot (RCSI)** — the milder variant: every read statement sees a snapshot, but transactions don't. Cheap to enable, dramatically reduces blocking.
- **DENY** — a SQL Server permission that overrides any GRANT. Belt and suspenders for sensitive columns.

---

## 1. Replica vs. mirror — recommendation

**Recommendation: start with the Azure replica plus a thin curated reporting schema of views (option C, the hybrid). Do not stand up a separate mirror DB for MVP. Re-evaluate when we hit the first real cost or query-shape pain.**

Here is the reasoning, with the trade-offs laid bare.

### Option A — replica only, query OLTP tables directly

Pros:
- Already paid for. The replica exists as part of our Azure SQL tier.
- Lag is sub-second in practice for Azure SQL geo-replication / read-scale.
- Zero net-new infrastructure. The connection string + a read-only login is the entire ops story.

Cons (real ones, not boilerplate):
- The replica mirrors the OLTP schema column-for-column, including the credential and PII columns enumerated in the exploration notes (see `Site.DasToken`, `User.PasswordHash`, `Key.ApiKey`, etc., listed at exploration-notes §6 "Sensitive fields"). A breach of the read-only login is a breach of those secrets. We can revoke at the column level via `DENY`, but every new sensitive column added to OLTP is a new column the security team has to remember to deny. That's a process risk we'd own forever.
- The OLTP schema is normalized for write throughput. "Photo count by deployment per day" becomes a join across `Photo → Deployment → Site → Organization` (per exploration-notes §6 org-scoping paths) plus a `GROUP BY` over potentially tens of millions of `Photo` rows. The OLTP indexes weren't designed for that. We will eat real query time.
- The schema evolves on the API team's cadence. A column rename in a migration silently breaks any saved AI-generated SQL that referenced it. The AI service has no early-warning system unless we build one.
- LLMs introspecting an OLTP schema will hallucinate joins. `Deployment.DeviceId` and `Photo.DeploymentId` are obvious; `Photo → Organization` requires three hops and the LLM will sometimes invent a shortcut column. The error rate matters for a feature whose value proposition is correctness.

### Option B — curated reporting mirror / warehouse

Pros:
- Explicit semantic layer. We choose what to surface and how to name it. The sensitive columns simply don't exist in the mirror, so denial is by absence rather than by policy.
- Decoupled from OLTP evolution. We can absorb a column rename in the OLTP without breaking saved queries — the mirror's schema is the contract.
- Pre-aggregated time-series (see §5) become trivial: nightly job, target table, cheap reads.

Cons:
- Real cost. Even an Azure SQL S2 ($30/mo) or a small Synapse Serverless pool isn't free, and the human running this is Wildlife Protection Solutions, not a hyperscaler.
- Replication mechanics. We have to write and operate the ETL/ELT (Azure Data Factory, a custom job, or change data capture). That's an engineer-month we don't have for MVP.
- Lag. A nightly mirror is fine for "Photo count by month." It is not fine for "show me which deployments went offline today." We'd end up with a tiered freshness story we have to explain to users.
- More moving parts means more operational surface area, more on-call paging, more "is the mirror up to date?" questions.

### Option C — hybrid (the recommendation)

Use the existing replica as the read source, but **expose a curated `reporting` schema of views** (and, eventually, a small handful of summary tables — see §5) on top of it. The AI service connects to the replica with a read-only login that has access **only** to `reporting.*` and to nothing in `dbo.*` directly.

Why this wins for WPS at our current scale:
- We get the cost profile of the replica (essentially free) and the safety profile of a curated reporting layer.
- The view layer becomes the contract. Renaming `BatteryLevel` to `BatteryPercent` in OLTP is harmless if `reporting.vw_DeploymentBattery.battery_percent` keeps that column name.
- Sensitive columns are denied by absence, not by `DENY`. The sensitive-data list from the exploration notes never appears in any view, so the AI literally cannot see it.
- The migration path to a real mirror later is straightforward: replace the views with tables fed by jobs. The AI service doesn't notice.

The cost is that view authoring is real work and the views still execute against the OLTP tables, so we don't get the time-series performance wins until we layer in materialized aggregates (§5). That's fine — those wins come later, when we have measured pain to justify them.

**Decision recorded: hybrid, with the curated `reporting` schema living *on the replica*, not on the primary.** Putting it on the replica means even a runaway query (we do have those — see §4) cannot block production writes.

---

## 2. Schema exposure to the LLM

**Recommendation: stored-procedure catalog for the seven canned reports + a narrowly-scoped semantic-layer manifest of views for ad-hoc queries. No raw OLTP introspection, ever.**

Direct LLM introspection of a normalized OLTP schema with 41+ `DbSet` entries (per `Wps.Watch.Data/Context/WpsDbContext.cs:11–70`) will produce wrong SQL at a rate that is unacceptable for a feature whose entire purpose is being right.

Concretely:

1. **Canned reports → stored procedures (or table-valued functions).** Each of the seven Looker replacements becomes one parameterized object: `reporting.usp_CameraInventory(@OrganizationId)`, `reporting.usp_PhotoCountByCamera(@OrganizationId, @StartDate, @EndDate)`, etc. The AI orchestrator learns "if the user asks for Camera Inventory, call this proc with these params." The LLM does not generate SQL for these — it generates a tool call.
2. **Ad-hoc questions → views + semantic manifest.** The orchestrator gives the LLM a JSON manifest of the `reporting.*` views with human-readable names, column descriptions, value examples, and join hints. The LLM generates SQL against those views only. The manifest is the prompt context, not a `SELECT * FROM INFORMATION_SCHEMA` call.
3. **The manifest lives in source control** in the AI service repo (e.g. `data-manifest.json`) and is the single source of truth for what the LLM is told the schema looks like. If it drifts from reality, contract tests fail (see §6).

Why this hybrid beats each pure approach:

- **Pure stored-proc catalog:** safe, but every new question type ("battery level for site X over the last 30 days") needs a new proc. We freeze users into the seven Looker reports forever. The whole point of the project is "chat to SQL"; we'd be neutering it.
- **Pure view + LLM-generates-SQL:** flexible, but the LLM will write expensive queries, will sometimes invent joins, and will sometimes succeed in producing technically-correct SQL that answers a different question than the user asked. We need a fallback for the canned reports specifically because users have learned to trust them and the SQL for "Top 10 Offline" should be deterministic, not AI-generated each time.
- **Pure semantic manifest with no views:** describes OLTP tables in friendly language, but still asks the LLM to traverse `Photo → Deployment → Site → Organization`. The LLM gets the words right and the joins wrong.

### Evolution

- New question types appear → an engineer adds a view to `reporting`, adds an entry to the manifest, runs the contract tests, ships. No LLM retraining, no system-prompt rewrite.
- OLTP schema changes → the view definitions absorb the change; the manifest stays the same; saved queries keep working. If we cannot absorb a change in a view (column truly removed), we surface a deprecation warning on saved queries that reference the affected view (§7).
- A new canned report is requested → we write a stored procedure, register it as a tool, and expose it in the AI orchestrator's tool list. We do not just expand the manifest and hope.

---

## 3. Off-limits data

The exploration notes (§6 Sensitive fields) identify the credential and PII columns. Extending that list and assigning a containment mechanism:

| Object | Treatment | Mechanism |
|---|---|---|
| `User` table (entire) | **Off-limits** | Not modeled in `reporting` schema at all. The AI never sees a user object beyond the IDs it needs for org-scoping (which come from the JWT, not from a query). |
| `User.PasswordHash`, `SecurityStamp`, `ConcurrencyStamp` | Off-limits (auth secrets) | Absent from views; backed by `DENY SELECT ON dbo.[User](PasswordHash, SecurityStamp, ConcurrencyStamp)` for the AI login as belt-and-suspenders. |
| `User.Email`, `User.PhoneNumber` | Off-limits (PII) | Same. If a future report needs "users in this org," surface display names only. |
| `Key` table (entire) | Off-limits | API keys. Not modeled in `reporting`. |
| `ImageRecognitionService.ApiKey`, `Url` | Off-limits | ML service credentials. Not modeled. |
| `Site.DasToken`, `DasBaseUrl` | Off-limits | DAS API credentials. Not modeled. |
| `Site.SmartIntegrateToken`, `SmartIntegrateBaseUrl` | Off-limits | External API credentials. Not modeled. |
| `Site.SlackWebhookUrl` | Off-limits | Webhook URL is effectively a secret. Not modeled. |
| `Device.PhoneNumber`, `Device.ForwarderPhoneNumber` | Coarsened or off-limits | Device phone numbers are PII tied to SIM contracts. The SIM Contract Renewal report needs *the existence* of a phone number and the carrier; it does not need the digits themselves. Surface a masked form (`+1***-***-1234`) or omit. |
| `Site.Latitude`, `Site.Longitude` | **Role-gated, never to LLM** | Endangered-species GPS. Even though the existing `GpsViewer` role gates these in the UI, the AI service must never include raw lat/lon in a prompt or response *under any role*. Pre-aggregated geographic answers ("3 sites in this region") are fine. Map-rendering integrations are out of scope. |
| `Deployment.Latitude`, `Deployment.Longitude` | Same as above | Same. |
| `Photo` blob URLs / image bytes | Off-limits | The AI is for tabular reporting, not image surfacing. Modeling a `PhotoUrl` column invites the LLM to embed signed URLs in chat responses. |

**Mechanism: defense in depth.**

1. **Primary mechanism — curated views.** The `reporting` schema simply does not project sensitive columns. If a column isn't in the view, the AI cannot ask for it. This is the strongest guarantee: secrets are absent, not just denied.
2. **Secondary mechanism — explicit `DENY`.** A separate read-only login (`ai_reports_reader`) used by the AI service has `DENY SELECT` on the sensitive columns at the table level. Even if a future engineer accidentally adds `User.Email` to a view, the underlying `DENY` blocks it. The error surfaces in CI long before production.
3. **Tertiary mechanism — schema-bound views.** Views in `reporting` are created `WITH SCHEMABINDING` where possible, so OLTP schema changes that would alter the projection fail loudly rather than silently changing what the AI can see.
4. **Quaternary mechanism — output filter.** Before the AI service returns a result row to the UI, it runs the result columns through a small allowlist of column-name patterns. Any column whose name matches `*token*`, `*hash*`, `*secret*`, `*password*`, `*apikey*` is dropped with a logged warning. This catches any accidental leakage from a future view.

The first three are infrastructure controls that hold even if the application code is wrong. The fourth is application-level; it exists for the case where someone names a sensitive column innocuously.

---

## 4. Query cost controls

Today the OLTP `WpsDbContext` runs with a 30-second command timeout (`Wps.Watch.Api/Startup.cs:156`, per exploration-notes §8). For the AI service we tighten this *and* add the cost controls below.

| Control | Recommendation | Why |
|---|---|---|
| Statement timeout (default) | **15 seconds** | Most canned reports run sub-second. 15s is a generous ceiling for ad-hoc queries against the curated views. If a query genuinely needs longer, it's not chat-shaped — it's an export and should go to the export path. |
| Statement timeout (export path) | 120 seconds | A separate code path triggered when the user explicitly requests "export to CSV." Different connection, different timeout, different rate limit. |
| Default row cap | **5,000 rows** | `TOP 5000` injected into every ad-hoc query unless the orchestrator explicitly overrides for an export. Chat UIs do not usefully render more. |
| Hard row cap | 100,000 rows | Even on export, refuse to materialize a result larger than this without an admin-only flag. Protects the service memory and the user's browser. |
| Plan inspection | Mandatory `SET STATISTICS IO/TIME` capture in dev/staging; per-query plan hash logged in prod | We don't bake `OPTION (RECOMPILE)` into every query — that's expensive and usually wrong. We do log the plan hash so the DB team can find the heavy hitters via Query Store. |
| Read-only login | **Required.** `ai_reports_reader` with `db_datareader` on `reporting` only, plus the `DENY`s above | Per §3. |
| Connection string | `ApplicationIntent=ReadOnly` against the replica endpoint | Required to route to the read replica rather than the primary. Misconfiguration here is the one way an AI query could block production writes; treat it as a deploy-blocking check. |
| Isolation level | **Read-committed snapshot (RCSI)** | We don't need full snapshot isolation; we need readers not to block on writers. RCSI is the right balance. The OLTP migrations use `SERIALIZABLE` (per exploration-notes §7) but those are migration scripts, not query workloads — different concern. |
| Query Store | Enabled on the replica with a 7-day retention and `WAIT_STATS_CAPTURE_MODE = ON` | The DB team's primary tool for "what's the AI service actually running, and which queries are causing CPU spikes?" Without this we are flying blind. |
| Cost-profile metadata | Each stored proc and each named view carries an `expected_cost_class` (`fast` < 1s, `medium` < 5s, `slow` < 15s) in the manifest | The orchestrator surfaces "this is a heavy query, expect ~10s" in the UI before running. Sets user expectations and gives us telemetry on actual vs expected. |
| Concurrency cap | Max 5 concurrent queries per user, max 25 per service instance | Prevents one user firing 50 questions in a row from monopolizing the replica. Implemented at the AI service layer, not the DB. |
| Resource governor (future) | Optional — `RESOURCE GOVERNOR` workload group on the replica capping the AI login at 30% CPU | Worth setting up if/when we observe contention. Not required for MVP. |

The cost-profile metadata point is the one most often skipped; it's also the one users notice the most. A canned report with `expected_cost_class = fast` should run in under a second; if it doesn't, the orchestrator logs that mismatch and the DB team gets a weekly digest of "queries whose actual cost exceeds expected by >2x." That's how we keep the system honest.

---

## 5. Time-series shapes — what to pre-aggregate

The OLTP tables that get expensive at reporting cadence are `Photo` (counts grouped by date) and `Deployment` (online/offline derivation, battery snapshots over time). The exploration notes confirm there's no pre-aggregation today (§7 stored procedures and views). Recommendations, in priority order:

### 5.1 `reporting.fct_photo_count_daily` — **must-build**
- **Grain:** one row per (`deployment_id`, `device_id`, `site_id`, `organization_id`, `capture_date`).
- **Columns:** `photo_count`, `video_count`, `removed_count`, `reviewed_count`, `favorited_count`, `reported_count` (booleans on `Photo` rolled up to counts), `first_capture_utc`, `last_capture_utc`.
- **Source:** `Photo` table, `IsRemoved = false` for the visible-count and as a separate column for the removed-count.
- **Update mechanism:** nightly job at 02:00 UTC that recomputes the trailing 3 days (allows for late-arriving photos and `IsCleared` flips). Older days are immutable. Initial backfill is a one-time `INSERT … SELECT` over the full `Photo` history; expected to run in tens of minutes against the replica.
- **Storage cost:** at WPS scale (rough order of magnitude — 10k active deployments × 365 days × 1 row × ~120 bytes), ~430 MB/year. Negligible.
- **Win:** "Photo Count By Camera" goes from a `GROUP BY` over the `Photo` table to a sum over a tiny aggregate. Sub-100ms instead of sub-30s.

### 5.2 `reporting.fct_device_event_hourly` — **should-build**
- **Grain:** one row per (`deployment_id`, `device_id`, `event_hour`).
- **Columns:** `event_count`, `last_event_utc`, `min_battery_level`, `max_battery_level`, `avg_battery_level`.
- **Source:** Whatever feeds `Deployment.LastEventDateTimeUtc` and `Deployment.EventCount`. If those are the only event-shape fields we have (the exploration notes don't reveal a `DeviceEvent` table), we derive from `Photo.IngestDateTimeUtc` as a proxy until/unless an event table is added.
- **Update mechanism:** hourly job. Trailing 6 hours recomputed each run.
- **Storage cost:** 10k deployments × 24 × 365 = 87.6M rows/year × ~80 bytes ≈ 7 GB/year. Larger but still cheap. Compressible via columnstore index — recommend `CLUSTERED COLUMNSTORE` for this table specifically.
- **Win:** "last seen" and "Top 10 Offline" become a one-row-per-device max query against the daily rollup or a `WHERE last_event_utc < @threshold` filter on the latest hourly bucket.

### 5.3 `reporting.fct_battery_snapshot_daily` — **nice-to-have**
- **Grain:** one row per (`deployment_id`, `snapshot_date`).
- **Columns:** `battery_level_eod` (the last value seen in the day), `battery_level_min`, `battery_level_max`, `solar_panel` (point-in-time copy of the bool).
- **Update mechanism:** nightly. Source is `Deployment.BatteryLevel` over time. Note: the OLTP table only stores the *current* battery level, not history — so this fact table is only meaningful if we **start** capturing snapshots prospectively. Backfill is impossible.
- **Storage cost:** trivial.
- **Win:** "show me battery level over the last 90 days for these deployments" becomes a primary-key seek on a small table. Without this the question literally cannot be answered (data does not exist today).

### 5.4 `reporting.dim_deployment` and `reporting.dim_organization` — supporting dimensions
- Slim, denormalized lookups so the fact tables can be sliced without joining back to OLTP.
- Refreshed nightly (or via change tracking if we want to be fancy).
- Includes the human-friendly names the LLM needs (`organization_name`, `region_name`, `site_name`, `device_make_model`).
- **The org-scope chain (`Photo → Deployment → Site → Organization`, exploration-notes §6) is collapsed to a single column on each fact table — `organization_id` is duplicated onto every row.** This is the entire point of denormalization. The AI never has to traverse the chain.

### What we *don't* pre-aggregate yet

- No per-photo metadata aggregates beyond counts. If "show me all dawn-captured photos" becomes a thing, we revisit.
- No cross-org rollups. MVP is single-org (per the human owner's call recorded in exploration-notes §5).
- No Cosmos data. The exploration notes confirm Cosmos holds preferences and dashboard state only — not reporting-relevant data.

### Maintenance mechanism — what runs the jobs

Recommend **Azure SQL elastic jobs** (cheapest option that doesn't require us to operate a separate scheduler) running stored procedures in the `reporting` schema. Failures alert via the existing App Insights wiring. Each job logs its run to a `reporting.job_run_log` table so we can answer "is the data current?" without guessing.

We deliberately do **not** use SQL Server indexed views for the fact tables. Indexed views have severe restrictions (no `OUTER JOIN`, no subqueries, schema-bound to the underlying OLTP tables) that conflict with our goal of decoupling from OLTP evolution. Plain summary tables with scheduled refresh are the right shape.

---

## 6. Schema evolution — keeping the AI's understanding in sync

The cost of getting this wrong, as the prompt notes, is the AI giving silently-incorrect answers. That's worse than failing — it's a credibility-destroying outcome.

**Recommendation: contract tests in CI plus a generated manifest, with the curated `reporting` schema serving as the versioned contract.**

The four options as posed:

- **Manifest file refreshed manually:** rejected. The team will forget. Manual refresh of a contract is how you get silently-incorrect answers.
- **Generated docs from EF metadata, refreshed on every API release:** partial fit. Useful as one input, but EF metadata describes the OLTP schema, not the reporting layer the AI actually queries. Not enough on its own.
- **Contract tests that fail CI when an exposed table/view changes shape:** **the core mechanism.** Every view and every stored proc in `reporting` has a test that asserts its column names, types, and (for procs) parameter signatures. The AI service repo carries the manifest; the test suite asserts the manifest matches the live schema.
- **Versioned reporting schema with explicit deprecation cycles:** **the wrapper.** When a view's shape *needs* to change, we publish `reporting.vw_DeploymentBattery_v2`, leave the v1 view in place for 90 days emitting a deprecation warning to App Insights every time it's queried, and update the manifest to point at v2.

Concrete pipeline:

1. **Manifest format.** A `data-manifest.json` in the AI service repo describing each exposed object: name, type (view/proc), purpose, columns (name, type, description, nullable, example), parameters (for procs), `expected_cost_class`, `version`.
2. **Generation.** A script in the SQL repo extracts the live `reporting` schema and emits a candidate manifest. Diffing the candidate against the committed manifest is the contract test.
3. **CI gate.** The SQL migrations repo's CI runs the diff. Any change that adds, removes, or alters a `reporting.*` object requires a corresponding manifest update in the AI service repo. Cross-repo coordination is enforced by the build, not the calendar.
4. **Deployment ordering.** The reporting schema is forward-compatible (new objects, new columns) before the AI service consumes them. Backward-incompatible changes follow a v1/v2 deprecation cycle. The AI service is never the cause of a broken deploy because its understanding always trails the schema.
5. **Monitoring.** App Insights tracks every saved-query execution. A query that targets a deprecated object emits a warning event. A weekly digest to the data architect team flags "saved queries scheduled for breakage in <30 days."

The OLTP schema changes whenever the API team ships a migration. None of that has to ripple to the AI service immediately, *because the AI service does not query OLTP directly*. The view layer absorbs the change. This is the second-largest reason for choosing the hybrid in §1 (the first being the security boundary).

---

## 7. Saved-SQL implications (phase 3)

The Jira ticket is explicit: "save the SQL not the prompt." This is the right call (prompts are unstable across LLM versions; SQL is reproducible) but it makes saved queries part of our system surface. Implications:

### 7.1 Schema pinning at save time vs. re-validation at run time

**Recommendation: pin the manifest version at save time, re-validate against the live schema on every run.**

- The saved record stores: the SQL, the `data_manifest_version` at save time, the user-facing name/description, the user/org that saved it.
- On every run we check: is the SQL syntactically valid? Does it reference only objects in the *current* manifest? Does the current user have permission on the org-scope of the underlying data?
- If the manifest version has advanced and the query still parses, run it. If it references a deprecated object, run it but emit a warning. If it references a deleted object, fail loudly with a "this saved query needs updating" message.

Pinning the version at save time is what lets us tell the user *why* their query broke — not just "syntax error" but "this query was saved against manifest v3.2; column X was removed in v4.0; here is the suggested replacement."

### 7.2 Migration safety — what happens when a column disappears

The deprecation cycle from §6 is the answer. A column is never just removed; it goes through a v1/v2 view cycle with a 90-day overlap. During that window, saved queries referencing v1 still work and emit deprecation warnings. After v1 is dropped, queries referencing it fail with a clear "deprecated since 2026-08-01" error message and a pointer to the v2 equivalent.

For ad-hoc saved queries written against a specific column that *can't* be preserved in v2 (rare, but possible), the user is told at save time what the deprecation horizon looks like. We do not silently break their work.

### 7.3 Validation on load

Three layers, in order:

1. **Syntax validation.** Parse the saved SQL through SQL Server's `sys.dm_exec_describe_first_result_set_for_object` or an `EXPLAIN`-equivalent in a sandboxed connection. Catches dropped tables/columns immediately.
2. **Permission validation.** Check that the current user has permission on the org-scope of the data the query touches. Saved queries are not transferable across orgs in MVP — a query saved by an OrgA user is not visible to an OrgB user. Multi-org saved queries are out of scope until the cross-org work happens.
3. **Manifest validation.** Confirm every `reporting.*` object the query references exists in the current manifest with a compatible signature. Mismatches surface as deprecation warnings or hard errors as appropriate.

### 7.4 Storage

- New backend table `reporting.saved_query` (or a similar name): `id`, `organization_id`, `user_id`, `name`, `description`, `sql_text`, `manifest_version`, `created_utc`, `last_run_utc`, `last_run_status`. **Saved queries are scoped to a single org** in MVP; the `organization_id` column is non-nullable and indexed. Cross-org saved queries are a future item.
- An audit table `reporting.saved_query_run` logs every execution for cost and security review.
- Index on `(organization_id, user_id)` for the "show me my saved queries" UI path.

### 7.5 Editing saved queries

If the user edits the SQL of a saved query, we treat it as a new save (new `manifest_version` pinned). We don't try to be clever about incremental changes; the saved-query record is small and immutability simplifies the audit story.

---

## 8. The unresolved-data questions from the exploration notes

### 8.1 SIM Prepaid Data — source-of-truth recommendation

The exploration notes (§9 Open Question 1) flag that the EF model has plan limits but no `DataUsedGb` / `DataRemainingGb` columns. The current Looker report likely pulls from Twilio's Super SIM API or a carrier portal not modeled in EF.

**Recommendation:** Model it explicitly in the OLTP schema. Add a `Device.PrepaidDataUsedGb` (nullable decimal) and `Device.PrepaidDataLastSyncedUtc` (nullable datetime). Build a small sync job (separate from the AI service, owned by the API team) that polls Twilio (or whichever carrier API is the actual source) on a daily cadence and writes those columns. The AI service then reads them through `reporting.vw_DeviceSimStatus` and the report becomes deterministic.

The alternative — having the AI service call Twilio at query time — is worse for three reasons:
1. Twilio rate limits become AI-feature rate limits. One user spamming the report rate-limits everyone.
2. The AI service starts needing carrier API credentials, which is a security-surface expansion we don't want.
3. We can't pre-aggregate or cache a value that doesn't live in our database.

This is a small piece of API team work that pays back immediately in report performance and predictability. **Open question for the API team**, but the data-architecture answer is unambiguous: the source of truth should be a column in our database, populated by a background sync.

If the API team can't take this on for MVP, the fallback is: the AI report shows "Plan limit: 5 GB; Current usage: not tracked in WPS Watch — see carrier portal." Honest is better than fake.

### 8.2 "Top 10 Offline" threshold

The exploration notes (§9 Open Question 2) flag that "offline" is computed from `Deployment.LastEventDateTimeUtc` with no defined threshold.

**Recommendation:**
- **Default threshold: 24 hours.** A camera that hasn't reported an event in 24 hours is operationally interesting; a 1-hour threshold is too noisy (cell coverage gaps, scheduled uploads), a 7-day threshold is too late (we want to know within a day if a deployment has gone dark).
- **Override mechanism:** the threshold is a parameter on the `Top10Offline` stored proc — `@OfflineThresholdHours INT = 24`. The AI orchestrator exposes this as an optional natural-language override ("show me cameras offline for more than 3 days" → calls the proc with `@OfflineThresholdHours = 72`). The default is documented in the manifest so the LLM knows about it.
- **Surfacing in the UI:** the report header reads "Cameras offline for more than 24 hours" with an obvious editable threshold control. No invisible defaults.
- **A second column worth adding:** `expected_event_interval` per device or per device-make. A solar-powered camera that reports daily is not "offline" at 25 hours; a high-traffic camera that reports hourly is *very* offline at 25 hours. If the data exists or can be derived from event history, expose `hours_since_last_event_relative_to_typical` and rank by that. MVP can use the flat 24-hour threshold; v2 should consider this.

---

## 9. Data-layer requirements

These are the requirements that the synthesis doc and downstream personas can reference by number.

1. **Read source.** The AI Reports service reads from `wpswatchprodreplica.database.windows.net` (the Azure SQL read replica), not from the primary. Connection string includes `ApplicationIntent=ReadOnly`.

2. **Schema isolation.** A new `reporting` schema is created on the replica (and replicated from primary). The AI service has access to objects in `reporting` only.

3. **Read-only login.** A dedicated SQL login `ai_reports_reader` is created with `db_datareader` on `reporting` and **no permissions on `dbo`**. Auth via managed identity where possible; fallback to a connection-string secret stored in Key Vault.

4. **Sensitive-column denial.** `DENY SELECT` is applied at the column level on the OLTP tables for the credential and PII columns enumerated in §3, against the AI login. This is belt-and-suspenders; primary protection is non-modeling in `reporting`.

5. **No raw OLTP exposure.** The AI orchestrator may not, under any circumstance, generate or execute SQL against the `dbo` schema. All queries target `reporting.*`.

6. **Stored-proc catalog for canned reports.** The seven Looker replacements (Camera Inventory, Battery Level, Photo Count, Region & Site Totals, SIM Contract Renewal, SIM Prepaid Data, Top 10 Offline) plus the three regular-user reports are implemented as parameterized stored procedures in `reporting`. The LLM does not generate SQL for these; it generates a tool call with parameters.

7. **View catalog for ad-hoc queries.** Ad-hoc natural-language questions resolve to LLM-generated SQL against `reporting.*` views only. The view layer hides the OLTP join chain (notably `Photo → Deployment → Site → Organization`).

8. **Semantic manifest.** A `data-manifest.json` in the AI service repo describes every exposed object with human-readable names, descriptions, types, examples, parameters, and `expected_cost_class`. This is the LLM's only source of schema knowledge.

9. **GPS columns are never surfaced to the LLM.** `Site.Latitude/Longitude` and `Deployment.Latitude/Longitude` are absent from all `reporting.*` views.

10. **Statement timeout: 15 seconds default, 120 seconds for the explicit export path.**

11. **Row caps.** Default `TOP 5000` injected into ad-hoc queries; hard ceiling of 100,000 rows even on export; admin-only flag required to exceed the hard ceiling.

12. **Read-committed snapshot isolation (RCSI)** is enabled on the reporting database / schema. Full snapshot isolation is not required.

13. **Query Store** is enabled on the replica with 7-day retention and `WAIT_STATS_CAPTURE_MODE = ON` for diagnostic visibility.

14. **Cost-profile metadata.** Every stored proc and every named view carries an `expected_cost_class` (`fast` < 1s, `medium` < 5s, `slow` < 15s) in the manifest. The orchestrator surfaces an estimate to the user before running and logs actual-vs-expected delta for ops review.

15. **Per-user concurrency cap of 5 simultaneous queries; per-service-instance cap of 25.**

16. **Pre-aggregation: photo-count daily fact table.** `reporting.fct_photo_count_daily` is built as a non-negotiable MVP item. Refreshed nightly with a 3-day trailing recompute window. (§5.1)

17. **Pre-aggregation: device-event hourly fact table.** `reporting.fct_device_event_hourly` is built for MVP. Refreshed hourly with 6-hour trailing recompute. Backed by a clustered columnstore index. (§5.2)

18. **Pre-aggregation: battery-snapshot daily fact table** is added prospectively (no backfill possible — OLTP only stores current value). (§5.3)

19. **Denormalized dimensions.** `reporting.dim_deployment` and `reporting.dim_organization` (and others as needed) collapse the OLTP join chain so fact tables carry `organization_id` directly. The AI never traverses joins for org scoping.

20. **Refresh mechanism.** Pre-aggregations are kept current by Azure SQL elastic jobs running stored procedures in `reporting`. Failures alert via App Insights. A `reporting.job_run_log` table records freshness.

21. **Schema-evolution contract tests.** A CI job in the SQL migrations repo diffs the live `reporting` schema against the committed `data-manifest.json` and fails if they disagree. The AI service is never deployed against an unknown schema.

22. **Versioned reporting objects.** Backward-incompatible changes to a `reporting` view or proc go through a v1/v2 cycle with a 90-day deprecation overlap. v1 emits a deprecation warning to App Insights for every execution during overlap.

23. **Output filter.** Before returning result rows to the UI, the AI service drops any column whose name matches sensitive-pattern allowlist (`*token*`, `*hash*`, `*secret*`, `*password*`, `*apikey*`). Logged warnings for any drop indicate a view-layer mistake.

24. **Saved-query storage.** A new `reporting.saved_query` table stores the SQL, pinned `manifest_version`, owner user/org, name, description, and audit timestamps. Saved queries are single-org in MVP.

25. **Saved-query validation on load.** Three-layer check on every run: syntax, permission, manifest. Deprecation warnings are surfaced to the user; deletions are hard errors with a pointer to the replacement.

26. **Saved-query audit.** Every saved-query execution writes a row to `reporting.saved_query_run` with user, org, manifest version, runtime, status, row count.

27. **SIM Prepaid Data source of truth.** A new column `Device.PrepaidDataUsedGb` (and `PrepaidDataLastSyncedUtc`) is added to the OLTP schema, populated by a daily sync from the carrier API (owned by the API team). The AI report reads from a `reporting.vw_DeviceSimStatus` projection of this column. If this work cannot ship for MVP, the report displays "not tracked in WPS Watch" rather than calling Twilio at query time.

28. **Top 10 Offline default threshold.** 24 hours, exposed as a parameter `@OfflineThresholdHours` on the stored proc, overridable via the orchestrator and surfaced as an editable control in the UI. A future enhancement may use per-device expected-event-interval data for relative offline-ness.

29. **No cross-org data in MVP.** All `reporting.*` objects accept a single `@OrganizationId` parameter (or filter on a single `organization_id`) and reject multi-org queries. Cross-org reporting is a phase-2 item, gated by the broader cross-org migration tracked in the API team's roadmap.

30. **Deployment ordering invariant.** The `reporting` schema is always deployed *before* the AI service is updated to consume new objects. Backward-incompatible removals in `reporting` are always *after* the AI service has stopped consuming them. This is enforced by the contract-test CI gate (req. 21).

---

## 10. What I'm not recommending and why

A few things worth being explicit about so the synthesis doc isn't surprised.

- **No separate mirror DB for MVP.** Real cost, real ETL, no concrete pain to justify it yet. The hybrid covers us.
- **No `WITH (NOLOCK)`.** It's still wrong. RCSI is the correct answer.
- **No raw `INFORMATION_SCHEMA` introspection by the LLM.** Even on the curated `reporting` schema. The manifest is the contract; introspection invites drift.
- **No row-level security via SQL Server's `CREATE SECURITY POLICY`.** Tempting, but it imposes per-row predicate checks on every query, hurts plan caching, and adds operational complexity. We achieve the same outcome by parameterizing every proc/view on `@OrganizationId` and validating at the application layer.
- **No Cosmos involvement.** The exploration notes confirm Cosmos holds preferences only. Adding Cosmos as a reporting source for MVP would be scope creep with no payoff.
- **No multi-database federated queries.** If we ever need to join the OLTP and Cosmos data for a report, we move that join into our own pre-aggregation layer rather than asking SQL Server to do it at query time.

The AI Reports project is fundamentally about *making the right answer easy and the wrong answer hard*. The recommendations above all flow from that single principle: a curated semantic layer the LLM cannot escape, a security boundary that holds even if the application code is wrong, and pre-aggregations that make the common case cheap so we can afford to be careful about everything else.
