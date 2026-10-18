using System;
using System.IO;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Tactical.Orchestrator;
using WhiskeyRealism.Tactical.Orchestrator.Plays;
using static TestHarness;
using static TestFixtures;

// BC14W task 1: the reader table (spec § 15 item 7). FocalAssault and its two rect kinds are read by the catalog, the allocator, the corps
// assignment and the play executor, each with an explicit value (none falls through to a default that was written for another plan).
internal static partial class ManeuverFeasibilityTests
{
    static (string, Action)[] WorksReaderTests() => new (string, Action)[]
    {
        ("bc14w READER-AFFINITY FocalAssault is a frontal assault: no delaying affinity, 0.10 envelopment affinity, an offensive response target", WReaderAffinity),
        ("bc14w READER-AXIS FocalAssault may take a provisional attack axis, like the frontal plays", WReaderAxis),
        ("bc14w READER-ROLE the column maps to Main and the support to SupportMain", WReaderRole),
        ("bc14w READER-MANEUVEROF the column's zone carries a full maneuver, the support's a start delay held for release", WReaderManeuverOf),
        ("bc14w READER-MIRROR a template with one enveloping rect mirrors, FocalAssault's column does not, and the play executor reads the seam", WReaderMirror),
        ("bc14w FOCAL-TEMPLATE the template is five rects centred on the focal point, the column enveloping", WFocalTemplate),
    };

    static void WReaderAffinity()
    {
        AssertNear(0f, TacticalPlaybookCatalog.DelayingAffinity(BattlePlanId.FocalAssault), 1e-6f, "delaying affinity (the default for an unnamed plan is 0.20)");
        AssertNear(0.10f, TacticalPlaybookCatalog.EnvelopmentAffinity(BattlePlanId.FocalAssault), 1e-6f, "envelopment affinity (the default is 0.20)");
        AssertNear(0.6f, TacticalPlaybookCatalog.ResponseTargetFor(BattlePlanId.FocalAssault), 1e-6f, "response target (the default is 0.4)");
    }

    static void WReaderAxis()
    {
        var plan = new TacticalBattlePlan(BattlePlanId.FocalAssault, BattlePhase.Probe, 2, null, null, 1.2f, 0f, 7);
        AssertTrue(DirectChildAllocator.AllowsProvisionalAttackAxis(plan), "FocalAssault may fall back to a provisional attack axis");
        var consolidating = new TacticalBattlePlan(BattlePlanId.FocalAssault, BattlePhase.Consolidate, 2, null, null, 1.2f, 0f, 7);
        AssertFalse(DirectChildAllocator.AllowsProvisionalAttackAxis(consolidating), "control: not while consolidating");
    }

    static void WReaderRole()
    {
        AssertEqual(DirectChildRole.Main, PlayCorpsAssignment.RoleForRectKind(PlayRectKind.AssaultColumn), "the column is the Main role");
        AssertEqual(DirectChildRole.SupportMain, PlayCorpsAssignment.RoleForRectKind(PlayRectKind.AssaultSupport), "the support is SupportMain");
    }

    static void WReaderManeuverOf()
    {
        var column = new CorpsAssignment("child-1", PlayRectKind.AssaultColumn, 0, 100f, 200f, 0f, envelops: true, templateEnvelops: true);
        ManeuverSpec columnSpec = DirectChildAllocator.ManeuverOf(in column);
        AssertTrue(columnSpec.HasValue && columnSpec.Envelops, "the column is a full enveloping maneuver");
        AssertEqual(PlayRectKind.AssaultColumn, columnSpec.Kind, "of kind AssaultColumn");

        var support = new CorpsAssignment("child-2", PlayRectKind.AssaultSupport, 1, 100f, 150f, 0f, envelops: false, templateEnvelops: true);
        ManeuverSpec supportSpec = DirectChildAllocator.ManeuverOf(in support);
        AssertTrue(supportSpec.HasValue, "the support carries a spec");
        AssertFalse(supportSpec.Envelops, "that is a start delay, not an envelopment");
        AssertTrue(supportSpec.HoldsForRelease, "held for the column's strike");
        AssertEqual(PlayRectKind.AssaultSupport, supportSpec.Kind, "of kind AssaultSupport");
    }

    static void WReaderMirror()
    {
        AssertTrue(PlayExecutorRules.CanMirror(PlayTemplateCatalog.Lookup(BattlePlanId.ManeuverAndFix)), "control: ManeuverAndFix has one enveloping rect and may mirror");
        AssertTrue(PlayExecutorRules.CanMirror(PlayTemplateCatalog.Lookup(BattlePlanId.WideFlankApproach)), "control: WideFlankApproach may mirror");
        AssertFalse(PlayExecutorRules.CanMirror(PlayTemplateCatalog.Lookup(BattlePlanId.DoubleEnvelopment)), "control: two enveloping rects have nothing to flip to");
        AssertFalse(PlayExecutorRules.CanMirror(PlayTemplateCatalog.Lookup(BattlePlanId.FocalAssault)),
            "FocalAssault holds one enveloping rect (the column) but is centred on its focal point and never mirrored");
        AssertFalse(PlayExecutorRules.CanMirror(null), "no template, no mirror");
        string runtime = File.ReadAllText(FindRepoPath("src/WhiskeyRealism/Tactical/Orchestrator/Plays/PlayExecutorRuntime.cs"));
        AssertContains(runtime, "PlayExecutorRules.CanMirror(template)", "the play executor reads the seam");
        AssertFalse(runtime.Contains("envelopRects == 1"), "and carries no copy of the rule");
    }

    static void WFocalTemplate()
    {
        PlayTemplate template = PlayTemplateCatalog.Lookup(BattlePlanId.FocalAssault);
        AssertTrue(template != null, "the catalog knows FocalAssault");
        AssertEqual(5, template.Rects.Count, "five rects");
        int column = 0, support = 0, demonstration = 0, reserve = 0, envelops = 0;
        foreach (PlayRect r in template.Rects)
        {
            if (r.Envelops) envelops++;
            if (r.StartTimeOffsetSeconds != 0f) throw new Exception("no rect has a start offset: the timing is the releases'");
            switch (r.Kind)
            {
                case PlayRectKind.AssaultColumn: column++; AssertNear(0.6f, r.RelativeAnchorY, 1e-6f, "the column stands forward"); AssertTrue(r.Envelops, "the column envelops"); break;
                case PlayRectKind.AssaultSupport: support++; AssertNear(0.45f, r.RelativeAnchorY, 1e-6f, "the support stands behind the column"); break;
                case PlayRectKind.Demonstration: demonstration++; AssertNear(0.7f, Math.Abs(r.RelativeAnchorX), 1e-6f, "the demonstrations stand on the wings"); break;
                case PlayRectKind.Reserve: reserve++; break;
                default: throw new Exception("unexpected rect " + r.Kind);
            }
        }
        AssertEqual(1, column, "one column"); AssertEqual(1, support, "one support"); AssertEqual(2, demonstration, "two demonstrations"); AssertEqual(1, reserve, "one reserve");
        AssertEqual(1, envelops, "exactly one enveloping rect");
    }
}
