#!/usr/bin/env python3
"""Report source members nothing in production code references.

Heuristic, name-based: a declared type, method, property, field or enum member
under src/ whose name appears nowhere else in src/ (outside its own declaration
line) is ORPHAN; one referenced only from tests/ is TEST-ONLY. Harmony entry
points, Unity messages, overrides and serialized DTO fields are skipped. Name
collisions hide orphans, so an empty report is necessary, not sufficient.

Usage: tools/orphan-sweep.py [repo-root] [--include-test-only]
Exit 1 when anything is reported.
"""
import glob
import os
import re
import sys

SKIP_NAMES = {
    "Prefix", "Postfix", "Finalizer", "Transpiler", "TargetMethod", "TargetMethods",
    "Prepare", "Cleanup", "Awake", "Start", "Update", "LateUpdate", "FixedUpdate",
    "OnDestroy", "OnEnable", "OnDisable", "OnGUI", "OnApplicationQuit",
    "ToString", "Equals", "GetHashCode", "Dispose", "Compare", "CompareTo",
    "Main", "Run", "GetEnumerator", "MoveNext", "Reset", "Current",
}
KEYWORDS = {
    "if", "for", "foreach", "while", "switch", "return", "new", "using", "catch",
    "lock", "get", "set", "value", "base", "this", "typeof", "nameof", "sizeof",
    "default", "var", "else", "case", "throw", "in", "out", "ref", "is", "as",
}
MEMBER_RE = re.compile(
    r"^\s*(?:\[[^\]]*\]\s*)*"
    r"(?:(?:public|internal|private|protected|static|readonly|sealed|abstract|virtual|"
    r"partial|const|volatile|unsafe|extern|new)\s+)+"
    r"(?:(?P<kind>class|struct|enum|interface)\s+(?P<type>[A-Za-z_]\w*)"
    r"|[\w<>\[\],.? ]+?\s+(?P<member>[A-Za-z_]\w*)\s*(?:[({;=]|=>|$))"
)
ENUM_MEMBER_RE = re.compile(r"^\s*(?P<name>[A-Z]\w*)\s*(?:=\s*[^,]+)?,?\s*(?://.*)?$")
DTO_FILE_RE = re.compile(r"(Dto|Persistence)", re.I)


def strip_comments(text):
    text = re.sub(r"/\*.*?\*/", lambda m: "\n" * m.group(0).count("\n"), text, flags=re.S)
    return re.sub(r"//[^\n]*", "", text)


def read(path):
    with open(path, encoding="utf-8", errors="replace") as f:
        return f.read()


def declarations(path, text):
    lines = text.split("\n")
    in_enum = False
    depth_at_enum = 0
    depth = 0
    for i, line in enumerate(lines):
        stripped = line.strip()
        if in_enum and depth == depth_at_enum + 1:
            m = ENUM_MEMBER_RE.match(line)
            if m and not stripped.startswith("{"):
                yield i, m.group("name"), "enum-member"
        m = MEMBER_RE.match(line)
        if m:
            if " override " in " " + line + " ":
                pass
            elif m.group("type"):
                yield i, m.group("type"), m.group("kind")
                if m.group("kind") == "enum":
                    in_enum = True
                    depth_at_enum = depth
            elif m.group("member") and m.group("member") not in KEYWORDS:
                yield i, m.group("member"), "member"
        depth += line.count("{") - line.count("}")
        if in_enum and depth <= depth_at_enum and "}" in line:
            in_enum = False


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    include_test_only = "--include-test-only" in sys.argv
    root = os.path.abspath(args[0] if args else os.path.join(os.path.dirname(__file__), ".."))
    def sources(sub):
        found = glob.glob(os.path.join(root, sub, "**", "*.cs"), recursive=True)
        return sorted(p for p in found if not re.search(r"[\\/](obj|bin)[\\/]", p))

    src_files = sources("src")
    test_files = sources("tests")
    src_text = {p: strip_comments(read(p)) for p in src_files}
    test_blob = "\n".join(strip_comments(read(p)) for p in test_files)

    token_counts = {}
    for text in src_text.values():
        for tok in re.findall(r"[A-Za-z_]\w*", text):
            token_counts[tok] = token_counts.get(tok, 0) + 1
    test_tokens = set(re.findall(r"[A-Za-z_]\w*", test_blob))
    # Reflection/Harmony string references ("MethodName") keep a member alive.
    string_tokens = set()
    for text in src_text.values():
        for lit in re.findall(r'"([A-Za-z_]\w*)"', text):
            string_tokens.add(lit)

    findings = []
    for path, text in src_text.items():
        is_dto = bool(DTO_FILE_RE.search(os.path.basename(path)))
        lines = text.split("\n")
        for line_no, name, kind in declarations(path, text):
            if name in SKIP_NAMES or name in string_tokens:
                continue
            if is_dto and kind in ("member", "enum-member"):
                continue
            if kind == "class" and "[HarmonyPatch" in text:
                continue
            preceding = "\n".join(lines[max(0, line_no - 3):line_no + 1])
            if "[Harmony" in preceding:
                continue
            own = len(re.findall(r"\b%s\b" % re.escape(name), lines[line_no]))
            if token_counts.get(name, 0) - own > 0:
                continue
            rel = os.path.relpath(path, root)
            status = "TEST-ONLY" if name in test_tokens else "ORPHAN"
            if status == "TEST-ONLY" and not include_test_only:
                continue
            findings.append((status, rel, line_no + 1, kind, name))

    for status, rel, line, kind, name in sorted(findings):
        print("%-9s %s:%d %s %s" % (status, rel, line, kind, name))
    print("orphan-sweep: %d finding(s)" % len(findings))
    return 1 if findings else 0


if __name__ == "__main__":
    sys.exit(main())
