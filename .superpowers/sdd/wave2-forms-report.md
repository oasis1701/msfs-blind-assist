# Test Wave 2 — Forms pure-logic cluster + GetRunwayDetailedInfo seam

## Summary

All six targets covered. Build: 0 warnings / 0 errors. Baseline confirmed at exactly 619
(via `git stash`/`dotnet test`/`git stash pop`, restored and re-verified immediately after).
Full suite after this wave: 737 passed, 0 failed — 118 new tests added across 6 new test
files, all passing on first run (no expectation corrections needed — every hand-computed
literal matched actual output).

## Targets covered

1. **DisplayText.SetPreserveCaret** (`Forms/DisplayText.cs`) — COVERED, no production changes
   needed. The method and its containing class were already `public static`. A plain
   `TextBox` constructs fine in the xUnit host (net10.0-windows) and `GetLineFromCharIndex`/
   `GetFirstCharIndexFromLine` work without a visible window handle or message pump.
   Tests: `tests/MSFSBlindAssist.Tests/DisplayTextSetPreserveCaretTests.cs` (9 tests) — no-op
   on unchanged content, null box/null text safety, common-prefix/suffix minimal edit,
   caret-line restore across same-line-count / growing / shrinking edits, ReadOnly
   round-trip.

2. **DisplayList.UpdateInPlace** (`Forms/DisplayList.cs`) — COVERED, no production changes
   needed (already `public static`). A plain `ListBox` constructs fine in the test host.
   Tests: `tests/MSFSBlindAssist.Tests/DisplayListUpdateInPlaceTests.cs` (14 tests) — null/
   disposed-control safety, first populate, no-op fast path, row-content rewrite, tail
   grow/shrink, selection-follows-content when rows shift above the cursor, nearest-
   occurrence selection among duplicate rows, clamp-on-disappearance, and the "no selection
   before -> none introduced" case. `TopIndex` is deliberately NOT asserted — an unshown/
   unsized `ListBox` reports 0 visible rows in this headless host, so round-tripping it isn't
   meaningfully testable here; that's a secondary behavior next to the documented
   selection-preservation contract.

3. **HotkeyListForm filtering** (`Forms/HotkeyListForm.cs`) — COVERED via one small, explicitly
   permitted extraction (the item's own instruction says "Extract as pure string logic if
   entangled with the form"; the whole form is heavily entangled — constructor reads
   `HotkeyGuides\*.txt` off `AppDomain.CurrentDomain.BaseDirectory` and builds a full combo/
   textbox/button tree — so constructing the live form for this narrow test was judged
   higher-risk than a 3-line, zero-logic-change extraction).
   - **Promotions** (pure access-modifier changes): nested `enum HotkeyMode`
     `private -> internal`; nested `sealed class CategorySection` `private -> internal`
     (all its members were already `public`); `const string AllCategoriesLabel`
     `private -> internal`.
   - **Seam**: the inline 3-line "isAllCategoriesSentinel" boolean expression inside
     `ApplyFilters` was moved verbatim into a new `internal static bool
     IsAllCategoriesSentinel(string search)` method, zero logic change, called from the
     original call site.
   Tests: `tests/MSFSBlindAssist.Tests/HotkeyListFormFilterTests.cs` (~25 tests) — the 3-char
   sentinel floor (pins the "a"/"al" short-circuit-Altitude/Airspeed bug this exists to
   prevent), `CategorySection.DisplayName`/`ModeOrder`/`Matches`/`GetFilteredText`
   (including the header-line-plus-matching-block filtering and a wrapped-description-line
   match).

4. **TcasForm callsign/aircraft-type parsing** (`Forms/TcasForm.cs`) — COVERED via pure
   access-modifier promotions only (all targets were already pure `private static`
   functions with zero WinForms coupling): `FormatCallsign`, `ShortenAircraftType`,
   `FormatRoute`, `BuildItemText`, `TrafficKey` all promoted `private -> internal`.
   Tests: `tests/MSFSBlindAssist.Tests/TcasFormParsingTests.cs` (~40 tests) — callsign
   spacing (incl. registrations/hyphenated left unchanged), bare/embedded ICAO extraction,
   wake-suffix stripping, digit-model-to-ICAO mapping, manufacturer-prefix stripping, route
   formatting, traffic key fallback, and `BuildItemText` assembly (airborne vs. ground,
   gate-label suppression for airborne even with a resolver supplied, unknown-callsign
   fallback, route inclusion, omitted type/airline).

