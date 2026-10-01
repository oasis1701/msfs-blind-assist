# Rules for any file — rules in full

Each section is the complete text of one rule. Its one-line form, under the same ID, is in CLAUDE.md.
The text is verbatim from CLAUDE.md as of `1f37801a`; a trailing "→ doc" pointer is the original's.

## CORE-1

- Always build the .sln or pass `-p:Platform=x64`; NEVER build the bare `.csproj` alone — it silently defaults to Platform=AnyCPU and writes to a different output folder (`bin\Debug\...`) than the x64 run path, so the running exe never updates. → CLAUDE.md

## CORE-2

- RID-subfolder gotcha: `-r win-x64` (or `dotnet publish -r win-x64`) writes to a SEPARATE `net10.0-windows\win-x64\` tree that a plain `.sln` build never touches — always build/verify the exact folder the app launches from. → CLAUDE.md

## CORE-3

- The exe is file-locked while MSFSBA runs (MSB3021) — close the app before building a fresh exe. → CLAUDE.md

## CORE-4

- `tools/CDUTest` and `tools/CDUTest`-style standalone probes build on their own, NOT as part of the solution. → CLAUDE.md

## CORE-5

- Sim-facing paths are verified only against a live sim — describe an in-sim test plan in the PR; pure logic belongs in `tests/MSFSBlindAssist.Tests` (CI-enforced). → CLAUDE.md

## CORE-6

- `main` is protected — never commit directly to main; always branch + PR. → CLAUDE.md

## CORE-7

- NEVER announce button presses, combo/dropdown value changes, or any direct UI interaction in panel controls — screen readers already announce them; ONLY announce numeric input confirmations, validation errors, and background (non-user-triggered) state changes. The ONE scoped exception is the TFDi MD-11's once-after-settle press confirmation and the EFB shell's `announceChange` opt-in (the paragraph under Screen Reader Announcements) — never a licence to announce presses elsewhere. → CLAUDE.md

## CORE-8

- Combo double-announce suppression must be GLOBAL, never aircraft-gated: `_uiSetEcho`/`MarkUiSet` plus a wrap that sets `announcer.Suppressed` around `ProcessSimVarUpdate` for any var inside the echo window — gating the wrap to one aircraft (the old HS787-only gate) causes double-announces on every other def that self-announces from inside `ProcessSimVarUpdate`. → CLAUDE.md

## CORE-9

- The echo-window suppression must match on TIME only, never on value — a combo set can write a different encoding than the SDK reads back, so a value-compare silently misses the duplicate. → CLAUDE.md

## CORE-10

- Never blanket-suppress value-0 resting-state button labels in MainForm — use the opt-in `SuppressRestingButtonState` flag only; some 0-state labels (PMDG 777 "LNAV: Off", HS787 "Baro STD: QNH") are meaningful and must be spoken. → CLAUDE.md

## CORE-11

- CRITICAL: set `IsConnected = true` BEFORE calling `SetupDataDefinitions()` in SimConnectManager — `StartContinuousMonitoring()` guards on `IsConnected == true`. → CLAUDE.md

## CORE-12

- CRITICAL: never use `TreeView` directly in forms — use `NativeAccessibleTreeView`; the .NET 9/10 UIA `TreeViewAccessibleObject` produces wrong NVDA navigation order. → CLAUDE.md

## CORE-13

- CRITICAL: never hardcode the FBWBA/MSFSBlindAssist database path — always go through `Database/DatabasePathResolver` (`ResolveExistingDatabasePath` for reads, `GetCanonicalDatabasePath` for writes). → CLAUDE.md

## CORE-14

- CRITICAL: every diagnostic log path must be resolved through `Utils/AppLogs.PathFor(...)` into `%APPDATA%\MSFSBlindAssist\logs` — never hand-build a log path. → CLAUDE.md

## CORE-15

- Never hand-build a log write (`File.AppendAllText`/raw path) — every diagnostic log goes through `Utils/Logging/Log` (`Log.Debug/Info/Warn/Error(category,msg)` → debug.log, or `Log.Channel(name)` → named file); `AppLogs.PathFor` is the PATH layer only. → CLAUDE.md
