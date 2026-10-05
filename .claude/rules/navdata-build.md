---
paths:
  - "MSFSBlindAssist/Database/NavdataReader*.cs"
  - "MSFSBlindAssist/Resources/navdatareader.cfg"
  - "MSFSBlindAssist/Forms/DatabaseBuildProgressForm.cs"
  - "tests/MSFSBlindAssist.Tests/**/*NavdataReader*.cs"
---
# Navdata database build rules

Loaded when Claude reads matching code. Background: docs/architecture.md. Full text of each rule: docs/invariants/navdata-build.md.

- [NAV-1] `Resources/navdatareader.cfg` REPLACES navdatareader's built-in config, so re-derive it from the PINNED release's own `config/navdatareader.cfg` on every bump: re-copy stock, re-apply the single `*_jetways.bgl` line, change nothing else (`navdatareader-config.yml` enforces it). Full: docs/invariants/navdata-build.md#nav-1
- [NAV-2] The `*_jetways.bgl` exclusion only works for MSFS 2020: an MSFS 2024 database is built from SimConnect facility data and enumerates no BGL files, so never describe `ExcludeFilenames` as protecting FS2024 parking. Full: docs/invariants/navdata-build.md#nav-2
- [NAV-3] The jetways exclusion matches on FILENAME ONLY, library-wide, so a third-party `<ICAO>_jetways.bgl` is dropped too; this is an accepted, deliberate trade — do not "fix" it without raising it first. Full: docs/invariants/navdata-build.md#nav-3
- [NAV-4] `BuildDatabaseAsync` must refuse a missing/damaged config BEFORE touching the existing database, and must build to `<output>.building` and rename only on success — never delete the database up front. Full: docs/invariants/navdata-build.md#nav-4
- [NAV-5] Never reinstate the "SimConnect appears in stdout" failure heuristic: navdatareader's options dump contains that word on every run, so it reported every failure as a connection problem and hid the real error. Full: docs/invariants/navdata-build.md#nav-5
