# WPS Portfolio — prototype behaviour reference

Implementation-grade description of the "WPS Portfolio" prototype for the developers recreating it in React + ASP.NET Core inside this repository. It is derived from the design handoff bundle (`README.md`, `WPS Portfolio.dc.html` — template, logic and sample data — `support.js`, seven screenshots) and is written so that unit tests and Playwright specs can be taken from it directly.

Conventions used in this document:

- **Stored** = a field the app persists. **Derived** = computed at render time and never written. **Transient** = client-side UI state only.
- `TODAY` is `2026-09-23` in the prototype. Production uses the real clock; every date example below assumes the pinned value.
- Dates are ISO `yyyy-mm-dd` strings internally and are displayed as `d Mon yyyy` (`fmtD`): `2026-10-15` → `15 Oct 2026`; null/empty → `—`.
- Money: `usd(n)` = `'$' + Math.round(n).toLocaleString('en-US')` → `$36,900`.
- Tokens: `tok(n)`: `0`/falsy → `—`; `≥ 1e9` → `(n/1e9).toFixed(2)+'B'` (`1.22B`); `≥ 1e6` → `(n/1e6).toFixed(1)+'M'` (`198.4M`); else `Math.round(n).toLocaleString('en-US')`.
- `EMPTY(v)` is true for `null`, `undefined`, `''`, `'—'` (em dash) and `'–'` (en dash). It drives grey text, sum-row "missing" counts and the filter value list.
- Colours, typography and spacing are specified in the handoff `README.md` ("Design Tokens") and are not repeated here except where a colour carries state meaning.
- Copy in **bold-quoted** form in sections 2 and 3 is exact and should be copied verbatim (including `·` U+00B7, `—` U+2014, `→` U+2192, `⛓`, `Σ`, `▸`/`▾`, `↗`, curly apostrophes where shown).
- The prototype runtime maps `onChange` to React's `onChange` (fires per keystroke on text inputs, on commit for `select`/`date`/`checkbox`), and `onBlur` to blur. Persistence rules below say which event triggers a save.

Handoff bundle location during this session: `/tmp/claude-0/-home-user-wps-watch-ai-reporting/0ff003d4-953f-5824-85b5-ff3a21eb7900/scratchpad/upload/design_handoff_wps_portfolio/`.

---

## 1. Data model

### 1.1 Enumerations and constants

| Name | Values (in order) | Notes |
|---|---|---|
| `Type` | `Software`, `Implementation` | Required on create. |
| `Designation` (the column labelled **Status**) | `Active`, `Ongoing`, `Future`, `Experimental`, `Complete` | Required on create. Stored value; never `At Risk`. |
| Display status order (`DESIG_ORDER`) | `At Risk`, `Active`, `Ongoing`, `Future`, `Experimental`, `Complete` | Used for sorting the Status column. |
| `OPEN` view set | `At Risk`, `Active`, `Ongoing`, `Future`, `Experimental` | Default Status filter. |
| `STEPS.Software` | `Discovery`, `Design`, `Prototype Approved`, `Code`, `Final Testing`, `Release` | Index 0–5. Index 2 is the approval gate. |
| `STEPS.Implementation` | `Discovery`, `Design`, `Project Approved`, `Implement`, `Final Testing`, `Deploy` | Index 0–5. Index 2 is the approval gate. |
| `STEP_ORDER` (sort order across both types) | `Discovery`, `Design`, `Prototype Approved`, `Project Approved`, `Code`, `Implement`, `Final Testing`, `Release`, `Deploy` | Sort value = index; unknown → -1 (sorts last). |
| Task status | `Planned`, `In Progress`, `Complete` | Same set for tasks and sub-tasks. |
| Task source | `Manual`, `Jira` | Prototype literal is `'Manual'`; UI label is **typed**. |
| Repo entry scope | `all`, `label` | UI labels **all issues** / **label:**. |
| GitHub issue state / reason | `state`: `open`, `closed`; `reason`: `null`, `completed`, `not_planned` (any other non-`completed` reason is treated like `not_planned`) | Read-only, from GitHub. |
| Work-summary state | `idle`, `loading`, `cached`, `failed`, `norepo` | See 2.16. |
| Save state (per `<projectId>:<fieldKey>`) | `inflight`, `saved`, `failed` (absent = idle) | See 2.9. |
| Role | `Admin` (= `Roles.SystemAdmin`, string `"System Admin"`), `Viewer` (= `Roles.Viewer`, string `"Viewer"`) | From `Authorization/Roles.cs`. |
| Feed status | `green`, `amber`, `red` (+ paused) | Labels **OK** / **Degraded** / **Failed** / **Paused**. |
| Run result | `OK`, `Partial`, `Failed` | |
| Feed schedule | `Every hour`, `Every 6 hours`, `Nightly 06:00` (option label **Nightly · 06:00**), `Weekly · Monday 06:00` | Stored as the option *value*. |
| Alert threshold | `'1'`, `'2'`, `'3'`, `'5'` (strings) | Default `'3'`. Stored only; not evaluated in the prototype. |
| Ask model | `Claude Sonnet 4.5`, `Claude Opus 4.1`, `Claude Haiku 4.5` | Default `Claude Sonnet 4.5`. |
| Departments (sample) | `Dev`, `Field`, `Content`, `Executive`, `Administration`, `External` | Editable list. |
| Chain colours | `#b88c3a`, `#2f8a4f`, `#5a6b7e`, `#b8442e`, `#7b5ea7`, `#c98a1e` | Index `i % 6`. |
| Chain row tints | `#fcf6e8`, `#eef6f0`, `#eef1f4`, `#faeceb`, `#f2eef7`, `#fbf1e0` | Same index. |
| `GH` | `https://github.com/wildlife-protection-solutions/` | Issue/repo URL prefix. |
| `JIRA_URL` | `https://wps.atlassian.net/browse/` | Task key chip. |
| Jira project URL | `https://wps.atlassian.net/jira/software/projects/<KEY>/issues` | Tasks heading link and Integrations table. |
| Filter "none selected" sentinel | `'\u0000none'` | See 2.5. |

### 1.2 Project (`initiative`)

| Field | Type | Stored? | Default (new draft) | Rules |
|---|---|---|---|---|
| `id` | int | stored | next id | |
| `title` | string | stored | `''` | Required to create. Column **Project Name**. Empty shows **Untitled** (saved) / **New project** (draft) in `#9c9a8e`. |
| `owner` | string | stored | `''` | Required to create. Free text (not a person FK in the prototype). |
| `type` | `Type` \| `''` | stored | `''` | Required to create. Changing it re-maps `step` by index (2.11). |
| `designation` | `Designation` \| `''` | stored | `''` | Required to create. Displayed as **Status**; overridden by **At Risk** for display only. |
| `step` | step name | stored | `'Discovery'` | Must be a member of `STEPS[type]`. |
| `approved` | bool | stored | `false` | Approval-gate checkbox on step index 2. |
| `started` | ISO date \| null | stored | `TODAY` | Column **Start Date**. |
| `nextRelease` | ISO date \| null | stored | `TODAY + 42 days` | Column **Next Release**. |
| `public` | bool | stored | `false` | Public website feed toggle. |
| `desc` | string | stored | `''` | **Project Description** textarea. |
| `status` | string | stored | `''` | **Current Status** textarea (narrative; not the enum). |
| `next` | string | stored | `''` | **Next Step** textarea; column **Next Step**. |
| `links` | `{ [stepIndex 0..5]: Link[] }` | stored | `{}` | Step links. Index 2 never has links (approval gate). |
| `tasks` | `Task[]` | stored | `[]` | Ordered; order is significant (chains). |
| `repoEntries` | `RepoEntry[]` | stored | `[]` | |
| `gh` | `GhIssue[]` | read-only cache from GitHub | `[]` | Hourly poll + nightly full pass. |
| `recent` | `{ state, generated? }` | stored cache | `{ state: 'idle' }` | Work Summary. `generated` is a display string (`'21 Sep 2026, 06:10'`). |
| `summary` | `Segment[]` (`{ t: string }` or `{ link: repoName }`) | stored cache | absent | Work Summary body; links render as `GH + repoName`. |
| `autoStatus` | bool | stored | `false`/absent | "Auto-fill Current Status from summary". |
| `isDraft` | bool | transient | `true` on new | Drafts are not saved, not filtered, not summed. |
| `recentFails`, `recentError` | – | sample-only | – | Simulate a failed generation (project 3). |
| **derived** `atRisk` | bool | derived | | `nextRelease != null && nextRelease < TODAY && designation !== 'Complete'`. |
| **derived** displayed Status | string | derived | | `atRisk ? 'At Risk' : designation`. |
| **derived** `inProg`, `planned`, `done` | int | derived | | Counts of tasks by **rolled-up** status (2.13.1). |
| **derived** resolved repos | string[] | derived | | `unique(flatMap(repoEntries, resolve(pattern)))` — all entries, sync on or off. |

### 1.3 Link

`{ name: string, url: string }`. `name` may be empty in storage; display uses `name || url`. Used for step links, task links and sub-task links.

### 1.4 Task

| Field | Type | Stored? | Rules |
|---|---|---|---|
| `id` | int | stored | Unique across tasks and sub-tasks (single id sequence in the prototype). |
| `title` | string | stored | Editable inline for `Manual`; read-only for `Jira`. |
| `status` | Task status | stored | Editable for `Manual` and not rolled-up; read-only for `Jira` or rolled-up. |
| `source` | `Manual` \| `Jira` | stored | |
| `key` | string | stored | Jira key e.g. `AIDASH-134`; `''` for manual. |
| `start` | ISO date \| null | stored | Null when `Complete`; null when chained and following. |
| `days` | int ≥ 1 \| null | stored | Null when `Complete`. New task default 5. |
| `chain` | bool | stored | "start when the previous task ends". |
| `links` | `Link[]` | stored | |
| `subtasks` | `SubTask[]` | stored | |
| **derived** `rStart`, `rEnd` | ISO \| null | derived | Resolved start/end (2.13.2). |
| **derived** `chainId`, `isAnchor`, `colorIdx` | | derived | Chain grouping and colour (2.13.3). |
| **derived** `rolled` | bool | derived | True when the task has ≥ 1 sub-task; status/dates then come from sub-tasks (2.14). |

### 1.5 SubTask

`{ id, title, status, start, days, chain, links[] }` — same semantics as Task minus `source`, `key`, `subtasks`. New sub-task default: `status` from the add-row select (default `Planned`), `start = TODAY` (null if `Complete`), `days = 3`, `chain = <list already has a non-Complete sub-task>`.

### 1.6 RepoEntry

| Field | Type | Default | Rules |
|---|---|---|---|
| `id` | int | | |
| `pattern` | string | | Exact repo name or prefix ending in `*`. Trimmed. Case-sensitive. |
| `scope` | `all` \| `label` | `all` | |
| `label` | string | `''` | Only meaningful when `scope === 'label'`; compared trimmed. |
| `sync` | bool | `true` | Off = entry still resolves for Work Summary/Integrations counts but contributes no issues. |

### 1.7 GhIssue (read-only)

`{ id: '<repo>#<num>', num, repo, title, state, reason, labels: string[], assignees: string[] (initials), updated: ISO datetime, children: GhChild[] }`
`GhChild = { num, title, repo, state, reason, assignees: string[], unreadable?: true }`. An unreadable child renders as the placeholder row (2.15.7).

### 1.8 Person (Settings › People / AI)

| Field | Type | Notes |
|---|---|---|
| `id`, `name`, `email` | | Sample emails are `first.last@wps.example` (`@partner.example` for `External`). |
| `dept` | string | FK by **name** to Departments; renames cascade (2.21.2). |
| `role` | `Viewer` \| `Admin` | |
| `active` | bool | Inactive people are excluded from every AI figure. |
| `inactiveSince` | ISO \| null | Set to `TODAY` on Mark inactive; cleared on Reactivate. |
| AI figures (sample, per 30 days) | `claude` $, `tokens`, `gpt` $, `codex` $, `model`, `gDays`, `gApps` (comma list), `lastClaude`, `lastGpt`, `lastGemini` | In production these come from the AI Reports usage record per period, not from the person row. |

### 1.9 Other entities

