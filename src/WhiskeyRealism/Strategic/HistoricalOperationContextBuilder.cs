using System;
using System.Collections.Generic;
using UnityEngine;

namespace WhiskeyRealism.Strategic
{
    // Live inputs the coordinator resolves once per review so PhaseTruth and the operation
    // context see the same target position, classifier and LOCAL strengths (C-HardWired).
    internal sealed class HistoricalOperationLocalSignals
    {
        public bool HasTarget;
        public float TargetX;
        public float TargetZ;
        public Theater TargetTheater = Theater.Unknown;
        public float LocalOwnStrength;
        public float LocalEnemyStrength;
        public bool EnemyThreatensCapital;
    }

    internal struct CapitalForceSample
    {
        public float Distance;
        public float Strength;
        public float Morale;
    }

    // Responsiveness signals that used to be hard-wired off (C-HardWired).
    internal static class CampaignResponsivenessSignals
    {
        // Mirrors vanilla AICampaign.AssignUnitToDefendCapital (decompile 11668-11760):
        // need = aiminimumstrengthformovement
        //      + sum over enemies within aimaximumdistancetosearchforunitrelocations of
        //        min(str, str*morale) / max(10, dist)^0.3 * 0.5
        //      - sum over own units whose last waypoint is closer than the nearest enemy of
        //        str*morale.
        // Vanilla adds capital defenders while need > 0; we call the capital threatened when an
        // enemy is inside that radius and need is still positive.
        internal static bool EnemyThreatensCapital(
            IList<CapitalForceSample> enemies,
            IList<CapitalForceSample> own,
            float relocationRadius,
            float minimumStrengthForMovement)
        {
            if (enemies == null || enemies.Count == 0) return false;
            float nearestEnemy = 9999f;
            float need = minimumStrengthForMovement;
            bool enemyInRange = false;
            for (int i = 0; i < enemies.Count; i++)
            {
                float d = Math.Max(1f, enemies[i].Distance);
                if (d < nearestEnemy) nearestEnemy = d;
                if (d < relocationRadius)
                {
                    enemyInRange = true;
                    float s = Math.Max(0f, enemies[i].Strength);
                    need += Math.Min(s, s * enemies[i].Morale) / (float)Math.Pow(Math.Max(10f, d), 0.3) * 0.5f;
                }
            }
            if (!enemyInRange) return false;
            if (own != null)
            {
                for (int i = 0; i < own.Count; i++)
                {
                    if (own[i].Distance < nearestEnemy)
                        need -= Math.Max(0f, own[i].Strength) * own[i].Morale;
                }
            }
            return need > 0f;
        }

        // Consecutive threatened days, advanced at most once per game day; days skipped by
        // fast-forward between reviews count toward the streak.
        internal static int NextCapitalStreak(int streak, bool threatened, int lastDaySerial, int todaySerial, out int newLastDaySerial)
        {
            if (lastDaySerial >= 0 && todaySerial == lastDaySerial)
            {
                newLastDaySerial = lastDaySerial;
                return streak;
            }
            newLastDaySerial = todaySerial;
            if (!threatened) return 0;
            int elapsed = lastDaySerial >= 0 ? Math.Max(1, todaySerial - lastDaySerial) : 1;
            return Math.Max(0, streak) + elapsed;
        }

        internal static int DaysSince(int stampDaySerial, int todaySerial)
        {
            if (stampDaySerial < 0) return 0;
            return Math.Max(0, todaySerial - stampDaySerial);
        }
    }

