namespace WhiskeyRealism.Strategic
{
    public static class DefenseCustomOrderPolicy
    {
        public static bool RequiresCustomOrder(DefenseResponse response)
        {
            if (response == null || response.Threat == null) return false;
            if (response.Threat.SourceKind == DefenseThreatSourceKind.AssetProximity) return false;

            switch (response.Threat.Posture)
            {
                case DefensePosture.ActiveInvasion:
                case DefensePosture.ContainAndCounterattack:
                    return response.Threat.SourceKind == DefenseThreatSourceKind.SeaInvasion ||
                           response.Threat.SourceKind == DefenseThreatSourceKind.RaidForce;
                default:
                    return false;
            }
        }
    }
}
