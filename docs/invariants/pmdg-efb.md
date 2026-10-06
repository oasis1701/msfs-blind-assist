# PMDG EFB over the Coherent debugger — rules in full

Each section is the complete text of one rule. Its one-line form, under the same ID, is in `.claude/rules/pmdg-efb.md`, which Claude Code loads when it reads matching code. Background: [pmdg-efb.md](../pmdg-efb.md).
The text is verbatim from CLAUDE.md as of `1f37801a`; a trailing "→ doc" pointer is the original's. Cross-references such as "the bullet below", "above" or "under Core" point at CLAUDE.md's old single list, whose rules now live in several files: search `docs/invariants/` for the rule's key name to find it.

## PEFB-1

- No injection, no HTTP bridge, no HTML patching, and no sim restart for the PMDG EFB — it is driven purely over the Coherent GT remote debugger; never reintroduce a Community-folder mod for it. → [pmdg-efb.md](../pmdg-efb.md)

## PEFB-2

- `collect()` must drop truly-anonymous buttons (no text/icon/title/aria/id/row-pair) — post-SimBrief-load leaflet overlay buttons would otherwise flood the readout as "(button)". → [pmdg-efb.md](../pmdg-efb.md)

## PEFB-3

- Single-character glyph text must be dropped BEFORE the same-row merge — the Take-Off page's per-character runway-designator spans would otherwise combine into garbage if merged first. → [pmdg-efb.md](../pmdg-efb.md)

## PEFB-4

- A field label must contain letters — a value-display label (e.g. a weather temperature "28") must never be mistaken for a field label. → [pmdg-efb.md](../pmdg-efb.md)

## PEFB-5

- `.opt-output`/`.groundops_ui_outputlabel` value cells must be excluded from the generic measurement-value path — a value already owned by a dedicated output pass must never ALSO emit as an orphan duplicate line. → [pmdg-efb.md](../pmdg-efb.md)

## PEFB-6

- Alert/confirmation cards must be captured as ONE assertive-announced item, and the card's own heading/message subtree must be skipped in the main collect loop — a dynamically-injected alert must be app-side `AnnounceImmediate`d since WebView2 aria-live isn't reliable for it. → [pmdg-efb.md](../pmdg-efb.md)

## PEFB-7

- Unit toggles must read the LIVE `el.checked` state, never the lagging `Settings` object or a `::after` CSS caption — the current build's toggles are textless and `Settings` only commits on Save Preferences. → [pmdg-efb.md](../pmdg-efb.md)

## PEFB-8

- `window.Settings` is ALWAYS false on the live PMDG view (never mirrored to `window`) — any code reading it must fall back to the bare global, never a `window.Settings`-only check (that silently no-ops live). → [pmdg-efb.md](../pmdg-efb.md)
