using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using static TestFixtures;

// The type-aware half of the planner read census (PlannerReadCensusTests; plan § G7, verify F2).
//
// A grep for `.Member` cannot tell `crossing.X` from `area.X`. This class decides, for one reader file and one census type
// T, which receiver expressions are "a T", so the census only counts `recv.Member` when recv is one. It has no compiler,
// so it resolves a receiver the way a reader of the file would, from declarations the file (or any source file) states:
//
//   T-names (identifiers that hold one T)
//     1. a declaration `T x`, `T? x`, `T[] x`, `C<T> x` for the collection types below (locals, fields, parameters, in/out/ref
//        and typed foreach variables), the name not followed by `(`;
//     2. `var x = new T...` or `var x = new C<T>...` (x is a T-collection);
//     3. `var x = ...n(` / `var x = ...n;` where a member or method named n is declared anywhere in src to return T
//        (or a T-collection: then x is a collection);
//     4. an argument `out var x` at position k of a call to a method that declares `out T` at position k;
//     5. `foreach (var x in E)`, `var x = E[i]` and `var x = E.First/Last/Single...(`, where E ends in a T-collection;
//     6. a lambda parameter `x =>` on a line that already names a T-collection.
//   T-collections are identifiers declared `T[]`/`C<T>`, plus members and methods declared in src with such a type.
//
// A read of `Member` counts when its receiver is `T` itself (a static or an enum value), a T-name, a member or method
// declared to return T (`s.Own[i].Name`: `Own` is a collection and is indexed; `plan.Main.X`: `Main` is declared T), or a
// T-collection indexed with `[..]`. Identifiers are matched by name across the file (the same name declared with two types in
// two methods is both), so the check can over-accept a reuse of one identifier; it cannot accept a member of an unrelated type
// whose receiver is never declared as a T, which is the failure the plain `.Member` match had (verify G7 P12).
// Enum values always need `T.Value`.
internal static class CensusReceiverTypes
{
    private static readonly string Collections = @"(?:List|IList|IReadOnlyList|IEnumerable|ICollection|IReadOnlyCollection|HashSet|Queue|Stack|Dictionary<[^<>]*?,\s*|SortedList<[^<>]*?,\s*|SortedDictionary<[^<>]*?,\s*)";

    // ---- the global declarations index ----

    private static Dictionary<string, HashSet<string>> _declared;          // member or method name -> its declared types
    private static Dictionary<string, HashSet<(int index, string type)>> _outs; // method name -> (position, T) of every `out T`

    private static void EnsureIndex()
    {
        if (_declared != null) return;
        var declared = new Dictionary<string, HashSet<string>>();
        var outs = new Dictionary<string, HashSet<(int, string)>>();
        var member = new Regex(@"^(?:public|internal|private|protected)\s+(?:(?:static|readonly|override|virtual|abstract|sealed|new|extern)\s+)*(?<type>[\w<>\[\],.? ]+?)\s+(?<name>\w+)\s*(?:\(|\{|=>|=|;)");
        foreach (string path in Directory.GetFiles(FindRepoPath("src/WhiskeyRealism"), "*.cs", SearchOption.AllDirectories))
        {
            foreach (string line in PlannerReadCensusTests.CodeLines(File.ReadAllText(path)))
            {
                string t = line.Trim();
                var m = member.Match(t);
                if (m.Success)
                {
                    string type = m.Groups["type"].Value.Trim();
                    if (type != "class" && type != "struct" && type != "enum" && type != "return" && type != "new" && type != "delegate")
                    {
                        string name = m.Groups["name"].Value;
                        if (!declared.TryGetValue(name, out var set)) declared[name] = set = new HashSet<string>();
                        set.Add(type);
                    }
                }
                var call = Regex.Match(t, @"^(?:public|internal|private|protected)\b.*?\b(\w+)\s*\((.*)\)");
                if (call.Success && call.Groups[2].Value.Contains("out "))
                {
                    var args = SplitTopLevel(call.Groups[2].Value);
                    for (int i = 0; i < args.Count; i++)
                    {
                        var o = Regex.Match(args[i], @"^\s*out\s+(\w+)");
                        if (!o.Success) continue;
                        string method = call.Groups[1].Value;
                        if (!outs.TryGetValue(method, out var set)) outs[method] = set = new HashSet<(int, string)>();
                        set.Add((i, o.Groups[1].Value));
                    }
                }
            }
        }
        _declared = declared;
        _outs = outs;
    }

