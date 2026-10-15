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
        internal const int EffectAttrition = 22;
        internal const int EffectEngineering = 23;

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

        // BUG-WL-009. Actions 28 "Logistics Corps", 50 "Not Yet Ready" (effect 9)
        // and 27 "Engineer Corps" (effect 23) carry type-1 qualities, which
        // UpdateCumulativeEffects adds to cumulativeeffectsonce (DC:44757-44763);
        // effects 9 and 23 are only ever read from the running multiplier
        // (DC:114348, 87433, 173699), so the promised bonus never applies. The
        // once value is folded into the running multiplier as x(1 + once).
        internal static bool FoldsOnceEffect(int effectId)
        {
            return effectId == EffectSupplyEfficiency || effectId == EffectEngineering;
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

        private static bool IsPositiveFinite(float value)
        {
            return value > 0f && !float.IsInfinity(value) && !float.IsNaN(value);
        }
    }
}
