# AI Reports — front end

The in-app **Region & Site Totals** report (and the Phase-2 "Ask AI Reports" chat),
recreated from the design handoff. React + TypeScript, built with Vite. It is served
**same-origin** by the ASP.NET app from `../wwwroot`, so there is no CORS and no second
server in production — and the whole thing lives inside this one repo.

It talks only to the existing REST API:
- `POST /api/reports/region-site-totals/run` — the report table (mapped to the
  prototype's row shape in `src/api.ts`).
- `POST /api/ask` — the chat (Claude → SQL → guarded `DiscoveryQueryRunner` → answer
  with the transparency block). See the backend `Chat/` folder.

## Develop (hot reload)

```powershell
# Terminal 1 — the API (from the repo root)
dotnet run --project Wps.Watch.AiReporting

# Terminal 2 — the Vite dev server (this folder)
cd Wps.Watch.AiReporting/ClientApp
npm install        # first time only
npm run dev        # http://localhost:5173, proxies /api → https://localhost:7202
```

`npm run dev` proxies `/api/*` to the running .NET app (https profile). Trust the dev
cert once with `dotnet dev-certs https --trust` so the proxy connects cleanly.

## Build (integrated serve)

```powershell
cd Wps.Watch.AiReporting/ClientApp
npm run build      # compiles into ../wwwroot
```

Then `dotnet run --project Wps.Watch.AiReporting` and open the app's root URL — the
built SPA is served from `wwwroot` alongside the API. `npm run typecheck` runs `tsc`.

## Enabling the chat

The chat calls the Claude API. Set a key on the **server** (never in the browser):

```powershell
cd Wps.Watch.AiReporting
dotnet user-secrets set "Anthropic:ApiKey" "sk-ant-..."
# or set the Anthropic__ApiKey environment variable
```

With no key, `/api/ask` returns `503 chat_not_configured` and the chat panel shows a
"not configured" message — the reports table works regardless. The model is
`claude-opus-4-8` by default; switch `Anthropic:Model` to `claude-sonnet-4-6` or
`claude-haiku-4-5` to trade quality for cost.

## Layout

- `src/main.tsx`, `src/App.jsx` — entry + composition (keeps the prototype's Tweaks
  panel during the pilot so a final layout combo can be chosen).
- `src/api.ts` — typed REST client + the backend→prototype column adapter.
- `src/useReportData.ts` — fetch hook for the report table.
- `src/components/` — ported from the design handoff (chrome, report table,
  Excel-style autofilters, icons, tweaks). `chat.jsx` is wired to `/api/ask`.
- `src/styles.css` — the design tokens / styling, verbatim from the handoff.

## Path to the wpsWatch website

The reusable assets here — the `styles.css` design tokens and the component
structure — port directly into `wps.watch.web` (React 17 + TypeScript) when the
report graduates from this pilot to the main site.
