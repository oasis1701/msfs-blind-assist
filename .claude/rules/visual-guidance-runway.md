---
paths:
  - "MSFSBlindAssist/Services/VisualGuidance*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*VisualGuidanceRunwayDatum*.cs"
---
# Visual guidance's landing threshold and glide path angle rules

Loaded when Claude reads matching code. Background: docs/visual-guidance.md, "Where the path starts and how steep it is". Full text of each rule: docs/invariants/visual-guidance-runway.md.

- [VG-30] Visual guidance measures every distance, along-track position and path height from the painted landing threshold (`VisualGuidanceThreshold.Of`: the runway end moved its displaced-threshold offset down the course, an offset reaching the far end ignored), never the runway end's own position: KJFK 22R's 3,471 ft put the path 180 ft low. Full: docs/invariants/visual-guidance-runway.md#vg-30
- [VG-31] The glide path's angle is `VisualGuidanceVertical.PathAngleDeg`: the ILS glideslope's when believable (2° to 8°), else the PAPI's, else 3°; never let a PAPI override a believable ILS angle (the MSFS 2024 navdata has London City's PAPIs at 3.0 beside its 5.5° ILS). Full: docs/invariants/visual-guidance-runway.md#vg-31
