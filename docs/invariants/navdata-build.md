# Navdata database build — rules in full

Each section is the complete text of one rule. Its one-line form, under the same ID, is in `.claude/rules/navdata-build.md`, which Claude Code loads when it reads matching code. Background: [architecture.md](../architecture.md).
The text is verbatim from CLAUDE.md as of `1f37801a`; a trailing "→ doc" pointer is the original's.

## NAV-1

- `MSFSBlindAssist/Resources/navdatareader.cfg` REPLACES navdatareader's built-in config (it does not merge), so it must be re-derived from the PINNED release's own `config/navdatareader.cfg` on every version bump — re-copy stock, re-apply the single MSFSBA `*_jetways.bgl` line, change nothing else. It was once copied from navdatareader MASTER instead, which silently added an `ExcludeBglObjectFilter` token the shipped binary does not understand and dropped the `[Database]` section while the header claimed one change. `.github/workflows/navdatareader-config.yml` enforces this and also checks that all five version references agree. → CLAUDE.md

## NAV-2

- The `*_jetways.bgl` exclusion only works for MSFS 2020. An MSFS 2024 database is built from SimConnect facility data and enumerates NO BGL files (measured: one scenery area "SimConnect Airports", zero BGLs, against 2,380 present in Community), so `ExcludeFilenames` cannot match anything there — never describe it as protecting FS2024 parking. → CLAUDE.md

## NAV-3

- The exclusion matches on FILENAME ONLY, library-wide: a third-party package shipping its own `<ICAO>_jetways.bgl` is dropped too. This is an accepted, deliberate trade (a package-scoped `ExcludePathFilter` was tested and rejected for simplicity and rename-resistance) — do not "fix" it without raising it first. → CLAUDE.md

## NAV-4

- `BuildDatabaseAsync` must refuse a missing/damaged config BEFORE touching the existing database, and must build to `<output>.building` and rename only on success — the old code deleted the database up front, so every later failure left the pilot with no navdata at all. → CLAUDE.md

## NAV-5

- Never reinstate the "SimConnect appears in stdout" failure heuristic: navdatareader's routine options dump contains that word on every run, so it latched always and reported every failure — including FS2020 disk builds that use no SimConnect — as a connection problem while hiding the real error. → CLAUDE.md
