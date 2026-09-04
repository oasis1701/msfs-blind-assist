"""Unit tests for md11_paths — MD-11 package discovery.

stdlib unittest only (pytest is not installed on the target machine).
Every test builds a fake tree under a TemporaryDirectory; nothing reads the
real machine's MSFS install.
"""
import os
import tempfile
import unittest

import md11_paths


class ParseInstalledPackagesPathTests(unittest.TestCase):
    def _write(self, tmp, text):
        p = os.path.join(tmp, "UserCfg.opt")
        with open(p, "w", encoding="utf-8") as fh:
            fh.write(text)
        return p

    def test_reads_quoted_path(self):
        with tempfile.TemporaryDirectory() as tmp:
            p = self._write(tmp, 'SomeOther 1\nInstalledPackagesPath "D:\\MSFS2024 Community"\n')
            self.assertEqual(
                md11_paths.parse_installed_packages_path(p), "D:\\MSFS2024 Community"
            )

    def test_missing_file_returns_none(self):
        self.assertIsNone(
            md11_paths.parse_installed_packages_path(
                os.path.join("Z:\\", "definitely", "absent", "UserCfg.opt")
            )
        )

    def test_no_key_returns_none(self):
        with tempfile.TemporaryDirectory() as tmp:
            p = self._write(tmp, "Nothing 0\nRelevant 1\n")
            self.assertIsNone(md11_paths.parse_installed_packages_path(p))

    def test_unquoted_value_returns_none(self):
        # UserCfg.opt always quotes this value; an unquoted line is malformed,
        # and guessing at it risks picking up a partial path.
        with tempfile.TemporaryDirectory() as tmp:
            p = self._write(tmp, "InstalledPackagesPath D:\\NoQuotes\n")
            self.assertIsNone(md11_paths.parse_installed_packages_path(p))

    def test_case_insensitive_key(self):
        with tempfile.TemporaryDirectory() as tmp:
            p = self._write(tmp, 'installedpackagespath "D:\\Lower"\n')
            self.assertEqual(md11_paths.parse_installed_packages_path(p), "D:\\Lower")

    def test_next_boot_key_is_not_mistaken_for_the_real_one(self):
        # UserCfg.opt also carries InstalledPackagesPathNextBoot. That prefix
        # collision has had to be special-cased THREE separate times in this
        # repo's C# (AircraftCfgCatalog, GsxAirplaneProfile,
        # EFBModPackageManager). The regex is already safe by construction --
        # its mandatory \s+ between key and quote does not match
        # "...NextBoot\"" -- and this pins that so a later "simplify the
        # regex" edit cannot silently reintroduce the bug.
        with tempfile.TemporaryDirectory() as tmp:
            p = self._write(
                tmp,
                'InstalledPackagesPathNextBoot "D:\\WrongOne"\n'
                'InstalledPackagesPath "D:\\RightOne"\n',
            )
            self.assertEqual(
                md11_paths.parse_installed_packages_path(p), "D:\\RightOne"
            )

    def test_next_boot_key_alone_returns_none(self):
        # If InstalledPackagesPathNextBoot is the ONLY key present, the function
        # must return None. This validates the NextBoot key is never matched.
        with tempfile.TemporaryDirectory() as tmp:
            p = self._write(
                tmp,
                'InstalledPackagesPathNextBoot "D:\\WrongOne"\n',
            )
            self.assertIsNone(md11_paths.parse_installed_packages_path(p))


def make_package(parent, name="tfdidesign-aircraft-md11", with_wasm=True,
                 wasm_rel=("SimObjects", "Airplanes", "TFDi_Design_MD-11",
                           "common", "panel")):
    """Build a fake MD-11 package: the marker dir, an XML, and optionally a wasm."""
    pkg = os.path.join(parent, name)
    marker = os.path.join(pkg, "ModelBehaviorDefs", "TFDi_Design", "MD11")
    os.makedirs(os.path.join(marker, "FlightDeck"), exist_ok=True)
    with open(os.path.join(marker, "FlightDeck", "Overhead.xml"), "w",
              encoding="utf-8") as fh:
        fh.write("<root/>")
    if with_wasm:
        wasm_dir = os.path.join(pkg, *wasm_rel)
        os.makedirs(wasm_dir, exist_ok=True)
        with open(os.path.join(wasm_dir, "md11host.wasm"), "wb") as fh:
            fh.write(b"\0asm")
    return pkg


def make_decoys(parent):
    """The other TFDi packages + an unrelated MD11-named mod. None are candidates."""
    for name, sub in [
        ("tfdidesign-aircraft-efb", "html_ui"),
        ("tfdidesign-aircraft-md-11fge-n840td", "SimObjects"),
        ("tfdidesign-aircraft-md-11fpw-n850td", "SimObjects"),
        ("xAEKSND_MD11_EXP", "sound"),
    ]:
        os.makedirs(os.path.join(parent, name, sub), exist_ok=True)


class FindPackagesUnderTests(unittest.TestCase):
    def test_finds_package_at_depth_1(self):
        with tempfile.TemporaryDirectory() as tmp:
            pkg = make_package(tmp)
            self.assertEqual(md11_paths.find_packages_under(tmp), [pkg])

    def test_finds_package_at_depth_2_named_community(self):
        with tempfile.TemporaryDirectory() as tmp:
            community = os.path.join(tmp, "Community")
            os.makedirs(community)
            pkg = make_package(community)
            self.assertEqual(md11_paths.find_packages_under(tmp), [pkg])

    def test_finds_package_in_community2024(self):
        # The owner's real layout: InstalledPackagesPath is a parent holding
        # Community, Community2024, Official2020, Official2024,
        # StreamedPackages -- and the MD-11 is in Community2024.
        with tempfile.TemporaryDirectory() as tmp:
            for sub in ("Community", "Official2020", "Official2024",
                        "StreamedPackages"):
                os.makedirs(os.path.join(tmp, sub))
            c2024 = os.path.join(tmp, "Community2024")
            os.makedirs(c2024)
            pkg = make_package(c2024)
            self.assertEqual(md11_paths.find_packages_under(tmp), [pkg])

    def test_ignores_decoy_packages(self):
        with tempfile.TemporaryDirectory() as tmp:
            community = os.path.join(tmp, "Community")
            os.makedirs(community)
            make_decoys(community)
            pkg = make_package(community)
            self.assertEqual(md11_paths.find_packages_under(tmp), [pkg])

    def test_does_not_search_deeper_than_two_levels(self):
        with tempfile.TemporaryDirectory() as tmp:
            deep = os.path.join(tmp, "a", "b", "c")
            os.makedirs(deep)
            make_package(deep)
            self.assertEqual(md11_paths.find_packages_under(tmp), [])

    def test_missing_root_returns_empty(self):
        self.assertEqual(
            md11_paths.find_packages_under(os.path.join("Z:\\", "nope")), []
        )

    def test_multiple_packages_returned_sorted(self):
        with tempfile.TemporaryDirectory() as tmp:
            b = os.path.join(tmp, "Bfolder")
            a = os.path.join(tmp, "Afolder")
            os.makedirs(b)
            os.makedirs(a)
            pkg_b = make_package(b)
            pkg_a = make_package(a)
            self.assertEqual(
                md11_paths.find_packages_under(tmp), sorted([pkg_a, pkg_b])
            )


if __name__ == "__main__":
    unittest.main()
