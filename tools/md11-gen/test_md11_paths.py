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

    def test_finds_package_under_an_unrelated_folder_name(self):
        # The invariant is that a package is identified by its CONTENTS, never
        # its name. Every other "found" test uses the real folder name, which
        # would also pass a name-matching implementation. This one cannot: the
        # folder name resembles neither "tfdi" nor "md11", so only a
        # content-based search finds it. Guards a future "just look for
        # tfdidesign-aircraft-md11" simplification.
        with tempfile.TemporaryDirectory() as tmp:
            community = os.path.join(tmp, "Community")
            os.makedirs(community)
            pkg = make_package(community, name="zzz-some-renamed-folder")
            self.assertEqual(md11_paths.find_packages_under(tmp), [pkg])


class FindWasmTests(unittest.TestCase):
    def test_finds_wasm_under_common_panel_fs2024_shape(self):
        with tempfile.TemporaryDirectory() as tmp:
            pkg = make_package(tmp)  # default: common/panel
            self.assertEqual(
                md11_paths.find_wasm(pkg),
                os.path.join(pkg, "SimObjects", "Airplanes",
                             "TFDi_Design_MD-11", "common", "panel",
                             "md11host.wasm"),
            )

    def test_finds_wasm_under_panel_fs2020_shape(self):
        with tempfile.TemporaryDirectory() as tmp:
            pkg = make_package(
                tmp,
                wasm_rel=("SimObjects", "Airplanes", "TFDi_Design_MD-11",
                          "panel"),
            )
            self.assertEqual(
                md11_paths.find_wasm(pkg),
                os.path.join(pkg, "SimObjects", "Airplanes",
                             "TFDi_Design_MD-11", "panel", "md11host.wasm"),
            )

    def test_missing_wasm_returns_none(self):
        with tempfile.TemporaryDirectory() as tmp:
            pkg = make_package(tmp, with_wasm=False)
            self.assertIsNone(md11_paths.find_wasm(pkg))

    def test_shortest_path_wins_when_several_copies(self):
        with tempfile.TemporaryDirectory() as tmp:
            pkg = make_package(
                tmp,
                wasm_rel=("SimObjects", "Airplanes", "TFDi_Design_MD-11",
                          "panel"),
            )
            deeper = os.path.join(pkg, "SimObjects", "Airplanes",
                                  "TFDi_Design_MD-11", "variant", "x", "panel")
            os.makedirs(deeper)
            with open(os.path.join(deeper, "md11host.wasm"), "wb") as fh:
                fh.write(b"\0asm")
            self.assertEqual(
                md11_paths.find_wasm(pkg),
                os.path.join(pkg, "SimObjects", "Airplanes",
                             "TFDi_Design_MD-11", "panel", "md11host.wasm"),
            )

    def test_returns_none_when_simobjects_exists_but_holds_no_wasm(self):
        # The other missing-wasm test uses with_wasm=False, which creates no
        # SimObjects directory at all, so it only exercises the early
        # isdir() guard. This covers the second return-None path: the
        # directory is there and the walk simply finds nothing.
        with tempfile.TemporaryDirectory() as tmp:
            pkg = make_package(tmp, with_wasm=False)
            os.makedirs(os.path.join(pkg, "SimObjects", "Airplanes",
                                     "TFDi_Design_MD-11", "panel"))
            self.assertIsNone(md11_paths.find_wasm(pkg))


