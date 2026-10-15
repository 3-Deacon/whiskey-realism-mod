#!/usr/bin/env python3
"""Audit Plugin.cs config entries.

For every `Field = Config.Bind(section, key, default, description...)`:
  UNREAD     the field is never read outside its own Bind statement
  DUPLICATE  the same section/key is bound more than once
  DESC       the description states a default that contradicts the code default
             ("Default ON"/"default-on" on a false bool, "Default OFF" on a true
             bool, or "Default <n>" that differs from a numeric default)

Usage: tools/config-audit.py [repo-root]   (exit 1 when anything is reported)
"""
import glob
import os
import re
import sys


def read(path):
    with open(path, encoding="utf-8", errors="replace") as f:
        return f.read()


def split_args(text, start):
    """Split the argument list of the call whose '(' is at text[start]."""
    depth, args, cur, i, in_str = 0, [], [], start, False
    while i < len(text):
        c = text[i]
        if in_str:
            cur.append(c)
            if c == "\\":
                cur.append(text[i + 1])
                i += 1
            elif c == '"':
                in_str = False
        elif c == '"':
            in_str = True
            cur.append(c)
        elif c in "([{":
            depth += 1
            if depth > 1:
                cur.append(c)
        elif c in ")]}":
            depth -= 1
            if depth == 0:
                args.append("".join(cur).strip())
                return args, i
            cur.append(c)
        elif c == "," and depth == 1:
            args.append("".join(cur).strip())
            cur = []
        else:
            cur.append(c)
        i += 1
    return args, i


def literal_text(expr):
    return "".join(re.findall(r'"((?:[^"\\]|\\.)*)"', expr))


def main():
    root = os.path.abspath(sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(__file__), ".."))
    plugin_path = os.path.join(root, "src", "WhiskeyRealism", "Plugin.cs")
    plugin = read(plugin_path)
    others = [p for p in glob.glob(os.path.join(root, "src", "**", "*.cs"), recursive=True)
              if os.path.abspath(p) != plugin_path and not re.search(r"[\\/](obj|bin)[\\/]", p)]
    other_text = "\n".join(read(p) for p in others)

    findings = []
    seen = {}
    spans = []
    for m in re.finditer(r"(\w+)\s*=\s*Config\.Bind\s*(?:<[^>]*>)?\s*\(", plugin):
        field = m.group(1)
        args, end = split_args(plugin, m.end() - 1)
        spans.append((m.start(), end, field, args))

    for start, end, field, args in spans:
        line = plugin.count("\n", 0, start) + 1
        if len(args) < 3:
            continue
        section, key, default = literal_text(args[0]), literal_text(args[1]), args[2].strip()
        description = literal_text(",".join(args[3:]))

        ident = (section, key)
        if ident in seen:
            findings.append(("DUPLICATE", line, field, "%s/%s also bound at line %d" % (section, key, seen[ident])))
        else:
            seen[ident] = line

        outside_bind = plugin[:start] + plugin[end:]
        reads = len(re.findall(r"\b%s\b" % re.escape(field), outside_bind))
        # The field declaration itself is one occurrence in Plugin.cs.
        if reads <= 1 and not re.search(r"\b%s\b" % re.escape(field), other_text):
            findings.append(("UNREAD", line, field, "%s/%s has no reader" % (section, key)))

        lowered = description.lower()
        if default in ("true", "false"):
            says_on = re.search(r"default[\s:-]*(on|true|enabled)\b", lowered)
            says_off = re.search(r"default[\s:-]*(off|false|disabled)\b", lowered)
            if default == "false" and says_on and not says_off:
                findings.append(("DESC", line, field, "default false but description says '%s'" % says_on.group(0)))
            if default == "true" and says_off and not says_on:
                findings.append(("DESC", line, field, "default true but description says '%s'" % says_off.group(0)))
        else:
            num = re.match(r"^-?\d+(\.\d+)?f?$", default)
            stated = re.search(r"default[\s:=]*(-?\d+(?:\.\d+)?)", lowered)
            if num and stated and float(default.rstrip("f")) != float(stated.group(1)):
                findings.append(("DESC", line, field, "default %s but description says %s" % (default, stated.group(1))))

    for kind, line, field, msg in sorted(findings, key=lambda f: (f[0], f[1])):
        print("%-9s Plugin.cs:%d %s — %s" % (kind, line, field, msg))
    print("config-audit: %d finding(s) over %d entries" % (len(findings), len(spans)))
    return 1 if findings else 0


if __name__ == "__main__":
    sys.exit(main())
