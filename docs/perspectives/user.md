# User Perspective — AI Reports for wpsWatch

**From:** Ops room, [reserve], southern Africa
**To:** wpsWatch dev team
**Re:** the new "ask wpsWatch a question" thing
**Date:** 2026-05-05

A note before we start. I run the ops room. There are usually two of us on shift, sometimes three when we're rotating field volunteers through. We watch the camera grid, we coordinate with the anti-poaching unit (APU) on the ground, we keep the cameras alive. wpsWatch is on a screen in front of me from 05:30 to about 22:00. Reports today, honestly, I barely use. Not because I don't need them — because clicking through Looker dashboards in the middle of a shift is a tax I can't afford. If the new feature lets me ask a question and get an answer in the next thirty seconds without leaving the page I'm already on, this becomes one of the most-used things in the product. If it makes me wait, or makes me distrust it, I'll go back to ignoring it.

Here's how it actually plays out.

---

## 1. Real questions I'd want to ask

I've grouped these the way I think about them, not the way the current Reports menu groups them.

### Device health (the morning sweep)

1. **"Which cameras went offline overnight?"** — Daily, every morning at 06:00 before the field team rolls. Expect a list with site name, last-seen time, last battery reading, and a quick map pin so I can see if it's a cluster (signal issue) or scattered (battery/individual faults). On demand, desktop. This is the single most important question I have. Today I dig it out of the Top 10 Offline Looker report — which I don't have access to anyway — by squinting at the battery-level dashboard.
2. **"Show me everything below 30% battery, sorted worst first, with site name and access notes."** — Daily. List, ten to thirty rows. Desktop. The "access notes" piece is what Looker can't do — it's a join I do in my head right now.
3. **"Any camera that hasn't sent a photo in 48 hours but was healthy 3 days ago?"** — Daily. List with last-event timestamps. Desktop. This is the "something just broke" question. Looker can't ask it.
4. **"Which cameras are on solar vs battery box, and of the battery-box ones, which are due for a swap based on install date plus average run time?"** — Weekly. Table. Desktop. Honestly nobody computes this today, we just react when batteries die.
5. **"Push me a phone alert if any camera in the western fence line drops below 20% before 14:00."** — Proactive, daily during rainy season when solar charge is bad. Phone. This is the kind of "subscribe to a question" idea that would change how we run.

### Photo / detection volume (the rhythm check)

6. **"How many photos came in last night, by site, compared to the same night last week?"** — Daily, around 07:00. Bar chart with deltas, or a simple table — I don't care, I just need to see "site 14 normally does 200 a night, last night did 6, why." Desktop.
7. **"Top 10 cameras by photo volume this week — and bottom 10."** — Weekly, Monday morning when I write up the report for the warden. Two lists. Desktop. Photo Count by Camera does the top half of this; the bottom half is what I actually want and it doesn't.
8. **"Which sites had a sudden spike in captures yesterday between 22:00 and 04:00?"** — Daily, on incident. List with counts and a sparkline of the previous 14 nights so I can see if it's normal for that camera. Desktop AND phone — sometimes I'm at home when the night-shift volunteer pings me.
9. **"How many of last week's photos were tagged as humans-on-foot vs vehicles vs livestock vs nothing-of-interest?"** — Weekly. Pie or stacked bar. Desktop. Looker doesn't slice by tag at all today.
10. **"Show me every photo capture in the eastern block in the last 24h on a map, animated by time."** — On incident, when APU thinks there's been an incursion. Map with timeline scrubber. Desktop primarily but phone would be huge.

### Deployment coverage (the planning questions)

11. **"Which sites in region X don't currently have an active deployment?"** — Weekly to monthly, when planning re-deployments. List with last deployment end-date so I can see what was decommissioned vs never deployed. Desktop.
12. **"How many active deployments per region and per site, and where's the gap?"** — Monthly, before the warden's planning meeting. Table plus map. Desktop. This is roughly Region & Site Totals — which I can't see, because that's a SysAdmin report.

### SIM / connectivity

13. **"Which SIMs are renewing in the next 30 days, and which carrier?"** — Weekly. List with renewal date and carrier. Desktop. Today I rely on central WPS staff to email me when SIMs are about to lapse.
14. **"Which prepaid cameras are likely to run out of data in the next week based on recent photo volume?"** — Weekly during peak season. List with projected exhaust date. Desktop. Today's report only shows the limit, not the burn rate — and even the limit isn't visible to my role.
15. **"Of cameras that went offline this week, how many were SIM/connectivity vs battery vs hardware vs unknown?"** — Weekly. Pie. Desktop. This is post-mortem analysis we do not do today and probably should.

