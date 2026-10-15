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
    internal static class FoggedCampRule
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

        // Camp and entrenchment models of a unit the player cannot see stay hidden; any
        // other unit shows them as vanilla does. Player-alliance units are never touched.
        internal static bool ShouldHideModels(bool isPlayerAlliance, bool isWithinFog)
        {
            return !isPlayerAlliance && isWithinFog;
        }
    }
}
