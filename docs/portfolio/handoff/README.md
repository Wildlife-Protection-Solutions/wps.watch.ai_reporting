# Handoff: WPS Portfolio

## Overview

WPS Portfolio is an internal reporting and light project-management app for Wildlife Protection Solutions. It replaces the tiles-and-boards "AI Dashboard v1" with four pages:

- **Portfolio** — a ledger of projects with Excel-style column filters, inline editing, steps, tasks (typed here or synced from Jira), GitHub Issues, an AI-written Work Summary from commits, and repo links.
- **AI** — per-vendor AI spend and usage by person (Claude, ChatGPT incl. Codex, Gemini), with a detail table per vendor.
- **Ask** — natural-language questions answered from the same data with a written answer plus the evidence rows, window, filters, query and CSV.
- **Settings** — People, Departments, Vendor pricing, Integrations (GitHub + Jira), Connection health & alerts (with a per-feed controls page), Public website feed, Warden Rules, Ask settings.

Roles: **Admin** and **Viewer** (mapped to `Roles.SystemAdmin` / `Roles.Viewer` in the wpsWatch AI Reports service). Viewers see Portfolio and their own AI rows; Settings is read-only for them and hidden from Ask.

Source repo used for visual tokens and table/filter/chat patterns: `Wildlife-Protection-Solutions/wps.watch.ai_reporting` (branch `main`). See `github.md` in this bundle for the screen ↔ repo-file map.

## About the Design Files

The files in this bundle are **design references written in HTML** (`WPS Portfolio.dc.html` plus `support.js`, a small rendering runtime). They show the intended look and behaviour with fictional sample data. They are **not production code to copy**. The task is to **recreate these screens in the target codebase's existing environment** — the wpsWatch AI Reports client (React, `ClientApp/src`) with its `styles.css` tokens, `autofilter.jsx`, `chrome.jsx` and `chat.jsx` patterns — or, if the Portfolio app is started fresh, in the framework chosen for it, reusing the same tokens.

Only the project names in the sample data are real. People, spend, tokens, links, rules, summaries and dates are invented. `TODAY` is pinned to `2026-09-23` in the prototype; use the real clock.

## Fidelity

**High-fidelity.** Colours, typography, spacing, copy, states and interactions are final and should be recreated as specified. Where the wpsWatch stylesheet already has a token for a value listed here, use the token.

## Global chrome

- Page background `#f1ede4` (cream). Body text `#1a1a2e`, 14px / 1.45, `"Segoe UI", system-ui, -apple-system, sans-serif`, antialiased.
- **Top bar**: 56px tall, `#c8c5be`, bottom border 1px `#b9b3a4`, sticky, z-index 20, 0 20px padding, 22px gap. Left: 40×40 WPS mark (`assets/wps-logo-small.png`) + "WPS Portfolio" 15px/600. Nav: PORTFOLIO · AI · ASK · SETTINGS as 12.5px/700 uppercase, letter-spacing .6px, 0 12px padding, full 56px height; active item text `#a37a2f` with a 2px bottom border `#b88c3a`; Settings shows a red badge (`#b8442e` pill, white 11px/700) with the count of active health alerts (Admins only). Right: "SAMPLE DATA" tag (10.5px/700 uppercase, `#8a6524`, 1px `#d9b878` border, radius 3, 2px 7px) and a user button (1px `#b9b3a4` border, radius 4, 5px 12px, name + ▾) that opens a menu (white, 1px `#d8d4c8`, radius 6, shadow `0 8px 24px rgba(20,25,20,.16)`, min-width 240) with name, email, "Role: Admin|Viewer", and **Log out**.
- **Sample-data strip** under the bar: `#f8e8c9` background, 1px `#e6cf9a` bottom border, `#6b4d0b` 12.5px text, 6px 20px padding: "SAMPLE DATA · This prototype runs on fictional data. People, spend, tokens, links, rules and summaries are made up; only the project names are real. Nothing is connected to a live system." Drop this strip in production.
- Content column: max-width 1560, centred, padding 22px 20px 96px.
- **Floating Ask button**: fixed right 24 / bottom 24, `#b88c3a` (hover `#a37a2f`), white 14px/600, pill radius, 12px 20px padding, shadow `0 6px 18px rgba(20,25,20,.18)`, z-index 30. Navigates to Ask.
- **Sign-in screen** (signed-out state): full-viewport `#1a1a2e` with the white logo (`assets/wps-logo-white.png`) at 6% opacity bottom-right; centred white card (radius 8, 36px 40px, max-width 380): 96px logo, "WPS Portfolio" 20/600, "Same wpsWatch login, same roles. Sessions stay signed in until you log out." 13.5px `#6b6a63`, gold button "Sign in with wpsWatch", "SAMPLE DATA · PROTOTYPE" 11px/700 `#8a6a12`.

