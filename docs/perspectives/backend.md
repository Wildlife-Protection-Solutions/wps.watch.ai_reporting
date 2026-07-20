# AI Reports — Backend Perspective

**Audience:** the engineer who will build this.
**Scope:** the new .NET service that sits between wpsWatch users and the SQL Server replica `wpswatchprodreplica.database.windows.net`, replacing canned Looker reports today and growing into a chat-with-your-data surface tomorrow.
**Framing:** Eric's roadmap positions this as the **Foundation** of a 3-phase MCP vision — phase 1 builds the data-access fabric, phase 2 is a Mission Control UX over it, phase 3 is bounded physical agency. The technical principle that survives across all three is **"augment, do not replace."** Translated to backend terms: the service should never act on the operator's behalf without a human in the loop, and the operator should always be able to see exactly what query ran.

A glossary, because two of these acronyms get thrown around loosely:

- **MCP** (Model Context Protocol): an Anthropic-published protocol for exposing **tools** (named functions with JSON-Schema arguments) and **resources** (read-only blobs) to an LLM client over a stdio or SSE transport. The LLM picks tools, the server executes them, and results round-trip back through the protocol. It is, mechanically, a thin wrapper around tool-use — its value is the standardisation, not novel capability.
- **RAG** (Retrieval-Augmented Generation): inject relevant context (here, schema snippets, sample rows, FK paths) into the LLM prompt at query time. We will need RAG-shaped behaviour for text-to-SQL, even if we don't call it that.

---

## 1. Transport / protocol layer

### Option A — MCP server (the ticket's leading suggestion)

An MCP server exposes a small tool catalogue. Reasonable initial tool surface for our case:

- `list_canned_reports()` → metadata for the seven (or three) reports the user is allowed to see.
- `run_canned_report(report_id, parameters)` → executes the underlying parameterised SQL.
- `query_database(sql)` (gated, phase 2+) → text-to-SQL escape hatch.
- `list_schema()` / `describe_table(table)` → schema introspection so the model can self-serve.
- `save_query(name, sql, parameters_template)` (phase 3).
- `run_saved_query(query_id, parameters)` (phase 3).

Three places the LLM could live, and they have very different operational profiles:

1. **LLM in Anthropic's API, tool-use loop in our service.** Our service is the **MCP host *and* the LLM client.** The React page POSTs `{messages: [...]}` to our service. Our service holds the user's session, calls Anthropic with the user's question + tool schemas, executes whichever tool the model picked, loops, and streams the final response back as SSE. **Recommended.** All auth, rate limiting, audit logging, and DB access stay server-side. The browser never holds an Anthropic key, never sees raw schema, never sees raw SQL unless we choose to surface it.
2. **LLM in Claude Desktop / Claude Code on the user's machine, our service is just an MCP server.** This is the way MCP is most often demoed. Doesn't fit us — our users are reserve operators using a browser, not Claude Desktop. Cute for internal tooling, not a product surface.
3. **LLM client-side in the React app, calling our MCP server from the browser.** Forces an Anthropic API key into the browser or a thin proxy, gives up centralised audit and rate control, and complicates row-level security because the client has to be trusted to forward auth correctly. Don't do this.

**Wins of MCP:** standardised tool schema; trivial to point a different model at the same server later (Claude → another vendor → an internal model); the same server can be re-used by Mission Control in phase 2 and by physical-agency tooling in phase 3 because tool-discovery is a protocol primitive, not bespoke wiring.

**Costs of MCP:** the protocol itself is young — SDK churn, transport quirks, less mature observability than plain HTTP. The `query_database(sql)` tool is exactly the kind of thing MCP makes easy and dangerous (see §2). And there is genuine engineering effort to host MCP-over-SSE behind a webapp without inviting CORS / streaming headaches.

**Phase fit:** very good. Canned reports become typed tools the model selects from. Free-text query becomes an additional tool (`query_database`). Saved queries become per-user dynamic tools or a `run_saved_query` dispatcher. The Mission Control UX in phase 2 reuses the same tool catalogue.