### Anomalies / one-offs

16. **"Any camera that captured zero photos for the last 30 days but is showing as 'active deployment' and 'online'?"** — Monthly cleanup question. List. Desktop. This is the "ghost camera" problem — physically there but not seeing anything, maybe the lens is fouled, maybe an animal moved it.
17. **"Show me sites where the same camera serial number was deployed, recovered, and re-deployed within 14 days."** — Monthly. List. Desktop. This catches volunteers swapping cameras around without telling the ops room.
18. **"Compare this month's total detections vs the same month last year, by region."** — Monthly. Bar chart. Desktop. Donor-report fodder.
19. **"On the night the snare incident at site 23 happened, what was the activity at the three nearest cameras in the 6 hours before and after?"** — On incident. Time series with multiple lines. Desktop. Forensic question. Today this is a 2-hour manual job.
20. **"Find every photo captured between 02:00 and 04:30 last Tuesday across all of the southern boundary cameras."** — On incident. List with thumbnails ideally. Desktop. This is the kind of free-form filter Looker absolutely cannot do.

A note on phone vs desktop: I split it about 70/30 desktop. But the 30% of phone questions are the highest-stakes ones — incident response, after-hours pings. So phone has to work. Not pretty, just work.

---

## 2. Canned-report replacement (MVP)

I have access to three reports today: Camera Battery Level, Camera Deployment Detail, Photo Count By Camera.

**Camera Battery Level.** I open this maybe twice a week. Mostly I rely on the Top 10 Offline-style dashboards I can't see, and on the warden's central staff to flag low batteries to me. The Looker version is fine but it doesn't sort the way I want, doesn't let me filter to a region without first remembering the org-level filter, and doesn't pair the battery number with site access notes (which is the actual decision input — a 25% battery on a site I drive past every Wednesday is not the same urgency as 25% on a 4-hour bushwhack).

**Camera Deployment Detail.** I use this maybe once every two weeks, and only when planning. It's a wall of rows and I scroll. Honestly more useful as a CSV export than as a dashboard.

**Photo Count by Camera.** I open it maybe once a week to spot-check sites that feel quiet. Same problem — it's a chart, when what I want is "who's anomalously low this week."

### What a chat version makes BETTER

- **Combinable filters.** "Cameras under 30% battery in the western region that I haven't visited in 14 days" — that's three filters across two tables, and Looker can't compose them.
- **Follow-ups.** I ask the offline question, see five cameras, and want to drill: "of those five, which had a battery reading under 40% last week?" In Looker that's a different report or a hand-job.
- **Plain-English bucketing.** "Group these by region" without me having to know the column name.
- **Pairing data with operational context.** If the model can pull access notes, last-visit dates, and the site contact's phone number alongside the battery number, that's a 5x improvement in decision speed.
- **Speed of the morning sweep.** If I can fire off three questions in 90 seconds at 06:00 instead of clicking through three dashboards over five minutes, I get the radio call to the field team out before the heat.

### What it makes WORSE

- **Reproducibility across shifts.** When my colleague comes on at 14:00, the Looker dashboard is exactly where I left it. My chat history isn't necessarily a thing he wants to read. We'll need shared "saved questions" or shift handover notes that include the question + answer.
- **Browsing.** Looker dashboards I can scan visually for outliers I wasn't looking for. A chat answer only tells me what I asked. We'll lose serendipity unless the chat surfaces "by the way, X also looks weird."
- **Trust on first answer.** Looker has the implicit credibility of "this is the dashboard, the data team built it, it's right." A chat answer of "5 cameras are offline" — I will second-guess that for the first month at least. Especially if the LLM ever fabricates a site name. Once.
- **Latency under bad signal.** A Looker iframe loads once and is interactive. A chat-and-wait has to do a round trip per question. On a satellite link with 800ms RTT and load shedding hitting the local tower every other Tuesday, that adds up.

### The four SysAdmin-only reports — which do I want?

All four, honestly, but in priority order:

