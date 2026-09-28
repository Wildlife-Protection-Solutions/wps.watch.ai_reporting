# WPS Portfolio — Execution model (Epic, issues, models, executor session)

**Status:** draft for review · 2026-09-28. Companion to `PLAN.md`.

## 1. Roles

| Who | Does |
|---|---|
| Planning session (this one) | Gathers everything, writes `PLAN.md`, `prototype-behaviour.md`, `repo-inventory.md`, `integrations-research.md`; drafts every issue under `docs/portfolio/issues/`; after approval creates the GitHub Epic and sub-issues. |
| Executor session (new Fable session, Ultracode on) | Reads the runbook issue, then works the Epic end to end: claims issues in dependency order, dispatches one subagent per issue with the recommended model, runs the QA gate, commits and pushes, closes the issue, opens one PR per phase. Asks the human only for the I-items in `PLAN.md` §3 and for phase acceptance. |
| Product owner (Eric) | Answers the D-items and provides I-items when available; acceptance-tests each phase with the one documented command; files findings as new issues on the Epic. |

## 2. GitHub structure

- **Epic:** one parent issue titled `WPS Portfolio v1 — Epic`, labelled `portfolio`, `epic`. Body: goal, non-negotiables, links to the plan documents, the phase list, the decision/input tables (D1–D8, I1–I13) with their current answers, and the definition of done for the whole Epic.
- **Runbook issue** `P0-0 Runbook for the executor`: how to work the Epic (below). The executor reads it first.
- **One sub-issue per task row** in `PLAN.md` §4 (ids `P0-1` … `P5-6`), attached to the Epic as native GitHub sub-issues in dependency order. Title format: `P1-2 · Table engine (pure TS)`.
- **Labels:** `portfolio`; `phase:0`…`phase:5`; `area:host|data|client|integrations|ask|auth|ops|docs`; `model:opus|sonnet|haiku` (recommended subagent model); `size:S|M|L`; `needs-input` (blocked on an I-item); `runbook`; `epic`.
- **Issue body template** (every sub-issue): Goal · Scope in/out · Owned paths (disjoint from parallel siblings) · Depends on (`Blocked by #n`) · Read first (exact sections of `PLAN.md`, `prototype-behaviour.md`, handoff `README.md` line ranges, `repo-inventory.md`) · Implementation notes · Acceptance criteria (checklist) · Automated checks (exact commands) · Definition of done · Recommended model and why.
- Drafts live in `docs/portfolio/issues/<id>.md` with the same front matter, so the set can be reviewed in one PR before it is pushed to GitHub, and regenerated if the plan changes.

## 3. Model per issue

The orchestrator is **Claude Fable 5.1**. Each issue recommends the model for the subagent that implements it; the orchestrator may escalate one tier after a failed gate.

| Recommend | For | Examples |
|---|---|---|
| `model:opus` (Claude Opus 5.5) | Logic-dense or architecture-shaping work where a subtle mistake propagates: pure algorithms with many rules, security boundaries, concurrency, schema design | P0-3 data core, P0-7 auth, P1-2 table engine, P2-1 task scheduling, P2-3 drag reorder, P2-5 feed core, P3-6 Ask engines + SQL guard, P4-1 scheduler + credentials, P5-1 JWT, P5-4 security pass |
| `model:sonnet` (Claude Sonnet 5) | Well-specified UI and API work with clear acceptance criteria: pages and sections, CRUD controllers, providers against recorded fixtures, Playwright specs | most P1–P4 UI tasks, Settings APIs, real providers, acceptance packs |
| `model:haiku` (Claude Haiku 4.5) | Mechanical or documentation-only work with a checkable output | README lines, phase reports from a template, screenshot contact-sheet scripts, run-history generators |

Review subagents (the adversarial code review in the QA gate) run at `opus` for `model:opus` issues and `sonnet` otherwise.

## 4. Branches, commits, PRs

