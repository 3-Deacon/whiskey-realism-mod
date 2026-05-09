using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    /// <summary>
    /// BepInEx-coupled partial — excluded from the test assembly.
    ///
    /// Contains the real runtime entry points called from Harmony patches:
    ///   OnBattleStart  — resolves player CIC side, discovers commanders from vanilla
    ///                    BattleUnits, builds roster, calls BuildAndActivate, attaches
    ///                    ArmyOrchestrator per non-suppressed side, picks initial plan,
    ///                    emits bootstrap + plan telemetry.
    ///   OnBattleEnd    — clears inter-battle ledger state, tears down orchestrators.
    ///   Tick           — cascades to per-side orchestrators; emits first-tick once-marker.
    ///
    /// All methods are try/catch guarded — this code runs inside or adjacent to Harmony
    /// patches and must never throw.
    /// </summary>
    public static partial class TacticalBattleCoordinator
    {
        private const float MaxTickDeltaSeconds = 5f;

        // Cached AIBattle.bunits FieldInfo — avoids reflection cost on every battle bootstrap.
        private static FieldInfo _bunitsFieldCache;
        private static float _lastTickTimeSeconds;
        private static int _battleSequence;
        private static readonly HashSet<string> _tickWarningKeys = new HashSet<string>();
        private static readonly HashSet<int> _directChildDeferLogged = new HashSet<int>();

        public static void OnBattleStart(AIBattle battle)
        {
            if (active) return;
            if (!Plugin.EnableTacticalBattleOrchestrator.Value) return;

            try
            {
                int playerCicAllianceId = ResolvePlayerCicAllianceId();
                var commanders = DiscoverCommandersFromVanilla(battle);
                var roster = TacticalCommanderRoster.BuildFromSynthetic(commanders);
                BuildAndActivate(playerCicAllianceId, roster);
                _battleSequence++;

                if (Plugin.EnableTacticalOrchestratorArmy != null && Plugin.EnableTacticalOrchestratorArmy.Value)
                {
                    AttachArmyIfActive(side0, battle);
                    AttachArmyIfActive(side1, battle);
                    AttachDirectChildrenIfReady(side0, battle);
                    AttachDirectChildrenIfReady(side1, battle);
                }

                int suppressed = (playerCicAllianceId == 0 || playerCicAllianceId == 1) ? 1 : 0;
                int activated = 2 - suppressed;
                Plugin.Log.LogInfo("[TacticalCommanderRoster] alliance=0 total=" + roster.GetSide(0).Count
                    + " matched=" + MatchedCount(roster, 0) + " unknown=" + UnknownCount(roster, 0));
                Plugin.Log.LogInfo("[TacticalCommanderRoster] alliance=1 total=" + roster.GetSide(1).Count
                    + " matched=" + MatchedCount(roster, 1) + " unknown=" + UnknownCount(roster, 1));
                foreach (var entry in roster.GetSide(0))
                    if (!entry.MatchedHistoricalRegistry)
                        Plugin.Log.LogInfo("[TacticalCommanderUnknown] echelon=" + entry.Echelon
                            + " name=" + (string.IsNullOrEmpty(entry.Name) ? "<null>" : entry.Name));
                foreach (var entry in roster.GetSide(1))
                    if (!entry.MatchedHistoricalRegistry)
                        Plugin.Log.LogInfo("[TacticalCommanderUnknown] echelon=" + entry.Echelon
                            + " name=" + (string.IsNullOrEmpty(entry.Name) ? "<null>" : entry.Name));
                OnceLog.Info("orch-bootstrap", "[TacticalOrchestrator] bootstrap sidesActive=" + activated
                    + " sidesSuppressed=" + suppressed);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("[TacticalOrchestrator] OnBattleStart skipped: "
                    + e.GetType().Name + " " + e.Message);
                ClearForFailure();
            }
        }

        public static void OnBattleEnd()
        {
            if (!active) return;
            try
            {
                ClearLedgersBetweenBattles();
                ResetRuntimeTickState();
                _directChildDeferLogged.Clear();
                side0 = null;
                side1 = null;
                active = false;
                OnceLog.Info("orch-teardown", "[TacticalOrchestrator] teardown");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("[TacticalOrchestrator] OnBattleEnd partial: "
                    + e.GetType().Name + " " + e.Message);
                ClearForFailure();
            }
        }

        public static void Tick(AIBattle battle)
        {
            if (!active) return;
            try
            {
                OnceLog.Info("orch-coordinator", "[TacticalOrchestrator] coordinator first tick");
                side0?.Tick();
                side1?.Tick();

                if (Plugin.EnableTacticalOrchestratorIntentInference != null
                    && Plugin.EnableTacticalOrchestratorIntentInference.Value)
                {
                    float deltaSeconds = ComputeTickDeltaSeconds();
                    DriveTickCycle(side0, battle, deltaSeconds);
                    DriveTickCycle(side1, battle, deltaSeconds);

                    DriveDirectChildCycle(side0, battle);
                    DriveDirectChildCycle(side1, battle);
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("[TacticalOrchestrator] Tick skipped: "
                    + e.GetType().Name + " " + e.Message);
            }
        }

        // ---- Private runtime helpers ----

        private static float ComputeTickDeltaSeconds()
        {
            try
            {
                float now = UnityEngine.Time.realtimeSinceStartup;
                float delta = _lastTickTimeSeconds <= 0f ? 1f : Math.Max(0f, now - _lastTickTimeSeconds);
                _lastTickTimeSeconds = now;
                return SanitizeDelta(delta);
            }
            catch
            {
                return 1f;
            }
        }

        private static float SanitizeDelta(float deltaSeconds)
        {
            if (deltaSeconds <= 0f || float.IsNaN(deltaSeconds) || float.IsInfinity(deltaSeconds)) return 0f;
            return Math.Min(deltaSeconds, MaxTickDeltaSeconds);
        }

        private static void DriveTickCycle(TacticalBattleOrchestrator side, AIBattle battle, float deltaSeconds)
        {
            try
            {
                if (side == null || side.Army == null || !side.Army.HasPlan) return;

                var bundle = ArmyEvidenceBuilder.Build(battle, side.AllianceId);
                int minReplanSeconds = (Plugin.TacticalOrchestratorMinReplanSeconds != null)
                    ? Plugin.TacticalOrchestratorMinReplanSeconds.Value
                    : 60;

                var trigger = ArmyTickCycle.MaybeReplan(
                    side.Army,
                    deltaSeconds: deltaSeconds,
                    ownEvidence: bundle.OwnEvidence,
                    enemyVisible: bundle.EnemyVisible,
                    ownMainEffortStrength: bundle.OwnMainEffortStrength,
                    ownArmyMorale: bundle.OwnArmyMorale,
                    ownReservesCommittedFraction: bundle.OwnReservesCommittedFraction,
                    reinforcementsArrivingDelta: bundle.ReinforcementsArrivingDelta,
                    minReplanSeconds: minReplanSeconds);

                var intent = side.Army.CurrentIntentModel;
                if (intent.PrimaryIntent != InferredIntent.Unknown)
                {
                    OnceLog.Info("orch-intent:" + _battleSequence + ":" + side.AllianceId + ":" + intent.PrimaryIntent + ":" + intent.InferredMainEffort,
                        "[TacticalIntent] side=" + side.AllianceId
                        + " seesEnemy=" + intent.PrimaryIntent
                        + " mainEffort=" + intent.InferredMainEffort
                        + " confidence=" + intent.Confidence01.ToString("0.00"));
                }

                if (trigger != ReplanTrigger.None)
                {
                    OnceLog.Info("orch-replan:" + _battleSequence + ":" + side.AllianceId + ":" + trigger + ":" + side.Army.CurrentPlan.PlanId,
                        "[TacticalReplan] side=" + side.AllianceId
                        + " trigger=" + trigger
                        + " newPlan=" + side.Army.CurrentPlan.PlanId
                        + " phase=" + side.Army.CurrentPlan.Phase);
                }
            }
            catch (Exception e)
            {
                WarnTickCycleOnce(side, e);
            }
        }

        private static void WarnTickCycleOnce(TacticalBattleOrchestrator side, Exception e)
        {
            try
            {
                string sideKey = side == null ? "null" : side.AllianceId.ToString();
                string key = sideKey + ":" + e.GetType().Name;
                if (_tickWarningKeys.Contains(key)) return;
                _tickWarningKeys.Add(key);
                Plugin.Log.LogWarning("[TacticalOrchestrator] Army tick skipped side="
                    + sideKey + ": " + e.GetType().Name + " " + e.Message);
            }
            catch { }
        }

        private static int ResolvePlayerCicAllianceId()
        {
            try
            {
                if (!DLC_WL.dlc_scenarioactive) return -1;
                if (!DLC_WL.IsCommanderInChief()) return -1;
                int chosen = DLC_WL.dlc_chosencommander;
                if (chosen < 0 || chosen >= GameVars.commander.Count) return -1;
                return GameVars.commander[chosen].alliance;
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning("[TacticalOrchestrator] ResolvePlayerCicAllianceId failed (W&L suppression gate disabled until next battle): " + e.GetType().Name + " " + e.Message);
                return -1;
            }
        }

        private static IEnumerable<SyntheticCommanderInput> DiscoverCommandersFromVanilla(AIBattle battle)
        {
            var inputs = new List<SyntheticCommanderInput>();
            try
            {
                var bunits = ResolveBattleUnits(battle);
                if (bunits == null) return inputs;
                for (int side = 0; side < 2; side++)
                {
                    int allianceId = SafeAllianceForSide(bunits, side);
                    if (allianceId < 0 || allianceId >= 2)
                    {
                        if (allianceId >= 2)
                            Plugin.Log.LogInfo("[TacticalOrchestrator] skipping side=" + side + " alliance=" + allianceId + " (Europe/non-belligerent)");
                        continue;
                    }
                    int commanderId = SafeCommanderId(bunits, side);
                    string name = SafeCommanderName(commanderId);
                    if (string.IsNullOrEmpty(name)) name = "ArmyCO_side" + side;
                    inputs.Add(new SyntheticCommanderInput(name, EchelonKind.Army, allianceId));
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("[TacticalOrchestrator] DiscoverCommandersFromVanilla degraded: "
                    + e.GetType().Name + " " + e.Message);
            }
            return inputs;
        }

        private static BattleUnits ResolveBattleUnits(AIBattle battle)
        {
            try
            {
                if (battle == null) return null;
                if (_bunitsFieldCache == null)
                    _bunitsFieldCache = AccessTools.Field(typeof(AIBattle), "bunits");
                return _bunitsFieldCache?.GetValue(battle) as BattleUnits;
            }
            catch
            {
                return null;
            }
        }

        private static int SafeAllianceForSide(BattleUnits bunits, int side)
        {
            try
            {
                if (bunits == null || bunits.alliance == null) return -1;
                if (side < 0 || side >= bunits.alliance.Length) return -1;
                return bunits.alliance[side];
            }
            catch
            {
                return -1;
            }
        }

        private static int SafeCommanderId(BattleUnits bunits, int side)
        {
            try
            {
                if (bunits == null) return -1;
                return bunits.GetCommandingOfficerFromSide(side);
            }
            catch
            {
                return -1;
            }
        }

        private static string SafeCommanderName(int commanderId)
        {
            try
            {
                if (commanderId < 0) return null;
                if (GameVars.commander == null || commanderId >= GameVars.commander.Count) return null;
                return GameVars.commander[commanderId].name;
            }
            catch
            {
                return null;
            }
        }

        private static CommanderRosterEntry FindArmyEntry(TacticalBattleOrchestrator side)
        {
            try
            {
                if (side == null || side.Roster == null) return null;
                foreach (var entry in side.Roster.GetSide(side.AllianceId))
                {
                    if (entry != null && entry.Echelon == EchelonKind.Army) return entry;
                }
            }
            catch { }
            return null;
        }

        private static void AttachArmyIfActive(TacticalBattleOrchestrator side, AIBattle battle)
        {
            if (side == null) return;
            var armyEntry = FindArmyEntry(side);
            if (armyEntry == null) return;
            var army = new ArmyOrchestrator(side.AllianceId, BuiltInPlaybooks.SeedCatalog(), armyEntry.PersonalityVector);
            side.AttachArmy(army);
            var evidence = BuildArmyEvidenceForSide(side.AllianceId, battle);
            army.PickInitialPlan(evidence);
            if (army.HasPlan)
            {
                Plugin.Log.LogInfo("[TacticalPlan] side=" + side.AllianceId
                    + " plan=" + army.CurrentPlan.PlanId
                    + " phase=" + army.CurrentPlan.Phase
                    + " mainEffort=" + army.CurrentPlan.MainEffortSector);
            }
        }

        private static void AttachDirectChildrenIfReady(TacticalBattleOrchestrator side, AIBattle battle)
        {
            try
            {
                if (side == null || side.Army == null || !side.Army.HasPlan) return;
                // Already registered: skip silently. RegisterDirectChildren on a non-empty
                // list is idempotent in effect but resets allocator caches; avoid the churn.
                if (side.Army.CurrentDirectChildIntents.Count > 0) return;

                var snapshots = DirectChildDiscovery.Snapshot(battle);
                if (snapshots.Count == 0)
                {
                    if (!_directChildDeferLogged.Contains(side.AllianceId))
                    {
                        _directChildDeferLogged.Add(side.AllianceId);
                        OnceLog.Info("o3-defer-discovery:" + side.AllianceId,
                            "side=" + side.AllianceId + " reason=empty-or-no-command-units");
                    }
                    return;
                }

                side.Army.RegisterDirectChildren(snapshots);

                Plugin.Log.LogInfo("[TacticalDirectChildDiscovery] side=" + side.AllianceId
                    + " army=" + (snapshots.Count > 0 ? snapshots[0].ParentArmyId : "<none>")
                    + " shift=" + (snapshots.Count > 0 ? snapshots[0].CommandHierarchyShift : 0)
                    + " children=" + snapshots.Count
                    + " synthetic=" + IsSynthetic(snapshots));
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("[TacticalOrchestrator] AttachDirectChildrenIfReady skipped side="
                    + (side == null ? "null" : side.AllianceId.ToString())
                    + ": " + e.GetType().Name + " " + e.Message);
            }
        }

        private static bool IsSynthetic(IReadOnlyList<DirectChildSnapshot> snaps)
        {
            for (int i = 0; i < snaps.Count; i++)
                if (snaps[i].ChildId.StartsWith("synth-army-")) return true;
            return false;
        }

        private static void DriveDirectChildCycle(TacticalBattleOrchestrator side, AIBattle battle)
        {
            try
            {
                if (side == null || side.Army == null || !side.Army.HasPlan) return;
                if (side.Army.CurrentDirectChildIntents.Count == 0)
                {
                    AttachDirectChildrenIfReady(side, battle);
                    if (side.Army.CurrentDirectChildIntents.Count == 0) return;
                }

                var bundle = ArmyEvidenceBuilder.Build(battle, side.AllianceId);
                int childCount = side.Army.CurrentDirectChildIntents.Count;
                var primarySectors = new int[childCount];
                var flankBuckets = new int[childCount];
                var perChildIntent = new TacticalIntentModel[childCount];
                for (int i = 0; i < childCount; i++)
                {
                    var existing = side.Army.CurrentDirectChildIntents[i];
                    primarySectors[i] = existing.PrimarySector >= 0 ? existing.PrimarySector : 0;
                    flankBuckets[i] = 0;
                    perChildIntent[i] = ArmyIntentInference.BuildForFrontage(primarySectors[i], bundle.EnemyVisible, ownStrengthBucket: 1);
                }

                var snapshots = new DirectChildSnapshot[childCount];
                for (int i = 0; i < childCount; i++)
                {
                    var existing = side.Army.CurrentDirectChildIntents[i];
                    snapshots[i] = new DirectChildSnapshot(
                        existing.ChildId,
                        parentArmyId: "army-cached",
                        rawUnitTyp: existing.RawUnitTyp,
                        commandHierarchyShift: existing.RawUnitTyp - existing.EffectiveCommandLevel,
                        displayName: existing.DisplayName,
                        active: true);
                }

                var evidence = DirectChildEvidenceBuilder.BuildAll(snapshots, primarySectors, flankBuckets, bundle.EnemyVisible);
                side.Army.ObserveDirectChildEvidenceWithIntent(evidence, perChildIntent);

                for (int i = 0; i < side.Army.CurrentDirectChildIntents.Count; i++)
                {
                    var dci = side.Army.CurrentDirectChildIntents[i];
                    if (dci.Role == DirectChildRole.Unknown) continue;
                    OnceLog.Info("o3-direct-child-intent:" + _battleSequence + ":" + side.AllianceId + ":" + dci.ChildId + ":" + dci.Role,
                        "[TacticalDirectChildIntent] side=" + side.AllianceId
                        + " child=" + dci.ChildId
                        + " raw=" + dci.RawUnitTyp
                        + " effective=" + dci.EffectiveCommandLevel
                        + " role=" + dci.Role
                        + " sector=" + dci.PrimarySector
                        + " support=" + dci.SupportPriority01.ToString("0.00")
                        + " enemyIntent=" + dci.EnemyIntent.PrimaryIntent
                        + " confidence=" + dci.EnemyIntent.Confidence01.ToString("0.00"));
                }
            }
            catch (Exception e)
            {
                WarnTickCycleOnce(side, e);
            }
        }

        private static ArmyEvidence BuildArmyEvidenceForSide(int allianceId, AIBattle battle)
        {
            var bunits = ResolveBattleUnits(battle);
            int side = -1;
            try
            {
                if (bunits != null && bunits.alliance != null)
                {
                    for (int s = 0; s < 2 && s < bunits.alliance.Length; s++)
                        if (bunits.alliance[s] == allianceId) { side = s; break; }
                }
            }
            catch { }

            float own = 0f;
            float enemyTotal = 0f;
            try
            {
                if (bunits != null && bunits.sideinformation != null && side >= 0 && side < bunits.sideinformation.Length)
                    own = System.Math.Max(1f, bunits.sideinformation[side].totalactiveforce);
                if (bunits != null && bunits.sideinformation != null)
                {
                    for (int s = 0; s < 2 && s < bunits.sideinformation.Length; s++)
                        if (s != side) enemyTotal += System.Math.Max(0f, bunits.sideinformation[s].totalactiveforce);
                }
            }
            catch { }

            float odds = enemyTotal <= 0f ? 1f : own / enemyTotal;
            return new ArmyEvidence(odds, TerrainKind.Open, defaultMainEffortSector: 0);
        }

        private static void ClearLedgersBetweenBattles()
        {
            try { TacticalSectorLedger.ClearHelpRequests(); } catch { }
            try { Plugin.MoraleSnapshotLedger?.Clear(); } catch { }
            try { WhiskeyRealism.Patches.TacticalRegimentDiagnosticsPatch.Reset(); } catch { }
        }

        private static void ResetRuntimeTickState()
        {
            try { ArmyTickCycle.Reset(); } catch { }
            _lastTickTimeSeconds = 0f;
            _tickWarningKeys.Clear();
        }

        private static void ClearForFailure()
        {
            ResetRuntimeTickState();
            side0 = null;
            side1 = null;
            active = false;
        }
    }
}
