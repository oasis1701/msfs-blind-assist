# The checklist window (Shift+C)

What the checklist window shows, where it comes from, and the traps found building it. The rules
are in `.claude/rules/checklists.md`; their full text is DA40S-9 in
[invariants/da40-shared-code.md](invariants/da40-shared-code.md#da40s-9), where the DA40 work
that built the reader recorded it.

## Where the content comes from

`Services/ChecklistContent` owns the order, and `Forms/ChecklistForm` only renders what it hands
back:

1. **The aircraft's own checklist.** Every MSFS aircraft may ship one as plain XML under
   `SimObjects/Airplanes/<model>/Checklist/`. `Services/NativeChecklistReader` renders it: a `Page`
   becomes a category, a `Checkpoint` becomes "Subject … Expectation", and a `Clue` becomes its own
   line underneath, because a clue is often the only place an operating detail is written down.
   It is the vendor's, complete, and follows the aircraft through updates.
2. **The checklist the aircraft names** in `Checklists/` (`IAircraftDefinition`'s checklist file
   name), when the package cannot be found.
3. **Main's fallback**, unchanged.

## Traps in the real files

- **Walk a page's children in order, including one level of `<Block>`.** A page mixes loose
  checkpoints with blocks, and each block carries its own `SubjectTT` heading. Walking only the
  direct children dropped 105 of the COWS DA40-XLS's 220 checkpoints and 29 of the NG's 137.
- **Asobo puts the literal word "Clue" in the expectation column of a note row**, which would
  render as "Warmup … Clue".
- **Two pages can share a name across steps**, which a category dictionary would silently
  swallow: the second is numbered.
- **Find the packages root from UserCfg.opt** (the active key only), Community folders before
  `Official\*`. The four default Community folders alone missed every pilot who installed
  packages on another drive.

The tests assert against the installed package and skip when it is absent: a reader tested only
against a hand-built sample passes while dropping a shape the real file uses. The DA40 write-up is
in [da40.md](da40.md) ("The aeroplane's own checklist, read out of its package").
