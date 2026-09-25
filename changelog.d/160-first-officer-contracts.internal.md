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
- `Conf3RegistrationTests` ties `A32NX_SPEEDS_LANDING_CONF3` being `OnRequest` to the
  evaluators' poll lists.
- `FoFbwUnclaimedEventKeyTests` sweeps every A32NX and A330 First Officer write and
  requires the definition to claim it (`FoUnclaimedKeyPolicy`).
- There are pure-logic tests for the center-pump policy, gear confirmation, transition
  crossings, flow-completion exclusion and checklist latching.
- The PMDG dispatch tester, the CDU test probe and the iFly SDK probe were updated
  alongside.
