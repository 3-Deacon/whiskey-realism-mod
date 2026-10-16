using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Util;
using static TestHarness;
using static TestFixtures;

// Perf slice F4 (#38 CoordinatedOffensiveOperationsPatch): the per-call indexes and the pooled candidate
// assembly against the code they replace. (F9's target-area cache for #2 went with its objective-id lookup
// when G5a rewrote the patch on the published plan.)
internal static class CampaignHookPerfTests
{
    public static void Run()
    {
        RunAll(new (string, Action)[]
        {
            ("coord offensive id index matches FindUnitById", RegimentIdIndexMatchesFindUnitById),
            ("coord offensive membership matches List<Regiment>.Contains", ReferenceMembershipMatchesListContains),
            ("coord offensive pooled candidate fill writes every field", PooledCandidateFillWritesEveryField),
            ("coord offensive candidate assembly matches the old loop", CandidateAssemblyMatchesOldLoop),
            ("coord offensive cheap vanilla precheck is exact", CheapVanillaPrecheckIsExact),
            ("wl bridge ClassifyOnly context overload matches request path", ClassifyOnlyContextOverloadMatchesRequestPath),
            ("coord offensive patch binds delegates and indexes once per call", CoordinatedOffensivePatchSourcePins),
        });
    }

    // --- Fake Unity objects ---------------------------------------------------------
    // UnityEngine.Object (this game's CoreModule): Unity == null and Equals go through
    // CompareBaseObjects, which tests only m_CachedPtr for liveness and otherwise
    // compares references - all managed, so an uninitialized Regiment is a destroyed
    // unit and one with a non-zero m_CachedPtr a live one. GetInstanceID returns 0 for
    // a zero m_CachedPtr, else the int at m_CachedPtr + OffsetOfInstanceIDInCPlusPlusObject;
    // net8 cannot compile it (it references an engine internal call), so the fixture
    // swaps CoordinatedOperationRuntime.InstanceIdOf for that same read (offset 0).
    private sealed class FakeUnits : IDisposable
    {
        private static readonly FieldInfo CachedPtr = typeof(UnityEngine.Object)
            .GetField("m_CachedPtr", BindingFlags.Instance | BindingFlags.NonPublic);
        private readonly List<IntPtr> _memory = new List<IntPtr>();
        private readonly Func<UnityEngine.Object, int> _savedReader;

        internal FakeUnits()
        {
            _savedReader = CoordinatedOperationRuntime.InstanceIdOf;
            CoordinatedOperationRuntime.InstanceIdOf = Id;
        }

        internal static int Id(UnityEngine.Object unit)
        {
            var ptr = (IntPtr)CachedPtr.GetValue(unit);
            return ptr == IntPtr.Zero ? 0 : Marshal.ReadInt32(ptr);
        }

        internal Regiment Live(int id)
        {
            var unit = (Regiment)RuntimeHelpers.GetUninitializedObject(typeof(Regiment));
            IntPtr cell = Marshal.AllocHGlobal(sizeof(int));
            Marshal.WriteInt32(cell, id);
            _memory.Add(cell);
            CachedPtr.SetValue(unit, cell);
            return unit;
        }

        internal Regiment Destroyed()
        {
            return (Regiment)RuntimeHelpers.GetUninitializedObject(typeof(Regiment));
        }

        public void Dispose()
        {
            CoordinatedOperationRuntime.InstanceIdOf = _savedReader;
            foreach (var cell in _memory) Marshal.FreeHGlobal(cell);
            _memory.Clear();
        }
    }

