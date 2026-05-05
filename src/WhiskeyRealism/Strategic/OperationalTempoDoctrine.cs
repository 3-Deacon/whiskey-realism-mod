namespace WhiskeyRealism.Strategic
{
    public static class OperationalTempoDoctrine
    {
        public static OperationalProbeOptions For(
            int allianceId,
            EraStage era,
            int policyChapter,
            int campaignMonth,
            PersonalityVector personality)
        {
            var options = new OperationalProbeOptions();
            int chapter = ClampChapter(policyChapter);

            switch (chapter)
            {
                case 0:
                case 1:
                    options.MinimumProbeDays = 5;
                    options.MaximumProbeStrengthFraction = 0.24f;
                    options.EnemyReactionMultiplier = 1.30f;
                    options.EscalateFriendlyRatio = 2.25f;
                    options.WithdrawFriendlyRatio = 0.70f;
                    break;

                case 2:
                    options.MinimumProbeDays = 3;
                    options.MaximumProbeStrengthFraction = 0.35f;
                    options.EnemyReactionMultiplier = 1.45f;
                    options.EscalateFriendlyRatio = 1.80f;
                    options.WithdrawFriendlyRatio = 0.58f;
                    break;

                default:
                    options.MinimumProbeDays = 2;
                    options.MaximumProbeStrengthFraction = 0.42f;
                    options.EnemyReactionMultiplier = 1.60f;
                    options.EscalateFriendlyRatio = 1.65f;
                    options.WithdrawFriendlyRatio = 0.50f;
                    break;
            }

            ApplyEra(options, era);
            ApplyFaction(options, allianceId, era, chapter);
            ApplyPersonality(options, personality);
            ApplySeason(options, campaignMonth);
            return options;
        }

        private static void ApplyEra(OperationalProbeOptions options, EraStage era)
        {
            switch (era)
            {
                case EraStage.Amateur1861:
                    options.MinimumProbeDays += 1;
                    options.MaximumProbeStrengthFraction -= 0.04f;
                    options.EscalateFriendlyRatio += 0.15f;
                    break;

                case EraStage.Decisive1863:
                    options.MaximumProbeStrengthFraction += 0.03f;
                    options.EscalateFriendlyRatio -= 0.05f;
                    break;

                case EraStage.TotalWar1864:
                    options.MinimumProbeDays -= 1;
                    options.MaximumProbeStrengthFraction += 0.05f;
                    options.EscalateFriendlyRatio -= 0.10f;
                    break;
            }
        }

        private static void ApplyFaction(OperationalProbeOptions options, int allianceId, EraStage era, int chapter)
        {
            if (allianceId == 0 && (era == EraStage.TotalWar1864 || chapter >= 3))
            {
                options.MinimumProbeDays -= 1;
                options.MaximumProbeStrengthFraction += 0.08f;
                options.EscalateFriendlyRatio -= 0.10f;
                options.EnemyReactionMultiplier += 0.10f;
            }

            if (allianceId == 1 && (era == EraStage.TotalWar1864 || chapter >= 3))
            {
                options.MaximumProbeStrengthFraction -= 0.08f;
                options.EscalateFriendlyRatio += 0.20f;
                options.WithdrawFriendlyRatio += 0.08f;
            }
        }

        private static void ApplyPersonality(OperationalProbeOptions options, PersonalityVector personality)
        {
            float audacity = personality.Audacity;
            float caution = personality.Caution;

            options.MaximumProbeStrengthFraction += 0.05f * audacity - 0.04f * caution;
            options.EscalateFriendlyRatio += 0.15f * caution - 0.10f * audacity;
            if (audacity > 0.35f) options.MinimumProbeDays -= 1;
            if (caution > 0.35f) options.MinimumProbeDays += 1;
        }

        private static void ApplySeason(OperationalProbeOptions options, int campaignMonth)
        {
            if (campaignMonth == 12 || campaignMonth <= 2)
            {
                options.MinimumProbeDays += 2;
                options.MaximumProbeStrengthFraction -= 0.07f;
                options.EscalateFriendlyRatio += 0.15f;
            }

            options.MinimumProbeDays = ClampInt(options.MinimumProbeDays, 1, 9);
            options.MaximumProbeStrengthFraction = Clamp(options.MaximumProbeStrengthFraction, 0.15f, 0.55f);
            options.EnemyReactionMultiplier = Clamp(options.EnemyReactionMultiplier, 1.15f, 1.85f);
            options.EscalateFriendlyRatio = Clamp(options.EscalateFriendlyRatio, 1.35f, 2.60f);
            options.WithdrawFriendlyRatio = Clamp(options.WithdrawFriendlyRatio, 0.35f, 0.85f);
        }

        private static int ClampChapter(int chapter)
        {
            if (chapter < 0) return 1;
            if (chapter > 4) return 4;
            return chapter;
        }

        private static int ClampInt(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        private static float Clamp(float value, float min, float max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }
}
