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


if __name__ == "__main__":
    unittest.main()
