#!/usr/bin/env python3
"""For every rule: is every code name it relies on covered by a rule file that carries it?

  python tools/claude-md-split/check_coverage.py

A rule loads only when Claude reads a file one of its rule files' `paths:` globs match. A name the rule
quotes (a type or a method declared in the app) whose defining files no such glob covers is a place the
rule silently fails to load: the one way this layout can lose a guardrail. A rule counts as carried by its
own rule file and by every file that MIRRORS its line.

Expect a residue: generic names quoted in passing are ignored below, and a name declared in many files
(a common method) is skipped as unjudgeable. Read the list; do not chase it to zero.
"""
import collections
import glob
import os
import re
import sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
# Quoted in passing by many rules; never what a rule guards.
IGNORE = {"Log", "SettingsManager", "UserSettings", "Resources", "POINT", "Run", "SimVarDefinition", "SimVarType",
          "IAircraftDefinition", "GetVariables", "Runway", "ParkingSpot", "FlightPlan", "MainForm", "Task",
          "SimConnectManager", "ProcessSimVarUpdate", "BuildPanelControls", "Dispose", "ToString"}
MAX_DEFINING_FILES = 3


def glob_re(g):
    """The semantics ClaudeContextBudgetTests pins: '**/' is zero or more folders, '*' stays in one."""
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
    sys.stdout.reconfigure(encoding="utf-8")
    globs_of = {}                                   # rule file -> compiled globs
    carriers = collections.defaultdict(set)         # rule ID -> rule files carrying its line
    for f in glob.glob(os.path.join(ROOT, ".claude", "rules", "*.md")):
        text = open(f, encoding="utf-8").read().replace("\r\n", "\n")
        name = os.path.basename(f)
        front = text.split("---\n")[1]
        globs_of[name] = [glob_re(l.strip()[2:].strip('"')) for l in front.splitlines() if l.strip().startswith("- ")]
        for m in re.finditer(r"^- \[([A-Z0-9]+-\d+)\] ", text, re.M):
            carriers[m.group(1)].add(name)

    defs = collections.defaultdict(set)             # type or method name -> files declaring it
    types = set()                                   # names declared as a type somewhere
    decl = re.compile(r"\b(?:class|record|struct|interface|enum)\s+([A-Z][A-Za-z0-9_]+)"
                      r"|^\s*(?:(?:public|private|internal|protected|static|async|override|virtual|readonly|sealed|unsafe)\s+)+"
                      r"[\w<>\[\],.?]+\s+([A-Z][A-Za-z0-9_]+)\s*\(", re.M)
    for p in glob.glob(os.path.join(ROOT, "MSFSBlindAssist", "**", "*.cs"), recursive=True):
        if os.sep + "bin" + os.sep in p or os.sep + "obj" + os.sep in p:
            continue
        rel = os.path.relpath(p, ROOT).replace("\\", "/")
        for m in decl.finditer(open(p, encoding="utf-8", errors="ignore").read()):
            defs[m.group(1) or m.group(2)].add(rel)
            if m.group(1):
                types.add(m.group(1))

    reports, judged = [], 0
    for f in sorted(glob.glob(os.path.join(ROOT, "docs", "invariants", "*.md"))):
        if os.path.basename(f) == "core.md":        # its rules live in CLAUDE.md, which always loads
            continue
        text = open(f, encoding="utf-8").read().replace("\r\n", "\n")
        for m in re.finditer(r"^## ([A-Z0-9]+-\d+)\n(.*?)(?=^## |\Z)", text, re.S | re.M):
            rid, body = m.group(1), m.group(2)
            if not carriers[rid]:                    # mirrored into CLAUDE.md only, or retired
                continue
            # A type, or a method distinctive enough to judge: declared in ONE file, with a name long enough
            # not to be a common member ('Name', 'Key', 'Init', 'SendEvent' collide across the app).
            names = {part for tok in re.findall(r"`([^`]+)`", body) for part in re.split(r"[.(/ ,<>]", tok)
                     if part in defs and part not in IGNORE and len(defs[part]) <= MAX_DEFINING_FILES
                     and (part in types or (len(defs[part]) == 1 and len(part) >= 12))}
            if not names:
                continue
            judged += 1
            globs = [g for c in carriers[rid] for g in globs_of[c]]
            uncovered = sorted(n for n in names if not any(g.match(fp) for g in globs for fp in defs[n]))
            if uncovered:
                reports.append((rid, sorted(carriers[rid]), [(n, sorted(defs[n])[0]) for n in uncovered[:4]]))
    print(f"{judged} rules quote a name declared in the app; {len(reports)} quote one that no carrying rule "
          f"file's globs cover")
    for rid, files, names in reports:
        print(f"  {rid:9s} {', '.join(files)}: " + "; ".join(f"{n} ({p})" for n, p in names))


if __name__ == "__main__":
    main()