    private static void RegimentIdIndexMatchesFindUnitById()
    {
        using (var fakes = new FakeUnits())
        {
            var a = fakes.Live(11);
            var b = fakes.Live(22);
            var bTwin = fakes.Live(22); // same id twice: the first in list order wins
            var c = fakes.Live(33);
            var gone = fakes.Destroyed();
            AssertTrue(a != null && gone == null, "fixture: live vs destroyed under Unity ==");
            AssertEqual(11, FakeUnits.Id(a), "fixture: live id");
            AssertEqual(0, FakeUnits.Id(gone), "fixture: destroyed id");

            var lists = new List<IList>
            {
                new List<Regiment> { a, null, gone, b, bTwin, c },
                new List<Regiment> { gone, bTwin, b },
                new List<Regiment>(),
                new ArrayList { "not a regiment", a, 7, gone, c },
                null,
            };
            var index = new RegimentIdIndex();
            foreach (var list in lists)
            {
                index.Index(list);
                foreach (int id in new[] { 0, 11, 22, 33, 44, -1 })
                {
                    var expected = CoordinatedOperationRuntime.FindUnitById(list, id);
                    var actual = index.FindUnitById(id);
                    AssertTrue(ReferenceEquals(expected, actual),
                        "id " + id + " in list of " + (list?.Count.ToString() ?? "null"));
                }
            }
            index.Index(lists[0]);
            index.Clear();
            AssertTrue(index.FindUnitById(11) == null, "cleared index finds nothing");
        }
    }

    // Probes include a destroyed unit and a second reference carrying the same id as a
    // listed unit: vanilla's List<Regiment>.Contains (CompareBaseObjects reference
    // compare) says no to it, and so must the index.
    private static void ReferenceMembershipMatchesListContains()
    {
        using (var fakes = new FakeUnits())
        {
            var a = fakes.Live(11);
            var b = fakes.Live(22);
            var c = fakes.Live(33);
            var aTwin = fakes.Live(11);
            var gone = fakes.Destroyed();
            var goneOther = fakes.Destroyed();
            var outsider = fakes.Live(44);
            var probes = new object[] { a, b, c, aTwin, gone, goneOther, outsider };

            var lists = new List<List<Regiment>>
            {
                new List<Regiment> { a, null, gone, b, b },
                new List<Regiment> { null },
                new List<Regiment> { goneOther, c },
                new List<Regiment>(),
            };
            var membership = new ReferenceMembership();
            foreach (var list in lists)
            {
                membership.Fill(list);
                foreach (var probe in probes)
                {
                    bool expected = ((IList)list).Contains(probe);
                    AssertEqual(expected, membership.Contains(probe), "probe in list of " + list.Count);
                }
                AssertFalse(membership.Contains(null), "null probe");
            }
            membership.Fill(null);
            AssertFalse(membership.Contains(a), "null list holds nothing");
            membership.Fill(lists[0]);
            membership.Clear();
            AssertFalse(membership.Contains(a), "cleared");
        }
    }

    private static readonly FieldInfo[] CandidateFields =
        typeof(CoordinatedOperationCandidate).GetFields(BindingFlags.Instance | BindingFlags.Public);

    private static void AssertSameCandidate(CoordinatedOperationCandidate expected, CoordinatedOperationCandidate actual, string label)
    {
        foreach (var field in CandidateFields)
            AssertEqual(field.GetValue(expected), field.GetValue(actual), label + "." + field.Name);
    }

    private static FormationDirectiveAssignment Assignment(int id, int seed)
    {
        return new FormationDirectiveAssignment
        {
            UnitKey = "unit-" + id,
            AllianceId = seed % 2,
            StableUnitId = id,
            Level = (FormationLevel)(seed % 3),
            SectorKey = "sector-" + seed,
            X = seed * 1.5f,
            Z = seed * -2.25f,
            Directive = (FormationDirective)(seed % 5),
            CombatAvailability = 100f + seed,
            ExchangePressure = 0.1f * seed,
            LocalFriendlySupport = 2f * seed,
            LocalEnemyStrength = 3f * seed,
            Readiness = 0.6f,
            Morale = 0.7f,
            Ammo = 0.8f,
            Supply = 0.9f,
            Fatigue = 0.05f * seed,
            OffensiveAllowed = seed % 2 == 0,
            DefensiveAllowed = seed % 3 == 0,
            TransferDonorAllowed = seed % 4 == 0,
            DirectMovementAllowed = seed % 5 != 0,
            InheritsFromParent = seed % 7 == 0,
        };
    }