    private static List<string> SplitTopLevel(string args)
    {
        var parts = new List<string>();
        int depth = 0;
        var cur = new System.Text.StringBuilder();
        foreach (char ch in args)
        {
            if (ch == '(' || ch == '<' || ch == '[' || ch == '{') depth++;
            else if (ch == ')' || ch == '>' || ch == ']' || ch == '}') depth--;
            if (ch == ',' && depth == 0) { parts.Add(cur.ToString()); cur.Clear(); }
            else cur.Append(ch);
        }
        parts.Add(cur.ToString());
        return parts;
    }

    // ---- the per-file environment ----

    internal sealed class Env
    {
        public readonly HashSet<string> Names = new HashSet<string>();        // identifiers that hold one T
        public readonly HashSet<string> Collections = new HashSet<string>();  // identifiers that hold a collection of T
        public HashSet<string> MemberNames = new HashSet<string>();           // members/methods declared to return T
        public HashSet<string> MemberCollections = new HashSet<string>();     // members/methods declared to return a collection of T
    }

    private static readonly Dictionary<string, Env> EnvCache = new Dictionary<string, Env>();

    internal static Env EnvFor(string fileText, string cacheKey, string type)
    {
        EnsureIndex();
        string key = cacheKey + "|" + type;
        if (EnvCache.TryGetValue(key, out var cached)) return cached;
        var env = Build(fileText, type);
        EnvCache[key] = env;
        return env;
    }

    private static bool IsExactly(string declared, string type)
    {
        return Regex.IsMatch(declared, @"^\s*" + type + @"\??\s*$");
    }

    private static bool IsCollectionOf(string declared, string type)
    {
        if (IsExactly(declared, type)) return false;
        return Regex.IsMatch(declared, @"(?<![\w.])" + type + @"(?![\w])") && (declared.Contains("[]") || declared.Contains("<"));
    }