1. **Top 10 Offline.** I want this every morning. There's no defensible reason it's gated to SysAdmin — these are MY cameras, in MY region. Whoever made that call wasn't thinking about field ops. Give it to UserReport.
2. **Region & Site Totals with Deployment.** Planning meetings, donor reports. Monthly cadence. I can survive without it but I shouldn't have to.
3. **SIM Contract Renewal.** I get blindsided when a SIM lapses. Give me a heads-up window.
4. **SIM Prepaid Data.** Less urgent because central WPS staff seem to manage this, but if I could see it I'd plan around it instead of reacting.
5. **Camera Inventory.** Rarely needed, fine to ask the chat for it on demand rather than have a dashboard.

If the new feature inherits the role gating rigidly, you're carrying forward a permission model that the field doesn't actually agree with. Worth a conversation with the warden's office before MVP.

---

## 3. Free-form chat (phase 2)

### What makes me trust an answer

- **Show the rows.** If you tell me "5 cameras are offline," I want a table underneath with site name, last seen, region. The table is the proof. A naked sentence is not enough.
- **Show the count and the time window.** "5 cameras (out of 142 active) last seen before 2026-05-05 06:00 UTC." Both numbers. Always. Vague answers like "a few cameras are offline" make me lose trust instantly.
- **Show what was filtered.** "I limited to your region (X), excluded decommissioned, used 24h offline threshold." If I disagree with any of those choices I want to see them and override.
- **Show the SQL, optional but available.** I'm not a SQL person, but the warden's data analyst sometimes sits with me. If she can click "show query" and sanity-check it, that's gold.
- **Don't paraphrase the data.** If a site is called "Northern Boundary 4," call it that, not "the northern boundary camera." Names are how I match against my mental map.

### What makes me stop trusting after one bad experience

- **Hallucinated site names.** If the LLM ever invents a camera that doesn't exist, I'm done. Hard requirement: every entity in the answer is a real database row.
- **Wrong scoping.** If I ask about "my cameras" and it returns another organization's data, even once, the trust is gone for months. Org boundaries are sacred.
- **Confidently wrong counts.** "12 cameras are offline" when actually 7 are. If the count drifts by even one I will lose hours re-checking everything.
- **Stale data without saying so.** If the model is reading from a replica that's 4 hours behind and doesn't say so, and I make a field-team call based on stale info — that's the kind of failure that ends the feature.
- **"I don't know" dressed up as an answer.** I'd rather see "I can't answer that with the data I have access to" than a plausible-sounding fabrication.

### Question types I'd never trust an LLM with

- **Anything live-operational with safety implications.** "Is anyone in zone 7 right now?" — no. Cameras are after-the-fact. The LLM cannot see what is happening this second on the ground. If it answers this question at all it has to caveat that it's reporting *captured photos in the last X minutes*, not *current presence*.
- **APU coordination.** "Where should we patrol tonight?" — no. That's a human call informed by data, not a data answer. The LLM can give inputs but should refuse to give the conclusion.
- **Anything involving GPS coordinates of the rhinos themselves.** Even if the data exists. Especially in a chat that might log to a service we don't control. We coarsen GPS to the kilometer for a reason.
- **Legal/compliance questions.** "Are we allowed to share this data with X?" — no, that's the warden's office.
- **Anything about specific staff or volunteers.** "Has Sipho logged in this week?" — no. Surveillance of co-workers via the chat would poison morale immediately.

Operational queries about device state, photo volume, deployment coverage, historical patterns — yes, fully. The line is roughly "data describing the past or present configuration of the system" = OK; "decisions, predictions about people, or live ground truth" = no.

---

## 4. File export

### PDF

- **When:** Monthly warden reports, quarterly donor updates.
- **For:** Warden's office, the foundation that funds our APU, occasionally the provincial parks authority for compliance.
- **What it looks like:** A 4-8 page document with reserve logo, date range, headline numbers (active cameras, total photos, detections by category), a regional map, a couple of trend charts, a table of incident-of-interest captures. Goes by email or printed for the boardroom.
- **Picky requirement:** Page numbers, date generated, the question that produced it written on the cover. Donor lawyers love a paper trail.

### CSV

- **When:** Whenever I want to do something the chat can't, or hand data to the analyst.
- **For:** My own records (I keep a folder per month), the warden's data analyst, occasional partner orgs.
- **What it looks like:** Just the table, columns named in plain English not database column names. Headers I can read. Dates in ISO format because I've been burned by Excel auto-converting before.
- **Hard requirement:** UTF-8 with BOM if Windows Excel is going to open it, otherwise our analyst's Afrikaans place names corrupt.