    private static void PooledCandidateFillWritesEveryField()
    {
        var dirty = new CoordinatedOperationCandidate();
        foreach (var field in CandidateFields)
        {
            if (field.FieldType == typeof(int)) field.SetValue(dirty, 987);
            else if (field.FieldType == typeof(float)) field.SetValue(dirty, 98.7f);
            else if (field.FieldType == typeof(bool)) field.SetValue(dirty, true);
            else if (field.FieldType == typeof(string)) field.SetValue(dirty, "stale");
            else if (field.FieldType.IsEnum) field.SetValue(dirty, Enum.ToObject(field.FieldType, 3));
            else throw new InvalidOperationException("unhandled candidate field type " + field.FieldType);
        }
        for (int seed = 0; seed < 12; seed++)
        {
            var assignment = Assignment(100 + seed, seed);
            var mode = (CoordinatedCommitMode)(seed % 3);
            var expected = CoordinatedOperationRuntime.CandidateFromAssignment(assignment, seed % 2 == 0, seed % 3 == 0, seed % 5 == 0, mode);
            var actual = CoordinatedOperationRuntime.FillCandidateFromAssignment(dirty, assignment, seed % 2 == 0, seed % 3 == 0, seed % 5 == 0, mode);
            AssertTrue(ReferenceEquals(dirty, actual), "fills in place");
            AssertSameCandidate(expected, actual, "seed " + seed);
        }
    }

    private sealed class StubGate : ICoordinatedCandidateGate
    {
        internal Func<Regiment, bool> Available;
        internal Func<Regiment, CoordinatedCommitMode> Mode;
        internal readonly List<Regiment> Calls = new List<Regiment>();

        public bool IsAvailable(Regiment unit)
        {
            Calls.Add(unit);
            return Available(unit);
        }

        public CoordinatedCommitMode CommitMode(Regiment unit)
        {
            return Mode(unit);
        }
    }

    // The loop #38 ran before the scratch: FindUnitById per assignment, availability,
    // bridge mode, then IList.Contains on each operation list.
    private static List<CoordinatedOperationCandidate> OldAssembly(
        IReadOnlyList<FormationDirectiveAssignment> assignments, IList ownUnits,
        IList offensive, IList defensive, IList depots, ICoordinatedCandidateGate gate)
    {
        var candidates = new List<CoordinatedOperationCandidate>();
        for (int i = 0; i < assignments.Count; i++)
        {
            var assignment = assignments[i];
            var unit = CoordinatedOperationRuntime.FindUnitById(ownUnits, assignment.StableUnitId);
            if (unit == null) continue;
            if (!gate.IsAvailable(unit)) continue;
            var mode = gate.CommitMode(unit);
            candidates.Add(CoordinatedOperationRuntime.CandidateFromAssignment(
                assignment,
                OldListContains(offensive, unit),
                OldListContains(defensive, unit),
                OldListContains(depots, unit),
                mode));
        }
        return candidates;
    }

    private static bool OldListContains(IList list, Regiment unit)
    {
        return list != null && unit != null && list.Contains(unit);
    }

    private static void CandidateAssemblyMatchesOldLoop()
    {
        using (var fakes = new FakeUnits())
        {
            var units = new List<Regiment>();
            for (int id = 1; id <= 12; id++) units.Add(fakes.Live(id));
            var twin = fakes.Live(5);
            var gone = fakes.Destroyed();
            var own = new List<Regiment>(units) { null, gone, twin };
            var offensive = new List<Regiment> { units[0], units[3], twin, null };
            var defensive = new List<Regiment> { units[4], gone };
            var depots = new List<Regiment> { units[7], units[7] };

            var scratch = new CoordinatedOffensiveScratch();
            for (int round = 0; round < 3; round++)
            {
                // Round 1 has fewer candidates than round 0, so the pool is reused dirty.
                var assignments = new List<FormationDirectiveAssignment>();
                int[] ids = round == 1 ? new[] { 3, 99, 5 } : new[] { 1, 2, 3, 4, 5, 5, 0, 99, 6, 7, 8, 9, 10, 11, 12 };
                for (int i = 0; i < ids.Length; i++) assignments.Add(Assignment(ids[i], i + round));

                var gate = new StubGate
                {
                    Available = u => FakeUnits.Id(u) % 4 != round,
                    Mode = u => (CoordinatedCommitMode)((FakeUnits.Id(u) + round) % 3),
                };
                var expected = OldAssembly(assignments, own, offensive, defensive, depots, gate);
                var oldCalls = new List<Regiment>(gate.Calls);
                gate.Calls.Clear();

                scratch.Units.Index(own);
                scratch.Offensive.Fill(offensive);
                scratch.Defensive.Fill(defensive);
                scratch.Depots.Fill(depots);
                scratch.AssembleCandidates(assignments, gate);

                AssertEqual(expected.Count, scratch.Candidates.Count, "round " + round + " count");
                for (int i = 0; i < expected.Count; i++)
                    AssertSameCandidate(expected[i], scratch.Candidates[i], "round " + round + " #" + i);
                AssertEqual(oldCalls.Count, gate.Calls.Count, "round " + round + " gate calls");
                for (int i = 0; i < oldCalls.Count; i++)
                    AssertTrue(ReferenceEquals(oldCalls[i], gate.Calls[i]), "round " + round + " gate call order " + i);
                scratch.Clear();
                AssertEqual(0, scratch.Candidates.Count, "cleared candidates");
            }
        }
    }

