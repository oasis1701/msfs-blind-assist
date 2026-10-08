---
paths:
  - "MSFSBlindAssist/Aircraft/A380SurfaceDeflection.cs"
  - "tests/MSFSBlindAssist.Tests/**/A380SurfaceDeflectionTests.cs"
---
# A380 surface deflection rules

Loaded only with `A380SurfaceDeflection`, so this lesson does not add to every A380 file's rule budget. Background: docs/a380x.md.

Mirrored from da40-shared-code.md (it governs A380SurfaceDeflection; change it there and here together):
- [DA40S-11] FBW mirrors the LEFT aileron only: `A380SurfaceDeflection.DescribeMirrored` is for the left aileron and `Describe` for the right aileron and both elevators, or a droop reads as a roll. Full: docs/invariants/da40-shared-code.md#da40s-11
