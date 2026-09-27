# Route briefing: the scenery's taxi route, checked by the AI, with real-world suggestions — design

Date: 2026-09-27. Branch: `feature/route-briefing-taxi-routes` (worked in the worktree branch
`claude/route-briefing-taxi-refine-2963c1`, which starts at the feature tip `c47ecb3e` and is fast-forwarded back onto
the feature branch at the end). Round 6 of the route briefing's taxi section; round 5 is
`2026-09-26-route-briefing-one-grounded-taxi-section-design.md`.

## 1. What changes for a pilot

The Describe Route briefing's **TAXI OUT AND TAXI IN** section keeps both legs, and each leg now has up to three
parts, in this order:

1. **The route paragraph — your scenery's route, as today.** For the taxi out: where the taxi starts (the stand
   you are parked at, named when the scenery marks one, or a typical stand said to be typical when you are not at
   the departure airport yet), the taxiways with their turns, every runway crossing with its hold short, the hold
   short before the departure runway, and the total distance. For the taxi in: the landing runway, the exit with its
   side and distance, the next exit if you miss it, the taxiways with their turns, every runway crossing with its
   hold short, the gate, and the total distance. SayIntentions' runways (both legs) and its arrival gate are used
   exactly as now; a runway SayIntentions assigned that differs from the flight plan is named here and in the
   departure or arrival part of the briefing. The paragraph ends with the short reminder that this is the expected
   route on your scenery and that SayIntentions or ATC gives the actual clearance. Nothing in it comes from the AI's
   own knowledge (the one exception is below).
2. **One check line** — always there when the scenery gave that leg something to check (a route, an exit or a
   stand). It says what the AI checked against — *"Real-world check, from memory rather than live charts: …"*, or
   *"Real-world check, against current charts: …"* only when web search actually found and read that airport's
   charts for this briefing — and then, in a few words, that the scenery agrees with the real airport, or what
   differs, or that the AI does not know the airport well enough to check it. It never claims an agreement or a
   difference it cannot support.
3. **"Real-world suggestions, not from your scenery:"** — a paragraph of at most three short sentences, only
   when there is something to add that the scenery cannot provide: a preferred exit from the real airport's charts
   (its side and distance taken from your scenery's exit list), restrictions that apply to your aircraft's size (a
   wide-body kept off a taxiway, a wingspan limit, a full-length departure requirement), current operational
   information such as a NOTAM closing a taxiway on the route, and at most one sentence on how controllers usually
   route there. Never a full alternative route. Left out entirely when there is nothing to add.

The check line and the suggestions name only taxiways, exits and stands your scenery has; a point that could only be
made with another name is left out (owner's choice: that is the guard that stopped round 4's invented names, KMEM
"V1" and KATL "V3"/"V4"; the accepted cost is that a scenery out of date against the real airport is not called out
by name).

**Kept exactly as now:** the owner's question word for word (`GeminiService.RealWorldTaxiQuestion`, still an
instruction, never output), how every route, exit, stand and gate is worked out, the "typical, not assigned" wording,
one distance unit throughout, the OpenStreetMap wording, and the rest of the briefing (overview, SID, enroute, STAR,
weather, NOTAMs, the 600 to 900 word target).

**The one place names outside the scenery can appear** (owner confirmed, 2026-09-27): a leg with no taxiway data in
the scenery and none from OpenStreetMap still gets the AI's usual real-world route, said to be general knowledge and
not checked against the scenery — unchanged from round 5.

**Web search:** off by default for both AI providers (Settings, AI tab). With it off, every check line says "from
memory rather than live charts". The app tells the AI whether search is on for that request, so a check line can
never claim live charts it did not have — including when Claude's model cannot use web search and the briefing is
redone without it.

## 2. Why