## Screens / Views

### 1. Portfolio

**Purpose**: see every project at a glance, filter/sort like a spreadsheet, and open a row to edit it and manage its steps, tasks, GitHub issues, summary and repos.

**Header row** (flex, space-between, wrap, 14px bottom margin): "Portfolio" h1 20px/600 letter-spacing −.01em; a segmented view switch **Open | Complete** (white, 1px `#d8d4c8` border, radius 4; active segment `#b88c3a` bg, white 600 text). Right group: search input (270px, placeholder "Search names, owners, descriptions", 1px `#d8d4c8`, radius 4, 6px 10px, focus border `#b88c3a`) matching title/owner/description/status/next; "Clear filters (n)" (only when any filter or sort is active; gold outline `#d9b878`, text `#8a6524`, hover bg `#efe2c4`); "Export CSV" (white, 1px `#d8d4c8`); "+ New project" (gold `#b88c3a`, white 13px/600, radius 4, 7px 14px; disabled at 50% opacity while a draft exists).

**Table container**: white, 1px `#d8d4c8`, radius 6, `overflow-x:auto`.

**Columns** (in order, with per-column min width): 40px chevron lead, then Project Name (min 150) · Owner (90) · Type (84) · Status (110) · Step (84) · Start Date (108) · Next Release (110) · Next Step (150). Tracks are fluid: `grid-template-columns: 40px minmax(<min>px, <weight>fr) …` with default weights 230 140 120 130 120 130 140 250. The table fills the container; when the container is narrower than the sum of minimums it scrolls horizontally.

**Column resizing**: a 8px-wide handle on the right edge of each header cell (`cursor:col-resize`, hover bg `#d9b878`). Dragging sets that column to a fixed pixel width (min = its min width) that follows the mouse 1:1; undragged columns stay fluid. The last column keeps filling: a dragged width there becomes its minimum.

**Sum row** (above headers): `#f6f3eb` bg, 1px `#d8d4c8` bottom border, 8px 10px cells, 13px/600, tabular numbers. Lead cell "Σ" in `#9c9a8e`. Per column: Project Name "n items"; Owner/Type/Status/Step "n items" (non-empty count); Start Date "n not started" or "all started" (grey `#9c9a8e`); Next Release "n past due" in red `#b8442e` or "none past due" grey; Next Step "n missing" or "all set" grey.

**Header row**: `#ebe7dc` bg, 1px `#d8d4c8` bottom border. Each header cell is flex (gap 4, padding 6px 6px 6px 10px, `position:relative`, `min-width:0`): a sort button (label 12px/800 uppercase letter-spacing .5px `#3a3a4a`, ellipsizes when narrow; sort mark ↕ in `#b9b3a4`, or ↑/↓ in `#b88c3a` when sorted) and a 20×20 filter button "▾" (`#9c9a8e`; when a filter is active: `#b88c3a` bg, white glyph, plus a 7px gold dot with 1.5px white ring at top-right; hover bg `#efe2c4`).

**Filter/sort popover** (anchored under the header cell, fixed-position, width = header cell width, white, 1px `#d8d4c8`, radius 6, shadow `0 16px 40px rgba(34,30,18,.22), 0 2px 8px rgba(34,30,18,.1)`, max-height 360, z-index 50, 13px):
1. Two sort rows: "▲ Sort A → Z" / "▼ Sort Z → A" (dates/numbers: "Sort smallest → largest" / "Sort largest → smallest"), 12.5px, hover `#f6f3eb`.
2. "FILTER · <Column>" 10.5px/700 uppercase.
3. Search box "Search values…" (1px `#d8d4c8`, radius 4).
4. "All values" / "Select all" checkbox row with "n values" count.
5. Scrollable value list: checkbox, value (ellipsis), count in `#6b6a63`. Values are the column's *displayed* strings for rows passing every other column's filter. Selecting none is represented by a sentinel so the table shows no rows rather than all.
6. Footer (`#f6f3eb`): "Clear filter & sort" (underlined, `#8a6524` when there is something to clear, else `#b9b3a4` disabled) and a gold **Done** button.
Closes on outside mousedown, Escape, or scroll outside the popover. Filters accent-colour checkboxes with `#b88c3a`.

**Default view**: Status filter = At Risk, Active, Ongoing, Future, Experimental (the "Open" view). "Complete" view = Status filter Complete only. The segment is highlighted only when the filter exactly matches.

