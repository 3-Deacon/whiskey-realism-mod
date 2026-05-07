using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Strategic
{
    internal static class FormationDirectiveRuntime
    {
        internal static FormationDirectiveLedger BuildForAlliance(int allianceId, EraStage era, string planTargetAreaKey)
        {
            try
            {
                var faction = FindFaction(allianceId);
                if (faction == null)
                {
                    OnceLog.Warning(
                        "formation-directive:no-faction:" + allianceId,
                        $"[FormationDirective] build skipped: AICampaign faction not found for alliance={allianceId}");
                    return null;
                }

                var ownUnits = AccessTools.Field(faction.GetType(), "ownunits")?.GetValue(faction) as List<Regiment>;
                if (ownUnits == null)
                {
                    OnceLog.Warning(
                        "formation-directive:no-ownunits:" + allianceId,
                        $"[FormationDirective] build skipped: ownunits unavailable for alliance={allianceId}");
                    return null;
                }

                bool grandArmyStructure = GrandArmyStructure(allianceId);

                // Build spatial area index once per call — avoids O(n²) campaignunitlist scan in PopulateLocalPressure.
                var unitsByArea = BuildAreaIndex();

                var snapshots = new List<FormationSnapshot>();
                for (int i = 0; i < ownUnits.Count; i++)
                {
                    var unit = ownUnits[i];
                    if (unit == null) continue;
                    var snapshot = SnapshotUnit(allianceId, unit, grandArmyStructure, unitsByArea);
                    if (snapshot != null)
                    {
                        snapshot.IsPlanTargetArea = !string.IsNullOrEmpty(planTargetAreaKey) &&
                                                    string.Equals(snapshot.AreaKey, planTargetAreaKey, StringComparison.OrdinalIgnoreCase);
                        snapshots.Add(snapshot);
                    }
                }

                var memories = StrategicCoordinator.Instance?.DirectorMemories;
                var posture = (memories != null && allianceId >= 0 && allianceId < memories.Length)
                    ? memories[allianceId]?.LastPosture
                    : null;
                var options = new FormationDirectiveOptions
                {
                    RecoverMoraleFloor    = Clamp(0.35f + (posture?.RecoverFloorModifier ?? 0f), 0.20f, 0.50f),
                    RecoverReadinessFloor = Clamp(0.35f + (posture?.RecoverFloorModifier ?? 0f), 0.20f, 0.50f),
                    DivisionAttackRatio   = Clamp(1.5f  + (posture?.MassRatioModifier   ?? 0f), 1.20f, 1.80f),
                    CorpsAttackRatio      = Clamp(1.2f  + (posture?.MassRatioModifier   ?? 0f), 1.00f, 1.50f),
                    ArmyAttackRatio       = Clamp(1.05f + (posture?.MassRatioModifier   ?? 0f), 0.90f, 1.30f)
                };
                return FormationDirectiveLedger.Build(snapshots, era, planTargetAreaKey, options);
            }
            catch (Exception ex)
            {
                OnceLog.Warning("formation-directive:build", "[FormationDirective] build failed: " + ex.Message);
                return null;
            }
        }

        // Builds a Dictionary<areaKey, List<Regiment>> over top-formation top-units only.
        // Used to turn the O(n²) PopulateLocalPressure scan into an O(bucket) scan.
        private static Dictionary<string, List<Regiment>> BuildAreaIndex()
        {
            var index = new Dictionary<string, List<Regiment>>(StringComparer.Ordinal);
            try
            {
                if (BattleUnits.campaignunitlist == null) return index;
                for (int i = 0; i < BattleUnits.campaignunitlist.Count; i++)
                {
                    var u = BattleUnits.campaignunitlist[i];
                    if (!IsTopFormation(u)) continue;
                    string key = ArmyAreaRuntime.AreaKey(u.transform.position);
                    if (!index.TryGetValue(key, out var bucket))
                    {
                        bucket = new List<Regiment>();
                        index[key] = bucket;
                    }
                    bucket.Add(u);
                }
            }
            catch (Exception ex)
            {
                OnceLog.Warning("formation-directive:area-index", "[FormationDirective] BuildAreaIndex failed: " + ex.Message);
            }
            return index;
        }

        internal static FormationSnapshot SnapshotUnit(int allianceId, Regiment unit, bool grandArmyStructure,
            Dictionary<string, List<Regiment>> unitsByArea = null)
        {
            try
            {
                if (unit == null) return null;
                if (unit.unittyp < 14 || unit.unittyp > 16) return null;

                var snapshot = new FormationSnapshot
                {
                    UnitKey = UnitKey(unit),
                    ParentUnitKey = ParentUnitKey(unit),
                    AllianceId = allianceId,
                    StableUnitId = ((UnityEngine.Object)unit).GetInstanceID(),
                    UnitName = ((UnityEngine.Object)unit).name,
                    CommanderName = CommanderName(unit),
                    UnitType = unit.unittyp,
                    IsTopUnit = unit.istopunit,
                    IsGarrisoned = unit.garrisonreference != null,
                    GrandArmyStructureAvailable = grandArmyStructure,
                    X = unit.transform.position.x,
                    Z = unit.transform.position.z,
                    GroupStrengthActive = Math.Max(0f, unit.groupstrengthactive),
                    GroupStrengthDirect = Math.Max(0f, unit.groupstrengthdirect),
                    Morale = FormationSnapshot.Clamp01(unit.groupmorale),
                    Readiness = FormationSnapshot.Clamp01(unit.readiness),
                    RifleAmmo = ReadSupplyState(unit, 0, unit.groupammo),
                    ArtilleryAmmo = ReadSupplyState(unit, 1, unit.groupammo),
                    Supply = ReadSupply(unit),
                    Fatigue = FormationSnapshot.Clamp01(unit.groupfatigue),
                    WeaponFirepower = EstimateWeaponFirepower(unit),
                    CommandRange = unit.commanderrange,
                    BugleRange = unit.buglerange,
                    InBattle = unit.inbattle,
                    OnRetreat = unit.onretreat,
                    HasActivePath = unit.regimentpaths > 0,
                    IsCavalryCapable = HasCavalryStrength(unit)
                };

                snapshot.AreaKey = ArmyAreaRuntime.AreaKey(unit.transform.position);
                snapshot.SectorKey = FrontSectorRuntime.SectorKey(unit.transform.position);
                PopulateFrontContext(allianceId, snapshot);

                PopulateLocalPressure(snapshot, unit, allianceId, unitsByArea);
                return snapshot;
            }
            catch (Exception ex)
            {
                OnceLog.Warning("formation-directive:snapshot", "[FormationDirective] snapshot failed: " + ex.Message);
                return null;
            }
        }

        internal static bool ShouldAllowAreaMovement(int allianceId, string unitKey)
        {
            try
            {
                var assignment = GetAssignment(allianceId, unitKey);
                if (assignment == null) return true;
                if (!assignment.DirectMovementAllowed) return false;
                if (assignment.Directive == FormationDirective.Delay) return false;
                if (assignment.Directive == FormationDirective.Concede) return false;
                return true;
            }
            catch (Exception ex)
            {
                OnceLog.Warning("formation-directive:area-movement", "[FormationDirective] area movement gate failed: " + ex.Message);
                return true;
            }
        }

        internal static bool AllowsArmyGroupAttachment(int allianceId, string unitKey)
        {
            try
            {
                var assignment = GetAssignment(allianceId, unitKey);
                if (assignment == null) return false;
                if (assignment.Level == FormationLevel.Corps || assignment.Level == FormationLevel.Army) return true;
                if (assignment.Level != FormationLevel.Division) return false;
                return assignment.Directive == FormationDirective.Reinforce ||
                       assignment.Directive == FormationDirective.Reserve ||
                       assignment.Directive == FormationDirective.Guard ||
                       assignment.Directive == FormationDirective.Mass;
            }
            catch (Exception ex)
            {
                OnceLog.Warning("formation-directive:armygroup-attachment", "[FormationDirective] army-group attachment gate failed: " + ex.Message);
                return false;
            }
        }

        private static FormationDirectiveAssignment GetAssignment(int allianceId, string unitKey)
        {
            if (allianceId < 0 || string.IsNullOrEmpty(unitKey)) return null;
            var coordinator = StrategicCoordinator.Instance;
            if (coordinator == null) return null;

            object ledgers = AccessTools.Field(coordinator.GetType(), "FormationDirectives")?.GetValue(coordinator);
            if (ledgers == null)
                ledgers = AccessTools.Property(coordinator.GetType(), "FormationDirectives")?.GetValue(coordinator, null);

            var array = ledgers as FormationDirectiveLedger[];
            if (array == null || allianceId >= array.Length) return null;
            return array[allianceId]?.GetAssignment(unitKey);
        }

        private static void PopulateFrontContext(int allianceId, FormationSnapshot snapshot)
        {
            try
            {
                var fronts = StrategicCoordinator.Instance?.Fronts;
                if (fronts == null || allianceId < 0 || allianceId >= fronts.Length) return;
                var sector = fronts[allianceId]?.GetSector(snapshot.SectorKey);
                if (sector == null) return;
                snapshot.FrontPosture = sector.Posture;
                snapshot.IsCriticalSector = sector.IsCritical;
                snapshot.IsPlanTargetArea = sector.IsPlanTarget;
            }
            catch (Exception ex)
            {
                OnceLog.Warning("formation-directive:front-context", "[FormationDirective] front context failed: " + ex.Message);
            }
        }

        // Populates local pressure from same-area bucket only (avoids full campaignunitlist scan).
        private static void PopulateLocalPressure(FormationSnapshot snapshot, Regiment unit, int allianceId,
            Dictionary<string, List<Regiment>> unitsByArea)
        {
            try
            {
                if (unitsByArea == null || string.IsNullOrEmpty(snapshot.AreaKey)) return;
                if (!unitsByArea.TryGetValue(snapshot.AreaKey, out var bucket)) return;

                Vector3 position = unit.transform.position;
                float supportRange = snapshot.CommandRange > 0f ? snapshot.CommandRange : snapshot.BugleRange;
                float enemyRange = snapshot.BugleRange > 0f ? snapshot.BugleRange * 2f : snapshot.CommandRange;

                for (int i = 0; i < bucket.Count; i++)
                {
                    var other = bucket[i];
                    if (ReferenceEquals(other, unit)) continue;
                    float distance = Vector3.Distance(position, other.transform.position);
                    if (other.alliance == allianceId)
                    {
                        if (supportRange > 0f && distance <= supportRange)
                        {
                            snapshot.LocalFriendlySupportStrength += Math.Max(0f, other.groupstrengthactive);
                            snapshot.SupportCanReach = true;
                        }
                    }
                    else if (enemyRange > 0f && distance <= enemyRange)
                    {
                        snapshot.LocalEnemyStrength += Math.Max(0f, other.groupstrengthactive);
                        snapshot.LocalEnemyExchangePressure += EstimateEnemyExchangePressure(other);
                        var level = FormationSnapshot.LevelFromUnitType(other.unittyp);
                        if ((int)level > (int)snapshot.VisibleEnemyLevel)
                            snapshot.VisibleEnemyLevel = level;
                    }
                }
            }
            catch (Exception ex)
            {
                OnceLog.Warning("formation-directive:local-pressure", "[FormationDirective] local pressure failed: " + ex.Message);
            }
        }

        private static object FindFaction(int allianceId)
        {
            var aicType = AccessTools.TypeByName("AICampaign");
            var list = AccessTools.Field(aicType, "aifaction")?.GetValue(null) as IList;
            if (list == null) return null;
            for (int i = 0; i < list.Count; i++)
            {
                var faction = list[i];
                var fi = AccessTools.Field(faction.GetType(), "allianceid");
                if (fi == null) continue;
                try
                {
                    if (Convert.ToInt32(fi.GetValue(faction)) == allianceId) return faction;
                }
                catch { }
            }
            return null;
        }

        private static bool GrandArmyStructure(int allianceId)
        {
            try
            {
                var method = AccessTools.Method(AccessTools.TypeByName("AICampaign"), "GrandArmyStructure");
                if (method == null) return false;
                return Convert.ToBoolean(method.Invoke(null, new object[] { allianceId }));
            }
            catch (Exception ex)
            {
                OnceLog.Warning("formation-directive:grand-army", "[FormationDirective] GrandArmyStructure failed: " + ex.Message);
                return false;
            }
        }

        private static bool IsTopFormation(Regiment unit)
        {
            return unit != null &&
                   unit.istopunit &&
                   unit.garrisonreference == null &&
                   unit.unittyp >= 14 &&
                   unit.unittyp <= 16;
        }

        // Uses Unity hierarchy (canonical) — parentregiment GameObject field is not needed.
        private static string ParentUnitKey(Regiment unit)
        {
            try
            {
                if (unit == null) return null;
                if (unit.transform.parent != null)
                {
                    var parent = unit.transform.parent.GetComponent<Regiment>();
                    if (parent != null) return UnitKey(parent);
                }
            }
            catch (Exception ex)
            {
                OnceLog.Warning("formation-directive:parent", "[FormationDirective] parent lookup failed: " + ex.Message);
            }
            return null;
        }

        private static string UnitKey(Regiment unit)
        {
            return ((UnityEngine.Object)unit).name + ":" + unit.commander.ToString();
        }

        private static string CommanderName(Regiment unit)
        {
            try
            {
                int commanderId = unit.commander;
                var commanders = GameVars.commander;
                if (commanders == null || commanderId < 0 || commanderId >= commanders.Count) return null;
                return commanders[commanderId].combinedname;
            }
            catch (Exception ex)
            {
                OnceLog.Warning("formation-directive:commander", "[FormationDirective] commander lookup failed: " + ex.Message);
                return null;
            }
        }

        private static float ReadSupplyState(Regiment unit, int index, float fallback)
        {
            try
            {
                var values = unit.groupsupplystate;
                if (values != null && index >= 0 && index < values.Length)
                    return FormationSnapshot.Clamp01(values[index]);
            }
            catch { }
            return FormationSnapshot.Clamp01(fallback);
        }

        private static float ReadSupply(Regiment unit)
        {
            try
            {
                var values = unit.groupsupplystate;
                if (values != null && values.Length > 3)
                {
                    float provisions = FormationSnapshot.Clamp01(values[2]);
                    float forage = FormationSnapshot.Clamp01(values[3]);
                    return Math.Min(provisions, forage);
                }
            }
            catch { }
            return 1f;
        }

        private static bool HasCavalryStrength(Regiment unit)
        {
            var values = unit.groupstrengthperunittyp;
            if (values == null) values = unit.groupstrengthperunittypcampaigndirect;
            if (values == null) return false;

            int cavalry = values.Length > 1 ? Math.Max(0, values[1]) : 0;
            int mountedArtillery = values.Length > 4 ? Math.Max(0, values[4]) : 0;
            return cavalry + mountedArtillery > 0;
        }

        private static float EstimateWeaponFirepower(Regiment unit)
        {
            // groupstrengthperunittypcampaigndirect: infantry/cavalry/artillery strength breakdown
            float direct = Sum(unit.groupstrengthperunittypcampaigndirect);
            if (direct <= 0f)
                direct = Sum(unit.groupstrengthperunittyp);
            if (direct <= 0f)
                direct = Math.Max(0f, unit.groupstrengthactive);

            float guns = Math.Max(0, unit.groupstatsgunsactive);
            return Math.Max(1f, direct + guns * 65f);
        }

        private static float EstimateEnemyExchangePressure(Regiment unit)
        {
            if (unit == null) return 0f;
            float strength = Math.Max(0f, unit.groupstrengthactive);
            float guns = Math.Max(0, unit.groupstatsgunsactive);
            return strength + guns * 65f;
        }

        private static float Sum(int[] values)
        {
            if (values == null) return 0f;
            float total = 0f;
            for (int i = 0; i < values.Length; i++) total += Math.Max(0, values[i]);
            return total;
        }

        private static float Clamp(float v, float lo, float hi) => v < lo ? lo : (v > hi ? hi : v);
    }
}
