using System;
using System.Reflection;
using HarmonyLib;

namespace WhiskeyRealism.Strategic
{
    // Pure decision helpers for the campaign military write surfaces (WS C2a).
    // Runtime adapters and Harmony patches gather vanilla state and call these;
    // nothing here touches Unity objects, so the console harness covers them.
    internal static class CampaignMilitaryGates
    {
        // ---- C-DefIntent: sea-invasion landed signal -------------------------
        // Vanilla SeaInvasion exposes two overloads (decompile 141061 instance,
        // 141066 static List<Regiment> variant). The untyped lookup throws
        // AmbiguousMatchException, which silently killed landed-signal condition 3.
        internal static MethodInfo ResolveSeaInvasionStrengthMethod()
        {
            return AccessTools.Method(
                typeof(SeaInvasion),
                "GetUnitStrengthInRange",
                new[] { typeof(int), typeof(bool), typeof(int) });
        }

        // returnbalance=true is the only mode vanilla itself uses (9238, 9433):
        // units at war with `allianceid` add, own/allied units subtract. With
        // returnbalance=false own units ALSO add, so our own garrison at the spot
        // reads as a landing. Ships weigh 0: a landing means troops ashore.
        internal static object[] SeaInvasionBalanceArgs(int defenderAllianceId)
        {
            return new object[] { defenderAllianceId, true, 0 };
        }

        internal static bool IsLandedByStrengthBalance(int enemyMinusFriendly)
        {
            return enemyMinusFriendly > 0;
        }

        // ---- B-46-campaign: snapshot restore --------------------------------
        // Exact in-place restore of a vanilla list from a Prefix snapshot.
        // Idempotent, so Postfix and Finalizer paths can both call it.
        internal static bool RestoreListSnapshot(System.Collections.IList live, System.Collections.Generic.IList<object> snapshot)
        {
            if (live == null || snapshot == null) return false;
            live.Clear();
            for (int i = 0; i < snapshot.Count; i++)
                live.Add(snapshot[i]);
            return true;
        }
    }
}
