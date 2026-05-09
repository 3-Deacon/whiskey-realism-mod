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

                var enemyVisible = BuildEnemyVisibleState(bunits, side, allianceId);
                float ownMorale = Clamp01OrDefault(SafeSideInfoFloat(bunits, side, "averagemorale"), 1f);
                float ownReservesCommitted = SafeReserveCommitFraction(bunits, side);
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

        private static EnemyVisibleState BuildEnemyVisibleState(BattleUnits bunits, int ownSide, int ownAllianceId)
        {
            try
            {
                var units = SafeGetCompleteUnitList();
                if (units == null) return EmptyEnemyVisible();

                int enemySide = OppositeSide(ownSide);
                var sectors = new List<EnemyVisibleSector>();
                bool anyContactSpotted = false;
                bool anyContactBroken = false;

                for (int i = 0; i < units.Count; i++)
                {
                    var group = units[i] as Regiment;
                    if (!IsUsableOwnGroup(group, ownAllianceId)) continue;

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

                float enemyReserveCommitFraction = SafeReserveCommitFraction(bunits, enemySide);
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

        private static bool IsUsableOwnGroup(Regiment group, int ownAllianceId)
        {
            try
            {
                if (group == null) return false;
                if (group.alliance != ownAllianceId) return false;
                if (group.unittyp <= 13) return false;
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
                var field = AccessTools.Field(info.GetType(), fieldName);
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

        private static float SafeReserveCommitFraction(BattleUnits bunits, int side)
        {
            return TrySideInfoFloat(bunits, side, "reservescommittedfraction", out var value)
                ? Clamp01OrDefault(value, UnknownReserveCommitFraction)
                : UnknownReserveCommitFraction;
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
    }
}
