namespace WhiskeyRealism.Strategic
{
    // Which weapon models an alliance can order, by the rule of the player's order screen:
    // GameVars.Weapon.AbleToOrder (63938) rejects GetAvailability (63962) "Not produced" and
    // tooltip 1831, so a model is orderable when it is researched AND produced
    // (productionmultiplier > 0), or imported. Vanilla's GetAvailability reads the player
    // alliance for every caller (63965-63982); this helper takes the asking alliance's own
    // research and import state, so it answers for the AI too. Pure; the runtime reads
    // weapon[id].IsResearched(alliance), IsImported(alliance) and productionmultiplier
    // (WeaponOrderabilityRuntime). Shared with stage G's G9 purchase planner.
    public static class WeaponOrderability
    {
        public static bool IsOrderable(bool researched, bool imported, float productionMultiplier)
        {
            return (researched && productionMultiplier > 0f) || imported;
        }

        // Vanilla AI purchasing (CheckPurchaseWeapons 16296, 16313) takes researched OR
        // imported models; this is a researched model it may pick that the order screen
        // refuses (Gatling 37, Coffee Mill 38, Williams 12: projects 10/11, pm 0).
        public static bool IsResearchedButUnorderable(bool researched, bool imported, float productionMultiplier)
        {
            return researched && !IsOrderable(researched, imported, productionMultiplier);
        }
    }
}
