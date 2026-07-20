# AI Reports — Stage 1 Exploration Notes (v3)

**Purpose:** Ground-truth findings across all four wpsWatch reporting-adjacent repos to support the AI Reports requirements doc. Citations are file paths within those repos.

**Supersedes:** [`ai-reports-exploration-notes-v2.md`](./ai-reports-exploration-notes-v2.md) (v2, 2026-05-09). v1 and v2 preserved unchanged for comparison.

**Date assembled:** 2026-05-14.

**What changed from v2 to v3.** v2 was the first complete-after-discovery write-up. v3 incorporates Matt Hron's review comments (from the May 8 and May 11 review of the v1 doc) that turned out to be exploration-relevant rather than purely requirements-relevant. Specifically: a dual-threshold model for offline vs warning (matt-confirmed fact not found in code), a confirmation of the v2 finding that SIM Prepaid Data is dynamically calculated, and a flag that **site-level user roles are in active development** and will affect the AI Reports permission model. The v2 surprises list carries forward; v3 adds one item.

**Repos covered (unchanged from v2):**
- `wps.watch.api` (ASP.NET Core 8 API)
- `wps.watch.web` (React)
- `wpswatch.reporting` (pure T-SQL: views, procs, UDFs, runbooks)
- `wps.watch.functions.scheduledreporting` (Azure Functions v4, .NET 8 isolated)

---

## What changed from v2

Five items, none of which contradict v2 — they tighten or confirm it.

1. **Dual offline-threshold model (Matt Hron, 2026-05-11):** 24 hours for "offline," **12 hours for "warning."** The 1440-minute threshold I found in `sp_wpsWatchDeployedCameraTrends` is the offline tier; the 12-hour warning tier was not visible in the proc text I read. Either it lives in a different proc I didn't open, or it's encoded in the Looker dashboard / reporting UI layer, or it's an operational convention not yet in code. Worth confirming where it actually lives before AI Reports relies on it. See [§14.1](#141-dual-threshold-confirmation) below.
2. **SIM Prepaid Data dynamic calculation confirmed (Matt Hron, 2026-05-11):** v2 §6 / data-v2 §8.1 found via reading `sp_wpsWatchSIMDataReport` that data-used is estimated as `PrepaidDataLimitGb - (0.0001 × photo_count_in_billing_cycle)`. Matt's three-word confirmation ("These are dynamically calculated") matches. No code change needed; v3 strengthens the citation in §6.
3. **Site-level user roles in flight (Matt Hron, 2026-05-11):** a planned future addition to wpsWatch beyond the existing org-level roles. Will affect what data users can access in reports. Currently unimplemented (not in `RoleNames.cs` or `OperationRoles.cs`); timeline not stated. **New open question** in §9. AI Reports must architect for this so DR-V2-22's `@organization_id` parameter is easy to extend to `@organization_id, @site_id_list` later.
4. **Seven SysAdmin reports are the complete in-scope set (Matt Hron, 2026-05-11):** *"Additional Sys Admin reports may be added in the future, but for now the scope can be limited to this."* Open question 1 in v2 §9 is resolved. The seven reports the prompt named (Camera Inventory, Battery Level, Photo Count, Region & Site Totals, SIM Contract Renewal, SIM Prepaid Data, Top 10 Offline) are the full SysAdmin set the AI Reports project replaces.
5. **The role-gating split (UserReport vs AdminReport) is preserved by policy, not just by inheritance (Matt Hron, 2026-05-11):** *"Not yet. Reports are promoted to general availability following a period of review by the Field Team. Let's keep the separation as it is for this scope."* In v2 I framed "should regular users get access to the SysAdmin reports?" as a design-council question. Matt clarifies it's an existing wpsWatch policy: reports are pre-promoted with a field-team review process. AI Reports inherits that process; it doesn't override it. See §2 for the implication.

---

## Surprises that change the shape of the project (carried from v2, one item added)