### Option B — REST + `web_fetch`-style tool use (Matt M.'s alternative)

The interpretation that makes sense: the LLM is hosted by us (same as A1), but instead of MCP, we expose a **conventional REST API** on the new service (`GET /reports`, `POST /reports/{id}/run`, `POST /chat`) and give the model a generic `http_get` / `http_post` tool. The model decides which endpoint to hit.

**Wins:** every backend engineer already knows REST. Standard observability stack (App Insights, request logs, normal HTTP middlewares). No new transport to learn, no SSE plumbing.

**Costs:** giving the model a generic HTTP tool means it must construct URLs and bodies as plain strings — easier to hallucinate, harder to validate. JSON-Schema-typed MCP tools constrain the model up front; an HTTP tool moves that validation into our handlers as defensive checks. The model's freedom to call arbitrary URLs is also a small but real SSRF (server-side request forgery) surface — we must whitelist.

**Phase fit:** good for canned reports, awkward for free-text chat. For chat we still need a tool the model invokes per-token to ask for schema / sample rows / execute SQL — and at that point we've reinvented MCP minus the standardisation. The phase-2 / phase-3 leverage that the rest of Eric's roadmap depends on (one tool catalogue, many surfaces) is weaker.

### Option C — Hybrid (recommended)

Build **both transports on the same handler core**:

- One internal C# service (`AiReportsService`) implements the actual operations: list reports, run canned report, run text-to-SQL, save query.
- Wrap it twice. **REST** controllers for the React page and for any human-debugging needs. **MCP** server (SSE transport) for the LLM tool-use loop and for any future MCP client (Claude Desktop for ops, Mission Control, etc.).
- The Anthropic API call lives in the service, not the browser. The browser POSTs `/api/chat` (REST) and gets streamed tokens back. The service, in turn, talks to Anthropic with the MCP tools attached.

This gives us standardised tool surface where it matters (model side) and ergonomic REST where it matters (browser side), without doubling the business logic.

**Recommendation: Option C.** REST for the React boundary, MCP for the model boundary, one service implementation underneath. We get Matt M.'s ergonomic outer loop and the ticket's standardised inner loop.

End-to-end call sequence for a chat question, recommended path:

1. Browser POSTs `/api/chat { question, conversationId }` with the user's existing wpsWatch JWT.
2. Service validates JWT, hydrates user + orgs from `OrganizationUserRole` (see §4 / §5).
3. Service calls Anthropic Messages API with: system prompt (org-scope reminder + schema overview), conversation history, user message, MCP tool definitions.
4. Model emits a `tool_use` block (e.g. `run_canned_report` or `query_database`). Service executes the tool **with org-scope predicates injected**, captures rows.
5. Service returns the rows to the model as a `tool_result`. Model emits the final user-facing assistant message.
6. Service streams that message back to the browser, attaches the **executed SQL** and a **download token** (CSV / PDF / DOCX export).

---

## 2. Query generation strategy (orthogonal to transport)

This is the call that determines the safety story. Three positions:

### A. Predefined parameterised queries only

A static catalogue of, say, 30 named SQL templates (the seven SysAdmin reports + the three regular-user reports + ~20 narrower questions we expect — "battery below threshold X", "photos in last N days at site Y"). The LLM's job is to pick one and fill parameters, not to write SQL.

- **Safety:** highest. Every query has been read by a human. SQL injection is a non-issue (parameters are typed).
- **Cost:** low and predictable per query — no whole-schema-in-prompt overhead.
- **Schema-evolution coupling:** tight. Every table rename forces a sweep of the catalogue. This is fine; we already do this for our 41+ EF entities.
- **Debugging / observability:** trivial — log `(report_id, parameters, user_id, org_id, row_count)` and you have everything.
- **Failure mode:** the user asks something not in the catalogue ("how many photos in March 2024 at sites with elevation above 1000m") and the model must say "I can't answer that." Frustrating in a chat surface.

### B. Free text-to-SQL with guardrails

