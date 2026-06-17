# Wps.Watch.AiReporting

Phase 1 of the AI Reports service for wpsWatch. Exposes the existing Looker-tier canned reports as both a REST API and an MCP (Model Context Protocol) tool surface, backed by a read-only mirror of the wpsWatch operational database.

See [`docs/ai-reports-design-brief-v3.md`](../docs/ai-reports-design-brief-v3.md) and [`docs/ai-reports-requirements-v5.md`](../docs/ai-reports-requirements-v5.md) for the full design context.

---

## Quick start

### Prerequisites

- **.NET 8 SDK** (or .NET 9 with SDK targeting net8.0 — the build works with either)
- **Azure CLI** signed in (`az login`) — used by the .NET app to authenticate to SQL via Entra
- **Database access**: your Entra identity must be mapped to a database user on `wpswatch-dev` (the QA-tier DB). If you can connect via SSMS using Microsoft Entra auth, this works too.

### Local config

`Wps.Watch.AiReporting/appsettings.json` contains the local-dev defaults — connection string and `DevAuth` shim, both safe to commit (no secrets: the SQL connection uses Active Directory Default auth via `az login`, and the dev shim is fail-closed at startup outside the Development environment).

No env vars need to be set for local dev. For non-local deployments, override these values via `appsettings.{Environment}.json` (e.g. `appsettings.Production.json`) or environment variables (`ConnectionStrings__WpsReplica`, `DevAuth__Enabled=false`).

### From a fresh terminal — full sequence

```powershell
# 1. Verify your Azure session (token persists for days, but check first)
az account show
# If it errors with "Please run 'az login'", run that and re-auth in the browser.

# 2. Build (first run after a clone, or any time you've edited C# source)
cd "C:\Users\Jacob WPS\wkspaces\WPSWatch\wps.watch.ai_reporting"
dotnet build
```

Then pick one of the modes below.

### Mode A — Web API

```powershell
dotnet run --project Wps.Watch.AiReporting
```

The server listens on `http://localhost:5077`. In another terminal:

```powershell
curl http://localhost:5077/health/db
curl http://localhost:5077/api/reports
curl -X POST -H "Content-Type: application/json" -d "{}" `
  http://localhost:5077/api/reports/region-site-totals/run
```

Swagger UI: `http://localhost:5077/swagger`.

### Mode B — MCP stdio (raw)

```powershell
dotnet run --project Wps.Watch.AiReporting --no-launch-profile -- mcp
```

Reads JSON-RPC on stdin, writes responses on stdout, logs to stderr. Used directly by MCP clients (Claude Desktop, the Inspector, etc.) — not usually invoked by a human.

> **Important: always pass `--no-launch-profile` for MCP mode.** Without it, `dotnet run` prints a `"Using launch settings from..."` banner to **stdout** before the server starts, which corrupts the JSON-RPC stream. The MCP Inspector tolerates this; Claude Desktop and other strict clients fail to connect with errors like `Unexpected token 'U', "Using laun"... is not valid JSON`.

### Mode C — MCP Inspector (visual UI for testing tools)

