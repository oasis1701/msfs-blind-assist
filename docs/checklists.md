# The aircraft's own checklist

Every MSFS aircraft may ship its own checklist as plain XML under
`SimObjects/Airplanes/<model>/Checklist/`. `Services/NativeChecklistReader` reads it out of the
installed package. The rules are in `.claude/rules/checklists.md`; their full text is DA40S-9 and DA40S-14 in
[invariants/da40-shared-code.md](invariants/da40-shared-code.md#da40s-9), where the DA40 work that
built the reader recorded it.

## Who reads it

The checklist window that once showed it (Shift+C) was replaced by the First Officer window in PR #160,
which removed `Forms/ChecklistForm`, `Services/ChecklistContent` and the bundled `Checklists/*.txt`.
Today the reader's one user is the **COWS DA40's First Officer**: its read-back checklists and its two
reference pages (Weights/Speeds, Tips and help) are the aircraft's own pages, built at run time by
`FirstOfficer/DA40/DA40FoReadback` from `NativeChecklistReader.LoadPages` (FOD-4,
[first-officer.md](first-officer.md), "COWS Diamond DA40").

## What it gives

- `LoadPages(simObjectFolder)` / `ReadPages(XDocument)`: the document as pages, each a `Page` with its
  `Step` and its lines in the author's order. A line is a checkpoint (subject, expectation, its `Clue`
  lines and the cockpit `Instrument` ids it highlights) or a block's heading; a note row has no
  expectation.
- `Render` / `RenderFile`: the same as text, "[Category]" then "Subject ... Expectation" and each clue
  indented under it.

A clue is often the only place an operating detail is written down, so it is always kept.

## Traps in the real files

- **The files declare `encoding="Windows-1252"`**, which .NET reads only once a code-page provider
  is registered. Without it every load threw and the reader answered "no checklist": the checklist
  window quietly showed its bundled file, and the DA40 First Officer came up with no read-backs at all.
  `NativeChecklistReader` registers the provider and opens every file through `LoadDocument`.
- **Walk a page's children in order, including one level of `<Block>`.** A page mixes loose
  checkpoints with blocks, and each block carries its own `SubjectTT` heading. Walking only the
  direct children dropped 105 of the COWS DA40-XLS's 220 checkpoints and 29 of the NG's 137.
- **Asobo puts the literal word "Clue" in the expectation column of a note row**, which would
  read as "Warmup … Clue".
- **Two pages can share a name across steps**, which a category dictionary would silently
  swallow: the text rendering numbers the second.
- **Find the packages root from UserCfg.opt** (the active key only), Community folders before
  `Official\*`. The four default Community folders alone missed every pilot who installed
  packages on another drive.

The tests assert against the installed package and skip only when no file is found: a reader
tested only against a hand-built sample passes while dropping a shape the real file uses, and a
test that skipped on "nothing loaded" passed through the encoding failure above. The DA40 write-up is
in [da40.md](da40.md) ("The aeroplane's own checklist, read out of its package").