    private static Env Build(string text, string type)
    {
        var env = new Env();
        var lines = PlannerReadCensusTests.CodeLines(text).ToList();
        foreach (var kv in _declared)
        {
            if (kv.Value.Any(d => IsExactly(d, type))) env.MemberNames.Add(kv.Key);
            if (kv.Value.Any(d => IsCollectionOf(d, type))) env.MemberCollections.Add(kv.Key);
        }

        var declName = new Regex(@"(?<![\w.])(?:" + type + @"\??(?:\[\])?|" + Collections + @"<\s*" + type + @"\s*>)\s+(?<n>\w+)\b(?!\s*\()");
        var declColl = new Regex(@"(?<![\w.])(?:" + type + @"\[\]|" + Collections + @"<\s*" + type + @"\s*>)\s+(?<n>\w+)\b(?!\s*\()");
        var newVar = new Regex(@"\bvar\s+(?<n>\w+)\s*=\s*new\s+" + type + @"\b");
        var newColl = new Regex(@"\bvar\s+(?<n>\w+)\s*=\s*new\s+" + Collections + @"<\s*" + type + @"\s*>");
        var varFromName = new Regex(@"\bvar\s+(?<n>\w+)\s*=\s*(?:[\w.]+\.)?(?<m>\w+)\s*\(");
        var varFromMember = new Regex(@"\bvar\s+(?<n>\w+)\s*=\s*[\w.]*?\b(?<m>\w+)\s*;");
        var foreachVar = new Regex(@"foreach\s*\(\s*(?:var|" + type + @")\s+(?<n>\w+)\s+in\s+(?<e>[^)]+)\)");
        var varIndexed = new Regex(@"\bvar\s+(?<n>\w+)\s*=\s*(?<e>.+?)\[[^\]]*\]\s*;");
        var varFirst = new Regex(@"\bvar\s+(?<n>\w+)\s*=\s*(?<e>.+?)\.(?:First|Last|FirstOrDefault|LastOrDefault|Single|SingleOrDefault)\w*\(");
        var lambda = new Regex(@"\b(?<n>\w+)\s*=>");

        foreach (string l in lines)
        {
            foreach (Match m in declName.Matches(l)) env.Names.Add(m.Groups["n"].Value);
            foreach (Match m in declColl.Matches(l)) env.Collections.Add(m.Groups["n"].Value);
            var nv = newVar.Match(l); if (nv.Success) env.Names.Add(nv.Groups["n"].Value);
            var nc = newColl.Match(l); if (nc.Success) env.Collections.Add(nc.Groups["n"].Value);
        }

        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach (string l in lines)
            {
                var m = varFromName.Match(l);
                if (m.Success)
                {
                    if (env.MemberNames.Contains(m.Groups["m"].Value)) changed |= env.Names.Add(m.Groups["n"].Value);
                    if (env.MemberCollections.Contains(m.Groups["m"].Value)) changed |= env.Collections.Add(m.Groups["n"].Value);
                }
                m = varFromMember.Match(l);
                if (m.Success)
                {
                    if (env.MemberNames.Contains(m.Groups["m"].Value)) changed |= env.Names.Add(m.Groups["n"].Value);
                    if (env.MemberCollections.Contains(m.Groups["m"].Value)) changed |= env.Collections.Add(m.Groups["n"].Value);
                }
                m = foreachVar.Match(l);
                if (m.Success && IsCollectionExpr(env, m.Groups["e"].Value)) changed |= env.Names.Add(m.Groups["n"].Value);
                m = varIndexed.Match(l);
                if (m.Success && IsCollectionExpr(env, m.Groups["e"].Value)) changed |= env.Names.Add(m.Groups["n"].Value);
                m = varFirst.Match(l);
                if (m.Success && IsCollectionExpr(env, m.Groups["e"].Value)) changed |= env.Names.Add(m.Groups["n"].Value);
                foreach (Match call in Regex.Matches(l, @"\b(?<c>\w+)\s*\("))
                {
                    if (!_outs.TryGetValue(call.Groups["c"].Value, out var positions)) continue;
                    int start = call.Index + call.Length, depth = 1, end = start;
                    while (end < l.Length && depth > 0) { if (l[end] == '(') depth++; else if (l[end] == ')') depth--; end++; }
                    var args = SplitTopLevel(l.Substring(start, Math.Max(0, end - start - 1)));
                    for (int k = 0; k < args.Count; k++)
                    {
                        var ov = Regex.Match(args[k], @"^\s*out\s+var\s+(\w+)");
                        if (ov.Success && positions.Contains((k, type))) changed |= env.Names.Add(ov.Groups[1].Value);
                    }
                }
                foreach (Match lm in lambda.Matches(l))
                {
                    string before = l.Substring(0, lm.Index);
                    bool names = env.Collections.Any(c => Regex.IsMatch(before, @"\b" + Regex.Escape(c) + @"\b"))
                              || env.MemberCollections.Any(c => Regex.IsMatch(before, @"\b" + Regex.Escape(c) + @"\b"));
                    if (names) changed |= env.Names.Add(lm.Groups["n"].Value);
                }
            }
        }
        return env;
    }

    // The expression ends in a T-collection: an identifier declared as one, or `.Member` / `.Method(...)` declared to return one.
    private static bool IsCollectionExpr(Env env, string expr)
    {
        var m = Regex.Match(expr.Trim(), @"(\w+)\s*(?:\([^()]*\))?\s*$");
        if (!m.Success) return false;
        string w = m.Groups[1].Value;
        return env.Collections.Contains(w) || env.MemberCollections.Contains(w);
    }

    // ---- the read test ----

    // A read of `type.member` in `text`: `recv.member` that is not a plain assignment, with recv resolving to a `type`.
    internal static bool ReadsMember(string text, string cacheKey, string type, string member)
    {
        var env = EnvFor(text, cacheKey, type);
        var rx = new Regex(@"(?<recv>[\w\]\[\)\(.]*?)\." + Regex.Escape(member) + @"\b(?!\s*=[^=])");
        foreach (string line in PlannerReadCensusTests.CodeLines(text))
        {
            foreach (Match m in rx.Matches(line))
            {
                string left = line.Substring(0, m.Index + m.Groups["recv"].Length);
                if (ReceiverIs(env, left, type)) return true;
            }
        }
        return false;
    }

    // An enum value is read as `Enum.Value`.
    internal static bool ReadsEnumValue(string text, string type, string value)
    {
        var rx = new Regex(@"(?<![\w.])" + type + @"\." + Regex.Escape(value) + @"\b");
        return PlannerReadCensusTests.CodeLines(text).Any(l => rx.IsMatch(l));
    }

    // Limit (verify G7 r2 n2): only the receivers the environment resolves are followed. A write through a local alias of a part of
    // a published object (`var list = plan.Sectors; list.Add(x);`, or a copied element variable) is not seen unless the alias is
    // itself declared or inferred as `type`; such a patch needs a reviewer's eye.
    // The first line in `text` that writes through a receiver of `type` (an assignment to a member or to an element, a compound
    // assignment, ++/--, or a mutating collection call on a member chain), or null. Used by the patches' read-only pin.
    internal static string FindWrite(string text, string cacheKey, string type)
    {
        var env = EnvFor(text, cacheKey, type);
        var holders = env.Names.Union(env.Collections).ToList();
        if (holders.Count == 0) return null;
        string alt = string.Join("|", holders.Select(Regex.Escape));
        // holder, an optional index, then at least one `.member` step: a write THROUGH the holder (rebinding the holder itself is not one)
        string chain = @"(?<![\w.])(?:" + alt + @")(?:\[[^\]]*\])?(?:\.\w+(?:\[[^\]]*\])?)+";
        var assign = new Regex(chain + @"\s*(?:=(?!=)|\+=|-=|\*=|/=|%=|\|=|&=|\^=|<<=|>>=|\+\+|--)");
        var mutate = new Regex(chain + @"\.(?:Add|AddRange|Remove|RemoveAt|RemoveAll|Clear|Insert|InsertRange|Sort|Reverse)\(");
        var declaration = new Regex(@"(?<![\w.])(?:" + type + @"\??(?:\[\])?|" + Collections + @"<\s*" + type + @"\s*>)\s+\w+\s*=(?!=)");
        foreach (string line in PlannerReadCensusTests.CodeLines(text))
        {
            string probe = declaration.Replace(line, "");   // `TheaterPlan plan = ...` declares, it does not write
            if (assign.IsMatch(probe) && !Regex.IsMatch(probe, @"^\s*(?:var|" + type + @")\s+\w+\s*=[^=]")) return line.Trim();
            if (mutate.IsMatch(probe)) return line.Trim();
        }
        return null;
    }

    internal static bool ReceiverIs(Env env, string left, string type)
    {
        string trimmed = left.TrimEnd();
        if (Regex.IsMatch(trimmed, @"(?<![\w.])" + type + @"$")) return true;   // T.Member
        bool indexed = trimmed.EndsWith("]", StringComparison.Ordinal);
        var tail = Regex.Match(trimmed, @"(\w+)\s*(?:\([^()]*\))?\s*(?:\[[^\]]*\])?$");
        if (!tail.Success) return false;
        string w = tail.Groups[1].Value;
        bool dotted = Regex.IsMatch(trimmed, @"\.\s*" + Regex.Escape(w) + @"\s*(?:\([^()]*\))?\s*(?:\[[^\]]*\])?$");
        if (env.Names.Contains(w)) return true;
        if (indexed && env.Collections.Contains(w)) return true;
        if (dotted && env.MemberNames.Contains(w)) return true;
        if (dotted && indexed && env.MemberCollections.Contains(w)) return true;
        return false;
    }
}
