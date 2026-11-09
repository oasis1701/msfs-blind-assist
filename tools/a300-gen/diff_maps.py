r"""Reports what changed between two A300 control maps, for reviewing a regeneration.

    git show HEAD:MSFSBlindAssist/Resources/a300_control_map.json > %TEMP%\old_map.json
    python tools/a300-gen/diff_maps.py %TEMP%\old_map.json MSFSBlindAssist/Resources/a300_control_map.json

Every line it prints is something a panel row, a spoken word or a cockpit write may have
changed for; read each one before committing the new map. It compares every field of every
control (by id), the order the controls are listed in (it sets the row order of the panel
rows the checklist does not name), every lamp (by node: the cockpit rule it lights from and
its bus) and every other top-level key (the package version), so "no differences" means the
maps agree.

Exit status: 1 when the maps differ, 0 when they agree, 2 for a wrong argument count.
"""
import json
import sys

# The control fields a review reads first, in this order; any other field follows, sorted.
CONTROL_FIELDS = ('key', 'area', 'panel', 'title', 'kind', 'event', 'state_var', 'state_unit', 'scale',
                  'positions', 'values', 'press', 'rest', 'action', 'checklist')
# The lamp's cockpit rule, then the bus whose light power it needs.
LAMP_FIELDS = ('state', 'power')
# The map's two lists; every other top-level key is one value.
LISTS = ('controls', 'lamps')


def _changed(label, old, new, first, ident):
    """A `changed` line for each field two entries differ in: `first` in that order, then any other
    field sorted. A field one entry lacks counts as null."""
    fields = list(first) + sorted((set(old) | set(new)) - set(first) - {ident})
    return ['changed %s %s: %s -> %s' % (label, field, json.dumps(old.get(field)), json.dumps(new.get(field)))
            for field in fields if old.get(field) != new.get(field)]


def _diff_list(old, new, ident, first, prefix='', tag=lambda entry: ''):
    """Lines for one list of entries matched by `ident`: added, removed, then each field changed."""
    old_by = {e[ident]: e for e in old}
    new_by = {e[ident]: e for e in new}
    lines = ['added   %s%s%s' % (prefix, key, tag(new_by[key])) for key in sorted(set(new_by) - set(old_by))]
    lines += ['removed %s%s%s' % (prefix, key, tag(old_by[key])) for key in sorted(set(old_by) - set(new_by))]
    for key in sorted(set(old_by) & set(new_by)):
        lines += _changed(prefix + key, old_by[key], new_by[key], first, ident)
    return lines


def _reordered(old, new, ident):
    """True when the entries both lists carry are listed in a different order."""
    in_old = {e[ident] for e in old}
    in_new = {e[ident] for e in new}
    return ([e[ident] for e in old if e[ident] in in_new]
            != [e[ident] for e in new if e[ident] in in_old])


def diff(old, new):
    old_controls, new_controls = old.get('controls', []), new.get('controls', [])
    lines = _diff_list(old_controls, new_controls, 'id', CONTROL_FIELDS,
                       tag=lambda control: ' (%s)' % control.get('kind'))
    # The controls' array order is the row order of the panel rows the checklist does not name
    # (A300PanelLayout); lamps are looked up by node, so their order is not reported.
    if _reordered(old_controls, new_controls, 'id'):
        lines.append('order   controls: the surviving controls are listed in a new order')
    lines += _diff_list(old.get('lamps', []), new.get('lamps', []), 'node', LAMP_FIELDS, prefix='lamp ')
    for key in sorted((set(old) | set(new)) - set(LISTS)):
        if old.get(key) != new.get(key):
            lines.append('changed %s: %s -> %s' % (key, json.dumps(old.get(key)), json.dumps(new.get(key))))
    return lines


def exit_status(lines):
    """1 when there is any line to read, 0 when the maps agree."""
    return 1 if lines else 0


def main(argv=None):
    argv = sys.argv[1:] if argv is None else argv
    if len(argv) != 2:
        print(__doc__)
        return 2
    with open(argv[0], encoding='utf-8') as f:
        old = json.load(f)
    with open(argv[1], encoding='utf-8') as f:
        new = json.load(f)
    lines = diff(old, new)
    print('\n'.join(lines) if lines else 'no differences')
    return exit_status(lines)


if __name__ == '__main__':
    sys.exit(main())
