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
            HistoricalOperationContext context,
            ICollection<int> availableObjectiveIds = null)
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
                if (availableObjectiveIds != null && !PhaseTargetsAvailable(profile, availableObjectiveIds))
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

        // R5(c): a profile whose phase targets are not in vanilla's available set would be
        // selected, abort on ObjectiveUnavailable the next review, and be reselected daily.
        public static bool PhaseTargetsAvailable(HistoricalOperationProfile profile, ICollection<int> availableObjectiveIds)
        {
            if (profile?.Phases == null || availableObjectiveIds == null) return false;
            for (int i = 0; i < profile.Phases.Length; i++)
            {
                var phase = profile.Phases[i];
                if (phase == null || !availableObjectiveIds.Contains(phase.TargetObjectiveId))
                    return false;
            }
            return true;
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

            if (profile.MaxEra < profile.Era)
            {
                reason = "invalid-era-span";
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

            if (era < profile.Era || era > profile.MaxEra)
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

        public static bool IsDefensivePosture(OperationPosture posture)
        {
            return posture == OperationPosture.ReinforceAndHold ||
                posture == OperationPosture.ScreenAndDelay ||
                posture == OperationPosture.Recover;
        }

        // R5: every target below is validated against Config/campaignobjectives.dat by the
        // harness (CampaignLifecycleFixTests). Rules that come from vanilla:
        //  - CampaignObjective.GetAvailableObjectives (decompile 178827) only returns
        //    objectives whose ObjectiveAlliance == the asking alliance, so a CIC can only
        //    ever pursue its own alliance's objectives (31/35/36/37/29 are Union, 30/32/33/34
        //    CSA; 29-37 exist only in scenario 002).
        //  - IsDeactivated (178909-179010) gates chapter, scenario, validfrom/validto and
        //    precampaign prerequisites (36 needs 29).
        //  - followedcampaignobjective only adds importance to towns/IIPs that are part of the
        //    objective (UpdateImportanceValues 14922/14961) — it pulls forces toward them.
        //    Objectives 0-28 have no towns (no vanilla effect).
        // The allow list equals the phase targets so a match on X always pursues X.
        //
        // Every CSA objective with ground (30 West Virginia, 32 B&O lines, 33 Maryland, 34
        // Pennsylvania) is an OFFENSIVE goal: its towns start Union-held or contested.
        // followedcampaignobjective is an offensive importance weight that pulls forces toward
        // those towns, so a phase whose live posture is defensive never writes it
        // (CampaignObjectiveSteering.PhaseSteers) and vanilla keeps choosing the objective. The former csa-capital-defense and
        // csa-protraction-defense ops were retired: no CSA objective describes ground the CSA
        // defends, and aiming them at 4 (Capture Washington) or 32 only pulled forces north.
        // csa-western-depth stays: it holds and reinforces in western Virginia (objective 30's
        // Charleston/Grafton/Lewisburg/Romney, the ground the CSA actually contested in 1862)
        // through the mod's own ledgers (front/formation/probe posture flags); its defensive
        // phases do not steer vanilla's objective.
        private static HistoricalOperationProfile[] BuildProfiles()
        {
            return new[]
            {
                Profile("union-east-pressure", "Union Eastern Pressure", 0, EraStage.Amateur1861, Theater.East, 3, new[] { 3 },
                    OperationChapterPolicy.AllowDateDrift, 10, new[] { StrategyTag.CapitalThreat, StrategyTag.Logistics },
                    OperationTempoPreset.Standard, OperationPosture.ProbeAndDevelop, 1, 1, 4, 1861, 12, 1861,
                    new[]
                    {
                        Phase("develop-contact", "Develop Contact", 3, PhaseTransition.TargetEngaged, 0.35f, OperationPosture.ProbeAndDevelop, true, false, true, 30),
                        Phase("concentrate-for-attack", "Concentrate For Attack", 3, PhaseTransition.TargetEngaged, 0.55f, OperationPosture.ConcentratedAttack, true, true, false, 60),
                        Phase("attack-objective", "Attack Objective", 3, PhaseTransition.TargetTaken, 0.65f, OperationPosture.ConcentratedAttack, true, true, false, 90)
                    }),


                Profile("csa-valley-disruption", "CSA Valley Disruption", 1, EraStage.Amateur1861, Theater.East, 32, new[] { 32 },
                    OperationChapterPolicy.AllowDateDrift, 15, new[] { StrategyTag.RailHub, StrategyTag.ForeignRecognition },
                    OperationTempoPreset.Press, OperationPosture.Counterstroke, 1, 1, 4, 1861, 12, 1861,
                    new[]
                    {
                        Phase("develop-contact", "Develop Contact", 32, PhaseTransition.TargetEngaged, 0.35f, OperationPosture.ProbeAndDevelop, true, false, true, 30),
                        Phase("attack-objective", "Attack Objective", 32, PhaseTransition.TargetTaken, 0.55f, OperationPosture.Counterstroke, true, false, false, 75),
                        Phase("screen-and-delay", "Screen And Delay", 32, PhaseTransition.DeadlineExpired, 0.40f, OperationPosture.ScreenAndDelay, false, true, true, 90)
                    }),

                // Objectives 35/37 are valid from 01.01.1862 (IsDeactivated), so this is an 1862 op.
                Profile("union-coastal-pressure", "Union Coastal Pressure", 0, EraStage.Operational1862, Theater.Coast, 37, new[] { 37 },
                    OperationChapterPolicy.AllowDateDrift, 20, new[] { StrategyTag.Blockade, StrategyTag.PortAccess, StrategyTag.Logistics },
                    OperationTempoPreset.Standard, OperationPosture.ProbeAndDevelop, 1, 2, 1, 1862, 12, 1862,
                    new[]
                    {
                        Phase("develop-contact", "Develop Contact", 37, PhaseTransition.TargetEngaged, 0.30f, OperationPosture.ProbeAndDevelop, true, false, true, 30),
                        Phase("attack-objective", "Attack Objective", 37, PhaseTransition.TargetTaken, 0.55f, OperationPosture.ConcentratedAttack, true, true, false, 90)
                    }),

                // 36 (Saltville) is deactivated until 29 is accomplished (precampaign 029), so the
                // operation pursues 29 (West Virginia) itself.
                Profile("union-western-pressure", "Union Western Pressure", 0, EraStage.Operational1862, Theater.East, 29, new[] { 29 },
                    OperationChapterPolicy.AllowDateDrift, 10, new[] { StrategyTag.RiverControl, StrategyTag.RailHub, StrategyTag.Logistics },
                    OperationTempoPreset.Press, OperationPosture.ConcentratedAttack, 2, 2, 1, 1862, 12, 1862,
                    new[]
                    {
                        Phase("develop-contact", "Develop Contact", 29, PhaseTransition.TargetEngaged, 0.40f, OperationPosture.ProbeAndDevelop, true, false, true, 30),
                        Phase("concentrate-for-attack", "Concentrate For Attack", 29, PhaseTransition.TargetEngaged, 0.60f, OperationPosture.ConcentratedAttack, true, true, false, 60),
                        Phase("attack-objective", "Attack Objective", 29, PhaseTransition.TargetTaken, 0.70f, OperationPosture.ConcentratedAttack, true, true, false, 105)
                    }),

                Profile("csa-western-depth", "CSA Western Depth", 1, EraStage.Operational1862, Theater.East, 30, new[] { 30 },
                    OperationChapterPolicy.AllowDateDrift, 10, new[] { StrategyTag.DefensiveDepth, StrategyTag.Logistics },
                    OperationTempoPreset.Recover, OperationPosture.ReinforceAndHold, 2, 2, 1, 1862, 12, 1862,
                    new[]
                    {
                        Phase("screen-and-delay", "Screen And Delay", 30, PhaseTransition.TargetEngaged, 0.35f, OperationPosture.ScreenAndDelay, false, true, true, 30),
                        Phase("reinforce-and-hold", "Reinforce And Hold", 30, PhaseTransition.ForceBelowThreshold, 0.60f, OperationPosture.ReinforceAndHold, false, true, false, 75),
                        Phase("recover-combat-power", "Recover Combat Power", 30, PhaseTransition.DeadlineExpired, 0.45f, OperationPosture.Recover, false, true, true, 105)
                    }),

                // A calendar prior: Grant and Sherman opened their 1864 campaigns in May, six months
                // before the election that makes vanilla's chapter 3. The era stage follows the chapter
                // (EraStageManager), so this profile matches Decisive1863 through TotalWar1864 and is
                // bounded by its date window; a lost election leaves the chapter at 2 and the era at
                // Decisive1863 but the operation still runs to the end of the campaign.
                WithMaxEra(Profile("union-late-pressure", "Union Late Pressure", 0, EraStage.Decisive1863, Theater.East, 3, new[] { 3 },
                    OperationChapterPolicy.AllowDateDrift, 10, new[] { StrategyTag.ArmyDestruction, StrategyTag.RailHub, StrategyTag.Logistics },
                    OperationTempoPreset.Exploit, OperationPosture.ExploitBreakthrough, 3, 4, 5, 1864, 12, 1865,
                    new[]
                    {
                        Phase("develop-contact", "Develop Contact", 3, PhaseTransition.TargetEngaged, 0.45f, OperationPosture.ProbeAndDevelop, true, false, true, 21),
                        Phase("concentrate-for-attack", "Concentrate For Attack", 3, PhaseTransition.TargetEngaged, 0.70f, OperationPosture.ConcentratedAttack, true, true, false, 45),
                        Phase("attack-objective", "Attack Objective", 3, PhaseTransition.TargetTaken, 0.80f, OperationPosture.ExploitBreakthrough, true, true, false, 75)
                    }), EraStage.TotalWar1864)
            };
        }

        // A profile that matches a span of eras instead of one (an unset MaxEra is Era).
        private static HistoricalOperationProfile WithMaxEra(HistoricalOperationProfile profile, EraStage maxEra)
        {
            profile.MaxEra = maxEra;
            return profile;
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
                // Vanilla's "nearby" for campaign AI relocations: campaignprefs.txt
                // aimaximumdistancetosearchforunitrelocations = 275 (GamePrefs, loader 56220).
                NearTargetRadius = 275f
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
