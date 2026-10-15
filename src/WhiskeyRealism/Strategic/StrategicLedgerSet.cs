using WhiskeyRealism.Strategic.Construction;
using WhiskeyRealism.Strategic.Fiscal;
using WhiskeyRealism.Strategic.Policies;

namespace WhiskeyRealism.Strategic
{
    // The per-alliance strategic state patches read. StrategicCoordinator keeps two sets: the
    // working set the daily review builds in place, in the same order as ever, and the
    // published set every reader outside a review job sees. PublishFrom copies the working set
    // into the published one when a day's review completes, so a reader between the slices
    // of a time-sliced review sees the whole previous day, never half of today.
    // Ledgers the review replaces by reference (fronts, army areas, fiscal, construction,
    // defense, needs, map) are shared, never mutated after they are built. State the review
    // mutates in place (the CIC and its plan, the era, the director memory, the policy plan,
    // the formation directives with their probe overlay) is copied, so the next day's
    // in-place work cannot leak into the published copy.
    public sealed class StrategicLedgerSet
    {
        public readonly CIC[] CICs = new CIC[2];
        public readonly EraStageManager[] Eras = new EraStageManager[2];
        public readonly FrontSectorLedger[] Fronts = new FrontSectorLedger[2];
        public readonly ArmyAreaLedger[] ArmyAreas = new ArmyAreaLedger[2];
        public readonly FormationDirectiveLedger[] FormationDirectives = new FormationDirectiveLedger[2];
        public readonly FiscalOutput[] FiscalIntents = new FiscalOutput[2];
        public readonly ConstructionOutput[] ConstructionIntents = new ConstructionOutput[2];
        public readonly DefenseIntentLedgerOutput[] DefenseIntents = new DefenseIntentLedgerOutput[2];
        // Stage E need-driven policy: per-alliance smoothed needs, the snapshot they came from
        // (ProjectSelectionPatch signals) and the ranked policy plan PolicySelectionPatch reads.
        public readonly NeedVector[] Needs = new NeedVector[2];
        public readonly NeedSnapshot[] NeedSnapshots = new NeedSnapshot[2];
        public readonly PolicyPlanLedger[] PolicyPlans = { new PolicyPlanLedger(), new PolicyPlanLedger() };
        public readonly DirectorMemory[] DirectorMemories = { new DirectorMemory(), new DirectorMemory() };
        public CampaignMapLedger CampaignMap = CampaignMapLedger.Build(null);

        public void PublishFrom(StrategicLedgerSet working)
        {
            if (working == null || ReferenceEquals(working, this)) return;
            for (int a = 0; a < 2; a++)
            {
                CICs[a] = working.CICs[a]?.CloneForPublish();
                Eras[a] = working.Eras[a] != null ? new EraStageManager { Stage = working.Eras[a].Stage } : null;
                Fronts[a] = working.Fronts[a];
                ArmyAreas[a] = working.ArmyAreas[a];
                FormationDirectives[a] = working.FormationDirectives[a]?.Clone();
                FiscalIntents[a] = working.FiscalIntents[a];
                ConstructionIntents[a] = working.ConstructionIntents[a];
                DefenseIntents[a] = working.DefenseIntents[a];
                Needs[a] = working.Needs[a];
                NeedSnapshots[a] = working.NeedSnapshots[a];
                PolicyPlans[a] = working.PolicyPlans[a]?.Clone();
                DirectorMemories[a] = working.DirectorMemories[a]?.Clone();
            }
            CampaignMap = working.CampaignMap;
        }
    }
}