Launches the [MCP Inspector](https://github.com/modelcontextprotocol/inspector) and points it at the MCP server as a subprocess. Opens a browser tab with a click-through UI for inspecting tools and running them.

```powershell
npx.cmd @modelcontextprotocol/inspector dotnet run --project Wps.Watch.AiReporting --no-build --no-launch-profile -- mcp
```

Click **Connect** when the browser opens, then call `list_canned_reports` or `run_canned_report` from the tools panel.

> **Why `npx.cmd` and not `npx`?** PowerShell's default execution policy blocks the unsigned `npx.ps1` shim that ships with Node. The `.cmd` variant works without any policy changes. For a one-time permanent fix that makes all Node tooling work normally in PowerShell, run:
> ```powershell
> Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser
> ```
> After that, plain `npx ...` works.

### Mode D — Claude Desktop

In `%APPDATA%\Claude\claude_desktop_config.json`:

```json
{
  "mcpServers": {
    "wps-watch-ai-reporting": {
      "command": "dotnet",
      "args": [
        "run",
        "--project",
        "C:/Users/Jacob WPS/wkspaces/WPSWatch/wps.watch.ai_reporting/Wps.Watch.AiReporting",
        "--no-build",
        "--no-launch-profile",
        "--",
        "mcp"
      ]
    }
  }
}
```

Restart Claude Desktop. Ask something like *"list the canned reports"* or *"run the region-site-totals report"*.

### Common gotchas

- **`Could not copy ... file is being used by another process`** during build — a previous run's `Wps.Watch.AiReporting.exe` is still holding the dll. Fix:
  ```powershell
  Get-Process Wps.Watch.AiReporting -ErrorAction SilentlyContinue | Stop-Process -Force
  ```
  Then rebuild.
- **Inspector tool calls fail with a generic "an error occurred" message** — most often an expired Azure token (DB auth fails inside the server). Check the Inspector's "Server stderr" pane; if it's a SQL auth error, re-run `az login` and reconnect.
- **Edited C# code, but Inspector still shows the old behavior** — Inspector is running the old binary because of `--no-build`. Stop Inspector (Ctrl+C), rebuild, restart Inspector.

---

## Reports

| ID | Name | Operation | Cost |
|---|---|---|---|
| `region-site-totals` | Region & Site Totals with Deployment | AdminReport | medium |
| `camera-inventory` | Camera Inventory | AdminReport | medium |
| `deployed-camera-battery` | Deployed Camera Battery Level | AdminReport | medium |
| `photo-count-by-camera-last-month` | Photo Count By Camera (last 30 days) | AdminReport | slow |
| `photo-count-by-camera-range` | Photo Count By Camera (custom range) | AdminReport | slow |
| `sim-contract-renewal` | SIM Contract Renewal | AdminReport | fast |
| `sim-prepaid-data` | SIM Prepaid Data | AdminReport | slow |
| `deployment-health-status` | Deployment Health Status | AdminReport | fast |
| `camera-battery-level` | Camera Battery Level | UserReport | medium |
| `camera-deployment-detail` | Camera Deployment Detail | UserReport | medium |
| `photo-count-by-camera` | Photo Count By Camera | UserReport | slow |

`AdminReport` is gated to system admins only. `UserReport` is open to OrgAdmin, IncidentManager, Viewer, and GpsViewer roles. Volunteer / PhotoUploader / PhotoTagger are 403'd everywhere.

---

## Architecture (Phase 1)

```
┌──────────────────────────────────────────────────────────┐
│  Wps.Watch.AiReporting (ASP.NET Core 8)                  │
│                                                          │
│  REST          MCP stdio (mode-switched at startup)      │
│  Controllers   Tools                                     │
│       │             │                                    │
│       └──────┬──────┘                                    │
│              ▼                                           │
│        ReportRunner ──► IReportCatalog                   │
│              │             │                             │
│              ▼             ▼                             │
│        IUserContext   IReportDefinition × N              │
│        Accessor                                          │
│              │                                           │
│              ▼                                           │
│        DevAuth shim (Phase 1)   ──► JWT validation       │
│        / real JWT (later)            (HS256, deferred)   │
│                                                          │
│  Wps.Watch.AiReporting.Data                              │
│        WpsReplicaDbContext + narrow entities             │
│              │                                           │
└──────────────┼───────────────────────────────────────────┘
               ▼
        wpswatch-dev (Entra auth via `az login`)
        — eventual target: wpswatchprodreplica
```

---

## What's deferred from v5

These items from the [v5 requirements](../docs/ai-reports-requirements-v5.md) are *not* in Phase 1 of this repo, by design — they require cross-repo work or were de-scoped while we're pointed at devqa:

- **`ai_reports_reader` SQL principal with column-level DENYs** — the v5 §6 belt-and-suspenders. Phase 1 uses your Entra identity locally. Sensitive columns are kept out by "exclude by absence" in the EF entity definitions (`Wps.Watch.AiReporting.Data/Entities/`).
- **`dbo.vw_ai_*` curated views in the OLTP primary** — the queries here run directly against base tables. When the views land, the SQL strings in `Reports/Definitions/` swap to one-line `SELECT * FROM dbo.vw_ai_*` calls.
- **Replica-lag freshness gate** (`FR-V5-10`, `NFR-V5-04`) — devqa isn't a replica, so the `sys.dm_database_replica_states` query has nothing meaningful to report. Will be wired when we cut over to `wpswatchprodreplica`.
- **Real JWT validation** — the dev-shim middleware (`Authorization/DevAuthMiddleware.cs`) pretends every request is from a system admin. Real HS256 validation with the shared `JwtKey` from Key Vault is scaffolded but not wired.
- **Shared `Wps.Watch.Authorization` NuGet package** — role and operation constants are *copied* into `Authorization/Roles.cs` and `Authorization/ReportOperations.cs` for now. Consolidate later when we touch `wps.watch.api`.
- **React page in `wps.watch.web`** — out of scope for this repo.
- **Schema CI gate (`S-V5-MUST-40`)** — depends on the OLTP primary's schema repo.

---

## Cutting over to the production replica

When ready to point at `wpswatchprodreplica.database.windows.net`:

1. Get a database user mapped to either a managed identity (for production deploy) or your Entra identity (for verification).
2. Create `Wps.Watch.AiReporting/appsettings.Production.json` (gitignored) with overrides:
   ```json
   {
     "ConnectionStrings": {
       "WpsReplica": "Server=tcp:wpswatchprodreplica.database.windows.net,1433;Database=wpswatchprodreplica;Authentication=Active Directory Default;ApplicationIntent=ReadOnly;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;"
     },
     "DevAuth": { "Enabled": false }
   }
   ```
   (Or supply equivalent env vars: `ConnectionStrings__WpsReplica`, `DevAuth__Enabled=false`, `DOTNET_ENVIRONMENT=Production`.)
3. Wire the replica-lag gate (re-enable `Task 3` in the original plan).
4. Wire real JWT validation. The startup guardrail in `Program.cs` will refuse to start with `DevAuth:Enabled=true` outside Development — so deploying without flipping the flag is a startup error, not a silent fake-auth.
5. Coordinate the `dbo.vw_ai_*` view set + `ai_reports_reader` principal landing in the primary OLTP schema repo.

---

## Project layout

```
Wps.Watch.AiReporting/                 ASP.NET Core 8 web API + MCP server
├─ Program.cs                          Mode-switched entrypoint (web vs mcp)
├─ Controllers/
│  ├─ HealthController.cs              /health, /health/db
│  └─ ReportsController.cs             /api/reports, /api/reports/{id}/run
├─ Authorization/
│  ├─ Roles.cs                         Role name constants (copied)
│  ├─ ReportOperations.cs              Operation constants + role mapping
│  ├─ UserContext.cs                   Per-request identity + Can()
│  ├─ IUserContextAccessor.cs          DI accessor
│  ├─ DevAuthOptions.cs                Dev-shim config
│  └─ DevAuthMiddleware.cs             Sets the fake user per request
├─ Reports/
│  ├─ IReportDefinition.cs             Report contract
│  ├─ IReportCatalog.cs                Registry of all reports
│  ├─ ReportRunner.cs                  Auth gate + dispatch + audit log
│  ├─ ReportResults.cs                 Shape helpers, org-scope filter
│  ├─ ReportParameter.cs               Parameter schema
│  └─ Definitions/                     One file per report
└─ Mcp/
   └─ ReportTools.cs                   list_canned_reports, run_canned_report

Wps.Watch.AiReporting.Data/            EF Core data layer
├─ WpsReplicaDbContext.cs              Read-only context (NoTracking globally)
└─ Entities/                           Narrow entity projections

Wps.Watch.AiReporting.Test/            MSTest unit tests (21 passing)
```

---

## Testing

```bash
dotnet test
```

Current coverage focuses on the security-critical bits:
- `UserContextTests` — role gating (system admin / each role / volunteer-style 403)
- `ReportCatalogTests` — visibility filtering + duplicate-id detection
- `OrgScopeFilterTests` — SQL fragment generation, including the "no orgs = see nothing" denial case

Integration tests against devqa are not yet automated; the manual smoke is `curl http://localhost:5077/api/reports/{id}/run` for each report.
