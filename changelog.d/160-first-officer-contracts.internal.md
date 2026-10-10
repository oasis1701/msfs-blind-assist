Test and contract coverage for the First Officer. It also condenses the PR's iteration
fragments, since that work is part of the feature and none of it shipped separately.

- `FoFbwEventContractTests` fails if a First Officer executor fires a dotted FBW event
  that its aircraft definition does not register. This caught the A380's "FCU heading:
  managed" step, which still fired `A32NX.FCU_TO_AP_HDG_PUSH` after FBW #10855 deleted
  it.
- `HwA330ParityTests` keeps the Headwind A330 profile in step with the A32NX profile. Any
  difference must be named, with a reason, in an allow-list that cannot go stale.
- `HwA330DivergenceTests` pins the A330's own switch mappings, including the two
  operands each indexed nav/logo light event needs.
- `Conf3RegistrationTests` ties each airframe's CONF 3 landing variable (`A32NX_FM_LANDING_CONF3`,
  `A380X_FM_LANDING_CONF3`, and the Headwind A330's `A32NX_SPEEDS_LANDING_CONF3`) being `OnRequest`
  to its evaluator's poll list, and each First Officer auto manager's read to its own airframe's name.
- `FbwAutopilotEngageTests` and `UniversalAutomationServiceTests` pin the FlyByWire auto-engage
  decisions. The helper presses AP 1 only on a definite off, never blind on an unknown state,
  and holds a press back while it would wait in the probe queue. Only the helper is pinned:
  whether each aircraft definition passes that queue condition in is not, because the send path
  needs a live sim. The service spaces its attempts three seconds apart, speaks its success
  confirmation without interrupting the aircraft's own call, and gives up with the
  Captain-action line that matches the latest reading.
- `FoFbwUnclaimedEventKeyTests` sweeps every A32NX and A330 First Officer write and
  requires the definition to claim it (`FoUnclaimedKeyPolicy`).
- `FbwA380FoKeyRegistrationTests` fails if a FlyByWire-family First Officer reads a key
  its aircraft definition does not register, or if the A380 one writes such a key. This
  caught the A380's flight-director step, whose per-side keys went away when FBW #10855
  gave the A380 a single flight-director button.
- There are pure-logic tests for the center-pump policy, gear confirmation, transition
  crossings, flow-completion exclusion and checklist latching.
- The PMDG dispatch tester, the CDU test probe and the iFly SDK probe were updated
  alongside.
