namespace WhiskeyRealism.Strategic.BugFixes
{
    // BUG-LOG-002. Regiment.UpdateCampsAndEntrenchments (decompile 115392-115470) places
    // a camp only when !iswithinfog (115408), destroys camp and entrenchment when
    // iswithinfog (115426) and places the entrenchment model only when !iswithinfog
    // (115455). For a unit outside the player's alliance iswithinfog means "hidden from
    // the player" (CheckRegimentVisibility 127209-127229), so AI armies the player cannot
    // see never encamp, never take winter quarters and never entrench. Vanilla's fog gate
    // keeps the camp model from revealing the unit; the fix runs the camp logic as if
    // the unit were visible and hides the camp and entrenchment models instead.
    //
    // BUG-LOG-002a. Regiment.CheckModelReplacement (116372-116503) is the same fog gate on
    // embarkation: the river boat, sea ship and train model, and currentmodeltype with it,
    // are created only when !iswithinfog (116383, 116399, 116446) and removed when fogged
    // (116461). A hidden AI land unit is therefore never embarked: no rail speed (133401)
    // or rail terrain cost (132891), it halts at night like marching troops (133958), uses
    // no transport capacity (116471-116501) or locomotives/ships (133900-133915), skips
    // the sea embarkation delay (SetEmbarkmentMode, 116418), eats full rations instead of
    // the transport factor (114186) and keeps receiving reinforcements (114501). The fix
    // runs the model logic as if the unit were visible and hides the model instead.
    internal static class FoggedModelRule
    {
        // Run vanilla's camp logic with iswithinfog cleared for this unit. A unit on a
        // river or the sea is left alone: when visible it shows a boat model there and
        // vanilla gives it no camp (115408 shownmodel == null; CheckModelReplacement
        // 116383-116399 skips the model only because the unit is fogged).
        internal static bool ShouldTreatAsVisibleForCamp(
            bool fixEnabled,
            bool calledFromCampaign,
            bool useFogOfWar,
            bool isPlayerAlliance,
            bool isWithinFog,
            bool onWater)
        {
            return fixEnabled && calledFromCampaign && useFogOfWar && !isPlayerAlliance && isWithinFog && !onWater;
        }

        // Run vanilla's embarkation (model) logic with iswithinfog cleared for this unit.
        // Fleets (unittyp 17) are left to the naval stage: vanilla never removes a fleet's
        // model in fog (116461 excludes them) and fleets have their own visibility rules.
        internal static bool ShouldTreatAsVisibleForEmbarkation(
            bool fixEnabled,
            bool calledFromCampaign,
            bool useFogOfWar,
            bool isPlayerAlliance,
            bool isWithinFog,
            bool isFleet)
        {
            return fixEnabled && calledFromCampaign && useFogOfWar && !isPlayerAlliance && isWithinFog && !isFleet;
        }

        // Camp, entrenchment and transport models of a unit the player cannot see stay
        // hidden; any other unit shows them as vanilla does. Player-alliance units are never
        // touched.
        internal static bool ShouldHideModels(bool isPlayerAlliance, bool isWithinFog)
        {
            return !isPlayerAlliance && isWithinFog;
        }
    }
}
