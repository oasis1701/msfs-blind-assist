#!/usr/bin/env python3
"""Generates the Synaptic A220 documented-SimVar table from the official docs.

Input:
  reference/simvars.mdx — the vendored SimVars page of docs.synapticsim.com
                          (repo synapticsim/docs): the complete official L:var
                          surface, one markdown table row per variable.

Output:
  MSFSBlindAssist/Aircraft/A220/SynapticA220SimVarData.cs

Usage:  python generate_a220_simvars.py [<simvars.mdx>] [<out.cs>]

What is derived per row (never hand-edit the output — extend THIS script):
  - Name        raw documented name, with `{placeholder}` templates expanded
                through the explicit EXPANSIONS table below (engines 1/2,
                sides L/R, DUs 1-5, the named electrical buses). Rows whose
                placeholder has no expansion rule (the pullable circuit
                breaker matrix) are dropped with a stderr note — nothing in
                MSFSBA needs them yet.
  - IsLvar      name starts with `L:` (stock SimVars like GEAR HANDLE POSITION
                are documented in the same tables).
  - Unit        the documented unit string, normalized to a SimConnect unit.
  - IsLamp      name ends with ` Lamp` — annunciator output, monitor-only.
  - IsAural     name starts with `L:A22X Aural ` — the aircraft plays these
                sounds itself; MSFSBA must NEVER register or announce them
                (plan gotcha #5). They are kept in the table so a test can
                pin the exclusion.
  - EnumLabels  parsed from `N = Label` runs in the description (e.g.
                "0 = Off, 1 = Auto, 2 = On"), position = value.

Duplicate documented names (a var listed both under its owning system and the
ICCP monitor section) are collapsed to one entry; when only one of the copies
carries enum labels, those labels win.
"""

from __future__ import annotations

import re
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
DEFAULT_IN = HERE / "reference" / "simvars.mdx"
DEFAULT_OUT = HERE.parents[1] / "MSFSBlindAssist" / "Aircraft" / "A220" / "SynapticA220SimVarData.cs"

# Explicit placeholder expansions. A template row is expanded once per value,
# substituting into both the name and (for {index}/{side}/{N}) nothing else —
# descriptions are not emitted. Anything not listed here is dropped loudly.
EXPANSIONS: dict[str, list[str]] = {
    "{index}": ["1", "2"],
    "{side}": ["L", "R"],
    "{N}": ["1", "2", "3", "4", "5"],
    "{name}": [
        "AC Bus 1", "DC Bus 1", "DC Ess Bus 1", "Batt Dir Bus 1", "DC Emer Bus",
        "AC Bus 2", "DC Bus 2", "DC Ess Bus 2", "Batt Dir Bus 2", "AC Ess Bus",
        "DC Ess Bus 3",
    ],
}

# Documented unit string -> SimConnect unit string used at registration time.
UNIT_MAP = {
    "Number": "number",
    "Bool": "bool",
    "Enum": "number",
    "Percent over 100": "percent over 100",
    "Degree": "degrees",
    "Degree per second": "degrees per second",
    "Volt": "volts",
    "Celsius": "celsius",
    "Percent": "percent",
    "Feet": "feet",
    "—": "number",
}

# "L:A22X Aural *" marks the RIU's native aural PLAYBACK flags — but three vars
# share the prefix without being playback flags: the ICCP aural-warning-inhibit
# switch (+ its lamp) and the RIU self-test trigger. Those are real controls.
AURAL_PREFIX_EXCEPTIONS = {
    "L:A22X Aural Warn Inhibit",
    "L:A22X Aural Warn Inhibit Lamp",
    "L:A22X Aural Internal Test",
}

ROW_RE = re.compile(r"^\|\s*`([^`]+)`\s*\|\s*([^|]+?)\s*\|\s*(.*?)\s*\|$")
ENUM_RE = re.compile(r"(\d+)\s*=\s*([A-Za-z][A-Za-z0-9 /()'-]*?)(?=\s*(?:,|\.|\(|$|;))")


def parse_rows(text: str) -> list[tuple[str, str, str]]:
    rows: list[tuple[str, str, str]] = []
    for line in text.splitlines():
        m = ROW_RE.match(line.strip())
        if not m:
            continue
        name, unit, desc = m.groups()
        if name == "Variable" or set(name) <= {"-"}:
            continue
        rows.append((name, unit, desc))
    return rows


def parse_enum_labels(desc: str) -> list[str] | None:
    pairs = [(int(v), label.strip()) for v, label in ENUM_RE.findall(desc)]
    if len(pairs) < 2:
        return None
    pairs.sort()
    # Require a dense 0..n run — anything else is prose, not an enum table.
    if [v for v, _ in pairs] != list(range(len(pairs))):
        return None
    return [label for _, label in pairs]


def expand(name: str, unit: str, desc: str) -> list[tuple[str, str, str]] | None:
    placeholders = re.findall(r"\{[A-Za-z]+\}", name)
    if not placeholders:
        return [(name, unit, desc)]
    if len(placeholders) > 1:
        return None
    ph = placeholders[0]
    if ph not in EXPANSIONS:
        return None
    return [(name.replace(ph, v), unit, desc) for v in EXPANSIONS[ph]]


