using WhiskeyRealism.Tactical.Orchestrator;
using WhiskeyRealism.Telemetry;

namespace WhiskeyRealism.Tactical.Battle.Producers
{
    // #45 group stance on the order arbiter (plan BC5b; replaces the deleted
    // BattleGroupStancePatch Postfix on AIBattle.AdjustGroupAIStance).
    //
    // Vanilla AdjustGroupAIStance (4221-4278) re-decides ai_stanceordered for
    // every pool group once per macro cycle (UpdateAITasks 5869) and writes it
    // with ChangeStance unless lastaistancechangetime holds it (4264). This
    // producer runs in the arbiter tick right after it (AdjustGroupFormations
    // Postfix, 5870) and turns the coordinator's cached brigade decision
    // (TacticalBattleCoordinatorRuntime.PopulateResolvedDecisionsForSide:
    // doctrine, ledger task, sector-odds scorer) into a Scorer-priority Stance
    // intent (BattleWriterPolicy.TryGroupStanceIntent). The order executor
    // writes it through TacticalStanceWriter, which stamps vanilla's 4264 hold.
    // The arbiter tick is the cadence (the old real-time throttle is gone), and
    // the army-retreat hand-back (macroai 3) is the arbiter patch gate's
    // TacticalStanceWriter.ReleaseForRetreat.
    //
    // Reads: snapshot.Own (roster, stance, settle state, ownership flags) and the
    // coordinator cache. The cache is keyed by the brigade's Regiment instance id
    // (Component.GetInstanceID, as the coordinator fills it), the snapshot by its
    // GameObject id, so the live unit behind the record supplies the cache key.
    internal sealed class GroupStanceIntentProducer : IBattleIntentProducer
    {
        public string Name => "GroupStance";

        public void Produce(BattleSnapshot snapshot, IIntentSink sink)
        {
            using (TelemetryPerf.Scope(BattleLodRuntime.InWakePass ? "tactical.producer.group-stance.wake" : "tactical.producer.group-stance", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
            {
                if (snapshot == null || sink == null || !PluginEnabled()) return;
                UnitRecord[] own = snapshot.Own;
                for (int i = 0; i < snapshot.OwnCount; i++)
                {
                    if ((own[i].Flags & UnitFlags.IsGroup) == 0 || !TacticalDoctrineScorer.AllowsLocalGroupStanceWriter(own[i].UnitTyp)) continue;
                    // BC15: a Distant group that is not due keeps the stance claim it published; its hold is renewed on its due tick.
                    if (!BattleLodRuntime.IsDue(snapshot, i)) continue;
                    if (!TryResolvedDecision(snapshot, i, out TacticalResolvedGroupDecision resolved)) continue;
                    if (BattleWriterPolicy.TryGroupStanceIntent(own[i], resolved, out BattleOrderIntent intent)) sink.Submit(intent);
                }
            }
        }

        // The coordinator's cached decision for own record `index` of the
        // alliance's current snapshot; false when the brigade has none (no
        // decision = no intent; there is no live re-decide fallback any more).
        internal static bool TryResolvedDecision(BattleSnapshot snapshot, int index, out TacticalResolvedGroupDecision resolved)
        {
            resolved = default(TacticalResolvedGroupDecision);
            Regiment unit = BattleSnapshotBuilder.UnitAt(snapshot, index, enemy: false);
            if ((UnityEngine.Object)unit == null) return false;
            return TacticalBattleCoordinator.TryGetResolvedGroupDecision(snapshot.Alliance, unit.GetInstanceID(), out resolved);
        }

        internal static bool PluginEnabled()
        {
            Plugin plugin = Plugin.Instance;
            return plugin != null && plugin.Enabled != null && plugin.Enabled.Value;
        }
    }
}