**Rows**: grid with the same tracks, 1px `#e4e0d4` bottom border, `cursor:pointer`, white (hover `#fcf8ec`). Cells 9px 10px, tabular, single line with ellipsis. Chevron ▸/▾ 18px `#3a3a4a` in the lead cell. Project Name 500 weight ("Untitled" / "New project" in `#9c9a8e` when empty). Empty values ("—") in `#9c9a8e`.
- **Expanded row**: bg `#e6d3a3`, hover `#dfc78f`.
- **At Risk row** (Next Release is before today and Status ≠ Complete): row bg `#f6dcd5`, hover `#f0cdc3`; when also expanded `#eec3b8`, hover `#e8b5a8`. The Status cell reads **At Risk** in `#b8442e` 600 (the stored status — e.g. Active — is unchanged and still editable). Next Release cell text turns `#b8442e` and gets a "PAST DUE" tag (9.5px/700 uppercase letter-spacing .06em, `#b8442e`, 1px `#e8c2b8` border, radius 3, 1px 4px). "At Risk" is a derived display value: filtering, sorting (it sorts first), the Open view, and CSV all use it.
- Next Step cell shows the project's Next Step text; the cell's `title` carries the full text.

**Inline row editing** (`inlineRowEditing` on, default): when a row is expanded, its cells become editors bound to the same fields: Project Name / Owner text inputs (1px `#d9b878`, radius 3, 4px 6px, focus `#b88c3a`), Type / Status / Step selects (Step options depend on Type), Start Date / Next Release `type=date` inputs (13.5px; Next Release text red when past due). Clicks inside editors don't collapse the row. Next Step stays read-only in the row (edited in the narrative block). With the tweak off, a **fields block** appears at the top of the panel instead: grid `minmax(0,1.6fr) repeat(4,minmax(0,1fr))`, gap 14px 18px, labels 11px/600 uppercase letter-spacing .07em `#6b6a63`, underline inputs (border-bottom 1px `#b9b3a4`, focus `#b88c3a`); required fields show a red asterisk on drafts. Fields: Project Name*, Owner*, Type* (Software | Implementation), Status* (Active | Ongoing | Future | Experimental | Complete), Next Release.

**Expanded panel** (`#fbf9f4`, top border 1px `#d9b878`, padding 16px 20px 18px 50px). Sections in order:

1. **Draft banner** (new projects only): white box, "New project. Project Name, Owner, Type and Status are required; dates are pre-filled and editable." with **Discard** and **Create project** (gold; disabled/50% until Title, Owner, Type, Status are set). Creating opens the Tasks section.
2. **Save status line**: "Saving…" `#6b6a63` → "Saved" `#2f8a4f` (fades after 1.6s) or "Couldn't save · Retry" `#b8442e`. Right-aligned: "Delete project" (red underlined link, saved rows only) and "Collapse ▴".
3. **Delete confirmation** (`#f6dcd5` box, 1px `#e8c2b8`): "Delete **<title>**? Its n tasks, repo entries, links and narrative go with it, and it leaves the public website feed. Jira projects are unlinked, not deleted." Buttons **Keep** and red **Delete project** (`#b8442e`).
4. **Steps** (when Type is set). Caption: "STEPS · adding a link moves the step forward; the approval checkbox gates everything after it". Six equal columns. Software: Discovery, Design, Prototype Approved, Code, Final Testing, Release. Implementation: Discovery, Design, Project Approved, Implement, Final Testing, Deploy. Each column: 2px bottom rule (`#b88c3a` on the current step, else `#b9b3a4`), a 22px circle (filled `#b88c3a` with white ✓ for passed steps, white with `#b9b3a4` border and the step number otherwise), the step name (600 on the current step; `#6b6a63` for future steps). Clicking a step header sets it as current. Step 3 is the approval gate: an "Approved" checkbox instead of links. Other steps list links (13.5px, ✎ edit glyph) and a "+ Link" pill (white, 1px `#d8d4c8`, radius 999). Link editor: white card (1px `#d9b878`, radius 4) with Link name + https:// underline inputs, gold **Save** (disabled until URL), Cancel, Delete (edit mode). Enter saves, Escape cancels; a URL without scheme gets `https://`; empty name falls back to the URL host+path.
   Auto-step rule: current step = max(current, highest step index with a link) once approved; before approval it can't advance past Design. Un-approving pulls the step back to at most Design.
