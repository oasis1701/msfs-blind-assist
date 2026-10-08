---
paths:
  - "MSFSBlindAssist/SimConnect/CoherentPmdgEfbClient.cs"
  - "MSFSBlindAssist/Resources/coherent-pmdg-efb-agent.js"
  - "tests/MSFSBlindAssist.Tests/**/*PmdgEfb*.cs"
---
# PMDG EFB over the Coherent debugger rules

Loaded when Claude reads matching code. Background: docs/pmdg-efb.md. Full text of each rule: docs/invariants/pmdg-efb.md.

- [PEFB-1] The PMDG EFB is driven purely over the Coherent GT remote debugger: no injection, no HTTP bridge, no HTML patching, no sim restart; never reintroduce a Community-folder mod for it. Full: docs/invariants/pmdg-efb.md#pefb-1
- [PEFB-2] `collect()` must drop truly-anonymous buttons (no text/icon/title/aria/id/row-pair), or post-SimBrief-load leaflet overlay buttons flood the readout as "(button)". Full: docs/invariants/pmdg-efb.md#pefb-2
- [PEFB-3] Drop single-character glyph text BEFORE the same-row merge, or the Take-Off page's per-character runway-designator spans combine into garbage. Full: docs/invariants/pmdg-efb.md#pefb-3
- [PEFB-4] A field label must contain letters; a value-display label (e.g. a weather temperature "28") must never be mistaken for a field label. Full: docs/invariants/pmdg-efb.md#pefb-4
- [PEFB-5] Exclude `.opt-output`/`.groundops_ui_outputlabel` value cells from the generic measurement-value path: a value owned by a dedicated output pass must never also emit as an orphan duplicate line. Full: docs/invariants/pmdg-efb.md#pefb-5
- [PEFB-6] Capture an alert/confirmation card as ONE assertive item and skip its heading/message subtree in the main collect loop; the app must `AnnounceImmediate` a dynamically-injected alert, since WebView2 aria-live isn't reliable for it. Full: docs/invariants/pmdg-efb.md#pefb-6
- [PEFB-7] Unit toggles must read the LIVE `el.checked` state, never the lagging `Settings` object or a `::after` CSS caption: the toggles are textless and `Settings` only commits on Save Preferences. Full: docs/invariants/pmdg-efb.md#pefb-7
- [PEFB-8] `window.Settings` is ALWAYS false on the live PMDG view; code reading it must fall back to the bare global, never a `window.Settings`-only check, which silently no-ops live. Full: docs/invariants/pmdg-efb.md#pefb-8
- [A380C-13] Never detect the PMDG EFB settings page's unit toggles (`coherent-pmdg-efb-agent.js`) with a universal "checked=metric" rule; direction differs per toggle id, so use the per-id `UNIT_PAIRS` map. Full: docs/invariants/pmdg-efb.md#a380c-13
