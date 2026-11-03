# Plan 11 evidence: the DCDU window

Fenix build 3.0.0.7613 (`fenix_channel.log`, "build 3.0.0.7613: checked", 2026-11-03), MSFS 2024. Raw fields were read with
a read-only stream reader built from the team's local channel tools.

## Task 1 step 1: the empty DCDU (partly done)

Read on 2026-11-03 at 18:40 on the Fenix then loaded, which has no DCDU fitted (`B_CONFIG_DCDU` 0). The step asks for a
Fenix with the DCDU option on, so it is repeated on one; the result below is what the channel sends with none fitted.

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
