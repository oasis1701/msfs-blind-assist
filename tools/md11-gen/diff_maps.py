#!/usr/bin/env python3
"""Compare two MD-11 control maps by node id, ignoring order.

    python diff_maps.py <old map.json> <new map.json>

After a regeneration the committed map cannot be reviewed with a text diff: it is ~31,000 lines,
and a reorder moves whole controls. This prints what the review needs (docs/md11.md §6): every
node added or removed; every field that changed on a node both maps carry (kind, label,
state_var, value_map, events, guard_id and state first, then the rest, sorted); ORDER-only
differences (a value_map's key order, a state block's lamp order -- which is the order lit legends
are spoken in -- or the controls array) on lines of their own, apart from real changes; and the
top-level keys that are not controls (counts, export_vars, state_only_vars, ...) entry by entry.
Exit status 1 when anything was added, removed or changed, 0 when the maps agree in content.
"""
import json
import sys

# The fields a regeneration review reads first, in this order; any other field follows, sorted.
KEY_FIELDS = ("kind", "label", "state_var", "value_map", "events", "guard_id", "state")
REAL_CHANGE = ("ADDED", "REMOVED", "CHANGED")
_MISSING = object()


def _show(value):
    return "(absent)" if value is _MISSING else json.dumps(value, ensure_ascii=False)


def canonical(value):
    """`value` with every dict's keys and every list's items sorted: its content, order removed."""
    if isinstance(value, dict):
        return {k: canonical(value[k]) for k in sorted(value)}
    if isinstance(value, list):
        return sorted((canonical(v) for v in value), key=lambda v: json.dumps(v, sort_keys=True))
    return value


def _diff_value(path, old, new, lines):
    """The line for one value at `path` ('<node>.<field>' or a top-level key), if it differs."""
    if _show(old) == _show(new):
        return
    if old is _MISSING:
        lines.append(f"ADDED   {path}: {_show(new)}")
    elif new is _MISSING:
        lines.append(f"REMOVED {path}: {_show(old)}")
    elif canonical(old) == canonical(new):
        lines.append(f"ORDER   {path}: {_show(old)} -> {_show(new)}")
    else:
        lines.append(f"CHANGED {path}: {_show(old)} -> {_show(new)}")


def _diff_top(key, old, new, lines):
    """A top-level key that is not the controls: dicts entry by entry, lists as items gained/lost."""
    if isinstance(old, dict) and isinstance(new, dict):
        before = len(lines)
        for sub in sorted(set(old) | set(new)):
            _diff_top(f"{key}.{sub}", old.get(sub, _MISSING), new.get(sub, _MISSING), lines)
        if len(lines) == before and list(old) != list(new):
            lines.append(f"ORDER   {key}: same entries, new key order")
        return
    if isinstance(old, list) and isinstance(new, list):
        gone = [v for v in old if v not in new]
        came = [v for v in new if v not in old]
        lines.extend(f"REMOVED {key}: {_show(v)}" for v in gone)
        lines.extend(f"ADDED   {key}: {_show(v)}" for v in came)
        if not gone and not came and old != new:
            lines.append(f"ORDER   {key}: same items, new order")
        return
    _diff_value(key, old, new, lines)


def diff_maps(old, new):
    """Every difference between two maps, one line each; [] when they are identical, order too."""
    lines = []
    old_by = {c["node_id"]: c for c in old.get("controls", [])}
    new_by = {c["node_id"]: c for c in new.get("controls", [])}
    for nid in sorted(set(old_by) - set(new_by)):
        lines.append(f"REMOVED {nid} ({old_by[nid].get('kind')}, {_show(old_by[nid].get('label'))})")
    for nid in sorted(set(new_by) - set(old_by)):
        lines.append(f"ADDED   {nid} ({new_by[nid].get('kind')}, {_show(new_by[nid].get('label'))})")
    for nid in sorted(set(old_by) & set(new_by)):
        a, b = old_by[nid], new_by[nid]
        rest = sorted((set(a) | set(b)) - set(KEY_FIELDS) - {"node_id"})
        for field in [f for f in KEY_FIELDS if f in a or f in b] + rest:
            _diff_value(f"{nid}.{field}", a.get(field, _MISSING), b.get(field, _MISSING), lines)
    kept_old = [c["node_id"] for c in old.get("controls", []) if c["node_id"] in new_by]
    kept_new = [c["node_id"] for c in new.get("controls", []) if c["node_id"] in old_by]
    if kept_old != kept_new:
        lines.append("ORDER   controls: the surviving nodes are listed in a new order")
    for key in sorted((set(old) | set(new)) - {"controls"}):
        _diff_top(key, old.get(key, _MISSING), new.get(key, _MISSING), lines)
    return lines


def exit_status(lines):
    """1 when any line is a real change (added, removed or changed), 0 for none or order only."""
    return 1 if any(line.startswith(REAL_CHANGE) for line in lines) else 0


def _load(path):
    with open(path, encoding="utf-8") as fh:
        return json.load(fh)


def main(argv=None):
    args = sys.argv[1:] if argv is None else argv
    if len(args) != 2:
        sys.exit("usage: python diff_maps.py <old map.json> <new map.json>")
    lines = diff_maps(_load(args[0]), _load(args[1]))
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(errors="backslashreplace")
    for line in lines:
        print(line)
    real = sum(1 for line in lines if line.startswith(REAL_CHANGE))
    print(f"-- {real} change(s), {len(lines) - real} order-only difference(s)")
    return exit_status(lines)


if __name__ == "__main__":
    sys.exit(main())
