using System;
using System.Collections.Generic;
using HarmonyLib;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Vanilla Regiment.UpdateCampaignUnitStats(groupunit, state) (decompile 114965) runs
    // on every regiment of a campaign group (UpdateGroupStrength 120409). Out of battle it
    // scales wounded mortality by woundedtokilledambulancecorps when HasPerk(8) (115113-115120)
    // and, on a forced march, sickness by attritionfootcavalry when HasPerk(6) (115153-115162),
    // but it asks the regiment, not the group. A regiment's ids are battle ids (8 Expert
    // Scouts, 6 Ace of Spade), and HasPerkParent needs unittyp 15 (132750), so the campaign
    // Ambulance Corps and Foot Cavalry never apply and Expert Scouts / Ace of Spade regiments
    // get their effects instead (BUG-AI-005). Both factors are locals, so this Prefix gives the
    // regiment, for the length of the call, a perks list holding only ids 8/6 at level 0 and
    // points GamePrefs.woundedtokilledambulancecorps / attritionfootcavalry at arrays holding
    // the group's own x parent-army factor (vanilla's own rule, e.g. RegimentMove 133418-133425).
    // Nothing else in the call reads the regiment's perks or those arrays; the experience
    // grants already go to groupunit (115122). The Finalizer restores everything even if
    // vanilla throws. Same rule for every side, W&L player included.
    // Rollback: Campaign / Fix Perk Id Mismatches.
    [HarmonyPatch(typeof(Regiment), nameof(Regiment.UpdateCampaignUnitStats))]
    internal static class CampaignGroupPerkPatch
    {
        private static readonly List<Regiment.Perk> _scratchPerks = new List<Regiment.Perk>(2);
        private static readonly Regiment.Perk _ambulance = new Regiment.Perk { perk = VanillaPerkTables.CampaignAmbulanceCorps, perklevel = 0 };
        private static readonly Regiment.Perk _footCavalry = new Regiment.Perk { perk = VanillaPerkTables.CampaignFootCavalry, perklevel = 0 };
        private static readonly float[] _ambulanceFactors = new float[3];
        private static readonly float[] _footCavalryFactors = new float[3];

        private static Regiment _rewritten;
        private static List<Regiment.Perk> _savedPerks;
        private static float[] _savedAmbulance;
        private static float[] _savedFootCavalry;

        // One group serves all its regiments in the same frame; its parent lookup walks
        // transform.parent (HasPerkParent 132748), so the factors are computed once per frame.
        private static Regiment _cacheGroup;
        private static int _cacheFrame = -1;
        private static float _cacheAmbulance = 1f;
        private static float _cacheFootCavalry = 1f;

        static CampaignGroupPerkPatch()
        {
            CampaignScope.Register("perk-ids-group-perk-cache", () =>
            {
                _cacheGroup = null;
                _cacheFrame = -1;
            });
        }

        [HarmonyPrefix]
        internal static void Prefix(Regiment __instance, Regiment groupunit)
        {
            using (TelemetryPerf.Scope("campaign.patch.perk-ids.group-perks.prefix", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("campaign.patch.perk-ids.group-perks.prefix");
                try
                {
                    Restore();
                    if ((UnityEngine.Object)__instance == null || (UnityEngine.Object)groupunit == null || !Enabled()) return;
                    if (VanillaPerkTables.TableFor(__instance.unittyp) != PerkTable.Battle || __instance.perks == null) return;

                    GroupFactors(groupunit, out float ambulance, out float footCavalry);
                    var plan = VanillaPerkTables.PlanCampaignWounded(
                        __instance.unittyp,
                        groupunit.unittyp,
                        __instance.HasPerk(VanillaPerkTables.BattleExpertScouts),
                        __instance.HasPerk(VanillaPerkTables.BattleAceOfSpade),
                        ambulance,
                        footCavalry);
                    if (!plan.Rewrite) return;

                    _scratchPerks.Clear();
                    if (plan.AmbulanceFactor != 1f && GamePrefs.woundedtokilledambulancecorps != null)
                    {
                        Fill(_ambulanceFactors, plan.AmbulanceFactor);
                        _savedAmbulance = GamePrefs.woundedtokilledambulancecorps;
                        GamePrefs.woundedtokilledambulancecorps = _ambulanceFactors;
                        _scratchPerks.Add(_ambulance);
                    }
                    if (plan.FootCavalryFactor != 1f && GamePrefs.attritionfootcavalry != null)
                    {
                        Fill(_footCavalryFactors, plan.FootCavalryFactor);
                        _savedFootCavalry = GamePrefs.attritionfootcavalry;
                        GamePrefs.attritionfootcavalry = _footCavalryFactors;
                        _scratchPerks.Add(_footCavalry);
                    }
                    _savedPerks = __instance.perks;
                    _rewritten = __instance;
                    __instance.perks = _scratchPerks;

                    OnceLog.Info("perk-ids:group-perks", "[Patch:PerkIds] UpdateCampaignUnitStats: regiment wounded mortality / forced-march sickness read from its group's Ambulance Corps / Foot Cavalry");
                }
                catch (Exception ex)
                {
                    Restore();
                    OnceLog.Warning("perk-ids:group-perks:prefix", "[Patch:PerkIds] group perk prefix failed, vanilla left as is: " + ex.Message);
                }
            }
        }

        [HarmonyFinalizer]
        internal static void Finalizer()
        {
            using (TelemetryPerf.Scope("campaign.patch.perk-ids.group-perks.finalizer", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
                PatchFirstFire.Mark("campaign.patch.perk-ids.group-perks.finalizer");
                Restore();
            }
        }

        private static void GroupFactors(Regiment group, out float ambulance, out float footCavalry)
        {
            ambulance = 1f;
            footCavalry = 1f;
            if (!VanillaPerkTables.GroupPerksApply(group.unittyp)) return;

            int frame = UnityEngine.Time.frameCount;
            if (ReferenceEquals(group, _cacheGroup) && frame == _cacheFrame)
            {
                ambulance = _cacheAmbulance;
                footCavalry = _cacheFootCavalry;
                return;
            }

            int amb = VanillaPerkTables.CampaignAmbulanceCorps;
            int foot = VanillaPerkTables.CampaignFootCavalry;
            ambulance = VanillaPerkTables.GroupPerkFactor(GamePrefs.woundedtokilledambulancecorps,
                group.HasPerk(amb), group.GetPerkLevel(amb), group.HasPerkParent(amb), group.GetPerkLevelParent(amb));
            footCavalry = VanillaPerkTables.GroupPerkFactor(GamePrefs.attritionfootcavalry,
                group.HasPerk(foot), group.GetPerkLevel(foot), group.HasPerkParent(foot), group.GetPerkLevelParent(foot));

            _cacheGroup = group;
            _cacheFrame = frame;
            _cacheAmbulance = ambulance;
            _cacheFootCavalry = footCavalry;
        }

        private static void Fill(float[] factors, float value)
        {
            for (int i = 0; i < factors.Length; i++) factors[i] = value;
        }

        private static void Restore()
        {
            try
            {
                if ((object)_rewritten != null && ReferenceEquals(_rewritten.perks, _scratchPerks))
                    _rewritten.perks = _savedPerks;
                if (_savedAmbulance != null && ReferenceEquals(GamePrefs.woundedtokilledambulancecorps, _ambulanceFactors))
                    GamePrefs.woundedtokilledambulancecorps = _savedAmbulance;
                if (_savedFootCavalry != null && ReferenceEquals(GamePrefs.attritionfootcavalry, _footCavalryFactors))
                    GamePrefs.attritionfootcavalry = _savedFootCavalry;
            }
            catch (Exception ex)
            {
                OnceLog.Warning("perk-ids:group-perks:restore", "[Patch:PerkIds] group perk restore failed: " + ex.Message);
            }
            _rewritten = null;
            _savedPerks = null;
            _savedAmbulance = null;
            _savedFootCavalry = null;
        }

        private static bool Enabled()
        {
            try
            {
                return Plugin.Instance != null && Plugin.Instance.Enabled.Value
                    && Plugin.FixPerkIdMismatches != null && Plugin.FixPerkIdMismatches.Value;
            }
            catch { return false; }
        }
    }
}