> ⚠️ Read first. Detail follows.
>
> 1. **The seven Looker reports the prompt listed (Camera Inventory, Battery Level, Photo Count, Region & Site Totals, SIM Contract Renewal, SIM Prepaid Data, Top 10 Offline) are *all* gated by the `AdminReport` operation — i.e. they are the System Admin set.** Regular users (`UserReport` operation) see only **three** iframe-embedded reports: Camera Battery Level, Camera Deployment Detail, Photo Count By Camera. ([`Header.tsx:582–730` vs `731–816`](#-5-reports-menu--frontend-wiring-the-seam-being-replaced))
> 2. **JWT validation uses a symmetric `JwtKey` (HS256), not Azure AD/Entra (RS256).** A separate service can only validate tokens by sharing the same secret key, or by routing through the API. ([`Startup.cs:242–261`](#-3-auth-surface-for-an-external-service))
> 3. **Reports today is single-org by design.** Both Reports menu items are disabled with a tooltip in multi-org mode. The new feature inherits this constraint for MVP per the human owner's call. ([`cross-org-overview.md:14–25`](#-5-reports-cross-org-status))
> 4. **The Jira ticket's `wpswatchprodreplica` target appears not to exist in production.** No code in any of the four repos references it. The team's actual reporting story is a **separate analytics database** `wps-sql-analytics-prod` on the same SQL Server, fed by cross-DB external tables. (§11.)
> 5. **An extensive `wpsWatch.*` reporting schema already exists** in `wps-sql-analytics-prod` — 15 views, 7 stored procs, 9 UDFs, 10 aggregation tables. It backs the Looker dashboards today. (§11.)
> 6. **Scheduled deployment-status emails are already in production.** `wps.watch.functions.scheduledreporting` is an Azure Functions v4 (.NET 8 isolated) app that sends weekly HTML emails via SendGrid (US) / Mailjet (EU) / Brevo (EU alt), pulling data via `Wps-Api-Key` from the wpsWatch API's `DeploymentReportController`. Subscriptions are site-level via the `ScheduledReport` table. (§12.)
> 7. **Looker's row-level user-org filtering is fed by a SQL → BigQuery sync.** `OrganizationUserRole (OLTP)` → `sp_wpsWatchUserOrgUpdate` → `wpsWatchLookerUserOrg (analytics DB)` → `NewUserOrgSyncFunction` (in the Functions app) → BigQuery `wpsWatchUserOrgId.wpsUserOrg` → Looker.
> 8. **Volunteers are excluded from Reports at the API layer (`UserReport` role list omits them) and at the UI layer (no Reports menu render path).** Confirmed. ([`OperationRoles.cs:446–463`](#-2-roles-and-report-visibility))
> 9. **Four runbook PowerShell scripts in `wpswatch.reporting/Jobs/` contain a plain-text SQL password.** Mixed-casing on the user (`wpswatchLooker` / `wpswatchlooker`). Separate ticket. (§13.)
> 10. **(v3 new) Offline status is reported in two tiers: 24h "offline" and 12h "warning."** The 24h tier is encoded in `sp_wpsWatchDeployedCameraTrends`; the 12h warning tier is dev-team-confirmed but its source-of-truth in code is not yet located. AI Reports' Top-N-offline canned report must surface both tiers, not just the binary online/offline view a literal reading of v2 implied. (§14.1.)

---

## 1. Permission and org-scoping model

*Unchanged from v2.* `IUserContextService`, the `OrganizationUserRole` table as the source of truth, the `X-MultiOrgMode` header read at `UserAuthorizationFilter.cs:44`.

**(v3 addition)** Per Matt's comment on v2 §2 cross-org reporting: **site-level roles are in active development** in wpsWatch as a planned extension of the existing org-level roles model documented in `cross-org-roles.md`. No code visible in the current `wps.watch.api` repo (`RoleNames.cs` has only `SystemAdmin`, `Viewer`, `GpsViewer`, `OrgAdmin`, `PhotoUploader`, `PhotoTagger`, `IncidentManager`, `Volunteer` — all org-tier-or-global, none site-tier). The implication for AI Reports: the `@organization_id` parameter pattern (DR-V2-22 in the requirements doc) needs to be designed so it extends to `@site_id_list` without a rewrite. Timeline TBD — open question §9 OQ-V3-21.

---

## 2. Roles and report visibility

*Unchanged from v2* for the role-name and operation-mapping facts.

**(v3 addition — policy clarification from Matt Hron):** the existing UserReport/AdminReport split is not an accident of history — it reflects an active wpsWatch policy. Reports are pre-promoted from `AdminReport` to `UserReport` after a **Field Team review period**. AI Reports inherits this policy: the MVP gates exactly the same reports `AdminReport` gates today, and the four currently-SysAdmin reports get promoted to `UserReport` later, via the same Field Team process, **outside the AI Reports project**. (Matt: *"Not yet. Reports are promoted to general availability following a period of review by the Field Team. Let's keep the separation as it is for this scope."*)

This means a v2 framing — "design council should decide whether to open the four reports to regular users" — was wrong. The decision authority is the Field Team, the process exists, and it runs separately from AI Reports.

---

## 3. Auth surface for an external service

*Unchanged from v2.* HS256 + shared `JwtKey`; the alternatives in §3 (share, introspect, migrate to RS256) and the data-architect's deeper analysis remain.

---

## 4. Code-sharing potential between the API and a new .NET service

*Unchanged from v2.*

---

## 5. Reports menu — frontend wiring (the seam being replaced)

*Unchanged from v2.*

---

## 6. Schema surface for the seven reports (primary OLTP)

*Unchanged from v2.*

**(v3 confirmation, Matt Hron):** the SIM Prepaid Data report's heuristic estimation (`PrepaidDataLimitGb - 0.0001 × photo_count_in_billing_cycle`) is confirmed as the production approach (*"These are dynamically calculated"*). v2 already reflected this via reading `sp_wpsWatchSIMDataReport`; v3 simply strengthens the citation.

---

## 7. Stored procedures and views (PRIMARY OLTP database only)

*Unchanged from v2.*

---

## 8. Replica realities

*Unchanged from v2.* The ticket's `wpswatchprodreplica` is not in use.

---

## 9. Open questions surfaced during exploration (v3 set)

Carried forward from v2 with status updates.

| ID | Question | Status |
|---|---|---|
| OQ-V2-1 | Are there additional SysAdmin reports beyond the seven? | **Resolved (v3, Matt Hron):** No — the seven are the full in-scope SysAdmin set. Additional ones are future work. |
| OQ-V2-2 | Should regular users get access to the four currently-SysAdmin reports? | **Resolved (v3, Matt Hron):** Not in this project. Reports are pre-promoted by the Field Team via a separate process. Inherit existing gating. |
| OQ-V2-3 | SIM Prepaid Data source of truth | **Resolved (v3, Matt Hron):** Dynamically calculated as estimation; matches v2's read of `sp_wpsWatchSIMDataReport`. |
| OQ-V2-4 | "Top 10 Offline" threshold | **Partly resolved (v3, Matt Hron):** Dual threshold — 24h offline, 12h warning. See §14.1 for the source-of-truth gap. |
| OQ-V2-5 | Mirror DB beyond the replica | **Resolved (v2):** No mirror; extend the analytics DB. |
| OQ-V2-6 | JWT migration to RS256/Entra | Open. |
| OQ-V2-7 | Saved-queries primary store: dedicated SQL DB vs. analytics DB | **Reopened (v3, Matt Hron):** Cosmos is also a viable option, given wpsWatch's existing Cosmos usage. Three-way choice now. |
| OQ-V2-8 | Cross-org reporting timing for Reports | **Partly resolved (v3, Matt Hron):** Cross-org Reports is **dependent on this project landing** — AI Reports is the wedge. Timing remains flexible. |
| OQ-V2-9 | Scheduled email digest cadence and deliverability | **Resolved (v3, Matt Hron):** Phase 3, after Phase 2's ad-hoc chat. |
| OQ-V2-10 | Anthropic vs. other model provider | Open. |
| OQ-V2-11 | Looker SQL extraction | Open. Partially mitigated by reading `wpswatch.reporting` schema; specific Looker data-source URLs still useful. |
| OQ-V2-12 | iframe-based Reports cutover plan | **Resolved (v3, Matt Hron):** Side-by-side with existing menu during pilot; **SysAdmin migrates first**, then regular users. |
| OQ-V2-13 | AI Reports repo layout | Open. Recommendation: new repo as sibling to existing two. |
| OQ-V2-14 | Runbook refresh cadence | Open. Needed for the freshness gate threshold. |
| OQ-V2-15 | Which `vw_wpsWatchRegionSiteTotals` version is in Looker (V1/V2/V3) | Open. Becomes more important now that Region & Site Totals is the proposed tracer-bullet target (see requirements v3 §10). |
| OQ-V2-16 | BigQuery sync deprecation | Open. |
| OQ-V2-17 | Subscription model unification | Open. |
| OQ-V2-18 | `DeploymentReportController` and AI Reports relationship | Open / informational. AI Reports does not call it. |
| OQ-V2-19 | Runbook plain-text credentials | Open — separate ticket. |
| OQ-V2-20 | Analytics-DB deployment automation | Open. |
| **OQ-V3-21** (new) | **Site-level user roles timeline** | New. Matt Hron flagged this in v1 review. If site-level roles ship during AI Reports' MVP window, the `@organization_id` scope mechanism in DR-V2-22 must be extended to `@site_id_list`. Needs a timeline from the wpsWatch core team. |
| **OQ-V3-22** (new) | **Where is the 12h warning threshold encoded?** | The 24h offline threshold is in `sp_wpsWatchDeployedCameraTrends` (line 66 / 77 / 188–189 / 224, 1440 minutes). The 12h warning tier Matt mentions is not visible in that proc's read. Needs locating before AI Reports' Top-N-offline implementation, or it must be re-derived in `sp_ai_TopOfflineDeployments` from explicit threshold parameters. |
| **OQ-V3-23** (new) | **Field Team review process** | The mechanism by which `AdminReport` → `UserReport` promotion happens. What does this process look like? Is there documentation? Does AI Reports need to integrate with it (e.g. surface a "this report is in field-team review" UI affordance), or is it entirely an out-of-band human process? |

---

## 10. File-citation index

*Unchanged from v2.* See [`ai-reports-exploration-notes-v2.md` §10](./ai-reports-exploration-notes-v2.md#10-file-citation-index-quick-reference).

---

## 11. The analytics-DB architecture

*Unchanged from v2.*

---

## 12. The scheduled-reporting Functions app

*Unchanged from v2.*

---

## 13. Findings tangential to AI Reports

*Unchanged from v2.* The runbook credential issue, the three versions of `vw_wpsWatchRegionSiteTotals`, the unused `ScheduledReport` int columns.

---

## 14. New findings from dev-team review (v3)

This section captures facts Matt Hron confirmed or surfaced in his comments on the v1 doc that are exploration-shaped rather than requirements-shaped.

### 14.1 Dual-threshold confirmation

Two operationally-distinct states beyond simply "online":

- **24 hours** since last event → **offline** (camera is reported as failed).
- **12 hours** since last event → **warning** (camera is at risk).

The 24h tier is encoded explicitly in `wpswatch.reporting/StoredProcedures/wpsWatch.sp_wpsWatchDeployedCameraTrends.StoredProcedure.sql` at the lines I read (1440 minutes used in three filter clauses). The 12h warning tier was not visible in that proc; possibilities:

- It exists in a different stored procedure or UDF I didn't open. The `tvf_wpsWatchRegionSiteTotalsV3` UDF is the most likely candidate given it computes per-org online/offline counts.
- It exists in a Looker view's WHERE clause or computed dimension, not in SQL.
- It's an operational/UI convention, not encoded in any layer yet, and is applied at the dashboard rendering side.

**For the AI Reports requirements doc, this means:** the canned "Top 10 Offline" / "Region & Site Totals" replacements must surface **three states** (online / warning / offline), parameterised by both thresholds. The data perspective's `sp_ai_TopOfflineDeployments(@OrganizationId, @ThresholdHours)` should become `sp_ai_DeploymentHealthStatus(@OrganizationId, @OfflineHours = 24, @WarningHours = 12)` returning a multi-tier classification.

### 14.2 Site-level user roles in development

A wpsWatch core-team workstream not visible in the current `wps.watch.api` repo. Adds a tier below organization-level roles (referenced in `cross-org-roles.md` as the existing tier model: global → org-level → planned site-level).

Implications I can confidently state:
- AI Reports' scoping parameter today is `@organization_id`. When site-level roles ship, scope becomes `(@organization_id, @site_id_list)` or `(@user_site_access_list)`. The data-layer requirement DR-V2-22 should be designed extensible.
- The bootstrap of "which orgs does this user have access to" via `wpsWatchLookerUserOrg` may need to grow into `wpsWatchLookerUserSites` (analogous cache table) or be replaced.

Implications I can't confidently state without more info:
- Whether site-level roles ship before, during, or after the AI Reports MVP window.
- Whether the cross-org reporting that v2 deferred to Phase 2+ is itself dependent on site-level roles landing (i.e., is "cross-org" actually "cross-site within an org" in some cases?).

This goes in the requirements doc as a hard open question with implications for the architecture if it lands on the same timeline. See OQ-V3-21 in §9.

### 14.3 The Field Team review process

The path by which `AdminReport`-only reports become `UserReport`-accessible. Matt's comment surfaces it as an existing wpsWatch policy. The mechanism is not documented in the four repos I've read. Plausibly it's a Confluence page, a recurring meeting, an issue-tracker workflow, or a tribal convention.

For AI Reports' purposes the practical implications are:
- **MVP gating mirrors today's exactly.** No reports get re-tiered as part of this project.
- A future Field Team decision to promote a report becomes a content-only change in the AI Reports config (move the report's manifest entry from the `AdminReport` allowlist to the `UserReport` allowlist).
- The AI Reports UI may benefit from a "this report is under field-team review" affordance if such a review state exists today; or it may not.

Open question OQ-V3-23.

---

*End of v3 exploration notes.*
