using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Telemetry;
using static TestHarness;
using static TestFixtures;

// BC7: battle-core integration (docs/superpowers/plans/2026-10-15-battle-core.md § BC7).
// The producer registry: BattleCoreTick builds every intent producer from one
// explicit list in plan order, so no producer depends on its own static
// constructor having run.
internal static class BattleCoreIntegrationTests
{
    public static void Run()
    {
        RunAll(new (string, Action)[]
        {
            ("battle-core ProducerRegistryOrderIsThePlanOrder", ProducerRegistryOrderIsThePlanOrder),
            ("battle-core ProducerRegistryBuildsEveryNameInOrder", ProducerRegistryBuildsEveryNameInOrder),
            ("battle-core ProducerRegistryDropsMissingAndMisnamedProducers", ProducerRegistryDropsMissingAndMisnamedProducers),
            ("battle-core ProducerRegistrySurvivesAThrowingFactory", ProducerRegistrySurvivesAThrowingFactory),
            ("battle-core RuntimeRegistryBuildsEveryProducerExplicitly", RuntimeRegistryBuildsEveryProducerExplicitly),
            ("battle-core GroupReachIsWidestCombatSightOrCancelDistance", GroupReachIsWidestCombatSightOrCancelDistance),
            ("battle-core ArbiterStatsRowReachesTheMainLog", ArbiterStatsRowReachesTheMainLog),
            ("battle-core DiscardedLegacyRowsRouteToTheSidecar", DiscardedLegacyRowsRouteToTheSidecar),
            ("battle-core ArbiterStandsDownWhenThePluginIsDisabled", ArbiterStandsDownWhenThePluginIsDisabled),
            ("battle-core RetreatReleaseOnlyWhenWritesAllowed", RetreatReleaseOnlyWhenWritesAllowed),
        });
    }

    private static readonly string[] PlanOrder =
    {
        "Withdrawal", "ArtilleryDoctrine", "ArtilleryPointBlank", "GroupStance", "ChargeDemotion", "Posture",
        "FacingPulse", "FireControl", "DivisionPlay", "ReserveRelease", "Charge",
    };

    private sealed class NamedProducer : IBattleIntentProducer
    {
        public NamedProducer(string name) { Name = name; }
        public string Name { get; }
        public void Produce(BattleSnapshot snapshot, IIntentSink sink) { }
    }

    // Plan BC5 order without "Play": the geometric play is no mover (BC5a As built).
    private static void ProducerRegistryOrderIsThePlanOrder()
    {
        AssertEqual(PlanOrder.Length, BattleProducerRegistry.Order.Count, "producer count");
        for (int i = 0; i < PlanOrder.Length; i++)
            AssertEqual(PlanOrder[i], BattleProducerRegistry.Order[i], "slot " + i);
    }

    private static void ProducerRegistryBuildsEveryNameInOrder()
    {
        var asked = new List<string>();
        var registry = new BattleProducerRegistry(name =>
        {
            asked.Add(name);
            return new NamedProducer(name);
        });

        AssertTrue(registry.HasProducers, "registry built from the list has producers");
        AssertEqual(PlanOrder.Length, registry.Count, "every producer built");
        AssertEqual(PlanOrder.Length, asked.Count, "the factory is asked once per name");
        for (int i = 0; i < PlanOrder.Length; i++)
        {
            AssertEqual(PlanOrder[i], asked[i], "asked in order, slot " + i);
            AssertEqual(PlanOrder[i], registry[i].Name, "registered in order, slot " + i);
        }
        AssertEqual(string.Join(",", PlanOrder), registry.Names, "producer line order");
        AssertEqual("", registry.Missing, "nothing missing");
    }

    private static void ProducerRegistryDropsMissingAndMisnamedProducers()
    {
        var registry = new BattleProducerRegistry(name =>
        {
            if (name == "Posture") return null;
            if (name == "Charge") return new NamedProducer("Charged");
            return new NamedProducer(name);
        });

        AssertEqual(PlanOrder.Length - 2, registry.Count, "missing and misnamed producers are not registered");
        AssertEqual("Posture,Charge", registry.Missing, "missing names reported in order");
        for (int i = 0; i < registry.Count; i++)
        {
            AssertTrue(registry[i].Name != "Posture" && registry[i].Name != "Charged", "slot " + i + " holds no bad producer");
        }
        AssertEqual("ReserveRelease", registry[registry.Count - 1].Name, "order kept around the gaps");
    }

