# AI Reports — Requirements Document (v4)

**Status:** Draft — for wpsWatch dev team review.
**Supersedes:** [`ai-reports-requirements-v3.md`](./ai-reports-requirements-v3.md) (v3, 2026-05-14). v1, v2, and v3 preserved unchanged for comparison.
**Authors:** Synthesis of Stage 1 codebase exploration ([v3 notes](./ai-reports-exploration-notes-v3.md)) + four parallel persona reviews (backend, security, user, data) + Matt Hron's v1 review comments + post-v3 stakeholder feedback on telemetry and catalog growth.
**Date:** 2026-05-14.

> **How to read this document.** Sections 1–10 are the requirements proper. Section 11 lists open questions the dev team needs to resolve before the design doc. Section 12 is a glossary. File-path citations point to one of four repos: `wps.watch.api`, `wps.watch.web`, `wpswatch.reporting`, and `wps.watch.functions.scheduledreporting`.

---

## What changed from v3 to v4

Two additions driven by stakeholder feedback after v3 was circulated:

1. **Phase 2 explicitly includes adding new canned reports.** v3 framed Phase 2 as "free-form chat + export + cross-org." v4 expands this: as users use the chat feature, the dev team observes which questions get asked repeatedly and **promotes those patterns into new canned reports** that all users (within the existing role tiers) can run with one click. Phase 2 is no longer just "the chat feature shipping" — it's the start of an ongoing **catalog-growth loop**.

2. **Usage telemetry, placed in Phase 2.** A new instrumentation layer captures who runs which reports, which questions are asked in chat, and which generated SQL queries are most common. This telemetry feeds two purposes: operational visibility (which reports are popular vs. dead weight) and the catalog-growth loop above (data-driven evidence for which patterns to promote to canned reports). Placement choice: Phase 2 rather than Phase 3, because the catalog-growth feature requires it and because chat-question patterns can only be observed from Phase 2 onward.

Together these changes turn Phase 2 from a "ship chat" phase into a "ship chat, learn from it, expand the catalog" phase. The numbering scheme: requirements that change get a `-V4-` infix. v3 requirements that don't change keep their previous IDs.

---

## 1. Background and motivation

*Same as v3 §1.*

---

## 2. Goals and non-goals

### Goals

**Phase 1 (MVP).** *Same as v3.* Replace existing canned Looker reports with an in-product page, with the four UX upgrades (sortable columns, dynamic filtering, summarisation, flexible date range). Mirror existing role gating exactly. Single-org. Cutover sequence: pilot side-by-side, SysAdmin first, regular users second.

**Phase 2 — *(expanded in v4: catalog grows, telemetry ships)*.**
- Free-form chat ("ask wpsWatch a question") gated to `AdminReport` initially, opened to `UserReport` after Phase 2 stabilises.
- File export (PDF, CSV, DOCX, XLSX, KML, GeoJSON, PNG) of report and chat results.
- Cross-org reporting (the new feature becomes the first cross-org reporting surface in wpsWatch).
- **(v4 new) Usage telemetry across canned reports and chat.** Captures who runs what, when, with what parameters, and (for chat) the question asked, the generated SQL, the latency, the result row count. Foundation for the catalog-growth loop below and for operational visibility.
- **(v4 new) Catalog-growth loop.** The dev team monitors telemetry to identify chat questions that are asked repeatedly and produce stable, useful answers. Those patterns are converted into new canned reports — same shape as the original 10, added to the manifest, surfaced as one-click reports in the same UI. New reports inherit role-tier gating (Field Team review process per v3 §7 applies if a new report should be `UserReport`-tier rather than `AdminReport`).
- **(v4 new — possibly slipping to Phase 3) Promotion workflow.** A tool (SysAdmin-only) that surfaces the telemetry, lets a reviewer cluster similar chat questions, preview the generated SQL, parameterise it, and publish as a new canned report. The MVP version of the catalog-growth loop is manual (dev team adds reports via PR); the workflow tool is the polished version that lets non-developers do the same thing.

**Phase 3.** *Same as v3*, with one v4 note:
- Save and re-run custom SQL queries with manifest-pinned schema validation and relative-date support.
- Share saved queries within an org.
- Diff-against-last-run on saved queries.
- Scheduled email digest (FR-V3-48a global-timer variant; FR-V3-48b per-org local-time variant).
- Subscription-to-saved-query unification (extend `ScheduledReport` table vs new table — design council).
- **(v4 fallback) Promotion workflow** — if it doesn't ship in Phase 2 due to capacity, it ships here.