5. **FbwMcduFormat** (`Services/FbwMcduFormat.cs`) — COVERED, **zero production changes** of
   any kind. Every target (`DecodeCell`, `PositionLine`, `LitAnnunciators`, `JoinColumns`,
   `BuildDisplayData`) was already `public static` on a `public static class`; no promotion
   or seam needed. No conflict risk with any Services-cluster agent since this file was not
   touched at all.
   Tests: `tests/MSFSBlindAssist.Tests/FbwMcduFormatTests.cs` (~24 tests) — color-tag
   decoding, the mixed-color green-segment `*`-marker rule, `{sp}` literal-space insertion,
   drop-tags (`small`/`big`/`left`/`right`), the LSK arrow/bracket glyph (unmatched `{`/`}`)
   drop-without-eating-content rule, `PositionLine`'s left/center/right layout and trailing-
   space trim, `LitAnnunciators`' documented order and strict-boolean-type gate, `JoinColumns`,
   and a full canned-JSON `BuildDisplayData` assembly (title/page/scratchpad/annunciators/
   arrows/lines/RawLines, including out-of-range line pairs resolving to blank not throwing).

6. **GetRunwayDetailedInfo** (`Forms/ElectronicFlightBagForm.cs`) — COVERED via a minimal,
   surgical seam. **Decision: extraction, not a synthetic-SQLite fixture.**
   - The method is a genuinely monolithic ~300-line block: opens its own SQLite connection
     via `DatabasePathResolver`/`SettingsManager.Current`, runs the runway+runway_end+airport
     join, then a SECOND nested ILS query, then a `LittleNavMapProvider` spatial-ILS-recovery
     fallback, all interleaved with `StringBuilder` formatting of ~40 columns across 10
     sections (ILS, dimensions, surface, headings, coordinates, threshold, pattern, lighting,
     markings, operations, VASI). Extracting the WHOLE method into a DB-free pure formatter,
     or driving the private instance method through reflection on a fully-constructed
     `ElectronicFlightBagForm` (which itself needs a `FlightPlanManager`, `SimConnectManager`,
     `ScreenReaderAnnouncer`, `WaypointTracker`) were both judged HIGHER-risk for this wave
     than a small, targeted extraction.
   - Instead, the two blocks that actually consume the aliased `runway_end` columns — the
     exact ones the CLAUDE.md invariant ("`GetRunwayDetailedInfo` must explicitly alias the
     runway_end columns … the bare columns are ambiguous … and silently resolve to the
     primary-end value") is about — were extracted VERBATIM (zero logic change) into:
     - `internal static string FormatRunwayHeadingsAndCoordinates(double endHeadingTrue, double magVar, object? endLonx, object? endLaty)`
     - `internal static string FormatRunwayPatternAltitude(object? patternAltitude, object? endAltitudeMsl)`
     Both take already-resolved column values (not a DB connection/reader) and are called
     from `GetRunwayDetailedInfo` exactly where the original `sb.AppendLine` calls were
     (`GetRunwayDetailedInfo` itself, its SQL, and every other section, are untouched).
   Tests: `tests/MSFSBlindAssist.Tests/ElectronicFlightBagFormRunwayInfoTests.cs` (8 tests) —
   pins the secondary-end-shows-its-own-heading-not-the-reciprocal's-180°-off-value case
   explicitly (240.0° in, asserts 240.0° out AND asserts 60.0° — the reciprocal — is absent),
   magnetic-heading subtraction, coordinate passthrough, `DBNull.Value` -> blank rendering
   (characterizes the existing, pre-wave lack of a null-guard on these specific fields — not
   a new bug, just pinned as-is), and the pattern-altitude/MSL-altitude block.

## WinForms-in-test limitations hit

None that blocked a target. Plain `TextBox`/`ListBox` construction and the specific methods
used (`GetLineFromCharIndex`, `GetFirstCharIndexFromLine`, `Items` manipulation, `SelectedIndex`,
`BeginUpdate`/`EndUpdate`) all work in this xUnit host without a message pump or `[STAThread]`.
The only WinForms behavior deliberately left untested is `ListBox.TopIndex` round-tripping
(see item 2) because an unshown/unsized control reports 0 visible rows headless — that's a
test-host measurement limitation, not a target that had to be dropped.

## Verification

- `dotnet build MSFSBlindAssist.sln -c Debug -p:Platform=x64` -> Build succeeded, 0 Warning(s), 0 Error(s).
- `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64`
  -> Passed! Failed: 0, Passed: 737, Skipped: 0, Total: 737.
- All 118 new tests passed on the FIRST run with no expectation corrections — every
  hand-computed literal (spacing in `GetRunwayDetailedInfo`'s format strings, regex-derived
  callsign/ICAO outputs, `PositionLine` column math, `DecodeCell`'s mixed-color `*`-marker
  placement) matched actual output exactly.

## Pinned bugs / behavior notes (not fixed, per characterization methodology)

- None found to be genuine NEW bugs. The one edge case worth flagging: `reader["end_lonx"]`/
  `reader["end_laty"]`/`reader["pattern_altitude"]`/`reader["end_altitude"]` in
  `GetRunwayDetailedInfo` are interpolated directly with no `DBNull.Value` guard (unlike most
  other fields in the same method), so a NULL column renders as a blank value rather than
  "N/A". This is PRE-EXISTING behavior (unchanged by the extraction) and is pinned as-is in
  `ElectronicFlightBagFormRunwayInfoTests` rather than "fixed", per the wave's
  characterization-only mandate.