### DOCX

- **When:** Rarely — but when the warden asks for "a write-up" rather than "a report."
- **For:** The warden's office. They edit it.
- **What it looks like:** Same content as the PDF but in editable form. Tables instead of pinned images. They want to be able to add commentary and forward it.

### Excel (.xlsx)

- **YES, please add this.** The analyst lives in Excel. CSV works but Excel with multiple sheets, formatting, and a header row that's frozen is what she actually wants. Bonus points if you can put the chart on a second sheet alongside the data.

### KML / GeoJSON

- **When:** When I want to overlay camera positions on QGIS or Google Earth — for example, presenting an incursion analysis to the APU commander on a map he already trusts.
- **For:** Internal ops, occasionally law enforcement.
- **What it looks like:** Camera locations + attributes (name, status, last seen) as geometry. KML for non-technical users (drag-and-drop into Google Earth). GeoJSON for the analyst.

### PNG

- **For quick shares on WhatsApp.** Half my coordination with the field team is on WhatsApp because that's what works on bad signal. A chart as a PNG that I can paste into the group chat is more useful than a link to a dashboard the field team can't open. Don't underestimate this.

### Formats I don't need

I have not once needed PowerPoint. The warden does her own decks.

---

## 5. Saved custom queries (phase 3)

### My first three

1. **"The morning sweep"** — every camera below 30% battery OR offline >24h, with site name, region, access notes, and last 7 days of photo volume as a sparkline. I'd run this every morning at 06:00.
2. **"Quiet cameras"** — active deployments with photo count below 50% of their 30-day average over the last 7 days. Weekly. Catches lens fouling, repositioning, and animals that have abandoned a path.
3. **"SIM watch"** — SIMs renewing in the next 45 days, plus prepaid cameras projected to exhaust in the next 14 days. Weekly.

### How I'd find them later

- Pinned to the top of the chat, like favorites in a browser.
- A simple list with my own short labels. Not folders — too much overhead. Maybe two tags max: "morning routine," "monthly report."
- I'd LOVE for "the morning sweep" to be runnable as a one-click rather than retyping the question. A "run" button next to the saved name.

### Sharing

- **With my colleague on the other shift:** absolutely yes. Two-person ops room, we should not be reinventing each other's queries.
- **With the warden's analyst:** yes, for cross-checking.
- **With other reserves:** would be amazing — there's a small WhatsApp group of WPS reserve ops people and we already trade tips. But this requires WPS HQ to think about it, since orgs are otherwise siloed.
- **With WPS HQ:** yes, especially if they curate "best of" queries other reserves should adopt.

### The oh-no scenario

I save a query in May 2026 that says "show all cameras in region X." I re-run it in May 2027. It returns three cameras instead of fifteen — because some were decommissioned, the region was renamed, and the underlying schema added a new "is_archived" column that I should have filtered on but my saved query was written before that column existed.

The terror is: **I don't notice.** I write a report based on three cameras, send it to a donor, and they think we've shrunk the program.

What I want:
- The saved query stores the question (in English) AND the resolved query underneath it.
- When I re-run, if the schema has changed in a way that affects the answer, warn me.
- Even better: when I re-run, show me the previous result alongside the new one. "Last time you ran this on 2026-05-05, you got 15 cameras. Today you got 3. Want to see the diff?"
- A tiny version note: "this query was last validated against schema vX." Boring engineering thing, but it'd save me.

---

## 6. Where and how I'd access this

### Desktop in the ops room

- Standard Chromium browser on a Windows 11 box, two monitors. wpsWatch on the left, the camera feed grid on the right.
- We have a wall-mounted display showing the live camera feed and current alerts; that's not where I want this. The chat lives in the browser, alongside everything else.
- 1080p mostly. Don't assume retina.

### Phone in the field

- Yes, it gets used. Not by me at the desk, but by me when I'm walking back from getting coffee, by the warden when she's in town, by volunteers between deployments.
- **Browser, ideally a PWA.** Don't make us install a separate native app. The wpsWatch web is already responsive-ish; the chat just needs to be usable on a 6" screen with one thumb.
- Phone questions are short and high-stakes. "Any cameras offline?" "Was anything captured at site 14 in the last hour?" Snappy. If a phone question takes more than 5 seconds to come back, I'll just call the ops room.

### Scheduled email digest

