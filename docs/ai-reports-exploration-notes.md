# AI Reports — Stage 1 Exploration Notes

**Purpose:** Ground-truth findings from `wps.watch.api` and `wps.watch.web` to support the AI Reports requirements doc. Citations are file paths within those repos.

**Date assembled:** 2026-05-04.

> ⚠️ **Surprises that change the shape of the project** — read first. Detail follows.
>
> 1. **The seven Looker reports the prompt listed (Camera Inventory, Battery Level, Photo Count, Region & Site Totals, SIM Contract Renewal, SIM Prepaid Data, Top 10 Offline) are *all* gated by the `AdminReport` operation — i.e. they are the System Admin set.** Regular users (`UserReport` operation) see only **three** iframe-embedded reports: Camera Battery Level, Camera Deployment Detail, Photo Count By Camera. ([`Header.tsx:582–730` vs `731–816`](#-1-reports-menu--frontend-role-gating))
> 2. **JWT validation uses a symmetric `JwtKey` (HS256), not Azure AD/Entra (RS256).** A separate service can only validate tokens by sharing the same secret key — or by routing through the API. ([`Startup.cs:242–261`](#-3-auth-surface-for-an-external-service))
> 3. **Reports today is single-org by design.** Both Reports menu items are disabled with a tooltip in multi-org mode. The new feature will inherit this constraint for MVP per the human owner's call. ([`cross-org-overview.md:14–25`](#-5-reports-cross-org-status))
> 4. **No replica connection string is configured anywhere in the codebase.** Wiring it up is a net-new task. ([`Startup.cs:135`](#-replica-realities))
> 5. **The DB schema has no reporting views, no stored procs, and no pre-aggregation tables.** All reporting is OLTP today. ([`Wps.Watch.Sql/Migration/`](#-stored-procedures-and-views))
> 6. **Volunteers are excluded from Reports at the API layer (`UserReport` role list omits them) and at the UI layer (no Reports menu render path).** This was stated in the prompt as an assumption to verify — confirmed. ([`OperationRoles.cs:446–463`](#-roles-and-report-visibility))

---

## 1. Permission and org-scoping model

### `IUserContextService`

- **Interface:** `Wps.Watch.Api/Authorization/IUserContextService.cs:11–79`
- **Implementation:** `Wps.Watch.Api/Authorization/UserContextService.cs:11–78`

Per-request state hydrated by a filter (see below). Exposes:

- `UserId` — extracted from JWT claims (`ClaimTypes.NameIdentifier` with `"sub"` fallback) — `UserContextService.cs:26–29`
- `IsAdmin`, `IsVolunteer` — `User?.IsInRole(RoleNames.*)`
- `IsMultiOrg` — flag set per-request (default `false`)
- `OrganizationUserRoles` — list hydrated from SQL at request start
- `OrganizationIds` — distinct org IDs derived from `OrganizationUserRoles`

### How org membership is loaded

**Cosmos is NOT used for org membership.** The full set is fetched from SQL on every request:

- `Wps.Watch.Api/Authorization/UserAuthorizationFilter.cs:36–38` queries the `OrganizationUserRole` table by `UserId` and stores the result on `IUserContextService`.
- The cross-org data model doc (`wps.watch.api/docs/business-rules/cross-org-data-model.md`) confirms this is the source of truth: one row per (user, org, role) triplet.
- **Cosmos** holds user preferences and dashboard/quick-filter org-scope state only — not org membership.

**Implication for the new service:** The new AI Reports service must either query the same `OrganizationUserRole` table directly (cleanest if it already imports `Wps.Watch.Data`) or call a new bootstrap endpoint on the API.

### Single-org vs multi-org decision

`UserContextService.Can<TEntity>(...)` (lines 60–72):

- If `IsMultiOrg || IsAdmin || IsVolunteer` → permission check is "in any org the user has access to."
- Otherwise → permission check is "in the current org from the JWT's org claim, intersected with authorized orgs."

### `X-MultiOrgMode` header

Read at `UserAuthorizationFilter.cs:44`:

```csharp
var isMultiOrgMode = context.HttpContext.Request.Headers["X-MultiOrgMode"] == "true";
_userContext.SetMultiOrg(isMultiOrgMode);
```

API keys never get multi-org mode (`UserAuthorizationFilter.cs:86` forces `false`).

### Cross-org data model (key facts)

From `wps.watch.api/docs/business-rules/cross-org-data-model.md`:

- Replaces legacy `UserOrganization` table with `OrganizationUserRole` (one row per user/org/role triplet).
- One role per org (enforced).
- Global roles (System Admin, Volunteer) → no `OrganizationUserRole` rows; org-level roles → at least one row.
- Cascading cleanup on user removal: `AdminContact`, `FieldContact`, `CustomContacts` for sites + alert configs in that org.

---

## 2. Roles and report visibility

### Role definitions

`Wps.Watch.Api/Authorization/Operations/RoleNames.cs:7–52`:

| Constant | String | Tier |
|---|---|---|
| `SystemAdmin` | `"System Admin"` | global |
| `Viewer` | `"Viewer"` | org-level |
| `GpsViewer` | `"GPS Viewer"` | org-level |
| `OrgAdmin` | `"Org Admin"` | org-level |
| `PhotoUploader` | `"Photo Uploader"` | org-level |
| `PhotoTagger` | `"Photo Tagger"` | org-level |
| `IncidentManager` | `"Incident Manager"` | org-level |
| `Volunteer` | `"Volunteer"` | global |

### Report-relevant operation/role mappings

`Wps.Watch.Api/Authorization/OperationRoles.cs:446–463` — `ReportOperationRoles.RolesByOperation`:

- `UserReport` → `[OrgAdmin, IncidentManager, Viewer, GpsViewer]`
- `AdminReport` → `[]` — i.e. **System Admin only** (the empty list still allows admins via the global bypass)

**Volunteers are explicitly excluded from `UserReport`.** Confirmed at both API and UI layers (see §3).

### Frontend role detection

`wps.watch.web/src/context/reducer.ts:6–21` exposes `UserState` with:
- `resources: string[]` — flat list of resource strings the user has been granted (includes `"AdminReport"`, `"UserReport"`, `"SystemAdmin"`, `"Volunteer"`)
- `isSystemAdmin: boolean` and `isVolunteer: boolean` — derived in `reducer.ts:83–85`

The Reports menu in `Header.tsx` checks `currentUser.resources.includes("AdminReport")` and `currentUser.resources.includes("UserReport")`.

### Reuse for the new service

**Reuse the `UserReport` / `AdminReport` operations.** The new feature is reports-with-AI; it can fit cleanly into the existing operation model. If we want a finer split (e.g. `UserReport.NaturalLanguageQuery`, `UserReport.SaveCustomQuery`), add new operation names rather than introducing new resources.

---

## 3. Auth surface for an external service

### JWT validation today

`Wps.Watch.Api/Startup.cs:242–261`:

```csharp
services.AddAuthentication(...).AddJwtBearer(cfg => {
    cfg.ValidIssuer = _wpsConfiguration.JwtIssuer;
    cfg.ValidAudience = _wpsConfiguration.JwtIssuer;
    cfg.IssuerSigningKey = new SymmetricSecurityKey(
        Encoding.UTF8.GetBytes(_wpsConfiguration.JwtKey));
});
```

`JwtIssuer` and `JwtKey` come from the `WpsConfiguration` section of `appsettings.json`. **This is HS256 (symmetric) — not the asymmetric Entra setup hinted at in the API CLAUDE.md.** The signing key is a shared secret.

**Options for the AI Reports service:**

1. **Share the symmetric `JwtKey`** with the new service. Cheapest; introduces a key-distribution surface.
2. **Migrate to asymmetric (RS256) issued by Entra**, then both services validate using the JWKS endpoint. Cleanest long-term; requires changes in the API and any other JWT consumers.
3. **Validate-via-callback** — the new service calls a (new) `/api/auth/introspect` endpoint that returns user id + org list. Simple, adds a per-request hop.

Recommendation will be made by the backend persona in Stage 2.

### API key (`Wps-Api-Key`)

`Wps.Watch.Api/ApiKeyMiddleware.cs:52–106` — looks up the header against the `KeyRoles` SQL table and constructs a `ClaimsPrincipal` with org + role claims. Stores key info in `HttpContext.Items["ApiKeyInfo"]`. **Always single-org** (`UserAuthorizationFilter.cs:86`).

This is the existing pattern for service-to-service auth. The new AI Reports service could itself hold an API key (for any back-channel calls it needs to make to the main API), but the user's session must still be established from a JWT — API keys don't represent end users.

### Bootstrap pattern

The API does **not** expose a "give me this user's orgs and roles" endpoint today. Auth is intermixed with `UserAuthorizationFilter`. The new service can either:

- import `Wps.Watch.Data` and read `OrganizationUserRole` directly (preferred — same source of truth), or
- a new endpoint is added on the API for the AI Reports service to call once per session (cleanest separation, adds a request).

---

## 4. Code-sharing potential between the API and a new .NET service

`.csproj` analysis:

- **`Wps.Watch.Data.csproj`** — targets .NET 7. Pulls EF Core, SQL Server, Identity. **Importable** by a new service that will read SQL Server.
- **`Wps.Watch.Business.csproj`** — targets .NET 7. Light deps (`Newtonsoft.Json`, `NodaTime`) + reference to `Wps.Watch.Data`. **Importable** for shared domain logic.
- **`Wps.Watch.Api.csproj`** — targets .NET 8, heavy deps (App Insights, Cosmos, Blob, Twilio, SendGrid…). **Not suitable** for import.

**The authorization classes (`Operation<T>`, `OperationRoleMapping<T>`, `RoleNames`, `*Operations`) live inside `Wps.Watch.Api/Authorization/`.** They cannot be imported as-is. To reuse them in the new service, either:

- extract into a new `Wps.Watch.Authorization` shared project / NuGet, or
- copy the constants and re-implement the lightweight `Can(...)` logic.

The first is recommended; the auth constants are the kind of contract that shouldn't drift.

**Net target alignment** — the new service should target .NET 8 to match the API. `Wps.Watch.Data` is still .NET 7; either bump it or reference it cross-version.

---

## 5. Reports menu — frontend wiring (the seam being replaced)

### Reports menu — frontend role gating

`wps.watch.web/src/components/common/Header.tsx`:

- **AdminReport gate (System Admin only)** — lines 582–630. Renders 7 entries that link out to Looker Studio dashboards using `target="_blank"`. The seven reports the prompt lists.
- **UserReport gate (regular users with the `UserReport` resource)** — lines 731–816. Renders 3 entries that route to `/reports/<id>` via `handleIframeRedirect()` (line 105–107):
  - Camera Battery Level → `handleIframeRedirect("batteryLevel")`
  - Camera Deployment Detail → `handleIframeRedirect("cameraTotals")`
  - Photo Count By Camera → `handleIframeRedirect("photoCount")`

**Both menus are disabled with a tooltip when `multiOrgState.isMultiOrgModeActive` is true** (lines 588–595, 737–745).

> **Important correction to the prompt's framing:** The seven reports are not "the regular set." They're the System Admin set. The regular-user set is the three iframe reports above. The requirements doc must clarify which set the MVP replaces (most likely both, but should be stated).

### Reports component & Looker handoff

- Route in `wps.watch.web/src/App.tsx:72`: `<PrivateRoute path="/reports/:id" component={IframeComponent} />`
- Component: `wps.watch.web/src/components/common/IFrameComponent.tsx:14–70`
- Mechanism: **iframe with query-parameter filtering**. Constructs Looker URLs with `?params=<JSON-encoded org and user IDs>`:

```js
// IFrameComponent.tsx
cameraTotals:  ds9.orgiduuid + ds9.userid
photoCount:    ds6.orgiduuid + ds6.userid
batteryLevel:  ds7.orgiduuid + ds7.userid
```

- The org UUID is fetched via `getOrganizationUuid(userId, organizationId)` (`usersApi.ts:206`) before the iframe loads.
- **No signed URLs, no Looker JWT, no row-level security delegated by Looker.** The filter parameters are client-controllable — security relies on Looker's own access controls plus parameter filtering at the data-source level.

### Hardcoded Looker dashboard URLs (System Admin set)

Found in `Header.tsx:630–723`:

| Report | Looker URL |
|---|---|
| Camera Inventory | `lookerstudio.google.com/u/0/reporting/4734926e-48de-4852-be76-d659b42e6e2a/page/aIqUD` |
| Deployed Camera Battery Level | `…/reporting/bb90526c-4693-465e-99e2-6c7e1e18fae4/page/wADhD` |
| Photo Count By Camera | `…/reporting/56fa2e9c-5c2e-4aca-a067-8bd43aea8599/page/qqsfD` |
| Region & Site Totals with Deployment | `…/u/0/reporting/dbe8998a-3286-4034-b597-2cbb2fd454ce/page/r0XaD` |
| SIM Contract Renewal | `…/reporting/ca916f1f-3768-4982-b702-97e8c7824d5e` |
| SIM Prepaid Data | `…/reporting/0e62777c-eeac-4a69-a3b3-cdb87ea807f3` |
| Top 10 Offline | `…/u/0/reporting/a1f146d4-efd7-40a8-b545-a787ed40f2ac/page/R4OVD` |

There is **no `ReportsController` on the API.** The Looker integration is entirely client-side.

### Reports cross-org status

`wps.watch.web/docs/business-rules/cross-org-overview.md:14–25` lists Reports under "Out-of-Scope Pages (no cross-org support yet)."

Per the human owner's clarification (recorded in the plan): the AI Reports MVP will **inherit single-org behavior**. Cross-org reporting is phase-2+.

### Saved-queries precedent (relevant for phase 3)

The web app already persists named query sets to `localStorage`:

- `localStorage.savedQueries` → dashboard views (`DashboardComponent.tsx:354–358, 835`)
- `localStorage.savedQuickFilters` → quick-filter definitions

These are **client-only**; there is no backend `DashboardView` table. Phase 3 ("save the SQL not the prompt") will need new backend storage.

---

## 6. Schema surface for the seven reports

DbContext: `Wps.Watch.Data/Context/WpsDbContext.cs:11–70`. 41+ `DbSet<>` entries. Reports-relevant entities: `Devices`, `Deployments`, `Photos`, `Sites`, `Organizations`, `Regions`, `SimCardDataPlans`, `PrepaidDataLimitTerms`, `Users`.

### Inferred mappings

#### Camera Inventory
- Tables: `Device` (primary), `DeviceType`, `DeviceMake`, `DeviceModel`, `DeviceSource`, `Organization`.
- Key columns: `DeviceId`, `OrganizationId`, `Name`, `SerialNumber`, `ImeiNumber`, `Make/Model` (denormalized + FK), `FirmwareVersion`, `CarrierName`, `WpsSponsoredSim`, `PurchaseDate`, `DecommissionDate`.

#### Deployed Camera Battery Level
- Tables: `Deployment` (primary), `Device`, `Site`, `Organization`.
- Active filter: `Deployment.EndDateTimeUtc IS NULL`.
- Key columns on `Deployment`: `BatteryLevel` (decimal, nullable), `BatteryBoxStartDate`, `SolarPanel` (bool), `Latitude`, `Longitude`, `LastEventDateTimeUtc`, `EventCount`.

#### Photo Count by Camera
- Tables: `Photo` (primary), `Deployment`, `Device`, `Site`, `Organization`.
- Aggregation: count of `Photo` rows grouped by `DeploymentId`/`DeviceId` and a date bucket; filter `IsRemoved = false`.
- Date columns: `CaptureDateTimeUtc`, `IngestDateTimeUtc`, `CaptureDateTimeLocal`.
- Status flags: `IsCleared`, `IsRemoved`, `IsReviewed`, `HasVideo`, `IsFavorited`, `IsReported`.

#### Region & Site Totals with Deployment
- Tables: `Organization`, `Region`, `Site`, `Deployment`.
- Aggregation: count of `Site` per `Region` (via `Organization`), plus count of active `Deployment` per site.
- `Site` includes `Latitude`, `Longitude` (decimal 12,6), `CountryName`, `IsActive`.

#### SIM Contract Renewal
- Tables: `Device` (primary).
- Key columns: `SimContractRenewalDate`, `SimCardDataPlanId`, `SimCardNumber`, `PhoneNumber`, `CarrierName`.

#### SIM Prepaid Data
- Tables: `Device` (primary), `PrepaidDataLimitTerm`, `SimCardDataPlan`.
- Key columns: `PrepaidDataLimitGb`, `PrepaidDataPlanRenewal`, `PrepaidDataLimitTermId`.
- **Gap:** No `DataUsedGb` / `DataRemainingGb` column on `Device`. Likely fed by Twilio or similar carrier API not visible in the EF model. **This is an open question for the requirements doc.**

#### Top 10 Offline
- Tables: `Deployment` (primary), `Device`, `Site`, `Organization`.
- "Offline" computed from `Deployment.LastEventDateTimeUtc` (no explicit `IsOnline` column). Threshold (24h, 7d, …) is undefined in code — **open question.**

### Org-scoping paths

Every report-bound table is scoped via `OrganizationId`:

```
Organization (1) → (n) Device, Site
Site (1) → (n) Deployment
Device (1) → (n) Deployment
Deployment (1) → (n) Photo
```

`Photo`'s scope chain is the longest (`Photo → Deployment → Site → Organization`). This means the new service's row-level security needs to traverse joins, not just filter on `OrganizationId` directly.

### Sensitive fields (must NOT be exposed to the LLM tool surface)

| Table | Column | Reason |
|---|---|---|
| `Site` | `DasToken`, `DasBaseUrl` | DAS API credentials |
| `Site` | `SmartIntegrateToken`, `SmartIntegrateBaseUrl` | external API credentials |
| `Site` | `SlackWebhookUrl` | Slack webhook |
| `Device` | `PhoneNumber`, `ForwarderPhoneNumber` | PII / routing |
| `User` | `PasswordHash`, `SecurityStamp`, `ConcurrencyStamp` | auth secrets |
| `User` | `Email`, `PhoneNumber` | PII |
| `Key` | `ApiKey` | service-to-service secret |
| `ImageRecognitionService` | `ApiKey`, `Url` | ML service credentials |

GPS columns (`Site.Latitude/Longitude`, `Deployment.Latitude/Longitude`) are not credential-sensitive but **are operationally sensitive for endangered species** — coarsening or role-gating recommended.

---

## 7. Stored procedures and views

Searched `Wps.Watch.Sql/Migration/` and `Wps.Watch.Sql/`:

- **No `CREATE VIEW` statements found** in current migrations.
- **No `CREATE PROCEDURE` statements found.**
- **No pre-aggregation tables** (`PhotoCountByDay`, `DeviceOnlineStatus`, etc.).
- All migrations follow a strict pattern: `SET XACT_ABORT ON`, `SET TRANSACTION ISOLATION LEVEL SERIALIZABLE`, version-table check.
- No `WITH (NOLOCK)` or `READ UNCOMMITTED` patterns.

**Implication:** the new service either runs raw queries against OLTP tables (with all the indexing risk that implies) or the dev team adds a curated set of reporting views / pre-aggregations. Sub-agent 4 should make the call.

---

## 8. Replica realities

Searched both repos for `wpswatchprodreplica`, `replica`, `ReadOnly`, `ApplicationIntent`:

- **Zero hits in source code.** The replica connection string is not currently configured.
- `Wps.Watch.Api/Startup.cs:135` reads a single connection string `"WpsDbContext"`.
- `appsettings*.json` files are gitignored, so the replica string may be present in deployed config but isn't visible in the repo.
- No replication-lag commentary in code, READMEs, or CLAUDE.md files.
- DB context auth is via `DefaultAzureCredential()` (managed identity, `Startup.cs:147`).
- Command timeout: 30 seconds (`Startup.cs:156`, from `WpsConfiguration.SqlCommandTimeout`).

**Net:** wiring up the replica (connection string + `ApplicationIntent=ReadOnly` + read-only login + lag tolerance) is net-new work. There's no existing pattern to extend.

---

## 9. Open questions surfaced during exploration

These are inputs for Stage 2 sub-agents and the final doc:

1. **`SIM Prepaid Data` source-of-truth:** the EF model has plan limits but no usage. Where does data-used come from today? (Twilio API? Carrier portal? Manual entry into a column not visible to the agent?)
2. **"Offline" threshold:** what `LastEventDateTimeUtc` cutoff defines offline? Currently undefined in code.
3. **Regular-user report set vs SysAdmin set:** the seven reports the prompt named are SysAdmin-only. The three regular-user reports are a strict subset. Should MVP replace both sets, or only one to start?
4. **Legacy `UserOrganization` references in Looker:** the cross-org migration switched the source of truth from `UserOrganization` to `OrganizationUserRole`. If any of the Looker queries still reference the legacy table, that's a re-platform forcing function the new service inherits.
5. **JWT migration to RS256/Entra:** is this on anyone's roadmap? If yes, the new service should plan for it; if no, key-sharing with HS256 is the practical path.

---

## 10. File-citation index (quick reference)

API:
- `Wps.Watch.Api/Authorization/IUserContextService.cs` (interface)
- `Wps.Watch.Api/Authorization/UserContextService.cs` (impl, single-vs-multi-org logic)
- `Wps.Watch.Api/Authorization/UserAuthorizationFilter.cs` (per-request hydration, X-MultiOrgMode read)
- `Wps.Watch.Api/Authorization/OperationRoles.cs` (UserReport / AdminReport mapping at 446–463)
- `Wps.Watch.Api/Authorization/Operations/RoleNames.cs` (role constants)
- `Wps.Watch.Api/ApiKeyMiddleware.cs` (Wps-Api-Key handling)
- `Wps.Watch.Api/Startup.cs:135–168, 242–261` (DbContext + JWT setup)
- `wps.watch.api/docs/business-rules/cross-org-data-model.md`
- `Wps.Watch.Data/Context/WpsDbContext.cs`
- `Wps.Watch.Data/Models/Device.cs`, `Deployment.cs`, `Photo.cs`, `Site.cs`, `Organization.cs`, `Region.cs`, `SimCardDataPlan.cs`, `PrepaidDataLimitTerm.cs`
- `Wps.Watch.Data.csproj`, `Wps.Watch.Business.csproj` (deps)
- `Wps.Watch.Sql/Migration/WpsWatch-2.1.9.sql`, `WpsWatch-2.1.10.sql`

Web:
- `wps.watch.web/src/components/common/Header.tsx:582–816` (Reports menus)
- `wps.watch.web/src/components/common/IFrameComponent.tsx:14–70` (Looker iframe + filter passing)
- `wps.watch.web/src/App.tsx:72` (`/reports/:id` route)
- `wps.watch.web/src/context/reducer.ts:6–21, 83–85` (UserState + role derivation)
- `wps.watch.web/src/api/users/usersApi.ts:206` (`getOrganizationUuid`)
- `wps.watch.web/src/components/common/DashboardComponent.tsx:354–358, 835` (savedQueries pattern)
- `wps.watch.web/docs/business-rules/cross-org-overview.md:14–25` (Reports out-of-scope for cross-org)
