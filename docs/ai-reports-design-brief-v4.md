# AI Reports — Design Brief (v4)

**A new way to explore wpsWatch data: ask questions, get answers, and roll it out one safe step at a time.**

*Supersedes [`ai-reports-design-brief-v3.md`](./ai-reports-design-brief-v3.md). v1–v3 preserved.*

---

## Why this, why now

Operators at our partner reserves use wpsWatch to monitor camera traps and protect endangered species. The reports they have access to today live in Looker Studio — fixed dashboards. They work for the questions they were designed to answer, but they're rigid: columns can't be sorted, filters can't be combined, totals don't update as you narrow a view, date ranges are fixed. Anything outside a dashboard's designed scope is out of reach.

AI Reports is the upgrade: a new in-product surface that runs the equivalent reports with the missing usability, and — increasingly — lets people just *ask the data questions* in plain English and get back answers with the underlying numbers visible.

## What we're building

- **Run reports**, with the everyday upgrades they've been missing — sortable columns, working filters, live totals, flexible date ranges.
- **Ask questions in plain English** and get an answer plus the rows behind it. ("Which cameras have been offline more than 24 hours?")
- **Download results** to verify them and share them.
- Over time, **turn the questions people ask most into one-click reports** — so the catalog grows from real use, instead of us guessing what dashboards to build up front.

Behind the scenes it's a new service that reads a copy of the wpsWatch database — never writing to it, and isolated from the systems operators use day to day.

## Two things to keep separate: *what it does* and *who can use it*

This is the heart of the plan. We advance along **two independent tracks**:

- **Capabilities — what the product can do.** Canned reports → free-form questions → saved/scheduled reports.
- **Rollout rungs — who's allowed to use it, on what data, behind what safeguards.** This is how we keep an AI-over-an-anti-poaching-database safe.

The mistake to avoid is assuming "a more advanced feature" automatically means "riskier." It doesn't. A powerful feature used by *our own team on test data* is low-risk. The same feature open to *outside users on live rhino locations* is high-risk. So we gate by **who and what data**, not by how advanced the feature is.

## The rollout ladder

### Rung 1 — our team, on test data *(where we are now)*
The internal team uses the tool against a **test database** (synthetic and stale data — no live animal locations). Here we turn on *everything*, including free-form "ask anything" querying, and we use it heavily to learn what questions actually matter. Low risk by construction: trusted people, no real data. This is the learning engine, and it's already built and running.

### Rung 2 — our team, on real data
Same trusted internal team, now pointed at a live read-only copy of the real database. The safeguards step up accordingly: **animal GPS coordinates are stripped** from anything the AI sees, the service connects through a locked-down read-only account that can't reach credentials or sensitive columns, real logins replace the dev placeholder, and everything is audited. This is where we prove it's genuinely useful and safe on real data — and where the most-asked questions start becoming polished, repeatable reports.

### Rung 3 — external reserve users *(distant — only after a lot of evaluation)*
Finally, and only after extensive internal testing convinces us it's both useful and safe, we open it to operators at partner reserves, behind the **full** safeguard set (strict per-organization data boundaries, the complete security checklist, and legal/donor sign-off on anything sensitive). By this point the reports we ship are the *distilled* result of everything we learned at Rungs 1–2 — evidence-based, not guessed.

**We climb these rungs in order, and each step is earned** — by proof that the previous rung was useful, and by having the next rung's safeguards built and tested first. No calendar-driven jump to external users.

## How capabilities map onto the ladder

| | Run reports | Ask questions freely | Save & schedule |
|---|---|---|---|
| **Rung 1** (team · test data) | ✅ built | ✅ built — the learning engine | optional |
| **Rung 2** (team · real data) | validated on real data | with real safeguards (GPS stripped, locked-down access) | for the internal team |
| **Rung 3** (external users) | polished, evidence-based reports | only if proven safe | for external users |

## What this *isn't*

- **Not a replacement for wpsWatch alerts.** Real-time low-battery alerts, threat notifications, and WhatsApp pings stay with the alerting system. AI Reports answers questions; it doesn't act on your behalf.
- **Not the AI making decisions.** Every answer shows the rows, counts, time window, and filters behind it. People verify and decide.
- **Not "point an AI at rhino locations for outsiders on day one."** External access is the *last* rung, behind the full safeguard set, after extensive internal proof. Animal GPS is kept away from the AI entirely the moment we touch real data.
- **Not a change to how you log in.** Same wpsWatch login, same roles, same organization scoping.

## How it fits in

This is the Foundation step of the broader AI-orchestration vision Eric introduced — *augment, do not replace*. Classical computer vision and rules keep doing what they do well; the AI layer interprets, packages, and proposes. Human in the loop, always.

---

## Key question for the team

The detailed requirements document has 25+ open questions; this is the one that most needs this group's input before we lock the next step.

### How users authenticate to the new service

The service has to know **who you are** and **which organizations you can see** — the same information wpsWatch already tracks at login. Three ways, with different trade-offs (this becomes important at Rung 2, when real data is involved and the dev placeholder login has to go):

- **Share the existing login key with the new service.** Cheapest and fastest. The cost: two systems now hold the same shared secret; if either is compromised, both are.
- **Add a one-call check-in to the wpsWatch API.** Cleaner separation, no shared secret — but the new service then depends on the main API being up to authenticate.
- **Upgrade the wpsWatch login system to a modern asymmetric model** (the approach Microsoft Entra and similar identity providers use). Eliminates the shared-secret problem and sets up single sign-on; larger upfront investment.

**Recommendation in the requirements doc:** the dev placeholder is fine through Rung 1 (internal, test data); before Rung 2 (real data), move to the shared-key approach, with a clear path to the asymmetric upgrade if more services come online. Open to direction from this group.

---

*Full requirements document: `ai-reports-requirements-v6.md`. Coordinate-handling detail: `ai-reports-coordinate-handling.md`. Architecture and data findings: `ai-reports-exploration-notes-v3.md`.*
