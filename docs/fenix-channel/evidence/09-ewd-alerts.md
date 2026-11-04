# Plan 09 evidence: E/WD alert announcements

Fenix build 3.0.0.7613, MSFS 2024. Worked by Robin Kipp with Claude. Robin flew; Claude pressed only what Robin agreed to,
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
to one push, about 60 ms. (The ground run found why: the list is sampled every 3.84 s, below.)

| Change | Line changed | Entries changed | Entry behind the line |
| --- | --- | --- | --- |
| Seat belts on | 15:27:49.521 | 15:27:53.122 | 3.60 s |
| Seat belts off | 15:28:13.046 | 15:28:16.184 | 3.14 s |
| Seat belts on | 15:29:10.175 | 15:29:13.776 | 3.60 s |
| No smoking on | 15:29:30.337 | 15:29:32.978 | 2.64 s |
| No smoking back to Auto | 15:29:58.666 | 15:29:59.876 | 1.21 s |
| Seat belts off | 15:30:14.024 | 15:30:15.225 | 1.20 s |

The entries' messages did not fall on the heartbeat's beat. A real failure lags the same way (below).

### What a memo looks like

- Seat belts: `Ata` "01- 0-250", `Name` "MEMO-SEAT BELTS", `Title` blank, `IsAlert` absent; line "gSEAT BELTS".
- No smoking: `Ata` "01- 0-260", `Name` "MEMO-NO SMOKING", `Title` blank, `IsAlert` absent; line "gNO SMOKING".
- With both on: two entries in that order, and left column "gSEAT BELTS", "gNO SMOKING".
- **Both memos' lines were in the left column (173), with the right column empty.** The plan looks in the right column first
  for a memo, then the left, so it still finds them. They do not move right when a failure holds the left: they are not drawn.
- A blank `Title` is what rule 2 expects: the text is the name after "MEMO-", and it equals the line's joined text.

### A line with no entry: RCL

S_ECAM_RCL pressed with nothing cleared to recall, and released about 5 s later (two separate calls, so the press was held far
longer than the app's). The left column became blank, blank, "           gNORMAL" at 15:30:29.386 and empty again at
15:30:34.188, 4.80 s later: that may be the Fenix's own timer or the end of the long press, which this run cannot tell apart.
No entries message came. So:

- The column can hold blank elements above a line, and spaces before the color letter.
- The announcer, which works from the entries alone, says nothing for it. The plan asks nothing of this line; it is only a
  note.

### The 2026-11-02 fire drill's notes

- `*AIR BLEED` as an entry: **not measured**. The notes' one entries list (18:43:3x) is from before that line appeared (after
  the agent at about 18:44:55).
- That list does hold two names the plan's rules did not expect (the notes give no `Title` for them):
  - "MEMO-LAND ASAP (RED)": under rule 2 this is a memo, so it is muted by default, and if its `Title` is blank its text is
    "LAND ASAP (RED)", which matches no line ("rLAND ASAP"), so it would be spoken with those words and no color.
  - "MEMO - COMPANY MSG": spaces around the dash, so it does not start "MEMO-", and with no `IsAlert` rule 2 logs it as an
    entry of no known kind and never speaks it.

### Also seen

During this flight's refuel at the gate the E/WD went "not trusted: fuel disagrees" for about a second, about every 25 s (the
log's last five, 13:28:42 to 13:30:23, about 100 kg apart), and trusted again each time, without a latch. That build still
compared the displays with SimConnect; fe58e5df3 removed the comparison (spec row 58), so this no longer happens.

## Task 1 on the ground: A321 IAE at LPPT, 2026-11-04

