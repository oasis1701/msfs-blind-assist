---
paths:
  - "MSFSBlindAssist/Aircraft/BBS757/Bbs757ControlMap.cs"
  - "MSFSBlindAssist/Aircraft/BBS757/Bbs757Guarded.cs"
  - "MSFSBlindAssist/Aircraft/BBS757/Bbs757Write.cs"
  - "MSFSBlindAssist/Aircraft/BBS757/Bbs757Rpn.cs"
  - "MSFSBlindAssist/Aircraft/BBS757/Bbs757LandingAltitude.cs"
  - "MSFSBlindAssist/Aircraft/BBS757/Bbs757SpringControls.cs"
  - "MSFSBlindAssist/Aircraft/BBS757Definition.Overhead.cs"
  - "MSFSBlindAssist/Aircraft/BBS757Definition.Interaction.cs"
  - "MSFSBlindAssist/Aircraft/BBS757Definition.FirstOfficer.cs"
  - "MSFSBlindAssist/Resources/bbs757_control_map.json"
  - "tools/b757-gen/**"
  - "tests/MSFSBlindAssist.Tests/Bbs757ControlMapTests.cs"
  - "tests/MSFSBlindAssist.Tests/Bbs757GuardedTests.cs"
  - "tests/MSFSBlindAssist.Tests/Bbs757WriteTests.cs"
  - "tests/MSFSBlindAssist.Tests/Bbs757RpnTests.cs"
  - "tests/MSFSBlindAssist.Tests/Bbs757LandingAltitudeTests.cs"
  - "tests/MSFSBlindAssist.Tests/Bbs757SpringControlsTests.cs"
---
# BlueBird 757 control map and panel write rules

Loaded when Claude reads matching code. Background: docs/b757.md. Full text: docs/invariants/b757-controls.md. The shared 757 rules are in b757.md.

- [B757-6] A guarded 757 control (`guarded` in the curation, `Bbs757Control.Guard` in the app) carries no click code: its vendor click runs only with the guard open, so the generator emits its state var and guard fields alone, and stops on an `[if <test>]` gate note left in any other control's press, release, cw or ccw. Full: docs/invariants/b757-controls.md#b757-6
- [B757-7] Set the 757 landing altitude as `Bbs757LandingAltitude.Write` does: `L:B757_OVHD_LDG_ELEV` = target - 10, then one `L:B757_OVHD_LDG_ELEV_CHG` step (a 1 adds 10 ft); a direct write alone holds but leaves the panel's digits stale until the next knob step (measured). Full: docs/invariants/b757-controls.md#b757-7
- [B757-12] Write a guarded 757 control (a map row naming a `Guard`) only through `Bbs757Guarded.Plan`: open the guard, write the state var, and for `CloseSets` close the guard and write it too; an unread control or guard writes nothing. A click behind a closed guard does nothing, and BlueBird's close forces `CloseSets` (measured). Full: docs/invariants/b757-controls.md#b757-12
- [B757-13] After an app press of a fire handle Unlock button, relock it (`Bbs757Write.RelockAfterUnlock`, `FireHandleRelockMs` later): the cockpit relocks on an `O:` timer a calculator write never starts, so without it the handle stays unlocked (measured). Only a profile switch drops the relock, never a flight load. Full: docs/invariants/b757-controls.md#b757-13
- [B757-14] A 757 control whose click code the map leaves out (`direct_write` in the curation) is written through its state var, so its vendor click may write only that var and `O:` variables: another L:var, a K: or H: event or a gate note would be lost, and the generator's `check_direct_write` stops on any of them. Full: docs/invariants/b757-controls.md#b757-14
- [B757-15] A spring switch the app holds (`Bbs757SpringControls`: the trims, the manual cabin altitude switch, the P61 tests) is always released, the release read back and written again while it reads off rest: nothing in BlueBird returns an outside write to rest, and a lost release ran the rudder trim to its stop (measured). Full: docs/invariants/b757-controls.md#b757-15
- [B757-16] The PFD brightness is `B757_PFD_1_SCREEN_KNOB` and `B757_PFD_2_SCREEN_KNOB`, the state vars of the ADI knobs' rows (`direct_positions` in the curation); never the nodes of those names, whose clicks write `BBS757_RASTER_BRIGHTNESS_CA` and `_FO`, which nothing in the package reads. Full: docs/invariants/b757-controls.md#b757-16
