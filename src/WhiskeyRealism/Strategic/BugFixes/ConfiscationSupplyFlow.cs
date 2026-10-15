namespace WhiskeyRealism.Strategic.BugFixes
{
    // BUG-LOG-012. The Confiscation Act (policy 25) multiplies the alliance's
    // supplyenemysoilfactor by confiscationsupplyflow (Policies.LaunchPolicyEffect,
    // decompile 209893; campaignprefs 2788 "Confiscation, supply flow improvement if
    // unit is on enemy soil, factor [>1]", 1.15; manual: "Supply efficiency of armies
    // operating in enemy states +15%"). Regiment.UpdateIIPSupplyLink multiplies the
    // resupply duration num15 by that factor (114330-114335), so the act made supply on
    // enemy soil 15% slower. Policies.UpdatePolicyEffects resets the factor to 1 and
    // re-applies the acts every update (209621), so storing the reciprocal right after
    // that update makes the duration shorter by the configured improvement.
    internal static class ConfiscationSupplyFlow
    {
        internal static float DurationFactor(float vanillaFactor)
        {
            return vanillaFactor > 0f ? 1f / vanillaFactor : vanillaFactor;
        }
    }
}
