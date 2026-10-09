---
paths:
  - "MSFSBlindAssist/Resources/coherent-flypad-agent.js"
  - "MSFSBlindAssist/Forms/**/FbwEfbForm*.cs"
  - "tools/flypad-shell-test/**"
  - "MSFSBlindAssist/Resources/flypad-shell.html"
---
# FlyByWire flyPad EFB (A320 and A380) rules

Loaded when Claude reads matching code. Background: docs/flypad.md. Full text of each rule: docs/invariants/flypad.md.

- [FPD-1] A control drivable only by an L:var write must not be wired through the injected agent (`SimVar.SetSimVarValue` silently no-ops there); expose it as an app-side panel control instead. Full: docs/invariants/flypad.md#fpd-1
- [FPD-2] Never render the flyPad scrape as native WinForms controls, except the silent list mode `FbwEfbForm.CreateControlFor` falls back to when WebView2 fails ([MD11-13]); only a WebView2 HTML document gives NVDA full browse mode, with headings and static text reachable. Full: docs/invariants/flypad.md#fpd-2
- [FPD-3] Never wipe and rebuild the flyPad WebView2 DOM every poll (`innerHTML` replace), which steals screen-reader focus; use the keyed in-place reconcile only. Full: docs/invariants/flypad.md#fpd-3
- [FPD-4] The flyPad reconcile key must be element CONTENT, never the scrape idx, which is re-stamped every scrape and collides nodes across pages and sub-tabs. Full: docs/invariants/flypad.md#fpd-4
- [FPD-5] The reconcile key must strip dynamic state suffixes (`(active)`/`(called)`/`(selected)`), or a state change rebuilds the node and throws NVDA's focus off the control just activated. Full: docs/invariants/flypad.md#fpd-5
- [FPD-6] Wheel Chocks/Safety Cones are read-only status with no setter or click handler in FBW; never make them clickable. Full: docs/invariants/flypad.md#fpd-6
- [FPD-7] The Ground Payload/Fuel builders must own (suppress) every non-actionable section child, the CG-chart card and all tooltip nodes, or the scrape flattens into unreadable fragments. Full: docs/invariants/flypad.md#fpd-7
- [FPD-8] `setValue` on a flyPad SimpleInput must commit with a synthetic Enter keydown/keyup plus blur; FBW commits on Enter/blur, not onChange, so a plain value-set never reaches the sim. Full: docs/invariants/flypad.md#fpd-8
- [FPD-9] On the Ground Payload/Fuel pages, suppress the "Fill ... from SimBrief" caption tooltip and icon button, never the value input; the user imports via the Dashboard instead. Full: docs/invariants/flypad.md#fpd-9
- [A380-3] Never implement the FBW flyPad pushback controls; pushback is done with GSX, a permanent decision. Full: docs/invariants/flypad.md#a380-3
- [FPD-10] `FbwEfbForm`'s list mode (`CreateControlFor`, `ApplyDisabledInPlace`) keeps a disabled control focusable: label + `DimmedSuffix`, activation says "Unavailable" and posts nothing, never `Enabled = false`. It dims EVERY control while the browser shell dims buttons and links only, on purpose; never harmonize. Full: docs/invariants/flypad.md#fpd-10

Mirrored from pmdg-efb.md (it governs the shared EFB shell, FbwEfbForm.cs; change it there and here together):
- [PEFB-6] Capture an alert/confirmation card as ONE assertive item and skip its heading/message subtree in the main collect loop; the app must `AnnounceImmediate` a dynamically-injected alert, since WebView2 aria-live isn't reliable for it. Full: docs/invariants/pmdg-efb.md#pefb-6

Mirrored from md11.md (they govern the MD-11 key and `announceChange` handling in the shared EFB shell, FbwEfbForm.cs; change them there and here together):
- [MD11-13] Stepper options come from the FIRST React fiber with `options`, never hand-listed and never past an empty array; a press is the pointer sequence plus `el.click()` and NOTHING ELSE; refuse targets under `pointer-events-none` (`A.isInert`); stamp a stable `key` in the reader, never a shared-shell text heuristic. (more: see full) Full: docs/invariants/md11.md#md11-13
- [MD11-26] The EFB shell speaks a pressed control's changed label ONLY where the reader flags it `announceChange: true` (the two stepper arrows, the tile button, the state tile's ACTION button), never unconditionally and never by suffix or by who was pressed; the per-item `live` hint cannot do this job. Full: docs/invariants/md11.md#md11-26
