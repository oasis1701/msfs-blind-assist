#!/usr/bin/env python3
"""Prove every invariant bullet and every core paragraph of CLAUDE.md at BASE still exists verbatim.

  python tools/claude-md-split/verify_moved.py [--base 1f37801a]

Compares with whitespace collapsed and link targets ignored, since the move rewrites relative link
targets. Exit status 1 lists every block not found. Blocks that were deliberately REWRITTEN, not
moved (doc lists, "Details:" stubs, headings, the old Invariants preamble), are allowlisted below and
printed, so a reviewer sees them.
"""
import argparse
import glob
import os
import re
import subprocess
import sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))

ALLOWLIST = [
    r"^Details: \[",                                  # stub pointers, now rows of the map
    r"^\*\*Claude: Read these docs only",              # the old doc-list preamble
    r"^\*\*When to read detailed docs:\*\*",
    r"^\*\*Available documentation:\*\*",
    r"^- \*\*.*\*\* → \[",                             # "when to read" list items, now map rows
    r"^- \*\*\[[^\]]+\]\(docs/",                       # "available documentation" list items, now map rows
    r"^Every bullet below is a condensed guardrail",   # the old Invariants preamble
    r"^This file provides guidance to Claude Code",    # replaced by the new opening line
    r"^\*\*Check before calling it an oversight\.\*\*",  # pointer now names the rule files, not the old section
    r"^2\. Do NOT add to `BuildPanelControls\(\)`",       # pointer now names [VAR-6], not the old section
]


def norm(s):
    s = re.sub(r"\]\([^)]*\)", "]()", s)
    return re.sub(r"\s+", " ", s).strip()


def blocks(text):
    """Invariant bullets, plus paragraphs and bullets of the core (everything outside Invariants)."""
    text = text.replace("\r\n", "\n")
    a = text.index("## Invariants (do not revert)")
    b = text.index("## Quick Reference")
    inv, core = text[a:b], text[:a] + text[b:]
    out = [x for c in re.split(r"\n(?=### )", inv)[1:] for x in re.split(r"\n(?=- )", c)[1:]]
    in_code = False
    for para in re.split(r"\n\s*\n", core):
        if para.lstrip().startswith("```"):
            in_code = not in_code if para.count("```") % 2 else in_code
            continue
        if in_code:
            continue
        for item in re.split(r"\n(?=- |\d+\. )", para):
            if item.strip() and not item.lstrip().startswith("#"):
                out.append(item)
    return out


def corpus():
    paths = [os.path.join(ROOT, "CLAUDE.md"), os.path.join(ROOT, "changelog.d", "README.md")]
    paths += glob.glob(os.path.join(ROOT, "docs", "*.md"))
    paths += glob.glob(os.path.join(ROOT, "docs", "invariants", "*.md"))
    paths += glob.glob(os.path.join(ROOT, ".claude", "rules", "**", "*.md"), recursive=True)
    return norm("\n".join(open(p, encoding="utf-8").read() for p in paths))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--base", default="1f37801a")
    args = ap.parse_args()
    base = subprocess.run(["git", "show", f"{args.base}:CLAUDE.md"], capture_output=True,
                          encoding="utf-8", cwd=ROOT, check=True).stdout
    hay = corpus()
    missing, allowed, found = [], [], 0
    for blk in blocks(base):
        n = norm(blk)
        if not n:
            continue
        if n in hay:
            found += 1
        elif any(re.search(p, blk.strip()) for p in ALLOWLIST):
            allowed.append(blk)
        else:
            missing.append(blk)
    print(f"found verbatim: {found}; rewritten by design (allowlisted): {len(allowed)}; missing: {len(missing)}")
    for blk in allowed:
        print("  rewritten:", norm(blk)[:110])
    for blk in missing:
        print("  MISSING:", norm(blk)[:160])
    sys.exit(1 if missing else 0)


if __name__ == "__main__":
    main()
