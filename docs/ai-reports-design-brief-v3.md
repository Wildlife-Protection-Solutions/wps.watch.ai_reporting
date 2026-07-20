# AI Reports — Design Brief (v3)

**A new way to explore wpsWatch data: ask questions, get answers, grow the report library over time.**

*Supersedes [`ai-reports-design-brief-v2.md`](./ai-reports-design-brief-v2.md). v1 and v2 preserved.*

---

## Why this, why now

Operators at our partner reserves use wpsWatch to monitor camera traps and protect endangered species. The reports they have access to today live in Looker Studio — fixed dashboards opened from the Reports menu. They work for the questions they were designed to answer, but they're rigid: columns can't be sorted, filters can't be combined, totals don't update when you narrow a view, and date ranges are fixed. Anything outside the designed scope of each dashboard is out of reach without exporting data elsewhere.

AI Reports is the upgrade. A new in-product page that runs the equivalent reports with the missing usability, then in later phases lays the foundation for a chat-driven exploration layer, an expanding library of canned reports driven by what users actually ask, and saved repeatable queries.

## What we're building

A new page in wpsWatch where you can:

- **Run the existing reports**, with the everyday quality-of-life upgrades they've been missing — sortable columns, filters that work, totals that update as you filter, flexible date ranges.
- **Ask questions in plain English** about your reserve's data and get back an answer with the underlying numbers visible. ("Which cameras have been offline for more than 24 hours?")
- **Watch the catalog of one-click reports grow over time**, as the team converts the most commonly asked chat questions into new canned reports — so frequently-asked questions stop needing the chat and become one-click again.
- **Save the questions you ask repeatedly** so they run the same way every time, with one click.

Behind the scenes it's a new service that reads from a **read-only mirror of the wpsWatch production database** — kept continuously in sync, so the data is current, but isolated from the systems that operators use day to day. Users still sign in to wpsWatch as they do today; the new page lives alongside the existing Reports menu during a pilot period, then progressively replaces it.

## What this *isn't*

- **Not a replacement for wpsWatch alerts.** Real-time low-battery alerts, threat notifications, and WhatsApp pings stay with the alerting system. AI Reports answers questions about the past and present state of your fleet; it doesn't act on your behalf.
- **Not the AI making decisions.** Every answer shows the rows it came from, the count, the time window, and the filters applied. Operators verify and decide; the model just makes the data easier to reach.
- **Not automatic.** When the system spots a chat question being asked repeatedly, the dev team reviews the pattern and decides whether to make it a canned report. Humans always pick what gets added, named, and which roles can see it.
- **Not a change to how you log in.** Same wpsWatch login, same roles, same organization scoping — the new page sees exactly what your existing access lets you see.

## How it fits in

This is the Foundation step of the broader AI-orchestration vision Eric introduced — *augment, do not replace*. Classical computer vision and rules keep doing what they do well; the AI layer interprets, packages, and proposes. Human in the loop, always.

---

## Roadmap

### Phase 1 — Parity with the existing reports, better.

*Goal: every operator can do what they did in Looker, faster and with clearer answers, from a wpsWatch page they don't have to leave.*

- Replace today's ten Looker reports (7 admin-tier, 3 user-tier) with equivalent reports through the new system.
- Add sortable columns, in-page filtering, live summary totals, and flexible date ranges.
- Mirror existing role access exactly — no changes to who sees what.
- Pilot rollout: new menu lives side-by-side with the existing Looker menu, then System Admins migrate first, regular users second.
- Tracer bullet: Region & Site Totals — a high-value report currently restricted to System Admins, used as the proof-of-concept for the full migration.

### Phase 2 — Ask questions, and grow the report catalog.

*Goal: free-form chat ships. The questions users ask most often turn into new canned reports over time, so frequent questions get easier without staying as chat queries.*

- A chat panel on the new page where users can ask questions like "which cameras had unusually high capture volume last night?" or "show me sites with no active deployment in this region."
- Every answer shows the rows, the count, the time window, the filters, and (if you want) the SQL the system ran.
- Download answers as PDF, CSV, Excel, Word, KML, GeoJSON, or PNG — formats appropriate for different downstream uses (printed reports, spreadsheet analysis, mapping software, quick sharing).
- **Usage telemetry** — track which reports get used and how often, which chat questions come up repeatedly, and which ones fail or return nothing useful. Aggregate counts are visible to System Admins; full prompt text is restricted to the same access tier as the audit log.
- **Catalog growth** — the dev team monitors the telemetry and adds new canned reports for the patterns that emerge. By the end of Phase 2 the report catalog has grown beyond the original ten, with the most common questions converted into one-click reports under the existing role tiers.
- First cross-organization reporting in wpsWatch — multi-org users finally see all their data in one query.

### Phase 3 — Save your queries, run them on a schedule.

*Goal: questions users run repeatedly become saved one-click queries, optionally delivered by scheduled email.*

- Save any question (with its underlying query, not the typed prompt) so it returns the same kind of answer each time.
- Relative dates that stay current — "last 30 days" means the last 30 days *whenever you run it*, not the calendar dates you saved.
- Share saved queries with your team.
- Scheduled email delivery of any saved query, at a cadence the user chooses.

### Longer term — Beyond reporting.

*Map-based geospatial queries (with role-gated coordinate precision for species safety), composite "camera health" reports that combine offline / battery / SIM / issue data in one view, and the broader Mission Control surface from Eric's roadmap.*

---

## Key question for the team

The detailed requirements document includes 25+ open questions; this is the one that most needs this group's input before we lock the Phase 1 design.

### How users authenticate to the new service

The new service has to know **who you are** and **which organizations you can see** — the same information wpsWatch already tracks every time you log in. Three ways to do this, with different trade-offs:

- **Share the existing login key with the new service.** Cheapest and fastest to ship. The new service can verify wpsWatch logins immediately. The cost is that two systems now hold the same shared secret; if either is compromised, both are.
- **Add a one-call check-in to the wpsWatch API.** Every session starts with one extra request to wpsWatch's existing API to confirm "yes, this is a valid user, here's what they can see." Cleaner separation, no shared secret. Trade-off: the new service depends on wpsWatch's API being up to authenticate, even though it reads data from a separate database.
- **Upgrade the wpsWatch login system to a modern asymmetric model** (the same family of approach Microsoft Entra and other identity providers use). Eliminates the shared-secret problem entirely, sets us up for single sign-on with org tooling, and any future service inherits the upgrade. Larger investment — a couple of weeks of work on wpsWatch itself before AI Reports can use it.

**Recommendation in the requirements doc:** start with the share-the-key approach for the Phase 1 MVP, with a clear commitment to upgrade post-Phase 1 if other services come on line that need it. Open to direction from this group.

---

*Full requirements document: `ai-reports-requirements-v5.md`. Architecture and data findings: `ai-reports-exploration-notes-v3.md`. Persona reviews: `perspectives/`.*
