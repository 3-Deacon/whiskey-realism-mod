using System;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Vanilla Regiment.MoveNonAIUnits auto-links idle group/HQ units to the
    // closest own unit without checking command hierarchy. Cross-command links
    // can make HQs follow unrelated troops.
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
            if (!Enabled())
            {
                __state = default;
                return;
            }

            __state = new LinkState(
                __instance != null ? __instance.unitlinkedto : null,
                __instance != null ? __instance.linkedposition : 0);
        }

        [HarmonyPostfix]
        internal static void Postfix(Regiment __instance, LinkState __state)
        {
            if (!Enabled()) return;

            try
            {
                if (__instance == null) return;
                GameObject newLink = __instance.unitlinkedto;
                bool newlyLinked = __state.PreviousLink == null && newLink != null;
                Regiment target = newLink != null ? newLink.GetComponent<Regiment>() : null;

                bool clear = TacticalHqLinkGuard.ShouldClearAutoGroupLink(
                    modEnabled: true,
                    newlyLinked: newlyLinked,
                    sourceIsGroupUnit: __instance.unittyp > 13,
                    targetExists: target != null,
                    sameHierarchy: SameHierarchy(__instance, target),
                    sameNonRootParent: SameNonRootParent(__instance, target),
                    sameAiGroup: SameAiGroup(__instance, target));
                if (!clear) return;

                string sourceName = SafeUnitName(__instance);
                string targetName = SafeUnitName(target);
                __instance.unitlinkedto = __state.PreviousLink;
                __instance.linkedposition = __state.PreviousPosition;

                OnceLog.Info(
                    "tactical-hq-link-guard:" + sourceName + ":" + targetName,
                    "[TacticalHqLinkGuard] cleared cross-command auto group link unit=" +
                    sourceName + " target=" + targetName);
            }
            catch (Exception ex)
            {
                OnceLog.Warning(
                    "tactical-hq-link-guard:failed",
                    "[TacticalHqLinkGuard] failed; preserving vanilla link state: " + ex.Message);
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

        private static bool SameHierarchy(Regiment source, Regiment target)
        {
            if (source == null || target == null) return false;
            Transform sourceTransform = ((Component)source).transform;
            Transform targetTransform = ((Component)target).transform;
            return sourceTransform.IsChildOf(targetTransform) || targetTransform.IsChildOf(sourceTransform);
        }

        private static bool SameNonRootParent(Regiment source, Regiment target)
        {
            if (source == null || target == null) return false;
            Transform sourceParent = ((Component)source).transform.parent;
            Transform targetParent = ((Component)target).transform.parent;
            if (sourceParent == null || targetParent == null) return false;
            if (sourceParent != targetParent) return false;
            return sourceParent.name != "Units";
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
