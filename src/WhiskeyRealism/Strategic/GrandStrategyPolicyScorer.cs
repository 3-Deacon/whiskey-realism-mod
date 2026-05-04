namespace WhiskeyRealism.Strategic
{
    public static class GrandStrategyPolicyScorer
    {
        public static float PolicyWeight(GrandStrategyProfile profile, int alliance, int policyId)
        {
            if (profile == null) return 0f;

            if (alliance == 1)
                return CsaWeight(profile, policyId);

            return UnionWeight(profile, policyId);
        }

        private static float UnionWeight(GrandStrategyProfile profile, int policyId)
        {
            switch (policyId)
            {
                case 35: // Arming Civilian Ships
                    return (profile.WeightFor(StrategyTag.Blockade) * 0.85f)
                         + (profile.WeightFor(StrategyTag.PortAccess) * 0.20f);
                case 41: // Legal Blockade
                    return profile.WeightFor(StrategyTag.Blockade) * 1.00f;
                case 30:
                case 31:
                case 32: // Northern Routes / Feed Europe chain
                    return (profile.WeightFor(StrategyTag.Agriculture) * 0.50f)
                         + (profile.WeightFor(StrategyTag.IndustrialBase) * 0.35f)
                         + (profile.WeightFor(StrategyTag.Logistics) * 0.20f);
                case 33:
                case 34:
                case 44: // USCT / Emancipation / Abolition chain
                    return (profile.WeightFor(StrategyTag.Manpower) * 0.45f)
                         + (profile.WeightFor(StrategyTag.ArmyDestruction) * 0.20f);
                case 39:
                case 40:
                case 45:
                case 46: // Enrollment / bounty escalations
                    return profile.WeightFor(StrategyTag.Recruitment) * 0.65f;
                default:
                    return 0f;
            }
        }

        private static float CsaWeight(GrandStrategyProfile profile, int policyId)
        {
            switch (policyId)
            {
                case 103:
                case 104:
                case 105:
                case 106: // King Cotton chain
                    return (profile.WeightFor(StrategyTag.ForeignRecognition) * 0.55f)
                         + (profile.WeightFor(StrategyTag.PortAccess) * 0.25f);
                case 115:
                case 116:
                case 117:
                case 118:
                case 119: // Diplomacy branch
                    return (profile.WeightFor(StrategyTag.ForeignRecognition) * 0.70f)
                         + (profile.WeightFor(StrategyTag.ArmsImports) * 0.25f);
                case 141: // Free Trade
                    return (profile.WeightFor(StrategyTag.ForeignRecognition) * 0.45f)
                         + (profile.WeightFor(StrategyTag.PortAccess) * 0.60f);
                case 142: // Organized Blockade Running
                    return (profile.WeightFor(StrategyTag.PortAccess) * 0.70f)
                         + (profile.WeightFor(StrategyTag.ArmsImports) * 0.35f);
                case 143: // Letters of Marque
                    return (profile.WeightFor(StrategyTag.TradeWarfare) * 0.70f)
                         + (profile.WeightFor(StrategyTag.PortAccess) * 0.20f);
                case 139:
                case 140:
                case 145:
                case 146: // Conscription / bounty escalations
                    return profile.WeightFor(StrategyTag.Manpower) * 0.65f;
                case 135: // Arming Civilian Ships
                    return profile.WeightFor(StrategyTag.Blockade) * 0.30f;
                default:
                    return 0f;
            }
        }
    }
}