class CandidateRootsTests(unittest.TestCase):
    def _env(self, tmp):
        appdata = os.path.join(tmp, "Roaming")
        local = os.path.join(tmp, "Local")
        os.makedirs(appdata, exist_ok=True)
        os.makedirs(local, exist_ok=True)
        return {"APPDATA": appdata, "LOCALAPPDATA": local}, appdata, local

    def _usercfg(self, folder, target):
        os.makedirs(folder, exist_ok=True)
        with open(os.path.join(folder, "UserCfg.opt"), "w",
                  encoding="utf-8") as fh:
            fh.write('InstalledPackagesPath "%s"\n' % target)

    def test_fs2024_roaming_usercfg_external_path(self):
        with tempfile.TemporaryDirectory() as tmp:
            env, appdata, _local = self._env(tmp)
            external = os.path.join(tmp, "MSFS2024 Community")
            os.makedirs(external)
            self._usercfg(
                os.path.join(appdata, "Microsoft Flight Simulator 2024"),
                external,
            )
            roots = md11_paths.candidate_roots(env)
            self.assertIn((md11_paths.SIM_2024, external), roots)

    def test_fs2020_roaming_usercfg(self):
        with tempfile.TemporaryDirectory() as tmp:
            env, appdata, _local = self._env(tmp)
            external = os.path.join(tmp, "FS2020Packages")
            os.makedirs(external)
            self._usercfg(
                os.path.join(appdata, "Microsoft Flight Simulator"), external
            )
            roots = md11_paths.candidate_roots(env)
            self.assertIn((md11_paths.SIM_2020, external), roots)

    def test_fs2024_msstore_localcache_packages(self):
        with tempfile.TemporaryDirectory() as tmp:
            env, _appdata, local = self._env(tmp)
            store = os.path.join(
                local, "Packages", "Microsoft.Limitless_8wekyb3d8bbwe",
                "LocalCache", "Packages",
            )
            os.makedirs(store)
            roots = md11_paths.candidate_roots(env)
            self.assertIn((md11_paths.SIM_2024, store), roots)

    def test_fs2020_msstore_localcache_packages(self):
        with tempfile.TemporaryDirectory() as tmp:
            env, _appdata, local = self._env(tmp)
            store = os.path.join(
                local, "Packages", "Microsoft.FlightSimulator_8wekyb3d8bbwe",
                "LocalCache", "Packages",
            )
            os.makedirs(store)
            roots = md11_paths.candidate_roots(env)
            self.assertIn((md11_paths.SIM_2020, store), roots)

    def test_fs2020_steam_packages(self):
        with tempfile.TemporaryDirectory() as tmp:
            env, appdata, _local = self._env(tmp)
            steam = os.path.join(appdata, "Microsoft Flight Simulator",
                                 "Packages")
            os.makedirs(steam)
            roots = md11_paths.candidate_roots(env)
            self.assertIn((md11_paths.SIM_2020, steam), roots)

    def test_fs2024_steam_packages(self):
        with tempfile.TemporaryDirectory() as tmp:
            env, appdata, _local = self._env(tmp)
            steam = os.path.join(appdata, "Microsoft Flight Simulator 2024",
                                 "Packages")
            os.makedirs(steam)
            roots = md11_paths.candidate_roots(env)
            self.assertIn((md11_paths.SIM_2024, steam), roots)

    def test_nonexistent_installed_packages_path_skipped(self):
        with tempfile.TemporaryDirectory() as tmp:
            env, appdata, _local = self._env(tmp)
            self._usercfg(
                os.path.join(appdata, "Microsoft Flight Simulator 2024"),
                "Z:\\gone",
            )
            roots = md11_paths.candidate_roots(env)
            self.assertEqual(
                [r for r in roots if r[1] == "Z:\\gone"], []
            )

    def test_nothing_installed_returns_empty(self):
        with tempfile.TemporaryDirectory() as tmp:
            env, _appdata, _local = self._env(tmp)
            self.assertEqual(md11_paths.candidate_roots(env), [])

    def test_fs2024_ordered_before_fs2020(self):
        with tempfile.TemporaryDirectory() as tmp:
            env, appdata, _local = self._env(tmp)
            r24 = os.path.join(tmp, "p24")
            r20 = os.path.join(tmp, "p20")
            os.makedirs(r24)
            os.makedirs(r20)
            self._usercfg(
                os.path.join(appdata, "Microsoft Flight Simulator 2024"), r24
            )
            self._usercfg(
                os.path.join(appdata, "Microsoft Flight Simulator"), r20
            )
            roots = md11_paths.candidate_roots(env)
            labels = [label for label, _ in roots]
            self.assertLess(
                labels.index(md11_paths.SIM_2024),
                labels.index(md11_paths.SIM_2020),
            )

    def test_duplicate_roots_collapsed(self):
        # Roaming UserCfg and the Store UserCfg can name the same folder.
        with tempfile.TemporaryDirectory() as tmp:
            env, appdata, local = self._env(tmp)
            shared = os.path.join(tmp, "Shared")
            os.makedirs(shared)
            self._usercfg(
                os.path.join(appdata, "Microsoft Flight Simulator 2024"),
                shared,
            )
            self._usercfg(
                os.path.join(local, "Packages",
                             "Microsoft.Limitless_8wekyb3d8bbwe",
                             "LocalCache"),
                shared,
            )
            roots = md11_paths.candidate_roots(env)
            self.assertEqual(
                [r for r in roots if r[1] == shared],
                [(md11_paths.SIM_2024, shared)],
            )

    def test_fs2020_store_usercfg_is_read_on_its_own(self):
        # The store UserCfg.opt source had no isolating test: FS2020's was
        # never built, and FS2024's only appeared alongside the
        # higher-priority roaming UserCfg naming the same folder, which
        # would mask a broken path here.
        with tempfile.TemporaryDirectory() as tmp:
            env, _appdata, local = self._env(tmp)
            external = os.path.join(tmp, "Fs2020FromStoreCfg")
            os.makedirs(external)
            self._usercfg(
                os.path.join(local, "Packages",
                             "Microsoft.FlightSimulator_8wekyb3d8bbwe",
                             "LocalCache"),
                external,
            )
            self.assertIn((md11_paths.SIM_2020, external),
                          md11_paths.candidate_roots(env))

    def test_fs2024_store_usercfg_is_read_on_its_own(self):
        with tempfile.TemporaryDirectory() as tmp:
            env, _appdata, local = self._env(tmp)
            external = os.path.join(tmp, "Fs2024FromStoreCfg")
            os.makedirs(external)
            self._usercfg(
                os.path.join(local, "Packages",
                             "Microsoft.Limitless_8wekyb3d8bbwe",
                             "LocalCache"),
                external,
            )
            self.assertIn((md11_paths.SIM_2024, external),
                          md11_paths.candidate_roots(env))

    def test_shared_root_takes_the_fs2024_label_not_fs2020(self):
        # "De-duplicate, FIRST label wins" was only tested with the same
        # label twice, so it asserted nothing about WHICH label survives.
        # Both sims' roaming UserCfg name one folder here; FS2024 is
        # iterated first, so the surviving entry must be FS2024 and there
        # must be exactly one.
        with tempfile.TemporaryDirectory() as tmp:
            env, appdata, _local = self._env(tmp)
            shared = os.path.join(tmp, "OneFolderBothSims")
            os.makedirs(shared)
            self._usercfg(
                os.path.join(appdata, "Microsoft Flight Simulator 2024"),
                shared,
            )
            self._usercfg(
                os.path.join(appdata, "Microsoft Flight Simulator"), shared
            )
            matching = [r for r in md11_paths.candidate_roots(env)
                        if r[1] == shared]
            self.assertEqual(matching, [(md11_paths.SIM_2024, shared)])