5. **Narrative**: three equal columns (gap 18) of labelled textareas (13px/600 labels, 3 rows, 1px `#d8d4c8`, radius 4, 8px 10px, 14px/1.5, vertical resize, focus `#b88c3a`): **Project Description** ("What this project is and why it exists"), **Current Status** ("Where it stands today"; shows a small "FROM WORK SUMMARY" label in `#a37a2f` when auto-fill is on), **Next Step** ("What happens before the next review").
6. **Tasks** (saved rows). Heading row: ▸/▾ chevron button + "TASKS" + "typed here or synced from Jira", a count pill "In Progress n · Planned n" (pill, 1px `#d8d4c8`; open state bg `#efe2c4`), "Jira · KEY" link if a Jira project is linked, and when open a "show Complete (n)" checkbox. Both the chevron and the pill toggle the list.
   Task table (white, 1px `#d8d4c8`, radius 4, max-width 1140, scrolls): header `#f6f3eb` 10.5px/800 uppercase; grid `22px 112px minmax(160px,1fr) 72px 116px 52px 84px 36px 84px` = drag handle · Status · Task · Sub-tasks · Start · Days · End · Chain · Source. Rows have a 3px left border and tinted background in the chain colour.
   - Status: select (Planned | In Progress | Complete) for typed tasks; read-only text with "– Jira" suffix for Jira tasks and "· sub-tasks" for tasks whose status rolls up from sub-tasks.
   - Task: inline title input for typed tasks; read-only title + "– Jira" for Jira. Complete tasks are struck through in `#9c9a8e`. A small gold pill shows "n links" when the task has links.
   - Sub-tasks: button "▸ done/total" (or "+" when none) opening the task's links and sub-tasks.
   - Start: date input when editable; "⛓ date" when chained (follows previous task's end); "Σ date" when rolled up from sub-tasks; a 9px dot in the chain colour marks a chain anchor. Complete tasks show "—".
   - Days: number input (min 1); End is computed = start + days.
   - Chain checkbox: "start when the previous task ends". Disabled on the first task and on rolled-up tasks.
   - Source: Jira key as a mono link chip to `https://wps.atlassian.net/browse/<KEY>`; typed tasks show "typed" and a × delete.
   - Drag ⋮⋮ to reorder. A chain (consecutive chained tasks + their anchor) moves as one block; drop indicator is a 2px gold inset line. Dropping an unchained dated task under a task whose end differs from its start shows a prompt bar (`#fcf6e8`): "You moved **X**. It still starts <date>; the task above it, **Y**, ends <date>." with **Chain after it · start <date>** (gold), **Just move the date**, **Keep <date>**.
   - Deleting a task shows "Deleted **title**. Undo" for 8 seconds.
   - Add row: Status select, "Add a task and press Enter", start date (default today), days (default 5), **Add**.
   - Footer note: "Rows marked "– Jira" are synced from Jira: status and title change there, but start, days, chain and order are set here. Tasks in a chain share a colour and each starts when the one above it ends. An unchained task with followers starts a new chain (●). Drag ⋮⋮ to reorder; a chain moves as one block. ▸ opens a task's links and sub-tasks." Right side: legend "● Chain A", "● Chain B"… in the chain colours.
   - **Task drawer** (`#fbf9f4`): "LINKS" row with link chips + "+ Link" (same link editor); "SUB-TASKS · n of m complete" and a nested table with the same columns minus Source (last col 44px for ×): sub-tasks have status, title, links, start/days/end, chain and drag-reorder. Add row: "Add a sub-task and press Enter" · "starts today, 3 days, chained". Roll-up: parent status = Complete when all sub-tasks complete, In Progress when any has started or completed, else Planned; parent start = earliest open sub-task start, end = latest end; parent chain is forced off.
   - Chain colours (in order): `#b88c3a`, `#2f8a4f`, `#5a6b7e`, `#b8442e`, `#7b5ea7`, `#c98a1e`; row tints `#fcf6e8`, `#eef6f0`, `#eef1f4`, `#faeceb`, `#f2eef7`, `#fbf1e0`.
7. **GitHub Issues** (only when at least one repo entry has sync on). Heading: chevron + "GITHUB ISSUES", count pill "in progress n" (hover shows a tooltip "n epics|top-level issues · n in progress · n done · n not planned hidden" — or swaps the pill text, per tweak), "show done (n)" checkbox when open, aggregate sentence "n top-level issues · d / t sub-issues closed" (or a 120px progress bar variant), and right-aligned sync note "synced 12 min ago" `#9c9a8e` (or "sync failed · last synced 6 h ago" in `#b8442e`).
   List (white card, max-width 1140). **Ledger** layout grid `22px minmax(180px,1fr) 58px 112px 150px 86px [124px repo] 26px`: expand chevron · Issue title (strike-through + grey when done) · "GITHUB" badge (9.5px/700, 1px `#d8d4c8`; red-tinted `#e8c2b8` border when sync failed) · status text ("in progress" / "done", with a red note "closed in GitHub · n sub-issues open" when applicable) · sub-issue progress (84px bar `#e4e0d4` with `#b88c3a` fill + "d / t") · assignee initials (18px circles, `#efe2c4` bg, `#8a6524` text, max 3 then "+n") · repo (mono 11px, only when the project spans several repos) · ↗ link to the issue. **Two-line** layout: title line with badge, avatars, ↗; meta line with status, bar, repo, sync note. Expanding lists sub-issues (`#fbf9f4` rows: 8px dot — filled gold when closed, dashed when unreadable — title, status, first assignee initial, repo, ↗; open ones first; 10 shown then "show all n"; a placeholder "1 child in another repo — not readable" for unreadable children). Issues closed as "not planned" (and their children) are hidden. Sorted by last update; capped at `taskCap` (default 50) with "View all n in GitHub · showing the 50 most recently updated". Empty states: "All n … are done · tick show done to list them" or "no in-scope issues in n repos".
   Scope rule: an issue counts if its repo matches a synced entry and the entry scope is "all issues" or the issue carries the entry's label.
8. **Work Summary** (saved rows). Heading: chevron + "WORK SUMMARY" + meta ("cached · 21 Sep 2026, 06:10" / "generating…" / "failed" / "no repository linked" / "n repositories"), then GitHub repo links ("GitHub Repository", "GitHub · repo", or "GitHub · n repositories" for >3). Opening an idle summary triggers generation. Body card (white, max-width 880, 14px/1.55): loading "Summarising recent commits from n linked repositories…" (pulsing opacity .45↔1, 1.4s); failure "Couldn't generate a summary — <error>. Retry"; no repo "No repository is linked to this project, so there is nothing to summarise. Link one in Settings › Integrations."; cached: paragraph with repo names as links, then "Cached · generated <when> · summarised by the Ask model from commit history · Regenerate". Footer checkbox "Auto-fill Current Status from summary" copies the text into Current Status on each generation.
9. **Repos** (all rows; max-width 780). Heading: chevron + "REPOS" + "exact name or prefix pattern · issues sync hourly, commits feed Work Summary", count pill "n entries · m repos" (or "none"), right-aligned save status. Entries grid `minmax(150px,1.3fr) minmax(110px,1fr) 176px 58px 20px`: mono pattern · "→ n repos ▸" (click expands the resolved repo names; "→ no repos match" in red) · scope select (all issues | label:) with a mono label input when "label:" · "sync" checkbox · ×. Add row: "Add a repo or prefix pattern, e.g. argus-*" with a live "→ n repos" preview and **Add**. A pattern is an exact repo name or a prefix ending in `*`, resolved against the organisation's repo list. Empty: "No repos · no GitHub Issues or Work Summary for this row."

Empty table: "No projects match these filters."

### 2. AI

**Purpose**: what AI costs and who uses it, per vendor, for a period.

Header: "AI" + range label "1 Sep 2026 – 23 Sep 2026 · 30 days"; right: segmented **7 days | 30 days | 90 days | Custom** (Custom reveals two date inputs), "Clear filters (n)", "Export CSV". Amber notice when a feed is unhealthy: "<feed note> · Connection health" link.

Three cards (grid, 3 equal columns, gap 14; white, 1px `#d8d4c8`, radius 6, 14px 16px): label 11px/600 uppercase + "Details" link; value 28px/600 letter-spacing −.02em tabular; sub-line 13px `#6b6a63`.
- **Claude**: total cost; "n tokens · n people".
- **ChatGPT incl. Codex**: total; "ChatGPT $ · Codex $ · n people".
- **Gemini**: "n active users"; "n active days · apps. From the Workspace Reports API (fields to be confirmed); Google reports activity, not cost."

Table (same machinery as Portfolio: sum row, filter/sort headers, fluid resizable columns, weights 200 160 120 130 120 120 120): Person · Department · Claude cost · Claude tokens · ChatGPT · Codex · Gemini (active days). Rows hover `#fcf8ec`. Footnote: "Inactive people are excluded. Cost is what each vendor billed (Claude priced from tokens at the rates in Settings). Gemini is active days in the period."

### 3. AI detail

"← AI" back link; h1 = vendor; segmented **Claude | ChatGPT | Gemini** tabs; range label; Clear filters; Export CSV. Explanatory paragraph: "Every vendor is shown in the same five-column record: Person · Department · Product · Cost · Usage (the columns-and-rows shape the AI Reports service already returns). Cost is what the vendor billed. Usage is the vendor's own unit — Claude tokens, Gemini active days; ChatGPT usage is unavailable while its feed is skipped." Amber notes for Gemini assumptions and the skipped ChatGPT usage feed. Five-column table (weights 200 160 200 120 150) with the same filter/sort/resize behaviour. Empty: "No records match these filters." Ask "person" rows deep-link here with the Person filter preselected.

### 4. Ask

Max-width 900. h1 "Ask"; form with input (15px, placeholder "Ask about projects, releases or AI spend", 10px 12px) and gold **Ask** button; example question pills ("Which Active projects release in the next 30 days?", "Who spent the most on Claude in this period?", "Which data feeds are unhealthy?"); note "Every answer shows its row count, time window, filters and the query behind it. Read-only, scoped to what your role can already see."

Answer card (white, 1px `#d8d4c8`, radius 6): **QUESTION** + text · **ANSWER** ("Written by the Ask model from the rows below — verify before acting.") paragraphs 14.5px/1.6, or an amber "No written answer for this question yet…" box · **DATA** one-line summary · chips "**n** rows", window, and mono filter chips (`#f6f3eb`) · evidence table (rows clickable: project rows open that project in Portfolio, person rows open AI detail filtered to them, feed rows open the feed controls) · footer with **SQL / Hide SQL** toggle (query shown in a dark `#1a1a2e` pre) , **CSV**, and source text right-aligned. Viewers asking about feeds get: "Connection health lives in Settings, which your role cannot see, so Ask cannot answer this for you…".

Routing in the prototype is keyword-based (feed/health → feeds; claude/spend/cost → Claude spend; release/due/ship → releases); in production this is the Ask model over MCP.

### 5. Settings

Left rail (170px, sticky at top 72): numbered section list 01–08 (13.5px, hover `#efe2c4`); clicking scrolls to the section. Viewer notice: "Settings are read-only for Viewers and their contents are not available to Ask." Active health alerts box at top (`#f6dcd5`, 1px `#e8c2b8`): "ACTIVE HEALTH ALERTS" then per alert: feed name (red/amber), "Failed|Degraded · last good run <when> · <note>", **Retry** (gold; "Retrying…" pulsing, then "Still failing") and **Acknowledge**.

Sections are white cards (1px `#d8d4c8`, radius 6) with a header (12px 16px, number in `#9c9a8e`, h2 15px/600, helper text 13px `#6b6a63`) and grid tables with `#f6f3eb` 11px/800 uppercase headers.

1. **People** — "People are never deleted. Marking someone inactive removes them from AI." Columns Name · Email · Department (select) · Role (Viewer | Admin) · Status ("Active" / "Inactive since <date>") · action (**Mark inactive** with an amber confirm "Marking **name** inactive removes them from AI. They are never deleted — their projects and history stay." → red **Mark inactive**; **Reactivate** link for inactive people).
2. **Departments** — rename inline (renames follow through to People and AI); "Active people" count; add row.
3. **Vendor pricing** — Model · Input $ / M tokens · Output $ / M tokens · Effective from. Claude Opus 4.1 15/75, Sonnet 4.5 3/15, Haiku 4.5 1/5. "Turns Claude tokens into cost. ChatGPT and Codex arrive priced from OpenAI; Gemini has no cost feed."
4. **Integrations** — "GitHub organisation **wildlife-protection-solutions** and Jira site **wps.atlassian.net** connected…". Token line: dot + `GITHUB_TOKEN` + state (Valid `#2f8a4f` / Expires soon `#c98a1e` at ≤30 days / Expired `#b8442e`) + "expires <date> · n days" + "· shared with the GitHub commits feed" + **Rotate →**. Repo entries table: Project · Repo entry (mono) · Resolves to · Scope · Sync · Last sync · "Edit on the row" link (opens the project). Then Jira: "Jira tasks sync nightly and are read-only here…" table Jira project (mono link) · Linked project (select, "Unlinked — pick a project") · Last sync · **Unlink**; add row with key + project.
5. **Connection health & alerts** — Feed · Status (Healthy `#2f8a4f` / Degraded `#c98a1e` / Failed `#b8442e` dot + label) · Last successful run · Note · **Controls →**. Feeds: Claude, ChatGPT cost, ChatGPT usage, Codex, Gemini, GitHub, GitHub Issues, Jira (+ Copilot in run history). Footer (`#f6f3eb`): **Email recipients** chip list (`#efe2c4` pills with ×; "Add email, press Enter"; "Enter a valid email address." on invalid), **Alert after** 1/2/3/5 failed runs, "Also alert on Degraded".
6. **Public website feed** — two columns: checkbox list of projects with their status in grey; right, a mono `<pre>` of the exact JSON the site receives.
7. **Warden Rules** — Rule (plain English) · Integration String (JSON, mono, ellipsis; "Not valid JSON" in red) · **Edit** / **Delete**. Edit mode (`#fbf9f4`): rule input + JSON textarea; Save disabled until JSON parses. Delete confirm: "Delete this rule? The Warden stops evaluating it immediately." Add row.
8. **Ask settings** — Model select (Claude Sonnet 4.5 | Opus 4.1 | Haiku 4.5), "Scope by role" matrix (Viewer: Portfolio All · AI Own rows only · Settings None; Admin: All · Everyone · All), Standing instructions textarea.

### 6. Feed controls (per feed)

"← Settings · Connection health"; h1 feed name + status dot/label + source text; "Last successful run <when> · next run <when> · <schedule>". Two cards (`auto-fit, minmax(360px,1fr)`):
- **Controls**: Feed (Enabled/Paused + **Pause/Resume**), Schedule select (Every hour | Every 6 hours | Nightly · 06:00 | Weekly · Monday 06:00), Manual run (**Run now** → "Running…" → result message), Credential (masked value + meta, **Rotate** → password input "Paste the new key", **Save key**, Cancel; "The key is stored server-side and never shown again; the next run uses it."), "Include in health alerts".
- **Recent runs**: Run · Result (OK `#2f8a4f` / Partial `#c98a1e` / Failed `#b8442e`) · Duration · Records · Message.

## Interactions & Behavior

- **Navigation**: top nav buttons switch pages and scroll to top. Settings badge = number of un-acknowledged Failed (and, if enabled, Degraded) feeds with alerts on.
- **Row expand**: click anywhere on a row (except inside editors) toggles the panel; the chevron flips ▸/▾. Multiple rows can be open.
- **Saves**: every field change saves on blur/change per field key; UI shows Saving… → Saved (1.6s) → nothing, or Couldn't save with Retry (retries only the failed keys). No transitions other than the pulse keyframe (`0%,100% opacity .45; 50% 1`, 1.4s ease-in-out) on loading text.
- **Filters** apply per column and combine with the search box; sums recompute from the filtered rows. Sort toggles asc → desc → none on the header label.
- **CSV export**: UTF-8 BOM, every cell quoted, cells starting with `= + @ -` prefixed with `'` (formula-injection guard), columns = visible labels, rows = filtered/sorted rows with displayed values.
- **Responsive**: all layouts are flex/grid with wrapping headers; tables scroll horizontally only below their minimum widths. Settings uses a fixed 170px rail.
- **Keyboard**: Enter adds tasks/sub-tasks/repo entries/departments/recipients/rules and saves link editors; Escape closes popovers, the user menu and link editors.
- **Validation**: draft projects need Title, Owner, Type, Status; link editors need a URL; recipients must be valid emails; Warden JSON must parse; task days ≥ 1.

## State Management

Per-user session: `page`, `signedIn`, `role`.
Portfolio: `initiatives[]` (see data model), `expanded{}`, `tasksOpen{}`, `showDone{}`, `taskOpen{}` (drawer per task), `ghOpen{}`, `ghShowDone{}`, `ghExp{}`/`ghAll{}` (issue children), `recentOpen{}`, `repoOpen{}`, `repoListOpen{}`, drafts for new task / sub-task / repo pattern per row, `taskDrag` / `taskDrop` / `taskPrompt`, `taskUndo` (8s), `linkEdit` / `tlinkEdit`, `confirmDelete`, `saves{ "<id>:<field>": inflight|saved|failed }`.
Tables: `tbl[tableId] = { filters: { col: { text, selected[] } }, sort: { key, dir } | null }`, `colW[tableId][]` (fluid weights), `colFix[tableId]{}` (fixed px after drag), `pop` (open filter popover).
AI: `range { preset: 7|30|90|'custom', start, end }`, `aiTab`.
Settings: `people[]`, `depts[]`, `pricing[]`, `jira[]`, `warden[]`, `alerts { recipients[], threshold, includeDegraded, acked{}, retry{} }`, `feedCfg{ name: { enabled, schedule, credential, alerts, runs[], running, runMsg, rotating } }`, `askCfg { model, instructions }`.
Ask: `askText`, `askAsked`, `askKey`, `askSql`.

**Data model (project)**: `id, title, owner, type ('Software'|'Implementation'), designation ('Active'|'Ongoing'|'Future'|'Experimental'|'Complete'), step, approved, started, nextRelease, public, desc, status, next, links { stepIndex: [{name,url}] }, tasks [{ id, title, status, source ('Manual'|'Jira'), key, start, days, chain, links[], subtasks [{ id, title, status, start, days, chain, links[] }] }], repoEntries [{ id, pattern, scope ('all'|'label'), label, sync }], gh [issues from GitHub: { id, num, repo, title, state, reason, labels[], assignees[], updated, children[] }], recent { state: idle|loading|cached|failed|norepo, generated }, summary [{ t } | { link }], autoStatus`. "At Risk" is **not** stored: `nextRelease < today && designation !== 'Complete'`.

**Data fetching**: projects/tasks/links/repo entries CRUD; Jira nightly sync (tasks read-only); GitHub Issues hourly poll + nightly full pass; commit summaries generated on demand via the Ask model and cached; AI usage from the AI Reports service's columns-and-rows record; feed health, runs and credentials from the ingestion service; Ask over MCP with role scoping.

## Design Tokens

Colours
- Page `#f1ede4` · panel `#fbf9f4` · card white · sum-row/section header `#f6f3eb` · table header `#ebe7dc`
- Text `#1a1a2e` · secondary `#3a3a4a` · muted `#6b6a63` · faint `#9c9a8e`
- Borders `#d8d4c8` (default), `#e4e0d4` (row), `#ebe7dc` (light row), `#b9b3a4` (underline inputs, bar border), `#f1ede4`
- Gold `#b88c3a` (primary), hover `#a37a2f`, link `#8a6524` (hover `#6e5020`), light gold `#efe2c4`, gold border `#d9b878`, expanded row `#e6d3a3` / hover `#dfc78f`, row hover `#fcf8ec`, prompt bar `#fcf6e8`
- Amber notice `#f8e8c9` bg / `#e6cf9a` border / `#6b4d0b` or `#8a6a12` text · warning `#c98a1e`
- Red `#b8442e`, red tint `#f6dcd5` / border `#e8c2b8`, at-risk hover `#f0cdc3`, at-risk expanded `#eec3b8` / hover `#e8b5a8`, red-tinted chain row `#faeceb`
- Green `#2f8a4f` · chain palette listed under Tasks · SQL pre `#1a1a2e` with `#d9e1f2` text

Typography — `"Segoe UI", system-ui, -apple-system, sans-serif`; mono `ui-monospace, SFMono-Regular, Menlo, Consolas, monospace`.
- h1 20/600 letter-spacing −.01em · h2 15/600 · card value 28/600 −.02em · body 14 · table cell 14 (13.5 in Settings) · small 13 / 12.5 / 12 · caption 11 or 10.5 uppercase 600–800 with .07em or .5px letter-spacing · tags 9.5/700 uppercase .06em.

Spacing — 4/6/8/10/12/14/16/18/20/22 px steps; card padding 12px 16px (headers) and 14px 16px (bodies); table cells 9px 10px, headers 8px 10px; panel padding 16px 20px 18px 50px.

Radii — 3 (inputs, tags), 4 (buttons, cards inside panels), 6 (page cards, popovers), 8 (sign-in card), 999 (pills).

Shadows — menu `0 8px 24px rgba(20,25,20,.16)`; popover `0 16px 40px rgba(34,30,18,.22), 0 2px 8px rgba(34,30,18,.1)`; Ask button `0 6px 18px rgba(20,25,20,.18)`.

## Tweaks (prototype props)

`inlineRowEditing` (bool, default on) · `role` (Admin | Viewer) · `simulateSaveFailures` (bool) · `taskRowLayout` (Ledger | Two-line) · `pillHover` (Tooltip | Swap) · `aggregateStyle` (Sentence | Bar) · `taskCap` (int, 50) · `simulateSyncFailure` (bool) · `tokenDaysLeft` (0–365, 50). The first and the three GitHub Issues layout options are product decisions still open; the rest are prototype switches.

## Assets

`assets/wps-logo.png` (96px sign-in mark), `assets/wps-logo-small.png` (40px top-bar mark), `assets/wps-logo-white.png` (sign-in backdrop). All from the WPS brand; use the brand assets already in the codebase.

## Screenshots

Full-page captures of the prototype at a 1168px-wide pane (`screenshots/`). Colours and spacing are exact; the top bar and sample-data strip are cropped out of the page captures.

- `00-sign-in.png` — signed-out screen
- `01-portfolio.png` — Portfolio with *AI Usage Dashboard* expanded (inline row editing on; steps, narrative, Tasks, GitHub Issues, Work Summary, Repos) and the *V.3 of RAD Box* row shown At Risk
- `02-ai.png` — AI cards and per-person table
- `03-ai-detail.png` — Claude detail record
- `04-ask.png` — Ask with the releases question answered
- `05-settings.png` — all eight Settings sections
- `06-feed-controls.png` — Claude feed controls and run history

## Files

- `WPS Portfolio.dc.html` — the full prototype (template + logic + sample data). Open it directly in a browser next to `support.js`.
- `support.js` — rendering runtime required by the HTML file (not for reuse).
- `assets/` — logos.
- `github.md` — repo association and screen ↔ source-file map for `wps.watch.ai_reporting`.
- `screenshots/` — the captures listed above.
