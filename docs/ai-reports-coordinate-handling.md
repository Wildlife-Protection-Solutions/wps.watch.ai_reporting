# AI Reports — Coordinate Handling & MCP Design Implications

**Purpose:** document how GPS coordinates are stored and access-controlled in the existing wpsWatch system, and lay out the design choices that gives the AI Reports MCP server. Coordinate exposure has special weight here because wpsWatch protects endangered species — leaked rhino locations can get animals killed.

**Date:** 2026-05-14.

**Sources:** all citations are from the local clones of `wps.watch.api` and `wps.watch.web`. Verified by reading the actual code, not just the requirements docs.

---

## 1. How coordinates are stored today

GPS coordinates live in **two primary OLTP tables**: `Site` and `Deployment`. Both store `Latitude` and `Longitude` as nullable decimals.

- `Wps.Watch.Data/Models/Site.cs:23–24`:
  ```csharp
  public decimal? Latitude { get; set; }
  public decimal? Longitude { get; set; }
  ```
- `Wps.Watch.Data/Models/Deployment.cs` — same shape (verified via the external-table mirror that ships with the analytics DB, which projects them as `decimal(12, 9)` — see `wpswatch.reporting/Tables/ExternalTables/dbo.Site.Table.sql`).

`decimal(12, 9)` means up to 12 total digits with 9 places after the decimal — about 0.1 millimetre theoretical precision. That's far above what GPS hardware actually produces (consumer hardware tops out around 5–7 decimal places, ~1 metre precision), so the storage type is effectively "full precision, whatever the source produced."

**Other places coordinates appear:**
- `Incident.InitialLatitude` / `InitialLongitude` — copied from the deployment that started the incident (`Wps.Watch.Api/Adapters/IncidentAdapter.cs:127–128`).
- `Photo` rows don't store coordinates directly. When a `Photo` is shown with a location, the location comes from `Photo.Deployment.Latitude/Longitude` (the deployment the photo belongs to — see `PhotoAdapter.cs:60–61`).
- `Site` and `Deployment` are the canonical sources. Everything else derives from them.

There is **no separate "coarsened lat/lon" column** in the schema. Coordinates are stored at full precision or not at all (`null`).

---

## 2. How coordinates are access-controlled today

The wpsWatch authorization model has a per-entity operation called `ViewLocation`. It's separate from the entity's `View` and `Search` operations — meaning a user can be allowed to see a site or a deployment without being allowed to see *where* it is.

### The operations involved

From `wps.watch.api/Wps.Watch.Api/Authorization/OperationRoles.cs`:

| Operation | Roles granted |
|---|---|
| `PhotoOperations.Names.ViewLocation` | `OrgAdmin`, `IncidentManager`, `GpsViewer` (line 69) |
| `SiteOperations.Names.ViewLocation` | `GpsViewer`, `IncidentManager`, `OrgAdmin` (line 165) |
| `DeploymentOperations.Names.ViewLocation` | `GpsViewer`, `OrgAdmin`, `IncidentManager` (line 310) |
| `MapOperations.Names.View` | `GpsViewer`, `IncidentManager`, `OrgAdmin` (line 284) |

`SystemAdmin` gets all of these via the global-bypass in `IUserContextService.Can<TEntity>()`.

### Roles *denied* coordinate access today

- **`Viewer`** — has `View` and `Search` on most entities but is *not* in any `ViewLocation` list. Sees the site exists, can't see where it is.
- **`PhotoUploader`** and **`PhotoTagger`** — limited scopes; not in any `ViewLocation` list.
- **`Volunteer`** — global role; explicitly excluded from Photo `ViewLocation` despite being in many other Photo operations.

This is the meaningful gradient in the existing model: **`Viewer` is the "see the data but not the location" tier**, and `GpsViewer` is "Viewer + can see locations." The role name suggests "user with GPS access" — it's effectively `Viewer-with-coordinates`.

### The mechanism: server-side null-out, not coarsening