| Entity | Shape |
|---|---|
| Department | `{ id, name }`; derived `count` = active people whose `dept === name`. |
| Pricing row | `{ id, model, input ($/M tokens), output ($/M tokens), from (ISO) }`. |
| Jira project link | `{ id, key (upper-case), initId (project id \| ''), sync (display string) }`. |
| Warden rule | `{ id, rule (plain English), str (JSON string) }`; derived `bad = !jsonOk(str)` where `jsonOk('')` is **true** and otherwise `JSON.parse` must succeed. |
| Feed | static `[name, status, lastSuccessfulRun, note]` + `FEED_META[name] = [schedule, source, credentialMasked, credentialMeta]`. |
| Feed config (`feedCfg[name]`) | `{ enabled: true, schedule, credential, credentialMeta, alerts: true, runs: Run[], running: false, runMsg: '', rotating: false, newKey: '' }`. |
| Run | `{ when, result, duration, records, message }` (display strings). |
| Alerts config | `{ recipients: string[], threshold: '3', includeDegraded: false, acked: { [feed]: true }, retry: { [feed]: 'running' \| 'failed' } }`. |
| Ask config | `{ model: 'Claude Sonnet 4.5', instructions: 'Answer with rows, not prose. Name the page each row came from. Never estimate value or compare people to a target.' }`. |
| Table state (per table id) | `tbl[id] = { filters: { [colKey]: { text: '', selected: string[] } }, sort: { key, dir: 'asc' \| 'desc' } \| null }`; `colW[id]: number[]` (fr weights); `colFix[id]: { [i]: px }`; `pop: { tid, key, top, left, width, search } \| null`. `filters[k].text` exists but is vestigial (never set by the UI). |
| AI range | `{ preset: 7 \| 30 \| 90 \| 'custom', start, end }`; default `{ 30, TODAY-29, TODAY }`. |
| Session | `page` (`portfolio` \| `ai` \| `ai-detail` \| `ask` \| `settings` \| `feed`), `signedIn`, `role`, `menuOpen`, `aiTab`, `feedName`. |

Table ids: `portfolio`, `ai`, `det-Claude` / `det-ChatGPT` / `det-Gemini` (filters and sort are per vendor tab) with **shared** width state under key `det`.

### 1.10 Sample dataset summary (only project names are real)

12 projects (`id`: title · owner · type · designation · step · approved · started · nextRelease · public · tasks · link step indexes · repo entries · Jira):

| id | Project | Owner | Type | Designation | Step | Appr. | Started | Next Release | Public | Tasks | Links at | Repo entries | Jira |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 1 | SERCA Desktop | Priya Raman | Software | Active | Code | ✓ | 2026-03-02 | 2026-10-15 | ✓ | 10 | 0,1,3 | `serca-desktop` all, sync on | SERCA |
| 2 | AI Usage Dashboard | Tomás Herrera | Software | Active | Code | ✓ | 2026-05-11 | 2026-11-02 | – | 7 | 0,1,3 | `ai-usage-dashboard` all, on | AIDASH |
| 3 | Akili Completion | Lena Fischer | Software | Active | Final Testing | ✓ | 2026-01-19 | 2026-09-30 | ✓ | 8 | 0,1,3,4 | `akili-completion` all, **off** | AKILI |
| 4 | V.3 of RAD Box | Daniel Mwangi | Implementation | Active | Deploy | ✓ | 2025-11-03 | **2026-09-15** (At Risk) | ✓ | 7 | 0,1,3,5 | – | – |
| 5 | Installation for ARGUS | Sofia Almeida | Implementation | Active | Implement | ✓ | 2026-04-06 | – | – | 7 | 0,1,3 | – | – |
| 6 | Acoustic Monitoring v2 Testing | Kwame Boateng | Implementation | Experimental | Final Testing | ✓ | 2026-02-16 | – | – | 5 | 0,4 | – | – |
| 7 | Data Center Migration to Azure | Ingrid Solberg | Implementation | Active | Design | ✗ | 2026-08-03 | – | – | 6 | 0 | – | – |
| 8 | WPS Product Videos | Marcus Lee | Implementation | Ongoing | Deploy | ✓ | 2025-06-02 | – | – | 6 | 5 | – | – |
| 9 | Web Site Redevelopment | Aisha Khan | Software | Ongoing | Release | ✓ | 2025-09-01 | – | ✓ | 6 | 1,5 | – | WEB |
| 10 | Partner Training and Certification | Noor Haddad | Implementation | Future | Discovery | ✗ | – | – | – | 0 | – | – | – |
| 11 | SpeciesNet Impact Assessment | Elena Petrova | Software | Complete | Release | ✓ | 2025-10-06 | – | ✓ | 4 (all Complete) | 0,5 | – | – |
| 12 | wpsWatch 2.0 | Tomás Herrera | Software | Active | Code | ✓ | 2026-06-01 | 2026-12-10 | ✓ | 3 | 1 | `wps.watch.*` all on · `wpsWatch-meta` label:`epic` on · `wps.watch.legacy.*` all on (→ no repos match) | – |

Sample-data derivations worth reproducing in a seed fixture:

- **Jira tasks**: for projects 1, 2, 3, 9 every task at an even index (0, 2, 4 …) has `source = 'Jira'`, `key = <KEY>-<120 + i*7>`.
- **Task dates** (applied to every project, `i` = task index, `DAYS = [5,8,3,10,6,4,12,7]`): `Complete` → `start = days = null, chain = false`; otherwise `days = DAYS[i % 8]`; `In Progress` → `start = TODAY − (3 + i)`, `chain = false`; the first `Planned` task → `start = TODAY + 4`, `chain = false`; every later `Planned` task → `start = null`, `chain = true`. Exception: SERCA's "Field test with Kruger team" is unchained with `start = TODAY + 30`.
- **Sub-tasks**: SERCA "Offline map tile cache" (1 link; sub-tasks: "Tile schema and storage budget" Complete 2d; "Cache eviction policy" In Progress 3d start TODAY−2; "Sync on reconnect" Planned 4d chained; "Field test at two sites" Planned 5d chained). AI Usage Dashboard "Common usage record across vendors" (2 links; sub-tasks: "Define the five-column record" Complete 1d; "Map Claude fields" In Progress 2d start TODAY−1; "Map Gemini fields" Planned 3d chained; "Map OpenAI fields" Planned 2d chained) → rolls up to **In Progress**, start 22 Sep 2026, 7 days, end 29 Sep 2026.
- **Work Summary**: project 2 starts `cached` (`generated: '21 Sep 2026, 06:10'`); project 3 is wired to fail with `GitHub API returned 502 for akili-completion`; all others `idle`.
- **GitHub issues**: project 1 has 31 top-level issues in `serca-desktop` (26 epics + "Maintenance" 22/9 + "Security intake" 27/15 + two closed-completed epics + "Tablet layout" closed `not_planned`); project 2 has 6 epics in `ai-usage-dashboard` (one closed-completed); project 3 has 3 issues but no synced entry (section hidden); project 12 has 15 issues across `wpsWatch-meta` and `wps.watch.*` repos, including "Partner API v2" whose 6th child is `unreadable`. Children are generated deterministically (seeded LCG, seed 17) — do not try to reproduce exact numbers; reproduce the shapes.
- **Organisation repos** (`ORG_REPOS`, 23): `serca-desktop, ai-usage-dashboard, akili-completion, wpsWatch-meta, wps.watch.api, wps.watch.web, wps.watch.ingest, wps.watch.alerts, wps.watch.ai_reporting, wps.watch.auth, wps.watch.mobile, wps.watch.warden, wps.watch.exports, wps.watch.partner-api, wps.watch.infra, wps.watch.docs, wps.watch.sdk, wps.watch.notifications, wps.watch.search, argus-site-tools, radbox-firmware, acoustic-v2-bench, wps-website`. `wps.watch.*` resolves to 15 repos.
- **People** (12; figures are per 30 days): Priya Raman Dev/Admin $6,000 198.4M tokens ChatGPT $32 Codex $420 Opus 4.1 Gemini 14 d "Gmail, Docs"; Tomás Herrera Dev/Viewer $5,400 176.9M $12 $380 Sonnet 4.5 9 d "Gmail, Meet"; Lena Fischer Dev $4,900 161.2M $0 $260 Opus 4.1 0 d; Kwame Boateng Dev $4,200 139.8M $28 $190 Sonnet 4.5 11 d "Docs, Sheets"; Daniel Mwangi Field $3,800 124.1M $60 $0 Sonnet 4.5 16 d "Gmail, Docs, Meet"; Sofia Almeida Field $3,300 108.6M $45 $0 Haiku 4.5 7 d "Gmail"; Marcus Lee Content $2,900 95.3M $110 $0 Sonnet 4.5 18 d "Docs, Slides, Meet"; Aisha Khan Content $2,400 79.4M $95 $156 Sonnet 4.5 12 d "Docs, Gmail"; Ingrid Solberg Executive/Admin $1,800 58.9M $88 $0 Opus 4.1 9 d "Gmail, Meet"; Noor Haddad Administration $1,200 40.2M $74 $0 Haiku 4.5 0 d; Elena Petrova External $1,000 33.7M $40 $0 Sonnet 4.5 4 d "Docs"; Owen Gallagher External $0 0 $20 $0 model `—` 0 d. Totals for 30 days: Claude $36,900 · 1.22B tokens · 11 people; ChatGPT $604 · Codex $1,406 · 12 people; Gemini 9 active users · 100 active days · "most used in Gmail, Docs, Meet".
- **Signed-in sample user**: Ingrid Solberg, `ingrid.solberg@wps.example` (the Viewer-scoped Ask answer uses her row).
- **Pricing**: Claude Opus 4.1 15 / 75; Claude Sonnet 4.5 3 / 15; Claude Haiku 4.5 1 / 5; all effective from 2026-09-01.
- **Jira projects**: SERCA→1, AIDASH→2, AKILI→3, WEB→9; last sync `23 Sep 2026, 06:20`.
- **Feeds** (10): Claude green `23 Sep 2026, 06:00` "Usage and cost by API key"; ChatGPT cost green `23 Sep 2026, 06:05` "Billing export"; ChatGPT usage **red** `10 Sep 2026, 06:05` "13 consecutive runs failed with 401. Usage figures are stale."; Codex **amber** `23 Sep 2026, 06:05` "The last two runs returned partial data"; Gemini green `23 Sep 2026, 05:40` "Workspace Reports API — activity, apps and last-active per user"; GitHub green `23 Sep 2026, 06:20` (note computed, 2.23.4); GitHub Issues green `23 Sep 2026, 06:20` (computed); Jira green `23 Sep 2026, 06:20` "Connected"; Copilot **red** `12 Aug 2026, 06:00` "Dark since 12 Aug — token expired"; WPS Agents **amber** `22 Sep 2026, 03:10` "Last run late — 27 h ago".
- **Feed meta** `[schedule, source, credential, credentialMeta]`: Claude `Nightly 06:00` · `Anthropic Admin API · usage and cost by API key` · `sk-ant-admin-…4f2c` · `Rotated 1 Aug 2026`; ChatGPT cost `Nightly 06:00` · `OpenAI billing export (CSV)` · `oai-bill-…9d1e` · `Rotated 1 Aug 2026`; ChatGPT usage `Nightly 06:00` · `OpenAI Compliance API` · `oai-comp-…77a0` · `Rotated 12 Mar 2026 · rejected since 10 Sep`; Codex `Nightly 06:00` · `OpenAI billing export (Codex line items)` · `oai-bill-…9d1e` · `Shared with ChatGPT cost`; Gemini `Nightly 06:00` · `Google Workspace Reports API` · `svc-reports@wps…iam` · `Service account · rotated 15 Jul 2026`; GitHub `Every 6 hours` · `GitHub App · wildlife-protection-solutions` · `ghs_…b21f` · `Installation token · auto-renews`; GitHub Issues `Every hour` · `GitHub REST · issues and sub-issues · org wildlife-protection-solutions` · `GITHUB_TOKEN ghp_…4c9e` · `Expires 12 Nov 2026 · shared with the GitHub commits feed`; Jira `Every 6 hours` · `Jira Cloud REST · wps.atlassian.net` · `ATATT…c3e8` · `Rotated 2 Jun 2026`; Copilot `Nightly 06:00` · `GitHub Copilot usage API` · `ghp_…08aa` · `Expired 12 Aug 2026`; WPS Agents `Every hour` · `Internal agent run log` · `internal` · `No credential`.
- **Warden rules**: (1) "If a project date is overdue, send an email to the project owner." / `{"trigger":"project.nextRelease < today","action":"email","to":"project.owner"}`; (2) "If a project has not been edited in 30 days, flag it at the fortnightly review." / `{"trigger":"project.lastEdited > 30d","action":"flag","where":"portfolio.review"}`; (3) "If a data feed fails three runs in a row, email ops." / `{"trigger":"feed.failedRuns >= 3","action":"email","to":"ops@wps.example"}`.
- **Alert recipients**: `ops@wps.example`, `ingrid.solberg@wps.example`; threshold `'3'`; `includeDegraded = false`.
- **Initial UI state** in the prototype: project 2 expanded with Tasks, GitHub Issues, Work Summary and Repos open, and a baked-in failed save `'2:nextRelease': 'failed'` (so the screenshot shows **Couldn’t save · Retry**).

---

## 2. Derived rules and algorithms

### 2.1 Date arithmetic

