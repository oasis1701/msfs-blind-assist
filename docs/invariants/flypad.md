# FlyByWire flyPad EFB (A320 and A380) — rules in full

Each section is the complete text of one rule. Its one-line form, under the same ID, is in `.claude/rules/flypad.md`, which Claude Code loads when it reads matching code. Background: [flypad.md](../flypad.md).
The text is verbatim from CLAUDE.md as of `1f37801a`; a trailing "→ doc" pointer is the original's. Cross-references such as "the bullet below", "above" or "under Core" point at CLAUDE.md's old single list, whose rules now live in several files: search `docs/invariants/` for the rule's key name to find it.

## FPD-1

- A control with no native `<input>`/click handler that can only be driven by an L:var write must NOT be wired through the injected agent — `SimVar.SetSimVarValue` silently no-ops from inside an injected agent function (Coherent restriction); expose it as an app-side panel control instead. → [flypad.md](../flypad.md)

## FPD-2

- Never render the flyPad scrape as native WinForms controls — only a WebView2 HTML document gives NVDA full browse mode; headings and static text are otherwise unreachable. The one exception is the silent list mode `FbwEfbForm.CreateControlFor` builds when WebView2 fails to initialise (no user toggle); MD11-13 governs how it shows a disabled control. → [flypad.md](../flypad.md)

Corrected 2026-10-08: names the WebView2-failed list mode, which the rule's "never" did not allow for. Evidence: `FbwEfbForm` (its "LIST MODE (silent fallback)" design note and `CreateControlFor`), and MD11-13's full text on that fallback.

## FPD-3

- Never wipe and rebuild the flyPad WebView2 DOM every poll (`innerHTML` replace) — that steals screen-reader focus; use the keyed in-place reconcile only. → [flypad.md](../flypad.md)

## FPD-4

- The flyPad reconcile key must be element CONTENT, not the scrape idx — idx is unstable across pages/sub-tabs and re-stamped from 1 every scrape, causing cross-tab node collisions. → [flypad.md](../flypad.md)

## FPD-5

- The reconcile key must strip dynamic state suffixes (`(active)`/`(called)`/`(selected)`) — keying on the full label destroys+rebuilds the node the instant its state changes, throwing NVDA's focus off the control the user just activated. → [flypad.md](../flypad.md)

## FPD-6

- Wheel Chocks/Safety Cones are READ-ONLY status (no click handler in FBW source) — never make them clickable; there is no setter, they only turn green when ground equipment is actually placed. → [flypad.md](../flypad.md)

## FPD-7

- The Ground Payload/Fuel builders must own (suppress) every non-actionable section child, the CG-chart card, and all tooltip nodes — a flat positional scrape otherwise flattens the W&B table/fuel schematic into unreadable fragments. → [flypad.md](../flypad.md)

## FPD-8

- `setValue` on a flyPad SimpleInput must commit via a synthetic Enter keydown/keyup + blur — FBW commits on Enter/blur, not on React onChange; a plain value-set silently fails to reach the sim. → [flypad.md](../flypad.md)

## FPD-9

- Suppress the "Fill … from SimBrief" controls' caption tooltip and icon button (never the value input) on the Ground Payload/Fuel pages — the user imports via the Dashboard instead. → [flypad.md](../flypad.md)