The model receives the schema (or a RAG-retrieved subset of it), produces a SQL string, the service runs it.

- **Safety:** lowest by default; survivable with discipline. Guardrails we'd need:
  - **Read-only DB login.** Mandatory and easy — the replica is already read-only at the engine level via `ApplicationIntent=ReadOnly`, but we should **also** use a SQL login that has only `SELECT` on a curated schema. Defence in depth.
  - **Statement-type whitelist.** Parse the produced SQL (Microsoft.SqlServer.TransactSql.ScriptDom is in-box for .NET) and reject anything that isn't a single `SELECT`. No `;`-joined statements, no `EXEC`, no `xp_cmdshell`, no `OPENROWSET`, no `INSERT`/`UPDATE`/`DELETE`/`MERGE`/`TRUNCATE`/`DROP`.
  - **Mandatory org-scope predicate injection.** This is the hardest piece. The model cannot be trusted to remember `WHERE OrganizationId IN (@userOrgs)` on every join. We rewrite the query post-hoc: parse the AST, find every reference to a tenant-scoped table, and add a predicate it can't bypass. Or — much simpler — front the catalogue with **views that pre-apply org-scope based on `SESSION_CONTEXT`**, set the session context from our service before each query, and let the model only `SELECT FROM` those views. Per the exploration notes there are **no views today** (`Wps.Watch.Sql/Migration/`); this is net-new SQL work but it's the safest design.
  - **Row + cost limits.** Statement-level `SET QUERY_GOVERNOR_COST_LIMIT`, hard `TOP n` injection on result rows, hard query timeout (the API uses 30s; chat timeouts should likely be tighter, ~20s).
  - **Sensitive-column allowlist.** The exploration notes enumerate eight credential / PII columns (`Site.DasToken`, `Site.SmartIntegrateToken`, `Site.SlackWebhookUrl`, `Device.PhoneNumber`, `User.PasswordHash`, `User.Email`, `Key.ApiKey`, `ImageRecognitionService.ApiKey`) plus operationally-sensitive GPS columns. These do not get exposed in the schema we hand the model, and the runtime `SELECT *` results get filtered before they go back to the model **or** the user. The cleanest way is to expose a `reporting` SQL schema with views that omit these columns, and only let the model see that schema.
- **Cost:** highest. Schema text in every prompt, multiple round-trips per question (model often asks for schema, asks for sample rows, then writes SQL).
- **Schema-evolution coupling:** loose at the catalogue level, tight at the prompt level — every renamed column is a regression risk for queries the model used to get right.
- **Debugging:** painful. "Why did the model produce this SQL" is a question we'll answer 30 times a week.
- **Failure mode:** the model produces a syntactically valid SQL that joins wrong and silently returns the wrong number. This is the worst possible failure for a tool the operator is supposed to trust. **Mitigations:** always show the user the SQL that ran; encourage operators to flag wrong answers; build a small eval set of known-good question→SQL pairs and run it nightly.

### C. Hybrid (recommended)

- **Predefined catalogue is the main surface for canned reports MVP.** All seven SysAdmin reports + all three regular-user reports become entries in the catalogue on day one.
- **Text-to-SQL is the chat escape hatch**, gated behind a feature flag and (initially) the `AdminReport` operation only — i.e. SysAdmin gets the chat box first, regular users get the canned catalogue. Once the eval set is mature and the org-scope-injection mechanism is battle-tested, expand to `UserReport`.
- **Saved queries are always SQL** (template + parameter schema), regardless of how they were authored. A SysAdmin who gets a useful answer from text-to-SQL hits "save" → it becomes a parameterised entry in their personal catalogue. The next run is just a parameter-fill operation, identical in mechanics to a canned report.

Why this is the right call: the predefined catalogue is the cheap-and-safe MVP that gets us to feature parity with Looker quickly. The text-to-SQL surface is where the Foundation phase earns its keep — it's the data-access primitive that phase 2 (Mission Control) and phase 3 (bounded physical agency) will both depend on. Building it second, behind a flag, gated to admins, with eval coverage, is the only sane sequencing.