Owner, 2026-09-27: strip the AI's part back to verifying the scenery's information, and have it add the real-world
data and procedures the navdata cannot provide — preferred exits from the charts, wide-body restrictions where they
apply — clearly flagged as suggestions. First asked for the landing exit and taxi in only; then, the same day, for the
departure as well, with SayIntentions' departure runway kept. SayIntentions assigns no departure gate (its
`assigned_gate` is always the arrival gate — per its developer, and the KMEM capture: `getParking` named KATL "B3"
with the aircraft at KMEM Gate 17), so the taxi out keeps starting where the aircraft is parked, else a typical stand
(owner's choice, 2026-09-27).

Owner's choices in this round (2026-09-27): one short agreement line rather than silence (silence cannot tell "checked
and fine" from "never checked") or a verdict per part; suggestions in their own paragraph rather than inline; names
outside the scenery left out rather than flagged; the check made inside the same briefing request (no extra wait or
cost; web search only when the pilot turned it on) rather than a separate request that forces search on.

## 3. Scope

**Changed:**

- The prompt's section 7 (`GeminiService.GetRouteDescriptionPrompt`), rewritten as in §4. Every other section, the
  guidelines and the word target are byte-identical.
- `GetRouteDescriptionPrompt(string flightData)` gains a required `bool webSearch`: whether the request carrying this
  prompt has web search. It changes exactly one sentence of section 7 (§4, the search sentence).
- `GeminiService.DescribeRouteAsync` passes its `enableSearch` (`GeminiSearchGrounding`).
  `ClaudeService.DescribeRouteAsync` passes its `enableSearch` (`ClaudeWebSearch`) on the first attempt and `false`
  on the retry it makes when the model rejects the web_search tool — the retry runs without search, so its prompt
  must say so.
- `RouteDescriptionPromptTests` (§5), `docs/gemini.md` ("Taxi routes in the route briefing"), and the CLAUDE.md
  invariant bullet that describes the taxi section.

**Not changed:** the TAXI ROUTES block and its renderer, the planner and everything it uses (tiers, the online name
wait, the stand and exit pickers, turns, hold shorts, the OpenStreetMap planning graph), SayIntentions' runways and
gate, the EFB form, Taxi Assist, `RouteBriefingText.RemoveEchoedTaxiQuestion` (still in both providers), and Gemini's
truncation note.

## 4. The prompt's section 7

Exact text (C# verbatim interpolated string: `""` is one `"`; `{RealWorldTaxiQuestion}` and `{searchSentence}` are
interpolated). Lines are indented three spaces like the rest of the prompt, with no blank lines inside the section.

```
7. TAXI OUT AND TAXI IN
   The flight plan data ends with a TAXI ROUTES block worked out from the pilot's own simulator scenery.
   Write this section as two legs, the taxi out at the departure airport and then the taxi in at the arrival airport, each in up to three parts in this order: the route paragraph, the check line and the suggestions paragraph, each part on its own line.
   For each leg, answer the question below from that leg's lines of the TAXI ROUTES block, taking the bracketed items (the airport, the runway, the stand or terminal, and the aircraft type) from them (the runway there may be the one SayIntentions assigned rather than the flight plan's):
      ""{RealWorldTaxiQuestion}""
   That question is an instruction to you, not text for the pilot: write only your answer, and never write the question itself into the briefing, as shown here or with the items filled in.
   For the taxi out, the route runs from the stand to the departure runway; for the taxi in, it runs from the landing runway, via the exit, to the stand.
   The route paragraph is one short paragraph per leg, in the voice of real-world operations, and apart from the general-knowledge route described below, nothing in it comes from your own knowledge.
   The route comes from the block only: the stand, the taxiways in order with the turn at each change of taxiway and into the stand wherever the block gives one, every hold-short point and the runway it protects, and for the arrival which side to leave the runway (left or right), the exit taxiway and its distance from the threshold, the next exit if that one is missed, every runway crossed, and the gate.
   Use ONLY the taxiway, exit and stand names given in the block, and repeat distances, sides and turn directions exactly as given; where the block gives no turn for a taxiway, give none.
   Give the total taxi distance for each leg the block gives one for, and never estimate one.
   At the end of each route paragraph, say in a short phrase that this is the expected route on the pilot's scenery and that SayIntentions or ATC will give the actual taxi clearance.
   Always give every runway the route crosses, including one a note says has no hold short point, and when a note says the mapped route leaves the runway on another taxiway, say which.
   Mention any other note from the block only when it changes what the pilot does or hears, such as a runway SayIntentions assigned that differs from the flight plan, a representative stand (say it is typical, not assigned), a SayIntentions gate the scenery lists under another name or at a different position, a stand the scenery marks as a fuel or other special stand, or a taxiway width or stand size note.
   When a leg's route comes from OpenStreetMap, say so in a few words, and call it the expected route on OpenStreetMap's map rather than on the pilot's scenery; taxi guidance cannot use it.
   If the block says a leg is unavailable, say so in a few words, and still give whatever the block does give for that leg, such as the exit with its side and distance, and the stand.
   When the reason is that the aircraft is already at the runway, give no route for that leg.
   Otherwise you may give that leg's usual route from your own knowledge, saying it is general knowledge and not checked against the scenery; where the leg has a ""Taxiway names at"" list, name only taxiways from it, and only a leg with no such list may name taxiways the block does not give.
   The scenery phrase belongs only to a route the block gives: end a general-knowledge route by saying only that SayIntentions or ATC will give the actual taxi clearance, never that it is the expected route on the pilot's scenery, and give no scenery phrase for a leg with no route.
   The check line is one sentence after each route paragraph that checks the block's route, exit and stand for that leg against the real airport as you know it.
   Begin it with ""Real-world check, from memory rather than live charts:"", or with ""Real-world check, against current charts:"" only when a web search in this briefing found and read that airport's current airport diagram or chart notes.
   {searchSentence}
   When they agree, say so in a few words; when something differs, name what differs instead; when you do not know the airport well enough to check it, say so; never claim an agreement or a difference you cannot support.
   Leave the check line out for a leg the block gives no route, exit or stand for.
   The suggestions paragraph comes after the check line (or after the route paragraph when there is none), only when you have something to add that the scenery cannot provide, and otherwise is left out; it begins ""Real-world suggestions, not from your scenery:"" and has at most three short sentences.
   It may give a preferred exit from the real airport's charts, restrictions that apply to this aircraft's size (from the block's Aircraft line, such as a wide-body kept off a taxiway, a wingspan limit or a full-length departure requirement), current operational information such as a NOTAM closing a taxiway on the route, and at most one sentence saying that controllers usually route differently there; never give a full alternative route.
   A suggested exit takes its side and distance from the block's exits list, and gets none when the list does not give them.
   When the block gives no size class for the aircraft, say which aircraft a size restriction applies to.
   Any taxiway, exit or stand you name in the check line or the suggestions must appear in that leg's lines, including its ""Taxiway names at"" list; when a point could only be made with a name that is not there, leave the point out.
   When the name rule above makes you leave a point out of a check line, do not call that leg an agreement: say that not everything could be checked, without naming what.
   Keep it short: do not list every exit, and do not describe where the data came from beyond the wording this section asks for.
   Give every distance in this section in the unit the block's ""Distance unit"" line names, and never mix units.
   When a leg's note says SayIntentions assigned a different runway from the flight plan, say so here, and also in the DEPARTURE AND SID or ARRIVAL AND STAR section, naming both runways.
```

The search sentence (`{searchSentence}`):

- `webSearch` false: `Web search is off for this briefing, so every check line begins ""Real-world check, from memory rather than live charts:"".`
- `webSearch` true: `Web search is on for this briefing: do any lookups before you write any part of the briefing, and you may look up each airport's current airport diagram and chart notes; a check line says current charts only when that search found and read them.`

What moved relative to round 5: the AI's own additions (restrictions, current operational information, the one
"controllers usually route differently" sentence) leave the route paragraph for the labelled suggestions paragraph,
which also gains preferred exits from the charts — usual routing practice survives only as that one "controllers
usually route differently" sentence, the rest of round 5's "usual practice" wording was dropped; the name rule now
covers the check line and the suggestions and drops a point rather than inventing a name; the check line is new.