    // The runtime registry is built in BattleCoreTick's type initializer: a
    // producer constructor that throws must not take the tick (and with it the
    // snapshot every battle reader uses) down.
    private static void ProducerRegistrySurvivesAThrowingFactory()
    {
        var registry = new BattleProducerRegistry(name =>
        {
            if (name == "GroupStance") throw new InvalidOperationException("boom");
            return new NamedProducer(name);
        });

        AssertEqual(PlanOrder.Length - 1, registry.Count, "the throwing producer is skipped");
        AssertEqual("GroupStance", registry.Missing, "and reported");
    }

    // BattleCoreTick and the producers are runtime files (not in this csproj), so
    // the runtime wiring is pinned at the source: the tick builds its registry
    // through one factory with exactly one case per plan name, each case builds
    // a producer whose class declares that name, and no producer registers
    // itself from a static constructor any more.
    private static void RuntimeRegistryBuildsEveryProducerExplicitly()
    {
        string tick = File.ReadAllText(FindRepoPath("src/WhiskeyRealism/Tactical/Battle/BattleCoreTick.cs"));
        AssertTrue(tick.Contains("new BattleProducerRegistry(CreateProducer)"), "BattleCoreTick builds the registry from its factory");

        var cases = new List<KeyValuePair<string, string>>();
        foreach (Match m in Regex.Matches(tick, "case \"(\\w+)\":\\s*return new (\\w+)\\("))
            cases.Add(new KeyValuePair<string, string>(m.Groups[1].Value, m.Groups[2].Value));
        AssertEqual(PlanOrder.Length, cases.Count, "one factory case per producer");

        string producersDir = FindRepoPath("src/WhiskeyRealism/Tactical/Battle/Producers");
        var sources = new List<string>();
        foreach (string path in Directory.GetFiles(producersDir, "*.cs")) sources.Add(File.ReadAllText(path));

        for (int i = 0; i < PlanOrder.Length; i++)
        {
            AssertEqual(PlanOrder[i], cases[i].Key, "factory case order, slot " + i);
            AssertTrue(DeclaresName(sources, cases[i].Value, PlanOrder[i]), cases[i].Value + " is a producer class named " + PlanOrder[i]);
        }

        string src = FindRepoPath("src/WhiskeyRealism");
        foreach (string path in Directory.GetFiles(src, "*.cs", SearchOption.AllDirectories))
        {
            string text = File.ReadAllText(path);
            AssertFalse(text.Contains("RegisterProducer("), Path.GetFileName(path) + " still self-registers a producer");
            AssertFalse(text.Contains("EnsureRegistered("), Path.GetFileName(path) + " still touches producer static constructors");
        }
    }

    // TelemetryRouter.LegacyInfo writes only to the sidecar; for a tag with no
    // sidecar route (the default route: main log allowed, no sidecar) it returns
    // true and writes nothing, leaving the main-log write to the caller
    // (LegacyInfoToMainLogIfAllowed, OnceLog). The [OrderArbiter] 30 s row has no
    // sidecar route, so it must go through LegacyInfoToMainLogIfAllowed.
    private static void ArbiterStatsRowReachesTheMainLog()
    {
        const string row = "[OrderArbiter] alliance=0 intents=4 winners=3 issued=2 moved=1 failed=0 retried=0 gaveUp=0 skipped=1 deduped=0 superseded=0 topSkip=Cooldown";
        TelemetryTagRoute route = TelemetryTagPolicy.Route(row);
        AssertTrue(route.RouteToSidecar || route.AllowMainLog, "the arbiter row has a sink");
        string tick = File.ReadAllText(FindRepoPath("src/WhiskeyRealism/Tactical/Battle/BattleCoreTick.cs"));
        AssertTrue(tick.Contains("TelemetryRouter.LegacyInfoToMainLogIfAllowed(\"[OrderArbiter] alliance=\""),
            "the arbiter row is written to the main log when its route allows it");
    }

    // A statement-level LegacyInfo call discards the main-log answer, so its row
    // is written only if its tag routes to the sidecar. Every such call with a
    // literal tag in the battle code must name a sidecar-routed tag.
    private static void DiscardedLegacyRowsRouteToTheSidecar()
    {
        int checkedCalls = 0;
        foreach (string dir in new[] { "src/WhiskeyRealism/Tactical", "src/WhiskeyRealism/Patches" })
        {
            foreach (string path in Directory.GetFiles(FindRepoPath(dir), "*.cs", SearchOption.AllDirectories))
            {
                string text = File.ReadAllText(path);
                foreach (Match m in Regex.Matches(text, @"(?m)^\s*TelemetryRouter\.LegacyInfo\("))
                {
                    int end = text.IndexOf(';', m.Index);
                    string call = end > m.Index ? text.Substring(m.Index, end - m.Index) : text.Substring(m.Index);
                    Match tag = Regex.Match(call, "\"\\[([A-Za-z&][^\\]\"]*)\\]");
                    if (!tag.Success) continue;
                    checkedCalls++;
                    TelemetryTagRoute route = TelemetryTagPolicy.Route("[" + tag.Groups[1].Value + "] x=1");
                    AssertTrue(route.RouteToSidecar,
                        Path.GetFileName(path) + ": [" + tag.Groups[1].Value + "] goes through LegacyInfo but has no sidecar route, so it is written nowhere");
                }
            }
        }
        AssertTrue(checkedCalls > 10, "the scan found the battle code's LegacyInfo rows");
    }

