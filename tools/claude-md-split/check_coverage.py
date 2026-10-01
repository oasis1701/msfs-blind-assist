#!/usr/bin/env python3
"""For every rule: does its rule file's `paths:` cover at least one file that DEFINES a type the rule names?

  python tools/claude-md-split/check_coverage.py

A rule whose code no glob covers never loads when that code is edited, the one silent way this layout can
fail. Expect a residue of false alarms: a rule naming a generic type in passing (`Log`, `SettingsManager`)
or one whose real home is covered under another name. Read the list; do not chase it to zero.
"""
import collections
import glob
import os
import re

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))


def glob_re(g):
    """The same semantics ClaudeContextBudgetTests pins: '**/' is zero or more folders, '*' stays in one."""
    out, i = "^", 0
    while i < len(g):
        if g[i:i + 3] == "**/":
            out += "(?:[^/]+/)*"
            i += 3
            continue
        if g[i:i + 2] == "**":
            out += ".*"
            i += 2
            continue
        c = g[i]
        out += "[^/]*" if c == "*" else "[^/]" if c == "?" else re.escape(c)
        i += 1
    return re.compile(out + "$")


def main():
    rules = {}
    for f in glob.glob(os.path.join(ROOT, ".claude", "rules", "*.md")):
        front = open(f, encoding="utf-8").read().replace("\r\n", "\n").split("---\n")[1]
        rules[os.path.basename(f)[:-3]] = [glob_re(l.strip()[2:].strip('"'))
                                          for l in front.splitlines() if l.strip().startswith("- ")]

    defs = collections.defaultdict(set)   # type name -> files that define it
    for p in glob.glob(os.path.join(ROOT, "MSFSBlindAssist", "**", "*.cs"), recursive=True):
        if os.sep + "bin" + os.sep in p or os.sep + "obj" + os.sep in p:
            continue
        rel = os.path.relpath(p, ROOT).replace("\\", "/")
        text = open(p, encoding="utf-8", errors="ignore").read()
        for m in re.finditer(r"\b(?:class|record|struct|interface|enum)\s+([A-Z][A-Za-z0-9_]+)", text):
            defs[m.group(1)].add(rel)

    uncovered, judged = [], 0
    for f in sorted(glob.glob(os.path.join(ROOT, "docs", "invariants", "*.md"))):
        stem = os.path.basename(f)[:-3]
        if stem not in rules:          # core.md: its rules live in CLAUDE.md, which always loads
            continue
        text = open(f, encoding="utf-8").read().replace("\r\n", "\n")
        for m in re.finditer(r"^## ([A-Z0-9]+-\d+)\n(.*?)(?=^## |\Z)", text, re.S | re.M):
            rid, body = m.group(1), m.group(2)
            names = {part for tok in re.findall(r"`([^`]+)`", body)
                     for part in re.split(r"[.(/ ,]", tok) if part in defs}
            if not names:
                continue
            judged += 1
            files = set().union(*(defs[n] for n in names))
            if not any(g.match(fp) for g in rules[stem] for fp in files):
                uncovered.append((rid, stem, sorted(names)[:4], sorted(files)[:3]))
    print(f"{judged} rules name a type defined in the app; {len(uncovered)} have no defining file under "
          f"their rule file's globs")
    for rid, stem, names, files in uncovered:
        print(f"  {rid:9s} [{stem}] names {names} -> {files}")


if __name__ == "__main__":
    main()