Amended 2026-09-27 after the final review (owner approved): a general-knowledge route no longer borrows the scenery
phrase, and a leg with no route at all gets no scenery phrase either; the check line's overclaim guard now covers a
claimed difference as well as a claimed agreement; leaving a point out of a check line is described only as "not
everything could be checked," never as an agreement; the search-on sentence now tells the AI to do its lookups
before it starts writing, and "current charts" needs the material found and read, not merely found; and the
suggestions paragraph falls after the route paragraph, not the check line, on a leg with no check line at all.

Amended again 2026-09-27 (fix wave 2): "before you start writing" could be read as "before this section" rather
than "before this briefing," and with Claude only the text after the last tool block survives `ParseResponse`, so a
lookup delayed to section 7 would drop sections 1-6 — the search-on sentence now reads "before you write any part
of the briefing." And "when you leave a point out of a check line" was read too broadly: a one-sentence check line
always leaves detail out for brevity, so the rule is now explicitly tied to the name rule two lines above it —
"when the name rule above makes you leave a point out of a check line." Neither change touches wording pinned
elsewhere in this section; the exact strings quoted throughout §4 above and the pin list in §5 are updated to
match.

## 5. Testing

`RouteDescriptionPromptTests`, calling `GetRouteDescriptionPrompt(data, webSearch: …)`:

- **Kept pins** (unchanged wording): the section heading; the question verbatim from the constant, and never written
  out; the direction clause; the block-only route with names, distances, sides and turns exactly as given and no
  invented turn; the total distance never estimated; the preview phrase; every crossing always given; the notes
  filter with the representative-stand and special-stand wording; the OpenStreetMap wording; the unavailable-leg and
  general-knowledge rules; the distance unit; the SayIntentions runway difference in both places; "do not list every
  exit" and "do not describe where the data came from"; the word target; no "Real-world practice" heading.
- **New pins:** the three parts per leg, in order, each on its own line; nothing in the route paragraph from the AI's
  own knowledge except the general-knowledge route; the two exact check-line openings and "current charts" only when
  a search found and read them; agreement, difference and "does not know the airport" wording, and never an
  unsupported agreement or difference; the check line left out when the block gives nothing to check; the exact suggestions opening, at most
  three short sentences, left out when there is nothing to add; the suggestion kinds (preferred exit from the charts,
  size restrictions from the Aircraft line, current operational information, at most one routing sentence) and never a
  full alternative route; a suggested exit's side and distance from the exits list; the unknown-size rule; the name
  rule for the check line and suggestions, with the point left out rather than a name invented.
- **Two more pins, added in fix wave 1 (final-review coverage the original brief's items didn't reach on their own):**
  `A_general_knowledge_route_is_never_called_the_scenery_route` — a general-knowledge route ends only with the
  "SayIntentions or ATC will give the actual taxi clearance" phrase and never with the scenery-route preview phrase,
  and a leg with no route at all gets neither; `The_search_sentence_follows_the_check_line_opening_rule` — the search
  sentence sits immediately after the "Begin it with …" check-line-opening rule, as one unit, in both the
  search-off and search-on prompts.
- **The search flag:** off gives the off sentence and never the on sentence, and on the reverse; with each sentence
  removed, the two prompts are identical (the flag changes nothing else); both contain the flight data and the
  question.
- **Removed pins** (their wording is gone): "ONE short paragraph for the taxi out at the departure airport and ONE
  short paragraph for the taxi in …", "From your own knowledge you may add …", "at most one sentence per leg saying
  that controllers usually route differently there", "but only where it concerns a taxiway, runway or stand the block
  names", "Any taxiway you name must appear in that leg's lines …".
- **Fix wave 2** re-pinned two of the strings above to their tightened wording rather than adding new tests: the
  search-on sentence ("before you write any part of the briefing" in place of "before you start writing") in
  `The_search_sentences_are_exact`, and the omission rule ("When the name rule above makes you leave a point out of
  a check line…" in place of the unscoped "When you leave a point out of a check line…") in
  `The_check_line_and_suggestions_name_only_what_the_scenery_has`. No test was added or removed by wave 2.

The provider wiring (which flag each request passes, and `false` on Claude's retry) is a one-argument change per call
site with no unit seam; it is covered by review and by the in-sim check with search off and on. The full suite must
pass (8,507 on the feature tip before this round).

**In-sim (owner):** KMEM→KATL with SayIntentions running, search off: both legs' routes unchanged from the round-5
build (taxi out J, T, P, N, M, M2 to 36L; taxi in B11 on the right, B, D across 08R, E, F5, F to C50), one check line
per leg beginning "Real-world check, from memory rather than live charts:", suggestions only when there is something to
add and naming only real taxiways, and the question not read back. Optionally a second press with web search on, to
hear "against current charts" (or "from memory" when the search found none).

## 6. Docs and project notes

- `docs/gemini.md`, "Taxi routes in the route briefing": the prompt paragraph describes the three parts per leg, the
  check line and its search-dependent opening, the suggestions paragraph and the name rule; the History sentence gains
  round 6.
- CLAUDE.md: the bullet beginning "The briefing's taxi section is ONE short part" is rewritten for the three parts per
  leg, keeping every guardrail it carries (block-only route, preview phrase, every crossing always given,
  `UnheldRunways`, names only from the leg's lines, never a full alternative route, SimBrief-only classification,
  widths advisory).

## 7. Branch and build

Work continues the one feature branch, local only, never pushed unless the owner says so. It is done in this worktree's
branch and fast-forwarded onto `feature/route-briefing-taxi-routes` at the end — that branch is checked out in the
owner's main folder, so the owner is asked before that folder is touched. The owner's one test build
(`D:\Claude\oasis1701\builds\MSFSBA-route-briefing-pr160-debug\`) is refreshed as before: PR #160 is merged only into a
throw-away detached copy that is deleted afterwards, never into the branch.
