Merged main into the First Officer branch and carried the FlyByWire #10855 input-event
migration through to the FO. The A380 First Officer still fired `A32NX.FCU_TO_AP_HDG_PUSH`,
which that FBW change deleted, so its "FCU heading: managed" step would have been a silent
no-op — the merge was conflict-free, because the migration never touched the FirstOfficer
directory, and an unregistered K-event produces no error and no log line. Renamed to
`A32NX.FCU_HDG_PUSH` and added `FoFbwEventContractTests`, which asserts every dotted FBW
event an FO executor can fire is one its aircraft definition actually registers, so the next
FBW rename cannot slip past the definition-side contract test the same way.
