#!/usr/bin/env python3
"""Unit tests for tools/smoke-check.py. Run: python3 -I tools/test_smoke_check.py"""
import importlib.util
import os
import sys
import unittest

sys.dont_write_bytecode = True

_spec = importlib.util.spec_from_file_location(
    "smoke_check", os.path.join(os.path.dirname(os.path.abspath(__file__)), "smoke-check.py"))
smoke = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(smoke)


class DeclaredIds(unittest.TestCase):
    def test_collects_literal_ids(self):
        src = 'PatchFirstFire.Mark("campaign.patch.a");\n  PatchFirstFire.Mark( "tactical.patch.b.prefix" );'
        self.assertEqual({"campaign.patch.a", "tactical.patch.b.prefix"},
                         set(smoke.declared_patch_ids([("X.cs", src)])))

    def test_rejects_non_literal_id(self):
        with self.assertRaises(smoke.SourceError) as ctx:
            smoke.declared_patch_ids([("X.cs", "\n\nPatchFirstFire.Mark(ScopeName);")])
        self.assertIn("X.cs:3", str(ctx.exception))

    def test_ignores_definition(self):
        src = "internal static void Mark(string patchId) { }"
        self.assertEqual({}, smoke.declared_patch_ids([("PatchFirstFire.cs", src)]))


class ScanLog(unittest.TestCase):
    LOG = [
        "[Info   :Whiskey Realism — Strategic AI Overhaul] [first-fire] campaign.patch.a\r\n",
        "[Info   :Whiskey Realism — Strategic AI Overhaul] [first-fire] tactical.patch.b.prefix\n",
        "[Warning:Whiskey Realism — Strategic AI Overhaul] [once:x] reflection lookup failed\n",
        "[Warning:   BepInEx] Plugin skipped\n",
        "[Error  :Whiskey Realism — Strategic AI Overhaul] boom\n",
        "[Error  : Unity Log] NullReferenceException: Object reference not set\n",
        "  at AIBattle.Update () [0x00000] in <abc>:0\n",
        "[Info   :Whiskey Realism — Strategic AI Overhaul] errorBudget=3 InvalidOperationException seen\n",
    ]

    def test_collects_fired_ids_and_strips_crlf(self):
        fired, _ = smoke.scan_log(self.LOG)
        self.assertEqual({"campaign.patch.a", "tactical.patch.b.prefix"}, fired)

    def test_counts_levels_exceptions_and_whiskey_lines(self):
        _, counts = smoke.scan_log(self.LOG)
        self.assertEqual(2, counts["warning"])
        self.assertEqual(2, counts["error"])
        self.assertEqual(2, counts["exception"])
        self.assertEqual(1, counts["whiskey_warning"])
        self.assertEqual(1, counts["whiskey_error"])


class Check(unittest.TestCase):
    def test_splits_expected_and_situational_and_flags_stale_entries(self):
        declared = {"campaign.patch.a": "A.cs", "campaign.patch.wl-camp.x": "W.cs", "tactical.patch.b": "B.cs"}
        entries = smoke.parse_situational(
            "# header\ncampaign.patch.wl-camp.*   # W&L only\ncampaign.patch.gone  # removed patch\n")
        expected, situational, unknown, stale = smoke.check(declared, {"campaign.patch.a", "old.id"}, entries)
        self.assertEqual(["tactical.patch.b"], expected)
        self.assertEqual([("campaign.patch.wl-camp.x", "W&L only")], situational)
        self.assertEqual(["old.id"], unknown)
        self.assertEqual(["campaign.patch.gone"], stale)

    def test_shipped_allowlist_matches_declared_ids(self):
        here = os.path.dirname(os.path.abspath(__file__))
        declared = smoke.declared_patch_ids(smoke.read_sources(os.path.join(here, "..")))
        with open(os.path.join(here, "smoke-situational.txt"), encoding="utf-8") as f:
            entries = smoke.parse_situational(f.read())
        _, _, _, stale = smoke.check(declared, set(), entries)
        self.assertEqual([], stale)
        self.assertTrue(all(reason for _, reason in entries), "every allowlist entry has a reason")


if __name__ == "__main__":
    unittest.main()
