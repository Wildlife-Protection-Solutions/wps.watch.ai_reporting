# AI Reports — Requirements Document (v6)

**Status:** Draft — for wpsWatch dev team review.
**Supersedes:** [`ai-reports-requirements-v5.md`](./ai-reports-requirements-v5.md) (v5, 2026-05-14). v1–v5 preserved unchanged for comparison.
**Authors:** Synthesis of Stage 1 codebase exploration ([v3 notes](./ai-reports-exploration-notes-v3.md)) + four persona reviews + Matt Hron's v1 comments + the v4 telemetry/catalog-growth feedback + the v5 replica decision + the discovery-first pivot and the rung-based rollout ladder.
**Date:** 2026-06-17.

> **How to read this document.** Sections 1–10 are the requirements proper; §10 (rollout) is where v6's main change lives. §11 lists open questions; §12 is a glossary. File-path citations point to `wps.watch.api`, `wps.watch.web`, `wpswatch.reporting`, `wps.watch.functions.scheduledreporting`, or the new `wps.watch.ai_reporting` service repo.

---

## What changed from v5 to v6

v6 introduces **one organizing idea** and re-frames the rollout around it: **the project advances along two independent axes, and they should not be conflated.**

- **Capability phases** — *what the product can do.* Phase 1 (canned reports), Phase 2 (free-form chat, telemetry, catalog growth, cross-org), Phase 3 (saved queries, scheduled digest). Unchanged from v4/v5.
- **Exposure rungs** — *who can use it, against what data, at what security bar.* **Rung 1** (internal team, devqa/test data), **Rung 2** (internal team, prod replica/real data), **Rung 3** (external partner-reserve users). New in v6.

