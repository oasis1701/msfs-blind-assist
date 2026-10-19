---
paths:
  - "guide/**"
  - "tools/GuideBuilder/**"
  - "MSFSBlindAssist/Services/HelpLinks.cs"
  - ".github/workflows/user-guide-*.yml"
  - "tests/MSFSBlindAssist.Tests/**/*GuideBuilder*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*UserGuideLine*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*HelpLinks*.cs"
---
# User guide rules

Loaded when Claude reads matching code. Background: docs/user-guide.md. Full text of each rule: docs/invariants/user-guide.md.

- [GUIDE-1] Link between guide pages with plain relative Markdown links (`taxi-guidance.md#taxi-before-you-start`, `index.md#appendices`), never Liquid, a `/guide/` path or a raw `<a href>`: the website's preview edition, GitHub's file view and the one-page build all follow them, and `GuideChecker` fails the tests otherwise. Full: docs/invariants/user-guide.md#guide-1
- [GUIDE-2] Every heading in `guide/` carries an explicit `{#id}` that is unique across the whole guide and never changes once published: other pages, outside links and the one-page build, which joins every chapter into one page, all point at it. Full: docs/invariants/user-guide.md#guide-2
- [GUIDE-3] The bundled guide's file name (`HelpLinks.BundledGuideFileName`) is a contract with the one-page step in release.yml and preview.yml: change all three together, or Help, User Guide quietly opens the online guide in every release (`HelpLinksTests` pins it). Full: docs/invariants/user-guide.md#guide-3
