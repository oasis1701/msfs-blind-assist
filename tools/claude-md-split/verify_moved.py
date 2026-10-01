#!/usr/bin/env python3
"""Prove every invariant bullet and every core paragraph of CLAUDE.md at BASE still exists verbatim.

  python tools/claude-md-split/verify_moved.py [--base 1f37801a]

Compares with whitespace collapsed and link targets ignored, since the move rewrites relative link
targets. Exit status 1 lists every block not found. Core blocks that were deliberately REWRITTEN, not
moved (the two doc lists, the "Details:" stub pointers, the old Invariants preamble), are allowlisted
below and printed, so a reviewer sees them. Invariant bullets are never allowlisted: every one must be
found. Headings are not checked (they carry no rule text; the old iFly heading's exceptions live in
docs/ifly-737.md).
"""
import argparse
import glob
import os
import re
import subprocess
import sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))

ALLOWLIST = [
    r"^Details: \[[^\]]+\]\([^)]+\)\.$",                 # a bare stub pointer, now a row of the map
    r"^Details \+ all A380X invariants: \[[^\]]+\]\([^)]+\)\.$",
    # The two stubs that carried a sentence of their own; the facts are in the doc they point at
    # (checked 2026-10-01: docs/gsx.md's "Developer internals", docs/pmdg-737.md's EFB and CDU notes).
    r"^Details: \[docs/gsx\.md\]\(docs/gsx\.md\) — user-facing usage in the main sections, developer internals",
    r"^Details: \[docs/pmdg-737\.md\]\(docs/pmdg-737\.md\)\. Key gotchas: two CDUs",
    r"^\*\*Claude: Read these docs only when the task specifically requires them\.\*\*$",   # old doc-list preamble
    r"^\*\*When to read detailed docs:\*\*$",
    r"^\*\*Available documentation:\*\*$",
    r"^- \*\*.+?\*\* → \[",                            # "when to read" list items, now map rows
    r"^- \*\*\[[^\]]+\]\(docs/[^)]+\)\*\* - ",           # "available documentation" list items, now map rows
    r"^Every bullet below is a condensed guardrail",     # the old Invariants preamble
    r"^This file provides guidance to Claude Code when working with this repository\.$",  # new opening line
    r"^\*\*Check before calling it an oversight\.\*\*",  # pointer now names the rule files, not the old section
    r"^2\. Do NOT add to `BuildPanelControls\(\)`",       # pointer now names [VAR-6], not the old section
]


def norm(s):
    s = re.sub(r"\]\([^)]*\)", "]()", s)
    return re.sub(r"\s+", " ", s).strip()


def blocks(text):
    """[(kind, block)]: kind "rule" for an Invariants bullet, "core" for a paragraph or list item elsewhere."""
    text = text.replace("\r\n", "\n")
    a = text.index("## Invariants (do not revert)")
    b = text.index("## Quick Reference")
    inv, core = text[a:b], text[:a] + text[b:]
    out = [("rule", x) for c in re.split(r"\n(?=### )", inv)[1:] for x in re.split(r"\n(?=- )", c)[1:]]
    for para in re.split(r"\n\s*\n", core):
        if para.lstrip().startswith("```"):
            continue                                   # code blocks: the build commands are kept in CLAUDE.md
        for item in re.split(r"\n(?=- |\d+\. )", para):
            if item.strip() and not item.lstrip().startswith("#"):
                out.append(("core", item))
    return out


def corpus():
    paths = [os.path.join(ROOT, "CLAUDE.md"), os.path.join(ROOT, "changelog.d", "README.md")]
    paths += glob.glob(os.path.join(ROOT, "docs", "*.md"))
    paths += glob.glob(os.path.join(ROOT, "docs", "invariants", "*.md"))
    paths += glob.glob(os.path.join(ROOT, ".claude", "rules", "**", "*.md"), recursive=True)
    return norm("\n".join(open(p, encoding="utf-8").read() for p in paths))


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    ap = argparse.ArgumentParser()
    ap.add_argument("--base", default="1f37801a")
    args = ap.parse_args()
    base = subprocess.run(["git", "show", f"{args.base}:CLAUDE.md"], capture_output=True,
                          encoding="utf-8", cwd=ROOT, check=True).stdout
    hay = corpus()
    missing, allowed, found = [], [], 0
    for kind, blk in blocks(base):
        n = norm(blk)
        if not n:
            continue
        if n in hay:
            found += 1
        elif kind == "core" and any(re.search(p, blk.strip()) for p in ALLOWLIST):
            allowed.append(blk)
        else:
            missing.append((kind, blk))
    print(f"found verbatim: {found}; rewritten by design (allowlisted): {len(allowed)}; missing: {len(missing)}")
    for blk in allowed:
        print("  rewritten:", norm(blk)[:110])
    for kind, blk in missing:
        print(f"  MISSING ({kind}):", norm(blk)[:160])
    sys.exit(1 if missing else 0)


if __name__ == "__main__":
    main()
