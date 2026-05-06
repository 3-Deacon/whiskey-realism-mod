using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Patches;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Strategic
{
    // OffensiveAvailabilityWrapper — reflection shim around the vanilla gate
    //   AICampaign.IsUnitAvailableForOffensiveOperations(int, Regiment, Vector3, bool, bool)
    //   (decompile line 14080)
    //
    // The primary path resolves and calls the vanilla method via reflection. If that
    // lookup fails, MirrorVanillaGates() reproduces all 13 known gates without weather
    // (weather is intentionally omitted; the vanilla path covers it when available).
    //
    // Nested-type note: RaidForce, FortConstructionOrder, SeaInvasionForce, and
    // CampaignArmyPanel are nested inside AICampaign in Assembly-CSharp and are not
    // addressable as plain names; they are resolved via AccessTools.TypeByName().
    internal static class OffensiveAvailabilityWrapper
    {
        // Vanilla IsUnitAvailableForOffensiveOperations
        private static MethodInfo _vanillaMethod;
        private static bool _vanillaLookupAttempted;

        // Private IsUnitTakingTown
        private static MethodInfo _isUnitTakingTownMethod;
        private static bool _isUnitTakingTownLookupAttempted;

        // Mirror helpers — cached on first use
        private static MethodInfo _isRaidUnitMethod;
        private static MethodInfo _unitAlreadyConstructingMethod;
        private static MethodInfo _getSeaInvasionRefMethod;
        private static MethodInfo _getReadinessStepMethod;
        private static bool _mirrorHelperLookupAttempted;

        public static bool IsAvailable(int aifactionIndex, Regiment unit, Vector3 operationPosition)
        {
            if (unit == null || aifactionIndex < 0) return false;

            try
            {
                var method = ResolveVanillaMethod();
                if (method != null)
                {
                    return (bool)method.Invoke(null, new object[]
                    {
                        aifactionIndex, unit, operationPosition,
                        /*checkexistingoperations*/ true,
                        /*checkforbadweather*/ true
                    });
                }
            }
            catch (Exception ex)
            {
                OnceLog.Warning("offensive-availability:vanilla",
                    "[OffensiveAvailability] vanilla call failed, falling back to mirror: " + ex.Message);
            }

            return MirrorVanillaGates(aifactionIndex, unit, operationPosition);
        }

        private static MethodInfo ResolveVanillaMethod()
        {
            if (_vanillaMethod != null) return _vanillaMethod;
            if (_vanillaLookupAttempted) return null;
            _vanillaLookupAttempted = true;
            _vanillaMethod = AccessTools.Method(
                typeof(AICampaign),
                "IsUnitAvailableForOffensiveOperations",
                new[] { typeof(int), typeof(Regiment), typeof(Vector3), typeof(bool), typeof(bool) });
            if (_vanillaMethod == null)
                OnceLog.Warning("offensive-availability:lookup",
                    "[OffensiveAvailability] AICampaign.IsUnitAvailableForOffensiveOperations not found — using mirror");
            return _vanillaMethod;
        }

        private static bool MirrorVanillaGates(int aifactionIndex, Regiment unit, Vector3 operationPosition)
        {
            // Mirror of decompile 14080-14157. Every gate must remain in sync if vanilla changes.
            // Weather gate intentionally omitted (brittle field name; vanilla path covers it).
            try
            {
                EnsureMirrorHelpers();

                // Gate 1: readiness step < 2
                if (_getReadinessStepMethod != null)
                {
                    int readiness = Convert.ToInt32(_getReadinessStepMethod.Invoke(null, new object[] { unit }));
                    if (readiness < 2) return false;
                }

                // Gate 2: raid unit
                if (_isRaidUnitMethod != null)
                {
                    bool isRaid = Convert.ToBoolean(_isRaidUnitMethod.Invoke(null, new object[] { unit }));
                    if (isRaid) return false;
                }

                // Gate 3: insufficient strength
                if ((float)unit.groupstrengthactive <= GamePrefs.aiminimumstrengthformovement) return false;

                // Gate 4: insufficient morale
                if (unit.groupmorale <= GamePrefs.aiminimummoraleformovement) return false;

                var faction = AICampaignReflect.GetFaction(aifactionIndex);
                if (faction == null) return false;
                var ftype = faction.GetType();

                // Gate 5: already in offensive operations
                if (ListContains(faction, ftype, "unitsinoffensiveoperations", unit)) return false;

                // Gate 6: already in defensive operations
                if (ListContains(faction, ftype, "unitsindefensiveoperations", unit)) return false;

                // Gate 7: in capital defense group
                if (ListContains(faction, ftype, "groupstodefendcapital", unit)) return false;

                // Gate 8: constructing supply depots
                if (ListContains(faction, ftype, "unitsconstructingsupplydepots", unit)) return false;

                // Gate 9: fort construction order in progress
                if (_unitAlreadyConstructingMethod != null)
                {
                    bool building = Convert.ToBoolean(
                        _unitAlreadyConstructingMethod.Invoke(null, new object[] { unit, null }));
                    if (building) return false;
                }

                // Gate 10: part of a sea invasion force
                if (_getSeaInvasionRefMethod != null)
                {
                    object seaRef = _getSeaInvasionRefMethod.Invoke(null, new object[] { unit });
                    if (seaRef != null) return false;
                }

                // Gate 11: not a fighting force
                if (!AICampaign.UnitIsFightingForce(unit)) return false;

                // Gate 12: outside operations theater
                if (operationPosition != default(Vector3) &&
                    !AICampaign.IsWithinOperationsTheater(unit, operationPosition))
                    return false;

                // Gate 13: unit is currently taking a town (private method, accessed via reflection)
                if (ReflectIsUnitTakingTown(unit)) return false;

                return true;
            }
            catch (Exception ex)
            {
                OnceLog.Warning("offensive-availability:mirror",
                    "[OffensiveAvailability] mirror failed: " + ex.Message);
                return false;
            }
        }

        private static void EnsureMirrorHelpers()
        {
            if (_mirrorHelperLookupAttempted) return;
            _mirrorHelperLookupAttempted = true;

            // RaidForce, FortConstructionOrder, SeaInvasionForce, and CampaignArmyPanel
            // are nested inside AICampaign in Assembly-CSharp; use TypeByName to resolve.
            var raidType = AccessTools.TypeByName("RaidForce");
            if (raidType != null)
                _isRaidUnitMethod = AccessTools.Method(raidType, "IsRaidUnit", new[] { typeof(Regiment) });
            else
                OnceLog.Warning("offensive-availability:lookup-raid",
                    "[OffensiveAvailability] RaidForce type not found — gate 2 skipped in mirror");

            var fortType = AccessTools.TypeByName("FortConstructionOrder");
            if (fortType != null)
                _unitAlreadyConstructingMethod = AccessTools.Method(
                    fortType, "UnitAlreadyConstructing", new[] { typeof(Regiment), fortType });
            else
                OnceLog.Warning("offensive-availability:lookup-fort",
                    "[OffensiveAvailability] FortConstructionOrder type not found — gate 9 skipped in mirror");

            var sifType = AccessTools.TypeByName("SeaInvasionForce");
            if (sifType != null)
                _getSeaInvasionRefMethod = AccessTools.Method(
                    sifType, "GetSeaInvasionForceReference", new[] { typeof(Regiment) });
            else
                OnceLog.Warning("offensive-availability:lookup-sif",
                    "[OffensiveAvailability] SeaInvasionForce type not found — gate 10 skipped in mirror");

            var panelType = AccessTools.TypeByName("CampaignArmyPanel");
            if (panelType != null)
                _getReadinessStepMethod = AccessTools.Method(panelType, "GetReadinessStep", new[] { typeof(Regiment) });
            else
                OnceLog.Warning("offensive-availability:lookup-panel",
                    "[OffensiveAvailability] CampaignArmyPanel type not found — gate 1 skipped in mirror");
        }

        private static bool ReflectIsUnitTakingTown(Regiment unit)
        {
            try
            {
                if (_isUnitTakingTownMethod == null && !_isUnitTakingTownLookupAttempted)
                {
                    _isUnitTakingTownLookupAttempted = true;
                    _isUnitTakingTownMethod = AccessTools.Method(
                        typeof(AICampaign), "IsUnitTakingTown", new[] { typeof(Regiment) });
                    if (_isUnitTakingTownMethod == null)
                        OnceLog.Warning("offensive-availability:lookup-takingtown",
                            "[OffensiveAvailability] AICampaign.IsUnitTakingTown not found — gate 13 skipped in mirror");
                }
                if (_isUnitTakingTownMethod != null)
                    return (bool)_isUnitTakingTownMethod.Invoke(null, new object[] { unit });
            }
            catch (Exception ex)
            {
                OnceLog.Warning("offensive-availability:takingtown-call",
                    "[OffensiveAvailability] IsUnitTakingTown reflection failed: " + ex.Message);
            }
            return false; // fail-open on this single gate; vanilla path covers it normally
        }

        private static bool ListContains(object faction, Type ftype, string fieldName, object element)
        {
            var f = AccessTools.Field(ftype, fieldName);
            if (f == null) return false;
            var list = f.GetValue(faction) as System.Collections.IList;
            return list != null && list.Contains(element);
        }
    }
}
