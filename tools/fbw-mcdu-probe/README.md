# fbw-mcdu-probe

Standalone Node CLI to inspect, drive, and capture the **FlyByWire A32NX** MCDU over
SimBridge's WebSocket (`ws://localhost:8380/interfaces/v1/mcdu`). Used to develop and
verify the accessible MCDU in MSFS Blind Assist, and to capture shareable offline screen
dumps for diagnosing a page without a live sim.

## Requirements
- Node.js 18+ (uses the built-in `node --test` runner)
- SimBridge running with the A32NX loaded (the same one the FBW remote MCDU uses)

## Install
```
npm install
```

## Commands
```
node probe.js watch  [--side left|right] [--host host:port] [--export file.jsonl]
node probe.js press  <KEY> [--side left|right] [--host host:port]
node probe.js type   "<text>" [--side left|right] [--host host:port]
node probe.js replay <file.jsonl>
```

- **watch** — print every screen update as decoded accessible text. `--export` appends
  each update as JSONL (`{ts, side, raw, decoded}`) for sharing/offline replay.
- **press** — send one key (`L1`..`R6`, `INIT`, `FPLN`, `DOT`, `CLR`, `OVFY`, ...), print result.
- **type** — send a character sequence to the scratchpad (verifies key timing).
- **replay** — re-render an exported capture offline (no sim needed). Share the `.jsonl`
  file with another party and they can replay the exact screens.

`--side` defaults to `left` (Captain / MCDU1); `right` = First Officer / MCDU2.
`--host` defaults to `localhost:8380`.

## Tests
```
npm test          # node --test — unit tests for the decode module
```

`mcdu-format.js` is the authoritative reference for the C# decoder
(`MSFSBlindAssist/Services/FbwMcduFormat.cs`); keep the two in sync.