**Longer-term (post-Phase 3).** *Same as v3.*

### Non-goals
*Same as v3*, with one v4 clarification:
- **No automatic promotion of chat patterns to canned reports.** The catalog-growth loop is reviewer-driven: a human evaluates each pattern and decides whether to promote it, what to name it, what its parameters should be, and which role tier sees it. The system surfaces candidates; humans decide. (Per Eric's "augment, do not replace" principle and S-MUST-25 — no follow-up action solely from tool output.)

---

## 3. User stories

*Same as v3*, plus three new stories for the v4 additions:

### Catalog growth and telemetry (phase 2)
- **US-V4-26 — Phase 2.** As a System Admin, I want to see which reports are used most often, by which roles, and which are barely touched — so I can prioritise where to invest and identify dead reports to retire.
- **US-V4-27 — Phase 2.** As a System Admin, I want to see which chat questions are asked repeatedly across users — so I can decide which patterns deserve to become canned reports.
- **US-V4-28 — Phase 2.** As a System Admin (or dev), I want to take a frequently-asked chat question, review the SQL it generated, parameterise it where appropriate, and publish it as a new canned report — so all users (within the right role tier) can run it with one click instead of typing the question every time.

---

## 4. Functional requirements

### 4.1 — 4.7 Same as v3.

### 4.8 Behavioural and trust requirements

*Same as v3 (FR-40 to FR-45).*

### 4.9 Connectivity and graceful degradation (phase 2+)

*Same as v3.*

### 4.10 Usage telemetry and catalog growth — *new section in v4 (phase 2)*

- **FR-V4-49.** **Telemetry events on canned report runs.** Every canned-report execution emits a structured event with: `user_id` (full, not hashed — SysAdmin scope only), `role`, `organization_id`, `report_id`, `parameters` (with sensitive values redacted per a configurable allowlist), `started_utc`, `completed_utc`, `latency_ms`, `row_count`, `status` (success/timeout/error), `source` (UI / scheduled / chat-invoked). Events go to App Insights (operational view) and to a new `wpsWatch.report_run_log` table (queryable for the dashboard in FR-V4-51).

- **FR-V4-50.** **Telemetry events on chat interactions.** Every chat turn emits: `user_id`, `role`, `organization_id`, `conversation_id`, `prompt_text` (full text, access-restricted; see security note below), `generated_sql` (or tool-call name + parameters if no SQL), `views_or_objects_referenced`, `started_utc`, `completed_utc`, `latency_ms`, `row_count`, `status`, `user_action_taken` (viewed only / exported / saved as query / discarded). Events go to App Insights and to a new `wpsWatch.chat_interaction_log` table.

- **FR-V4-51.** **Telemetry dashboard.** A new admin-only page in the wpsWatch React app shows: (a) most-used canned reports by run count and unique users, weekly and monthly; (b) least-used canned reports (candidates for retirement); (c) most-asked chat-question patterns, clustered by semantic similarity of the generated SQL (not the prompt text — see security note); (d) chat questions that failed or returned zero rows (candidates for new canned reports or for view-layer improvements); (e) usage by org and by role tier. Dashboard accessible only to SystemAdmin. Drill-down to individual events accessible only to SystemAdmin within the same restricted audit-log access tier (per S-MUST-32).

- **FR-V4-52.** **Catalog-growth: manual path (must ship in Phase 2).** The dev team can identify a chat-question pattern from the telemetry, write a new view or stored proc in the `wpsWatch.*` schema, add it to the AI Reports manifest, assign a role tier, and ship — same mechanism used for the original 10 reports. The dashboard (FR-V4-51) surfaces candidate patterns; the implementation path is a normal PR.

- **FR-V4-53.** **Catalog-growth: promotion workflow (should ship in Phase 2, may slip to Phase 3).** A SysAdmin-only tool in the AI Reports admin UI that:
  1. Surfaces clustered chat patterns from the telemetry.
  2. Lets the reviewer preview the generated SQL for each cluster, see the row counts, see how many distinct users asked it.
  3. Lets the reviewer convert the SQL into a parameterised stored proc or view (with the reviewer choosing which literal values become parameters, naming the report, writing the description).
  4. Assigns a role tier (AdminReport / UserReport) for the new report.
  5. Publishes — adds to the manifest, triggers a CI build, surfaces the new report in the next user load.
  This is essentially a low-code "create a canned report from a query" workflow. It does not bypass any validation that would apply to a hand-written report — same AST validator, same column-allowlist filter, same role-gate enforcement.

- **FR-V4-54.** **Role-tier gating for new canned reports.** When a new canned report is added (either manually per FR-V4-52 or via the workflow per FR-V4-53), the default role tier is `AdminReport`. Promotion to `UserReport` follows the existing Field Team review process (per v3 §7) — out of scope for the AI Reports project, but the manifest supports the re-tiering as a content-only edit.

- **FR-V4-55.** **Telemetry retention.** `report_run_log` retained for 13 months (matches the existing audit-log retention in S-MUST-32). `chat_interaction_log` retained for 13 months. Aggregate counts (anonymised, no user_id, no full prompt text) may be retained longer for trend analysis — design council decision per OQ-V4-27.

- **FR-V4-56.** **Telemetry exclusions.** Volunteer users — already excluded from AI Reports entirely (FR-08) — also generate no telemetry rows. Test accounts and SystemAdmin "preview" requests can be tagged with an `is_test` flag to exclude from the dashboard's headline counts (so dogfooding by the team doesn't skew the "most-used" rankings).

