Test and contract coverage for the First Officer; the PR's fragments are condensed to what a
pilot sees changed, since the iteration on its own code never shipped separately.

- FlyByWire family: `FoFbwEventContractTests`, `FoFbwUnclaimedEventKeyTests` and
  `FbwA380FoKeyRegistrationTests` fail on a First Officer event, write or read its aircraft
  definition does not register; `Conf3RegistrationTests` ties each airframe's CONF 3 variable to
  its poll list; `FbwAutopilotEngageTests` pins the AP 1 press decisions.
- Headwind A330: `HwA330ParityTests` (named, reasoned differences from the A32NX profile only) and
  `HwA330DivergenceTests` (its own switch mappings).
- Shared: `UniversalAutomationServiceTests`, `FlowManager*Tests`, and pure-logic tests for the
  center-pump policy, gear confirmation, transition crossings, flow-completion exclusion and
  checklist latching.
- The PMDG dispatch tester, the CDU test probe and the iFly SDK probe were updated alongside.
