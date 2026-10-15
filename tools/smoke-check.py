#!/usr/bin/env python3
"""Check a BepInEx LogOutput.log against the patch first-fire registry.

Every Harmony patch entry calls PatchFirstFire.Mark("<scope id>"), which logs
one "[first-fire] <id>" line per session. This tool collects the ids from the
Mark calls in src/, reads the log, and reports:
  - patch ids that never fired, split into expected and situational
    (tools/smoke-situational.txt lists the situational ones, with reasons);
  - counts of WARNING / ERROR lines, lines naming an exception, and
    Whiskey Realism warning/error lines.

Usage: tools/smoke-check.py <LogOutput.log> [--root <repo-root>] [--situational <file>]
Exit: 0 all expected patches fired; 1 an expected patch never fired;
      2 usage or source error (e.g. a Mark call whose id is not a string literal).
"""
import argparse
import fnmatch
import glob
import os
import re
import sys

MARK_CALL = re.compile(r"PatchFirstFire\.Mark\(\s*([^)]*?)\s*\)")
STRING_LITERAL = re.compile(r'^"([^"\\]+)"$')
FIRST_FIRE = re.compile(r"\[first-fire\] (\S+)")
# BepInEx 5 log line: "[Warning:Source Name] message" (level padded to 7 chars).
LEVEL = re.compile(r"^\[(Warning|Error|Fatal)\s*:\s*([^\]]*)\]")
EXCEPTION = re.compile(r"\b\w*Exception\b")
WHISKEY_SOURCE = "Whiskey Realism"


class SourceError(Exception):
    pass


def declared_patch_ids(sources):
    """sources: iterable of (path, text). Returns {id: first path}."""
    ids = {}
    for path, text in sources:
        for m in MARK_CALL.finditer(text):
            lit = STRING_LITERAL.match(m.group(1))
            if not lit:
                line = text.count("\n", 0, m.start()) + 1
                raise SourceError("%s:%d: PatchFirstFire.Mark argument is not a string literal: %s"
                                  % (path, line, m.group(1)))
            ids.setdefault(lit.group(1), path)
    return ids


def parse_situational(text):
    """Returns [(pattern, reason)] from the allowlist text."""
    entries = []
    for raw in text.splitlines():
        line = raw.strip()
        if not line or line.startswith("#"):
            continue
        pattern, _, reason = line.partition("#")
        entries.append((pattern.strip(), reason.strip()))
    return entries


def situational_reason(patch_id, entries):
    for pattern, reason in entries:
        if fnmatch.fnmatchcase(patch_id, pattern):
            return reason or "(no reason given)"
    return None


def scan_log(lines):
    """Returns (fired ids, counts dict)."""
    fired = set()
    counts = {"warning": 0, "error": 0, "exception": 0, "whiskey_warning": 0, "whiskey_error": 0}
    for raw in lines:
        line = raw.rstrip("\r\n")
        m = FIRST_FIRE.search(line)
        if m:
            fired.add(m.group(1))
        level = LEVEL.match(line)
        if level:
            is_whiskey = level.group(2).strip().startswith(WHISKEY_SOURCE)
            if level.group(1) == "Warning":
                counts["warning"] += 1
                counts["whiskey_warning"] += is_whiskey
            else:
                counts["error"] += 1
                counts["whiskey_error"] += is_whiskey
        if EXCEPTION.search(line):
            counts["exception"] += 1
    return fired, counts


def check(declared, fired, entries):
    """Returns (expected_missing, situational_missing [(id, reason)], unknown_fired, stale_entries)."""
    expected_missing, situational_missing = [], []
    for patch_id in sorted(declared):
        if patch_id in fired:
            continue
        reason = situational_reason(patch_id, entries)
        if reason is None:
            expected_missing.append(patch_id)
        else:
            situational_missing.append((patch_id, reason))
    unknown_fired = sorted(fired - set(declared))
    stale = [p for p, _ in entries if not any(fnmatch.fnmatchcase(i, p) for i in declared)]
    return expected_missing, situational_missing, unknown_fired, stale


def read_sources(root):
    pattern = os.path.join(root, "src", "WhiskeyRealism", "**", "*.cs")
    for path in sorted(glob.glob(pattern, recursive=True)):
        if re.search(r"[\\/](obj|bin)[\\/]", path):
            continue
        with open(path, encoding="utf-8", errors="replace") as f:
            yield os.path.relpath(path, root), f.read()


def main(argv):
    here = os.path.dirname(os.path.abspath(__file__))
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("log")
    parser.add_argument("--root", default=os.path.join(here, ".."))
    parser.add_argument("--situational", default=os.path.join(here, "smoke-situational.txt"))
    args = parser.parse_args(argv)

    try:
        declared = declared_patch_ids(read_sources(os.path.abspath(args.root)))
    except SourceError as e:
        print("smoke-check: " + str(e), file=sys.stderr)
        return 2
    if not declared:
        print("smoke-check: no PatchFirstFire.Mark calls found under %s" % args.root, file=sys.stderr)
        return 2

    with open(args.situational, encoding="utf-8") as f:
        entries = parse_situational(f.read())
    with open(args.log, encoding="utf-8", errors="replace") as f:
        fired, counts = scan_log(f)

    expected_missing, situational_missing, unknown_fired, stale = check(declared, fired, entries)
    fired_declared = len(set(declared) & fired)

    print("patches: %d declared, %d fired, %d expected never fired, %d situational never fired"
          % (len(declared), fired_declared, len(expected_missing), len(situational_missing)))
    if not fired:
        print("no [first-fire] lines in the log: the deployed DLL predates the first-fire registry, "
              "or the plugin never loaded / the game was never launched")
    for patch_id in expected_missing:
        print("  NEVER FIRED  %s  (%s)" % (patch_id, declared[patch_id]))
    for patch_id, reason in situational_missing:
        print("  situational  %s  -- %s" % (patch_id, reason))
    for patch_id in unknown_fired:
        print("  unknown id fired (log from a different build?)  %s" % patch_id)
    for pattern in stale:
        print("  stale allowlist entry matches no declared id  %s" % pattern)
    print("log: %d WARNING, %d ERROR, %d exception lines; Whiskey Realism: %d warning, %d error"
          % (counts["warning"], counts["error"], counts["exception"],
             counts["whiskey_warning"], counts["whiskey_error"]))
    return 1 if expected_missing else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