The runtime decision is **binary**: a user either gets full-precision coordinates or `null`. There's no intermediate coarsening (rounding to 1km, 10km, etc.) in the existing system.

Three implementation patterns are in use across the codebase, with some inconsistency:

**Pattern 1 — flag passed into adapter (cleanest).** `DeploymentAdapter.cs:18–38`:
```csharp
private static Expression<Func<Deployment, DeploymentDto>> ConvertToDto(bool canViewLocation)
{
    return deployment => new DeploymentDto()
    {
        // ...
        Latitude = canViewLocation ? deployment.Latitude : null,
        Longitude = canViewLocation ? deployment.Longitude : null,
        // ...
    };
}
```
Controller computes `canViewLocation = _userContextService.Can(..., ViewLocation)` and passes it in. `DeploymentSearchAdapter.cs:50–54` follows the same pattern.

**Pattern 2 — post-hoc null in the controller.** `SitesController.cs:603–609`:
```csharp
var siteDto = SiteAdapter.ConvertToDto(site);
if (!canViewLocation)
{
    siteDto.Latitude = null;
    siteDto.Longitude = null;
}
```
The adapter unconditionally projects coordinates; the controller strips them after. Same outcome, but the safety property is weaker — easy to forget the null-out on a new endpoint.

**Pattern 3 — no permission check at all.** This is the concerning one. `PhotoAdapter.cs:60–61` projects `Latitude` and `Longitude` unconditionally:
```csharp
Latitude = photo.DeploymentId.HasValue ? photo.Deployment.Latitude : null,
Longitude = photo.DeploymentId.HasValue ? photo.Deployment.Longitude : null,
```
There's no `canViewLocation` parameter on `PhotoAdapter.ConvertToDto()`. The controllers that call it (`PhotosController.cs:731, 795, 863, 989, 1053, 1206`) don't compute `canViewLocation` for Photo. **This means the Photo endpoints currently return GPS coordinates to any non-volunteer user regardless of `PhotoOperations.Names.ViewLocation` permission.** The frontend conditionally hides them in the UI (`PhotoDetails.tsx:485` checks `perms?.canViewLocation`), but the data is on the wire either way.

`IncidentAdapter.cs:127–128` also unconditionally projects `InitialLatitude` / `InitialLongitude`.

These are existing inconsistencies — likely either deliberate (incident response needs coordinates regardless of role) or oversight. Either way, AI Reports inherits them as starting state.

### Frontend handling

The web app receives `canViewLocation` from each "permissions" endpoint and toggles UI accordingly. Six components check `perms.canViewLocation` before rendering coordinates: `PhotoDetails.tsx`, `SiteDetails.tsx`, `OrganizationDetails.tsx`, `DeploymentStatusDetails.tsx`, `ManageEditDeploymentLocation.tsx`, plus map components. The Map page itself is gated by `MapOperations.Names.View` (line 284) — same role set as `Site.ViewLocation`.

The frontend does **not** coarsen or mask coordinates — it shows full precision or nothing.

### Summary of the today-state

- Storage: full precision (`decimal(12, 9)`) on `Site` and `Deployment`.
- Authorization: per-entity `ViewLocation` operation, three roles granted (`GpsViewer`, `OrgAdmin`, `IncidentManager`) + `SystemAdmin` via global bypass.
- Mechanism: server-side null-out for `Deployment` and `Site`; **no enforcement at all** for `Photo` and `Incident` adapters (UI-only gating).
- No coarsening / precision-tiering — binary visibility.
- Existing inconsistencies in enforcement; the user-facing behaviour is more consistent than the wire-level behaviour.

---

## 3. Implications for the MCP server

The MCP server has properties that change the threat model compared to the existing REST API:

**The LLM context is a logging surface.** When the AI Reports service calls Anthropic, the prompt and any data passed as tool-result context are sent over the wire and logged by Anthropic (subject to whatever data-retention policy is in place). If GPS coordinates flow into that context, they flow into Anthropic's infrastructure. Even with restrictive data-retention agreements, this is a fundamentally different exposure surface than wpsWatch's own infrastructure.

