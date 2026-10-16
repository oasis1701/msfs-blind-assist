# Fenix A320: the missing ACP reception switches (brief)

2026-10-13, written before the work, against main at 44a53741: the sections after "Outcome"
describe the code as it was then. Done in PR #305; what it settled is under "Outcome".

## Outcome (PR #305)

- All 15 reception latches are on the ACP panel, in the Fenix model's channel order, as are
  the volume and transmit groups. Each latch is an OnRequest combo read when the panel opens.
- Steps 2 and 3 below: each latch names its light as `UiEchoVariable`, so MainForm's UI-set
  echo gate also swallows the light's change after a panel pick, and the pilot hears the pick
  once (CORE-7). The light still speaks when anything else changes it.
- Each latch and its light are labelled from one constant ([VAR-1]): "ACP VHF 2 Receive" and
  "ACP VHF 2 Receive Light".
- The five new latches have no `HandleUIVariableSet` case: MainForm's generic L:var combo
  write covers a plain write (docs/architecture.md).
- Also fixed on the same panel: HF 2 Transmit reads its light `I_ASP_HF_2_SEND`, the only
  ACP1 transmit light that was not registered.

## What we're trying to achieve

A pilot flying the Fenix must be able to listen to any radio from MSFSBA. On the A320, a
radio is heard only when its reception knob on the audio control panel (ACP) is switched on
(the knob latches out) and its volume is up. MSFSBA's Fenix ACP panel had all 15 volume knobs
but only 10 of the 15 reception switches, so VHF 2, VHF 3, HF 2, INT and MLS could not be
heard unless the knob happened to be on already.

Done means: every Fenix ACP reception switch is on the ACP panel, shows its real state, and
switching it changes what the pilot hears in the sim.

## How it was found

2026-10-13 at ESGG, Fenix A320 CEO. The pilot tuned COM 2 to 118.380 (ESGG ATIS, the same
frequency SayIntentions shows), transferred it to active on RMP 2, and heard nothing.
debug.log shows the tune and the transfer went out (19:47:32 `COM2_STBY_RADIO_SET_HZ
118380000`, 19:47:34 `RMP2 Transfer` press and release). MSFSBA had no way to switch VHF 2
reception on.

Not confirmed: the actual value of `S_ASP_VHF_2_REC_LATCH` during that attempt (MSFSBA never
registered it, so it was never read). Confirming that is step 1 of the test plan.

## The facts

**The Fenix model** (`Community\fnx-aircraft-320\SimObjects\Airplanes\FNX_32X\model\FNX32X_Interior.xml`,
lines ~4102-4205): each ACP channel is one `FNX32X_Interact_Knob_PushButton_ACP_Template` with
three variables:

- `VAR_NAME`: the volume, `A_ASP_<ch>_VOLUME`
- `VAR_NAME_BUTTON`: the reception latch, `S_ASP_<ch>_REC_LATCH`
- `VAR_NAME_LIGHT`: the reception light, `I_ASP_<ch>_REC`

| Channel | Latch (`S_ASP_…_REC_LATCH`) | Light (`I_ASP_…_REC`) | In MSFSBA before #305 |
| --- | --- | --- | --- |
| VHF 1 | `VHF_1` | `VHF_1` | latch and light |
| **VHF 2** | `VHF_2` | `VHF_2` | **light only** |
| **VHF 3** | `VHF_3` | `VHF_3` | **light only** |
| HF 1 | `HF_1` | `HF_1` | latch and light |
| **HF 2** | `HF_2` | `HF_2` | **light only** |
| **INT** | `INT` | `INT` | **light only** |
| CAB | `CAB` | `CAB` | latch and light |
| VOR 1 | `VOR_1` | `NAV_1` | latch and light |
| VOR 2 | `VOR_2` | `NAV_2` | latch and light |
| MARKER | `MARKER` | `MARKER` | latch and light |
| ILS | `ILS` | `ILS` | latch and light |
| **MLS** | `MLS` | `MLS_1` | **neither** |
| ADF 1 | `ADF_1` | `ADF_1` | latch and light |
| ADF 2 | `ADF_2` | `ADF_2` | latch and light |
| PA | `PA` | `PA` | latch and light |

