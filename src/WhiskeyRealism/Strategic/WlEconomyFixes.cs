using System;

namespace WhiskeyRealism.Strategic
{
    // What a steered vanilla CheckTransferOfUnits call actually did with the
    // unit it appended to unitstobetransfered (DC:17327).
    internal enum WlTransferOutcome
    {
        // SetTransfer ran (DC:93147): the unit is in transit.
        Queued,
        // BattleUnits.MoveUnit returned at its career guard (DC:93099): nothing moved.
        BlockedByCareerGuard,
        // No transit outside a career: re-parented inside its own corps, or refused by MoveUnit's type gates.
        NoTransit,
    }

    // Pure decisions for the W&L player-economy fixes (BUG-WL-005..010; plan
    // docs/superpowers/plans/2026-10-15-wl-economy-bugs.md). The patches in
    // Patches/Wl*Patch.cs and TransferOfUnitsPatch read vanilla state and call these.
    internal static class WlEconomyFixes
    {
        // Effect ids (DLC_WL constants, DC:43023-43093; effectdescriptions.dat).
        internal const int EffectSupplyEfficiency = 9;
        internal const int EffectFoodSupplies = 10;
        internal const int EffectAmmunition = 11;
        internal const int EffectReinforcementSpeed = 14;
        internal const int EffectMorale = 16;
        internal const int EffectFightingSpirit = 17;
        internal const int EffectContractRenewal = 19;
        internal const int EffectAttrition = 22;
        internal const int EffectEngineering = 23;
        internal const int EffectStaffEfficiency = 27;

        // Player effects the campaign army panel lists without an ownership test
        // (CampaignArmyPanel.UpdateStats, DC:173699-173702, 173742-173747).
        internal static readonly int[] PanelStaffEffects =
        {
            EffectSupplyEfficiency, EffectFoodSupplies, EffectAmmunition, EffectReinforcementSpeed, EffectMorale, EffectFightingSpirit,
        };

        // Camp station "Inspect Logistics" (camp.dat; applied at DC:114349-114351).
        internal const int CampStationInspectLogistics = 5;

        // Highest basic-unit type; 14 division, 15 corps, 16 army (DC:12788-12798, 17134).
        internal const int MaxBasicUnitType = 13;

        // BUG-WL-005. Sickness and desertion read EffectMultiplier(22) with the
        // default isplayerunit=true (DC:115149-115151, 115196-115198) inside a
        // method that runs for every regiment (DC:120260, 120409). Medical,
        // training and readiness scope their effects by the campaign group's
        // ownership (DC:115107, 115243, 115276); attrition gets the same rule.
        internal static bool NeutralizeAttritionBonus(bool careerActive, bool groupOwned)
        {
            return careerActive && !groupOwned;
        }

        // A Prefix that overwrote a cumulative-effect slot restores it only if the
        // array is still the one it wrote and nothing rewrote the slot meanwhile.
        internal static bool ShouldRestore(bool sameArray, float current, float written)
        {
            return sameArray && current.Equals(written);
        }

        // BUG-WL-006. GetAverageSupplySubordinates / AddSuppliesSubordinates test
        // currentcommand.unittyp <= 13 where the attached unit's type was meant
        // (DC:47392, 47428). The recipient rule with the attached unit's type:
        internal static bool IsSupplyRecipient(bool present, bool owned, int unitType)
        {
            return present && owned && unitType <= MaxBasicUnitType;
        }

        // For a basic-unit command every attached unit is a basic unit too, so
        // vanilla's test already equals the per-unit one; only HQ commands
        // (division, corps, army) lose every recipient.
        internal static bool NeedsSubordinateTypeCorrection(int commandUnitType)
        {
            return commandUnitType > MaxBasicUnitType;
        }

        // DC:47387-47407: strength-weighted shortage, denominator floored at 1, clamped to [0, 1].
        internal static float AverageShortage(float weightedShortage, float totalStrength)
        {
            float value = weightedShortage / Math.Max(1f, totalStrength);
            if (value > 1f) value = 1f;
            if (value < 0f) value = 0f;
            return value;
        }

        // DC:47438-47446: fill ratio per recipient, max(0, amount / shortage) capped at 1.
        internal static float FillRatio(float amount, float totalShortage)
        {
            if (!(totalShortage > 0f)) return 0f;
            float ratio = Math.Max(0f, amount / totalShortage);
            return ratio > 1f ? 1f : ratio;
        }

        // BUG-WL-007. UpdateIIPSupplyLink keys the player's supply staff (effects
        // 9/10/11/14) and camp station 5 off the map group's own flag
        // (DC:114192-114195, 114346-114351, 114553-114555). The map group of a
        // brigade/regiment player, or of a division inside an AI corps, is the AI
        // parent, which IsUnitUnderCommander never flags (DC:91203-91217). Vanilla
        // itself names that group the player's campaign unit: Camp.UpdateUnitRef
        // (DC:172032) and DLC_WL.IsPlayerPartOfUnit (DC:47578). Only that group,
        // never any other AI group, is treated as the player's for the call.
        internal static bool TreatMapGroupAsPlayerOwned(bool careerActive, bool groupOwned, bool isPlayersMapGroup)
        {
            return careerActive && !groupOwned && isPlayersMapGroup;
        }