class DiscoverTests(unittest.TestCase):
    def _env(self, tmp):
        appdata = os.path.join(tmp, "Roaming")
        os.makedirs(appdata, exist_ok=True)
        return {"APPDATA": appdata, "LOCALAPPDATA": os.path.join(tmp, "Local")}

    def _usercfg(self, appdata, sim_folder, target):
        folder = os.path.join(appdata, sim_folder)
        os.makedirs(folder, exist_ok=True)
        with open(os.path.join(folder, "UserCfg.opt"), "w",
                  encoding="utf-8") as fh:
            fh.write('InstalledPackagesPath "%s"\n' % target)

    def test_owner_layout_found_with_wasm(self):
        with tempfile.TemporaryDirectory() as tmp:
            env = self._env(tmp)
            root = os.path.join(tmp, "MSFS2024 Community")
            c2024 = os.path.join(root, "Community2024")
            os.makedirs(c2024)
            os.makedirs(os.path.join(root, "Community"))
            pkg = make_package(c2024)
            self._usercfg(env["APPDATA"], "Microsoft Flight Simulator 2024",
                          root)

            finds = md11_paths.discover(env)
            self.assertEqual(len(finds), 1)
            self.assertEqual(finds[0].sim_label, md11_paths.SIM_2024)
            self.assertEqual(finds[0].package_dir, pkg)
            self.assertIsNotNone(finds[0].wasm_path)
            self.assertEqual(finds[0].root, root)

    def test_package_without_wasm_is_reported_with_none(self):
        with tempfile.TemporaryDirectory() as tmp:
            env = self._env(tmp)
            root = os.path.join(tmp, "Pkgs")
            community = os.path.join(root, "Community")
            os.makedirs(community)
            make_package(community, with_wasm=False)
            self._usercfg(env["APPDATA"], "Microsoft Flight Simulator 2024",
                          root)

            finds = md11_paths.discover(env)
            self.assertEqual(len(finds), 1)
            self.assertIsNone(finds[0].wasm_path)

    def test_both_sims_give_two_finds_fs2024_first(self):
        with tempfile.TemporaryDirectory() as tmp:
            env = self._env(tmp)
            r24 = os.path.join(tmp, "p24")
            r20 = os.path.join(tmp, "p20")
            os.makedirs(r24)
            os.makedirs(r20)
            make_package(r24)
            make_package(r20)
            self._usercfg(env["APPDATA"], "Microsoft Flight Simulator 2024",
                          r24)
            self._usercfg(env["APPDATA"], "Microsoft Flight Simulator", r20)

            finds = md11_paths.discover(env)
            self.assertEqual(len(finds), 2)
            self.assertEqual(finds[0].sim_label, md11_paths.SIM_2024)
            self.assertEqual(finds[1].sim_label, md11_paths.SIM_2020)

    def test_nothing_installed_returns_no_finds(self):
        with tempfile.TemporaryDirectory() as tmp:
            self.assertEqual(md11_paths.discover(self._env(tmp)), [])

    def test_same_package_not_reported_twice(self):
        # Both sims' UserCfg naming one shared folder must not double-report.
        with tempfile.TemporaryDirectory() as tmp:
            env = self._env(tmp)
            root = os.path.join(tmp, "Shared")
            os.makedirs(root)
            make_package(root)
            self._usercfg(env["APPDATA"], "Microsoft Flight Simulator 2024",
                          root)
            self._usercfg(env["APPDATA"], "Microsoft Flight Simulator", root)

            self.assertEqual(len(md11_paths.discover(env)), 1)

    def test_describe_roots_lists_searched_roots(self):
        with tempfile.TemporaryDirectory() as tmp:
            env = self._env(tmp)
            root = os.path.join(tmp, "Pkgs")
            os.makedirs(root)
            self._usercfg(env["APPDATA"], "Microsoft Flight Simulator 2024",
                          root)
            described = md11_paths.describe_roots(env)
            self.assertTrue(any(root in line for line in described))


class ParseChoiceTests(unittest.TestCase):
    def test_valid_choice_is_zero_based(self):
        self.assertEqual(md11_paths.parse_choice("1", 2), 0)
        self.assertEqual(md11_paths.parse_choice("2", 2), 1)

    def test_whitespace_tolerated(self):
        self.assertEqual(md11_paths.parse_choice("  2  ", 2), 1)

    def test_rejects_out_of_range_and_junk(self):
        for bad in ("0", "-1", "3", "", "   ", "abc", "1.5", "1x"):
            self.assertIsNone(
                md11_paths.parse_choice(bad, 2), "should reject %r" % bad
            )


if __name__ == "__main__":
    unittest.main()
