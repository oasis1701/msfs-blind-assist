"""Unit tests for generate_md11_map.resolve_paths.

resolve_paths() is where the owner's ruling actually lives: refuse to write
anything rather than write a partial map. Before this file, nothing tested
it -- three mutations that destroy the guarantee (deleting the missing-wasm
exit, deleting the --wasm isfile guard, and an off-by-falsy loop condition
that hangs forever) all left the pre-existing 45-test suite green.

This module tests ONLY resolve_paths(). Every failure path must exit
non-zero via sys.exit() WITHOUT ever reaching main()'s write step, and the
interactive multi-find prompt must behave correctly -- in particular,
answering "1" must select the FIRST find and must NOT loop:
parse_choice("1", n) returns the 0-based index 0, which is falsy, so a
naive `while not chosen:` loop (instead of `while chosen is None:`) spins
forever on exactly that answer.

stdlib unittest + unittest.mock only (pytest is not installed on the target
machine). Nothing here touches the real machine's MSFS install: every
scenario either builds a throwaway temp directory or monkeypatches
md11_paths.discover/describe_roots, so the result is independent of what is
actually installed on whatever machine runs this.

generate_md11_map.main() is guarded by `if __name__ == "__main__":`, so
importing the module below runs no argparse/sys.exit/file I/O -- confirmed
by hand before relying on it here.
"""
import contextlib
import io
import os
import tempfile
import unittest
from unittest import mock

import generate_md11_map
import md11_paths


def _make_find(package_dir, wasm_path, sim_label=md11_paths.SIM_2024, root=None):
    return md11_paths.Md11Find(
        sim_label=sim_label,
        package_dir=package_dir,
        wasm_path=wasm_path,
        root=root or os.path.dirname(package_dir),
    )


def _two_finds():
    """Two distinct, deterministic Md11Find fixtures for the multi-find tests."""
    return [
        _make_find(os.path.join("C:", "pkgA"), os.path.join("C:", "pkgA", "a.wasm"),
                   sim_label=md11_paths.SIM_2024),
        _make_find(os.path.join("C:", "pkgB"), os.path.join("C:", "pkgB", "b.wasm"),
                   sim_label=md11_paths.SIM_2020),
    ]


class ResolvePathsTestCase(unittest.TestCase):
    """Shared assertion for every "must refuse to write" scenario."""

    def assertExitsWithError(self, cm):
        # resolve_paths() never calls sys.exit(0) or sys.exit(None) -- every
        # failure path passes an explanatory string, which the interpreter
        # (when this propagates out of a real run instead of being caught by
        # a test) reports as exit status 1. A bare
        # `with self.assertRaises(SystemExit)` alone would also accept a
        # clean sys.exit() / sys.exit(0), which none of these paths intend.
        self.assertIsInstance(cm.exception.code, str)
        self.assertTrue(cm.exception.code)


class ResolvePathsWasmArgTests(ResolvePathsTestCase):
    def test_wasm_arg_nonexistent_file_exits(self):
        # discover() is mocked to return ONE perfectly good find, so that if
        # the --wasm isfile guard is ever deleted, resolve_paths() has
        # nothing else standing between it and a normal (wrong) return --
        # this proves the guard itself, not some other check, is what raises
        # here. (Without the mock, this would depend on whatever the real
        # host machine has installed.)
        find = _make_find(os.path.join("C:", "pkgOnly"),
                           os.path.join("C:", "pkgOnly", "real.wasm"))
        with tempfile.TemporaryDirectory() as tmp:
            bad_wasm = os.path.join(tmp, "nope.wasm")
            with mock.patch.object(md11_paths, "discover", return_value=[find]), \
                 contextlib.redirect_stdout(io.StringIO()):
                with self.assertRaises(SystemExit) as cm:
                    generate_md11_map.resolve_paths(None, bad_wasm)
        self.assertExitsWithError(cm)


class ResolvePathsPkgArgTests(ResolvePathsTestCase):
    def test_pkg_missing_marker_exits(self):
        with tempfile.TemporaryDirectory() as tmp:
            pkg = os.path.join(tmp, "not_an_md11_pkg")
            os.makedirs(pkg)
            with self.assertRaises(SystemExit) as cm:
                generate_md11_map.resolve_paths(pkg, None)
        self.assertExitsWithError(cm)

    def test_pkg_with_marker_but_no_wasm_exits(self):
        with tempfile.TemporaryDirectory() as tmp:
            pkg = os.path.join(tmp, "pkg")
            os.makedirs(os.path.join(pkg, md11_paths.PACKAGE_MARKER))
            # No SimObjects/ anywhere under pkg -- find_wasm(pkg) returns None.
            with self.assertRaises(SystemExit) as cm:
                generate_md11_map.resolve_paths(pkg, None)
        self.assertExitsWithError(cm)