- **Yes, please.** A "morning sweep" email at 05:45 in my timezone, before I'm at the desk. Five-line summary, link to the chat for follow-ups. If a camera went offline overnight I want to know it before I sit down.
- Honestly, this might be the highest-leverage feature in the whole proposal. Most useful when *I'm not at my desk*.

### Connectivity

- Ops room: satellite + LTE failover. The satellite link is fine but latency is awful (700-900ms). LTE is faster but flaps when the local tower has issues. Assume one-second round trips minimum.
- Field: intermittent. We do not need the chat to work fully offline in the field — phones get used at base camps where there's at least 3G.
- **What we DO need: graceful degradation.** If the request times out, tell me the request timed out. Don't show me a stale answer or a spinner that never ends. A partial answer with "this is what I have so far, X is still loading" is better than nothing.

### Load shedding

- Stage 4 load shedding is normal. We get 4-6 hours of outage per day, on a published schedule, and our UPS covers about an hour. Beyond that the ops room runs on a generator.
- **A "last known answer" cache for saved queries would be brilliant.** If load shedding hits at 06:15 and I haven't done my morning sweep yet, having yesterday's saved-query result available offline (with a clear "this is from yesterday at 06:00" stamp) would let me cover the gap until power's back.
- This is not the same as full offline mode. It's just "remember the last result of my pinned queries."

---

## 7. Things the dev team should know about real ops-room work

A few things I think you'll get wrong unless someone says them out loud.

**The morning sweep is the heartbeat of the day, not a feature request.** Whatever this thing does, the question "what broke overnight?" must be answerable in under 30 seconds at 06:00 with bad signal and one cup of coffee. If it can't, we won't use it. Build for that one moment first; everything else is cake.

**We're not data analysts. We're operators.** Don't make me think in joins, schemas, column names, or SQL. I know "camera," "site," "region," "deployment," "battery," "photo," "offline." If your model needs me to know more than that, your model is wrong.

**The data model in your code does not match the language we use.** "Deployment" in the database is a thing with start/end dates. "Deployment" to me is *putting a camera in the bush*. "Camera" in the database is `Device`. We say "site" sometimes meaning the geographic location and sometimes meaning the active deployment there. The chat needs to handle this fuzziness — ideally without making me clarify three times.

**Photos are not detections.** A photo is what the camera captured. A detection is when something interesting (animal, human, vehicle) was actually in the frame. Looker's "Photo Count" is a misleading name because it counts every trigger including wind on a branch. The new feature should expose both, clearly labeled.

**Offline cameras are the most expensive thing on the reserve.** Every offline night is a night a poacher could have been seen and wasn't. The cost of a false negative ("this camera is fine, no need to send anyone") is enormous compared to a false positive ("looks low, send someone"). Tune your thresholds and your defaults that way. Err on the side of telling me too much, not too little.

**Trust is asymmetric and slow.** A wrong answer costs 50 right answers. We've been burned before — by a Looker dashboard that silently dropped a region from the filter for two weeks. If your tool ever feels off, even once, it'll be a six-month rebuild of trust. Build the receipts (rows, counts, time windows, source notes) into the answer from day one. Don't add them later.

**Don't optimize for variety, optimize for repetition.** People imagine the value of a chat interface is "any question, any time." For us, 80% of the questions are the same five questions every morning. The highest-value path is making those five questions instantaneous, not making the long tail possible. Saved queries + scheduled email digest > free-form chat, in terms of operational impact.

**The chat will be read aloud over a radio.** When I get an answer at 06:05, I will literally read it on the radio to the field team. Format your answers to be speakable. "Five cameras offline. North Boundary 3, North Boundary 7, East Ridge 2, East Ridge 4, South Pan 1." Not a chart. Not a paragraph. A list.

**The single-org constraint is fine for me but it's a wedge.** I run one reserve. I don't need cross-org. But there are people in WPS HQ who do, and the moment they realize the chat is org-locked, they'll push to break that. Have a story ready.

**Volunteers being excluded from Reports is correct.** Don't unwind that. Volunteers should not be answering questions about device fleet status — it's not their job and the data has operational sensitivity (camera locations, particularly).

**One last thing — please don't put this in a chat bubble in the corner.** Make it a real page in wpsWatch with real estate. The ops room runs on a 27" screen. A 320px-wide chat panel is for someone whose product is mostly something else. Reports IS our work.

Good luck. Genuinely excited about this. Ping me when there's something to break.

— J.