### Privacy and security note on telemetry

The `chat_interaction_log.prompt_text` column is genuinely sensitive — a user's question reveals their operational interest in a way the result rows alone do not (e.g., "where has rhino X been most active this week" is a high-signal question even before the answer). v3 §6's existing audit-log restrictions (S-MUST-32, S-MUST-33) apply: separate restricted store, SystemAdmin + named security-ops group only, audit-of-audit logging on every read.

The dashboard in FR-V4-51 distinguishes between **aggregate views** (which canned reports are popular — low sensitivity, SysAdmin only) and **drill-down to prompts** (which requires the same access as reading the audit log — restricted-tier SysAdmin only). Clustering chat patterns by **generated SQL** rather than by **prompt text** reduces the surface area where prompt text appears in the dashboard UI.

---

## 5. Non-functional requirements

*Same as v3*, plus:

- **NFR-V4-09.** Telemetry write path must not slow down the user-facing request. Events emitted to App Insights are fire-and-forget; writes to `report_run_log` and `chat_interaction_log` happen asynchronously (e.g. via a queue or a background flush). A telemetry write failure must not fail the report or chat request.

---

## 6. Security and privacy requirements

*Same as v3 §6*, plus three v4 additions:

- **S-V4-MUST-38.** `chat_interaction_log.prompt_text` is treated as audit-tier data. Access matches the existing S-MUST-32 audit-log access controls: SystemAdmin + named security-ops group only, audit-of-audit on every read, 13-month retention.
- **S-V4-MUST-39.** The telemetry dashboard (FR-V4-51) exposes aggregate counts and SQL-clustered chat patterns by default. Drilling into individual prompt text requires the same elevated access as reading the audit log. Two distinct UI states; the elevated one is logged.
- **S-V4-SHOULD-14.** Telemetry retention beyond 13 months is permitted only for fully-anonymised aggregates (no user IDs, no full prompt text, only counts and SQL-cluster identifiers).

---

## 7. Permission model

*Same as v3.*

**(v4 addition)** New canned reports added through the catalog-growth loop default to `AdminReport` tier. Re-tiering to `UserReport` follows the existing Field Team review process. The manifest supports per-report tier assignment; tier changes are content-only edits with no code changes required.

---

## 8. Architecture options considered

*Same as v3 §8.*

**(v4 addition)** Telemetry storage: aligned with v3's substrate decision. `report_run_log` and `chat_interaction_log` tables live in the same database as the rest of the analytics layer (`wps-sql-analytics-prod` under the v3 plan). This keeps the dashboard's queries on the same connection the rest of the AI Reports service uses. The audit-log access restrictions are enforced via SQL grants on the AI service's read-write principal (it can write to the log tables but not read them; reads happen through a separate, restricted-access principal used by the dashboard).

---

## 9. Data layer requirements

*Same as v3 §9 (DR-V2-01 through DR-V3-34)*, plus:

- **DR-V4-35.** New tables in the analytics DB:
  - `wpsWatch.report_run_log` — one row per canned-report execution. Indexed on `(report_id, started_utc)` for dashboard queries and on `(user_id, started_utc)` for audit lookups.
  - `wpsWatch.chat_interaction_log` — one row per chat turn. Indexed on `(started_utc, status)` for "recent failures" queries and on `(generated_sql_hash, started_utc)` for clustering. The full `prompt_text` and `generated_sql` columns are NOT indexed and live in a column-encrypted form using Azure SQL Always Encrypted (or equivalent) — the decryption key is held by the restricted-access dashboard principal only.

- **DR-V4-36.** **Clustering signal.** Each `chat_interaction_log` row carries a `generated_sql_hash` (SHA-256 over a normalised form of the generated SQL — variable names canonicalised, literal values stripped). The dashboard groups by this hash to identify "the same query asked 50 different ways." Hashing the *generated SQL* rather than the *prompt* gives us semantic clustering for free (the LLM converging on similar SQL for similar intents) without having to do natural-language clustering ourselves.

- **DR-V4-37.** **Telemetry refresh cadence.** Unlike the existing aggregation tables which are refreshed by runbook on a schedule, the telemetry tables are append-only and written live by the AI Reports service. No batch refresh job. The dashboard runs queries directly against the log tables; for performance the most-used dashboard queries may have materialised summary views refreshed daily.

---

## 10. Phasing proposal

### Phase 1 (MVP)

*Same as v3.* Tracer bullet: Region & Site Totals. Replace the 10 canned reports with the four UX upgrades. SysAdmin-first cutover. Effort: ~9.5–13 engineer-weeks.

### Phase 2 — *(v4 expanded: catalog grows during the phase, telemetry ships)*

**Substrate checkpoint** (per v3 — re-evaluate analytics DB pattern vs. read replica vs. warehouse before implementation).

**Phase 2 deliverables:**
- Free-form chat panel gated to `AdminReport`.
- MCP tool catalogue + `query_ai_views` against the `vw_ai_*` set.
- AST validator + parameter-injection middleware.
- Anthropic host loop with streaming, tool-use orchestration, audit.
- Eval suite of golden questions.
- File export pipeline (all formats; net-new).
- Cross-org reporting (the first cross-org reporting surface in wpsWatch).
- **(v4 new) Telemetry events** on canned reports and chat (FR-V4-49, FR-V4-50). Foundation that ships at the start of Phase 2 alongside chat — without it we lose data on chat's earliest months.
- **(v4 new) Telemetry dashboard** (FR-V4-51). Mid-Phase-2.
- **(v4 new) Catalog growth via manual path** (FR-V4-52). The dev team adds new canned reports as patterns emerge in chat telemetry. Estimated 3–8 new reports added during Phase 2 — the exact number depends on what patterns are observed.
- **(v4 new — possibly slips to Phase 3) Promotion workflow** (FR-V4-53). The polished low-code tool for SysAdmins to promote chat patterns to canned reports without a dev PR.

**Revised effort estimate (engineer-weeks):**

| Piece | v3 estimate | v4 estimate | Why |
|---|---|---|---|
| v3 Phase 2 scope (chat + export + cross-org) | 10–18 | 10–18 | Unchanged. |
| Telemetry events + storage | n/a | ~1.5 | New tables, async write path, App Insights wiring. |
| Telemetry dashboard | n/a | ~2 | Aggregate queries, SQL-cluster grouping, drill-down with restricted access. |
| Catalog growth (manual path) | n/a | ~3–5 | Three to eight new reports during Phase 2, ~3–4 days each. Variable depending on what telemetry surfaces. |
| Promotion workflow | n/a | ~3 (if it ships in Phase 2) | Low-code SysAdmin UI; large enough that it may slip to Phase 3. |
| **Phase 2 total — workflow in Phase 2** | 10–18 | **~19.5–29** | All v4 additions ship. |
| **Phase 2 total — workflow slips to Phase 3** | 10–18 | **~16.5–26** | Workflow defers; manual path still ships. |

### Phase 3

*Same as v3.* Saved custom queries, scheduled email digest, subscription model unification, plus the promotion workflow if it slipped from Phase 2.

**Revised effort: ~10–16 engineer-weeks** (~13–19 if the workflow slipped from Phase 2).

### Longer-term (post-Phase 3)

*Same as v3.*

---

## 11. Open questions for the human owner

*Same as v3*, with three v4 additions and one update.

### New in v4