    // #38 checks the gates that need no list first, then indexes the three operation
    // lists only if those pass. That is exact: with the lead in no list the pure gate
    // reduces to the cheap gates, and lead membership can only make it stricter.
    private static void CheapVanillaPrecheckIsExact()
    {
        foreach (int count in new[] { -1, 0, 1, 5 })
        foreach (float timediff in new[] { -0.1f, 0f, 0.001f, 1f, float.NaN })
        for (int bits = 0; bits < 256; bits++)
        {
            bool present = (bits & 1) != 0, active = (bits & 2) != 0, recruiting = (bits & 4) != 0;
            bool inOff = (bits & 8) != 0, inDef = (bits & 16) != 0, inDepot = (bits & 32) != 0;
            bool full = CoordinatedOffensiveVanillaGates.VanillaWouldEvaluate(count, timediff, present, active, recruiting, inOff, inDef, inDepot);
            bool cheap = CoordinatedOffensiveVanillaGates.VanillaWouldEvaluate(count, timediff, present, active, recruiting, false, false, false);
            bool staged = cheap && CoordinatedOffensiveVanillaGates.VanillaWouldEvaluate(count, timediff, present, active, recruiting, inOff, inDef, inDepot);
            AssertEqual(full, staged, "count=" + count + " timediff=" + timediff + " bits=" + bits);
        }
    }

    // FactsFor reads Unity transforms, which net8 cannot compile, so the request path
    // and the context overload are pinned by source: the request path is the overload
    // with a fresh capture, and #38 captures once per call (the old loop captured per
    // assignment; nothing in the loop changes the W&L player's chain).
    private static void ClassifyOnlyContextOverloadMatchesRequestPath()
    {
        string bridge = ReadRepoFile("src/WhiskeyRealism/Strategic/WlStrategicOrderBridge.cs");
        string request = MethodBody(bridge, "internal static WlStrategicOrderDecision ClassifyOnly(WlStrategicOrderRequest request)");
        AssertContains(request, "if (request == null) return InvalidRequest();", "null request fails closed");
        AssertContains(request, "return ClassifyOnly(WlChainContext.Capture(), request.AllianceId, request.Unit, request.Intent);", "request path = overload with a fresh capture");
        string overload = MethodBody(bridge, "internal static WlStrategicOrderDecision ClassifyOnly(in WlChainContext context,");
        AssertContains(overload, "return Classify(intent, FactsFor(in context, allianceId, unit));", "overload = old Classify(intent, BuildFacts(request))");
        string buildFacts = MethodBody(bridge, "private static WlStrategicRoleFacts BuildFacts(");
        AssertContains(buildFacts, "return FactsFor(WlChainContext.Capture(), request.AllianceId, request.Unit);", "BuildFacts captures and reads the same three request fields");

        string patch = ReadRepoFile("src/WhiskeyRealism/Patches/CoordinatedOffensiveOperationsPatch.cs");
        int captures = 0;
        for (int at = patch.IndexOf("WlChainContext.Capture()", StringComparison.Ordinal); at >= 0; at = patch.IndexOf("WlChainContext.Capture()", at + 1, StringComparison.Ordinal))
            captures++;
        AssertEqual(1, captures, "#38 captures the chain context in one place");
        string commitMode = MethodBody(patch, "public CoordinatedCommitMode CommitMode(");
        AssertContains(commitMode, "if (!_chainCaptured)", "captured at most once per Begin");
        string begin = MethodBody(patch, "internal void Begin(");
        AssertContains(begin, "_chainCaptured = false;", "each call captures afresh");
    }

