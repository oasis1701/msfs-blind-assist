"""Find the installed TFDi MD-11 package on this PC.

Pure discovery: no printing, no input(), no sys.exit(). Callers render the
results. This is what makes it unit-testable against fake trees.

Covers FS2020 and FS2024, MS Store and Steam, and custom/external
InstalledPackagesPath. The package is identified by CONTAINING
ModelBehaviorDefs/TFDi_Design/MD11 -- never by the folder name -- because a
real install was measured with the aircraft in a folder called "Community2024"
(not "Community"), and because TFDi could rename the package.
"""
import os
import re
from dataclasses import dataclass

SIM_2024 = "MSFS 2024"
SIM_2020 = "MSFS 2020"

# The marker that identifies an MD-11 package, relative to the package dir.
PACKAGE_MARKER = os.path.join("ModelBehaviorDefs", "TFDi_Design", "MD11")

WASM_NAME = "md11host.wasm"

# How far below a package root to look. Measured shapes:
#   <root>/tfdidesign-aircraft-md11            (root is already a packages dir)
#   <root>/Community/tfdidesign-aircraft-md11
#   <root>/Community2024/tfdidesign-aircraft-md11
MAX_DEPTH = 2

_INSTALLED_PACKAGES_RE = re.compile(
    r'^\s*InstalledPackagesPath\s+"([^"]+)"', re.IGNORECASE
)


@dataclass(frozen=True)
class Md11Find:
    """One discovered MD-11 package."""

    sim_label: str
    package_dir: str
    wasm_path: str | None
    root: str


def parse_installed_packages_path(usercfg_path: str) -> str | None:
    """Return the InstalledPackagesPath value from a UserCfg.opt, or None.

    Mirrors EFBModPackageManager.TryParseInstalledPackagesPath: the value is
    always quoted, so an unquoted line is treated as malformed rather than
    guessed at.
    """
    try:
        with open(usercfg_path, "r", encoding="utf-8", errors="replace") as fh:
            for line in fh:
                m = _INSTALLED_PACKAGES_RE.match(line)
                if m:
                    value = m.group(1).strip()
                    return value or None
    except OSError:
        return None
    return None


def _is_md11_package(candidate: str) -> bool:
    return os.path.isdir(os.path.join(candidate, PACKAGE_MARKER))


def find_packages_under(root: str, max_depth: int = MAX_DEPTH) -> list[str]:
    """Return every MD-11 package at or below `root`, up to `max_depth` levels.

    Depth 0 is `root` itself. Identification is by content (PACKAGE_MARKER),
    so a renamed packages folder or a renamed TFDi package still resolves.
    Sorted for determinism -- the caller numbers these in a prompt.
    """
    if not root or not os.path.isdir(root):
        return []

    found: list[str] = []
    frontier = [(root, 0)]
    while frontier:
        current, depth = frontier.pop()
        if _is_md11_package(current):
            found.append(current)
            # Do not descend into a package -- its own subfolders are not packages.
            continue
        if depth >= max_depth:
            continue
        try:
            entries = sorted(os.scandir(current), key=lambda e: e.name)
        except OSError:
            continue
        for entry in entries:
            try:
                if entry.is_dir():
                    frontier.append((entry.path, depth + 1))
            except OSError:
                continue

    return sorted(found)


def find_wasm(package_dir: str) -> str | None:
    """Locate md11host.wasm inside a package, or None.

    Walked, never joined from a fixed relative path: the file was MEASURED at
    SimObjects/Airplanes/TFDi_Design_MD-11/common/panel/ on FS2024, while the
    generator's old hardcoded guess omitted the "common" level (presumed the
    FS2020 shape). Both must work without knowing which sim this is.

    Shortest path wins so the base aircraft beats a variant; sorted first so
    two runs on one machine can never disagree.
    """
    sim_objects = os.path.join(package_dir, "SimObjects")
    if not os.path.isdir(sim_objects):
        return None

    matches: list[str] = []
    for dirpath, _dirnames, filenames in os.walk(sim_objects):
        for name in filenames:
            if name.lower() == WASM_NAME:
                matches.append(os.path.join(dirpath, name))

    if not matches:
        return None
    return sorted(matches, key=lambda p: (len(p), p))[0]