    internal static class HistoricalOperationContextBuilder
    {
        internal static HistoricalOperationContext Build(
            int allianceId,
            int daySerial,
            int objectiveId,
            OperationalPlan plan,
            FrontSectorLedger fronts,
            TheaterPressureView pressure,
            DirectorPosture posture,
            OperationDecisionMemory memory,
            IReadOnlyList<BattleHistoryRecord> battleHistory,
            HistoricalOperationLocalSignals signals)
        {
            var context = new HistoricalOperationContext
            {
                ObjectiveAvailable = objectiveId >= 0,
                ObjectiveAccomplished = false,
                TargetPositionResolves = false,
                TargetEngagedRecently = false,
                TargetLocalOwnStrength = 0f,
                TargetLocalEnemyStrength = 0f,
                TargetLocalRatio = 0f,
                Pace = posture?.Pace ?? CampaignPace.Stable,
                DirectorIntent = posture?.Intent ?? StrategicIntent.Probe,
                CollapseRisk = posture?.Risk ?? CollapseRisk.Low,
                RecentReplanCount = memory?.CountRecentReplans(daySerial, 30) ?? 0
            };

            HistoricalOperationProfile profile = null;
            if (!string.IsNullOrEmpty(plan?.OperationId))
                HistoricalOperationCatalog.TryGetById(plan.OperationId, out profile);

            // Same target position (ObjectiveAdapter), classifier (CampaignMapTheaterRuntime) and
            // LOCAL strengths PhaseTruth used this review.
            if (signals == null || !signals.HasTarget)
                return context;
            context.TargetPositionResolves = true;
            var position = new Vector3(signals.TargetX, 0f, signals.TargetZ);
            Theater theater = fronts?.GetSector(signals.TargetTheater.ToString())?.Theater ?? signals.TargetTheater;
            context.TargetLocalOwnStrength = Math.Max(0f, signals.LocalOwnStrength);
            context.TargetLocalEnemyStrength = Math.Max(0f, signals.LocalEnemyStrength);
            context.TargetLocalRatio = context.TargetLocalOwnStrength /
                Math.Max(1f, context.TargetLocalEnemyStrength);

            FillTheaterPressure(context, pressure, fronts, theater);
            FillBattleSignals(context, allianceId, position, daySerial, battleHistory, NearTargetRadius(profile));

            context.EnemyConcentratesInTheater =
                context.TheaterEnemyPressure > context.TheaterOwnPressure &&
                context.TheaterEnemyPressure >= Math.Max(1f, context.TargetLocalEnemyStrength);

            context.EnemyThreatensCapitalCorridor = signals.EnemyThreatensCapital;
            return context;
        }

        private static void FillTheaterPressure(
            HistoricalOperationContext context,
            TheaterPressureView pressure,
            FrontSectorLedger fronts,
            Theater theater)
        {
            if (pressure == null && fronts != null)
                pressure = TheaterPressureView.From(fronts);

            if (pressure == null || theater == Theater.Unknown)
                return;

            pressure.OwnStrengthByTheater.TryGetValue(theater, out context.TheaterOwnPressure);
            pressure.EnemyStrengthByTheater.TryGetValue(theater, out context.TheaterEnemyPressure);
        }

        private static void FillBattleSignals(
            HistoricalOperationContext context,
            int allianceId,
            Vector3 position,
            int daySerial,
            IReadOnlyList<BattleHistoryRecord> battleHistory,
            float radius)
        {
            foreach (var battle in BattleHistoryQuery.Near(battleHistory, position, radius, daySerial, 14))
            {
                if (battle == null || !battle.IsLandBattle)
                    continue;

                context.TargetEngagedRecently = true;
                if (!battle.IsMajorResult)
                    continue;

                if (battle.AllianceWon == allianceId)
                    context.MajorFriendlyVictoryNearTarget = true;
                if (battle.LosingAlliance == allianceId)
                    context.MajorFriendlyDefeatNearTarget = true;
            }
        }

        // Near-target radius shared by PhaseTruth (local force, engagement) and this context
        // builder. Vanilla's own "nearby" notion for campaign AI relocations is
        // GamePrefs.aimaximumdistancetosearchforunitrelocations (campaignprefs.txt, 275);
        // a profile's NearTargetRadius narrows it when set.
        internal static float NearTargetRadius(HistoricalOperationProfile profile)
        {
            float vanilla;
            try
            {
                vanilla = GamePrefs.aimaximumdistancetosearchforunitrelocations;
            }
            catch
            {
                vanilla = 275f;
            }
            return NearTargetRadius(profile, vanilla);
        }

        internal static float NearTargetRadius(HistoricalOperationProfile profile, float vanillaRadius)
        {
            float vanilla = Math.Max(1f, vanillaRadius);
            if (profile != null && profile.NearTargetRadius > 0f)
                return Math.Min(profile.NearTargetRadius, vanilla);
            return vanilla;
        }
    }
}