**The LLM can paraphrase or restructure data unpredictably.** With raw API calls, a coordinate field is either present or absent — it doesn't move. With an LLM in the middle, the model might describe a camera's location in plain text ("the camera near the south water hole, roughly 1km north of …"), summarise it in unexpected ways, or include it in a follow-up answer the user didn't directly ask for. Defence-in-depth across views, schema, and audit becomes more important.

**The audit log inherits the data.** Every chat turn the AI Reports service logs (per v5 FR-V4-50 / DR-V4-35) includes the LLM's full response. If coordinates appear in a response, they appear in the audit log. The audit log is itself sensitive — restricted access, 13-month retention — but its existence multiplies the places coordinates can be reached.

**Aggregate answers are different from individual rows.** "How many cameras are in Region X?" doesn't expose any individual coordinate. "Show me all active deployments grouped by region" doesn't either. "Where exactly is camera #4231?" does. The MCP server design can lean on this distinction: aggregate answers can be permissive; individual-row coordinate access stays gated.

**Replica architecture changes nothing about the gating logic.** AI Reports reads from `wpswatchprodreplica.database.windows.net` per the v5 decision, so the raw data is identical to what's on the OLTP primary. The same `ViewLocation` operation maps apply by role. The replica doesn't add or remove access; it's the same access, queried elsewhere.

**Cross-org reporting in Phase 2 extends the surface.** When a user has `ViewLocation` in their own org and the AI Reports feature shows them cross-org data (the first cross-org reporting surface in wpsWatch), the gating must apply per-org per-row, not as a global flag. A user with `OrgAdmin` in org A and `Viewer` in org B should see locations from A but not B in the same response.

---

## 4. Three approaches for the MCP server

### Approach A — No GPS to the LLM, ever (current v5 plan)

The `dbo.vw_ai_*` views defined in the OLTP primary (per v5 DR-V5-02) do not project `Latitude` or `Longitude` columns. The AI principal (`ai_reports_reader`) is also `DENY SELECT` on the underlying lat/lon columns of base tables (per v5 S-V5-MUST-28). The CI check (v5 S-V5-MUST-40) enforces non-projection at deploy time.

The LLM never sees coordinates. Aggregate answers about geography are possible ("3 sites in this region") but no individual row carries coordinates. Map rendering, "where exactly is X" questions, and incident-response geographic queries are out of scope for AI Reports.

**Strengths:**
- Strongest defence against the new MCP-specific risks (LLM context exposure, paraphrase risk, audit-log proliferation).
- Matches the v1 security perspective's recommendation; matches the v3 data-v2 perspective's recommendation.
- Simplest to implement and reason about — there's no "did the role check pass?" decision per row, because the data isn't there in the first place.
- Aligns with Matt's review comment 4: *"It's okay to omit this for the MVP, but users will want to be able to query geospatial data and get locations (if their permissions allow) in the long term."*

**Weaknesses:**
- Loses parity with the existing UI for certain reports. The `Region & Site Totals with Deployment` Looker dashboard, for instance, may not surface individual coordinates today, but other reports (the SysAdmin-only ones that field-ops users have asked for access to) might. Phase 1 parity testing will surface this.
- The map-based questions Matt flagged for "the long term" become out of scope until this is revisited.
- Doesn't take advantage of existing `ViewLocation` infrastructure — re-implements gating by absence rather than by role-check.

**Recommended for MVP.** Phase 1 ships with no GPS surface at all.

### Approach B — Mirror the existing `ViewLocation` gating in the MCP layer

The `dbo.vw_ai_*` views *do* project `Latitude` and `Longitude`, but the AI Reports orchestrator computes `canViewLocation` per-entity per-request (same as `DeploymentsController.cs:100, 485, 557` etc. does today) and:

1. Passes the flag to the SQL layer, which conditionally returns coordinates (parameterised stored proc) — same null-out pattern as v3 `DeploymentAdapter`.
2. Strips coordinates from the LLM-context payload before sending to Anthropic if the user doesn't have `ViewLocation`.
3. Includes coordinates in the LLM-context payload if the user does have it — meaning Anthropic sees them.