- **OQ-V4-26.** **Telemetry storage location.** v4 places telemetry in the same analytics DB as the rest of AI Reports (DR-V4-35). Cross-check with OQ-V3-7 (saved-query storage decision — analytics DB vs. dedicated SQL DB vs. Cosmos). If saved queries end up in Cosmos, should telemetry follow? Probably no — telemetry is high-write and queryable, fits SQL well. But worth confirming the storage strategy is consistent across the new data the AI Reports service writes.
- **OQ-V4-27.** **Telemetry retention beyond 13 months.** v4 NFR-V4-09 / S-V4-SHOULD-14 allow longer retention for fully-anonymised aggregates. The exact retention policy and the anonymisation procedure need a security-team / design-council sign-off.
- **OQ-V4-28.** **Whether the promotion workflow ships in Phase 2 or Phase 3.** v4 prefers Phase 2 — the catalog-growth loop is part of what Phase 2 is *for* — but if Phase 2 capacity is tight (substrate decision per v3 OQ-V3-26 going to option b/c, cross-org work bigger than expected, etc.), the workflow can slip. The manual catalog-growth path (FR-V4-52) is the must-ship piece; the workflow (FR-V4-53) is the polish.

### Updated in v4

- **OQ-V2-17 (Subscription model unification).** Previously framed only around the Phase 3 scheduled-digest subscription model. v4 extends: the same subscription / notification infrastructure is what's used to surface "your chat question pattern is now a canned report" notifications to users, if that's a UX we want. Worth thinking about subscription/notification holistically — `ScheduledReport` extension or new table — across both Phase 3 digests and any in-product notifications.

---

## 12. Glossary

*Same as v3 §12*, plus:

- **Catalog-growth loop:** The Phase 2 cycle where chat questions reveal demand, telemetry surfaces patterns, and the team promotes those patterns to canned reports. Each canned report "absorbs" the chat questions that would otherwise repeatedly cover the same ground.
- **Manual catalog-growth path:** The dev team adds a new canned report via a normal PR — new view/proc in `wpsWatch.*`, manifest entry, role tier assignment. (FR-V4-52.)
- **Promotion workflow:** A SysAdmin-only low-code tool that lets a non-developer promote a chat pattern to a canned report from the telemetry dashboard. The polished version of the manual catalog-growth path. (FR-V4-53.)
- **Telemetry, in this doc:** Per-event records of report runs and chat interactions, written by the AI Reports service to two log tables in the analytics DB. Distinct from existing wpsWatch telemetry (App Insights, audit log) but feeds into both.
- **SQL-cluster hash:** A SHA-256 hash over the normalised form of generated SQL (variable names canonicalised, literal values stripped). Used by the telemetry dashboard to group "the same query asked 50 different ways" without doing natural-language clustering on prompt text.

---

## Appendix A — v3 → v4 requirement mapping

| v3 ID | v4 ID | Change |
|---|---|---|
| FR-V3-01 – FR-V3-44 | (unchanged) | — |
| FR-V3-48a / FR-V3-48b | (unchanged) | — |
| **(new)** US-V4-26 / 27 / 28 | — | New user stories for telemetry and catalog growth. |
| **(new)** FR-V4-49 | — | Canned-report telemetry events. |
| **(new)** FR-V4-50 | — | Chat-interaction telemetry events. |
| **(new)** FR-V4-51 | — | Telemetry dashboard. |
| **(new)** FR-V4-52 | — | Manual catalog-growth path. |
| **(new)** FR-V4-53 | — | Promotion workflow. |
| **(new)** FR-V4-54 | — | Role-tier gating for new canned reports. |
| **(new)** FR-V4-55 / 56 | — | Retention and exclusions. |
| **(new)** NFR-V4-09 | — | Telemetry write path performance. |
| **(new)** S-V4-MUST-38 / 39 | — | Prompt-text security tiering. |
| **(new)** S-V4-SHOULD-14 | — | Long-term anonymised-aggregate retention. |
| **(new)** DR-V4-35 / 36 / 37 | — | Telemetry tables, clustering signal, refresh cadence. |
| **(new)** OQ-V4-26 / 27 / 28 | — | Telemetry storage location, retention, workflow phasing. |
| OQ-V2-17 | (updated) | Subscription model unification now spans Phase 3 digests + catalog-growth notifications. |
| All other v3 requirements | (unchanged) | — |

---

*End of v4 requirements document.*
