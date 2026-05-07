using System;
using System.Collections.Generic;

namespace WhiskeyRealism.Strategic
{
    public static class HistoricalOperationCatalog
    {
        private static readonly HistoricalOperationProfile[] Profiles = BuildProfiles();

        public static HistoricalOperationMatch Resolve(
            int allianceId,
            EraStage era,
            int vanillaChapter,
            int month,
            int year,
            HistoricalOperationCandidate candidate,
            GrandStrategyProfile strategy,
            PersonalityVector cicPersonality,
            DirectorPosture posture,
            HistoricalOperationContext context)
        {
            if (candidate == null)
                return NoProfile("missing-candidate");

            if (candidate.ObjectiveId < 0)
                return NoProfile("invalid-objective");

            var matches = new List<HistoricalOperationMatch>();
            for (int i = 0; i < Profiles.Length; i++)
            {
                var profile = Profiles[i];
                string reason;
                if (!Matches(profile, allianceId, era, vanillaChapter, month, year, candidate, out reason))
                    continue;

                matches.Add(Matched(
                    profile,
                    Score(profile, candidate, strategy, cicPersonality, posture, context),
                    reason));
            }

            if (matches.Count == 0)
                return NoProfile("no-explicit-profile");

            matches.Sort(CompareMatches);
            return matches[0];
        }

        public static bool TryGetById(string operationId, out HistoricalOperationProfile profile)
        {
            profile = null;
            if (string.IsNullOrEmpty(operationId))
                return false;

            for (int i = 0; i < Profiles.Length; i++)
            {
                if (string.Equals(Profiles[i].OperationId, operationId, StringComparison.Ordinal))
                {
                    profile = Profiles[i];
                    return true;
                }
            }

            return false;
        }

        public static bool ValidateProfile(HistoricalOperationProfile profile, out string reason)
        {
            if (profile == null)
            {
                reason = "missing-profile";
                return false;
            }

            if (string.IsNullOrEmpty(profile.OperationId))
            {
                reason = "missing-operation-id";
                return false;
            }

            if (profile.PrimaryObjectiveId < 0)
            {
                reason = "invalid-primary-objective";
                return false;
            }

            if (profile.ObjectiveAllowList == null || profile.ObjectiveAllowList.Length == 0)
            {
                reason = "missing-objective-allow-list";
                return false;
            }

            if (!Contains(profile.ObjectiveAllowList, profile.PrimaryObjectiveId))
            {
                reason = "primary-objective-not-allowed";
                return false;
            }

            if (profile.Phases == null || profile.Phases.Length == 0)
            {
                reason = "missing-phases";
                return false;
            }

            for (int i = 0; i < profile.Phases.Length; i++)
            {
                if (!ValidatePhaseTemplate(profile.Phases[i], out reason))
                {
                    reason = "phase-" + i + ":" + reason;
                    return false;
                }
            }

            reason = "valid";
            return true;
        }

        public static bool ValidatePhaseTemplate(OperationPhaseTemplate phase, out string reason)
        {
            if (phase == null)
            {
                reason = "missing-phase";
                return false;
            }

            if (string.IsNullOrEmpty(phase.PhaseId))
            {
                reason = "missing-phase-id";
                return false;
            }

            if (phase.TargetObjectiveId < 0)
            {
                reason = "objective-less-phase";
                return false;
            }

            if (phase.DeadlineDays <= 0)
            {
                reason = "invalid-deadline";
                return false;
            }

            reason = "valid";
            return true;
        }

        public static HistoricalOperationMatch NoProfile(string reason)
        {
            return new HistoricalOperationMatch
            {
                Kind = HistoricalOperationMatchKind.NoProfile,
                Profile = null,
                Score = 0f,
                Reason = reason
            };
        }

        private static HistoricalOperationMatch Matched(
            HistoricalOperationProfile profile,
            float score,
            string reason)
        {
            return new HistoricalOperationMatch
            {
                Kind = HistoricalOperationMatchKind.Matched,
                Profile = profile,
                Score = score,
                Reason = reason
            };
        }