Note the irregular light names: VOR 1/2 light as `NAV_1`/`NAV_2`, MLS as `MLS_1`. Take every
name from the XML, never by pattern.

**MSFSBA before #305** (`MSFSBlindAssist/Aircraft/FenixA320Definition.cs`, line numbers from
44a53741):

- Volume knobs: `A_ASP_*_VOLUME`, ~2353-2480, all 15.
- Reception latches: `S_ASP_*_REC_LATCH`, ~2579-2659, 10 entries. Each is an OnRequest LVar
  combo with `ValueDescriptions = OffOn` and **no `StateVariable`**. The comment says
  "4 combo boxes", the panel list says 10: fix the comments too.
- Reception lights: `I_ASP_*_REC`, ~7660-7785, Continuous and announced, `OffOn`. Defined for
  every channel except MLS.
- ACP panel list: ~8996-9045 (the volume list, then the receive list at ~9031).
- Writes: `HandleUIVariableSet`, ~11300 (volumes) and ~11465 (latches). One `if` per var, each
  calling `simConnect.SetLVar(name, value)`.

## Suggested approach

1. Add the five missing latches (`VHF_2`, `VHF_3`, `HF_2`, `INT`, `MLS`), copying the VHF 1
   entry: definition, ACP panel-list entry in the Fenix's own channel order, and the
   `HandleUIVariableSet` write. Add `I_ASP_MLS_1_REC` for MLS's light.
2. Decide how the combo shows real state. The light `I_ASP_<ch>_REC` is the aircraft's own
   indication. Check how other Fenix combos read back (a `StateVariable` is used today for the
   `RenderAsButton` momentaries, `FenixA320Definition.cs` ~3711), and whether an OnRequest
   latch combo re-reads after a set. Do not invent a mechanism; follow what the Fenix
   definition already does for a latch with a separate light.
3. Avoid double speech (CORE-7): the light is announced on change. If the combo also reads
   back after the set, the pilot hears the same thing twice. Pick one.
4. Optional, ask the owner first: the 15 `if` blocks per group could become one lookup over
   the ACP vars. Not needed for the fix, and the house rule is to match surrounding code.

Before changing behaviour, read `.claude/rules/a32nx-fenix.md` (it loads when you Read the
Fenix definition with the Read tool, CORE-16) and `docs/a32nx.md`. For how the system behaves,
compare only with the FBW A32NX (CLAUDE.md "Before changing behaviour"). If the A32NX has the
same gap, name it in the PR as "also seen on" and leave it for its own PR.

## Test plan (in sim, Fenix, on the ground, avionics powered)

1. Before changing anything: read `S_ASP_VHF_2_REC_LATCH` and `I_ASP_VHF_2_REC` live (the
   playbook in `docs/troubleshooting-playbook.md` and `docs/tooling.md` show how). Note which
   value means "on". Do not assume 1 = on: it is a latch, and the knob is out when receiving.
2. Build, run, open the ACP panel. All 15 reception switches are listed, each with its state.
3. Tune COM 2 to the local ATIS (ESGG 118.380), switch VHF 2 reception on and the VHF 2 volume
   up. The ATIS is heard (with SayIntentions or the sim's own ATIS).
4. Switch VHF 2 reception off: the ATIS stops. Hear the change spoken once, not twice.
5. Spot-check VHF 3, HF 2, INT and MLS: each switch moves its knob in the cockpit and its light
   follows (DBG-4/DBG-5: read back the effect, not just the variable).

## Done

- Five latches and the MLS light added; comments and counts corrected.
- Built with the solution (CORE-1), pure-logic tests unaffected, in-sim plan run.
- PR with the in-sim test plan and `changelog.d/<pr>-fenix-acp-reception.fix.md`, written for
  a pilot: "On the Fenix A320, the audio control panel now has reception switches for VHF 2,
  VHF 3, HF 2, INT and MLS, so you can listen to the second radio." The PR number is READ from
  `gh pr create`, never guessed, and no PR is opened without the owner's go-ahead.
