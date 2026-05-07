using System;
using System.Collections.Generic;
using UnityEngine;

namespace WhiskeyRealism.Strategic
{
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
            IReadOnlyList<BattleHistoryRecord> battleHistory)
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

            Vector3? position = ResolveObjectivePosition(objectiveId);
            context.TargetPositionResolves = position.HasValue;
            if (!position.HasValue)
                return context;

            Theater theater = TheaterClassifier.FromPosition(position.Value.x, position.Value.z);
            string sectorKey = theater.ToString();
            FrontSector sector = null;
            if (fronts == null)
            {
            }
            else
            {
                sector = fronts.GetSector(sectorKey);
            }

            theater = sector?.Theater ?? theater;
            if (sector != null)
            {
                context.TargetSectorOwnStrength = Math.Max(0f, sector.OwnStrength);
                context.TargetSectorEnemyStrength = Math.Max(0f, sector.EnemyStrength);
                context.TargetSectorRatio = context.TargetSectorOwnStrength /
                    Math.Max(1f, context.TargetSectorEnemyStrength);
            }

            FillTheaterPressure(context, pressure, fronts, theater);
            FillBattleSignals(context, allianceId, position.Value, daySerial, battleHistory);

            context.EnemyConcentratesInTheater =
                context.TheaterEnemyPressure > context.TheaterOwnPressure &&
                context.TheaterEnemyPressure >= Math.Max(1f, context.TargetSectorEnemyStrength);

            context.EnemyThreatensCapitalCorridor = false;
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
            IReadOnlyList<BattleHistoryRecord> battleHistory)
        {
            float radius = NearTargetRadius();
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

        private static float NearTargetRadius()
        {
            try
            {
                return Math.Max(1f, GamePrefs.aimaximumdistancetosearchforunitrelocations);
            }
            catch
            {
                return 500f;
            }
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