    // The master Enabled switch can be turned off at runtime (Plugin.cs Enabled:
    // "short-circuit every patch"); the arbiter tick then stands its side down
    // (claims cleared) whatever the mode and the battle state say.
    private static void ArbiterStandsDownWhenThePluginIsDisabled()
    {
        AssertEqual(ArbiterTickAction.Run,
            PostureExecutorBattleGate.ArbiterAction(true, true, 5, 1, 0, 0, false), "enabled, Active, battle running");
        AssertEqual(ArbiterTickAction.StandDown,
            PostureExecutorBattleGate.ArbiterAction(false, true, 5, 1, 0, 0, false), "plugin disabled");
        AssertEqual(ArbiterTickAction.StandDown,
            PostureExecutorBattleGate.ArbiterAction(true, false, 5, 1, 0, 0, false), "MonitorOnly/Off");
        AssertEqual(ArbiterTickAction.StandDown,
            PostureExecutorBattleGate.ArbiterAction(true, true, 4, 1, 0, 0, false), "battle not started");
        AssertEqual(ArbiterTickAction.StandDown,
            PostureExecutorBattleGate.ArbiterAction(true, true, 5, 1, 0, 1, false), "player instance outside ai_vs_ai");
    }

    // Army retreat (macroai 3) hands Whiskey-held stances back to vanilla, which
    // lowers lastaistancechangetime: a write, so only when writes are allowed.
    private static void RetreatReleaseOnlyWhenWritesAllowed()
    {
        AssertEqual(ArbiterTickAction.StandDownAndReleaseRetreat,
            PostureExecutorBattleGate.ArbiterAction(true, true, 5, 3, 0, 0, false), "Active retreat releases");
        AssertEqual(ArbiterTickAction.StandDown,
            PostureExecutorBattleGate.ArbiterAction(true, false, 5, 3, 0, 0, false), "MonitorOnly retreat writes nothing");
        AssertEqual(ArbiterTickAction.StandDown,
            PostureExecutorBattleGate.ArbiterAction(false, true, 5, 3, 0, 0, false), "disabled retreat writes nothing");
    }

    // One reach for a group's enemies-in-range (#44 sector strength and the
    // executor's formation policy): vanilla's enemiesinrange list reach
    // max(maximumlineofsight[unittyp], distancetoenemytocancelinterruption)
    // (InitMaxUnitRanges 122510, UpdateUnitRangeFast 122608), widest over the
    // combat types a group holds (infantry, cavalry, artillery, skirmishers).
    private static void GroupReachIsWidestCombatSightOrCancelDistance()
    {
        float[] sight = { 100f, 300f, 200f, 250f, 5000f };
        AssertNear(300f, BattlePerception.GroupRangeReach(sight, 150f), 0.001f, "widest combat line of sight");
        AssertNear(400f, BattlePerception.GroupRangeReach(sight, 400f), 0.001f, "cancel-interruption distance wins");
        AssertNear(150f, BattlePerception.GroupRangeReach(null, 150f), 0.001f, "no sight table");
        AssertNear(150f, BattlePerception.GroupRangeReach(new[] { 120f }, 150f), 0.001f, "short table");
    }

    // Some declaration of the class (it may be partial) carries `Name => "X"`, or
    // the name literal its constructor picks (the artillery producer serves two
    // slots from one class), inside that declaration's own braces.
    private static bool DeclaresName(List<string> sources, string type, string name)
    {
        foreach (string text in sources)
        {
            foreach (Match m in Regex.Matches(text, "class " + type + "\\b"))
            {
                string body = ClassBody(text, m.Index);
                if (body.Contains("Name => \"" + name + "\"") || body.Contains("? \"" + name + "\"") || body.Contains(": \"" + name + "\""))
                    return true;
            }
        }
        return false;
    }

    private static string ClassBody(string text, int classAt)
    {
        int open = text.IndexOf('{', classAt);
        if (open < 0) return string.Empty;
        int depth = 0;
        for (int i = open; i < text.Length; i++)
        {
            if (text[i] == '{') depth++;
            else if (text[i] == '}' && --depth == 0) return text.Substring(open, i - open + 1);
        }
        return text.Substring(open);
    }
}
