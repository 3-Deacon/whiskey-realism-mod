using System;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // #55 — Vanilla Regiment.MoveNonAIUnits (decompile 129026, AI alliance
    // only) auto-links an idle, unlinked group/HQ unit to unitrange.
    // closestownunit[0] (129046-129051) without checking command hierarchy.
    // Cross-command links can make HQs follow unrelated troops; this Postfix
    // restores the previous (empty) link when the new link crosses commands.
    // "Same command" uses vanilla's hierarchy: allattachedunits membership
    // (GetComponentsInChildren, 129574) or a shared real parent
    // (BattleUnits.GetParentUnit 93883). Commander Mode != Active: no change.
    [HarmonyPatch(typeof(Regiment), "MoveNonAIUnits")]
    internal static class TacticalHqAutoLinkGuardPatch
    {
        internal readonly struct LinkState
        {
            public LinkState(GameObject previousLink, int previousPosition)
            {
                PreviousLink = previousLink;
                PreviousPosition = previousPosition;
            }

            public GameObject PreviousLink { get; }
            public int PreviousPosition { get; }
        }

        [HarmonyPrefix]
        internal static void Prefix(Regiment __instance, out LinkState __state)
        {
            using (TelemetryPerf.Scope("tactical.patch.hq-link-guard-prefix", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
            {
                __state = new LinkState(
                    __instance != null ? __instance.unitlinkedto : null,
                    __instance != null ? __instance.linkedposition : 0);
            }
        }

        [HarmonyPostfix]
        internal static void Postfix(Regiment __instance, LinkState __state)
        {
            using (TelemetryPerf.Scope("tactical.patch.hq-link-guard", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
            {
                try
                {
                    if ((UnityEngine.Object)__instance == null || __instance.unittyp <= 13) return;
                    GameObject newLink = __instance.unitlinkedto;
                    bool newlyLinked = (UnityEngine.Object)__state.PreviousLink == null && (UnityEngine.Object)newLink != null;
                    if (!newlyLinked) return;
                    if (!BattleWriterPolicy.GateMayIntervene(TacticalWriteGuard.ModeAllowsWrites(), Enabled())) return;

                    Regiment target = newLink.GetComponent<Regiment>();
                    bool clear = TacticalHqLinkGuard.ShouldClearAutoGroupLink(
                        modEnabled: true,
                        newlyLinked: true,
                        sourceIsGroupUnit: true,
                        targetExists: (UnityEngine.Object)target != null,
                        sameHierarchy: SameCommand(__instance, target),
                        sameNonRootParent: false,
                        sameAiGroup: SameAiGroup(__instance, target));
                    if (!clear) return;

                    __instance.unitlinkedto = __state.PreviousLink;
                    __instance.linkedposition = __state.PreviousPosition;

                    OnceLog.Info(
                        "tactical-hq-link-guard:cleared",
                        "[TacticalHqLinkGuard] cleared cross-command auto group link unit=" +
                        SafeUnitName(__instance) + " target=" + SafeUnitName(target));
                }
                catch (Exception ex)
                {
                    OnceLog.Warning(
                        "tactical-hq-link-guard:failed",
                        "[TacticalHqLinkGuard] failed; preserving vanilla link state: " + ex.Message);
                }
            }
        }

        private static bool Enabled()
        {
            return Plugin.Instance != null &&
                Plugin.Instance.Enabled != null &&
                Plugin.Instance.Enabled.Value &&
                Plugin.Instance.EnableTacticalHqLinkGuard != null &&
                Plugin.Instance.EnableTacticalHqLinkGuard.Value;
        }

        private static bool SameCommand(Regiment source, Regiment target)
        {
            if ((UnityEngine.Object)source == null || (UnityEngine.Object)target == null) return false;
            Regiment sourceParent = BattleUnits.GetParentUnit(source);
            Regiment targetParent = BattleUnits.GetParentUnit(target);
            return BattleWriterPolicy.HqLinkSameCommand(
                Contains(source.allattachedunits, target),
                Contains(target.allattachedunits, source),
                source.GetInstanceID(),
                target.GetInstanceID(),
                (UnityEngine.Object)sourceParent != null ? sourceParent.GetInstanceID() : source.GetInstanceID(),
                (UnityEngine.Object)targetParent != null ? targetParent.GetInstanceID() : target.GetInstanceID());
        }

        private static bool Contains(Regiment[] units, Regiment unit)
        {
            if (units == null) return false;
            for (int i = 0; i < units.Length; i++)
                if ((UnityEngine.Object)units[i] == (UnityEngine.Object)unit) return true;
            return false;
        }

        private static bool SameAiGroup(Regiment source, Regiment target)
        {
            if (source == null || target == null) return false;
            return source.groupaiobject != null && source.groupaiobject == target.groupaiobject;
        }

        private static string SafeUnitName(Regiment unit)
        {
            if (unit == null) return "-";
            return TacticalCurrentOrderSignature.Safe(unit.name);
        }
    }
}