- `addDays(iso, d)`: add `d` calendar days in UTC (noon-anchored to avoid DST drift). `addDays('2026-09-23', 42) = '2026-11-04'`.
- `daysBetween(a, b)`: `round((b − a) / 86 400 000) + 1` — **inclusive** day count. `daysBetween('2026-09-22','2026-09-29') = 8`.
- A task's **end** = `addDays(start, days)` (exclusive-style: 5 days from 20 Sep → 25 Sep). Roll-up days = `max(1, daysBetween(start, end) − 1)` so that `end = addDays(start, days)` still holds.
- Date comparisons use ISO string ordering.

### 2.2 At Risk (derived, never stored)

```
isPastDue(p) = p.nextRelease != null && p.nextRelease < TODAY && p.designation !== 'Complete'
```

Effects when true: Status cell text **At Risk** in `#b8442e` weight 600 (select still shows the stored designation when editing); Next Release cell text `#b8442e` plus tag **PAST DUE** (rendered from `'past due'` with `text-transform: uppercase`); row background `#f6dcd5` (hover `#f0cdc3`), expanded `#eec3b8` (hover `#e8b5a8`); the filter value list and CSV use `At Risk` as the Status value; the Status column sorts it first; the Open view includes it. `TODAY` itself is not past due. Equality with TODAY → not at risk. Tests: `nextRelease = TODAY−1, Active` → true; `TODAY−1, Complete` → false; `null` → false; `TODAY` → false.

### 2.3 Portfolio rows, search, views

- Row source: saved projects only (`!isDraft`) flow through the table machinery (filters, search, sort, sums). Drafts are prepended above the result, unfiltered and unsorted, and excluded from the sum row.
- **Search** (header input, placeholder **Search names, owners, descriptions**): case-insensitive substring over `[title, owner, desc, status, next].join(' ')`; applied after the column filters (`extraFilter`). It does not feed the filter popover value lists and does not count towards "Clear filters (n)".
- **Open / Complete segment**: clicking sets the Status filter to exactly `OPEN` (5 values) or `['Complete']` and clears its text. A segment is highlighted only when the current Status selection equals that set exactly (same length and every member present); after a manual filter edit or "Clear filters" neither segment is highlighted.
- The default state is the Open view; because it is a real filter, the header shows **Clear filters (1)** on first load and clicking it reveals Complete projects too.
- Empty state: **No projects match these filters.**

### 2.4 Sum row (recomputed from the filtered + searched rows, drafts excluded)

| Column | Text | Colour |
|---|---|---|
| Project Name (`count`) | `${rows.length} items` | default |
| Owner, Type, Status, Step (`distinct`) | `${rows.filter(r => !EMPTY(display(r))).length} items` — a **non-empty count**, not a distinct-value count despite the name | default |
| Start Date | `n not started` where `n = rows.filter(r => !r.started \|\| r.started > TODAY).length`; `all started` when `n = 0` | `#9c9a8e` either way |
| Next Release | `n past due` (`n = rows.filter(isPastDue).length`) in `#b8442e`; `none past due` in `#9c9a8e` | |
| Next Step | `n missing` (`EMPTY(r.next)`) or `all set` | `#9c9a8e` either way |
| AI money columns (`total`) | `usd(sum) + ' total'`, e.g. `$36,900 total` | default |
| AI Gemini | `${sum} days total` | |
| AI detail Usage | tokens → `tok(sum) + ' total'` (`1.22B total`); Gemini → `${sum} active days total`; ChatGPT → no sum | |
| AI detail Cost | `usd(sum) total`; omitted (empty) on the Gemini tab | |

Lead cell **Σ** in `#9c9a8e` (Portfolio only; AI tables have no lead column).

### 2.5 Column filters (Excel-style)

State per column: `{ text: '', selected: string[] }`. `selected = []` means **no filter (all pass)**. `selected = ['\u0000none']` (the `NONE` sentinel) means **nothing passes**.

- **Match**: `filterPass(row, exceptKey)` = for every column `c ≠ exceptKey` with `selected.length > 0`: `selected.includes(display(c, row))`, where `display` is the column's formatted string (`fmtD` for dates, `At Risk`/designation for Status, `usd`/`—` for money, `n days` for Gemini). Filtering is therefore on **displayed strings**.
- **Value list** for column `k`: rows = raw rows passing every *other* column's filter (search box ignored, drafts absent); values = distinct non-`EMPTY` display strings with counts. Empty values cannot be selected. Ordering: if the column has numeric sort values (Status, Step, money, Gemini, Usage) sort by that number ascending; date columns sort by raw ISO string; otherwise `a.localeCompare(b, undefined, { numeric: true })`.
- Popover search (**Search values…**): case-insensitive substring on the label; only affects what is listed. Empty → **No matching values**. Count line: `${opts.length} value` / `values`.
- `noneSel = selected.length === 1 && selected[0] === NONE`; `allSelected = !noneSel && (selected.length === 0 || vals.every(v => selected.includes(v)))`.
- Header checkbox label: **All values** when `allSelected`, else **Select all**. Toggling it: `allSelected ? selected = [NONE] : selected = []`.
- Row checkbox `checked = allSelected || (!noneSel && selected.includes(v))`. Toggling value `v`: if `allSelected` → `selected = vals.filter(x => x !== v)` (materialises the full list minus `v`); else if `noneSel` → `[v]`; else toggle membership and, if the result is empty, `[NONE]`.
- Header filter button: gold `#b88c3a` background with a white ▾ when the column has a filter (`selected.length > 0 || text`); the 7px gold dot (1.5px white ring, top-right) when the column has a filter **or** is the sorted column.
- **Clear filter & sort** (footer): `selected = [], text = ''` and `sort = null` if the sort is on this column. Disabled/grey when neither applies. **Done** closes.
- **Clear filters (n)** header button: `n` = number of columns with `selected.length > 0` + (1 if a sort is set). Clears all filters (including the default Status filter) and the sort.
- Popover geometry: fixed-position, `width = 260`, `left = max(8, min(headerCell.right − 260, innerWidth − 268))`, `top = min(headerCell.bottom + 4, max(8, innerHeight − 400))`; `max-height 360`. Closes on `mousedown` outside `[data-fpop]`, on `Escape`, on any `scroll` event (capture phase) whose target is outside the popover, on page navigation, and when switching AI detail tab. Clicking the same header's ▾ again toggles it closed.
- Popover sort rows: **▲ Sort A → Z** / **▼ Sort Z → A**, or for `kind: 'number' | 'date'` columns **Sort smallest → largest** / **Sort largest → smallest**. They set the sort directly and close the popover.
- The header label button cycles sort: none → `asc` → `desc` → none (per column; sorting another column starts at `asc`). Sort mark `↕` `#b9b3a4` unsorted; `↑`/`↓` `#b88c3a` sorted.

### 2.6 Sort semantics

```
value(c, r) = c.sortValue ? c.sortValue(r[c.key], r) : r[c.key]
empty(v)    = v == null || v === '' || v === -1
compare(a,b): empty both → 0; empty(a) → +1; empty(b) → −1   // empties always last, both directions
              both numbers → (a − b) * dir
              else String(a).localeCompare(String(b), undefined, { numeric: true }) * dir
```

Column sort values: Status → `DESIG_ORDER.indexOf(displayStatus)` (At Risk = 0 sorts first ascending); Step → `STEP_ORDER.indexOf(step)`; dates → raw ISO string; Project Name/Owner/Type/Next Step → raw string; money/Gemini/Usage → number. Sorting is stable relative to the seed order (`Array.prototype.sort` on a copy).

### 2.7 Column resizing

- Default fr weights: Portfolio `[230,140,120,130,120,130,140,250]` with mins `[150,90,84,110,84,108,110,150]` and a fixed `40px` lead track; AI `[200,160,120,130,120,120,120]`; AI detail `[200,160,200,120,150]`. Columns without an explicit `min` use 72.
- Track for column `i`: if `colFix[i]` is set **and** `i < n−1` → `${px}px`; otherwise `minmax(${max(min, colFix[i] ?? min)}px, ${weight}fr)`. So the last column always keeps filling and a drag there only raises its minimum.
- Drag: `mousedown` on the 8px handle (right edge, `right:-4px`), `preventDefault + stopPropagation`; `startPx` = the header cell's rendered width; on each `mousemove` `px = max(min, round(startPx + clientX − startX))`; on `mouseup` remove listeners. While dragging `body.cursor = col-resize`, `body.user-select = none`.
- AI detail shares one width set (`det`) across the three vendor tabs.
- Resizing is not persisted (session state only in the prototype).

### 2.8 CSV export

- Table = header row (visible column labels) + the current filtered/sorted rows, each cell being the **displayed** string (`fmt`), e.g. `At Risk`, `15 Oct 2026`, `—`, `$6,000`, `14 days`.
- Serialisation: prefix `﻿` (BOM); each cell → `s = String(v ?? '')`; if `/^[=+@-]/.test(s)` prepend `'`; then `"` + `s.replaceAll('"', '""')` + `"` — **every** cell quoted; cells joined with `,`; rows joined with `\n` (LF). MIME `text/csv`.
- File names: `wps-portfolio`, `wps-ai-by-person`, `wps-ai-claude` / `wps-ai-chatgpt` / `wps-ai-gemini`, `wps-ask-answer`; the prototype appends `-sample-data.csv` — production should append `.csv` only.
- Ask CSV uses the answer's header texts and cell texts.
- The service's `Discovery/CsvExporter.cs` also guards leading `\t` and `\r`; align the client with the server (guard `= + - @ \t \r`).

### 2.9 Save-status state machine (per project field key)

- Keys are `"<projectId>:<field>"`; fields used: `title, owner, type, designation, step, started, nextRelease, desc, status, next, links, approved, tasks, task<taskId>, autoStatus, repos`.
- Trigger: text inputs/textareas → `onChange` updates the model, `onBlur` saves; selects and checkboxes → save on change; date inputs → change updates, blur saves; tasks/sub-tasks/links/repo entries → save on each mutation (`tasks`, `links`, `repos`). Drafts never save (`save()` returns early while `isDraft`).
- Transitions: `save(k)` → clear any pending timer for `k`, set `inflight`; after the request (700 ms in the prototype) → `saved` (or `failed` when `simulateSaveFailures`) ; `saved` auto-clears after **1600 ms**; `failed` persists until **Retry**.
- Row status line aggregates every key of the row **except `repos`**: any `failed` → **Couldn’t save** `#b8442e` + **· Retry** (link `#8a6524`, underlined); else any `inflight` → **Saving…** `#6b6a63`; else any `saved` → **Saved** `#2f8a4f`; else empty. **Retry** re-saves only the keys currently `failed`.
- The `repos` key shows the same texts right-aligned in the Repos section heading (`Saving…` / `Saved` / `Couldn’t save`, no Retry link).
- No animation other than the loading pulse.

### 2.10 Drafts, create, delete

- **+ New project** (disabled at 50 % opacity while any draft exists) inserts `{ isDraft: true, title '', owner '', type '', designation '', step 'Discovery', approved false, started TODAY, nextRelease TODAY+42, public false, tasks [], links {}, repoEntries [], gh [], desc '', status '', next '', recent { state: 'idle' } }` at the **top** of the list and expands it.
- Draft row: Project Name cell reads **New project** grey; editors show `Select…` placeholders for Type/Status; the fields block (tweak off) marks required labels with a red ` *`.
- **Create project** enabled only when `title && owner && type && designation` are non-empty; on click `isDraft = false` and the Tasks section opens. **Discard** removes the draft.
- Draft panels show Steps (once Type is set), the narrative and Repos, but not the save line's **Delete project**, Tasks or Work Summary (all gated on `isSaved`). The GitHub Issues section is gated only on synced repo entries, so a draft that already has a synced entry shows it with the empty text.
- **Delete project** (saved rows) → confirmation box; **Keep** cancels; **Delete project** removes the project, sets `initId = ''` on any Jira project linked to it (unlinks, keeps the Jira row) and collapses it.

### 2.11 Steps, approval gate, auto-advance

Let `steps = STEPS[type] || STEPS.Software`, `idx = max(0, steps.indexOf(step))`, `maxLinked` = highest step index whose `links[index]` is non-empty (−1 if none).

