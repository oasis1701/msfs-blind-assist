"""Extract the Fenix EFB's own option labels from the EFB bundle chunk that defines the
`humanizeKey` pipe (the settings page). Reads the object literal `const $={...}` that ends
right before that pipe, with a small literal parser (no regex over values), so enum value
labels stay scoped to their option and descriptions are never truncated.

Usage: python -I option_labels.py <chunk.js> <out.tsv>
Output rows (tab-separated, UTF-8, header first):
  key  value  label  description  control
  - an option row has an empty `value`; an enum value row repeats the option key and names the value.
  - tabs/newlines inside text become single spaces.
"""
import sys

src = open(sys.argv[1], encoding="utf-8", errors="strict").read()
pipe = src.find('name:"humanizeKey"')
if pipe < 0:
    sys.exit("no humanizeKey pipe in this chunk")
start = src.rfind("={ac_ess_auto_switching_enabled:", 0, pipe)
if start < 0:
    # fall back: the last `const X={` before the pipe
    start = src.rfind("={", 0, pipe)
i = start + 1


def ws():
    global i
    while src[i] in " \t\r\n":
        i += 1


def parse_string():
    global i
    q = src[i]
    i += 1
    out = []
    while src[i] != q:
        c = src[i]
        if c == "\\":
            n = src[i + 1]
            if n == "u":
                out.append(chr(int(src[i + 2:i + 6], 16)))
                i += 6
                continue
            out.append({"n": "\n", "t": "\t", "r": "\r"}.get(n, n))
            i += 2
            continue
        out.append(c)
        i += 1
    i += 1
    return "".join(out)


def parse_key():
    global i
    ws()
    if src[i] in "\"'":
        return parse_string()
    j = i
    while src[i].isalnum() or src[i] in "_$":
        i += 1
    return src[j:i]


def parse_value():
    global i
    ws()
    c = src[i]
    if c == "{":
        return parse_object()
    if c in "\"'":
        return parse_string()
    if src.startswith("!0", i):
        i += 2
        return True
    if src.startswith("!1", i):
        i += 2
        return False
    j = i
    while src[i] not in ",}":
        i += 1
    return src[j:i].strip()


def parse_object():
    global i
    assert src[i] == "{"
    i += 1
    obj = {}
    while True:
        ws()
        if src[i] == "}":
            i += 1
            return obj
        k = parse_key()
        ws()
        assert src[i] == ":", src[i - 40:i + 40]
        i += 1
        obj[k] = parse_value()
        ws()
        if src[i] == ",":
            i += 1


table = parse_object()


def clean(s):
    t = " ".join(str(s).split()) if s is not None else ""
    if "|" in t:
        sys.exit(f"'|' inside a label or description: {t!r} (the C# table separates fields with it)")
    return t


rows = 0
with open(sys.argv[2], "w", encoding="utf-8", newline="\n") as f:
    f.write("key\tvalue\tlabel\tdescription\tcontrol\n")
    for key, e in table.items():
        f.write(f"{key}\t\t{clean(e.get('label'))}\t{clean(e.get('description'))}\t{clean(e.get('controlType'))}\n")
        rows += 1
        for v, ve in (e.get("options") or {}).items():
            f.write(f"{key}\t{v}\t{clean(ve.get('label'))}\t{clean(ve.get('description'))}\t\n")
            rows += 1
print(f"{len(table)} options, {rows} rows", file=sys.stderr)
