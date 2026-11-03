# Plan 01 evidence: the groundwork

Measured on 2026-11-03 on Fenix build 3.0.0.7613 (installed files of 2026-10-29), MSFS 2024, an A321 with IAE engines
and no DCDU (`FenixVariant`) at KSFO. The app was the Release build of this branch at 688faa3c7, run from
`C:\MSFSBA-FenixChannel`. The checks needing the sim were run by Claude with the aircraft Robin handed over ("Loaded, it's
yours"), with the app's own calls: `S_OH_ELEC_BAT1` and `BAT2` written, `S_OH_ELEC_APU_MASTER` written,
`S_OH_ELEC_APU_START` pressed 1 then 0. The aircraft was put back cold and dark afterward.

## The fields this plan adds ([FNX-23])

Checked against the installed program with the team's local channel tools, which stay local: its contract read fresh from
`FenixDisplay.exe`, compared by script with the team's earlier dump (files installed 2026-10-21), and a live cold-and-dark
push of 5,783 bytes read field by field.

- No field number moved anywhere in the contract between the two builds. Three members were added that nothing here reads.
- Every number in Task 1 and Task 2 stands: `Remote.Efis` 1 and the class `Efis`, `HeadingData` 2 and 3,
  `Remote.EngineState` 129 and its three classes, `Remote.FuelState` 134 with `FuelState.Used` 13 and `FuelUsedData`,
  `Remote.IsKg` 154, `Remote.ApuData` 24 and the class `ApuData`, `DisplayContent.NdCapt` 2 and `DcduCapt` 9, `Remote.Isis`
  182 and `IsisState.Powered` 1.
- `ValveStateEnum` holds Open 0, Closed 1, Transit 2, FailedOpen 3, FailedClose 4, DataInvalid 5, read from the program's
  own constants. A cold-and-dark engine start valve read 5.
- Three corrections to the plan's text, made in `FenixChannelFields.cs`:
  - `SingleEngineState` field 31 is `EiuFault` on this build; the plan's `FadecFault` is the older build's name.
  - `EngineControlState.EngLimit` is repeated nested NullWrapped, as plan 04 reads it, not a plain repeated string. Live:
    each element a part whose field 1 is the string ("aXX" cold and dark), the empty one an empty part.
  - `Efis.EfisSelectors` arrives unpacked, one varint per entry. Protobuf reads both forms the same.
- Shapes seen live: EFIS twice (captain, first officer), `QnhInch` and `QnhMillibar` fixed32, `Qnh` absent at 1013.
  `ApuData` doubles fixed64, NaN until the APU runs (43.67, 115, 100 with it running), `Avail`, `FlapOpen`, `MasterPower`
  varints. Engine doubles fixed64, NaN cold and dark. `EngineString` a string. `HeadingData` 1 to 3 fixed64. `FuelState.Used`
  present and empty with nothing used.
- `Remote.IsKg` absent reads LBS: the FOB line read "wFOB:   g6620 cs LBS" with no field 154, as the plan says.
- `IsisState.Powered`: absent cold and dark, varint 1 from the moment the batteries are on (with field 2, initializing).

## Text before a color letter

White. In the program's code that draws the channel's text, all three of its text routines start a line in the color of
the letter `w` (the same palette entry), and a color letter changes it from there. So Task 4's change applies:
`FenixChannelSpeech` speaks a part's leading colorless run as white when the part holds a color letter, and a part with no
color letter stays colorless, so no color is ever guessed (spec 2.7).

Seen in passing, for plan 03: `d` only moves the drawing position right and draws nothing.

## The gate in the sim

From `fenix_channel.log`:

| Time | What was done | The log |
|---|---|---|
| 07:21:15 | The app starts on the loaded Fenix | "display data: connected", then 4 ms later "build 3.0.0.7613: checked". The variant is known in the same second |
| 07:21:16 | Cold and dark | Six lines: PFD, E/WD, SD, ND, ISIS, DCDU, each "not trusted: display off" |
| 07:22:15 | Batteries on | 07:22:16 "ISIS trusted"; nothing else moves |
| 07:24 | APU on line | DCDU 07:24:04, ND 07:24:08, E/WD 07:24:13, PFD 07:24:14, SD 07:24:15, each "trusted" |
| 07:25 | APU master off | 07:25:13 PFD, E/WD, SD, ND, DCDU "not trusted: display off"; the ISIS stays trusted on the batteries |
| 07:27:36 | Batteries off | 07:27:38 "ISIS not trusted: display off" |
| 07:27:44 | The app closes | "channel stopped", the log's last line |

**The DCDU reads trusted on this airframe, which has none.** The channel's DCDU unit is powered and shows DCDUCapt whatever
`FenixVariant` says, so the plan's "display off otherwise" does not hold: the gate cannot tell. Nothing reaches a pilot
from this: plan 11 checks `FenixVariant` first ("This Fenix has no DCDU."). Its claimant should keep that check first.

The build's answer: 2 to 6 ms after a connection's first message in the three sessions of 2026-11-02 (15:41:10.255 and
.259, 16:42:07.627 and .629, 18:02:00.636 and .642) and 4 ms here, with the query retried every 30 s, so a healthy load is
nowhere near the notice's 60 s.

## The stall guard

The app's UI thread was suspended from outside for 1,618 ms at 07:24:47.9, with every display trusted. The log wrote one
line, "UI thread stalled 1.7 s; comparisons waited", and no verdict changed, no disagreement started and nothing latched.
SimConnect's longest delivery gap on 2026-11-02 was 1.1 s, so 1,000 ms is near the normal top of that gap, and a passing
gap only makes a comparison wait.

## The settings seed

The app's first start wrote `FenixChannelMonitorDefaultsSeeded` and muted `FENIX_CHANNEL_MEMOS` in the pilot's settings:
4,935 to 5,057 bytes, every other key kept, `LastAircraft` kept.

## The session notice

No "session notice:" line in the log through the whole run above: cold and dark, powered, the stall and the close, on a
checked, answering channel.

With a throwaway build of ec6bcf0a5 whose `VerifiedBuilds` was emptied (never committed), same aircraft, cold and dark, at
08:27:22: "build 3.0.0.7613: not checked; every display uses the AI read", then 84 ms later one line, "session notice: Fenix
display channel not available for Fenix build 3.0.0.7613: displays use AI reading; FMA and E/WD announcements are off.",
then six "not trusted: build 3.0.0.7613 not checked". No second line through the 50 s run and the close. The log proves the
call was made once, not that it was heard. That a reconnect says nothing more rests on a unit test: nothing forces a
reconnect short of killing the Fenix.

## The checks by ear

Robin ran Task 12's last checks on 2026-11-03, with speech, and reports "All verified as expected!".

- With `C:\MSFSBA-FenixChannel` (ec6bcf0a5): Alt+P/N/E/S/I give the AI read; Shift+1 to Shift+6, Ctrl+W, D and Shift+D are
  silent; input Ctrl+Shift+D says the DCDU sentence; Ctrl+M shows the five rows after the cabin rows, "E/WD memos" unticked.
  The log of that run, 10:49:40 to 10:54:28: "build 3.0.0.7613: checked" 4 ms after the first message, six "display off",
  the ISIS trusted at 10:49:55, the other five from 10:50:24 to 10:50:33, no notice and no problem line.
- With a copy of the same build with `VerifiedBuilds` emptied (never committed, deleted when the plan closed): the notice
  heard once at load. The log of that run, 10:54:48 to 10:55:00: "not checked" at 10:54:48.583, one "session notice:" line
  74 ms later, six "not trusted: build 3.0.0.7613 not checked", then "channel stopped".

That a reconnect says nothing more is still the unit test's, `An_unchecked_build_is_announced_once` ("The session notice").