        // BUG-WL-011. Per-regiment stats key the player's effects on the campaign
        // group: medical/training/readiness and camp station 11 on groupunit
        // (DC:115107, 115243, 115276-115279), morale effect 16 on
        // parentcampaignunit (DC:127874). For a player below the map group these
        // are inert, as in BUG-WL-007. These stats are per regiment, so the group
        // counts as the player's only while vanilla works on one of the player's
        // own regiments inside the map group carrying the player's command (the
        // readiness and attrition tooltips are per unit too, DC:173552, 173637);
        // AI peers in that group keep vanilla values.
        internal static bool RaiseGroupForPlayerUnit(bool careerActive, bool groupOwned, bool unitOwned, bool groupIsPlayersMapGroup)
        {
            return careerActive && !groupOwned && unitOwned && groupIsPlayersMapGroup;
        }

        // BUG-WL-008. num15 in UpdateIIPSupplyLink is a resupply duration
        // (campaignprefs supplyspeeddistance0/2000: "within this time the whole
        // unit is resupplied"; per-tick supply = need x elapsed / num15,
        // DC:114379), yet vanilla multiplies it by the supply-efficiency bonus
        // and by Camp.GetModifier(5) (DC:114348-114351), so a better bonus slows
        // resupply. Returns the running[9] value that makes vanilla's
        // `num15 *= running[9]; num15 *= camp` equal `num15 /= (effect x camp)`;
        // NaN when a factor is not a positive finite number (leave vanilla).
        internal static float SignCorrectedSupplyEfficiency(float effect, float campModifier)
        {
            if (!IsPositiveFinite(effect) || !IsPositiveFinite(campModifier)) return float.NaN;
            float value = 1f / (effect * campModifier * campModifier);
            return IsPositiveFinite(value) ? value : float.NaN;
        }

        // BUG-WL-009. These actions carry type-1 qualities, which
        // UpdateCumulativeEffects adds to cumulativeeffectsonce (DC:44757-44763),
        // on effects that are only ever read from the running multiplier, so the
        // bonus their text promises never applies (actions.dat):
        //   9  supply efficiency  28 Logistics Corps, 50 Not Yet Ready (DC:114348)
        //   17 fighting spirit    31 High Spirits "improve morale in your unit" (DC:116687, 116738)
        //   19 contract renewal   36/37 Recruitment Campaign, 46 Camp Pleasures, 64 Uncle Sam,
        //                         65 Irish Legion "re-sign their contracts" (DC:113593)
        //   23 engineering        27 Engineer Corps (DC:87433)
        //   27 staff efficiency   30 Staff Standards, 66 Examine Details (DC:44559-44583)
        // The once value is folded into the running multiplier as x(1 + once).
        // Not folded: 22 (actions 40/41/49: texts never mention attrition and "+"
        // would raise it), 0 (item qualities whose texts promise other things) and
        // 3 (companion type-0 data, rank progression never promised).
        internal static bool FoldsOnceEffect(int effectId)
        {
            return effectId == EffectSupplyEfficiency || effectId == EffectFightingSpirit || effectId == EffectContractRenewal
                || effectId == EffectEngineering || effectId == EffectStaffEfficiency;
        }

        internal static float FoldOnceIntoRunning(float running, float once)
        {
            return running * Math.Max(0f, 1f + once);
        }

        // BUG-WL-010. Vanilla CheckTransferOfUnits first drops every entry whose
        // transfertime <= 0 (DC:17255-17262), then appends at most one unit
        // (DC:17327). staleBefore = entries with transfertime <= 0 before the call.
        internal static int TransferAddedCount(int before, int staleBefore, int after)
        {
            return Math.Max(0, after - (before - staleBefore));
        }

        internal static WlTransferOutcome ClassifyTransfer(bool transitStarted, bool careerActive)
        {
            if (transitStarted) return WlTransferOutcome.Queued;
            return careerActive ? WlTransferOutcome.BlockedByCareerGuard : WlTransferOutcome.NoTransit;
        }

        // The running[9] value to write for one UpdateIIPSupplyLink call (NaN: leave
        // vanilla). With the sign fix on, every owned group gets the corrected
        // direction. With it off, a natively owned group keeps vanilla (the
        // rollback), and a group owned only through the staff scope fix gets
        // 1 / camp, which cancels effect 9 and camp station 5 to neutral, so the
        // scope fix never carries vanilla's inverted direction to a new player
        // (it still carries effects 10/11/14, which point the right way).
        internal static float SupplyEfficiencyForCall(bool nativelyOwned, bool raisedByScope, bool signFixOn, float effect, float campModifier)
        {
            if (!nativelyOwned && !raisedByScope) return float.NaN;
            if (signFixOn) return SignCorrectedSupplyEfficiency(effect, campModifier);
            if (nativelyOwned || !IsPositiveFinite(campModifier)) return float.NaN;
            return 1f / campModifier;
        }

        // BUG-WL-012. The campaign army panel lists the player's staff supply,
        // morale and fighting-spirit effects for whatever group is selected
        // (DC:173699-173702, 173742-173747). Show them only on a group the player
        // owns or the map group carrying the player's command.
        internal static bool HidePlayerStaffEffectsOnPanel(bool careerActive, bool panelUnitOwned, bool isPlayersMapGroup)
        {
            return careerActive && !panelUnitOwned && !isPlayersMapGroup;
        }

        // BUG-WL-009: effect 9 is folded only with the sign fix on (BUG-WL-008).
        internal static bool OnceFoldNeedsSignFix(int effectId)
        {
            return effectId == EffectSupplyEfficiency;
        }

        private static bool IsPositiveFinite(float value)
        {
            return value > 0f && !float.IsInfinity(value) && !float.IsNaN(value);
        }
    }
}