---

## 3. The "save the SQL, not the prompt" requirement

This requirement looks tautological for option A and load-bearing for option B.

- **Predefined-only:** "save the SQL" really means "save (`report_id`, `parameters`)". The SQL is already in our catalogue. Re-running it tomorrow gives the identical answer because the underlying template hasn't moved. **User trust: high** — the saved query will keep returning the same thing as long as the catalogue entry exists. We owe users a deprecation policy when we retire a catalogue entry.
- **Text-to-SQL:** when a SysAdmin says "save this query," we save **the literal SQL string** that ran (with parameters templatised — replace concrete values with `@param1`, `@param2`, and store a parameter schema beside it). The saved row in the new service's storage is `(query_id, owner_user_id, name, sql_template, parameters[], created_at, last_run_at)`. The next run binds parameters and executes; **the LLM is not in the loop on re-run.** This is exactly the user-experience invariant we want: the saved query is a deterministic SQL template that doesn't change unless the owner edits it.
- **Hybrid:** both shapes live in the same `SavedQuery` table; canned-report saves get a `source = 'catalogue'` flag and resolve `query_id → catalogue entry`, text-to-SQL saves get `source = 'generated'` and store the SQL inline. UI surface is identical.

The user-experience implication is the one to be loud about: **users will (rightly) start treating saved queries as "their report."** That means schema migrations break their reports silently. Two mitigations worth committing to in the design:

1. Every saved query gets an automated daily smoke run (cheap on the replica) that flags syntactic / semantic breakage.
2. The saved-query view shows the SQL prominently, with a "this query was generated by AI on date X — review before relying on it" banner for `source = 'generated'` rows. We are explicit about provenance.

---

## 4. Reusing existing logic

The exploration notes are unambiguous about what is and isn't importable. The sharp edges:

- **`Wps.Watch.Data` (.NET 7, EF Core) — import.** Same EF model the API uses; for the canned-reports MVP we want exactly the same `DbContext` and the same entity definitions, pointed at the replica. Two real costs: (a) net target mismatch with the new service if we go .NET 8 (cross-target compatibility works but is a smell — bump `Wps.Watch.Data` to .NET 8 and don't look back); (b) the `DbContext` registers 41+ `DbSet`s most of which we don't need. Acceptable.
- **`Wps.Watch.Business` (.NET 7, light deps) — import the parts we need.** Mostly relevant for shared domain helpers. Don't blanket-reference; cherry-pick.
- **`Wps.Watch.Api` (.NET 8, heavy deps) — do not import.** As the notes say, App Insights / Cosmos / Twilio / SendGrid are along for the ride. The new service should re-instantiate its own (lighter) infrastructure stack.
- **Authorization classes (`Operation<T>`, `OperationRoleMapping<T>`, `RoleNames`, `*Operations`) — extract.** They live in `Wps.Watch.Api/Authorization/Operations/RoleNames.cs:7-52` and `Wps.Watch.Api/Authorization/OperationRoles.cs:446-463`. **Pull them into a new `Wps.Watch.Authorization` project** (referenced by both the API and the new service). Copying the constants is a six-month bug factory — the moment a new role is added in one place and not the other, role checks silently disagree. The extract is a half-day of work and pays dividends across phase 2 and 3.
- **`UserAuthorizationFilter` (`Wps.Watch.Api/Authorization/UserAuthorizationFilter.cs:36-38`) — re-implement.** It's a 60-line MVC filter that runs a single SQL query against `OrganizationUserRole` and stores the result on `IUserContextService`. The new service will need its own ASP.NET Core middleware that does the same thing. Keep the contract identical (interface in the shared project) so a future consolidation is easy.
- **`UserContextService` (`Wps.Watch.Api/Authorization/UserContextService.cs:11-78`) — re-implement, keep interface.** Same logic minus the multi-org-mode header read (we're single-org for MVP per the notes).
- **`ApiKeyMiddleware` (`Wps.Watch.Api/ApiKeyMiddleware.cs:52-106`) — do not import.** The new service's auth surface is JWT-only for end users; service-to-service is out of scope.

Net: one new shared project (`Wps.Watch.Authorization`), import `Wps.Watch.Data` and the bits of `Wps.Watch.Business` we use, re-implement two thin auth glue classes.

---

## 5. Authentication options for the new service

The choice is forced by `Wps.Watch.Api/Startup.cs:242-261`: JWTs are HS256-signed with a shared `JwtKey`. There is no JWKS endpoint, no asymmetric key, no Entra integration today. This is the single most consequential constraint in the exploration notes.

The three options the prompt names, evaluated:

1. **Share the symmetric `JwtKey` with the new service.**
   - Wins: cheapest possible path; the new service validates the same JWTs the API does, full stop. No new endpoints, no API changes.
   - Costs: doubles the blast radius of `JwtKey` leakage. Now two deployments hold the secret; key rotation is a coordinated dance instead of an API-only operation.
   - Mitigation: store the key in the same Azure Key Vault both services already use (the API's `DefaultAzureCredential()` pattern at `Wps.Watch.Api/Startup.cs:147` extends naturally to the new service). Rotation becomes a Key Vault change, not a deployment.
2. **New `/api/auth/introspect` endpoint on the API.**
   - Wins: cleanest separation. The new service holds no signing keys; it forwards the bearer token to the API on every session bootstrap and gets back `{userId, orgs[], roles[]}`. Per the notes (`§3 → "Bootstrap pattern"`) this endpoint does not exist today and is genuinely missing — building it would benefit other future consumers.
   - Costs: every new-service session pays a hop to the API. Easily mitigated with short-lived caching (~30s, the user's auth state is unlikely to change mid-session). The bigger cost is we just made the new service depend on the API at runtime, which directly contradicts the ticket's "separate service that isn't directly hitting our API." Strict reading of the ticket: this is non-compliant.
3. **Migrate JWT to RS256 / Entra.**
   - Wins: best long-term posture, plays well with future SSO, eliminates symmetric-key sharing entirely.
   - Costs: changes the API, the React login flow, the API-key code path possibly, and any other JWT consumers we don't know about. Out of scope for a feature ticket; it's an infra-tier project on its own. The notes flag this in §9 as an open question; for our purposes, treat it as out-of-scope.

**Recommendation: option 1 (shared `JwtKey` via Key Vault) as the primary path. Option 2 (`/api/auth/introspect`) as the documented fallback** if security review says no to key sharing.

The session bootstrap then becomes: validate JWT signature with the symmetric key locally → extract `sub` → query the replica's `OrganizationUserRole` table directly (the new service is already going to be talking to the replica) → cache the org/role list per request. This is a near-clone of what `UserAuthorizationFilter.cs:36-38` does today.

One subtlety: the existing API hydrates from the **primary** DB, not the replica. There is replication lag (unmeasured; the notes flag this). Org-membership changes will take some seconds to propagate to the new service. This is acceptable for a reporting tool but worth a documented latency budget — operators should not expect "added user to org" to immediately show new data in chat.

---

## 6. Recommended end-to-end design

Pulling §1 (Hybrid: REST + MCP), §2 (Hybrid: catalogue + gated text-to-SQL), §4 (extract auth, import data, re-implement filter), and §5 (shared `JwtKey`) into one design.

**Service shape.** A new ASP.NET Core 8 Web API project, `Wps.Watch.AiReports`. Hosts:
- REST controllers for the React UI (`/api/reports`, `/api/chat`, `/api/saved-queries`).
- An MCP server endpoint (`/mcp`, SSE transport) that the service's *own* Anthropic-API caller connects to as an MCP host. Same handlers underneath.
- A read-only EF `WpsDbContext` pointed at `wpswatchprodreplica.database.windows.net` with `ApplicationIntent=ReadOnly`, using a SQL login restricted to `SELECT` on a new `reporting` schema (views for canned reports + the org-scoped denormalized projections we'll need).
- Auth middleware: HS256 JWT validation with the shared key, then an `IUserContextService` clone that loads `OrganizationUserRole` from the replica.
- Storage for saved queries and a personal catalogue: a small dedicated DB (probably its own Azure SQL DB so it's not coupled to the replica's read-only constraint). **Not** the primary wpsWatch DB — feature isolation matters.

**Loading the canned-reports page (MVP).**
1. React page mounts. JS calls `GET /api/reports` with the wpsWatch JWT in `Authorization: Bearer …`.
2. New service validates JWT (HS256, shared key from Key Vault), hydrates user + org list from the replica's `OrganizationUserRole` table.
3. Service consults the shared `Wps.Watch.Authorization` package: `UserContextService.Can(UserReport)` and `Can(AdminReport)`. Returns the catalogue entries the user is allowed to see — three reports for `UserReport`, ten reports (seven SysAdmin + the three) for `AdminReport`.
4. User clicks a report. JS calls `POST /api/reports/{id}/run` with parameters.
5. Service binds parameters, calls a stored view in the `reporting` schema *with `SESSION_CONTEXT` set to the user's org list*, returns rows + an export-token.
6. JS renders. User downloads CSV / PDF / DOCX via `GET /api/exports/{token}`.

No LLM in this flow. Catalogue MVP is fast, cheap, and doesn't need Anthropic to be up.

**Asking a free-text question (phase 2).**
1. JS calls `POST /api/chat { question, conversationId }`. Auth as above; only `AdminReport` users see the chat surface initially.
2. Service builds an Anthropic Messages API request: system prompt (with the user's org IDs *baked in*, schema overview limited to the `reporting` schema), conversation history, user question, MCP tools (`list_canned_reports`, `run_canned_report`, `query_database`, `describe_table`, `save_query`).
3. Anthropic streams back tool-use blocks; service executes, loops, eventually streams the final answer + the executed SQL back to the browser as SSE.
4. **Every SQL the model produces flows through the same parser → org-scope predicate injection / view-only check → cost-limited execution path the catalogue uses.** No code path lets the model talk to the DB without that gate.
5. Browser renders the answer, the SQL, and the export-token.

**Saving and re-running a custom query (phase 3).**
1. After a successful chat answer, user clicks "Save." JS calls `POST /api/saved-queries { name, sql, parameter_schema }` (the parameter schema is inferred by the service from the SQL — `WHERE date > @cutoff` → `cutoff: date`).
2. Service stores the row in its own DB. *The LLM is not consulted on save.*
3. Tomorrow, the user opens "My Saved Queries," picks one, fills parameters via a form (not chat). JS calls `POST /api/saved-queries/{id}/run`.
4. Service binds parameters, runs through the same execution path. Same SQL today, tomorrow, next year — until the schema changes underneath it.

---

## 7. Effort tiers

Expressed as backend-engineer-weeks of focused work, not calendar weeks.

| Piece | Tier | Why |
|---|---|---|
| New service skeleton (.NET 8, project layout, CI) | S | Boilerplate. |
| Replica connection + read-only login + ReadOnly intent | S | Net-new but mechanical. |
| Shared `Wps.Watch.Authorization` extract (NuGet) | S | Move + reference. Coordinate with API team for one cross-cutting PR. |
| HS256 JWT validation + `IUserContextService` re-impl | S | ~150 lines, copy-with-adaptation from `UserContextService.cs:11-78` and `UserAuthorizationFilter.cs:36-38`. |
| `reporting` SQL schema: views for the seven SysAdmin reports + three user reports, with `SESSION_CONTEXT`-based org filtering | **L** | The hardest pure-SQL piece. Need to validate output matches Looker today, including the SIM Prepaid Data and Top 10 Offline reports whose semantics are open questions per `ai-reports-exploration-notes.md §9`. **Will dominate elapsed time.** |
| REST endpoints for canned catalogue + parameter binding + exports (CSV / PDF / DOCX) | M | Export plumbing in particular — three formats, server-side rendering. |
| Saved-queries CRUD + storage + smoke-runs | M | New small DB, daily background job. |
| MCP server stand-up + SSE transport | M | New protocol, needs care, but the SDK is real. |
| Anthropic-host loop (tool-use orchestration in our service) | M | Standard pattern; getting streaming + cancellation + token accounting right is the work. |
| Text-to-SQL guardrails: AST parser, statement whitelist, predicate injection or view-only enforcement, cost limits | **L** | The hardest engineering piece. **Will also dominate elapsed time.** Every shortcut here is a future incident. |
| Eval suite for text-to-SQL (golden questions → expected SQL / expected row counts) | M | Not technically hard but easy to under-invest in; under-investing is how we ship a tool that quietly lies to operators. |
| Cross-org reporting (phase 2+) | L | Out of MVP per the notes; called out only because the architecture should leave room. |

**Hardest pieces, ranked:**
1. The `reporting` schema and the org-scope mechanism. Get this wrong and either nothing works or worse, things work but show the wrong org's data.
2. Text-to-SQL guardrails. Get this wrong and the wrong-answer mode of failure goes from "rare" to "regular." This is the place where "augment, do not replace" is operationalised in code.
3. The export pipeline. Sounds boring; it's actually three integrations with three very different rendering models (CSV is cheap, DOCX is OpenXml, PDF wants either a Chrome runner or a paid library), and getting it production-quality is a sleeper L.

---

## 8. Assumptions I couldn't verify

The exploration notes are excellent but don't cover everything; I made judgement calls on:

1. **Anthropic specifically.** The roadmap says "frontier models." I've assumed Anthropic's Messages API throughout. If the choice is OpenAI / Bedrock / a self-hosted model, the host-loop code is portable but tool-use formats differ slightly — adjust accordingly.
2. **Where saved-query storage lives.** I've assumed a small dedicated Azure SQL DB owned by the new service. The exploration notes say no `DashboardView` table exists today and saved queries today are `localStorage`-only (`DashboardComponent.tsx:354-358, 835`). Could equally live as a new schema in the primary DB if the team prefers a single SQL Azure resource — slight coupling cost, slight ops simplification.
3. **Replication lag tolerance.** The notes flag that the replica connection is net-new and lag is uncharacterised (`§8`). I've assumed seconds, not minutes. If it turns out to be more, the "added user to org" UX needs an explicit caveat.
4. **`SESSION_CONTEXT` is available on the replica.** It's a normal SQL Server 2016+ feature and Azure SQL supports it, but I haven't verified the replica's auth model permits setting it from the read-only login. If not, fall back to predicate injection in the parser layer.
5. **Anthropic key custody.** I've assumed Key Vault, same as the rest of the wpsWatch secret story. Worth confirming nobody on the platform side wants the key in a different vault for cost-tracking reasons.
6. **Whether the existing API team will accept the `Wps.Watch.Authorization` extract PR quickly.** This is a coordination dependency, not a technical one, but it gates the new service's auth implementation. If pushback is significant, the fallback is to copy the constants and own the divergence-risk consciously.
7. **Volunteer / multi-org behaviour at MVP.** The notes confirm volunteers are excluded everywhere and reports are single-org today. I've taken both as hard constraints; if the human owner relaxes either, §6 needs adjustment but nothing is invalidated.
8. **The seven Looker reports' exact SQL.** The exploration notes infer the source tables (`§6`) but the actual Looker queries aren't visible in our repos. The `reporting` schema views need to be reverse-engineered against a side-by-side comparison with the live Looker dashboards. This is the place a "small" task balloons.
9. **The `SIM Prepaid Data` data-used source** (`§9` open question 1) and the **"offline" threshold** (`§9` open question 2). Both are gaps in the EF model, both would need product input to spec, both could block a 1:1 Looker replacement. Worth raising with Eric before we start.

---

*This document should be read alongside `docs/ai-reports-exploration-notes.md`. Where it disagrees with that document about what's in the codebase, the exploration notes win.*
