# Wave 2 — Services/GSX pure-logic characterization tests

## Scope covered

All 8 targets from the task were covered:

1. **GsxNavdataMerger** (`Services/Gsx/GsxNavdataMerger.cs`) — new `GsxNavdataMergerTests.cs`.
   Pinned the never-cross-concourse-borrow invariant (same-concourse donates coordinates,
   cross-concourse drops the spot entirely, including the multi-candidate case), the full
   position-priority chain (parking pos > navdata > stop pos as last resort), the
   unplaceable-gate drop, navdata-only append, and the no-concourse name-borrow behaviour.

2. **GsxPyOffsetEvaluator** (`Services/Gsx/GsxPyOffsetEvaluator.cs`) — new
   `GsxPyOffsetEvaluatorTests.cs`. Covered all four dispatch idioms (ByIdMajor,
   HandleAircraftOffsets incl. the both-miss-degrades-to-Zero Python-None case,
   IcaoAircraftOffsets 3-level fallback, ByGroup ARC/bare/category fallback) plus the
   `GsxOffset.Zero` strict-no-op on null profile / null aircraft / unmapped gate / null
   function / unclassified function body. Built synthetic `.py` fixtures via
   `GsxPyProfileReader.FromText`, syntax mirrored from `tools/GsxOffsetProbe/Program.cs`'s
   live-profile golden cases.

3. **GsxParkingNameEnum** (`Services/Gsx/GsxParkingNameEnum.cs`) — new
   `GsxParkingNameEnumTests.cs`. Pinned the A=12..Z=37 round-trip, the letterless-kind
   (NONE/PARKING/GATE/DOCK) "no concourse to compare" over-confirm behaviour, suffix
   agreement/mismatch, and the -1/0 "no suffix" decode.

4. **TaxiDataMerger** (`Services/TaxiAugment/TaxiDataMerger.cs`) — new
   `TaxiDataMergerTests.cs`. Pinned all four documented anti-grass invariants: navdata
   names never overwritten (online disagreement becomes an alias, not a rename); an
   unnamed segment freely adopts a matching online name; online-only geometry with no
   navdata match is ignored; the ambiguity guard (factor + epsilon) refuses a guess when
   two differently-named candidates are both in tolerance; plus the bearing gate and the
   apt.dat-wins-on-disagreement rule.

5. **AptDatParser** + **TaxiGeo** (`Services/TaxiAugment/AptDatParser.cs` /
   `TaxiGeo.cs`) — new `AptDatParserTests.cs` (1201/1202/1300 row shapes, unknown-node
   skip, non-taxiway-type skip, no-name skip, malformed/non-numeric row tolerance, CRLF)
   and `TaxiGeoTests.cs` (Haversine, bearing cardinals + undirected diff, WrapDeltaDeg,
   antimeridian-safe MidpointLon/PointToSegmentMeters, degenerate zero-length segment).

6. **GateSearchFilter** (`Services/GateSearchFilter.cs`) — new `GateSearchFilterTests.cs`.
   Normalize/NormalizeIdentity, NormalizeGateName + StandTypeWords stripping (incl. the
   deliberate GA exception and bare-type-word-only -> empty), Matches (substring,
   case/whitespace-insensitive, alias fallback, null alias list), Filter.

7. **DockingGuidanceManager.FriendlyVdgs** — promoted `private static` -> `internal
   static` (zero logic change); new `DockingGuidanceManagerFriendlyVdgsTests.cs`. Noted
   in the file header that this is a DIFFERENT mapping from `ParkingSpot.FriendlyVdgs`
   (different output strings, no Vgds/Honeywell handling) to avoid future confusion.

8. **GsxService.TextRules** — skipped per instructions (already covered by
   `GsxTextRulesTests.cs` in Phase 1); no untested members found.

## Bugs found

None. All behavior matched the read-the-source expectations once one test-authoring
mistake was fixed (see below) — no production logic was changed beyond the one access
promotion.

## Process note

First test run had 10 failures, all in `GsxPyOffsetEvaluatorTests`, all returning
`GsxOffset.Zero` where a non-zero value was expected. Root cause: my synthetic `.py`
fixtures wrote `parkings = { 66 : (Cat, func, ), }` as a single line, but
`GsxPyProfileReader`'s `GateEntryRegex` is anchored per-line (`^...$` with
`RegexOptions.Multiline`) and only matches a gate entry on its OWN line — mirroring real
GSX `.py` formatting. Fixed by spreading each fixture's dict entry onto its own line
(`parkings = {\n66 : (...),\n}\n`). This was a test-fixture bug, not a production bug —
confirms the "fix the test, not the code" rule.

## Promotions

- `DockingGuidanceManager.FriendlyVdgs`: `private static` -> `internal static`
  (`MSFSBlindAssist/Services/DockingGuidanceManager.cs`). No other access-modifier
  changes were needed; every other target was already public/internal static.

## Build / test results

- `dotnet build MSFSBlindAssist.sln -c Debug` -> Build succeeded, 0 Warning(s), 0 Error(s).
- `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64`
  -> Passed! Failed: 0, Passed: 759, Skipped: 0, Total: 759.
- Baseline was 619 -> **140 new tests** added across 8 new files.

## New files

- `tests/MSFSBlindAssist.Tests/GsxNavdataMergerTests.cs`
- `tests/MSFSBlindAssist.Tests/GsxPyOffsetEvaluatorTests.cs`
- `tests/MSFSBlindAssist.Tests/GsxParkingNameEnumTests.cs`
- `tests/MSFSBlindAssist.Tests/TaxiDataMergerTests.cs`
- `tests/MSFSBlindAssist.Tests/AptDatParserTests.cs`
- `tests/MSFSBlindAssist.Tests/TaxiGeoTests.cs`
- `tests/MSFSBlindAssist.Tests/GateSearchFilterTests.cs`
- `tests/MSFSBlindAssist.Tests/DockingGuidanceManagerFriendlyVdgsTests.cs`
