using System;
using System.Collections;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // v0.2.2 concrete steering for vanilla AICampaign.CheckTransferOfUnits.
    // Vanilla transfers troops from a surplus position to a deficit position.
    // We temporarily replace the deficit with the active CIC plan target, then
    // restore vanilla state after the method runs. The actual MoveUnit call and
    // all vanilla safety gates remain vanilla-owned.
    [HarmonyPatch(typeof(AICampaign), "CheckTransferOfUnits")]
    internal static class TransferOfUnitsPatch
    {
        private const float FallbackSearchRadius = 650f;

        internal sealed class TransferSteerState
        {
            public object Faction;
            public System.Reflection.FieldInfo DeficitField;
            public object OriginalDeficit;
            public bool Changed;
            public int AllianceId;
            public int ObjectiveId;
            public int BeforeTransferCount;
        }

        [HarmonyPrefix]
        internal static void Prefix(int _aifaction, out TransferSteerState __state)
        {
            __state = null;
            OnceLog.Info("transfer", "TransferOfUnitsPatch wired (concrete plan-target steering)");

            try
            {
                if (Plugin.Instance == null || !Plugin.Instance.Enabled.Value) return;
                if (StrategicCoordinator.Instance == null) return;

                int allianceId = AICampaignReflect.GetAllianceId(_aifaction);
                if (allianceId < 0 || allianceId >= StrategicCoordinator.Instance.CICs.Length) return;

                int playerAlliance = StrategicCoordinator.ResolvePlayerAlliance();
                if (StrategicCoordinator.IsPlayerCICOf(allianceId, playerAlliance)) return;

                var cic = StrategicCoordinator.Instance.CICs[allianceId];
                var phase = cic?.ActivePlan?.CurrentPhase;
                if (phase == null || phase.TargetObjectiveId < 0) return;

                var target = ObjectiveAdapter.ResolveObjectivePosition(phase.TargetObjectiveId);
                if (!target.HasValue) return;

                var faction = GetFaction(_aifaction);
                if (faction == null) return;

                var factionType = faction.GetType();
                var deficitField = AccessTools.Field(factionType, "positiondeficit");
                var surplusField = AccessTools.Field(factionType, "positionsurplus");
                if (deficitField == null || surplusField == null)
                {
                    OnceLog.Warning("transfer:field:deficit-surplus", "TransferOfUnitsPatch missing AIFaction positiondeficit/positionsurplus fields");
                    return;
                }

                var surplus = surplusField.GetValue(faction);
                if (surplus == null) return;

                var ownUnits = AccessTools.Field(factionType, "ownunits")?.GetValue(faction) as IList;
                var enemyUnits = AccessTools.Field(factionType, "enemyunits")?.GetValue(faction) as IList;
                if (ownUnits == null || enemyUnits == null)
                {
                    OnceLog.Warning("transfer:field:unit-lists", "TransferOfUnitsPatch missing AIFaction ownunits/enemyunits lists");
                    return;
                }

                float searchRadius = ReadGamePrefsFloat("aimaximumdistancetosearchforunitrelocations", FallbackSearchRadius);
                float ownNear = SumStrengthNear(ownUnits, target.Value, searchRadius);
                float enemyNear = SumStrengthNear(enemyUnits, target.Value, searchRadius);
                float totalOwn = SumStrength(ownUnits);
                if (totalOwn <= 0f) return;

                float requiredAtTarget = Math.Max(2500f, totalOwn * Clamp(phase.ForceFractionRequired, 0.15f, 0.95f));
                if (ownNear >= requiredAtTarget && ownNear >= enemyNear) return;

                float enemyProxy = Math.Max(enemyNear, requiredAtTarget);
                var transferData = CreateTransferData(factionType, target.Value, ownNear, enemyProxy);
                if (transferData == null) return;

                var originalDeficit = deficitField.GetValue(faction);
                __state = new TransferSteerState
                {
                    Faction = faction,
                    DeficitField = deficitField,
                    OriginalDeficit = originalDeficit,
                    Changed = true,
                    AllianceId = allianceId,
                    ObjectiveId = phase.TargetObjectiveId,
                    BeforeTransferCount = TransferCount(faction)
                };

                deficitField.SetValue(faction, transferData);

                if (Plugin.Instance.VerboseLogging.Value)
                    Plugin.Log.LogInfo($"[Patch:Transfer] steering alliance={allianceId} obj={phase.TargetObjectiveId} ownNear={ownNear:F0} required={requiredAtTarget:F0} enemyNear={enemyNear:F0}");
            }
            catch (Exception ex)
            {
                OnceLog.Warning("transfer:prefix", "[Patch:Transfer] prefix failed: " + ex.Message);
            }
        }

        [HarmonyPostfix]
        internal static void Postfix(TransferSteerState __state)
        {
            if (__state == null || !__state.Changed) return;

            try
            {
                int afterTransferCount = TransferCount(__state.Faction);
                if (afterTransferCount > __state.BeforeTransferCount)
                {
                    Plugin.Log.LogInfo(
                        $"[Patch:Transfer] alliance={__state.AllianceId} obj={__state.ObjectiveId} " +
                        $"queued={afterTransferCount - __state.BeforeTransferCount}");
                }
            }
            catch (Exception ex)
            {
                OnceLog.Warning("transfer:postfix-count", "[Patch:Transfer] postfix count failed: " + ex.Message);
            }
            finally
            {
                try
                {
                    __state.DeficitField?.SetValue(__state.Faction, __state.OriginalDeficit);
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("transfer:restore", "[Patch:Transfer] restore failed: " + ex.Message);
                }
            }
        }

        private static object GetFaction(int aifactionIndex)
        {
            var aicType = AccessTools.TypeByName("AICampaign");
            var list = AccessTools.Field(aicType, "aifaction")?.GetValue(null) as IList;
            if (list == null || aifactionIndex < 0 || aifactionIndex >= list.Count) return null;
            return list[aifactionIndex];
        }

        private static object CreateTransferData(Type factionType, Vector3 position, float ownStrength, float enemyStrength)
        {
            var transferDataType = AccessTools.Inner(factionType, "TransferData");
            if (transferDataType == null)
            {
                OnceLog.Warning("transfer:type:transferdata", "TransferOfUnitsPatch missing AIFaction.TransferData type");
                return null;
            }

            var ctor = AccessTools.Constructor(transferDataType, new[] { typeof(Vector3), typeof(float), typeof(float) });
            if (ctor == null)
            {
                OnceLog.Warning("transfer:ctor:transferdata", "TransferOfUnitsPatch missing AIFaction.TransferData(Vector3,float,float) constructor");
                return null;
            }

            return ctor.Invoke(new object[] { position, ownStrength, enemyStrength });
        }

        private static int TransferCount(object faction)
        {
            var list = AccessTools.Field(faction.GetType(), "unitstobetransfered")?.GetValue(faction) as IList;
            return list?.Count ?? 0;
        }

        private static float SumStrengthNear(IList units, Vector3 position, float radius)
        {
            float total = 0f;
            if (units == null) return total;
            for (int i = 0; i < units.Count; i++)
            {
                var unit = units[i];
                if (unit == null) continue;
                var pos = UnitPosition(unit);
                if (!pos.HasValue) continue;
                if (Tools.GetXZDistance(position, pos.Value) <= radius)
                    total += ReadStrength(unit);
            }
            return total;
        }

        private static float SumStrength(IList units)
        {
            float total = 0f;
            if (units == null) return total;
            for (int i = 0; i < units.Count; i++)
                total += ReadStrength(units[i]);
            return total;
        }

        private static Vector3? UnitPosition(object unit)
        {
            if (unit is Component component) return component.transform.position;
            try
            {
                var method = AccessTools.Method(unit.GetType(), "GetPosition");
                if (method != null) return (Vector3)method.Invoke(unit, null);
            }
            catch { }
            return null;
        }

        private static float ReadStrength(object unit)
        {
            if (unit == null) return 0f;
            string[] fields = { "groupstrengthactive", "groupstrengthdirect", "groupstrength", "strength" };
            for (int i = 0; i < fields.Length; i++)
            {
                var field = AccessTools.Field(unit.GetType(), fields[i]);
                if (field == null) continue;
                try { return Math.Max(0f, Convert.ToSingle(field.GetValue(unit))); }
                catch { return 0f; }
            }
            return 0f;
        }

        private static float ReadGamePrefsFloat(string fieldName, float fallback)
        {
            try
            {
                var field = AccessTools.Field(AccessTools.TypeByName("GamePrefs"), fieldName);
                if (field == null) return fallback;
                return Math.Max(1f, Convert.ToSingle(field.GetValue(null)));
            }
            catch { return fallback; }
        }

        private static float Clamp(float value, float min, float max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }
}
