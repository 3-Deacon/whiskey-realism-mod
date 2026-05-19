using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    /// <summary>
    /// Runtime-only vanilla adapter. Extracts per-side army evidence from
    /// BattleUnits without throwing back into Harmony/runtime callers.
    /// </summary>
    internal static class ArmyEvidenceBuilder
    {
        // Between ArmyIntentInference reserve thresholds: not committed, not uncommitted.
        private const float UnknownReserveCommitFraction = 0.35f;
        private static FieldInfo _bunitsFieldCache;
        private static FieldInfo _objectiveChainFieldCache;
        private static FieldInfo _commandHierarchyShiftField;
        private static readonly Dictionary<string, FieldInfo> _sideInfoFieldCache = new Dictionary<string, FieldInfo>();
        private static readonly Dictionary<string, FieldInfo> _objectFieldCache = new Dictionary<string, FieldInfo>();

        internal readonly struct Bundle
        {
            public Bundle(
                ArmyEvidence ownEvidence,
                EnemyVisibleState enemyVisible,
                float ownMainEffortStrength,
                float ownArmyMorale,
                float ownReservesCommittedFraction,
                float reinforcementsArrivingDelta)
            {
                OwnEvidence = ownEvidence;
                EnemyVisible = enemyVisible;
                OwnMainEffortStrength = ownMainEffortStrength;
                OwnArmyMorale = ownArmyMorale;
                OwnReservesCommittedFraction = ownReservesCommittedFraction;
                ReinforcementsArrivingDelta = reinforcementsArrivingDelta;
            }

            public ArmyEvidence OwnEvidence { get; }
            public EnemyVisibleState EnemyVisible { get; }
            public float OwnMainEffortStrength { get; }
            public float OwnArmyMorale { get; }
            public float OwnReservesCommittedFraction { get; }
            public float ReinforcementsArrivingDelta { get; }
        }

        internal static Bundle Build(AIBattle battle, int allianceId)
        {
            var fallback = NoEvidence();

            try
            {
                var bunits = ResolveBattleUnits(battle);
                if (bunits == null) return fallback;

                int side = ResolveSideFromAlliance(bunits, allianceId);
                if (side < 0) return fallback;

                float ownActive = SafeSideInfoFloat(bunits, side, "totalactiveforce");
                if (ownActive <= 0f) return fallback;

                float enemyActive = SumEnemyActiveForce(bunits, side);
                float odds = enemyActive <= 0f ? 1f : ownActive / Math.Max(1f, enemyActive);
                var ownEvidence = new ArmyEvidence(odds, TerrainKind.Open, defaultMainEffortSector: 0);

                var enemyVisible = BuildEnemyVisibleState(battle, bunits, side, allianceId);
                float ownMorale = Clamp01OrDefault(SafeSideInfoFloat(bunits, side, "averagemorale"), 1f);
                float ownReservesCommitted = EstimateReserveCommitFraction(battle, bunits, side, ownActive);
                float ownReinforcements = SanitizeNonNegative(
                    SafeSideInfoFloat(bunits, side, "reinforcementarrivalswithin24hrs"));
                float enemyReinforcements = SanitizeNonNegative(
                    SafeSideInfoFloat(bunits, OppositeSide(side), "reinforcementarrivalswithin24hrs"));

                return new Bundle(
                    ownEvidence,
                    enemyVisible,
                    Math.Max(1f, ownActive),
                    ownMorale,
                    ownReservesCommitted,
                    ownReinforcements - enemyReinforcements);
            }
            catch (Exception e)
            {
                OnceLog.Warning("tactical-orch:evidence-builder", "[TacticalOrchestrator] ArmyEvidenceBuilder.Build degraded: "
                    + e.GetType().Name + " " + e.Message);
                return fallback;
            }
        }

        private static Bundle NoEvidence()
        {
            return new Bundle(
                new ArmyEvidence(1f, TerrainKind.Open, defaultMainEffortSector: 0),
                new EnemyVisibleState(Array.Empty<EnemyVisibleSector>(), 0f, false, false, 0f),
                ownMainEffortStrength: 1f,
                ownArmyMorale: 1f,
                ownReservesCommittedFraction: 0f,
                reinforcementsArrivingDelta: 0f);
        }

        private static EnemyVisibleState BuildEnemyVisibleState(AIBattle battle, BattleUnits bunits, int ownSide, int ownAllianceId)
        {
            try
            {
                var units = SafeGetCompleteUnitList();
                if (units == null) return EmptyEnemyVisible();

                int enemySide = OppositeSide(ownSide);
                int effectiveCommandMin = ClampShiftedMin(ReadCommandHierarchyShift());
                var sectors = new List<EnemyVisibleSector>();
                bool anyContactSpotted = false;
                bool anyContactBroken = false;

                for (int i = 0; i < units.Count; i++)
                {
                    var group = units[i] as Regiment;
                    if (!IsUsableOwnGroup(group, ownAllianceId, effectiveCommandMin)) continue;

                    float ownStrength = Math.Max(1f, SafeRegimentFloat(group, "groupstrengthaigroup"));
                    float enemyStrength = VisibleEnemyStrength(group);
                    bool recentFire = HasRecentFire(group);
                    if (enemyStrength > 0f || recentFire)
                    {
                        anyContactSpotted = true;
                    }

                    sectors.Add(new EnemyVisibleSector(
                        sectors.Count,
                        ownStrength,
                        enemyStrength,
                        recentFire));
                }

                float enemyActiveForce = Math.Max(1f, SafeSideInfoFloat(bunits, enemySide, "totalactiveforce"));
                float enemyReserveCommitFraction = EstimateReserveCommitFraction(battle, bunits, enemySide, enemyActiveForce);
                float enemyReinforcements = SanitizeNonNegative(
                    SafeSideInfoFloat(bunits, enemySide, "reinforcementarrivalswithin24hrs"));

                return new EnemyVisibleState(
                    sectors.ToArray(),
                    enemyReserveCommitFraction,
                    anyContactSpotted,
                    anyContactBroken,
                    enemyReinforcements);
            }
            catch
            {
                return EmptyEnemyVisible();
            }
        }

        private static EnemyVisibleState EmptyEnemyVisible()
        {
            return new EnemyVisibleState(Array.Empty<EnemyVisibleSector>(), 0f, false, false, 0f);
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

        private static int ResolveSideFromAlliance(BattleUnits bunits, int allianceId)
        {
            try
            {
                if (bunits == null || bunits.alliance == null) return -1;
                for (int side = 0; side < 2 && side < bunits.alliance.Length; side++)
                {
                    if (bunits.alliance[side] == allianceId) return side;
                }
            }
            catch { }
            return -1;
        }

        private static float SumEnemyActiveForce(BattleUnits bunits, int ownSide)
        {
            float total = 0f;
            try
            {
                if (bunits == null || bunits.sideinformation == null) return 0f;
                for (int side = 0; side < 2 && side < bunits.sideinformation.Length; side++)
                {
                    if (side == ownSide) continue;
                    total += Math.Max(0f, SafeSideInfoFloat(bunits, side, "totalactiveforce"));
                }
            }
            catch { }
            return total;
        }

        private static bool IsUsableOwnGroup(Regiment group, int ownAllianceId, int effectiveCommandMin)
        {
            try
            {
                if (group == null) return false;
                if (group.alliance != ownAllianceId) return false;
                if (group.unittyp < effectiveCommandMin) return false;
                if (group.isrouted || group.markedforrout) return false;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static float VisibleEnemyStrength(Regiment group)
        {
            try
            {
                float strength = SumVisibleEnemyStrengthForGroup(group);
                if (strength > 0f) return strength;

                int enemiesInRange = Math.Max(0, SafeRegimentInt(group, "groupenemiesinrange"));
                if (enemiesInRange <= 0) return 0f;

                // Fallback only: groupenemiesinrange is a visible contact count, not exact strength.
                return enemiesInRange * 1000f;
            }
            catch
            {
                return 0f;
            }
        }

        private static float SumVisibleEnemyStrengthForGroup(Regiment group)
        {
            try
            {
                float total = 0f;
                if (group == null)
                    return 0f;
                if (group.allattachedunits == null) return 0f;

                for (int i = 0; i < group.allattachedunits.Length; i++)
                {
                    var unit = group.allattachedunits[i];
                    if (!IsUsableAttachedCombatUnit(unit)) continue;
                    total += SumVisibleEnemyStrengthByAngle(unit);
                }

                return total;
            }
            catch
            {
                return 0f;
            }
        }

        private static float SumVisibleEnemyStrengthByAngle(Regiment unit)
        {
            try
            {
                if (unit == null || unit.unitrange == null || unit.unitrange.enemystrengthwithinangle == null)
                    return 0f;

                float total = 0f;
                for (int i = 0; i < unit.unitrange.enemystrengthwithinangle.Length; i++)
                    total += Math.Max(0f, unit.unitrange.enemystrengthwithinangle[i]);
                return total;
            }
            catch
            {
                return 0f;
            }
        }

        private static bool IsUsableAttachedCombatUnit(Regiment unit)
        {
            try
            {
                if (unit == null) return false;
                if (unit.unittyp > 13) return false;
                if (unit.isrouted || unit.permanentlydetached) return false;
                if (!unit.gameObject.activeInHierarchy) return false;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool HasRecentFire(Regiment group)
        {
            try
            {
                if (group == null) return false;
                if (HasReceivedFire(group)) return true;
                if (group.allattachedunits == null) return false;
                for (int i = 0; i < group.allattachedunits.Length; i++)
                {
                    if (HasReceivedFire(group.allattachedunits[i])) return true;
                }
            }
            catch { }
            return false;
        }

        private static bool HasReceivedFire(Regiment unit)
        {
            try
            {
                return unit != null && unit.receivedfire != null && unit.receivedfire.Count > 0;
            }
            catch
            {
                return false;
            }
        }

        private static float SafeSideInfoFloat(BattleUnits bunits, int side, string fieldName)
        {
            return TrySideInfoFloat(bunits, side, fieldName, out var value) ? value : 0f;
        }

        private static bool TrySideInfoFloat(BattleUnits bunits, int side, string fieldName, out float value)
        {
            value = 0f;
            try
            {
                if (bunits == null || bunits.sideinformation == null) return false;
                if (side < 0 || side >= bunits.sideinformation.Length) return false;
                var info = bunits.sideinformation[side];
                if (info == null) return false;
                var field = ResolveSideInfoField(info.GetType(), fieldName);
                if (field == null) return false;
                object raw = field.GetValue(info);
                if (raw == null) return false;
                value = Convert.ToSingle(raw);
                return true;
            }
            catch
            {
                value = 0f;
                return false;
            }
        }

        private static FieldInfo ResolveSideInfoField(Type infoType, string fieldName)
        {
            if (infoType == null) return null;

            string key = infoType.FullName + ":" + fieldName;
            if (_sideInfoFieldCache.ContainsKey(key))
                return _sideInfoFieldCache[key];

            var field = infoType.GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            _sideInfoFieldCache[key] = field;
            return field;
        }

        private static float EstimateReserveCommitFraction(AIBattle battle, BattleUnits bunits, int side, float activeForce)
        {
            if (!TrySumReservePoolStrength(battle, bunits, side, out float reserveStrength, out bool hasDesignatedReserves))
                return UnknownReserveCommitFraction;

            // Chains exist but no reservegroups designated for this side. The army
            // has no reserve plan; report "unknown" rather than 100% committed.
            // Otherwise ArmyReplanTriggers.ReserveExhaustion fires every cycle.
            if (!hasDesignatedReserves)
                return UnknownReserveCommitFraction;

            float committed = 1f - reserveStrength / Math.Max(1f, activeForce);
            return Clamp01OrDefault(committed, UnknownReserveCommitFraction);
        }

        private static bool TrySumReservePoolStrength(AIBattle battle, BattleUnits bunits, int side, out float reserveStrength, out bool hasDesignatedReserves)
        {
            reserveStrength = 0f;
            hasDesignatedReserves = false;
            try
            {
                var chains = ResolveObjectiveChains(battle);
                if (chains == null || chains.Count == 0) return false;

                int alliance = SafeAlliance(bunits, side);
                if (alliance < 0) return false;
                int effectiveCommandMin = ClampShiftedMin(ReadCommandHierarchyShift());

                bool observedChain = false;
                var seen = new HashSet<int>();
                for (int i = 0; i < chains.Count; i++)
                {
                    object chain = chains[i];
                    if (chain == null) continue;
                    observedChain = true;

                    var reserves = SafeObjectList(chain, "reservegroups");
                    if (reserves == null) continue;

                    for (int j = 0; j < reserves.Count; j++)
                    {
                        var group = reserves[j] as Regiment;
                        if (!IsUsableOwnGroup(group, alliance, effectiveCommandMin)) continue;

                        int id = SafeInstanceId(group);
                        if (id != 0 && !seen.Add(id)) continue;

                        hasDesignatedReserves = true;
                        reserveStrength += ActiveGroupStrength(group);
                    }
                }

                return observedChain;
            }
            catch
            {
                reserveStrength = 0f;
                hasDesignatedReserves = false;
                return false;
            }
        }

        private static IList ResolveObjectiveChains(AIBattle battle)
        {
            try
            {
                if (battle == null) return null;
                if (_objectiveChainFieldCache == null)
                    _objectiveChainFieldCache = typeof(AIBattle).GetField(
                        "objectivechain",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                return _objectiveChainFieldCache?.GetValue(battle) as IList;
            }
            catch
            {
                return null;
            }
        }

        private static IList SafeObjectList(object instance, string fieldName)
        {
            try
            {
                if (instance == null) return null;
                var field = ResolveObjectField(instance.GetType(), fieldName);
                return field?.GetValue(instance) as IList;
            }
            catch
            {
                return null;
            }
        }

        private static FieldInfo ResolveObjectField(Type type, string fieldName)
        {
            if (type == null) return null;

            string key = type.FullName + ":" + fieldName;
            if (_objectFieldCache.ContainsKey(key))
                return _objectFieldCache[key];

            var field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            _objectFieldCache[key] = field;
            return field;
        }

        private static int SafeAlliance(BattleUnits bunits, int side)
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

        private static int SafeInstanceId(Regiment group)
        {
            try
            {
                return group == null ? 0 : group.GetInstanceID();
            }
            catch
            {
                return 0;
            }
        }

        private static float ActiveGroupStrength(Regiment group)
        {
            try
            {
                if (group == null) return 0f;
                int strength = group.groupstrengthactive;
                if (strength <= 0) strength = group.groupstrengthaigroup;
                return Math.Max(0f, strength);
            }
            catch
            {
                return 0f;
            }
        }

        private static int SafeRegimentInt(Regiment regiment, string fieldName)
        {
            try
            {
                if (regiment == null) return 0;
                var field = AccessTools.Field(typeof(Regiment), fieldName);
                if (field == null) return 0;
                object value = field.GetValue(regiment);
                return value == null ? 0 : Convert.ToInt32(value);
            }
            catch
            {
                return 0;
            }
        }

        private static float SafeRegimentFloat(Regiment regiment, string fieldName)
        {
            try
            {
                if (regiment == null) return 0f;
                var field = AccessTools.Field(typeof(Regiment), fieldName);
                if (field == null) return 0f;
                object value = field.GetValue(regiment);
                return value == null ? 0f : Convert.ToSingle(value);
            }
            catch
            {
                return 0f;
            }
        }

        private static IList SafeGetCompleteUnitList()
        {
            try
            {
                return BattleUnits.completeunitlist as IList;
            }
            catch
            {
                return null;
            }
        }

        internal static int ReadCommandHierarchyShift()
        {
            try
            {
                if (_commandHierarchyShiftField == null)
                {
                    _commandHierarchyShiftField = AccessTools.Field(typeof(GamePrefs), "commandhierarchyshift");
                }

                if (_commandHierarchyShiftField == null) return 0;
                var value = _commandHierarchyShiftField.GetValue(null);
                return value is int shift ? shift : 0;
            }
            catch
            {
                return 0;
            }
        }

        internal static int ClampShiftedMin(int shift)
        {
            int min = TacticalUnitType.MaxCombat + 1 + shift;
            if (min < 1) return 1;
            if (min > 18) return 18;
            return min;
        }

        private static int OppositeSide(int side)
        {
            return side == 0 ? 1 : 0;
        }

        private static float Clamp01OrDefault(float value, float fallback)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return fallback;
            if (value < 0f) return 0f;
            if (value > 1f) return 1f;
            return value;
        }

        private static float SanitizeNonNegative(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 0f;
            return value < 0f ? 0f : value;
        }

        // ---------- Reinforcement Opportunity force balance ----------

        /// <summary>
        /// Builds TacticalForceBalanceEvidence for the given alliance by reading
        /// vanilla BattleUnits.sideinformation (current strength), BattleUnits.scheduledarrival
        /// (per-arrival timing + strength), and commander personality fields. Returns null
        /// on any failure so callers can fall through to existing behavior.
        /// </summary>
        internal static TacticalForceBalanceEvidence? BuildForceBalance(
            AIBattle battle,
            int allianceId,
            float commanderInitiative01,
            float commanderAggression01)
        {
            try
            {
                var bunits = ResolveBattleUnits(battle);
                if (bunits == null) return null;
                int side = ResolveSideFromAlliance(bunits, allianceId);
                if (side < 0) return null;
                int enemySide = OppositeSide(side);

                float ownDeployed = Math.Max(0f, SafeSideInfoFloat(bunits, side, "totalactiveforce"));
                float enemyDeployed = Math.Max(0f, SafeSideInfoFloat(bunits, enemySide, "totalactiveforce"));

                var ownArrivals = BuildArrivalsForSide(bunits, side);
                var enemyArrivals = BuildArrivalsForSide(bunits, enemySide);

                // Intel confidence: with no visible enemy contact at all, drop
                // to 0.4 so the doctrine falls through to NoOpportunity (it
                // refuses to act on pure deployment-list strength estimates).
                // Otherwise 0.8 baseline so the doctrine engages.
                float intelConfidence = enemyDeployed > 0f ? 0.8f : 0.4f;

                return new TacticalForceBalanceEvidence(
                    ownDeployed,
                    enemyDeployed,
                    ownArrivals,
                    enemyArrivals,
                    commanderInitiative01,
                    commanderAggression01,
                    intelConfidence);
            }
            catch (Exception e)
            {
                OnceLog.Warning("tactical-orch:force-balance",
                    "[TacticalOrchestrator] ArmyEvidenceBuilder.BuildForceBalance degraded: "
                    + e.GetType().Name + " " + e.Message);
                return null;
            }
        }

        private static IReadOnlyList<ReinforcementArrival> BuildArrivalsForSide(BattleUnits bunits, int side)
        {
            try
            {
                int sideAlliance = -1;
                if (bunits != null && bunits.alliance != null && side >= 0 && side < bunits.alliance.Length)
                    sideAlliance = bunits.alliance[side];
                if (sideAlliance < 0) return Array.Empty<ReinforcementArrival>();

                var arrivalsList = ResolveScheduledArrivals(bunits);
                if (arrivalsList == null || arrivalsList.Count == 0) return Array.Empty<ReinforcementArrival>();

                var result = new List<ReinforcementArrival>();
                for (int i = 0; i < arrivalsList.Count; i++)
                {
                    object arrival = arrivalsList[i];
                    if (arrival == null) continue;
                    var regiment = SafeRegimentField(arrival, ref _arrivalRegimentField, "regimentrefreg");
                    if (regiment == null) continue;
                    if (regiment.alliance != sideAlliance) continue;
                    float durationSeconds = SafeFloatField(arrival, ref _arrivalDurationField, "durationleft");
                    if (durationSeconds < 0f) durationSeconds = 0f;
                    float hours = durationSeconds / 3600f;
                    float strength = EstimateReinforcementStrength(regiment);
                    if (strength <= 0f) continue;
                    result.Add(new ReinforcementArrival(hours, strength));
                }
                return result;
            }
            catch
            {
                return Array.Empty<ReinforcementArrival>();
            }
        }

        private static FieldInfo _scheduledArrivalsField;
        private static FieldInfo _arrivalRegimentField;
        private static FieldInfo _arrivalDurationField;

        private static IList ResolveScheduledArrivals(BattleUnits bunits)
        {
            try
            {
                if (bunits == null) return null;
                if (_scheduledArrivalsField == null)
                    _scheduledArrivalsField = AccessTools.Field(typeof(BattleUnits), "scheduledarrival");
                return _scheduledArrivalsField?.GetValue(bunits) as IList;
            }
            catch { return null; }
        }

        private static Regiment SafeRegimentField(object instance, ref FieldInfo cached, string name)
        {
            try
            {
                if (instance == null) return null;
                if (cached == null) cached = AccessTools.Field(instance.GetType(), name);
                return cached?.GetValue(instance) as Regiment;
            }
            catch { return null; }
        }

        private static float SafeFloatField(object instance, ref FieldInfo cached, string name)
        {
            try
            {
                if (instance == null) return 0f;
                if (cached == null) cached = AccessTools.Field(instance.GetType(), name);
                if (cached == null) return 0f;
                object v = cached.GetValue(instance);
                if (v is float f) return f;
                return 0f;
            }
            catch { return 0f; }
        }

        /// <summary>
        /// Estimates the strength a reinforcement Regiment will bring when it
        /// activates. Tries groupstrengthaigroup first (works for already-
        /// constructed reinforcements); falls back to summing the attached
        /// units' numofmenoriginal so even not-yet-spawned forces report a
        /// sensible estimate.
        /// </summary>
        private static float EstimateReinforcementStrength(Regiment regiment)
        {
            if (regiment == null) return 0f;
            try
            {
                float aiGroup = SafeRegimentFloat(regiment, "groupstrengthaigroup");
                if (aiGroup > 0f) return aiGroup;
            }
            catch { }

            try
            {
                float total = 0f;
                var units = regiment.allattachedunits;
                if (units != null)
                {
                    for (int i = 0; i < units.Length; i++)
                    {
                        var unit = units[i];
                        if (unit == null) continue;
                        // numofmenoriginal is the rostered strength; fall back
                        // to current strength fields if missing.
                        float men = TryReadFloatField(unit, "numofmenoriginal");
                        if (men <= 0f) men = TryReadFloatField(unit, "groupowninrange");
                        total += Math.Max(0f, men);
                    }
                }
                return total;
            }
            catch
            {
                return 0f;
            }
        }

        private static float TryReadFloatField(Regiment regiment, string name)
        {
            try
            {
                var field = AccessTools.Field(regiment.GetType(), name);
                if (field == null) return 0f;
                object v = field.GetValue(regiment);
                if (v is float f) return f;
                if (v is int i) return i;
                return 0f;
            }
            catch { return 0f; }
        }
    }
}