    private static string MethodBody(string text, string signature)
    {
        int start = text.IndexOf(signature, StringComparison.Ordinal);
        AssertTrue(start >= 0, "found " + signature);
        int brace = text.IndexOf('{', start);
        int depth = 0;
        for (int i = brace; i < text.Length; i++)
        {
            if (text[i] == '{') depth++;
            else if (text[i] == '}' && --depth == 0) return text.Substring(start, i - start + 1);
        }
        return text.Substring(start);
    }

    private static void CoordinatedOffensivePatchSourcePins()
    {
        string patch = ReadRepoFile("src/WhiskeyRealism/Patches/CoordinatedOffensiveOperationsPatch.cs");
        AssertFalse(patch.Contains(".Invoke(null"), "#38 makes no reflected Invoke");
        AssertFalse(patch.Contains("new WlStrategicOrderRequest"), "#38 builds no bridge request per assignment");
        AssertFalse(patch.Contains("FindUnitById(ownUnits"), "#38 does not scan ownunits per lookup");
        AssertFalse(patch.Contains("ListContains("), "#38 does not scan the operation lists per lookup");
        AssertContains(patch, "Func<Vector3, AICampaign.AIArea, int, bool, Vector3> _closestObjectivePos", "GetClosestObjectivePosOfArea bound to a typed delegate");
        AssertContains(patch, "WlStrategicOrderBridge.ClassifyOnly(in _chain", "the chain context is captured once per call");

        string prefix = MethodBody(patch, "internal static void Prefix(");
        int finallyAt = prefix.LastIndexOf("finally", StringComparison.Ordinal);
        AssertTrue(finallyAt >= 0 && prefix.IndexOf("Scratch.Clear();", finallyAt, StringComparison.Ordinal) > finallyAt,
            "the Prefix clears its scratch in a finally");
        AssertTrue(prefix.IndexOf("Gate.End();", finallyAt, StringComparison.Ordinal) > finallyAt, "and drops the chain context");

        string evaluate = MethodBody(patch, "private static bool VanillaWouldEvaluate(");
        int cheap = evaluate.IndexOf("false, false, false", StringComparison.Ordinal);
        int fill = evaluate.IndexOf("Scratch.Offensive.Fill", StringComparison.Ordinal);
        AssertTrue(cheap >= 0 && fill > cheap, "cheap gates run before any list is indexed");

        string build = MethodBody(patch, "private static PackageFilterDecision BuildAllowedSet(");
        int target = build.IndexOf("if (!vanillaTarget.HasValue)", StringComparison.Ordinal);
        int index = build.IndexOf("Scratch.Units.Index(ownUnits)", StringComparison.Ordinal);
        AssertTrue(target >= 0 && index > target, "ownunits is indexed only once a target resolved");

        string wrapper = ReadRepoFile("src/WhiskeyRealism/Strategic/OffensiveAvailabilityWrapper.cs");
        string isAvailable = MethodBody(wrapper, "public static bool IsAvailable(");
        AssertFalse(isAvailable.Contains(".Invoke("), "the vanilla gate is called through a delegate");
        int catchAt = isAvailable.IndexOf("catch (Exception", StringComparison.Ordinal);
        int mirror = isAvailable.IndexOf("return MirrorVanillaGates(", StringComparison.Ordinal);
        AssertTrue(catchAt >= 0 && mirror > catchAt, "a throwing vanilla gate still falls back to the mirror");
        AssertContains(wrapper, "Func<int, Regiment, Vector3, bool, bool, bool> _vanillaGate", "typed gate delegate");
    }
}