At the gate, stopped, both engines running, parking brake set, flaps 0, FWC phase 2, Robin's aircraft handed over. Writes went
through the simconnect MCP (the definition's L:vars, press and release for a button), failures through the failures window's
own path. The descent and landing are from the same logger, Robin flying. MSFSBA was closed for the ground run.

### The list is sampled on a beat of 3.84 s

Every list message of the day falls on one beat: 90 of the 93 gaps between them are within 100 ms of a whole number of 3.84 s,
and the other three are a new stream's first message, sent at once, and the aircraft reload at 18:18. The list is sent only if it
differs from the last sample, and it is the state at that moment: a line that changed 62 ms before a beat had already changed in
that beat's list (GEN 1 FAULT clearing, 18:24:42.976 and 18:24:43.038).

So every delay below is simply the wait for the next beat, and it runs from 0 to 3.84 s:

| Change | Line changed | Entries changed | Entry behind the line |
| --- | --- | --- | --- |
| AP OFF, red, appears | 17:41:54.427 | 17:41:54.670 | 0.24 s |
| PARK BRK, released | 17:44:11.734 | 17:44:12.934 | 1.20 s |
| PARK BRK, set | 18:18:50.128 | 18:18:53.549 | 3.42 s |
| GEN 1 OFF appears | 18:21:07.428 | 18:21:07.967 | 0.54 s |
| GEN 1 OFF clears | 18:21:34.313 | 18:21:34.854 | 0.54 s |
| T.O SPEEDS appears | 18:22:04.555 | 18:22:05.577 | 1.02 s |
| SLATS and FLAPS CONFIG appear | 18:22:39.121 | 18:22:40.140 | 1.02 s |
| SLATS and FLAPS CONFIG clear | 18:22:52.562 | 18:22:55.502 | 2.94 s |
| GEN 1 FAULT appears | 18:24:16.573 | 18:24:19.993 | 3.42 s |
| GEN 1 FAULT clears | 18:24:42.976 | 18:24:43.038 | 0.06 s |
| ENG 1 FIRE appears | 18:25:06.499 | 18:25:09.919 | 3.42 s |
| ENG 1 FIRE clears | 18:27:41.079 | 18:27:43.540 | 2.46 s |

**No entry ever came before its line.** The largest entry-to-line gap is therefore none at all, which gives `LineWaitMs` its floor
of 500 ms under Task 1's rule. An entry whose line never comes is a line the E/WD is not showing (below), not a late one.

What the beat costs:

- A warning is spoken up to 3.84 s after the E/WD draws it. ENG 1 FIRE was 3.42 s behind here; the master warning does not wait.
- **A warning shorter than the beat can go unsaid.** One press of T.O CONFIG (two writes, one straight after the other) drew the
  CONFIG warnings from 18:22:03.597 to 18:22:05.035, 1.44 s between two beats, and they never reached the list. Holding the button
  12 s did list them. The panel's TO CONFIG verdict ([FNX-4]) does not use this list, so it is not affected.

### A failure leaves the list during a power transfer, with the fire still burning

A generator change blanks both E/WD columns for 2.16 s (every one of the eight timed the same, for example 18:21:01.311 to
18:21:03.467 and 18:24:09.853 to 18:24:12.012), and the FWC page drops back to the cruise page inside it. If a beat falls in that
blank, **ENG 1 FIRE is missing from that beat's list and is back on the next one**, 3.84 s later, with the
fire set throughout. Seen twice, in the only two runs with a beat in a blank while the fire was showing:

| Power change | E/WD blank from | List without the fire | Line back | List with the fire |
| --- | --- | --- | --- | --- |
| Generator 1 failed | 18:25:58.886 | 18:25:59.846 | 18:26:01.046 | 18:26:03.687 |
| Generator 1 failure cleared | 18:27:15.877 | 18:27:16.657 | 18:27:18.036 | 18:27:20.498 |

The other failures stayed in those lists (T.O SPEEDS both times, GEN 1 FAULT in the second). The display units' power and content
(field 12) did not change through a blank (GEN 1 off, 18:32:59.481 to 18:33:01.641), so `EwdLive` stays true and the E/WD stays
trusted: Task 3's rules as written would say "ENG 1 FIRE cleared", then "ENG 1 FIRE, red" again and interrupting.

### The warnings and cautions

| Entry | `Name` | `Title` | The E/WD's line |
| --- | --- | --- | --- |
| "01-24-  5" | ELEC-GEN 1 OFF | ELEC GEN 1 OFF | "uaELECu aGEN 1 OFF" |
| "01-24-  1" | ELEC-GEN 1 FAULT | ELEC GEN 1 FAULT | "uaELECu aGEN 1 FAULT", then the action lines |
| "01-26- 10" | FIRE - ENG 1 FIRE | ENG 1 FIRE | "urENG 1 FIREu ", then the action lines |
| "01-27-180" | FLT CTL-SLATS CONFIG | SLATS NOT IN T.O CONFIG | "urCONFIGu " over "rSLATS NOT IN T.O CONFIG" |
| "01-27-185" | FLT CTL-FLAPS CONFIG | FLAPS NOT IN T.O CONFIG | "urCONFIGu " over "rFLAPS NOT IN T.O CONFIG" |
| "01-22-720" | T.O - SPEEDS NOT INSERTED | T.O SPEEDS NOT INSERTED | "uaT.Ou aSPEEDS NOT INSERTED" |
| "01-22- 15" | AUTO FLIGHT - AP OFF UNVOLUNTARY | AUTO FLT AP OFF | "urAUTO FLTu rAP OFF" |

All with `IsAlert` set. So:

- **Switching GEN 1 off is "ELEC GEN 1 OFF"**; "ELEC GEN 1 FAULT" is the failure (F_ELEC_DRIVE_FAILURE_L).
- **The CONFIG warnings are two entries, and neither `Title` holds "CONFIG".** Each finds the red line under its heading on its own,
  so "SLATS NOT IN T.O CONFIG, red" then "FLAPS NOT IN T.O CONFIG, red"; no title measured so far needs the two-line match.
- The status items came too: "01-24-190" ELEC-STS-GEN 1 INOP and "01-24-370" ELEC-STS-MAIN GALLEY INOP, 3.3 to 4.3 s ahead of the
  generator's line, as GEN 1 FAULT takes about 6.7 s from the failure to reach the E/WD. "-STS-" entries are never an item, so this
  says nothing.
- **A failure the E/WD is not showing has no line.** With the fire showing, GEN 1 FAULT and T.O SPEEDS were in the list and the
  right column had only "aELEC" and "aT.O". So a failure arriving under a fire is spoken without a color, as spec 2.7 has it.
- **Entries with `IsAlert` and no `Title`, and with neither, are real:** AP OFF MW, CAPABILITY CHANGE, ALTITUDE ALERT and ATC AUDIO
  ALERT (RING) have `IsAlert` and a blank `Title`; HUNDRED ABOVE and RETARD ANNOUNCE have neither. They look like the sounds' own
  entries, and rule 2 reports them and never speaks them.
- CLR does not touch the list. Two CLR presses took GEN 1 FAULT and T.O SPEEDS off the E/WD (18:30:45.906, 18:31:05.586) with no
  entries message; GEN 1 FAULT left at 18:31:30.129, after the failure was cleared. So "cleared" means the fault has gone, not that
  the pilot has dismissed it, and an item's line can go while it stays (rule 8 keeps its last color).
- LAND ASAP does not show on the ground (the fire's right column was "aT.O"), so its `Title` is still not measured, nor is
  `*AIR BLEED` as an entry: both need a flight.

### The memos

All had a blank `Title` except the T.O and LDG memos. The lines were in the left column with no failure showing only for
GND SPLRS ARMED, SEAT BELTS, NO SMOKING and FOB BELOW 3 T; the others are in the right column. With a failure in the left column,
those four are not drawn at all, though they stay in the list (17:41:54, 18:21:07).

Found by the name after "MEMO-": SEAT BELTS, NO SMOKING, GND SPLRS ARMED, IGNITION, LDG LT, PARK BRK, PRED W/S OFF (green, amber
from the T.O CONFIG press on), HYD PTU, APU AVAIL, APU BLEED, GPWS FLAP 3, LDG INHIBIT (magenta), and the T.O memo's `Title`
"T.O AUTO BRK.....MAX" ("guT.Ou AUTO BRKc.....MAX    ").

**Not found by it, as the entry's words are not the E/WD's:**

| `Name` | `Title` | The E/WD draws |
| --- | --- | --- |
| MEMO - COMPANY MSG (spaces round the dash) | blank | "gCOMPANY MSG" |
| MEMO-FUEL < 3 T | blank | "gFOB BELOW 3 T" |
| MEMO-T.CAS STBY | blank | "gTCAS STBY" |
| MEMO-T.O. INHIBIT | blank | "mT.O INHIBIT" |
| MEMO-SPEED BRAKE | blank | "gSPEED BRK", "paSPEED BRK" |
| MEMO-AUTO BRK | blank | "gAUTO BRK LO" |
| MEMO-LDG MEMO | "LDG LDG GEAR......DN" | "guLDGu LDG GEAR DN" (the `Title` does not follow the line) |

## Task 5: heard at the LPPT gate, 2026-11-04

A321 IAE, stopped, both engines running, the Release build of 27ab63152 from 21:12. Claude pressed and set the failures, Robin
listened. Fuel had run down to 448 kg with the engines idling, so both wing tanks were set to 900 gallons first (21:11, held on
read-back).

| Check | Heard | List change | Result |
| --- | --- | --- | --- |
| App started with six items on the list | nothing | | pass |
| GEN 1 off | "ELEC GEN 1 OFF, amber" once; GEN 1 INOP and MAIN GALLEY INOP never | 21:14:13.850 | pass |
| GEN 1 on | "ELEC GEN 1 OFF cleared" once | 21:15:07.619 | pass |
| F_ELEC_DRIVE_FAILURE_L set | "ELEC GEN 1 FAULT, amber" once | 21:16:24.432 | pass |
| The failure cleared | "ELEC GEN 1 FAULT cleared" once | 21:17:10.520 | pass |
| TO CONFIG held 15 s, flaps up | "SLATS NOT IN T.O CONFIG, red", then "FLAPS NOT IN T.O CONFIG, red" in full | 21:18:08.128 | pass |
| TO CONFIG released | both "cleared"; SPEEDS NOT INSERTED, redrawn but listed all along, not spoken | 21:18:23.492 | pass |
| F_OH_FIRE_ENG_1_1BOTTLE | "ENG 1 FIRE, red" once | 21:19:40.304 | pass |
| The generator failure with a beat in the blank | the fire out of the list 21:19:51.826, back 21:19:55.666: no "cleared", no repeat; "ELEC GEN 1 FAULT" with no color, hidden behind the fire | 21:19:59.508 | pass |
| Both failures cleared | "ELEC GEN 1 FAULT cleared", "ENG 1 FIRE cleared" | 21:20:37.914, 21:20:49.436 | pass |
| "E/WD cautions" unticked, GEN 1 off | nothing | 21:24:59.076 | pass |
| Ticked again | nothing replayed | | pass |
| GEN 1 on | "ELEC GEN 1 OFF cleared" once | 21:27:21.180 | pass |
| Memos muted, GPWS FLAP 3 on | nothing | 21:29:58.646 | pass |
| "E/WD memos" ticked, off, then on | nothing, then "GPWS FLAP 3, green" once | 21:31:00.097, 21:31:23.140 | pass |

- **Other speech.** Robin noticed by accident during these runs that an amber line does not cut NVDA's speech and that a red warning
  does, and asked for this to be marked as passed. It was not staged.
- **The delay.** Two fires timed to the beat: spoken 0.06 s after the E/WD line (21:21:31.622 to 21:21:31.683), then 3.42 s after
  it (21:22:06.668 to 21:22:10.089). Robin: a delay like the second is acceptable, so reading from the lines stays out.
- **Seat belts could not stand for the memos.** The T.O memo had replaced SEAT BELTS and NO SMOKING on the list since the T.O CONFIG
  press, so GPWS FLAP 3 stood in. The seat belt entry and line were measured under Task 1.

### A blackout

Both batteries and both generators off with the engines running, twice, then the batteries and GEN 2 on with GEN 1 left off. This
is the check that expected silence, and it was not silent. Robin heard it and ruled that it stays (spec row 62).

| | First | Second |
| --- | --- | --- |
| E/WD "display off" | 21:32:47.227 | 21:38:42.222 |
| E/WD trusted | 21:33:40.219 | 21:40:10.230 |
| First new list | 21:34:04.447 | 21:40:28.512 |
| FWS FWC 2 FAULT leaves, after that list | 3.84 s | 7.68 s |
| COM CIDS 1+2 FAULT leaves, after that list | 65 s | 73 s |
| ELEC GEN 1 OFF joins, after that list | 92 s | 92 s |

- The list does not change while the power is off: only heartbeats. A new connection made in the dark is sent the last list, so the
  server remembers it.
- From the E/WD trusted to the first new list its columns are blank, then "FWS FWC 1+2 FAULT" and the NOT AVAIL column, none of it
  on the list.
- So the baseline is the list from before the blackout. The first run was heard, with memos ticked: "COM CIDS 1+2 FAULT, amber", "FWS
  FWC 2 FAULT, amber", "IRS IN ALIGN 1", "IRS IN ALIGN 2", "T.O SPEEDS NOT INSERTED cleared", "FWS FWC 2 FAULT cleared", "PRED W/S OFF,
  green".
- Not measured: a fresh load from cold and dark with the app already running. The same path should speak its start-up failures too;
  the next flight from cold and dark will hear it.

### `fenix_channel.log`

No "no line on the E/WD for" line for a title the E/WD showed. All of them from this run:

- ELEC GEN 1 FAULT (21:20:00): hidden behind the fire's procedure.
- IRS IN ALIGN 1 and 2 (21:34:05): with a failure in the left column the memo is not drawn. Drawn, it reads "aIR IN ALIGN > 7 MN", one
  amber line with a countdown for the two entries, so these two would find no line either way and are said in the entry's words.
- T.O AUTO BRK.....MAX (21:36:34): the T.O memo with a failure in the left column, so spoken with its dots. Memos are muted by default.

"Entry of no known kind": FWS-STS FWC 2 FAULT, 01-31- 13 (a status item spelled without the second dash; silent, as a status item
should be). "Alert entry with no title": AUTO FLIGHT-CAPABILITY CHANGE, 01-22-200 (on the first list after a power-up, gone a beat or
so later, never drawn).

## Still to measure in flight

LAND ASAP's entry and `Title`, whether its `Name` changes when the line turns from red to amber, and `*AIR BLEED` as an entry.
