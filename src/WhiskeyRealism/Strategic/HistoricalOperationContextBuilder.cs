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
            PhaseTruthOutput baseTruth,
            FrontSectorLedger fronts,
            DefenseIntentLedgerOutput defense,
            FormationDirectiveLedger formation,
            CampaignMapLedger map,
            TheaterPressureView pressure,
            DirectorPosture posture,
            OperationDecisionMemory memory,
            IReadOnlyList<BattleHistoryRecord> battleHistory,
            HistoricalOperationLocalSignals signals = null)
        {
            var context = new HistoricalOperationContext
            {
                ObjectiveAvailable = objectiveId >= 0,
                ObjectiveAccomplished = false,
                TargetPositionResolves = false,
                TargetEngagedRecently = false,
                TargetSectorOwnStrength = 0f,
                TargetSectorEnemyStrength = 0f,
                TargetSectorRatio = 0f,
                Pace = posture?.Pace ?? CampaignPace.Stable,
                DirectorIntent = posture?.Intent ?? StrategicIntent.Probe,
                CollapseRisk = posture?.Risk ?? CollapseRisk.Low,
                RecentReplanCount = memory?.CountRecentReplans(daySerial, 30) ?? 0
            };

            if (baseTruth != null)
            {
                if (baseTruth.Verdict == PhaseTruthVerdict.ObjectiveUnavailable)
                    context.ObjectiveAvailable = false;
                if (baseTruth.Verdict == PhaseTruthVerdict.TargetAccomplished)
                    context.ObjectiveAccomplished = true;
                if (baseTruth.Verdict == PhaseTruthVerdict.TargetEngaged)
                    context.TargetEngagedRecently = true;
                if (baseTruth.Verdict == PhaseTruthVerdict.MissingTargetPosition)
                    context.TargetPositionResolves = false;
            }

            HistoricalOperationProfile profile = null;
            if (!string.IsNullOrEmpty(plan?.OperationId))
                HistoricalOperationCatalog.TryGetById(plan.OperationId, out profile);

            Vector3? position;
            Theater theater;
            if (signals != null)
            {
                // Same target position (ObjectiveAdapter), classifier (CampaignMapTheaterRuntime)
                // and local strengths PhaseTruth used this review.
                position = signals.HasTarget ? new Vector3(signals.TargetX, 0f, signals.TargetZ) : (Vector3?)null;
                theater = signals.TargetTheater;
            }
            else
            {
                position = ResolveObjectivePosition(objectiveId);
                theater = position.HasValue ? TheaterClassifier.FromPosition(position.Value.x, position.Value.z) : Theater.Unknown;
            }
            context.TargetPositionResolves = position.HasValue;
            if (!position.HasValue)
                return context;

            FrontSector sector = fronts?.GetSector(theater.ToString());
            theater = sector?.Theater ?? theater;
            if (signals != null)
            {
                context.TargetSectorOwnStrength = Math.Max(0f, signals.LocalOwnStrength);
                context.TargetSectorEnemyStrength = Math.Max(0f, signals.LocalEnemyStrength);
            }
            else if (sector != null)
            {
                context.TargetSectorOwnStrength = Math.Max(0f, sector.OwnStrength);
                context.TargetSectorEnemyStrength = Math.Max(0f, sector.EnemyStrength);
            }
            context.TargetSectorRatio = context.TargetSectorOwnStrength /
                Math.Max(1f, context.TargetSectorEnemyStrength);

            FillTheaterPressure(context, pressure, fronts, theater);
            FillBattleSignals(context, allianceId, position.Value, daySerial, battleHistory, NearTargetRadius(profile));

            context.EnemyConcentratesInTheater =
                context.TheaterEnemyPressure > context.TheaterOwnPressure &&
                context.TheaterEnemyPressure >= Math.Max(1f, context.TargetSectorEnemyStrength);

            context.EnemyThreatensCapitalCorridor = signals != null && signals.EnemyThreatensCapital;
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
                vanilla = Math.Max(1f, GamePrefs.aimaximumdistancetosearchforunitrelocations);
            }
            catch
            {
                vanilla = 275f;
            }
            if (profile != null && profile.NearTargetRadius > 0f)
                return Math.Min(profile.NearTargetRadius, vanilla);
            return vanilla;
        }

        private static Vector3? ResolveObjectivePosition(int objectiveId)
        {
            if (objectiveId < 0) return null;
            if (!ObjectiveCatalog.TryResolvePosition(objectiveId, out float x, out float z))
                return null;
            return new Vector3(x, 0f, z);
        }
    }
}