- Rendering per index `i`: rule colour `#b88c3a` if `i === idx` else `#b9b3a4`; circle filled `#b88c3a`/white text when `i <= idx` (the **current** step is filled too and shows its number), white with `#b9b3a4` border and `#6b6a63` number when `i > idx`; mark `✓` when `i < idx` else `String(i+1)`; name weight 600 only at `idx`; name colour `#1a1a2e` for `i <= idx`, `#6b6a63` after.
- Index 2 shows the **Approved** checkbox instead of links; every other index lists links + **+ Link**.
- `autoStep(p)` = `steps[min(target, 5)]` where `target = approved ? max(idx, 2, maxLinked) : max(idx, min(maxLinked, 1))`. Consequences: adding a link never moves the step backwards; before approval links can pull the step forward only as far as Design (1); approving jumps to at least the approval step (2) and to the highest linked step if higher.
- Saving/adding a link recomputes `step = autoStep(p)` (save key `links`). Deleting a link does **not** recompute.
- Toggling **Approved** on → `step = autoStep({...p, approved: true})`; off → `step = steps[min(idx, 1)]` (never beyond Design). Save key `approved`.
- Clicking a step header sets `step` directly (save key `step`) with **no** gate check — a user may select Code while unapproved; the next link save will not lower it (max semantics).
- **Type change** keeps the positional index: `step = STEPS[newType][max(0, STEPS[oldType].indexOf(step))]` (e.g. Code ↔ Implement). Save key `type`. Selecting a Step in the inline row uses the options of the row's current Type.

### 2.12 Link editor (steps, tasks, sub-tasks)

- One step-link editor (`linkEdit`) and one task/sub-task link editor (`tlinkEdit`) may be open at a time; opening another replaces it. Collapsing a row clears `linkEdit`.
- Fields: **Link name** and **https://** placeholders. **Save** disabled (50 % opacity) until `url.trim()` is non-empty; `Enter` saves, `Escape` cancels; **Cancel**; **Delete** (step links) / **Delete link** (task links) only in edit mode.
- Normalisation on save: `url = url.trim()`; if it does not match `/^https?:\/\//i` prefix `https://`; `name = name.trim() || url.replace(/^https?:\/\//, '')` (scheme stripped, host + path + query kept).
- Persist: step links → `links[stepIdx]` replaced (push or index update), step recomputed, save key `links`; task links → save key `tasks`.
- Display: anchor text `name || url`, `target=_blank rel=noopener`, ✎ opens edit.

### 2.13 Tasks

#### 2.13.1 Roll-up from sub-tasks (`rollUp(t)`)

If `t.subtasks` is empty → `t` unchanged. Otherwise resolve the sub-task list (2.13.2) and:

- `status` = `Complete` if every sub-task is Complete; else `In Progress` if any sub-task is `In Progress` **or** at least one is Complete; else `Planned`.
- `dated` = non-Complete sub-tasks with a resolved start. If none → `{ status, rolled: true, chain: false }` (dates fall back to the parent's own stored values). Else `start = min(rStart)`, `end = max(rEnd ?? rStart)`, `days = max(1, daysBetween(start, end) − 1)`, `chain = false`, `rolled = true`.
- Rolled parents: status read-only with suffix **· sub-tasks**; Start shows **Σ d Mon yyyy**; Days read-only; Chain checkbox disabled (title **Dates come from its sub-tasks; chain the first sub-task instead**).

#### 2.13.2 Resolving starts and ends (`resolveTasks(list)`)

Walk the list in order after rolling up, keeping `prevEnd = null`:

- `Complete` → `rStart = rEnd = null`; **does not** update `prevEnd` and does not take part in chains.
- Otherwise `start = (chain && prevEnd) ? prevEnd : (t.start || null)`; `end = start && days ? addDays(start, days) : null`; then `prevEnd = end ?? start ?? prevEnd`.
- So a chained task follows the nearest preceding **non-Complete** task; a chained task with nothing to follow uses its own stored start (may be null → dates `—`).

#### 2.13.3 Chains and colours

Second pass over the resolved list (Complete tasks skipped), `last` = previous non-Complete task:

- If `t.chain && last`: if `last.chainId == null` assign a new `chainId`, mark `last.isAnchor = true`; `t.chainId = last.chainId`. Then `last = t` for every non-Complete task (chained or not).
- An unchained task therefore becomes an **anchor** the moment the next task chains to it ("An unchained task with followers starts a new chain (●)").
- **Colour index**: anchors sorted by ascending `id` (creation order, not position); `colorIdx = position of the task's anchor in that order`. Colour = `CHAIN_COLORS[colorIdx % 6]`, row tint = `CHAIN_BGS[colorIdx % 6]`; unchained tasks get `transparent` border/background. Legend: **● Chain A**, **● Chain B** … by `colorIdx` (`String.fromCharCode(65 + colorIdx)`), shown only when at least one chain exists.
- Cell hints: chained start shows `⛓ d Mon yyyy` (title **Follows the previous task — move that one and this moves too**); an anchor's date input is followed by a 9px dot in the chain colour (title **Starts a chain — the tasks below follow this date**).

#### 2.13.4 Field rules per row (`taskVM`)

| Aspect | Rule |
|---|---|
| Status control | `select` when `source !== 'Jira' && !rolled`; otherwise read-only text `status` + grey suffix (`– Jira` or `· sub-tasks`) with title **Status is synced from Jira and can only be changed there** / **Rolled up from sub-tasks: Complete when all are complete, In Progress when any has started**. |
| Status change (manual) | Set status; if new status ≠ Complete and the task has neither `start` nor `chain` → `start = TODAY`, `days = days \|\| 5`. Save. |
| Title | Inline input for manual (save on blur); read-only text + **– Jira** (title **Synced from Jira and can only be edited there**) for Jira. Complete → `line-through`, `#9c9a8e`. |
| Links pill | When `links.length > 0`: `n link` / `n links` gold pill; click opens the drawer. |
| Sub-tasks button | `▸ done/total` when sub-tasks exist (title **d of n sub-tasks complete — click to open**), else `▸ +` (title **No sub-tasks yet — click to add**); border `#d9b878` when sub-tasks exist else `#d8d4c8`; bg `#efe2c4` when open. Chevron flips to ▾. |
| Start | Complete → `—`. Chained → `⛓ date`. Rolled → `Σ date`. Else date input (`onChange`: value or null; save). |
| Days | Rolled → read-only number; Complete → nothing; else number input `min=1`, `onChange`: `max(1, parseInt \|\| 1)`; save. |
| End | `rEnd ? fmtD(rEnd) : '—'`. |
| Chain checkbox | Hidden when Complete. Disabled when first in list (`idx === 0`) or rolled. Title **Start when the previous task ends**. On → `chain = true` (stored start kept); off → `chain = false, start = rStart \|\| TODAY`. Save. Jira tasks **can** be chained. |
| Source | Jira → mono key chip linking to `JIRA_URL + key` (title **Synced from Jira — remove it there**); manual → **typed** (title **Typed here · editable**) + **×** (title **Delete task**). |
| Drag handle | `⋮⋮`, title **Drag to reorder — chained tasks move together** (sub-tasks: **Drag to reorder**). |

Visibility: rows with rolled-up status `Complete` are hidden unless **show Complete (n)** is ticked (`n` = Complete count). Counts pill **In Progress n · Planned n** uses rolled-up statuses; the count pill and the ▸ chevron both toggle the table; pill bg `#efe2c4` when open.

#### 2.13.5 Adding

Add row: status select (default `Planned`), input **Add a task and press Enter**, date (default `TODAY`), days (default 5), **Add**. Enter or Add: ignore blank titles; push `{ title, status, source: 'Manual', key: '', start: status === 'Complete' ? null : startInput, days: max(1, parseInt(days) || 5), chain: false, links: [], subtasks: [] }`; clear the title only (status/date/days inputs keep their values); save `tasks`.

#### 2.13.6 Deleting and undo

`×` removes the task immediately (no confirm), shows the bar **Deleted _title_.** **Undo** above the rows for **8 000 ms**, and saves. Undo restores the **entire list snapshot taken before the delete** (any other edit to that list during the 8 s is discarded). Applies to sub-tasks too (snapshot of that sub-list; the bar is the parent row's). A new delete resets the timer and replaces the snapshot.

#### 2.13.7 Drag and drop (blocks)

- Blocks (`blocksFor`): walk the resolved list; consecutive tasks sharing a `chainId` form one block `c<chainId>`; every other task (including Complete ones) is its own block `t<id>`. Dragging any task drags its block; the whole block renders at opacity .45.
- `dragover` on a row: ignored (drop indicator cleared) unless the drag is in the same list and a different block; else `index = mouseY < rowMidpoint ? block.index : block.index + 1`. Indicator: `inset 0 2px 0 #b88c3a` on the first row of block `index`, or `inset 0 -2px 0 #b88c3a` on the last row of block `index − 1`.
- Drop: remove the moving block, insert at the adjusted index (indices skipping the moving block); if the block order is unchanged do nothing. Otherwise write the flattened task order, save `tasks`.
- **Drop prompt** (top-level lists only): after the move take the first task of the moved block; if it is not Complete, not chained and has a resolved start, find the nearest preceding non-Complete task with a resolved end; if `prev.rEnd !== first.rStart` show the bar: **You moved _X_. It still starts _d Mon yyyy_; the task above it, _Y_, ends _d Mon yyyy_.** with **Chain after it · start _d Mon yyyy_** (→ `chain = true`), **Just move the date** (→ `start = prev.rEnd, chain = false`), **Keep _d Mon yyyy_** (dismiss). Chain/shift save under key `task<id>`.
- `dragend` clears drag/drop state.

#### 2.13.8 Task drawer

Opens per task (`taskOpen[taskId]`). Contents: **LINKS** row (chips `name` + ✎, **+ Link** pill, inline editor with **Delete link**), **SUB-TASKS** heading with summary **d of n complete** / **none yet**, sub-task table (header only when sub-tasks exist), add row **Add a sub-task and press Enter** with hint **starts today, 3 days, chained** and **Add**.

### 2.14 Sub-tasks

Same VM as tasks with `parentId` set; differences: no Source column (last column 44px holds ×); title 13.5px; Chain checkbox disabled only when first (`isFirst`); add rule in 1.5; drag/drop within the sub-list only (no drop prompt); undo bar shared with the parent row. Chains and colours are computed per sub-list independently.

### 2.15 GitHub Issues

#### 2.15.1 Section visibility and scope

- `synced` = distinct repos resolved from entries with `sync = true`. Section renders only when `synced.length > 0`.
- `inScope(issue)` = some entry `e` with `e.sync && resolve(e.pattern).includes(issue.repo) && (e.scope === 'all' || issue.labels.includes(e.label.trim()))`.
- Per in-scope issue: `total = children.length`, `done = children closed`, `openKids = total − done`; `hidden = state === 'closed' && reason && reason !== 'completed'`; `status = (state === 'open' || openKids > 0) ? 'in progress' : 'done'`; `note = state === 'closed' && openKids > 0 ? 'closed in GitHub · ' + openKids + ' sub-issue' + (openKids === 1 ? '' : 's') + ' open' : ''`; `kidsVisible` = children that are not closed-with-non-completed-reason.
- Hidden issues (and their children) are excluded from everything except `hiddenCount`.
- Stats: `inProg`, `done`, `ghCount` (visible issues), `subTotal`/`subDone` over visible issues, `allEpic = ghCount > 0 && every visible issue has label 'epic'`, `multi = synced.length > 1`, `seen = all in-scope issues + their children` (feed note only).

#### 2.15.2 List

- `list = visible.filter(showDone || status !== 'done').sort(updated desc)`; `totalN = list.length`; `capped = totalN > cap`; render `list.slice(0, cap)` (`cap = taskCap`, default 50).
- Cap footer: link **View all _totalN_ in GitHub** + grey **· showing the _cap_ most recently updated**; link URL = single synced repo → `GH + repo + '/issues?q=is%3Aissue+is%3Aopen'`, otherwise `https://github.com/issues?q=is%3Aissue+is%3Aopen+org%3Awildlife-protection-solutions`.
- Empty text: `ghCount > 0` → **All _n_ epics|top-level issues are done · tick show done to list them**; else **no in-scope issues in _n_ repo|repos**.
- `kind` = `epics` when `allEpic` else `top-level issues`.

#### 2.15.3 Header

- Pill: **in progress _n_** (bg `#efe2c4` when open); hovering shows `split` = `${ghCount} ${kind} · ${inProg} in progress · ${done} done` + (hiddenCount ? ` · ${hiddenCount} not planned hidden` : '') either as a dark tooltip (`pillHover = Tooltip`) or by swapping the pill text (`Swap`).
- **show done (_n_)** checkbox once open.
- Aggregate (`aggregateStyle`): Sentence → `${ghCount} ${kind} · ${subDone} / ${subTotal} sub-issues closed` (falls back to the empty text when `ghCount = 0`); Bar → `${ghCount} ${kind}` + 120px bar (`subDone/subTotal` %) + `${subDone} / ${subTotal} sub-issues closed`; Bar reverts to Sentence when `ghCount = 0`.
- Sync note: **synced 12 min ago** `#9c9a8e`, or when sync failed **sync failed · last synced 6 h ago** `#b8442e`. In Ledger layout it sits right-aligned in the heading; in Two-line it appears in each row's meta line.

#### 2.15.4 Rows (Ledger)

Grid `22px minmax(180px,1fr) 58px 112px 150px 86px [124px] 26px` (the repo track only when `multi`). Cells: chevron (only when `total > 0`; title **Show sub-issues**) · title (button when it has children; done → `line-through` `#9c9a8e`) · **GITHUB** badge (title **Synced from GitHub · change it there**, or the sync-failed text; border `#e8c2b8` when failed) · status text (`in progress` / `done`, grey when done) + red note · progress bar 84px + `d / t` (only with children) · assignee initials (first 3 as 18px circles, then `+n`) · repo (mono, `multi` only) · **↗** (title **Open in GitHub**, URL `GH + repo + '/issues/' + num`).

#### 2.15.5 Rows (Two-line)

Line 1: chevron · title (500) · GITHUB badge · avatars · ↗. Line 2 (indented 22px): status · 110px bar + `d / t sub-issues closed` · repo (`multi`) · sync text · red note.

#### 2.15.6 Children

`kids = kidsVisible` sorted open-first (stable); show 10, then **show all _n_** / **show fewer**. Row: 8px dot (`unreadable` → `1.5px dashed #d8d4c8`, transparent; closed → `1.5px solid #b88c3a` filled `#b88c3a`; open → `1.5px solid #9c9a8e` transparent) · title (closed → strike, grey) · status `done` / `in progress` (`''` when unreadable) · first assignee initial (16px circle; none when unreadable) · repo (`multi`) · ↗ (not for unreadable).

#### 2.15.7 Unreadable child

Renders **1 child in another repo — not readable** in italic `#9c9a8e`; counts towards `total` and `open` (it is `state: 'open'`).

### 2.16 Work Summary

State machine on `recent.state`:

```
idle ──open section──▶ (repos.length ? loading : norepo)
loading ──success──▶ cached { generated }      loading ──error──▶ failed
cached/failed ──Regenerate/Retry──▶ loading    norepo ──open again after a repo was linked──▶ loading
```

- Opening the section (chevron) triggers generation when `state === 'idle'` or (`state === 'norepo'` and repos now exist). Closing does nothing. Generation checks `repoFor(project)` (all entries, sync on or off).
- Heading meta: `cached · <generated>` / `generating…` / `failed` / `no repository linked` / idle → `${n} repository|repositories` or `no repository linked` when `n = 0`.
- Repo links after the heading: `n > 3` → one link **GitHub · _n_ repositories** to `https://github.com/orgs/wildlife-protection-solutions/repositories`; `n > 1` → **GitHub · _repo_** per repo; `n = 1` → **GitHub Repository**.
- Body: loading **Summarising recent commits from _n_ linked repositories…** (pulse 1.4 s); failed **Couldn’t generate a summary — _error_.** + **Retry**; norepo **No repository is linked to this project, so there is nothing to summarise.** + link **Link one in Settings › Integrations** (navigates to Settings and scrolls to section 04) + `.`; cached → paragraph from `summary` segments (`{link}` → anchor to `GH + repo`), then **Cached · generated _when_ · summarised by the Ask model from commit history** + **Regenerate**.
- **Auto-fill Current Status from summary** (title **When a new summary is generated, copy it into Current Status**): stored `autoStatus`; when turned on and a cached summary exists the text is copied immediately; on every successful generation the plain text (segments joined, links as repo names, trimmed) overwrites `status`. The Current Status label then shows **FROM WORK SUMMARY** in `#a37a2f`.
- `generated` display format in the prototype: `'23 Sep 2026, HH:mm'`.

### 2.17 Repos

- `resolve(pattern)`: trim; empty → `[]`; ends with `*` → org repos starting with the prefix (prefix may be empty → all repos); else exact match (0 or 1). Case-sensitive.
- Entry row: mono pattern · resolution button: `→ no repos match` (`#b8442e`) / `→ 1 repo` / `→ n repos ▸|▾` (clickable only when `n > 1`; toggles the mono list of names) · scope select **all issues** / **label:** + mono label input (placeholder **epic**, saves on blur) when `label` · **sync** checkbox · **×** (title **Remove entry**).
- Add row: placeholder **Add a repo or prefix pattern, e.g. argus-*** when there are no entries, **Add another repo or pattern** otherwise; live preview `→ n repo|repos` or `→ no repos match` (red) or empty; Enter/Add ignores blank; new entry `{ pattern, scope: 'all', label: '', sync: true }`; input cleared. Duplicates are not prevented.
- Pill: `${entries} entry|entries · ${repos} repo|repos` or **none**; bg `#efe2c4` when open. Heading caption **exact name or prefix pattern · issues sync hourly, commits feed Work Summary**.
- Empty: **No repos · no GitHub Issues or Work Summary for this row.**
- All mutations save under key `repos`; status shown in the Repos heading only.

### 2.18 AI page

- Range: presets 7/30/90 → `days = preset`, `start = TODAY − (days − 1)`, `end = TODAY`; Custom → `start`/`end` from the two date inputs (blank ignored), `days = max(1, daysBetween(start, end))`. Label `${fmtD(start)} – ${fmtD(end)} · ${days} days` (en dash). Default 30 days → **25 Aug 2026 – 23 Sep 2026 · 30 days**.
- Sample scaling: `factor = days / 30`; money and tokens × factor; Gemini days per person = `gDays ? clamp(round(gDays × factor), 1, days) : 0`. In production the period is a query parameter; no scaling.
- Only `active` people appear anywhere.
- Cards: Claude `usd(Σ claude)` · `${tok(Σ tokens)} tokens · ${people with claude > 0} people`; ChatGPT incl. Codex `usd(Σ gpt + Σ codex)` · `ChatGPT ${usd(Σ gpt)} · Codex ${usd(Σ codex)} · ${people with gpt > 0 || codex > 0} people`; Gemini `${people with gDays > 0}` **active users** · `${Σ gemini days} active days · most used in ${top 3 apps by user count, comma-joined}` (or `no app detail`) + `. From the Workspace Reports API (fields to be confirmed); Google reports activity, not cost.` **Details** links open AI detail on that vendor.
- Table columns: Person (`count`, weight 500) · Department (non-empty count) · Claude · ChatGPT · Codex (money: right-aligned, `—` for 0, sum `$x total`) · Gemini (`n days` / `—`, sum `n days total`) · Total (money, = claude + gpt + codex). Zero/empty cells `#9c9a8e`. Sorting/filtering/resizing as 2.5–2.7. Empty: **No people match these filters.**
- Amber notice when any AI feed (`Claude`, `ChatGPT cost`, `ChatGPT usage`, `Codex`, `Gemini`) is not green: items `${feed} feed failed (last good run ${last})` for red or `${feed} feed degraded` for amber, joined by ` · `, then `. Affected figures may be stale.` + link **Connection health** (Settings § 05). Sample: **ChatGPT usage feed failed (last good run 10 Sep 2026, 06:05) · Codex feed degraded. Affected figures may be stale.**
- Footnote: **Inactive people are excluded. Cost is what each vendor billed (Claude priced from tokens at the rates in Settings). Gemini is active days in the period.**

### 2.19 AI detail

- Records per active person: Claude (when `claude > 0`): product = model, cost, usage = tokens; ChatGPT (when `gpt > 0`): product `ChatGPT`, cost, usage null; Codex (when `codex > 0`): vendor `ChatGPT`, product `Codex`, cost, usage null; Gemini (when `gDays > 0`): product `Gemini for Workspace`, cost null, usage = days.
- Tabs **Claude | ChatGPT | Gemini** (h1 = tab). Columns Person · Department · Product · Cost (`usd` / `—`; sum omitted on Gemini) · Usage (Claude `tok`; Gemini `n active days`; ChatGPT `—`; sum omitted on ChatGPT).
- Notices: ChatGPT tab → **Usage is unavailable: the ChatGPT usage feed has been skipped since 10 Sep.** + **Connection health**; Gemini tab → **Assumed: Gemini fields come from the Google Workspace Reports API (active days, apps used, last active). What Google actually returns still needs confirming.**
- Empty: **No records match these filters.** Back link **← AI**.
- Deep link from Ask person rows: `page = ai-detail`, `aiTab = Claude`, `tbl['det-Claude'] = { filters: { name: { selected: [personName] } }, sort: null }`.

### 2.20 Ask

- Submit (form Enter or **Ask**) with non-blank text, or an example pill: `askAsked = text`, `askKey = route(text)`, SQL hidden.
- Routing (case-insensitive, first match wins): `/feed|health|connection|unhealthy/` → `feeds`; `/claude|spen[dt]|cost|most/` → `claude`; `/release|due|ship/` → `releases`; else `unknown`. (Note `most` routes generic "most" questions to Claude spend.) Production replaces this with the Ask model over MCP; the answer contract below is what the UI renders.
- Answer contract: `{ q, paragraphs[], noNarrative, text, count, window, filters[] (mono chips), headers[], rows[] ({ open(), openLabel, cells[] {text,color,weight} }), cols (grid-template-columns), hasRows, sql, source }`. Chips always render: **_count_ rows** and `window` (`—` when none) plus one mono chip per filter. **Show SQL** / **Hide SQL** toggles a dark `pre`; **CSV** only when rows exist; `source` right-aligned grey.
- **releases**: `horizon = TODAY + 30`; rows = saved projects with `designation === 'Active' && nextRelease && nextRelease <= horizon`, sorted by date; `late` = those `< TODAY`; undated = Active projects without a date. Data line `${n} Active projects have a Next Release on or before ${fmtD(horizon)}.` + (late ? ` ${late} is|are already past due.` : ''); window `${fmtD(TODAY)} → ${fmtD(horizon)}`; filters `designation = Active`, `next_release <= ${horizon}`; headers Project · Owner · Step · Next Release (`cols: minmax(160px,2fr) minmax(120px,1fr) 110px 170px`); Next Release cell adds ` · past due` in red when late; Project cell gold 600; row click opens the project; source **Portfolio · sample data**. Paragraph 1 template: `${n} Active project(s) has|have a Next Release on or before ${horizon}[, and ${list(late titles)} has|have already slipped past its|their date]. ` then per row `${title} is due ${date}[ (past due)] and sits at ${step}. ${status} ${next}`; paragraph 2 (if undated) `${list(undated)} is|are Active but carries|carry no Next Release, so it|they will not appear here until a date is set.` `list()` joins with `, ` and ` and `. SQL: `SELECT project_name, owner, step, next_release` / `FROM portfolio.project` / `WHERE designation = 'Active'` / `  AND next_release <= '${horizon}'` / `ORDER BY next_release`.
- **claude (Admin)**: active people sorted by `claude` desc, top 5. Headers Person · Claude · Claude tokens (`cols: minmax(160px,2fr) 130px 150px`); data line **Top 5 people by Claude cost over the selected range. Cost is tokens priced at the rates in Settings.**; window = AI range label; filters `vendor = Claude`, `active = true`, `TOP 5 BY cost`; rows open AI detail for the person (title `Open ${name} in AI › Claude`); source `AI · ${activeCount} active people · sample data`. Paragraphs: share of top five, leader/followers with department, heaviest department %, and `${opus} of the top five mainly use Claude Opus 4.1, the most expensive model; moving routine work to Sonnet or Haiku is the only lever short of using less.`; when no cost: **No Claude cost was recorded for active people in this window.** SQL `SELECT TOP 5 p.name AS person, SUM(u.cost_usd) AS cost, SUM(u.tokens) AS tokens` / `FROM ai.usage u JOIN people p ON p.id = u.person_id` / `WHERE u.vendor = 'Claude' AND p.active = 1` / `  AND u.day BETWEEN @start AND @end` / `GROUP BY p.name` / `ORDER BY cost DESC`.
- **claude (Viewer)**: only the current user's row. Data line **Your role limits AI answers to your own usage, so this is you only. Rankings against other people are not available.**; filters `person = current user`, `vendor = Claude`; paragraphs `You spent ${usd} on Claude over the last ${days} days, about ${tok} tokens, mostly on ${model}.` and **Other people’s figures are outside your role’s scope, so Ask cannot rank you against the team. An Admin can see the full list on the AI page.**; row title **Open your Claude usage**; source **AI · scoped to your rows · sample data**; SQL uses `person_id = @current_user`.
- **feeds (Admin)**: `bad` = feeds not green (from the computed feed list, 2.23.4). Data line `${bad} of ${all} feeds are not healthy right now. Controls for each feed are in the old app.`; window `now`; filter `status <> 'ok'`; headers Feed · Status · Last successful run · Note (`cols: minmax(120px,1fr) 100px 160px minmax(200px,2fr)`); Status cell **Failed** `#b8442e` / **Degraded** `#c98a1e` weight 600; rows open the feed controls (title `Open the ${feed} feed controls`); source **Settings › Connection health · sample data**. Paragraphs: `${bad} of ${all} feeds need attention. ${list(failed)} has|have failed outright; ${list(degraded)} is|are degraded.`, then per bad feed `${name}: ${note without trailing .} (last good run ${last}).`, then either `Figures that depend on ${list(aiBad)} on the AI page should be read as stale until it recovers|they recover. Controls for every feed remain in the old app.` or just the last sentence. SQL `SELECT feed, status, last_success_at, note` / `FROM ops.feed_health` / `WHERE status <> 'ok'` / `ORDER BY status, feed`.
- **feeds (Viewer)**: no rows; paragraph and data line **Connection health lives in Settings, which your role cannot see, so Ask cannot answer this for you. An Admin can; you can still ask about the Portfolio and your own AI usage.** (data line ends after **An Admin can.**); filters `scope = Viewer`, `settings excluded`; source **Settings excluded for this role**.
- **unknown**: `noNarrative = true` → amber box **No written answer for this question yet. Once the Ask model is connected through MCP it will answer here in full sentences; the rows and query below stay as the evidence.**; data line `Ask answers questions about the Portfolio and AI${role === 'Admin' ? ' and Settings' : ''}. Try one of the examples above, or ask about a project by name.`; chips **0 rows**, **—**; no table, no SQL, no CSV.
- **Deep links**: project row → `openProject(id)`: if the current Status filter would exclude the project's displayed status, drop the Status filter entirely; clear the search; expand the row; navigate to Portfolio and smooth-scroll to `#proj-<id>` with a 150px offset. Person row → 2.19. Feed row → feed controls page.

### 2.21 Settings

Left rail: sections **01 People**, **02 Departments**, **03 Vendor pricing**, **04 Integrations**, **05 Connection health & alerts**, **06 Public website feed**, **07 Warden Rules**, **08 Ask settings**; click smooth-scrolls to the section (64px offset). Section anchors `s-people`, `s-depts`, `s-pricing`, `s-github`, `s-health`, `s-public`, `s-warden`, `s-ask`. Settings edits apply instantly in the prototype (no save-status line); production should persist each change and may reuse the 2.9 machinery.

1. **People** — row: name (500) · email · Department select · Role select (`Viewer` / `Admin`) · Status (`Active` / `Inactive since d Mon yyyy`) · action. Selects disabled when inactive; inactive rows in `#9c9a8e`. **Mark inactive** → amber confirm → red **Mark inactive** sets `active = false, inactiveSince = TODAY`; **Cancel** dismisses. **Reactivate** (link) sets `active = true, inactiveSince = null`. People are never deleted.
2. **Departments** — name input renames on every change and cascades to `people.dept` by old name; **Active people** = active count; add row **Add a department** (Enter/Add; blank ignored; duplicates not prevented). Table max-width 540.
3. **Vendor pricing** — Model (read-only) · Input $ / M tokens · Output $ / M tokens (number inputs, step 0.01, edit in place) · Effective from (read-only `fmtD`). No add row in the prototype.
4. **Integrations** — token line: dot + `GITHUB_TOKEN` + state + text + **· shared with the GitHub commits feed** + **Rotate →** (opens the GitHub Issues feed controls). Token info from `tokenDaysLeft` (2.23.3). Repo entries table across all saved projects: Project (name only on the project's first entry) · Repo entry (mono) · Resolves to (`no repos match` red / repo name / `n repos`) · Scope (`all` / `label:<label>`) · Sync (`on` `#8a6524` / `off` `#9c9a8e`) · Last sync (`—` when off; `23 Sep 2026, 06:20`; when sync failed `23 Sep 2026, 00:20 · failed since 01:20` in red) · **Edit on the row** (first entry only; opens the project). Below: `${n} project|projects has|have no repo entries · their tasks are typed here or from Jira. ` + **New entries are added on the row.** Jira: mono key link · Linked project select (**Unlinked — pick a project** as the empty option, red text when unlinked) · Last sync · **Unlink** (removes the Jira row entirely). Add row: **Jira project key** (trimmed, upper-cased) + **Link to project…** select; both required; new row `sync = 'not yet synced'`.
5. **Connection health & alerts** — table Feed · Status (dot + `OK` / `Degraded` / `Failed`, or grey `Paused` when the feed is disabled) · Last successful run · Note · **Controls →**. Footer: **Email recipients** chips (× removes) + input **Add email, press Enter** (Enter or `,` adds; blur adds; trailing comma stripped; must match `/^[^@\s]+@[^@\s]+\.[^@\s]+$/` else **Enter a valid email address.**; duplicates ignored); **Alert after** select (`1 failed run`, `2 failed runs`, `3 failed runs`, `5 failed runs`); **Also alert on Degraded** checkbox.
6. **Public website feed** — checkbox per saved project (label + designation in grey; title `#1a1a2e` when public else `#6b6a63`) toggling `public`; right pane `JSON.stringify(saved.filter(public).map(p => ({ name: title, type, designation, step, nextRelease: nextRelease || null })), null, 2)` — stored designation, not At Risk.
7. **Warden Rules** — view row: rule · JSON (mono, ellipsis, `title` = full string; **Not valid JSON** under it when `!jsonOk`) · **Edit** / **Delete**. Edit mode (`#fbf9f4`): rule input (**Rule in plain English**), JSON textarea (**JSON: what triggers it and who is notified**), **Save** enabled only when rule and JSON are non-blank and JSON parses (else **Not valid JSON — Save is disabled until it parses.**), **Cancel**; `Escape` cancels; Enter does not save. Delete → **Delete this rule? The Warden stops evaluating it immediately.** with **Keep** / **Delete rule**. Add row (**New rule**, **JSON: what triggers it and who is notified**, Enter/Add): requires both non-blank; JSON validity is **not** checked on add in the prototype (the row then shows **Not valid JSON**).
8. **Ask settings** — Model select; static **Scope by role** matrix (Viewer: Portfolio `All`, AI `Own rows only`, Settings `None` grey; Admin: `All`, `Everyone`, `All`); **Standing instructions** textarea.

### 2.22 Health alerts and the Settings badge

```
alertFeeds  = feeds.filter(f => feedCfg[f].alerts && feedCfg[f].enabled && (status === 'red' || (includeDegraded && status === 'amber')))
activeAlerts = alertFeeds.filter(f => !acked[f])
badge (Admins only) = activeAlerts.length     // hidden when 0 or for Viewers
```

- Alerts box (Admins only, when `activeAlerts.length > 0`): **ACTIVE HEALTH ALERTS**, then per feed: name (red for Failed, amber for Degraded) · `${Failed|Degraded} · last good run ${last} · ${note}` · **Retry** (gold; while retrying shows pulsing **Retrying…** and the button is disabled at 50 %; after 1600 ms **Still failing** in red) · **Acknowledge** (sets `acked[feed] = true`; there is no un-acknowledge; acked state is session-only in the prototype).
- Retry also performs a manual run of the feed (2.23.2), so a run row is appended to that feed's history.
- The `threshold` ("Alert after") is stored but not evaluated anywhere in the prototype.
- Sample: with defaults the badge is **2** (ChatGPT usage, Copilot); ticking **Also alert on Degraded** makes it 4 (adds Codex, WPS Agents); pausing a feed removes it.

### 2.23 Feed controls

#### 2.23.1 Header

**← Settings · Connection health** back link; h1 = feed name; status dot/label (`OK` / `Degraded` / `Failed` / `Paused`); source text (`FEED_META[1]`); line **Last successful run _last_ · next run _next_ · _schedule_** where `next` = `in 38 min` (Every hour) / `today 12:20` (Every 6 hours) / `tomorrow 06:00` (Nightly 06:00) / `Mon 28 Sep, 06:00` (Weekly) / `paused` when disabled (production computes real times).

#### 2.23.2 Controls card state machine

- **Feed**: `enabled` → text **Running on schedule**, button **Pause feed**; disabled → **Paused — no runs until resumed**, **Resume feed**. Paused feeds show grey **Paused** everywhere and drop out of alerts.
- **Schedule** select (values in 1.1); changing it updates the "next run" text.
- **Manual run**: idle message **Runs outside the schedule and records the result below.** (`#6b6a63`); **Run now** → button **Running…** disabled (50 %) for the request (1500 ms) → message and prepended run: green feed → **Completed just now** (`#2f8a4f`) + `{ when: 'Today, HH:mm', result: 'OK', duration: '9 s', records: '2,512', message: 'Manual run' }`; amber → **Partial — some data missing** (`#c98a1e`) + `{ 'Partial', '15 s', '1,182', 'Manual run — vendor returned 2 of 3 pages' }`; red → **Failed — rotate the credential and run again** (`#b8442e`) + `{ 'Failed', '2 s', '—', 'Manual run — 401 Unauthorized, credential still rejected' }`. Runs list is capped at 8. Message colour by prefix (`Failed`/`Partial`/`Completed`).
- **Credential**: masked value (mono) + meta line; **Rotate** → password input **Paste the new key** (`Escape` cancels), **Save key** disabled until `newKey.trim().length ≥ 8`, **Cancel**, note **The key is stored server-side and never shown again; the next run uses it.** Save → `credential = key.slice(0, min(6, len − 4)) + '…' + key.slice(-4)`, meta **Rotated today · takes effect on the next run**, editor closes. For GitHub Issues the default meta is replaced by the token's `credMeta` (2.23.3).
- **Include in health alerts** checkbox → `alerts`.

#### 2.23.3 `GITHUB_TOKEN` (from `tokenDaysLeft`, default 50)

`days = max(0, round(tokenDaysLeft))`, `expiry = fmtD(TODAY + days)`. State: `days ≤ 0` → **Expired** `#b8442e`; `≤ 30` → **Expires soon** `#c98a1e`; else **Valid** `#2f8a4f`. Text: expired → `expired ${expiry}`; else `expires ${expiry} · ${days} day|days` + (`≤ 30` ? ` · rotate before then` : ''). `credMeta` = `${Expired|Expires} ${expiry} · shared with the GitHub commits feed`. `syncFailed = simulateSyncFailure || days ≤ 0`.

#### 2.23.4 Computed feed rows (GitHub, GitHub Issues)

- **GitHub** note: `${allRepos} repository|repositories resolved from ${entries} repo entry|entries` where `allRepos` = distinct repos from every entry of every saved project (sync on or off) and `entries` = entry count. Sample: **19 repositories resolved from 6 repo entries**.
- **GitHub Issues**: status `red` if `syncFailed`, `amber` if token `days ≤ 30`, else `green`. Red → last run `23 Sep 2026, 00:20`, note `Last error: 401 from api.github.com on the 06:20 hourly run — token ${expired|rejected} on all ${synced} repos. Rows stay visible with a stale badge; ${rotate|fix} the token and run again.`; otherwise note `Hourly poll + nightly full pass · ${synced} repos resolved · ${seen} issues seen · token expires ${expiry} (${days} d)` + (` · rotate before then` when amber else ` · no errors`). `synced` = distinct repos from entries with sync on; `seen` = Σ per project of in-scope issues + their children. Sample: **Hourly poll + nightly full pass · 18 repos resolved · 337 issues seen · token expires 12 Nov 2026 (50 d) · no errors**.
- When GitHub Issues is red its run history rows are rewritten to `Failed · 1 s · — · 401 Unauthorized — token rejected by api.github.com` (manual runs kept), the Integrations "Last sync" turns red, issue rows get the failed badge, and the section sync note reads **sync failed · last synced 6 h ago**.
- Sample run histories (`mkRuns`): 6 rows, one per day back from 23 Sep at the feed's last-run time (GitHub Issues: hourly `23 Sep 2026, 06:20`, `05:20`, …); green → `OK`, durations `8 + (i*3 % 9) s`, records decreasing (`2480 − 11i`; GitHub `312 − 5i`; GitHub Issues `315 − i`; Jira `146 − 3i`); amber → first two `Partial` `Vendor returned 2 of 3 pages; retry scheduled`, then `OK`, `14 + i s`, `1180 − 7i`; red → `Failed 2 s —` with `401 Unauthorized — token expired` (Copilot) or `401 Unauthorized — token rejected`.

### 2.24 Navigation, chrome, keyboard, roles

- Nav items: PORTFOLIO (`page === 'portfolio'`), AI (active for `ai` and `ai-detail`), ASK, SETTINGS (active for `settings` and `feed`). Navigating closes the popover and user menu and scrolls to top. The floating **Ask** button is present on every page, including Ask.
- User menu: name, email, `Role: ${role}`, **Log out** (returns to the sign-in screen; **Sign in with wpsWatch** returns to Portfolio). Closes on outside `mousedown` and `Escape`.
- Row click toggles the panel; clicks inside inline editors stop propagation; **Collapse ▴** also toggles. Multiple rows may be open.
- Keyboard: `Enter` adds tasks, sub-tasks, repo entries, departments, recipients (also `,`), rules and saves link editors; `Escape` closes the popover, the user menu, link editors, the Warden editor and the key-rotation input. `Enter` does **not** save the Warden edit row or the Ask form beyond normal form submit.
- Validation summary: draft needs Title, Owner, Type, Status; link editors need a URL; recipients must be valid emails; Warden JSON must parse to Save an edit; task days ≥ 1 (coerced); key rotation ≥ 8 chars.
- **Role differences actually implemented in the prototype**: Settings badge and alerts box are Admin-only; Settings page shows the Viewer notice; Ask `claude` returns the Viewer's own row only and `feeds` is refused; the unknown-answer data line omits "and Settings" for Viewers. **Not implemented in the prototype but stated in the README** (must be enforced server-side and mirrored in the UI): Viewers see only their own rows on AI and AI detail; Settings controls are read-only for Viewers; Settings content is excluded from Ask for Viewers.

---

## 3. Copy inventory (exact strings, per screen)

Typographic characters are as in the HTML (`’` U+2019, `“ ”`, `—`, `·`, `→`, `←`, `▸ ▾ ▴`, `↗`, `⋮⋮`, `⛓`, `Σ`, `✓`, `✎`, `⌕`). Labels rendered with `text-transform: uppercase` are given in source case with "(uppercased)".

### 3.1 Sign-in

- Title **WPS Portfolio**; sub-line **Same wpsWatch login, same roles. Sessions stay signed in until you log out.**; button **Sign in with wpsWatch**; tag **Sample data · prototype** (uppercased); logo alt **Wildlife Protection Solutions**.

### 3.2 Global chrome

- Brand **WPS Portfolio**; nav **Portfolio**, **AI**, **Ask**, **Settings** (uppercased); tag **Sample data** (uppercased); user button `<name> ▾`; menu: name, email, **Role: Admin** / **Role: Viewer**, **Log out**.
- Strip: **Sample data** (uppercased) + **This prototype runs on fictional data. People, spend, tokens, links, rules and summaries are made up; only the project names are real. Nothing is connected to a live system.** (drop in production).
- Floating button **Ask**.

### 3.3 Portfolio

Header: h1 **Portfolio**; segments **Open** / **Complete**; search placeholder **Search names, owners, descriptions**; **Clear filters (n)**; **Export CSV**; **+ New project**.

Table: lead **Σ**; column labels **Project Name**, **Owner**, **Type**, **Status**, **Step**, **Start Date**, **Next Release**, **Next Step** (uppercased in headers; label case in CSV); header button titles **Sort**, **Filter & sort**, **Drag to resize**; sum texts (2.4); cell fallbacks **Untitled**, **New project**, **—**; tag **past due** (uppercased); select placeholder **Select…**; inline input placeholders **Project name**, **Owner**; empty **No projects match these filters.**

Filter popover: **▲ Sort A → Z** / **▼ Sort Z → A** / **Sort smallest → largest** / **Sort largest → smallest**; heading **Filter** (uppercased) **· <Column label>**; search **Search values…**; **All values** / **Select all** + `n value(s)`; **No matching values**; **Clear filter & sort**; **Done**.

Fields block (tweak off): labels **Project Name**, **Owner**, **Type**, **Status**, **Next Release** (uppercased; ` *` on drafts); placeholders **Project name**, **Name**; select options **Select…**, **Software**, **Implementation**; **Active**, **Ongoing**, **Future**, **Experimental**, **Complete**.

Panel:
- Draft banner **New project. Project Name, Owner, Type and Status are required; dates are pre-filled and editable.** — **Discard**, **Create project**.
- Save line **Saving…** / **Saved** / **Couldn’t save** **· Retry**; **Delete project**; **Collapse ▴**.
- Delete confirm **Delete _title_? Its _n_ tasks, repo entries, links and narrative go with it, and it leaves the public website feed. Jira projects are unlinked, not deleted.** (`title` falls back to **this project**) — **Keep**, **Delete project**.
- Steps caption **Steps** (uppercased) **· adding a link moves the step forward; the approval checkbox gates everything after it**; step button title **Set as current step**; **Approved**; link ✎ title **Edit link**; **+ Link**; link editor placeholders **Link name**, **https://**; **Save**, **Cancel**, **Delete**.
- Narrative labels **Project Description** (placeholder **What this project is and why it exists**), **Current Status** (**Where it stands today**; badge **from work summary** uppercased), **Next Step** (**What happens before the next review**).
- Tasks heading **Tasks** (uppercased) + **typed here or synced from Jira**; pill **In Progress _n_ · Planned _n_**; **Jira · _KEY_**; **show Complete (_n_)**.
- Task table headers (uppercased) **Status**, **Task**, **Sub-tasks** (title **Open a task’s sub-tasks and links. Status and dates roll up from sub-tasks.**), **Start**, **Days**, **End**, **Chain** (title **A chained task starts when the task directly above it ends. Tasks in the same chain share a colour; reorder to regroup.**), **Source**.
- Task row: drag title **Drag to reorder — chained tasks move together**; status options **Planned**, **In Progress**, **Complete**; suffixes **– Jira**, **· sub-tasks**; status titles **Status is synced from Jira and can only be changed there**, **Rolled up from sub-tasks: Complete when all are complete, In Progress when any has started**; Jira title text title **Synced from Jira and can only be edited there**; links pill `n link|links` (title **Open links**); sub-task button titles **d of n sub-tasks complete — click to open** / **No sub-tasks yet — click to add**; start titles **Follows the previous task — move that one and this moves too**, **Rolled up from sub-tasks: earliest start to latest end**, **Starts a chain — the tasks below follow this date**; days title **Rolled up from sub-tasks**; chain titles **Start when the previous task ends**, **Dates come from its sub-tasks; chain the first sub-task instead**; key chip title **Synced from Jira — remove it there**; **typed** (title **Typed here · editable**); × title **Delete task**.
- Drop prompt **You moved _X_. It still starts _date_; the task above it, _Y_, ends _date_.** — **Chain after it · start _date_**, **Just move the date**, **Keep _date_**.
- Undo bar **Deleted _title_.** **Undo**.
- Add row: placeholder **Add a task and press Enter**; **Add**.
- Footer note **Rows marked “– Jira” are synced from Jira: status and title change there, but start, days, chain and order are set here. Tasks in a chain share a colour and each starts when the one above it ends. An unchained task with followers starts a new chain (●). Drag ⋮⋮ to reorder; a chain moves as one block. ▸ opens a task’s links and sub-tasks.**; legend **Chain A**, **Chain B** …
- Drawer: **Links** (uppercased); **+ Link** (title **Add a link**); **Delete link**; **Sub-tasks** (uppercased) + **d of n complete** / **none yet**; sub-table headers **Status**, **Sub-task**, **Start**, **Days**, **End**, **Chain**; sub drag title **Drag to reorder**; × title **Delete sub-task**; add placeholder **Add a sub-task and press Enter**; hint **starts today, 3 days, chained**; **Add**.
- GitHub Issues heading **GitHub Issues** (uppercased); pill **in progress _n_**; **show done (_n_)**; aggregate / split / empty texts (2.15); sync **synced 12 min ago** / **sync failed · last synced 6 h ago**; table headers **Issue**, **Source**, **Status**, **Sub-issues**, **Assignees**, **Repo**; badge **GitHub** (uppercased; titles **Synced from GitHub · change it there**); chevron title **Show sub-issues**; statuses **in progress**, **done**; note **closed in GitHub · _n_ sub-issue(s) open**; ↗ title **Open in GitHub**; children **show all _n_** / **show fewer**; placeholder **1 child in another repo — not readable**; cap **View all _n_ in GitHub** **· showing the _cap_ most recently updated**; empty **All _n_ epics|top-level issues are done · tick show done to list them** / **no in-scope issues in _n_ repo(s)**.
- Work Summary heading **Work Summary** (uppercased) + meta **cached · _when_** / **generating…** / **failed** / **no repository linked** / **_n_ repository|repositories**; links **GitHub Repository** / **GitHub · _repo_** / **GitHub · _n_ repositories**; body **Summarising recent commits from _n_ linked repositories…**; **Couldn’t generate a summary — _error_.** **Retry**; **No repository is linked to this project, so there is nothing to summarise.** **Link one in Settings › Integrations**; **Cached · generated _when_ · summarised by the Ask model from commit history** **Regenerate**; checkbox **Auto-fill Current Status from summary** (title **When a new summary is generated, copy it into Current Status**).
- Repos heading **Repos** (uppercased) + **exact name or prefix pattern · issues sync hourly, commits feed Work Summary**; pill **_n_ entry|entries · _m_ repo|repos** / **none**; resolution **→ no repos match** / **→ 1 repo** / **→ _n_ repos ▸**; scope options **all issues**, **label:**; label placeholder **epic**; **sync**; × title **Remove entry**; add placeholders **Add a repo or prefix pattern, e.g. argus-*** / **Add another repo or pattern**; **Add**; empty **No repos · no GitHub Issues or Work Summary for this row.**

### 3.4 AI

h1 **AI**; range label; segments **7 days**, **30 days**, **90 days**, **Custom** (+ two date inputs separated by **–**); **Clear filters (n)**; **Export CSV**; notice `<items>. Affected figures may be stale.` + **Connection health**; cards **Claude** / **ChatGPT incl. Codex** / **Gemini** (uppercased) with **Details**; sub-lines **_tokens_ tokens · _n_ people**, **ChatGPT _$_ · Codex _$_ · _n_ people**, **_n_ active users** / **_d_ active days · most used in _apps_. From the Workspace Reports API (fields to be confirmed); Google reports activity, not cost.**; columns **Person**, **Department**, **Claude**, **ChatGPT**, **Codex**, **Gemini**, **Total**; empty **No people match these filters.**; footnote **Inactive people are excluded. Cost is what each vendor billed (Claude priced from tokens at the rates in Settings). Gemini is active days in the period.**

### 3.5 AI detail

**← AI**; h1 = vendor; tabs **Claude**, **ChatGPT**, **Gemini**; **Clear filters (n)**; **Export CSV**; paragraph **Every vendor is shown in the same five-column record: Person · Department · Product · Cost · Usage (the columns-and-rows shape the AI Reports service already returns). Cost is what the vendor billed. Usage is the vendor's own unit — Claude tokens, Gemini active days; ChatGPT usage is unavailable while its feed is skipped.**; Gemini note **Assumed: Gemini fields come from the Google Workspace Reports API (active days, apps used, last active). What Google actually returns still needs confirming.**; ChatGPT note **Usage is unavailable: the ChatGPT usage feed has been skipped since 10 Sep.** + **Connection health**; columns **Person**, **Department**, **Product**, **Cost**, **Usage**; empty **No records match these filters.**

### 3.6 Ask

h1 **Ask**; input placeholder **Ask about projects, releases or AI spend**; button **Ask**; examples **Which Active projects release in the next 30 days?**, **Who spent the most on Claude in this period?**, **Which data feeds are unhealthy?**; note **Every answer shows its row count, time window, filters and the query behind it. Read-only, scoped to what your role can already see.**; card labels **Question**, **Answer** (+ **Written by the Ask model from the rows below — verify before acting.**), **Data** (uppercased); no-narrative box (2.20); chips **_n_ rows**; **Show SQL** / **Hide SQL**; **CSV**; sources **Portfolio · sample data**, **AI · _n_ active people · sample data**, **AI · scoped to your rows · sample data**, **Settings › Connection health · sample data**, **Settings excluded for this role**; row titles **Open _title_ in Portfolio**, **Open _name_ in AI › Claude**, **Open your Claude usage**, **Open the _feed_ feed controls**.

### 3.7 Settings

h1 **Settings**; Viewer notice **Settings are read-only for Viewers and their contents are not available to Ask.**; alerts box **Active health alerts** (uppercased), rows `Failed|Degraded · last good run <when> · <note>`, **Retrying…**, **Still failing**, **Retry**, **Acknowledge**.

1. **People** — helper **People are never deleted. Marking someone inactive removes them from AI.**; headers **Name**, **Email**, **Department**, **Role**, **Status**; status **Active** / **Inactive since _date_**; **Mark inactive**; confirm **Marking _name_ inactive removes them from AI. They are never deleted — their projects and history stay.** — **Cancel**, **Mark inactive**; **Reactivate**.
2. **Departments** — helper **Rename inline; renames follow through to People and AI.**; headers **Department**, **Active people**; placeholder **Add a department**; **Add**.
3. **Vendor pricing** — helper **Turns Claude tokens into cost. ChatGPT and Codex arrive priced from OpenAI; Gemini has no cost feed.**; headers **Model**, **Input $ / M tokens**, **Output $ / M tokens**, **Effective from**.
4. **Integrations** — helper **GitHub organisation _wildlife-protection-solutions_ and Jira site _wps.atlassian.net_ connected. Each project's repo entries feed its GitHub Issues (hourly) and Work Summary (commits) and are edited on the row; Jira projects feed Tasks.**; token line `GITHUB_TOKEN` **Valid** / **Expires soon** / **Expired**, `expires <date> · n days[ · rotate before then]` / `expired <date>`, **· shared with the GitHub commits feed**, **Rotate →**; headers **Project**, **Repo entry**, **Resolves to**, **Scope**, **Sync**, **Last sync**; **Edit on the row**; note `n project(s) has|have no repo entries · their tasks are typed here or from Jira. New entries are added on the row.`; Jira helper **Jira tasks sync nightly and are read-only here; tasks typed in the Portfolio stay editable. Both count in the In Progress and Planned columns.**; headers **Jira project**, **Linked project**, **Last sync**; option **Unlinked — pick a project**; **Unlink**; placeholders **Jira project key**, **Link to project…**; **Add**.
5. **Connection health & alerts** — helper **Open a feed for its schedule, credentials, run history and manual runs. Failures are emailed to the recipients below.**; headers **Feed**, **Status**, **Last successful run**, **Note**; **Controls →**; footer **Email recipients**, placeholder **Add email, press Enter**, error **Enter a valid email address.**, **Alert after** with **1 failed run** / **2 failed runs** / **3 failed runs** / **5 failed runs**, **Also alert on Degraded**; chip × title **Remove**.
6. **Public website feed** — helper **Toggle what the public website may show. The preview is exactly what the site receives.**
7. **Warden Rules** — helper **Plain-English rule on the left; the JSON integration string the Warden executes on the right. Add as many as needed.**; headers **Rule**, **Integration String (JSON)**; **Not valid JSON**; **Edit**, **Delete**; edit placeholders **Rule in plain English**, **JSON: what triggers it and who is notified**; **Not valid JSON — Save is disabled until it parses.**; **Save**, **Cancel**; confirm **Delete this rule? The Warden stops evaluating it immediately.** — **Keep**, **Delete rule**; add placeholder **New rule**; **Add**.
8. **Ask settings** — helper **What Ask may read follows each person's role; only the model and standing instructions are configurable.**; **Model**; **Scope by role** with headers **Portfolio**, **AI**, **Settings**, rows **Viewer** (`All`, `Own rows only`, `None`) and **Admin** (`All`, `Everyone`, `All`); **Standing instructions**.

### 3.8 Feed controls

**← Settings · Connection health**; status labels **OK** / **Degraded** / **Failed** / **Paused**; line **Last successful run _when_ · next run _when_ · _schedule_**; card **Controls**: **Feed** — **Running on schedule** / **Paused — no runs until resumed**, **Pause feed** / **Resume feed**; **Schedule** options **Every hour**, **Every 6 hours**, **Nightly · 06:00**, **Weekly · Monday 06:00**; **Manual run** — **Runs outside the schedule and records the result below.**, **Run now** / **Running…**, results **Completed just now** / **Partial — some data missing** / **Failed — rotate the credential and run again**; **Credential** — **Rotate**, placeholder **Paste the new key**, **Save key**, **Cancel**, **The key is stored server-side and never shown again; the next run uses it.**, meta **Rotated today · takes effect on the next run**; **Include in health alerts**. Card **Recent runs** + feed note; headers **Run**, **Result**, **Duration**, **Records**, **Message**; results **OK** / **Partial** / **Failed**; messages **Manual run**, **Manual run — vendor returned 2 of 3 pages**, **Manual run — 401 Unauthorized, credential still rejected**, **Vendor returned 2 of 3 pages; retry scheduled**, **401 Unauthorized — token expired**, **401 Unauthorized — token rejected**, **401 Unauthorized — token rejected by api.github.com**.

---

## 4. Tweak switches (prototype props)

| Prop | Type / options | Default | Effect | Status |
|---|---|---|---|---|
| `inlineRowEditing` | bool | `true` | On: expanded row cells become editors (Project Name, Owner, Type, Status, Step, Start Date, Next Release). Off: a fields block (Project Name, Owner, Type, Status, Next Release; **no Step or Start Date editor**) at the top of the panel. | **Open product decision** |
| `taskRowLayout` | `Ledger` \| `Two-line` | `Ledger` | GitHub Issues row layout (2.15.4 / 2.15.5); Ledger puts the sync note in the heading, Two-line per row. | **Open product decision** |
| `pillHover` | `Tooltip` \| `Swap` | `Tooltip` | How the GitHub Issues count pill shows the breakdown. | **Open product decision** |
| `aggregateStyle` | `Sentence` \| `Bar` | `Sentence` | GitHub Issues aggregate as a sentence or a 120px bar. | **Open product decision** |
| `taskCap` | int 5–200 | `50` | Soft cap on issues per project before the "View all" link. | Prototype switch (production: a configurable constant; 50 recommended) |
| `role` | `Admin` \| `Viewer` | `Admin` | Simulates the signed-in role. | Prototype only (production: from auth) |
| `simulateSaveFailures` | bool | `false` | Every save fails → exercises **Couldn’t save · Retry**. | Prototype only (production: real failures; keep a test hook) |
| `simulateSyncFailure` | bool | `false` | GitHub Issues feed Failed; stale note and red badges. | Prototype only |
| `tokenDaysLeft` | 0–365 | `50` | Days until `GITHUB_TOKEN` expiry; amber at ≤ 30, expired at 0; drives feed status and Integrations token line. | Prototype only (production: real expiry from the credential store) |

---

## 5. Discrepancies and ambiguities (README vs HTML/screenshots)

Where the README and the prototype disagree, the HTML is what was screenshot-approved; the reading adopted for implementation is given in each item.

1. **AI table columns.** README §2 lists `Person · Department · Claude cost · Claude tokens · ChatGPT · Codex · Gemini (active days)`. The HTML (`AI_COLS`) and `02-ai.png` have `Person · Department · Claude · ChatGPT · Codex · Gemini · Total` — there is no tokens column and there is a Total. Reading: implement the HTML (7 columns incl. Total; the weights `200 160 120 130 120 120 120` match those 7). Tokens remain on the Claude card and in AI detail.
2. **Filter popover width.** README says "width = header cell width"; the HTML uses a fixed 260px anchored to the header cell's right edge and clamped to the viewport. Reading: 260px fixed (a 84px-wide Type column could not host the popover otherwise).
3. **Step circle fill.** README: filled gold with ✓ for *passed* steps, white with the number otherwise. HTML/screenshot: the **current** step is also filled gold and shows its number (`i <= idx`). Reading: HTML.
4. **Approval jump.** README: "current step = max(current, highest step index with a link) once approved". HTML adds a floor of 2: approving alone moves a Discovery/Design project to the approval step. Reading: HTML (`max(idx, 2, maxLinked)`).
5. **Manual step selection is ungated.** README says clicking a step header sets it as current; the HTML does so with no approval check, so an unapproved project can sit at Code. Ambiguity: whether production should block or auto-tick Approved. Reading: keep the prototype behaviour and flag for the product owner; the auto-step rule only ever raises the step.
6. **Link-name fallback.** README: "empty name falls back to the URL host+path"; HTML strips only the scheme (query string kept). Reading: HTML.
7. **Feed list.** README lists 8 feeds "(+ Copilot in run history)"; HTML/screenshot show 10 rows including **Copilot** (Failed) and **WPS Agents** (Degraded), both with controls pages and both counting towards alerts. Reading: 10 feeds; whether Copilot/WPS Agents exist in production is a data question, not a UI one.
8. **Status label wording.** README: "Healthy"; HTML: **OK** (plus **Paused**). Reading: **OK**.
9. **Range label example.** README's "1 Sep 2026 – 23 Sep 2026 · 30 days" is arithmetically wrong; 30 days ending 23 Sep starts 25 Aug (HTML and screenshot). Reading: `start = end − (days − 1)`.
10. **SQL toggle label.** README "SQL / Hide SQL"; HTML **Show SQL** / **Hide SQL**. Reading: HTML.
11. **AI page empty state** (**No people match these filters.**) is absent from the README. Reading: include it.
12. **Viewer restrictions are documented but mostly not implemented in the prototype.** The AI/AI-detail tables show every person regardless of role; Settings controls remain enabled for Viewers; only the Ask answers, the alerts box/badge and the notice differ. Reading: enforce README semantics server-side (Viewer → own AI rows, Settings read-only and excluded from Ask) and mirror them in the UI; the prototype is not a reference for these paths.
13. **Sum-row "distinct".** The column config calls it `distinct` but the computation is a non-empty count (README wording "n items (non-empty count)" is right). Reading: non-empty count; name the function accordingly.
14. **Filter value list ignores the search box and empties.** Values come from rows passing the other column filters only — the header search is not applied — and `EMPTY` values are never listed, so a user cannot filter *for* blank Next Release/Start Date. README does not say either. Reading: keep prototype behaviour; consider a "(blank)" entry as a product question.
15. **"Clear filters (1)" on first load.** Because the Open view is a Status filter, the button shows immediately (screenshot). README implies it appears "only when any filter or sort is active" — consistent, but worth calling out for tests.
16. **Alert threshold is inert.** "Alert after n failed runs" is stored but never evaluated; the badge counts current status only. Reading: production evaluates the threshold in the ingestion service; the UI contract is unchanged.
17. **Jira "Unlink" removes the row**, whereas deleting a project only clears `initId` (README: "Jira projects are unlinked, not deleted"). Reading: **Unlink** should set `initId = ''` and keep the row (matches the copy and the project-delete behaviour); confirm with the product owner.
18. **Warden JSON validation on add.** README lists "Warden JSON must parse" under Validation, but the add row accepts invalid JSON (only the edit row disables Save). Reading: validate on add as well.
19. **Sub-task add hint.** Copy says **starts today, 3 days, chained** but the new sub-task is chained only when an open sub-task already exists. Reading: keep the logic (a first sub-task has nothing to chain to).
20. **Undo restores a list snapshot**, not just the deleted task — edits made to sibling tasks within the 8 s window are lost. Not in the README. Reading: implement undo as re-insertion of the deleted task at its original index instead; note the deviation.
21. **Chain colour order** is by anchor **id** (creation order), not by position in the list; the README's "Chain colours (in order)" is ambiguous. Reading: HTML (stable colours when reordering). Legend letters follow the same order.
22. **Drop prompt only for top-level tasks.** README describes the prompt generally; the HTML never prompts inside a sub-task list. Reading: HTML.
23. **`tasks` vs `task<id>` save keys.** Most task mutations save under `tasks`; the drop-prompt choices save under `task<id>`. Reading: production saves per task/ordering endpoint; the row status line just needs one aggregated state.
24. **Floating Ask button on the Ask page.** README says it navigates to Ask; the HTML computes `notAsk` but never uses it, so the button shows on Ask too. Reading: hide it on the Ask page.
25. **Screenshot select values are unreliable.** In `05-settings.png` every People row shows `Dev`/`Viewer` and every Jira row shows **Unlinked — pick a project**, contradicting the data (Ingrid Solberg is Executive/Admin; AIDASH→AI Usage Dashboard, as the Tasks heading **Jira · AIDASH** proves). This is a prototype select-binding artefact (numeric vs string values). Reading: trust the data, not the screenshot, for select states. Likewise inline date inputs display in the browser's locale (`09/20/2026`); production should use one consistent format.
26. **CSV file names** end in `-sample-data.csv` in the prototype. Reading: `.csv` only.
27. **CSV guard set.** Prototype guards `= + @ -`; the service's `CsvExporter.cs` also guards `\t` and `\r`. Reading: the client mirrors the server (superset).
28. **Repo entry duplicates and department duplicates** are not prevented. README silent. Reading: prevent duplicates server-side (unique pattern per project; unique department name).
29. **`inlineRowEditing = false` loses editors.** The fields block has no Step or Start Date editor, so with the tweak off those fields can only be changed via the step header / not at all. Reading: if the switch is kept, add both to the fields block.
30. **GitHub Issues heading pill counts only visible issues** (not-planned hidden), while `seen` (feed note) counts everything in scope including children. README's "n epics|top-level issues" tooltip matches the visible count. Reading: HTML.
31. **Work Summary trigger on open.** README: "Opening an idle summary triggers generation." HTML also re-triggers a `norepo` summary once a repo has been linked, and *never* auto-regenerates a `cached` or `failed` one. Reading: HTML.
32. **Auto-fill on enable.** README: copies "on each generation"; HTML also copies immediately when the checkbox is turned on and a cached summary exists. Reading: HTML.
33. **Public feed JSON** uses the stored designation (no At Risk) and omits owner/desc. README says "the exact JSON the site receives" without fields. Reading: HTML field set `name, type, designation, step, nextRelease`.
34. **`Alert after` and recipients are global**, not per feed; per-feed **Include in health alerts** is the only per-feed switch. README consistent; noted for the data model.
35. **README "Columns" for Portfolio** give min widths but the Start Date/Next Release inline editors are `type=date` at 13.5px — with a 108px min the native picker overflows in some browsers. Implementation note only.

