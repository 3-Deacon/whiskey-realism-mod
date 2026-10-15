using System;
using System.Collections.Generic;
using HarmonyLib;
using WhiskeyRealism.Strategic.BugFixes;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // BUG-LOG-009. Vanilla BattleUnits.GrabSubordinateByStrength (decompile 93549-93597)
    // collects brigades (unittyp 13, or <= 2) of a group, last attached first, "up to"
    // strengthtosearchfor: it tests num + num5 <= strengthtosearchfor but then sets
    // num = num5 (93594) instead of num += num5, so only the last pick is compared and the
    // total can run well past the target. Its one caller, AICampaign.CheckTransferOfUnits
    // (17295), asks for min(deficit, surplus) and then transfers everything returned.
    //
    // The Postfix recomputes the pick over the same candidates in vanilla's order with the
    // running total (SubordinateStrengthSelector) and replaces the returned list's
    // contents. Vanilla's other filters stay: overridetype, minimumamount, transfertime,
    // and the count cap num3 - leaveminimumbrigades. The method has no other side effect
    // on the types it can return (its unittyp 14 branch, 93583-93587, is unreachable).
    // Rollback: Campaign Bug Fixes / Enable Subordinate Strength Grab Fix.
    [HarmonyPatch(typeof(BattleUnits), "GrabSubordinateByStrength")]
    internal static class SubordinateStrengthGrabPatch
    {
        // Vanilla's default when overridetype is null (93563-93570).
        private static readonly List<int> DefaultTypes = new List<int> { 0, 1, 2, 3, 4, 10, 11, 12, 13, 14, 15, 16, 17 };

        [HarmonyPostfix]
        internal static void Postfix(Regiment groupunit, int strengthtosearchfor, int minimumamount, int leaveminimumbrigades, List<int> overridetype, List<Regiment> __result)
        {
            using (TelemetryPerf.Scope("campaign.patch.subordinate-strength-grab.postfix", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("campaign.patch.subordinate-strength-grab.postfix");
                try
                {
                    if (!Enabled() || __result == null || (UnityEngine.Object)groupunit == null || groupunit.allattachedunits == null) return;
                    var types = overridetype ?? DefaultTypes;
                    var attached = groupunit.allattachedunits;

                    int units = 0;
                    for (int i = 0; i < attached.Length; i++)
                        if ((UnityEngine.Object)attached[i] != null && attached[i].unittyp <= 13) units++;

                    var visit = new List<Regiment>(attached.Length);
                    var candidates = new List<SubordinateStrengthSelector.Candidate>(attached.Length);
                    for (int i = attached.Length - 1; i >= 0; i--)
                    {
                        var unit = attached[i];
                        bool present = (UnityEngine.Object)unit != null;
                        visit.Add(unit);
                        candidates.Add(new SubordinateStrengthSelector.Candidate
                        {
                            Eligible = present && (unit.unittyp == 13 || unit.unittyp <= 2) && types.Contains(unit.unittyp),
                            Strength = present ? unit.strength : 0,
                            InTransfer = present && unit.transfertime > 0f,
                        });
                    }

                    var picked = SubordinateStrengthSelector.Select(candidates, strengthtosearchfor, minimumamount, units - leaveminimumbrigades);
                    bool same = picked.Count == __result.Count;
                    for (int k = 0; same && k < picked.Count; k++)
                        same = ReferenceEquals(visit[picked[k]], __result[k]);
                    if (same) return;

                    __result.Clear();
                    for (int k = 0; k < picked.Count; k++)
                        __result.Add(visit[picked[k]]);
                    OnceLog.Info("subordinate-strength-grab:trimmed", "[Patch:SubordinateGrab] a strength-limited brigade grab now stops at the requested strength");
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("subordinate-strength-grab:postfix", "[Patch:SubordinateGrab] postfix failed (vanilla pick kept): " + ex.Message);
                }
            }
        }

        private static bool Enabled()
        {
            try
            {
                return Plugin.Instance != null && Plugin.Instance.Enabled.Value
                    && Plugin.EnableSubordinateStrengthGrabFix != null
                    && Plugin.EnableSubordinateStrengthGrabFix.Value;
            }
            catch { return false; }
        }
    }
}
