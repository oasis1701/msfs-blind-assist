# FlyByWire ARINC 429 words — rules in full

Each section is the complete text of one rule. Its one-line form, under the same ID, is in `.claude/rules/fbw-arinc.md`, which Claude Code loads when it reads matching code. Background: [a380x.md](../a380x.md).
The text is verbatim from CLAUDE.md as of `1f37801a`; a trailing "→ doc" pointer is the original's. Cross-references such as "the bullet below", "above" or "under Core" point at CLAUDE.md's old single list, whose rules now live in several files: search `docs/invariants/` for the rule's key name to find it.

## ARINC-1

- **An FBW ARINC DISCRETE word carries its bitfield as the float's VALUE — read a bit from `Arinc429Word.DiscreteBits` (`BitValueOr` does), NEVER from the raw low 32 bits.** FBW builds the bitfield as an integer, converts it to a float NUMERICALLY and packs the float's IEEE bits; all three of its readers convert back (C++ `static_cast<uint32_t>(word.Data)`, TS `this.value >> (bit - 1)`, Rust `f32::from_bits(v) as u32`). Until 2026-09-25 `BitValueOr` tested the raw bits — the float's exponent and mantissa — so bit 28 read true for any word with a bit at 18 or above, bit 29 never read true, and bits 11-23 read true only by accident: FMA Reversion fired on selected/managed speed changes, Speed Protection and the A380 cruise-altitude qualifier never fired, every ROW/ROP and RWY AHEAD call-out was dead, and every discrete row on both FBW jets read noise. `coherent-oans-agent.js` carried the same raw test in JavaScript (`A.rwyAheadActive` — decode through `A.arinc` first). `BitValue` (NO SSM gate, FBW's own `bitValue`) is ONLY for a word whose writer never sets its SSM, so its own readers ignore it — the A32NX FWC word 124 (PseudoFWC builds it with `createEmpty()`, it stays Failure Warning for good, and the PFD reads CHECK ALT with `bitValue`), whose call-out a gated read had silenced from the day it was written; everywhere else a failed word must say nothing. A test that builds a discrete word must pack it the FBW way — `BitConverter.SingleToUInt32Bits((float)bitfield)` — never `(ulong)ssm << 32 | bitfield`, which is how the old tests agreed with the old bug. Two "live captures" recorded as puzzles were this bug (FG word 5 `0x48000000` = bit 18, not "reversion at rest"; FG word 3 `0x4D804000` = bits 29 and 20, not "constraint with nothing armed"); an older raw capture in any doc must be re-decoded before it is believed. Pinned by `Arinc429WordTests`. → [a380x.md](../a380x.md)

## ARINC-2

- Any ANNOUNCED enum var whose raw value is ARINC-large (≥2^32) must be decoded via `Arinc429Word` before comparing to its `ValueDescriptions` — otherwise the generic announcer speaks the raw multi-billion word. → [a380x.md](../a380x.md)

## ARINC-3

- The generic ARINC429 auto-decoder hook must run in `MainForm.UpdateDisplayText` AFTER `TryGetDisplayOverride` and only for vars `ProcessSimVarUpdate` didn't already handle — ad-hoc per-var decoders (baro, minimums, rudder trim) must win, and a var must never be decoded twice. → [a380x.md](../a380x.md)