class ResolvePathsDiscoveryTests(ResolvePathsTestCase):
    def test_nothing_found_exits(self):
        with mock.patch.object(md11_paths, "discover", return_value=[]), \
             mock.patch.object(md11_paths, "describe_roots", return_value=[]):
            with self.assertRaises(SystemExit) as cm:
                generate_md11_map.resolve_paths(None, None)
        self.assertExitsWithError(cm)

    def test_single_find_with_no_wasm_exits(self):
        # The discovery-path sibling of test_pkg_with_marker_but_no_wasm_exits
        # above -- covers the SECOND `if not wasm: _exit_no_wasm(...)` call
        # site in resolve_paths (the one reached when --pkg is omitted).
        find = _make_find(os.path.join("C:", "onlypkg"), None)
        with mock.patch.object(md11_paths, "discover", return_value=[find]), \
             contextlib.redirect_stdout(io.StringIO()):
            with self.assertRaises(SystemExit) as cm:
                generate_md11_map.resolve_paths(None, None)
        self.assertExitsWithError(cm)

    def test_multiple_finds_no_terminal_exits(self):
        finds = _two_finds()
        fake_stdin = mock.Mock()
        fake_stdin.isatty.return_value = False
        with mock.patch.object(md11_paths, "discover", return_value=finds), \
             mock.patch("sys.stdin", fake_stdin), \
             contextlib.redirect_stdout(io.StringIO()):
            with self.assertRaises(SystemExit) as cm:
                generate_md11_map.resolve_paths(None, None)
        self.assertExitsWithError(cm)

    def test_multiple_finds_stdin_none_exits_gracefully(self):
        # sys.stdin is None under pythonw.exe -- the exact case the review's
        # Finding 5 fixed. Plain `sys.stdin.isatty()` raises AttributeError
        # on a None stdin; the fix (`sys.stdin and sys.stdin.isatty()`) must
        # short-circuit to the same graceful exit as the no-terminal case
        # instead of crashing with an unrelated traceback.
        finds = _two_finds()
        with mock.patch.object(md11_paths, "discover", return_value=finds), \
             mock.patch("sys.stdin", None), \
             contextlib.redirect_stdout(io.StringIO()):
            with self.assertRaises(SystemExit) as cm:
                generate_md11_map.resolve_paths(None, None)
        self.assertExitsWithError(cm)

    def test_multiple_finds_answer_one_selects_first_and_does_not_loop(self):
        # THE falsy-zero trap: parse_choice("1", 2) returns 0 (0-based), and
        # 0 is falsy. input() is scripted with exactly ONE answer via
        # side_effect; if the loop is ever written as `while not chosen:` it
        # re-checks the condition, sees 0 (falsy) and calls input() a SECOND
        # time -- which raises StopIteration because the side_effect
        # iterable is exhausted. That is an error, not a silent hang, so
        # this test fails fast under that mutation instead of wedging the
        # whole suite (see the manual proof in the final review report,
        # which also confirms the real code really does hang before this
        # design choice is applied).
        finds = _two_finds()
        fake_stdin = mock.Mock()
        fake_stdin.isatty.return_value = True
        with mock.patch.object(md11_paths, "discover", return_value=finds), \
             mock.patch("sys.stdin", fake_stdin), \
             mock.patch("builtins.input", side_effect=["1"]) as mock_input, \
             contextlib.redirect_stdout(io.StringIO()):
            pkg, wasm = generate_md11_map.resolve_paths(None, None)
        mock_input.assert_called_once()
        self.assertEqual(pkg, finds[0].package_dir)
        self.assertEqual(wasm, finds[0].wasm_path)

    def test_invalid_answer_then_valid_reprompts_exactly_once(self):
        finds = _two_finds()
        fake_stdin = mock.Mock()
        fake_stdin.isatty.return_value = True
        with mock.patch.object(md11_paths, "discover", return_value=finds), \
             mock.patch("sys.stdin", fake_stdin), \
             mock.patch("builtins.input", side_effect=["nope", "2"]) as mock_input, \
             contextlib.redirect_stdout(io.StringIO()):
            pkg, wasm = generate_md11_map.resolve_paths(None, None)
        self.assertEqual(mock_input.call_count, 2)
        self.assertEqual(pkg, finds[1].package_dir)
        self.assertEqual(wasm, finds[1].wasm_path)

    def test_single_find_happy_path_returns_it(self):
        find = _make_find(os.path.join("C:", "onlypkg"),
                           os.path.join("C:", "onlypkg", "only.wasm"))
        with mock.patch.object(md11_paths, "discover", return_value=[find]), \
             contextlib.redirect_stdout(io.StringIO()):
            pkg, wasm = generate_md11_map.resolve_paths(None, None)
        self.assertEqual(pkg, find.package_dir)
        self.assertEqual(wasm, find.wasm_path)


if __name__ == "__main__":
    unittest.main()