        private static bool Matches(
            HistoricalOperationProfile profile,
            int allianceId,
            EraStage era,
            int vanillaChapter,
            int month,
            int year,
            HistoricalOperationCandidate candidate,
            out string reason)
        {
            if (!ValidateProfile(profile, out reason))
                return false;

            if (profile.AllianceId != allianceId)
            {
                reason = "alliance";
                return false;
            }

            if (profile.Era != era)
            {
                reason = "era";
                return false;
            }

            if (!InDateWindow(profile, month, year))
            {
                reason = "date-window";
                return false;
            }

            if (!Contains(profile.ObjectiveAllowList, candidate.ObjectiveId))
            {
                reason = "objective-not-allowed";
                return false;
            }

            if (!HasAllTags(candidate.Objective, profile.RequiredTags))
            {
                reason = "required-tags";
                return false;
            }

            bool chapterDrift;
            if (!ChapterMatches(profile, vanillaChapter, out chapterDrift))
            {
                reason = "chapter";
                return false;
            }

            reason = chapterDrift ? "matched:chapter-drift" : "matched";
            return true;
        }

        private static int CompareMatches(HistoricalOperationMatch a, HistoricalOperationMatch b)
        {
            int priority = a.Profile.Priority.CompareTo(b.Profile.Priority);
            if (priority != 0) return priority;

            int score = RoundedScore(b.Score).CompareTo(RoundedScore(a.Score));
            if (score != 0) return score;

            return string.CompareOrdinal(a.Profile.OperationId, b.Profile.OperationId);
        }

        private static float Score(
            HistoricalOperationProfile profile,
            HistoricalOperationCandidate candidate,
            GrandStrategyProfile strategy,
            PersonalityVector cicPersonality,
            DirectorPosture posture,
            HistoricalOperationContext context)
        {
            float score = candidate.ObjectiveScore;

            if (candidate.ObjectiveId == profile.PrimaryObjectiveId)
                score += 1.0f;

            if (candidate.Objective.Theater == profile.Theater)
                score += 0.50f;

            score += PreferredTagScore(candidate.Objective, profile.PreferredTags, strategy);

            if (posture != null)
            {
                if (posture.TheaterPriority == profile.Theater)
                    score += 0.35f;
                if (posture.Intent == StrategicIntent.Exploit && profile.Posture == OperationPosture.ExploitBreakthrough)
                    score += 0.25f;
                if (posture.Intent == StrategicIntent.Delay && profile.Posture == OperationPosture.ScreenAndDelay)
                    score += 0.25f;
                if (posture.Intent == StrategicIntent.Recover && profile.Posture == OperationPosture.Recover)
                    score += 0.25f;
                if (posture.Risk == CollapseRisk.Critical && profile.Posture == OperationPosture.ReinforceAndHold)
                    score += 0.30f;
            }

            if (context != null)
            {
                if (context.MajorFriendlyVictoryNearTarget && profile.Posture == OperationPosture.ExploitBreakthrough)
                    score += 0.25f;
                if (context.MajorFriendlyDefeatNearTarget && profile.Posture == OperationPosture.Recover)
                    score += 0.25f;
                if (context.EnemyThreatensCapitalCorridor && profile.Posture == OperationPosture.ReinforceAndHold)
                    score += 0.25f;
                if (context.RecentReplanCount > 1)
                    score -= Math.Min(0.50f, context.RecentReplanCount * 0.10f);
            }

            score += cicPersonality.Audacity * 0.20f;
            score -= cicPersonality.Caution * 0.10f;
            return score;
        }

        private static float PreferredTagScore(
            ObjectiveMetadata objective,
            StrategyTag[] preferredTags,
            GrandStrategyProfile strategy)
        {
            if (preferredTags == null || preferredTags.Length == 0)
                return 0f;

            float score = 0f;
            for (int i = 0; i < preferredTags.Length; i++)
            {
                if (!objective.HasTag(preferredTags[i]))
                    continue;

                score += 0.20f;
                if (strategy != null)
                    score += Math.Max(0f, strategy.WeightFor(preferredTags[i])) * 0.10f;
            }

            return score;
        }

        private static bool HasAllTags(ObjectiveMetadata objective, StrategyTag[] requiredTags)
        {
            if (requiredTags == null || requiredTags.Length == 0)
                return true;

            for (int i = 0; i < requiredTags.Length; i++)
            {
                if (!objective.HasTag(requiredTags[i]))
                    return false;
            }

            return true;
        }

        private static bool ChapterMatches(
            HistoricalOperationProfile profile,
            int vanillaChapter,
            out bool chapterDrift)
        {
            chapterDrift = false;

            if (profile.ChapterPolicy == OperationChapterPolicy.Exact)
            {
                if (vanillaChapter == -1)
                    return false;

                return vanillaChapter >= profile.MinChapter && vanillaChapter <= profile.MaxChapter;
            }

            if (vanillaChapter == -1)
            {
                chapterDrift = true;
                return true;
            }

            bool inWindow = vanillaChapter >= profile.MinChapter && vanillaChapter <= profile.MaxChapter;
            chapterDrift = !inWindow;
            return true;
        }