**Strengths:**
- Functional parity with the existing system — users with `GpsViewer` / `OrgAdmin` / `IncidentManager` see coordinates in AI Reports the way they see them in the rest of wpsWatch.
- Reuses the existing `ViewLocation` operation infrastructure; no parallel access-model.
- Map-based questions and "where exactly is X" become possible for the right roles.

**Weaknesses:**
- Coordinates flow into Anthropic's LLM context for authorised users. Even with data-retention agreements, this is meaningfully more exposure surface than the current REST API offers (where coordinates go from API → browser → user only, no LLM intermediary).
- The audit log captures coordinates in LLM responses for authorised users — 13-month retention, restricted access, but still a copy.
- Inherits the existing inconsistencies: `PhotoAdapter` and `IncidentAdapter` currently *don't* enforce `ViewLocation`, so AI Reports has to decide whether to be more strict than today or match the existing leniency.
- Per-row enforcement in cross-org Phase 2 gets complicated — coordinates from org A but not org B in the same query response means per-row decoration, not per-request decoration.

**Recommended for Phase 2+ if and when map-based questions become a real product need.**

### Approach C — Coarsened coordinates with explicit precision tiers

A middle ground proposed in the original v1 security perspective. Coordinates *are* available but at role-determined precision:

| Role | Precision returned |
|---|---|
| `SystemAdmin`, `OrgAdmin`, `IncidentManager` | Full precision (~1m) |
| `GpsViewer` | Full precision in own org; 3 decimal places (~110m) cross-org |
| `Viewer` | 2 decimal places (~1km) |
| `PhotoUploader`, `PhotoTagger` | Coordinates omitted entirely |
| `Volunteer` | No access to AI Reports at all |

Implementation: distinct views per precision tier (`dbo.vw_ai_deployment_coarse`, `dbo.vw_ai_deployment_fine`) in OLTP primary, or run-time rounding in a stored proc parameterised on the requesting role.

**Strengths:**
- Most graded approach — each role gets exactly the geographic resolution they need for their job, no more.
- Useful for aggregation answers ("rough map of where activity has been highest in the last week") that don't need pinpoint precision.
- Preserves an answer-capability for `Viewer` users that approach A wouldn't.

**Weaknesses:**
- Doesn't match the existing system. wpsWatch today is binary (full or null); coarsening is a net-new model.
- The coarsened coordinates still flow through the LLM context — same exposure surface concern as approach B, just at different precision.
- Coarsening is misleading. A coordinate rounded to 2 decimal places looks like a coordinate; users may not realise it's only accurate to 1km and over-trust the value (e.g. radio it to a field team who treats it as precise).
- More complex to implement, test, and audit. Multiple view variants. Per-row rounding logic. CI checks per tier.
- The "sensitive combination" rule (v3 security S-MUST-29: species + GPS + recent timestamp) still applies and complicates the precision-tier matrix.

**Not recommended for MVP. Possibly revisit in a future phase if map-based aggregation becomes a strong product need and approach B feels too permissive.**

---

## 5. Recommendation

**MVP and Phase 1: Approach A.** No GPS to the LLM. Matches v5 plans, matches v1 security perspective, matches the user-perspective's "things I'd never trust an LLM with — anything involving GPS coordinates of the rhinos themselves." Simplest defence; nothing to revisit during the MVP development period.

**Phase 2: revisit with usage data.** Once chat is live and we can see which questions users actually ask, we'll know whether map-based and location-based questions are a real demand. If yes, move to approach B for those questions, with the existing `ViewLocation` operations as the gate. The migration is:
1. Add `Latitude` / `Longitude` to specific `dbo.vw_ai_*` views that will support location queries.
2. Update the AI Reports orchestrator to check `ViewLocation` per relevant entity, strip coordinates from LLM context where the requester lacks it.
3. Update the CI check (S-V5-MUST-40) to allow these specific views to project lat/lon while still enforcing the rest.
4. Audit-log policy: explicitly mention coordinate-bearing responses as a sub-category of the existing prompt-text restriction (already S-V4-MUST-38).
5. Establish Anthropic data-retention agreements specifically for the AI Reports workload, with WPS HQ legal sign-off.

