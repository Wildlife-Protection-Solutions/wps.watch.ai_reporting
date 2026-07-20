These are the things the wpsWatch dev team should be aware of — some may invite pushback on the ticket's framing:

JWT is HS256 with a symmetric JwtKey, not Entra/RS256 (wps.watch.api/Wps.Watch.Api/Startup.cs:242–261). The CLAUDE.md hints at Entra but the implementation is symmetric. Any external service either shares the key, calls a new introspection endpoint, or waits for a JWT migration. This is the single most consequential auth constraint.

The seven reports the prompt listed are the AdminReport set, not the universal baseline. Header.tsx:582–630 (SysAdmin) vs 731–816 (regular). The ticket reads as if "the seven" were the regular-user set; in fact, regular users see only three.

No ReportsController, no signed Looker URLs. IFrameComponent.tsx:14–70 constructs Looker URLs with ?params=<JSON-encoded org UUID + user ID>. Filtering is purely on the Looker data-source side; the wpsWatch backend has no Reports surface today.

No replica connection string is configured anywhere in the repo. Wiring up wpswatchprodreplica.database.windows.net is net-new work, not extending an existing pattern.

The DB schema has no reporting views, no stored procedures, no pre-aggregation tables. All current reporting is OLTP, including the long join chain Photo → Deployment → Site → Organization.

Cosmos DB does NOT hold org membership — OrganizationUserRole in SQL Server is the source of truth, queried per-request by UserAuthorizationFilter.cs:36–38. The new service should query the same table on the replica.

UserReport and AdminReport operations already exist server-side (OperationRoles.cs:446–463). Volunteers are correctly excluded today at both the API and UI layers — no gap there. The new service should consume the same operations.

localStorage.savedQueries exists for dashboard views (DashboardComponent.tsx:354–358, 835) — a precedent for the phase-3 saved-query feature, though phase 3 needs proper backend storage rather than client-only persistence.

Reports is currently single-org and listed as out-of-scope for cross-org (cross-org-overview.md:14–25). The MVP inherits this constraint per the human owner's call; AI Reports could become the first cross-org reporting surface in phase 2+, which is a meaningful new design pattern in wpsWatch.