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
