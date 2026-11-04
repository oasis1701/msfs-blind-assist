# Plan 09 evidence: E/WD alert announcements

Fenix build 3.0.0.7613, MSFS 2024. Worked by Robin Kipp with Claude. Robin flew; Claude pressed only what he agreed to,
through the simconnect MCP with the definition's write paths. Both streams are logged with their arrival times in one
process by a local read-only logger (never committed), which opens only the two calls the app reads.

## Task 1 in cruise: A321 IAE, 2026-11-04

FL351, 427 knots over the ground, FWC phase 6, lower ECAM page 5, E/WD trusted the whole time, nothing on the E/WD at the
start. GEN 1, TO CONFIG and the parking brake were left for the ground (Robin: no generator switching in flight).

### Heartbeats and the list

- A quiet minute and more (15:26:01 to 15:27:46, with no switch touched): one heartbeat every 5.00 s, 2 bytes, Heartbeat set,
  no entries. None carried a list here, with the list empty or holding memos.
- A list is sent when it changes, with every entry in it, Heartbeat not set. **An empty list is a message of 0 bytes**, not a
  heartbeat: seen when the last memo went (15:28:16.184, 15:30:15.225).
- A new stream's first message is the whole list at once: 0 bytes with nothing showing (15:26:01.753), both entries with two
  memos showing (15:29:38.548, 2 ms after the stream opened). So a baseline never waits for a change.
- Plan 01's code reads this as intended: `FenixChannelClient` delivers a 0-byte message, `DecodeEcamEntries` reads it as no
  entries with Heartbeat false, and `IsBareHeartbeat` keeps it as the list, so a list that empties does clear the items. Read
  from the code, not yet run through it: Task 3's tests cover it.
- The data stream ran at 1,000 pushes a minute throughout.

### Memos: the line comes first, by 1.2 to 3.6 s

Every change reached the E/WD's line before the entries, the opposite of what Task 3's wait is for. The line's time is good
to one push, about 60 ms.

| Change | Line changed | Entries changed | Entry behind the line |
| --- | --- | --- | --- |
| Seat belts on | 15:27:49.521 | 15:27:53.122 | 3.60 s |
| Seat belts off | 15:28:13.046 | 15:28:16.184 | 3.14 s |
| Seat belts on | 15:29:10.175 | 15:29:13.776 | 3.60 s |
| No smoking on | 15:29:30.337 | 15:29:32.978 | 2.64 s |
| No smoking back to Auto | 15:29:58.666 | 15:29:59.876 | 1.21 s |
| Seat belts off | 15:30:14.024 | 15:30:15.225 | 1.20 s |

The entries' messages did not fall on the heartbeat's beat. Whether a real failure lags the same way is for the ground; if it
does, a warning is spoken up to about 3.6 s after the E/WD draws it.

### What a memo looks like

- Seat belts: `Ata` "01- 0-250", `Name` "MEMO-SEAT BELTS", `Title` blank, `IsAlert` absent; line "gSEAT BELTS".
- No smoking: `Ata` "01- 0-260", `Name` "MEMO-NO SMOKING", `Title` blank, `IsAlert` absent; line "gNO SMOKING".
- With both on: two entries in that order, and left column "gSEAT BELTS", "gNO SMOKING".
- **Both memos' lines were in the left column (173), with the right column empty.** The plan looks in the right column first
  for a memo, then the left, so it still finds them. Whether a memo moves right when a failure holds the left is for the ground.
- A blank `Title` is what rule 2 expects: the text is the name after "MEMO-", and it equals the line's joined text.

### A line with no entry: RCL

S_ECAM_RCL pressed and released at about 15:30:28, nothing cleared to recall. The left column became blank, blank,
"           gNORMAL" at 15:30:29.386 and empty again at 15:30:34.188, 4.80 s later. No entries message came. So:

- The column can hold blank elements above a line, and spaces before the color letter.
- The announcer, which works from the entries alone, says nothing for it. The press has its own read-back path, so this is
  only a note.

### The 2026-11-02 fire drill's notes

- `*AIR BLEED` as an entry: **not measured**. The notes' one entries list (18:43:3x) is from before that line appeared (after
  the agent at about 18:44:55).
- That list does hold two names the plan's rules did not expect (the notes give no `Title` for them):
  - "MEMO-LAND ASAP (RED)": under rule 2 this is a memo, so it is muted by default, and if its `Title` is blank its text is
    "LAND ASAP (RED)", which matches no line ("rLAND ASAP"), so it would be spoken with those words and no color.
  - "MEMO - COMPANY MSG": spaces around the dash, so it does not start "MEMO-", and with no `IsAlert` rule 2 logs it as an
    entry of no known kind and never speaks it.

### Also seen

During the morning's refuel the E/WD went "not trusted: fuel disagrees" for about a second, about every 25 s (13:28:42 to
13:30:23, about 100 kg apart), and trusted again each time, without a latch. Each return is a silent baseline under rule 3, so
an alert that appears inside one of those seconds would go unsaid.

## Still to measure on the ground

GEN 1 off and on; TO CONFIG with the flaps up; the parking brake set and released; a memo beside a failure; `LineWaitMs` from
the failures' gaps; and, if the owner agrees to a fire drill, the `Title` of LAND ASAP's entry and `*AIR BLEED`.
