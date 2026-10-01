# Troubleshooting a control — rules in full

Each section is the complete text of one rule. Its one-line form, under the same ID, is in `.claude/rules/troubleshooting.md`, which Claude Code loads when it reads matching code. Background: [troubleshooting-playbook.md](../troubleshooting-playbook.md).
The text is verbatim from CLAUDE.md as of `1f37801a`; a trailing "→ doc" pointer is the original's.

## DBG-1

- Never conclude "doesn't work" from the MCP `set_lvar` tool (native data-def write) — it is unreliable for many FBW/add-on L:vars; always test writes via the calculator/MobiFlight path. → [troubleshooting-playbook.md](../troubleshooting-playbook.md)

## DBG-2

- `L:NAME` and `L:1:NAME` are DIFFERENT variables — mirror whichever form the FBW source reads (newer systems use `RegisteredSimVar.create('L:1:…')`); writing the unprefixed form sticks on read-back yet the systems never see it, and `SetLVar` only ever emits the unprefixed form. → [troubleshooting-playbook.md](../troubleshooting-playbook.md)

## DBG-3

- Never "verify" a 9-digit ECAM code by injecting it through the MobiFlight calc path — that path is float32, so `310015001` lands as `310015008` and the lookup misses; trigger the real condition or rely on the lookup unit tests. → [troubleshooting-playbook.md](../troubleshooting-playbook.md)

## DBG-4

- A control can be fully working with NO audible/visible feedback — confirm by READ-BACK after ~1-2s, never by "I can't tell if it did anything." → [troubleshooting-playbook.md](../troubleshooting-playbook.md)

## DBG-5

- Read back the DOWNSTREAM effect (the stock simvar/system output), not just the L:var you wrote — a dead output-mirror L:var holds a write but drives nothing, and conversely a genuine mirror L:var can falsely "pass" a stickiness test if written to its own already-current value. → [troubleshooting-playbook.md](../troubleshooting-playbook.md)

## DBG-6

- Test every state of a multi-position control, not just 0→1 — a control can stick at one value and revert at another. → [troubleshooting-playbook.md](../troubleshooting-playbook.md)

## DBG-7

- The reliable existence test for an add-on control is its own SOURCE (cockpit XML/behavior template + a systems-side reader) — the write-stick test alone passes even on a nonexistent variable. → [troubleshooting-playbook.md](../troubleshooting-playbook.md)

## DBG-8

- Never assume a DOM/WebView control needs the visible widget you'd click — trace what the real cockpit hardware input actually writes underneath (e.g. an ECL driven by push-button L:var pulses, not DOM clicks; an MFD driven by keypress events, not a cursor). → [troubleshooting-playbook.md](../troubleshooting-playbook.md)

## DBG-9

- Coherent GT (Chromium 49) allows only ONE inspector socket per page for ANY aircraft using it — never open a second client against a view another client already holds; share the connection. → [troubleshooting-playbook.md](../troubleshooting-playbook.md)

## DBG-10

- A `_PRESSED`/`_Pressed`-style XMLVAR is almost always a model-only press-ANIMATION flag, not the real actuator — find the real K-event or state var instead of pulsing it. → [troubleshooting-playbook.md](../troubleshooting-playbook.md)

## DBG-11

- ⚠️ BEFORE diagnosing any reported A380/MSFSBA behaviour, CHECK WHICH BINARY IS RUNNING — `startup.log` names the Application Directory, and the DLL's ProductVersion carries the commit (`8.1.1-pre.263+470a5cfa`). A tester's "altimeter stuck on QNH" was chased through the FBW source to a wrong root cause and a needless commit, when the app was simply a pre-fix build. `debug.log`'s `Sending event:` lines say what the app ACTUALLY emitted and settle in seconds what source reading cannot. → [a380x.md](../a380x.md)
