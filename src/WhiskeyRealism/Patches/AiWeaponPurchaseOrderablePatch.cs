using System;
using HarmonyLib;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // BUG-AI-017. Vanilla AICampaign.CheckPurchaseWeapons (16259) builds its order candidates
    // from models that are researched OR imported and have no open order (16296, 16313), then
    // orders one at random. The player's order screen also requires a researched model to be
    // produced (AbleToOrder 63938 -> GetAvailability 63962: productionmultiplier > 0), so the
    // AI alone can order the Gatling (37), Coffee Mill (38) and Williams Gun (12), which
    // projects 10/11 research at pm 0 (UpdateProjectEffects 213113-213124) to hand out a
    // one-shot stock (212945-212950). With no production and no import their availability
    // floors at 0.01 (GetDeliveryTime 64000): the AI pays the whole order up front
    // (227162) for up to two years of delivery. The discarded `j == 37 || j == 38` at 16289
    // is the remains of that exclusion.
    //
    // The Prefix hides the ordering alliance from the research list of every model it has
    // researched but cannot order (WeaponOrderability, the order screen's rule computed for
    // that alliance), so vanilla's own candidate lists, coverage count (stock counts with or
    // without research), random pick and Count > 0 gates run unchanged over orderable models
    // only. The Finalizer restores the lists. No call is skipped. Units still receive the
    // project stock through vanilla's upgrade loop, which reads stock, not research.
    // Rollback: Campaign Bug Fixes / Enable AI Weapon Purchase Orderable Fix.
    [HarmonyPatch(typeof(AICampaign), nameof(AICampaign.CheckPurchaseWeapons))]
    internal static class AiWeaponPurchaseOrderablePatch
    {
        private static readonly ListMemberMask<GameVars.Weapon> Research = new ListMemberMask<GameVars.Weapon>(
            weapon => weapon.techavailableforalliance,
            (weapon, list) => weapon.techavailableforalliance = list);

        [HarmonyPrefix]
        internal static void Prefix(int _alliance)
        {
            using (TelemetryPerf.Scope("campaign.patch.ai-weapon-purchase-orderable.prefix", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("campaign.patch.ai-weapon-purchase-orderable.prefix");
                try
                {
                    if (!Enabled() || !Research.Begin()) return;
                    var weapons = GameVars.weapon;
                    if (weapons == null) return;
                    for (int id = 0; id < weapons.Length; id++)
                    {
                        if (weapons[id] == null || !WeaponOrderabilityRuntime.IsResearchedButUnorderable(_alliance, id)) continue;
                        Research.Hide(weapons[id], _alliance);
                    }
                    if (Research.Count > 0)
                        OnceLog.Info("ai-weapon-purchase-orderable", "[Patch:AiWeaponOrderable] AI weapon purchases skip researched models the order screen refuses (not produced, not imported)");
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("ai-weapon-purchase-orderable:prefix", "[Patch:AiWeaponOrderable] prefix failed (vanilla candidates kept): " + ex.Message);
                }
            }
        }

        [HarmonyFinalizer]
        internal static void Finalizer()
        {
            using (TelemetryPerf.Scope("campaign.patch.ai-weapon-purchase-orderable.finalizer", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("campaign.patch.ai-weapon-purchase-orderable.finalizer");
                string failure = Research.Restore();
                if (failure != null)
                    OnceLog.Warning("ai-weapon-purchase-orderable:restore", "[Patch:AiWeaponOrderable] restoring research lists failed: " + failure);
            }
        }

        private static bool Enabled()
        {
            try
            {
                return Plugin.Instance != null && Plugin.Instance.Enabled.Value
                    && Plugin.EnableAiWeaponPurchaseOrderableFix != null
                    && Plugin.EnableAiWeaponPurchaseOrderableFix.Value;
            }
            catch { return false; }
        }
    }
}