        private static bool InDateWindow(HistoricalOperationProfile profile, int month, int year)
        {
            if (month < 1 || month > 12 || year <= 0)
                return false;

            int current = (year * 12) + month;
            int start = (profile.StartYear * 12) + profile.StartMonth;
            int end = (profile.EndYear * 12) + profile.EndMonth;
            return current >= start && current <= end;
        }

        private static bool Contains(int[] values, int value)
        {
            if (values == null)
                return false;

            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] == value)
                    return true;
            }

            return false;
        }

        private static float RoundedScore(float score)
        {
            return (float)Math.Round(score, 3, MidpointRounding.AwayFromZero);
        }

        private static HistoricalOperationProfile[] BuildProfiles()
        {
            return new[]
            {
                Profile("union-east-pressure", "Union Eastern Pressure", 0, EraStage.Amateur1861, Theater.East, 3, new[] { 3, 37 },
                    OperationChapterPolicy.AllowDateDrift, 10, new[] { StrategyTag.CapitalThreat, StrategyTag.Logistics },
                    OperationTempoPreset.Standard, OperationPosture.ProbeAndDevelop, 1, 1, 4, 1861, 12, 1861,
                    new[]
                    {
                        Phase("develop-contact", "Develop Contact", 3, PhaseTransition.TargetEngaged, 0.35f, OperationPosture.ProbeAndDevelop, true, false, true, 30),
                        Phase("concentrate-for-attack", "Concentrate For Attack", 3, PhaseTransition.TargetEngaged, 0.55f, OperationPosture.ConcentratedAttack, true, true, false, 60),
                        Phase("attack-objective", "Attack Objective", 3, PhaseTransition.TargetTaken, 0.65f, OperationPosture.ConcentratedAttack, true, true, false, 90)
                    }),

                Profile("csa-capital-defense", "CSA Capital Defense", 1, EraStage.Amateur1861, Theater.East, 4, new[] { 4, 31, 32 },
                    OperationChapterPolicy.AllowDateDrift, 10, new[] { StrategyTag.CapitalDefense, StrategyTag.DefensiveDepth },
                    OperationTempoPreset.Deliberate, OperationPosture.ReinforceAndHold, 1, 1, 4, 1861, 12, 1861,
                    new[]
                    {
                        Phase("screen-and-delay", "Screen And Delay", 4, PhaseTransition.TargetEngaged, 0.35f, OperationPosture.ScreenAndDelay, false, true, true, 30),
                        Phase("reinforce-and-hold", "Reinforce And Hold", 4, PhaseTransition.ForceBelowThreshold, 0.65f, OperationPosture.ReinforceAndHold, false, true, false, 75),
                        Phase("counterstroke", "Counterstroke", 4, PhaseTransition.TargetTaken, 0.60f, OperationPosture.Counterstroke, true, true, false, 105)
                    }),

                Profile("csa-valley-disruption", "CSA Valley Disruption", 1, EraStage.Amateur1861, Theater.East, 31, new[] { 31, 32, 33 },
                    OperationChapterPolicy.AllowDateDrift, 15, new[] { StrategyTag.RailHub, StrategyTag.ForeignRecognition },
                    OperationTempoPreset.Press, OperationPosture.Counterstroke, 1, 1, 4, 1861, 12, 1861,
                    new[]
                    {
                        Phase("develop-contact", "Develop Contact", 31, PhaseTransition.TargetEngaged, 0.35f, OperationPosture.ProbeAndDevelop, true, false, true, 30),
                        Phase("attack-objective", "Attack Objective", 31, PhaseTransition.TargetTaken, 0.55f, OperationPosture.Counterstroke, true, false, false, 75),
                        Phase("screen-and-delay", "Screen And Delay", 31, PhaseTransition.DeadlineExpired, 0.40f, OperationPosture.ScreenAndDelay, false, true, true, 90)
                    }),

                Profile("union-coastal-pressure", "Union Coastal Pressure", 0, EraStage.Amateur1861, Theater.Coast, 37, new[] { 35, 37 },
                    OperationChapterPolicy.AllowDateDrift, 20, new[] { StrategyTag.Blockade, StrategyTag.PortAccess, StrategyTag.Logistics },
                    OperationTempoPreset.Standard, OperationPosture.ProbeAndDevelop, 1, 1, 4, 1861, 12, 1861,
                    new[]
                    {
                        Phase("develop-contact", "Develop Contact", 37, PhaseTransition.TargetEngaged, 0.30f, OperationPosture.ProbeAndDevelop, true, false, true, 30),
                        Phase("attack-objective", "Attack Objective", 37, PhaseTransition.TargetTaken, 0.55f, OperationPosture.ConcentratedAttack, true, true, false, 90)
                    }),

                Profile("union-western-pressure", "Union Western Pressure", 0, EraStage.Operational1862, Theater.West, 36, new[] { 29, 36 },
                    OperationChapterPolicy.AllowDateDrift, 10, new[] { StrategyTag.RiverControl, StrategyTag.RailHub, StrategyTag.Logistics },
                    OperationTempoPreset.Press, OperationPosture.ConcentratedAttack, 2, 2, 1, 1862, 12, 1862,
                    new[]
                    {
                        Phase("develop-contact", "Develop Contact", 36, PhaseTransition.TargetEngaged, 0.40f, OperationPosture.ProbeAndDevelop, true, false, true, 30),
                        Phase("concentrate-for-attack", "Concentrate For Attack", 36, PhaseTransition.TargetEngaged, 0.60f, OperationPosture.ConcentratedAttack, true, true, false, 60),
                        Phase("attack-objective", "Attack Objective", 36, PhaseTransition.TargetTaken, 0.70f, OperationPosture.ConcentratedAttack, true, true, false, 105)
                    }),

                Profile("csa-western-depth", "CSA Western Depth", 1, EraStage.Operational1862, Theater.West, 36, new[] { 30, 36 },
                    OperationChapterPolicy.AllowDateDrift, 10, new[] { StrategyTag.DefensiveDepth, StrategyTag.Logistics },
                    OperationTempoPreset.Recover, OperationPosture.ReinforceAndHold, 2, 2, 1, 1862, 12, 1862,
                    new[]
                    {
                        Phase("screen-and-delay", "Screen And Delay", 36, PhaseTransition.TargetEngaged, 0.35f, OperationPosture.ScreenAndDelay, false, true, true, 30),
                        Phase("reinforce-and-hold", "Reinforce And Hold", 36, PhaseTransition.ForceBelowThreshold, 0.60f, OperationPosture.ReinforceAndHold, false, true, false, 75),
                        Phase("recover-combat-power", "Recover Combat Power", 36, PhaseTransition.DeadlineExpired, 0.45f, OperationPosture.Recover, false, true, true, 105)
                    }),

                Profile("union-late-pressure", "Union Late Pressure", 0, EraStage.TotalWar1864, Theater.East, 3, new[] { 3, 31, 32, 37 },
                    OperationChapterPolicy.AllowDateDrift, 10, new[] { StrategyTag.ArmyDestruction, StrategyTag.RailHub, StrategyTag.Logistics },
                    OperationTempoPreset.Exploit, OperationPosture.ExploitBreakthrough, 3, 4, 1, 1864, 12, 1865,
                    new[]
                    {
                        Phase("develop-contact", "Develop Contact", 3, PhaseTransition.TargetEngaged, 0.45f, OperationPosture.ProbeAndDevelop, true, false, true, 21),
                        Phase("concentrate-for-attack", "Concentrate For Attack", 3, PhaseTransition.TargetEngaged, 0.70f, OperationPosture.ConcentratedAttack, true, true, false, 45),
                        Phase("attack-objective", "Attack Objective", 3, PhaseTransition.TargetTaken, 0.80f, OperationPosture.ExploitBreakthrough, true, true, false, 75)
                    }),

                Profile("csa-protraction-defense", "CSA Protraction Defense", 1, EraStage.TotalWar1864, Theater.East, 4, new[] { 4, 30, 31, 32, 36 },
                    OperationChapterPolicy.AllowDateDrift, 10, new[] { StrategyTag.CapitalDefense, StrategyTag.DefensiveDepth, StrategyTag.Manpower },
                    OperationTempoPreset.Recover, OperationPosture.ReinforceAndHold, 3, 4, 1, 1864, 12, 1865,
                    new[]
                    {
                        Phase("screen-and-delay", "Screen And Delay", 4, PhaseTransition.TargetEngaged, 0.35f, OperationPosture.ScreenAndDelay, false, true, true, 21),
                        Phase("reinforce-and-hold", "Reinforce And Hold", 4, PhaseTransition.ForceBelowThreshold, 0.70f, OperationPosture.ReinforceAndHold, false, true, false, 60),
                        Phase("counterstroke", "Counterstroke", 4, PhaseTransition.TargetTaken, 0.65f, OperationPosture.Counterstroke, true, true, false, 90),
                        Phase("recover-combat-power", "Recover Combat Power", 4, PhaseTransition.DeadlineExpired, 0.45f, OperationPosture.Recover, false, true, true, 120)
                    })
            };
        }

        private static HistoricalOperationProfile Profile(
            string id,
            string name,
            int allianceId,
            EraStage era,
            Theater theater,
            int primaryObjectiveId,
            int[] objectiveAllowList,
            OperationChapterPolicy chapterPolicy,
            int priority,
            StrategyTag[] preferredTags,
            OperationTempoPreset tempo,
            OperationPosture posture,
            int minChapter,
            int maxChapter,
            int startMonth,
            int startYear,
            int endMonth,
            int endYear,
            OperationPhaseTemplate[] phases)
        {
            return new HistoricalOperationProfile
            {
                OperationId = id,
                OperationName = name,
                AllianceId = allianceId,
                Theater = theater,
                Era = era,
                MinChapter = minChapter,
                MaxChapter = maxChapter,
                StartMonth = startMonth,
                StartYear = startYear,
                EndMonth = endMonth,
                EndYear = endYear,
                PrimaryObjectiveId = primaryObjectiveId,
                ObjectiveAllowList = objectiveAllowList,
                ChapterPolicy = chapterPolicy,
                Priority = priority,
                RequiredTags = Array.Empty<StrategyTag>(),
                PreferredTags = preferredTags ?? Array.Empty<StrategyTag>(),
                Tempo = tempo,
                Posture = posture,
                Phases = phases,
                DynamicRules = StandardDynamicRules(),
                AlternateOperationIds = Array.Empty<string>(),
                NearTargetRadius = 50000f
            };
        }

        private static OperationPhaseTemplate Phase(
            string id,
            string name,
            int targetObjectiveId,
            PhaseTransition transition,
            float forceFractionRequired,
            OperationPosture posture,
            bool allowCoordinatedAttack,
            bool allowReinforcementPackage,
            bool allowProbeOnly,
            int deadlineDays)
        {
            return new OperationPhaseTemplate
            {
                PhaseId = id,
                PhaseName = name,
                TargetObjectiveId = targetObjectiveId,
                TargetAreaId = -1,
                TargetAreaKey = null,
                TargetSectorKey = null,
                Transition = transition,
                ForceFractionRequired = forceFractionRequired,
                Posture = posture,
                AllowCoordinatedAttack = allowCoordinatedAttack,
                AllowReinforcementPackage = allowReinforcementPackage,
                AllowProbeOnly = allowProbeOnly,
                DeadlineDays = deadlineDays
            };
        }

        private static OperationDynamicRule[] StandardDynamicRules()
        {
            return new[]
            {
                Rule("objective-unavailable-abort", OperationDynamicTrigger.ObjectiveUnavailable, OperationDynamicAction.AbortOperation, 0, 0f, float.PositiveInfinity, "objective-unavailable"),
                Rule("objective-accomplished-advance", OperationDynamicTrigger.ObjectiveAccomplished, OperationDynamicAction.AdvancePhase, 10, 0f, float.PositiveInfinity, "objective-accomplished"),
                Rule("friendly-victory-exploit", OperationDynamicTrigger.MajorFriendlyVictoryNearTarget, OperationDynamicAction.Exploit, 20, 1.15f, float.PositiveInfinity, "victory-near-target"),
                Rule("friendly-defeat-recover", OperationDynamicTrigger.MajorFriendlyDefeatNearTarget, OperationDynamicAction.Recover, 30, 0f, float.PositiveInfinity, "defeat-near-target"),
                Rule("empty-target-screen", OperationDynamicTrigger.EmptyTarget, OperationDynamicAction.ScreenAndDelay, 40, 0f, float.PositiveInfinity, "empty-target-screen-probe")
            };
        }

        private static OperationDynamicRule Rule(
            string id,
            OperationDynamicTrigger trigger,
            OperationDynamicAction action,
            int priority,
            float minRatio,
            float maxRatio,
            string reason)
        {
            return new OperationDynamicRule
            {
                RuleId = id,
                Trigger = trigger,
                Action = action,
                Priority = priority,
                MinOwnEnemyRatio = minRatio,
                MaxOwnEnemyRatio = maxRatio,
                MinReadiness = 0f,
                WindowDays = 14,
                AlternateOperationId = null,
                Reason = reason
            };
        }
    }
}