**Approach C remains an option** for a far-future enhancement, particularly if cross-org reporting (Phase 2) surfaces a need for coarse-grained "show me a map of activity across all my reserves" answers that approach B wouldn't satisfy cleanly. Don't pre-build it; wait for the use case.

---

## 6. Specific implementation notes

A few code-level points the dev team should be aware of when implementing approach A (and B, if/when):

- **The `vw_ai_*` views must exclude lat/lon by definition.** The CI check (v5 S-V5-MUST-40) covers this. Worth a unit test on the view layer that asserts `INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME LIKE 'vw_ai_%' AND COLUMN_NAME IN ('Latitude', 'Longitude')` returns zero rows.
- **The output filter (v5 DR-V5-20)** drops columns matching patterns like `*token*`, `*hash*`, `*secret*`, etc. Add `*latitude*` and `*longitude*` to the list as belt-and-suspenders.
- **The audit log (v5 FR-V4-50)** should explicitly tag any chat response that includes a coordinate-like value pattern (e.g. matching `-?\d+\.\d{4,}`) as `contains_coordinates = true` even in approach A — that catches the case where a future view inadvertently surfaces a coordinate or the LLM hallucinates one. Investigate any non-zero count.
- **The existing inconsistencies in `PhotoAdapter` and `IncidentAdapter`** are not AI Reports' problem to fix, but worth a separate ticket on the wpsWatch core team — those endpoints leak coordinates over the wire to non-`ViewLocation` users today, even though the UI hides them. Flag for the security review they'd run on AI Reports anyway, since reviewers may notice the inconsistency between the new AI Reports controls and the older REST endpoints.
- **For approach B (later phase):** look at how the existing system handles the inconsistency. Decide whether AI Reports matches the strict interpretation (every Photo / Incident response strips coordinates if the user lacks `ViewLocation`) or the lenient interpretation (matches today). The strict interpretation is more defensible from a security standpoint but may surprise users who can see coordinates in the photo-details modal but not in the AI Reports answer about the same photo.
- **For approach C (future):** if implemented, the precision tier should be embedded in the view name or returned column name (`latitude_km_precision` vs. `latitude_full_precision`) so the LLM context makes the precision explicit. A user receiving a coarse coordinate should see "Rough location: 24.05, -28.21 (±1 km)" rather than "24.0500, -28.2100" which looks identical to a precise reading.

---

## 7. Open questions specific to coordinate handling

To track in OQ-V3-21 (site-level roles) and the broader Phase 2 planning:

- **Q1: Does the planned site-level user roles work** (OQ-V3-21) include a site-level `ViewLocation` operation, or does it stay at the org level? If site-level, that's a richer model that approach B will need to support natively.
- **Q2: Is the existing `PhotoAdapter` / `IncidentAdapter` coordinate exposure** deliberate (incident response needs lat/lon regardless of role) or a bug? Worth confirming with the wpsWatch core team. AI Reports' decision is independent but the answer informs what's reasonable.
- **Q3: What is Anthropic's data-retention agreement** for the wpsWatch tenant? If approach A is the MVP, this question can wait. If approach B becomes Phase 2 work, it's a prerequisite — coordinates flowing into a context with arbitrary retention would not pass security review.
- **Q4: Is there an explicit "no GPS to LLM" requirement** in any donor / grant / partner agreement that funds WPS? If yes, that locks in approach A regardless of technical preference. Worth confirming with WPS HQ legal before any future move to approach B.

---

*This document focuses on coordinates specifically. The broader security model — sensitive credentials, PII, the `chat_interaction_log.prompt_text` tier — is in [`ai-reports-requirements-v5.md` §6](./ai-reports-requirements-v5.md) and [`perspectives/security.md`](./perspectives/security.md).*
