# flyPad Settings — live accessibility audit (A380X, 2026-06-05)

Captured live via `coherent-eval.ps1` + `coherent-flypad-agent.js` (the in-sim flyPad
on each `/settings/<category>` sub-page). Raw scrapes: `tools/_probe/_cat_*.json`,
`_audio_raw2.json`. Each Settings category is a sub-route off `/settings` (an index of
8 category links); a category's top-left title (idx 12) is the **back** affordance.

## Structural / global issues (affect every Settings page)

1. **Page label is doubled.** Every detail page announces
   `"Settings: Settings - <Category>"` — `pageLabel()` prepends the active nav-rail
   route ("Settings") to a content `<h1>` that ALSO starts with "Settings - ".
2. **Back link reads as the page title.** idx 12 on every detail page is
   `"Settings - <Category>"` with no hint it navigates back. Should read e.g.
   "Back to Settings".
3. **All 8 category links read "(selected)" on the index.** `isActiveTab` matches the
   `bg-theme-accent` token on every category card, so the screen reader can't tell
   which category is active. Only the active one (or none) should be marked.

## Segmented "SelectGroup" controls — the main clutter source

These are rendered as a setting-name TEXT line followed by N **separate buttons**, with
no grouping and no binding of the options to the setting name. The "(selected)" marker
works, but the user must read the name then arrow through loose buttons:

| Page | Setting | Options (live) |
|---|---|---|
| Aircraft | ISIS Baro Unit | `hPa`, `hPa/inHg (selected)` ← NOT a bug: FBW's own option name (`AircraftOptionsPinProgramsPage.tsx:78` `{ name: 'hPa/inHg' }`); left faithful |
| Sim Options | Default Barometer Unit | Auto*/inHg/hPa |
| Sim Options | (MSFS route auto-load mode) | Auto*/Off + stray `Inactive` text |
| Sim Options | SimBridge Host Machine | This PC*/Remote PC |
| Sim Options | Pilot Seat for Control | Auto/Left*/Right |
| Realism | ADIRS Align Time | Instant/Fast*/Real |
| Realism | DMC Self Test Time | Instant/Fast/Real* |
| Realism | Boarding Time | Instant/Fast/Real* |
| ATSU/AOC | ATIS/ATC Source | FAA*/PilotEdge/IVAO/VATSIM |
| ATSU/AOC | METAR Source | MSFS*/NOAA/PilotEdge/VATSIM |
| ATSU/AOC | TAF Source | MSFS*/NOAA |
| ATSU/AOC | ACARS Provider | None*/Hoppie/BATC/SAI |
| flyPad | Time Displayed | UTC*/Local/UTC and Local |
| flyPad | Theme | Blue*/Dark/Light |

(* = currently selected.) Desired: each reads as a labelled group, e.g.
"ISIS Baro Unit: hPa (selected), inHg" — the option name carries the setting context.

## Mislabeled / value-polluted inputs (`fieldName` picks the wrong text)

- **Aircraft → Engine-Out Acceleration Height** input reads **"Acceleration Height (ft)"**
  — borrowed the previous row's label (a real mislabel; two rows now read identically).
- **Aircraft** inputs fold the current value into the label:
  `"Thrust Reduction Height (ft) (1500)"` then value `1500` again → number spoken twice.
- **Sim Options → External SimBridge Port** input reads **"Automatically Load MSFS Route"**
  (= 8380) — borrowed a far-away toggle's name.
- **3rd Party → Override SimBrief User ID** input reads **"Settings - 3rd Party Options"**
  (the page title fallback) — no real label.

## Audio page — effectively unreadable (worst page)

3 real volume controls (`Exterior Master Volume`, `Engine Interior Volume`,
`Wind Interior Volume`), each = a far-left `<span>` label + an `rc-slider` + a number
`<input>`. The agent emits the rc-slider's internal rail/track/step/handle nodes as
**4 phantom "slider" lines per volume — 12 total — all labelled "Settings - Audio"**
(page-title fallback). Only the 3 number inputs get a correct label. Fix: suppress the
rc-slider internals (as Fuel/Quick-Controls brightness already do) and keep one labelled
control per volume (the number input, or one re-emitted accessible slider bound to it).

## 3rd Party page — ordering + concatenation

- A description line ("Alternative SimBrief user ID to use instead of the linked
  Navigraph Account") floats **above** the back link (ordering).
- `"kn4ieeNavigraph Standard"` — username + plan name concatenated with no separator.

## flyPad page — Language / Keyboard not reachable as controls

`Language` → `English` and `Onscreen Keyboard Layout` → `English` render as **inert text**
(the SelectInput trigger div isn't classified as a control), so they can't be changed via
the reader. (Low priority, but currently inaccessible.)

## About page — OK

Reads cleanly as heading + label/value text pairs. Only nit: the long `SHA` splits across
two text lines. No action needed.

## Fix approach (proposed)

A **Settings-aware builder** in `coherent-flypad-agent.js` (mirroring
`buildPayloadLines`/`buildFuelLines`): detect a `/settings/<x>` sub-page, then for each
setting row emit ONE clean labelled line per control —
- segmented groups → "Name: opt (selected), opt, opt" (or a real radio-group),
- inputs → "Name (unit) = value" using the ROW's own label (fixes the borrowed-label +
  doubled-value bugs),
- Audio → suppress rc-slider internals, keep the labelled value,
plus the global fixes: de-double the page label, label the back link, and fix the
all-"(selected)" category index.

## Post-fix verification (2026-06-06)

Implemented a Settings-aware builder (`A.buildSettingsLines`) in
`coherent-flypad-agent.js` + two global helper fixes. Regression harness:
`tools/flypad-settings-test/` (jsdom, 17 tests, live-baked fixtures) — all green.
Live-verified in the sim (updated agent via `coherent-eval.ps1`):

| Page | Result |
|---|---|
| index | no category falsely "(selected)"; page label "Settings" (not doubled) |
| Aircraft | "Back to Settings"; inputs labeled from own row, value not duplicated; Engine-Out correctly labeled |
| Sim Options | compound "Auto Cabin Lighting" (toggle) + "Cabin Lighting Brightness" (input) split correctly; "External SimBridge Port" labeled; "Throttle Detents: Calibrate" qualified |
| Realism | segmented selectors = name line + tight options, one (selected) |
| 3rd Party | "Override SimBrief User ID" labeled; floating description + "kn4iee…"-concat gone |
| Audio | 12 phantom rc-slider lines gone; 3 labeled volume controls |
| flyPad | "Auto Brightness" toggle restored; "Time Displayed" reads name + UTC/Local/UTC-and-Local; Language/Keyboard now reachable as links (bonus) |
| About | bails to the generic pass (info table preserved) |
| Calibrate | UNREGRESSED — `orderThrottleCalib`/`relabelThrottleCalib` intact (axis values + detent low/high) |

Actuation spot-check: clicking ADIRS "Real" moved "(selected)" Fast→Real (options
stay individually clickable). Before→after example (Aircraft input):
`"Acceleration Height (ft) (1500)" = 1500` (wrong label + doubled value) →
`"Engine-Out Acceleration Height (ft)" = 1500` (own-row label, value once).
