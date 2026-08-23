Corrected the four documentation pages that still named the A380 input events FlyByWire deleted
in #10855 — `docs/a32nx.md`, `docs/architecture.md`, `docs/troubleshooting-playbook.md` and the
`tools/a380-simvars-catalog.md` reference table, which now carries the same predates-#10855 banner
`tools/a380-fcu-vars.md` was given. #198 migrated the code and updated `docs/a380x.md`; these were
missed. A deleted K-event is swallowed by the sim with no error and no log line, so a stale doc is
the only thing standing between a reader and a control that silently does nothing — the trap
`docs/a380x.md` records #198 itself losing an investigation to. The pages also now carry that
section's retraction: the stock `KOHLSMAN SETTING STD:{1,2}` mirror is not dead, it is simply no
longer what MSFSBA reads. Dated design records and raw probe dumps were left untouched as history.