The old plan was implicitly single-axis: it sequenced *phases* and assumed each shipped at production exposure. That conflated "later capability" with "more dangerous" — which is why free-form chat (a Phase 2 capability) was deferred. The **discovery-first pivot** (Eric's feedback, mid-2026) breaks that conflation: free-form "ask the data" is safe to deliver *now* if it sits at a low *rung* (internal team, test data), even though it's a "later phase" capability. The security perspective's concern about free-form SQL is satisfied by **keeping it at a low rung until the higher-rung guardrails are built** — not by deferring the capability to a later phase.

So v6's rollout (§10) is a **rung × phase grid**: rungs are the primary timeline (the trust ladder we climb), phases are the capability menu, and capabilities are delivered against the rung where they're safe. A crucial consequence: what gets formalized as polished canned reports / dashboards for external users (Rung 3) is *informed by* what internal discovery (Rung 1–2) reveals — we don't pre-guess dashboards, we distill them from real questions.

This also matches where the code already is: the `wps.watch.ai_reporting` repo has Phase 1 canned reports **and** the Phase 2 free-form discovery surface (`query_data` / `describe_schema`), both running at Rung 1 (dev-shim auth, devqa, GPS allowed because devqa is synthetic/stale).

Numbering: rung-affected requirements get a `-V6-` infix; capability requirements unchanged from v5 carry their prior IDs.

---

## 1. Background and motivation

*Same as v5 §1.*

---

## 2. Goals and non-goals

### The two axes (new framing)

**Capability phases (what):**
- **Phase 1** — Replace the canned Looker reports (with the four UX upgrades). The known-value baseline.
- **Phase 2** — Free-form "ask the data" chat; file export; usage telemetry; catalog growth; cross-org reporting.
- **Phase 3** — Saved custom queries (relative dates); scheduled email digest; subscriptions.

**Exposure rungs (who / what data / what bar):**
- **Rung 1 — internal team, devqa.** Trusted audience, test data (synthetic + stale). Lowest security bar: the dev-auth shim is acceptable, GPS may flow (devqa coordinates aren't real), no read-only DB principal required. The learning environment.
- **Rung 2 — internal team, prod replica.** Same trusted audience, but **real** data. Bar jumps: GPS stripped from the LLM surface, read-only `ai_reports_reader` principal + curated views, real JWT (once the audience is more than a handful of named admins), replica-lag gate, full audit retention.
- **Rung 3 — external users (partner reserves).** Distant, after extensive internal evaluation. Full guardrail set: per-org row scoping enforced for real (not sysadmin-shimmed), AST validation for any free-form, the complete security MUST list, donor/legal sign-off on anything sensitive (e.g. GPS).

### Goals
The capability goals are **unchanged from v5** (Phase 1 / 2 / 3 deliverables). v6 adds the rollout goal:

- **Climb the rungs in order, gated by evidence and guardrails — not by calendar.** Rung 1 → 2 is gated by "discovery proved useful" + "real-data security hardening done." Rung 2 → 3 is gated by "extensive internal evaluation" + "full external guardrail set" + "legal/donor sign-off where applicable."
- **Let lower-rung discovery inform higher-rung formalization.** The canned reports / dashboards that ship to external users (Rung 3) are the distillation of what internal discovery (Rung 1–2) showed to be valuable — evidence-based, not pre-guessed.

### Non-goals
*Same as v5*, plus:
- **No external exposure (Rung 3) until the full guardrail set is in place and internal evaluation justifies it.** This is now an explicit gate, not an assumption.
- **No free-form SQL against real data at a low security bar.** Free-form is fine at Rung 1 (test data); at Rung 2+ it carries the read-only-principal / curated-view / GPS-strip requirements.

---

## 3. User stories

*Same as v5* (US-01 … US-V4-28; US-04 removed per v3). v6 adds no new stories; it re-sequences *when/where* they're served via the rung ladder (§10).

---

## 4. Functional requirements

### 4.1 – 4.9
*Same as v5* (service surface, auth, canned reports, chat, export, saved queries, behavioural, connectivity, telemetry/catalog-growth). The capability set is unchanged.

### 4.10 Usage telemetry and catalog growth
*Same as v5 §4.9 / v4 §4.10.* (Delivered at Rung 2 in earnest — see §10 — but the audit-log-as-discovery-capture starts at Rung 1.)

### 4.11 Free-form discovery surface — *new in v6 (built, Rung 1)*

These reflect what now exists in `wps.watch.ai_reporting`. They are the Phase 2 free-form capability delivered at Rung 1.

- **FR-V6-57.** A `describe_schema` tool (MCP) / `GET /api/discovery/schema` (REST) returns the queryable tables and columns from the database mirror, excluding identity/secret tables and sensitive columns (tokens, password hashes, API keys, webhook URLs). Source: `Discovery/DiscoveryQueryRunner.DescribeSchemaAsync`.
- **FR-V6-58.** A `query_data` tool (MCP) / `POST /api/discovery/query` (REST) runs a single read-only `SELECT` (or `WITH … SELECT`) and returns the rows. Guarantees: SELECT-only + single-statement validation; sensitive columns stripped from output; row cap (default 5,000); statement timeout (default 15s); execution inside an always-rolled-back transaction (write-prevention defense-in-depth); every query audit-logged with the generated SQL and row count. Source: `Discovery/SqlGuard`, `Discovery/DiscoveryQueryRunner.RunQueryAsync`.
- **FR-V6-59.** Query results are downloadable as CSV (`?format=csv`) with a UTF-8 BOM and CSV-formula-injection escaping — the "download for verification" affordance. Source: `Discovery/CsvExporter`.
- **FR-V6-60.** The discovery surface is gated to a `DiscoveryQuery` authorization operation, system-admin-only at Rung 1 (matches "internal team"; the dev-auth shim is sysadmin). Source: `Authorization/ReportOperations.DiscoveryQuery`.
- **FR-V6-61.** The discovery surface has a master `Discovery:Enabled` switch (config) so it can be turned off per environment. Source: `Discovery/DiscoveryOptions`.

> **Rung note.** FR-V6-57–61 ship at Rung 1 as built. At Rung 2 they inherit the rung-2 security additions in §6 (GPS strip, read-only principal, real JWT). At Rung 3, free-form discovery for external users additionally requires AST validation and per-org row scoping, or is withheld entirely from external users while only canned/saved reports are exposed (decision deferred — OQ-V6-32).

---

## 5. Non-functional requirements

*Same as v5*, with the freshness/availability bar now understood as **per-rung**: Rung 1 has no replica-lag concern (devqa isn't a replica); Rung 2+ adds the replica-lag gate (NFR-V5-04) and the Azure SQL tier requirement (NFR-V5-10).

---

## 6. Security and privacy requirements — *re-tiered by rung in v6*

v5 carried a flat MUST/SHOULD/NICE list. v6 keeps every requirement but organizes them by **the rung at which each becomes binding.** The principle: a control binds at the rung where the risk it addresses becomes real. Nothing is dropped; the discovery pivot does not lower the production bar — it only recognizes that test-data-internal-use doesn't need the production bar *yet*.

### Rung 1 — internal team, devqa (binding now)

The bar is deliberately light because the audience is trusted and the data is synthetic/stale.

- **S-R1-01.** The dev-auth shim is permitted **only** in Development and **only** at Rung 1; the fail-closed guardrail (`AssertDevAuthSafeForEnvironment`) refuses to start with the shim enabled outside Development. (Already implemented.)
- **S-R1-02.** Free-form queries are SELECT-only / single-statement (SqlGuard), row-capped, timeout-bounded, and run in a rolled-back transaction. (FR-V6-58.)
- **S-R1-03.** Credential/secret columns (tokens, password hashes, API keys, webhook URLs) are stripped from all output and never advertised by `describe_schema`, **even on test data** — stale tokens may still be valid. (Already implemented.)
- **S-R1-04.** Every report run and discovery query is audit-logged (user, generated SQL, row count). This log is also the discovery-capture record.
- **S-R1-05.** GPS may flow at Rung 1 **only because devqa is synthetic/stale** (confirmed with the team). This is the one control deliberately relaxed at Rung 1; it re-binds at Rung 2 (S-R2-04).

### Rung 2 — internal team, prod replica (binding before any real data is queried)

Real data ⇒ the full v5 §6 production controls, minus only those specific to external multi-tenant exposure.

- **S-R2-01.** Connection targets `wpswatchprodreplica` with `ApplicationIntent=ReadOnly` (v5 S-V5-MUST-02).
- **S-R2-02.** Dedicated read-only `ai_reports_reader` principal: `SELECT` on curated `dbo.vw_ai_*` views only; `DENY` on base tables and system catalogs (v5 S-V5-MUST-03).
- **S-R2-03.** Sensitive credential/PII columns absent from every `vw_ai_*` view; CI check enforces non-projection (v5 S-V5-MUST-28, S-V5-MUST-40).
- **S-R2-04.** **GPS is stripped from the LLM/discovery surface.** `Latitude`/`Longitude` move into `SqlGuard.SensitiveColumnFragments` and are excluded from `vw_ai_*` views. (The one-line code change is already flagged in `SqlGuard.cs` comments.) Per the coordinate-handling doc.
- **S-R2-05.** Replica-lag freshness gate (v5 FR-V5-10 / NFR-V5-04).
- **S-R2-06.** Real JWT validation replaces the dev shim once the audience exceeds a handful of named sysadmins (HS256 shared key per the auth decision; the startup guardrail already prevents shipping the shim).
- **S-R2-07.** Audit store separation + 13-month retention; `chat_interaction_log.prompt_text` at the restricted audit tier (v5 S-V4-MUST-38/39).
- **S-R2-08.** AST validation of any free-form SQL (upgrade from SqlGuard's pragmatic checks) once free-form runs against real data (v5 S-V5-MUST-17 / S-V5-MUST-41).

### Rung 3 — external users (binding before any external exposure)

Adds the multi-tenant / untrusted-audience controls.

- **S-R3-01.** Per-org row scoping enforced for real — not mooted by a sysadmin shim. Every query carries the user's authorized org list; "no orgs = see nothing." (v5 S-MUST-08–13.)
- **S-R3-02.** Volunteer / PhotoUploader / PhotoTagger exclusion enforced at the service, tested (v5 S-MUST-06/07/30/31).
- **S-R3-03.** Prompt-injection defenses for DB-resident user content quoted back to the LLM; output rendered as sanitized markdown only (v5 S-MUST-22/24/25).
- **S-R3-04.** Rate limiting, idle-timeout, failed-auth throttling (v5 S-MUST-26, S-SHOULD-10/13).
- **S-R3-05.** Legal/donor sign-off on any sensitive-data exposure (notably GPS, if ever surfaced to external users) before it ships. Per the coordinate-handling doc OQ.
- **S-R3-06.** Saved-query revalidation, schema-pinning, running-user-scope-on-share (v5 S-MUST-34/35/36).

### Carried forward / unchanged
All v5 SHOULD and NICE-TO-HAVE items remain, applicable at the rung where their corresponding MUST binds. The full enumerated list is in [`perspectives/security.md` §10](./perspectives/security.md) and v5 §6.

---

## 7. Permission model — *per-rung in v6*

- **Rung 1:** dev-auth shim treats the caller as system admin; `DiscoveryQuery` / `AdminReport` / `UserReport` all resolve. Acceptable because internal + test data. Org-scoping logic exists in code but is mooted by the sysadmin shim.
- **Rung 2:** real JWT (once audience > a few admins); user's org list read from `OrganizationUserRole` on the replica; role gating real. Discovery stays `DiscoveryQuery` = system-admin-only (internal team).
- **Rung 3:** full per-org enforcement for external users across all roles; the existing tier gating (`AdminReport` / `UserReport`, Volunteers excluded) applies; Field Team review process governs report re-tiering (per v3 §7).

Otherwise the role model is **same as v5 §7** (mirror today's wpsWatch gating; cross-org is the first cross-org reporting surface; site-level roles forward-compatibility per OQ-V3-21).

---

## 8. Architecture options considered

*Same as v5 §8* (Hybrid REST + MCP; hybrid catalogue + gated text-to-SQL; shared HS256 JWT; Anthropic provider per the resolved OQ-V2-10; Azure SQL geo-replica per the resolved OQ-V3-26). v6 adds that the **discovery surface is built on the same handler core** — `DiscoveryQueryRunner` parallels `ReportRunner`, sharing auth, audit, and data context, so climbing rungs is additive (swap the shim for JWT, add GPS to the strip list, point at the replica) rather than a rewrite.

---

## 9. Data layer requirements

*Same as v5 §9* (DR-V5-01 … DR-V5-23). v6 adds the per-rung data target:
- **Rung 1:** `wpswatch-dev` (devqa), queried directly via narrow EF entities + raw SQL; no curated views required yet.
- **Rung 2:** `wpswatchprodreplica`; curated `dbo.vw_ai_*` views in the OLTP primary (replicated); read-only principal.
- **Rung 3:** same data substrate as Rung 2; the difference is enforcement (per-org scoping real) not storage.

---

## 10. Rollout — rungs × phases (v6 rewrite of §10)

The rollout is a climb up the exposure rungs. Within each rung we deliver whatever capability phases are safe and valuable at that exposure. The grid:

| | **Phase 1** — Canned reports (+UX) | **Phase 2** — Free-form chat · telemetry · catalog growth · cross-org | **Phase 3** — Saved queries · digest |
|---|---|---|---|
| **Rung 1**<br>internal · devqa | ✅ Built — 10 canned reports incl. Region & Site Totals tracer | ✅ Built — free-form `query_data`/`describe_schema` + CSV download; audit-as-discovery-capture | — (saved queries optional, internal convenience only) |
| **Rung 2**<br>internal · prod replica | Validate canned reports on real data; curated `vw_ai_*` views | Telemetry + catalog-growth on real usage; cross-org; **GPS stripped**, read-only principal, real JWT, AST validation | Saved queries + scheduled digest for the internal team |
| **Rung 3**<br>external users | GA the *distilled* canned reports / dashboards (informed by Rung 1–2 discovery) | Chat to external **only if** proven safe; full multi-tenant guardrails | Saved queries + digest GA to external users |

### Sequencing (the discovery-first consequence)

1. **Rung 1 (now → near future).** *Where the repo is.* Build the MCP foundation + canned reports (Phase 1) **and** free-form discovery (Phase 2 core) together — both cheap and safe internally on test data. **Drive a lot of chat-with-data scenarios; the audit log captures the questions.** This is the learning engine. Deliverable bar is low (dev shim, devqa) so iteration is fast.

2. **Rung 2 (after Rung 1 proves useful).** The security lift: harden for real data (read-only principal, curated views, GPS strip, real JWT, replica-lag gate, audit retention). Same trusted internal audience. Now telemetry and catalog-growth become meaningful (real usage), cross-org becomes real, and saved queries / digest land for the internal team. **What discovery surfaced at Rung 1 tells us which views and reports to formalize here.**

3. **Rung 3 (distant — after extensive internal evaluation).** Open to external partner-reserve users behind the full guardrail set. By now the canned reports / dashboards are evidence-based (distilled from Rung 1–2 discovery), not guessed. Free-form chat to external users is a separate go/no-go (OQ-V6-32); the safe floor for Rung 3 is canned + saved reports with per-org scoping, with chat added only if it clears the bar.

### Transition gates (evidence + guardrails, not calendar)

- **Rung 1 → 2:** discovery has demonstrably produced value (the team can point to questions/insights worth formalizing) **and** the Rung 2 security set (§6) is implemented and tested.
- **Rung 2 → 3:** extensive internal evaluation of usefulness and safety **and** the Rung 3 security set is implemented **and** legal/donor sign-off where sensitive data is involved.

### Effort

Capability effort estimates are **unchanged from v5** (Phase 1 ~11.5–14 wk; Phase 2 ~10–18 wk + telemetry/catalog-growth; Phase 3 ~10–16 wk). v6 reframes *when* they land by rung rather than changing the totals. The Rung 1 work (Phase 1 canned + Phase 2 discovery on test data) is largely **already done** in `wps.watch.ai_reporting`; the next major cost is the Rung 2 security hardening, which front-loads the security MUSTs that v5 already enumerated.

### Longer-term
*Same as v5* (GPS exposure with role-gated coarsening — a Rung 3 capability gated by S-R3-05; composite camera-health report; the broader Mission Control roadmap).

---

## 11. Open questions for the human owner

*Carries forward v5 §11 open items.* Resolved since v5: OQ-V2-10 (Anthropic), OQ-V3-26 (replica substrate). New in v6:

- **OQ-V6-32.** **Free-form chat for external users (Rung 3): in or out?** The safe Rung 3 floor is canned + saved reports with per-org scoping. Exposing free-form `query_data` to external partner-reserve users is a much higher bar (untrusted authors of SQL-shaped intent against real data). Decision can wait until Rung 2 evidence is in, but flag now so the architecture keeps it optional.
- **OQ-V6-33.** **What is the "discovery proved useful" bar for Rung 1 → 2?** Concretely: how many/what kind of discovered questions or insights justify the Rung 2 security investment? Worth defining a lightweight success signal before Rung 1 exploration starts in earnest, so the transition decision is evidence-based.
- **OQ-V6-34.** **Audience size at Rung 2 — does it cross the "real JWT now" threshold?** If Rung 2 stays a handful of named sysadmins, the dev-shim-with-real-data window might be tolerable briefly; if it's broader internal staff, real JWT is required before any real data. Confirm the intended Rung 2 audience size.
- Plus all still-open v5 items (OQ-V2-6 RS256/Entra, OQ-V3-7/V5-7 writable substrate, OQ-V3-21 site-level roles, OQ-V3-22 12h warning threshold source, OQ-V3-23/24 Field Team process, OQ-V4-27 telemetry retention, OQ-V4-28 promotion-workflow phasing, OQ-V5-29 pre-aggregation, OQ-V5-30 Azure SQL tier, OQ-V5-31 replica-write alerting).

---

## 12. Glossary

*Same as v5 §12*, plus:

- **Capability phase:** what the product can do, independent of who uses it. Phase 1 = canned reports; Phase 2 = free-form chat + telemetry + catalog growth + cross-org; Phase 3 = saved queries + scheduled digest.
- **Exposure rung:** who can use the product, against what data, at what security bar. Rung 1 = internal team on devqa (test data); Rung 2 = internal team on the prod replica (real data); Rung 3 = external partner-reserve users. Rungs are the rollout timeline; the security bar escalates per rung.
- **Discovery-first:** the pivot of delivering the free-form "ask the data" capability (a Phase 2 capability) early but at Rung 1 (internal/test data), so real usage informs which canned reports and dashboards are worth formalizing for higher rungs — rather than pre-guessing dashboards.

---

## Appendix A — v5 → v6 mapping

| Area | v5 | v6 |
|---|---|---|
| Rollout model | Linear phases (1→2→3), implicitly at production exposure | **Rung × phase grid**; rungs are the timeline, phases the capability menu (§10) |
| Free-form chat timing | Phase 2 (deferred for safety) | Phase 2 capability delivered at **Rung 1** (internal/test data); safety handled by rung, not by deferral |
| Security (§6) | Flat MUST/SHOULD/NICE list | **Re-tiered by rung** (Rung 1 light / Rung 2 real-data / Rung 3 external); nothing dropped |
| Permission model (§7) | Single production model | **Per-rung** (shim → real JWT → full per-org external) |
| Discovery surface | Not yet specified | **FR-V6-57–61** (built: `query_data`, `describe_schema`, CSV, gating, switch) |
| GPS handling | Excluded from MVP | **Allowed at Rung 1** (synthetic devqa); **stripped at Rung 2+** (one-line flagged change) |
| Open questions | v5 set | + OQ-V6-32 (external free-form), OQ-V6-33 (Rung 1→2 bar), OQ-V6-34 (Rung 2 audience size) |
| All capability FR/NFR/DR | v5 | Unchanged; only re-sequenced by rung |

---

*End of v6 requirements document.*
