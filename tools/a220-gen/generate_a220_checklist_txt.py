#!/usr/bin/env python3
"""Generates the A220 companion text checklist for MSFSBA's ChecklistForm.

Input:  the aircraft's own stock ECL pack (checklists.json, ecl-editor schema) —
        default: the FS2020 Marketplace install location.
Output: MSFSBlindAssist/Checklists/A220_Checklist.txt ([Section] + one line per
        item — the ChecklistForm format).

The text checklist is the read-along companion (ShowChecklist); the LIVE ECL with
sensed states and "do this item" actuation is the separate Ctrl+Shift+C form.
Content is the official pack verbatim (challenge — response), so the two never
disagree. Conditional/multi-select branches flatten to indented "If YES:/If NO:"
blocks. Re-run after a Synaptic update if the pack part number changes.

Usage: python generate_a220_checklist_txt.py [<checklists.json>] [<out.txt>]
"""

from __future__ import annotations

import json
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
DEFAULT_IN = Path.home() / (
    "AppData/Local/Packages/Microsoft.FlightSimulator_8wekyb3d8bbwe/"
    "LocalCache/Packages/Official/OneStore/inibuilds-aircraft-a220/"
    "Config/Default/checklists.json"
)
DEFAULT_OUT = HERE.parents[1] / "MSFSBlindAssist" / "Checklists" / "A220_Checklist.txt"


def flat(s: str | None) -> str:
    """Pack strings carry display line-breaks — collapse to one spoken line."""
    return " ".join((s or "").split())


def item_lines(item: dict, indent: str = "") -> list[str]:
    t = item.get("type", "action")
    if t == "action":
        challenge = flat(item.get("challenge"))
        response = flat(item.get("response"))
        if not challenge:
            return []
        return [f"{indent}{challenge} — {response}" if response else f"{indent}{challenge}"]
    if t == "free-text":
        text = flat(item.get("text"))
        return [f"{indent}{text}"] if text else []
    if t in ("conditional", "multi-select"):
        out = []
        challenge = flat(item.get("challenge"))
        if challenge:
            out.append(f"{indent}{challenge}")
        for path_name, path_items in (item.get("paths") or {}).items():
            body: list[str] = []
            for sub in path_items:
                body.extend(item_lines(sub, indent + "    "))
            if body:
                out.append(f"{indent}  If {path_name}:")
                out.extend(body)
        return out
    return []  # form-feed and unknown layout types carry no spoken content


def main() -> None:
    in_path = Path(sys.argv[1]) if len(sys.argv) > 1 else DEFAULT_IN
    out_path = Path(sys.argv[2]) if len(sys.argv) > 2 else DEFAULT_OUT
    pack = json.loads(in_path.read_text(encoding="utf-8"))

    lines = [
        f"A220 Normal Checklists — {pack.get('name', 'Synaptic Default')} "
        f"(pack {pack.get('partNumber', '?')})",
        "Generated from the aircraft's own ECL pack by tools/a220-gen/generate_a220_checklist_txt.py.",
        "For the LIVE checklist with sensed states and item actuation use Ctrl+Shift+C.",
        "",
    ]
    for checklist in pack.get("normal", []):
        name = " ".join((checklist.get("name") or "?").split())
        lines.append(f"[{name}]")
        for item in checklist.get("items", []):
            lines.extend(item_lines(item))
        lines.append("")

    out_path.write_text("\n".join(lines), encoding="utf-8", newline="\r\n")
    n = sum(1 for l in lines if l and not l.startswith("["))
    print(f"wrote {out_path}: {len(pack.get('normal', []))} checklists, {n} lines")


if __name__ == "__main__":
    main()
