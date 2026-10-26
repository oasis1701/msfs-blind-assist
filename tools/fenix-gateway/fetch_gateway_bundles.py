"""Download the Fenix v3 EFB and FAP web-app bundles from YOUR OWN running Fenix (gateway on 127.0.0.1:8083),
so the data names, write calls and GraphQL operations the app uses can be checked. The bundles are Fenix's code:
never commit them.

Usage:  python -I fetch_gateway_bundles.py <output-folder>
Needs:  the sim running with a Fenix A319/A320/A321 v3 loaded. Reads only (HTTP GET); writes nothing to the sim.
Output: <out>/efb/*.js and <out>/fap/*.js, plus <out>/data-names.txt (every quoted data name found) and
        <out>/gql-operations.txt (every query/mutation/subscription text found).
"""
import pathlib
import re
import sys
import urllib.request

BASE = "http://127.0.0.1:8083"


def get(url: str) -> str:
    """The body, or "" when the gateway answers 404 (the runtime's chunk map lists a few chunks it does not serve)."""
    try:
        with urllib.request.urlopen(url, timeout=20) as r:
            return r.read().decode("utf-8", "replace")
    except urllib.error.HTTPError as e:
        if e.code == 404:
            return ""
        raise


def fetch_app(page: str, prefix: str, out: pathlib.Path) -> None:
    out.mkdir(parents=True, exist_ok=True)
    html = get(f"{BASE}/{page}")
    scripts = re.findall(r'src="([^"]+\.js)"', html)
    for s in scripts:
        name = s.rsplit("/", 1)[-1]
        (out / name).write_text(get(f"{BASE}/{prefix}{name}"), encoding="utf-8")
    runtime = next((p for p in out.glob("runtime.*.js")), None)
    if runtime is None:
        return
    for chunk_id, digest in re.findall(r'(\d+):"([0-9a-f]{16})"', runtime.read_text(encoding="utf-8")):
        name = f"{chunk_id}.{digest}.js"
        (out / name).write_text(get(f"{BASE}/{prefix}{name}"), encoding="utf-8")


def main() -> None:
    if len(sys.argv) != 2:
        sys.exit(__doc__)
    out = pathlib.Path(sys.argv[1])
    fetch_app("index.html", "", out / "efb")
    fetch_app("fap/index.html", "fap/", out / "fap")
    text = "".join(p.read_text(encoding="utf-8") for p in out.rglob("*.js"))
    names = set(re.findall(r'"((?:aircraft|doors|fenix|system|simulator|groundservice|gsx|control)\.[A-Za-z0-9_.]+)"', text))
    names |= set(re.findall(r'dataRef:"([^"]+)"', text))
    (out / "data-names.txt").write_text("\n".join(sorted(names)) + "\n", encoding="utf-8")
    ops = set(re.findall(r'((?:query|mutation|subscription) [A-Za-z_]+[ (][^`"]*)', text))
    (out / "gql-operations.txt").write_text(
        "\n".join(sorted(o.replace("\\n", " ") for o in ops)) + "\n", encoding="utf-8")
    print(f"{len(list(out.rglob('*.js')))} files, {len(names)} data names, {len(ops)} operations -> {out}")


if __name__ == "__main__":
    main()