def cs_str(s: str) -> str:
    return '"' + s.replace("\\", "\\\\").replace('"', '\\"') + '"'


def main() -> None:
    in_path = Path(sys.argv[1]) if len(sys.argv) > 1 else DEFAULT_IN
    out_path = Path(sys.argv[2]) if len(sys.argv) > 2 else DEFAULT_OUT
    text = in_path.read_text(encoding="utf-8")

    seen: dict[str, dict] = {}
    dropped: list[str] = []
    order: list[str] = []
    for raw_name, raw_unit, desc in parse_rows(text):
        expanded = expand(raw_name, raw_unit, desc)
        if expanded is None:
            dropped.append(raw_name)
            continue
        for name, unit, d in expanded:
            if unit not in UNIT_MAP:
                raise SystemExit(f"unmapped unit {unit!r} on {name!r} — extend UNIT_MAP")
            labels = parse_enum_labels(d)
            if name in seen:
                if labels and not seen[name]["labels"]:
                    seen[name]["labels"] = labels
                continue
            seen[name] = {
                "unit": UNIT_MAP[unit],
                "labels": labels,
                "lamp": name.endswith(" Lamp"),
                "aural": name.startswith("L:A22X Aural ") and name not in AURAL_PREFIX_EXCEPTIONS,
                "lvar": name.startswith("L:"),
            }
            order.append(name)

    for d in dropped:
        print(f"note: dropped (no expansion rule): {d}", file=sys.stderr)

    lamps = [n for n in order if seen[n]["lamp"]]
    aurals = [n for n in order if seen[n]["aural"]]

    out: list[str] = []
    out.append("// <auto-generated>")
    out.append("// Generated by tools/a220-gen/generate_a220_simvars.py from the vendored official")
    out.append("// docs page tools/a220-gen/reference/simvars.mdx (docs.synapticsim.com, repo")
    out.append("// synapticsim/docs). Do not hand-edit — extend the generator and re-run instead.")
    out.append("// </auto-generated>")
    out.append("")
    out.append("namespace MSFSBlindAssist.Aircraft.A220;")
    out.append("")
    out.append("/// <summary>One documented Synaptic A220 simulator variable.</summary>")
    out.append("public readonly struct A220DocumentedVar(string name, string unit, bool isLvar, bool isLamp, bool isAural, string[]? enumLabels)")
    out.append("{")
    out.append("    /// <summary>Full documented name including the L: prefix where present (A220 L:var names contain SPACES).</summary>")
    out.append("    public string Name { get; } = name;")
    out.append("    /// <summary>SimConnect unit string to register reads with.</summary>")
    out.append("    public string Unit { get; } = unit;")
    out.append("    public bool IsLvar { get; } = isLvar;")
    out.append("    /// <summary>Annunciator lamp output — batch-monitor only, never a panel control.</summary>")
    out.append("    public bool IsLamp { get; } = isLamp;")
    out.append("    /// <summary>Native aural-callout flag — must NEVER be registered or announced (the aircraft plays the sound itself).</summary>")
    out.append("    public bool IsAural { get; } = isAural;")
    out.append("    /// <summary>Documented enum labels, index = value (e.g. Off/Auto/On), or null.</summary>")
    out.append("    public string[]? EnumLabels { get; } = enumLabels;")
    out.append("}")
    out.append("")
    out.append("/// <summary>The documented Synaptic A220 SimVar surface (see simvars.mdx).</summary>")
    out.append("public static class SynapticA220SimVarData")
    out.append("{")
    out.append(f"    /// <summary>All {len(order)} documented variables, in documentation order.</summary>")
    out.append("    public static readonly A220DocumentedVar[] Vars =")
    out.append("    [")
    for n in order:
        e = seen[n]
        labels = "null" if not e["labels"] else "[" + ", ".join(cs_str(x) for x in e["labels"]) + "]"
        flags = f"{str(e['lvar']).lower()}, {str(e['lamp']).lower()}, {str(e['aural']).lower()}"
        out.append(f"        new({cs_str(n)}, {cs_str(e['unit'])}, {flags}, {labels}),")
    out.append("    ];")
    out.append("")
    out.append(f"    /// <summary>The {len(lamps)} annunciator lamp vars (name ends \" Lamp\") — the batch monitor set.</summary>")
    out.append("    public static readonly string[] LampVars =")
    out.append("    [")
    for n in lamps:
        out.append(f"        {cs_str(n)},")
    out.append("    ];")
    out.append("")
    out.append(f"    /// <summary>The {len(aurals)} native aural flags — excluded from registration entirely.</summary>")
    out.append("    public static readonly string[] AuralVars =")
    out.append("    [")
    for n in aurals:
        out.append(f"        {cs_str(n)},")
    out.append("    ];")
    out.append("}")
    out.append("")

    out_path.write_text("\n".join(out), encoding="utf-8", newline="\n")
    print(f"wrote {out_path}: {len(order)} vars ({len(lamps)} lamps, {len(aurals)} aurals), dropped {len(dropped)} template rows")


if __name__ == "__main__":
    main()
