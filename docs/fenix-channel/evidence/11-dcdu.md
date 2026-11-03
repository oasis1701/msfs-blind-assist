# Plan 11 evidence: the DCDU window

Fenix build 3.0.0.7613 (`fenix_channel.log`, "build 3.0.0.7613: checked", 2026-11-03), MSFS 2024. Raw fields were read with
a read-only stream reader built from the team's local channel tools.

## Task 1 step 1: the empty DCDU (partly done)

Read on 2026-11-03 at 18:40 on the Fenix then loaded, and again at 20:45 on an A321 IAE the app reads as having no DCDU
(`debug.log`, `HasDcdu = False`), powered, the DCDU trusted: the same at both. The 18:40 airframe is not known. The simconnect MCP
read `B_CONFIG_DCDU` 0 then, but at 20:45 it read `N_CONFIG_AIRCRAFT_TYPE` 0 on this A321 too, so its readings of the Fenix's config
vars prove nothing: take the variant from the app's log. Repeated at 20:53 with the DCDU option on for the session (`HasDcdu = True` in
`debug.log` at 20:52:46, the A321 IAE powered, `fenix_channel.log` gives the DCDU trusted): the same again, so the step is done.

- Field 186 is nested. Field 1 is a varint, 1 (PrintFrame true). Fields 2-15 are all present as strings: 2-9 and 12-15
  empty, 10 and 11 (Info2, Info3) 14 spaces each. None is absent, and nothing else is in the message. This is the
  2026-11-01 capture's result.
- Field 187 (the first officer's) reads the same.
- It did not change over 6 s (100 messages).

## Task 2 step 6: the field numbers

186 (`dcduCapt`) and its sub-fields 1-15 are as the team's contract dump gives them, and plan 01's check found no field number
moved between that dump and 3.0.0.7613. The read above agrees on the installed build: every number 2-15 came as a string, 1 as a
varint, and Info2 and Info3 came on 10 and 11. The other lines have only been seen empty, so Task 1 step 3's real message is what
tells them apart.

## Task 1 step 2: which way the page and message keys go

Read on 2026-11-03 from the installed package,
`fnx-aircraft-a32x-common\SimObjects\Airplanes\FNX_32X\attachments\fnx\Part_Interior_Cockpit\model\Cockpit_Behavior.xml`,
lines 1485-1535. The file holds no tooltip text at all, so the answer is the name of the model part each variable moves:

| Variable | Model part | The app's label |
|---|---|---|
| `S_DCDU1_MSGUP` | `MainPanel_CPDLC_Message_Plus_Captain_Button` | Message Plus |
| `S_DCDU1_MSGDWN` | `MainPanel_CPDLC_Message_Minus_Captain_Button` | Message Minus |
| `S_DCDU1_PGUP` | `MainPanel_CPDLC_Page_Minus_Captain_Button` | Page Minus |
| `S_DCDU1_PGDN` | `MainPanel_CPDLC_Page_Plus_Captain_Button` | Page Plus |
| `S_DCDU1_LSK1L`, `LSK2L`, `LSK1R`, `LSK2R` | `MainPanel_CPDLC_LSK_1_Left`, `LSK_2_Left`, `LSK_1_Right`, `LSK_2_Right` | LSK 1 Left, and so on |
| `S_DCDU1_PRINT` | `MainPanel_CPDLC_Print_Captain_Button` | Print |

So MSGUP really is plus and PGUP really is minus: today's labels are right, no pair is swapped, Task 4's table stands and
Task 7 relabels nothing. Which way a plus key moves through real messages is still Task 1 step 3's to measure.

## Task 8: the window at the gate (2026-11-03, the plan's local build at 94335f387, by ear)

- **No DCDU fitted** (A321 IAE, `HasDcdu = False`): input Ctrl+Shift+D said "This Fenix has no DCDU." and opened nothing; input mode
  stayed on (Ctrl+Shift+F then opened the Fenix windows list with no second `[`); the "DCDU" entry said the same. Passed (Robin).
- **Opening, with a DCDU** (the same A321, the option on for the session): the key and the list entry each opened "DCDU display" on
  "(no CPDLC message displayed)", a second press came forward, and Escape closed it. Passed (Robin).
- **Soft keys on the empty DCDU, alternate keys on** (Robin's setting): F1, F2, F7 and F8 each said "No action on that key.", Ctrl+1,
  Ctrl+2, Alt+1 and Alt+2 were silent, and `debug.log` holds no `ExecuteButtonTransition` or `S_DCDU` line since 20:52. Passed (Robin).
- **Page, message and print keys:** PageDown `S_DCDU1_PGDN`, PageUp `S_DCDU1_PGUP`, Ctrl+PageDown `S_DCDU1_MSGUP`, Ctrl+PageUp
  `S_DCDU1_MSGDWN`, Ctrl+P `S_DCDU1_PRINT`: one `ExecuteButtonTransition` START and one COMPLETE each in `debug.log` (20:55:51 to
  20:56:06), each 0, then 1 after about 200 ms, then 0 after about 200 ms. Nothing spoken (Robin). Passed.
- **A held key:** Ctrl+PageDown held about 2 s: one `ExecuteButtonTransition` START in `debug.log` (20:56:52), nothing spoken. Passed.
- **The brightness combo:** with focus on it, PageDown pressed `S_DCDU1_PGDN` once (20:57:46) and the combo did not move. Down Arrow:
  `PulseSpringSwitch: S_DCDU1_DIM_BRT = 2`, then `= 1 (return to middle)` 212 ms later (20:57:48). Up Arrow: `= 0`, then `= 1 (return
  to middle)` 207 ms later (20:58:06). NVDA does not say the return to Mid, and Robin does not want it to. Passed.
- **The panel and the row:** Pedestal lists no "DCDU Left" and keeps the rest, "Cockpit Other" keeps "First Officer DCDU Brightness",
  Ctrl+M lists "DCDU messages", and nothing was announced on the empty DCDU. Passed (Robin).

The rest of Task 8 only exercises the app's own logic, so it is marked from the code, the tests and the logs, not run by hand (Robin's
word, 2026-11-03: only what needs the sim):

- **Soft keys with the alternate keys off:** `FenixChannelDcduForm.HandleWindowKey` passes the live setting to `Map`;
  `Each_chord_maps_to_its_key` pins both modes, and the refusal is the path heard above.
- **Cold and dark:** the channel's verdict was measured live, `fenix_channel.log` 20:32:03 "DCDU not trusted: display off" (and plan 01's
  evidence); `FenixChannelDisplayForm.RowsFor` makes it the status row, and `A_soft_key_on_an_untrusted_dcdu_sends_nothing` pins "DCDU not
  available".
- **The DCDU option off for the session:** the same `Variant.HasDcdu == false` path as the airframe with none, heard above.

Task 8 is done. From Task 9, the mute (no call while unticked, no replay, the next one called) is marked the same way:
`A_muted_message_is_not_replayed_on_unmute` and `The_sampler_reads_one_snapshot_checks_its_row_and_queues_the_call`.
