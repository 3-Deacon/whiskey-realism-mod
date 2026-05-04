using System.Collections.Generic;

namespace WhiskeyRealism.Strategic
{
    public static class GrandStrategyRegistry
    {
        private static readonly Dictionary<int, GrandStrategyProfile> Profiles = BuildProfiles();

        public static GrandStrategyProfile Resolve(int allianceId, EraStage stage)
        {
            int key = ((allianceId == 1 ? 1 : 0) * 10) + (int)stage;
            if (Profiles.TryGetValue(key, out var profile))
                return profile;

            int fallback = ((allianceId == 1 ? 1 : 0) * 10) + (int)EraStage.Amateur1861;
            return Profiles[fallback];
        }

        private static Dictionary<int, GrandStrategyProfile> BuildProfiles()
        {
            var profiles = new Dictionary<int, GrandStrategyProfile>();
            Add(profiles, ResolveUnion(EraStage.Amateur1861));
            Add(profiles, ResolveUnion(EraStage.Operational1862));
            Add(profiles, ResolveUnion(EraStage.Decisive1863));
            Add(profiles, ResolveUnion(EraStage.TotalWar1864));
            Add(profiles, ResolveCsa(EraStage.Amateur1861));
            Add(profiles, ResolveCsa(EraStage.Operational1862));
            Add(profiles, ResolveCsa(EraStage.Decisive1863));
            Add(profiles, ResolveCsa(EraStage.TotalWar1864));
            return profiles;
        }

        private static void Add(Dictionary<int, GrandStrategyProfile> profiles, GrandStrategyProfile profile)
        {
            profiles[(profile.AllianceId * 10) + (int)profile.EraStage] = profile;
        }

        private static GrandStrategyProfile ResolveUnion(EraStage stage)
        {
            if (stage == EraStage.TotalWar1864)
            {
                return Base(0, stage, "Union Late Exhaustion")
                    .WithTag(StrategyTag.ArmyDestruction, 1.35f)
                    .WithTag(StrategyTag.RailHub, 1.15f)
                    .WithTag(StrategyTag.IndustrialBase, 1.05f)
                    .WithTag(StrategyTag.Blockade, 0.95f)
                    .WithProject(104, 1.20f) // Subsidize Industry
                    .WithProject(100, 1.10f); // Logistics Reforms
            }

            if (stage == EraStage.Operational1862 || stage == EraStage.Decisive1863)
            {
                return Base(0, stage, "Union Coordinated Pressure")
                    .WithTag(StrategyTag.RiverControl, 1.25f)
                    .WithTag(StrategyTag.RailHub, 1.05f)
                    .WithTag(StrategyTag.Blockade, 1.05f)
                    .WithTag(StrategyTag.Logistics, 1.00f)
                    .WithProject(31, 1.15f) // Ironclad Gunboats
                    .WithProject(100, 1.10f) // Logistics Reforms
                    .WithProject(105, 1.00f); // Subsidize Agriculture
            }

            return Base(0, EraStage.Amateur1861, "Union Early Anaconda")
                .WithTag(StrategyTag.Blockade, 1.25f)
                .WithTag(StrategyTag.RiverControl, 1.15f)
                .WithTag(StrategyTag.Logistics, 1.00f)
                .WithTag(StrategyTag.IndustrialBase, 0.90f)
                .WithTag(StrategyTag.CapitalDefense, 0.45f)
                .WithProject(35, 1.20f) // Modern Warships
                .WithProject(41, 1.25f) // Warrior Class
                .WithProject(31, 1.10f) // Ironclad Gunboats
                .WithProject(100, 1.00f); // Logistics Reforms
        }

        private static GrandStrategyProfile ResolveCsa(EraStage stage)
        {
            if (stage == EraStage.TotalWar1864)
            {
                return Base(1, stage, "CSA Late Protraction")
                    .WithTag(StrategyTag.CapitalDefense, 1.35f)
                    .WithTag(StrategyTag.ArmyDestruction, 0.95f)
                    .WithTag(StrategyTag.Manpower, 1.20f)
                    .WithTag(StrategyTag.TradeWarfare, 1.05f)
                    .WithProject(118, 1.20f) // Training Manuals
                    .WithProject(100, 1.05f); // Logistics Reforms
            }

            if (stage == EraStage.Operational1862 || stage == EraStage.Decisive1863)
            {
                return Base(1, stage, "CSA Offensive Defensive")
                    .WithTag(StrategyTag.CapitalDefense, 1.20f)
                    .WithTag(StrategyTag.DefensiveDepth, 1.10f)
                    .WithTag(StrategyTag.ArmsImports, 1.10f)
                    .WithTag(StrategyTag.TradeWarfare, 1.05f)
                    .WithProject(6, 1.10f) // Confederate Rifles
                    .WithProject(37, 1.10f) // Armored Gunboats
                    .WithProject(106, 1.00f); // Trade Warfare
            }

            return Base(1, EraStage.Amateur1861, "CSA Early Cordon")
                .WithTag(StrategyTag.CapitalDefense, 1.30f)
                .WithTag(StrategyTag.DefensiveDepth, 1.15f)
                .WithTag(StrategyTag.ForeignRecognition, 1.20f)
                .WithTag(StrategyTag.ArmsImports, 1.05f)
                .WithTag(StrategyTag.PortAccess, 0.95f)
                .WithTag(StrategyTag.Blockade, 0.20f)
                .WithProject(0, 1.00f) // Austrian Rifles
                .WithProject(1, 1.05f) // British Rifles
                .WithProject(6, 1.15f) // Confederate Rifles
                .WithProject(37, 1.05f) // Armored Gunboats
                .WithProject(120, 0.95f); // Improvised Shipyards
        }

        private static GrandStrategyProfile Base(int allianceId, EraStage stage, string name)
        {
            return new GrandStrategyProfile
            {
                AllianceId = allianceId,
                EraStage = stage,
                Name = name
            };
        }
    }
}