- The executor's assigned branch is the **integration branch** for the whole run. Per-issue work happens on that branch (or in a temporary worktree merged back by the orchestrator when two .NET-heavy tasks run at once).
- One commit per green issue: `portfolio(<area>): <what>` + `Refs #<issue>`; pushed immediately. The orchestrator then closes the issue with a comment containing the commit SHA and the checks that ran.
- One **draft PR per phase** from the integration branch to `main`, titled `WPS Portfolio — Phase N: <name>`, body from `portfolio/docs/phases/phase-N.md` (the exact acceptance command, the click-through script, known gaps, open decisions). The product owner marks it ready and merges after acceptance. Because the sidecar shares no build or runtime with the existing app, merging to `main` cannot affect the current dashboard.
- Before the executor starts, a small PR merges the SessionStart hook and these documents to `main`, so the executor's fresh container has the .NET SDK, `dotnet-ef` and the docs on day one.

## 5. QA gate (per issue) and phase gate

Per issue, in order (from `PLAN.md` §5.2): `dotnet build -warnaserror` + `dotnet test` on the Portfolio solution → client `lint`, `typecheck`, `test`, `build` → the issue's Playwright specs plus the chrome/smoke specs across the three role projects → screenshots of touched screens compared side by side with the handoff PNGs (structural assertions gate; pixels do not) → adversarial code review by a second subagent (`code-review` skill, high effort) with the acceptance criteria and security rules as rubric → `portfolio/scripts/check-isolation.sh` → commit and push.

Per phase: full suite on a fresh `--reset` database, regenerated screenshots and contact sheets, `phase-N.md`, tag `portfolio-phase-N`, the phase PR, CI green, isolation guard green, and the legacy solution still building and passing its 64 tests.

## 6. Environment checklist before starting the executor

1. The Claude GitHub App covers `wps.watch.ai_reporting` with write access (done 2026-09-28).
2. The hook and docs are on `main` (merge the small PR first).
3. Start the executor in the **largest available cloud environment**: workflow parallelism is `CPUs − 2` agents (2 on a 4-CPU container).
4. Optional environment secrets for live smoke tests (never required for the run): `Portfolio__Feeds__github__Token`, `Portfolio__Feeds__jira__Token`/`Email`, `Portfolio__Anthropic__ApiKey`, `Portfolio__Feeds__openai__AdminKey`, Google service-account JSON. Without them every feed runs its sample provider and the keyword Ask engine answers.
5. Optional egress allowlist for the executor's environment if live smoke should run from Claude: `api.github.com`, `wps.atlassian.net`, `api.anthropic.com`, `api.openai.com`, `admin.googleapis.com`, `oauth2.googleapis.com`.

## 7. Executor kickoff prompt (paste into the new session)

```
ultracode

You are the executor for the GitHub Epic "WPS Portfolio v1 — Epic" in Wildlife-Protection-Solutions/wps.watch.ai_reporting.
Read, in this order: the runbook sub-issue (P0-0), docs/portfolio/PLAN.md, docs/portfolio/EXECUTION-MODEL.md,
docs/portfolio/prototype-behaviour.md, docs/portfolio/repo-inventory.md, docs/portfolio/integrations-research.md,
and the handoff spec docs/portfolio/handoff/README.md. Then work the Epic end to end on this session's branch:
claim sub-issues in dependency order, dispatch one subagent per issue using the issue's recommended model,
run the QA gate in the runbook before every commit, push after every green issue, close the issue with the
commit SHA and checks, and open one draft PR per phase. Never edit anything under Wps.Watch.AiReporting*,
the root solution, root .gitignore or wwwroot; never call a write tool of the "WPS AI Usage Dashboard" MCP.
Do not stop at phase ends; only the I-items in PLAN.md §3 or a genuine blocker may pause you, and then
only that issue is parked while independent issues continue. Report to the human only at phase ends and
when blocked.
```

## 8. Human touchpoints during the run

- **Blocking now:** D1 layout approval (sidecar under `portfolio/`), and the review of the drafted issue set before it is pushed to GitHub.
- **Blocking later, per phase:** D2 before the v1 import (Phase 2), D3/D4/I9/I10 before Phase 5.
- **Never blocking:** credentials for live providers (sample providers run without them), product switches (defaults recorded in `decisions.md`), email transport.
- **Acceptance:** one command per phase from `phase-N.md`; findings become new issues on the Epic and the executor picks them up with the same gate.
