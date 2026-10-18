using System;
using System.Collections.Generic;

namespace WhiskeyRealism.Strategic.Planner
{
    // Active campaign reconnaissance, planning half (plan § G1b; spec component 4b). Vanilla's
    // campaign AI never scouts: no AICampaign job sets cavalryorders 1 or 3 (only raid forces set 2,
    // 9177), and the scouting order does not widen sight (UpdateViewRange 101296-101307 uses only
    // maximumlineofsight, weather, lie-down and incident 63). A side sees what its viewers cover
    // (AssignUnitToFOW 116520-116534), so reconnaissance is viewer placement. Fog-honest: enemies
    // enter only as G1 beliefs (last-seen position, never a destination; vanilla's own AI reads
    // destinations at 13121, 13600, 13673, 9099, 94018) and enemy-held objective towns (public
    // ownership).
    //
    // Tasks (PlanTasks), highest priority first, capped at MaxScreensPerTheater screens per theater
    // and MaxMissionsPerAlliance tasks in all:
    //   screen-posture   every sector the plan marks TheaterPosture.Screen: toward the nearest
    //                    believed enemy (else enemy-held objective) of the sector's own formations.
    //   main-axis        the main effort's front -> its objective, when unwatched.
    //   probe            the main objective's surroundings hold no belief of confidence >= 0.5, it
    //                    is not in view and the main effort is not in contact there.
    //   capital-approach own capital -> each sector centroid holding a belief or an enemy-held
    //                    objective, when unwatched.
    //   advance-guard    the main body exceeds RequiredRatio x the believed enemy at its objective
    //                    and no belief lies within one sight radius of the front: one sight radius
    //                    ahead on the axis.
    //   front-approach   the main effort's front -> each such sector centroid, when unwatched.
    //   belief           toward each believed army no viewer covers, scored by age, uncertainty
    //                    radius and strength (AgeWeight/RadiusWeight/StrengthWeight).
    //   picket           each own major town or built own depot in a Hold or Screen sector that no
    //                    viewer covers and no belief threatens within the standoff.
    // An approach is unwatched when one of its samples (every 0.5 x sight radius) lies outside every
    // own viewer's radius. Formations already on a recon mission are not viewers for that test, so a
    // screen's own post never makes its approach "watched" and the task does not flap away the day
    // it arrives. Hold points keep StandoffFraction x sight radius from every live belief and
    // enemy-held objective (pulled back toward the axis start), then LogisticsGate.ClampToReach; no
    // task when no such point remains. Sight and command range are the minimum over the formations
    // that could take a task, so the standoff point is in view and in reach whichever is picked.
    //
    // Formation choice (PickFormation): cavalry-majority groups first (nearest the axis start), then
    // the smallest fighting group (nearest); never a busy group (G4's busy rule plus vanilla's own
    // operation, capital-defense and depot lists), never below vanilla's movement minima
    // (IsUnitAvailableForOffensiveOperations 14091-14106: readiness step >= 2, strength and morale
    // strictly above aiminimumstrength/moraleformovement; Strength here is groupstrengthdirect where
    // vanilla tests groupstrengthactive), never a formation in the W&L player's chain that fails
    // WlChainRules.MayMove (his own command, a CIC side, the map-group player: a picket is a pre-pledge,
    // and his own vision already reaches the side through vanilla), a carrying group freely (the
    // player it carries is sent no order), never the main body's last cavalry-majority group
    // unless the task is the main effort's own screen or the main effort already has one, a picket
    // only from its own sector and never its final reserve, an advance guard or probe at most
    // AdvanceGuardMaxStrengthShare of the main body. The group already holding a matching mission
    // keeps it (no churn); a group being recalled is not re-tasked.
    public static class ReconPlanner
    {
        public const int MaxScreensPerTheater = 2;
        public const int MaxMissionsPerAlliance = 6;
        public const float StandoffFraction = 0.8f;   // hold point >= 0.8 x own sight radius from the nearest belief
        public const float MinCavalryShare = 0.5f;     // a screen is a cavalry-majority group when one exists
        public const float AgeWeight = 0.5f, RadiusWeight = 0.3f, StrengthWeight = 0.2f;
        public const float AdvanceGuardMaxStrengthShare = 0.25f; // of the main-effort strength

        // A belief below this is stale: it sends a probe, and the probe's verdict never escalates on it.
        internal const float ProbeStaleConfidence = ContactEvidenceLedger.MinEscalationConfidence;
        // The reasons of the tasks that exist because an approach is unwatched (the [Recon] row counts them).
        internal const string ReasonMainAxis = "main-axis", ReasonCapitalApproach = "capital-approach", ReasonFrontApproach = "front-approach";
        private const float SampleSpacingFraction = 0.5f; // approach samples, of the sight radius
        private const float SameSpotFraction = 0.5f;      // screens closer than this share one post
        private const float StandoffStepFraction = 0.25f; // pull-back step, of the standoff

        private const float PriorityScreenPosture = 5f;
        private const float PriorityMainAxis = 4.5f;
        private const float PriorityProbe = 4.2f;
        private const float PriorityCapitalApproach = 4f;
        private const float PriorityAdvanceGuard = 3.8f;
        private const float PriorityFrontApproach = 3f;
        private const float PriorityBelief = 2f;
        private const float PriorityPicket = 1f;

        private const FormationFlags Busy =
            FormationFlags.InBattle | FormationFlags.OnRetreat | FormationFlags.RaidForce | FormationFlags.SeaInvasion |
            FormationFlags.FortBuilder | FormationFlags.VanillaDepotBuilder | FormationFlags.InTransfer | FormationFlags.Embarked |
            FormationFlags.CapitalDefense | FormationFlags.InOffensiveOps | FormationFlags.InDefensiveOps;

        public static void PlanTasks(CampaignSnapshot s, SectorModel sectors, TheaterPlan plan, ReconMissionLedger missions, List<ReconTask> into)
        {
            if (into == null) return;
            into.Clear();
            if (s == null || !PlannedAlliance(s.Alliance)) return;
            if (!TaskRanges(s, missions, MinimumOf(plan), out float sight, out float commandRange)) return;

            var ctx = new Context(s, sectors, plan, missions, sight, commandRange);
            var candidates = new List<ReconTask>();
            AddScreenPostureTasks(ctx, candidates);
            AddMainEffortTasks(ctx, candidates);
            AddApproachTasks(ctx, candidates);
            AddBeliefTasks(ctx, candidates);
            AddPicketTasks(ctx, candidates);

            candidates.Sort(CompareTasks);
            Select(ctx, candidates, into);
        }

        // The tasks that exist to see the staged main effort's objective: the probe, the advance guard and the main-axis screen. The other
        // tasks watch approaches and beliefs; none of them is worth a sector's hold reserve (ForceAllocator.AssignRecon).
        internal static bool ScoutsMainObjective(in ReconTask task)
        {
            return ScoutsMainObjective(task.Kind, task.Reason);
        }

        internal static bool ScoutsMainObjective(ReconKind kind, string reason)
        {
            return kind == ReconKind.Probe || kind == ReconKind.AdvanceGuard
                || (kind == ReconKind.Screen && string.Equals(reason, ReasonMainAxis, StringComparison.Ordinal));
        }

        public static int PickFormation(CampaignSnapshot s, SectorModel sectors, TheaterPlan plan, ReconMissionLedger missions,
                                        in ReconTask task, ICollection<int> taken, bool mainEffortHasScreen)
        {
            if (s == null || !PlannedAlliance(s.Alliance)) return -1;

            float minimum = MinimumOf(plan);
            int incumbent = Incumbent(s, missions, task, taken, minimum);
            if (incumbent >= 0) return incumbent;

            bool hasMain = TryMainEffort(s, sectors, plan, out var main);
            bool ownMainScreen = hasMain && task.Kind == ReconKind.Screen && task.SectorId == main.Sector;
            bool guardMainCavalry = hasMain && !mainEffortHasScreen && !ownMainScreen &&
                                    MainBodyCavalryLeft(s, main, missions, taken, minimum) <= 1;

            float sectorOwn = 0f, sectorEnemy = 0f;
            if (task.Kind == ReconKind.Picket)
            {
                if (sectors == null) return -1;
                SectorStrengths(s, sectors, task.SectorId, out sectorOwn, out sectorEnemy);
            }
            float holdNeed = (TheaterPlanner.HoldRatio + (s.Tempo?.MinimumHoldRatioModifier ?? 0f)) * sectorEnemy;

            int best = -1, bestTier = int.MaxValue;
            float bestStrength = 0f, bestDistance = 0f;
            for (int i = 0; i < s.Own.Count; i++)
            {
                var f = s.Own[i];
                if (taken != null && taken.Contains(f.InstanceId)) continue;
                if (!CanMove(s, f, minimum, IsDetachment(task.Kind)) || IsRecalling(missions, f.InstanceId)) continue;
                if (!WlChainRules.MayMove(f.Chain)) continue;
                bool cavalry = f.CavalryShare >= MinCavalryShare;

                switch (task.Kind)
                {
                    case ReconKind.Picket:
                        if (SectorOf(sectors, f) != task.SectorId) continue;
                        // Only committed defenders count (SectorStrengths), and only one can be picked
                        // (MayMove), so the sector keeps its hold need after the picket leaves.
                        if (sectorOwn - f.Strength < holdNeed) continue;
                        break;
                    case ReconKind.AdvanceGuard:
                    case ReconKind.Probe:
                        if (!hasMain || f.Strength > AdvanceGuardMaxStrengthShare * main.BodyStrength) continue;
                        break;
                }
                if (guardMainCavalry && cavalry && InMainBody(s, main, f)) continue;

                int tier = cavalry ? 0 : 1;
                float distance = Distance(f.X, f.Z, task.FromX, task.FromZ);
                if (!Better(tier, f.Strength, distance, f.InstanceId, best, bestTier, bestStrength, bestDistance)) continue;
                best = f.InstanceId;
                bestTier = tier;
                bestStrength = f.Strength;
                bestDistance = distance;
            }
            return best;
        }

        // True when a sample every sampleSpacing along a -> b (both ends included) lies outside every
        // viewer's own radius (ViewerCoverage, the radius that paints the grid).
        public static bool UnwatchedAlong(float ax, float az, float bx, float bz, float sampleSpacing,
                                          float[] viewerX, float[] viewerZ, float[] viewerRadius, int viewerCount)
        {
            float length = Distance(ax, az, bx, bz);
            int steps = sampleSpacing > 0f ? (int)Math.Ceiling(length / sampleSpacing) : 1;
            if (steps < 1) steps = 1;
            for (int k = 0; k <= steps; k++)
            {
                float t = (float)k / steps;
                float x = ax + (bx - ax) * t;
                float z = az + (bz - az) * t;
                if (!ViewerCoverage.Covers(x, z, viewerX, viewerZ, viewerRadius, viewerCount)) return true;
            }
            return false;
        }

        // ---- task builders ----

        private static void AddScreenPostureTasks(Context ctx, List<ReconTask> into)
        {
            if (ctx.Plan == null || ctx.Sectors == null) return;
            foreach (var sector in ctx.Plan.Sectors)
            {
                if (sector == null || sector.Posture != TheaterPosture.Screen) continue;
                if (!OwnMean(ctx.S, ctx.Sectors, sector.SectorId, out float ox, out float oz))
                    ctx.Sectors.Centroid(sector.SectorId, out ox, out oz);
                if (!NearestThreat(ctx, ox, oz, out float tx, out float tz)) continue;
                PointShort(ox, oz, tx, tz, ctx.Standoff, out float hx, out float hz);
                TryAdd(ctx, into, ReconKind.Screen, sector.SectorId, ox, oz, hx, hz, PriorityScreenPosture, "screen-posture");
            }
        }

        private static void AddMainEffortTasks(Context ctx, List<ReconTask> into)
        {
            if (!ctx.HasMain) return;
            var m = ctx.Main;

            if (ctx.Unwatched(m.FrontX, m.FrontZ, m.TargetX, m.TargetZ))
                TryAdd(ctx, into, ReconKind.Screen, m.Sector, m.FrontX, m.FrontZ, m.TargetX, m.TargetZ, PriorityMainAxis, ReasonMainAxis);

            // D1/D7: a probe asks one question, whether the main objective is held; ProbeWanted is false once a fresh belief lies near it or a
            // viewer's sight covers it (the two facts TheaterPlanner.IsScouted reads, an afloat belief included), so the question is
            // answered, the task is gone and the mission stands down.
            if (ProbeWanted(ctx))
            {
                PointShort(m.FrontX, m.FrontZ, m.TargetX, m.TargetZ, ctx.Standoff, out float px, out float pz);
                TryAdd(ctx, into, ReconKind.Probe, m.Sector, m.FrontX, m.FrontZ, px, pz, PriorityProbe, "probe");
            }

            float enemyAtTarget = EnemyBeliefQuery.StrengthWithin(ctx.S.Beliefs, m.TargetX, m.TargetZ, ctx.S.RelocationRange);
            EnemyBeliefQuery.Nearest(ctx.S.Beliefs, m.FrontX, m.FrontZ, out float nearestToFront);
            float axis = Distance(m.FrontX, m.FrontZ, m.TargetX, m.TargetZ);
            if (m.BodyStrength > m.RequiredRatio * enemyAtTarget && nearestToFront > ctx.Sight && axis > ctx.Sight)
            {
                float ax = m.FrontX + (m.TargetX - m.FrontX) / axis * ctx.Sight;
                float az = m.FrontZ + (m.TargetZ - m.FrontZ) / axis * ctx.Sight;
                TryAdd(ctx, into, ReconKind.AdvanceGuard, m.Sector, m.FrontX, m.FrontZ, ax, az, PriorityAdvanceGuard, "advance-guard");
            }
        }

        private static void AddApproachTasks(Context ctx, List<ReconTask> into)
        {
            if (ctx.Sectors == null || ctx.Sectors.Count == 0) return;
            var targets = ApproachTargets(ctx);
            float scale = Math.Max(1f, ctx.S.RelocationRange);
            foreach (int sector in targets)
            {
                ctx.Sectors.Centroid(sector, out float tx, out float tz);
                if (ctx.S.HasCapital && ctx.Unwatched(ctx.S.CapitalX, ctx.S.CapitalZ, tx, tz))
                {
                    float d = Distance(ctx.S.CapitalX, ctx.S.CapitalZ, tx, tz);
                    TryAdd(ctx, into, ReconKind.Screen, -1, ctx.S.CapitalX, ctx.S.CapitalZ, tx, tz,
                        PriorityCapitalApproach + 0.5f / (1f + d / scale), ReasonCapitalApproach);
                }
                if (ctx.HasMain && ctx.Unwatched(ctx.Main.FrontX, ctx.Main.FrontZ, tx, tz))
                {
                    float d = Distance(ctx.Main.FrontX, ctx.Main.FrontZ, tx, tz);
                    TryAdd(ctx, into, ReconKind.Screen, -1, ctx.Main.FrontX, ctx.Main.FrontZ, tx, tz,
                        PriorityFrontApproach + 0.5f / (1f + d / scale), ReasonFrontApproach);
                }
            }
        }

        private static void AddBeliefTasks(Context ctx, List<ReconTask> into)
        {
            var beliefs = ctx.S.Beliefs;
            float maxRadius = 0f, maxStrength = 0f;
            for (int i = 0; i < beliefs.Count; i++)
            {
                if (beliefs[i].Confidence01 <= 0f) continue;
                maxRadius = Math.Max(maxRadius, beliefs[i].UncertaintyRadius);
                maxStrength = Math.Max(maxStrength, beliefs[i].Strength);
            }
            for (int i = 0; i < beliefs.Count; i++)
            {
                var b = beliefs[i];
                if (b.Confidence01 <= 0f || ctx.Covered(b.X, b.Z)) continue;
                if (!NearestOwn(ctx.S, b.X, b.Z, out float fx, out float fz))
                {
                    if (!ctx.S.HasCapital) continue;
                    fx = ctx.S.CapitalX;
                    fz = ctx.S.CapitalZ;
                }
                float age = Math.Max(0, ctx.S.DaySerial - b.LastSeenDay);
                float score = AgeWeight * Math.Min(1f, age / EnemyArmyModel.ExpireDays) +
                              RadiusWeight * (maxRadius > 0f ? b.UncertaintyRadius / maxRadius : 0f) +
                              StrengthWeight * (maxStrength > 0f ? b.Strength / maxStrength : 0f);
                PointShort(fx, fz, b.X, b.Z, ctx.Standoff, out float hx, out float hz);
                TryAdd(ctx, into, ReconKind.Screen, -1, fx, fz, hx, hz, PriorityBelief + score, "belief");
            }
        }

        private static void AddPicketTasks(Context ctx, List<ReconTask> into)
        {
            if (ctx.Plan == null || ctx.Sectors == null) return;
            var s = ctx.S;
            var seen = new List<long>();
            for (int i = 0; i < s.Objectives.Count; i++)
            {
                var o = s.Objectives[i];
                if (o.HolderAlliance != s.Alliance || (o.Roles & ObjectiveRole.Capital) != 0) continue;
                if (o.Roles == ObjectiveRole.None && o.Importance01 < 0.5f) continue;
                TryPicket(ctx, into, seen, o.X, o.Z, PriorityPicket + SnapshotRules.Clamp01(o.Importance01), "picket-town");
            }
            for (int i = 0; i < s.Supply.Count; i++)
            {
                var d = s.Supply[i];
                if (d.Kind != SupplySourceKind.Depot || d.Owner != s.Alliance || !d.Built) continue;
                TryPicket(ctx, into, seen, d.X, d.Z, PriorityPicket + 0.25f, "picket-depot");
            }
        }

        private static void TryPicket(Context ctx, List<ReconTask> into, List<long> seen, float x, float z, float priority, string reason)
        {
            long spot = ((long)BitConverter.SingleToInt32Bits(x) << 32) | (uint)BitConverter.SingleToInt32Bits(z);
            if (seen.Contains(spot)) return;
            seen.Add(spot);
            int sector = ctx.Sectors.SectorAt(x, z);
            var posture = PostureOf(ctx.Plan, sector);
            if (posture != TheaterPosture.Hold && posture != TheaterPosture.Screen) return;
            if (ctx.Covered(x, z) || ctx.NearestAnchor(x, z) < ctx.Standoff) return;
            into.Add(new ReconTask { Kind = ReconKind.Picket, SectorId = sector, FromX = x, FromZ = z, HoldX = x, HoldZ = z, Priority = priority, Reason = reason });
        }

        // Standoff pull-back, then the supply clamp; adds nothing when no point qualifies.
        private static void TryAdd(Context ctx, List<ReconTask> into, ReconKind kind, int sector, float fromX, float fromZ,
                                   float holdX, float holdZ, float priority, string reason)
        {
            if (!PullBackForStandoff(ctx, fromX, fromZ, ref holdX, ref holdZ)) return;
            if (!LogisticsGate.ClampToReach(ctx.S, fromX, fromZ, ref holdX, ref holdZ, ctx.CommandRange, 0f)) return;
            if (ctx.NearestAnchor(holdX, holdZ) < ctx.Standoff - 0.01f) return;
            if (sector < 0 && ctx.Sectors != null) sector = ctx.Sectors.SectorAt(holdX, holdZ);
            into.Add(new ReconTask { Kind = kind, SectorId = sector, FromX = fromX, FromZ = fromZ, HoldX = holdX, HoldZ = holdZ, Priority = priority, Reason = reason });
        }

        private static bool PullBackForStandoff(Context ctx, float fromX, float fromZ, ref float x, ref float z)
        {
            if (ctx.NearestAnchor(x, z) >= ctx.Standoff - 0.01f) return true;
            float length = Distance(fromX, fromZ, x, z);
            float step = Math.Max(0.01f, ctx.Standoff * StandoffStepFraction);
            int steps = (int)Math.Ceiling(length / step);
            float sx = x, sz = z;
            for (int k = 1; k <= steps; k++)
            {
                float t = Math.Min(1f, k * step / Math.Max(length, 0.0001f));
                float px = sx + (fromX - sx) * t;
                float pz = sz + (fromZ - sz) * t;
                if (ctx.NearestAnchor(px, pz) < ctx.Standoff - 0.01f) continue;
                x = px;
                z = pz;
                return true;
            }
            return false;
        }

        // Highest priority first; screens closer than half a sight radius share one post; at most
        // MaxScreensPerTheater screens per theater and MaxMissionsPerAlliance tasks in all.
        private static void Select(Context ctx, List<ReconTask> candidates, List<ReconTask> into)
        {
            var screensPerTheater = new Dictionary<Theater, int>();
            float sameSpot = SameSpotFraction * ctx.Sight;
            foreach (var task in candidates)
            {
                if (into.Count >= MaxMissionsPerAlliance) return;
                if (task.Kind == ReconKind.Screen)
                {
                    if (SharesPost(into, task, sameSpot)) continue;
                    var theater = TheaterOfTask(ctx.Sectors, task);
                    screensPerTheater.TryGetValue(theater, out int n);
                    if (n >= MaxScreensPerTheater) continue;
                    screensPerTheater[theater] = n + 1;
                }
                into.Add(task);
            }
        }

        private static bool SharesPost(List<ReconTask> kept, in ReconTask task, float radius)
        {
            for (int i = 0; i < kept.Count; i++)
                if (kept[i].Kind == ReconKind.Screen && Distance(kept[i].HoldX, kept[i].HoldZ, task.HoldX, task.HoldZ) < radius)
                    return true;
            return false;
        }

        private static Theater TheaterOfTask(SectorModel sectors, in ReconTask task)
        {
            if (sectors != null && task.SectorId >= 0) return sectors.TheaterOf(task.SectorId);
            return TheaterClassifier.FromPosition(task.HoldX, task.HoldZ);
        }

        private static int CompareTasks(ReconTask a, ReconTask b)
        {
            int c = b.Priority.CompareTo(a.Priority);
            if (c != 0) return c;
            c = ((int)a.Kind).CompareTo((int)b.Kind);
            if (c != 0) return c;
            c = a.HoldX.CompareTo(b.HoldX);
            if (c != 0) return c;
            c = a.HoldZ.CompareTo(b.HoldZ);
            return c != 0 ? c : a.SectorId.CompareTo(b.SectorId);
        }

        // ---- main effort ----

        private struct MainEffort
        {
            public int Sector;
            public float TargetX, TargetZ, FrontX, FrontZ;
            public float BodyStrength, RequiredRatio;
        }

        // The main effort's objective is the main SectorPlan's target. Its front is the
        // strength-weighted centre of the own formations in the main sector, else the own formation
        // nearest the objective; its main body is every own formation within the relocation range of
        // the front (vanilla's own gathering radius, 14257-14275) that counts as a committed defender
        // (stage F's WlChainRules: never the W&L player's own command or a map-group player, C4).
        private static bool TryMainEffort(CampaignSnapshot s, SectorModel sectors, TheaterPlan plan, out MainEffort m)
        {
            m = default(MainEffort);
            if (plan == null || plan.MainSectorId < 0) return false;
            SectorPlan target = null;
            foreach (var sector in plan.Sectors)
                if (sector != null && sector.SectorId == plan.MainSectorId) { target = sector; break; }
            if (target == null) return false;

            m.Sector = plan.MainSectorId;
            m.TargetX = target.TargetX;
            m.TargetZ = target.TargetZ;
            m.RequiredRatio = RequiredRatioOf(s, plan, target);
            if (!OwnMean(s, sectors, m.Sector, out m.FrontX, out m.FrontZ) && !NearestOwn(s, m.TargetX, m.TargetZ, out m.FrontX, out m.FrontZ))
                return false;
            for (int i = 0; i < s.Own.Count; i++)
                if (InMainBody(s, m, s.Own[i]) && WlChainRules.MayMove(s.Own[i].Chain))
                    m.BodyStrength += Math.Max(0f, s.Own[i].Strength);
            return true;
        }

        // The odds the main effort needs: the plan's, else the sector's own, else vanilla's offensive dominance.
        internal static float RequiredRatioOf(CampaignSnapshot s, TheaterPlan plan, SectorPlan target)
        {
            return plan.MainRequiredRatio > 0f ? plan.MainRequiredRatio
                 : target.RequiredRatio > 0f ? target.RequiredRatio : s.OffensiveDominance;
        }

        private static bool InMainBody(CampaignSnapshot s, in MainEffort m, in FormationRecord f)
        {
            return Distance(f.X, f.Z, m.FrontX, m.FrontZ) <= Math.Max(0f, s.RelocationRange);
        }

        // The probe goes where the main objective's picture is absent or stale: no belief of
        // confidence >= 0.5 within the relocation range, the objective out of every viewer's sight,
        // and the main effort not in contact there (in battle in the main sector, or within its
        // bugle range of the objective).
        private static bool ProbeWanted(Context ctx)
        {
            var s = ctx.S;
            var m = ctx.Main;
            if (ctx.Covered(m.TargetX, m.TargetZ)) return false;
            for (int i = 0; i < s.Own.Count; i++)
            {
                var f = s.Own[i];
                if ((f.Flags & FormationFlags.InBattle) != 0 && SectorOf(ctx.Sectors, f) == m.Sector) return false;
                if (Distance(f.X, f.Z, m.TargetX, m.TargetZ) <= f.BugleRange) return false;
            }
            float radius = Math.Max(0f, s.RelocationRange);
            for (int i = 0; i < s.Beliefs.Count; i++)
            {
                var b = s.Beliefs[i];
                if (b.LastSource == DetectionSource.Mark) continue;   // a mark never answers the probe's question (foresight F4)
                if (b.Confidence01 >= ProbeStaleConfidence && Distance(b.X, b.Z, m.TargetX, m.TargetZ) <= radius) return false;
            }
            return true;
        }

        private static int MainBodyCavalryLeft(CampaignSnapshot s, in MainEffort m, ReconMissionLedger missions, ICollection<int> taken, float minimum)
        {
            int n = 0;
            for (int i = 0; i < s.Own.Count; i++)
            {
                var f = s.Own[i];
                if (f.CavalryShare < MinCavalryShare || !InMainBody(s, m, f)) continue;
                if (taken != null && taken.Contains(f.InstanceId)) continue;
                if (!CanMove(s, f, minimum) || !WlChainRules.MayMove(f.Chain) || IsRecalling(missions, f.InstanceId)) continue;
                n++;
            }
            return n;
        }

        // ---- shared helpers ----

        private static bool PlannedAlliance(int alliance)
        {
            return alliance == 0 || alliance == 1;
        }

        // Vanilla's offensive-availability minima (IsUnitAvailableForOffensiveOperations 14091-14106) for a screen or picket. D7 (smoke
        // amendments 2026-10-16): an advance guard or a probe is a detachment of the main body, not an offensive operation, so it needs
        // readiness (SetWaypoint refuses a march onto enemy ground below step 2, 91336-91355) and morale but not the 5000-man minimum of
        // a whole operation (14099): a brigade or a division detached forward leads the advance, as many 1861 advances were led.
        // minStrength is the plan's MinGarrisonStrength: the scenario's aiminimumstrengthformovement validated by the planner (foresight F5).
        private static bool CanMove(CampaignSnapshot s, in FormationRecord f, float minStrength, bool detachment = false)
        {
            if ((f.Flags & Busy) != 0) return false;
            if (f.ReadinessStep < 2) return false;                  // 14091
            if (detachment ? f.Strength <= 0f : f.Strength <= minStrength) return false; // 14099
            if (f.Morale <= s.MinMoraleForMovement) return false;     // 14103
            return f.CavalryShare >= MinCavalryShare || (f.Flags & FormationFlags.FightingForce) != 0;
        }

        // Whether the formation meets a task kind's own movement standard (the pick's first test): AI-orderable, ready, unbusy, strong
        // enough (a detachment needs only a body, a screen or a picket the vanilla offensive minimum), with the morale to march. The
        // allocator tells a task nobody could take (none-able) from one every able formation was already given away from.
        internal static bool CouldScout(CampaignSnapshot s, in FormationRecord f, ReconKind kind, float minStrength)
        {
            return WlChainRules.MayMove(f.Chain) && CanMove(s, f, minStrength, IsDetachment(kind));
        }

        private static bool IsDetachment(ReconKind kind)
        {
            return kind == ReconKind.AdvanceGuard || kind == ReconKind.Probe;
        }

        // How many formations the planner could send on a reconnaissance task at all (the [Recon] row's pool=): the AI-orderable, ready,
        // unbusy formations, a detachment's standard being the widest. 0 explains a day of unwatched approaches and no mission.
        internal static int ScoutPool(CampaignSnapshot s, TheaterPlan plan)
        {
            float minimum = MinimumOf(plan);
            int n = 0;
            for (int i = 0; i < s.Own.Count; i++)
                if (WlChainRules.MayMove(s.Own[i].Chain) && CanMove(s, s.Own[i], minimum, detachment: true)) n++;
            return n;
        }

        // The minimum strength every check here applies: the plan's (the scenario's aiminimumstrengthformovement, validated by the planner),
        // the fallback when no plan is at hand (foresight F5).
        private static float MinimumOf(TheaterPlan plan)
        {
            return plan != null ? plan.MinGarrisonStrength : TheaterPlanner.MinStrengthFallback;
        }

        // The formations the mass rule accepts and the scout pick refuses for one reason alone: they stand in a vanilla list the planner does not
        // treat as busy for massing (unitsinoffensiveoperations, groupstodefendcapital). The [Recon] row's listed=, so a day of tasks and
        // no scout says whether the body the plan massed is one of them.
        internal static int ListedCount(CampaignSnapshot s)
        {
            const FormationFlags lists = FormationFlags.InOffensiveOps | FormationFlags.CapitalDefense;
            int n = 0;
            for (int i = 0; i < s.Own.Count; i++)
                if ((s.Own[i].Flags & lists) != 0 && TheaterPlanner.Movable(s.Own[i]) && WlChainRules.MayMove(s.Own[i].Chain)) n++;
            return n;
        }

        // Smallest sight radius and command range over the AI formations that could take a task.
        private static bool TaskRanges(CampaignSnapshot s, ReconMissionLedger missions, float minimum, out float sight, out float commandRange)
        {
            sight = float.MaxValue;
            commandRange = float.MaxValue;
            for (int i = 0; i < s.Own.Count; i++)
            {
                var f = s.Own[i];
                if (!CanMove(s, f, minimum, detachment: true) || !WlChainRules.MayMove(f.Chain) || IsRecalling(missions, f.InstanceId)) continue;
                if (f.SightRadius <= 0f || f.CommandRange <= 0f) continue;
                sight = Math.Min(sight, f.SightRadius);
                commandRange = Math.Min(commandRange, f.CommandRange);
            }
            return sight < float.MaxValue;
        }

        private static int Incumbent(CampaignSnapshot s, ReconMissionLedger missions, in ReconTask task, ICollection<int> taken, float minimum)
        {
            if (missions == null) return -1;
            foreach (var m in missions.Missions)
            {
                if (m.Kind != task.Kind || !ReconMissionLedger.IsActive(m.State)) continue;
                int i = s.IndexOfOwn(m.InstanceId);
                if (i < 0) continue;
                var f = s.Own[i];
                if (taken != null && taken.Contains(f.InstanceId)) continue;
                if (Distance(m.HoldX, m.HoldZ, task.HoldX, task.HoldZ) > SameSpotFraction * f.SightRadius) continue;
                if (!CanMove(s, f, minimum) || !WlChainRules.MayMove(f.Chain)) continue;
                return f.InstanceId;
            }
            return -1;
        }

        private static bool IsRecalling(ReconMissionLedger missions, int instanceId)
        {
            return missions != null && missions.IsRecalling(instanceId);
        }

        private static bool Better(int tier, float strength, float distance, int id, int best, int bestTier, float bestStrength, float bestDistance)
        {
            if (best < 0) return true;
            if (tier != bestTier) return tier < bestTier;
            if (tier != 0 && strength != bestStrength) return strength < bestStrength; // smallest fighting group
            if (distance != bestDistance) return distance < bestDistance;
            return id < best;
        }

        private static int SectorOf(SectorModel sectors, in FormationRecord f)
        {
            if (sectors == null) return -1;
            int sector = f.AreaIndex >= 0 ? sectors.SectorOfArea(f.AreaIndex) : -1;
            return sector >= 0 ? sector : sectors.SectorAt(f.X, f.Z);
        }

        private static bool OwnMean(CampaignSnapshot s, SectorModel sectors, int sector, out float x, out float z)
        {
            x = z = 0f;
            if (sectors == null || sector < 0) return false;
            float total = 0f;
            for (int i = 0; i < s.Own.Count; i++)
            {
                var f = s.Own[i];
                if (SectorOf(sectors, f) != sector) continue;
                float w = Math.Max(1f, f.Strength);
                x += f.X * w;
                z += f.Z * w;
                total += w;
            }
            if (total <= 0f) return false;
            x /= total;
            z /= total;
            return true;
        }

        private static bool NearestOwn(CampaignSnapshot s, float x, float z, out float ox, out float oz)
        {
            ox = oz = 0f;
            float best = float.MaxValue;
            for (int i = 0; i < s.Own.Count; i++)
            {
                float d = Distance(s.Own[i].X, s.Own[i].Z, x, z);
                if (d >= best) continue;
                best = d;
                ox = s.Own[i].X;
                oz = s.Own[i].Z;
            }
            return best < float.MaxValue;
        }

        // The nearest live belief to (x, z), else the nearest enemy-held objective target.
        private static bool NearestThreat(Context ctx, float x, float z, out float tx, out float tz)
        {
            int b = EnemyBeliefQuery.Nearest(ctx.S.Beliefs, x, z, out _);
            if (b >= 0)
            {
                tx = ctx.S.Beliefs[b].X;
                tz = ctx.S.Beliefs[b].Z;
                return true;
            }
            tx = tz = 0f;
            float best = float.MaxValue;
            foreach (var o in ctx.S.Objectives)
            {
                if (!EnemyHeld(ctx.S, o)) continue;
                float d = Distance(o.X, o.Z, x, z);
                if (d >= best) continue;
                best = d;
                tx = o.X;
                tz = o.Z;
            }
            return best < float.MaxValue;
        }

        private static List<int> ApproachTargets(Context ctx)
        {
            var sectors = new List<int>();
            foreach (var b in ctx.S.Beliefs)
                if (b.Confidence01 > 0f) AddSector(sectors, ctx.Sectors.SectorAt(b.X, b.Z));
            foreach (var o in ctx.S.Objectives)
                if (EnemyHeld(ctx.S, o)) AddSector(sectors, ctx.Sectors.SectorAt(o.X, o.Z));
            sectors.Sort();
            return sectors;
        }

        private static void AddSector(List<int> sectors, int sector)
        {
            if (sector >= 0 && !sectors.Contains(sector)) sectors.Add(sector);
        }

        private static bool EnemyHeld(CampaignSnapshot s, in ObjectiveRecord o)
        {
            return o.HolderAlliance >= 0 && o.HolderAlliance != s.Alliance && !o.Accomplished;
        }

        private static void SectorStrengths(CampaignSnapshot s, SectorModel sectors, int sector, out float own, out float enemy)
        {
            own = enemy = 0f;
            for (int i = 0; i < s.Own.Count; i++)
                if (SectorOf(sectors, s.Own[i]) == sector && WlChainRules.MayMove(s.Own[i].Chain))
                    own += Math.Max(0f, s.Own[i].Strength);
            for (int i = 0; i < s.Beliefs.Count; i++)
            {
                var b = s.Beliefs[i];
                if (b.Confidence01 <= 0f || sectors.SectorAt(b.X, b.Z) != sector) continue;
                enemy += Math.Max(0f, b.Strength) * Math.Min(1f, b.Confidence01);
            }
        }

        private static TheaterPosture? PostureOf(TheaterPlan plan, int sector)
        {
            foreach (var p in plan.Sectors)
                if (p != null && p.SectorId == sector) return p.Posture;
            return null;
        }

        // The point `distance` short of (tx, tz) on the segment from (fx, fz); the start when the
        // segment is shorter.
        private static void PointShort(float fx, float fz, float tx, float tz, float distance, out float x, out float z)
        {
            float length = Distance(fx, fz, tx, tz);
            if (length <= distance)
            {
                x = fx;
                z = fz;
                return;
            }
            float t = (length - distance) / length;
            x = fx + (tx - fx) * t;
            z = fz + (tz - fz) * t;
        }

        private static float Distance(float ax, float az, float bx, float bz)
        {
            float dx = ax - bx;
            float dz = az - bz;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }

        // One review's derived inputs: viewers (own formations not on a recon mission), standoff
        // anchors (live beliefs and enemy-held objective targets) and the main effort.
        private sealed class Context
        {
            public readonly CampaignSnapshot S;
            public readonly SectorModel Sectors;
            public readonly TheaterPlan Plan;
            public readonly float Sight, CommandRange, Standoff, Spacing;
            public readonly bool HasMain;
            public readonly MainEffort Main;
            private readonly float[] _viewerX, _viewerZ, _viewerRadius;
            private readonly int _viewers;
            private readonly List<float> _anchorX = new List<float>(), _anchorZ = new List<float>();

            public Context(CampaignSnapshot s, SectorModel sectors, TheaterPlan plan, ReconMissionLedger missions, float sight, float commandRange)
            {
                S = s;
                Sectors = sectors;
                Plan = plan;
                Sight = sight;
                CommandRange = commandRange;
                Standoff = StandoffFraction * sight;
                Spacing = SampleSpacingFraction * sight;
                HasMain = TryMainEffort(s, sectors, plan, out Main);

                _viewerX = new float[s.Own.Count];
                _viewerZ = new float[s.Own.Count];
                _viewerRadius = new float[s.Own.Count];
                for (int i = 0; i < s.Own.Count; i++)
                {
                    var f = s.Own[i];
                    if (f.SightRadius <= 0f || (missions != null && missions.IsRecon(f.InstanceId))) continue;
                    _viewerX[_viewers] = f.X;
                    _viewerZ[_viewers] = f.Z;
                    _viewerRadius[_viewers] = f.SightRadius;
                    _viewers++;
                }
                foreach (var b in s.Beliefs)
                {
                    if (b.Confidence01 <= 0f) continue;
                    _anchorX.Add(b.X);
                    _anchorZ.Add(b.Z);
                }
                foreach (var o in s.Objectives)
                {
                    if (!EnemyHeld(s, o)) continue;
                    _anchorX.Add(o.X);
                    _anchorZ.Add(o.Z);
                }
            }

            public bool Covered(float x, float z)
            {
                return ViewerCoverage.Covers(x, z, _viewerX, _viewerZ, _viewerRadius, _viewers);
            }

            public bool Unwatched(float ax, float az, float bx, float bz)
            {
                return UnwatchedAlong(ax, az, bx, bz, Spacing, _viewerX, _viewerZ, _viewerRadius, _viewers);
            }

            public float NearestAnchor(float x, float z)
            {
                float best = float.MaxValue;
                for (int i = 0; i < _anchorX.Count; i++)
                    best = Math.Min(best, Distance(_anchorX[i], _anchorZ[i], x, z));
                return best;
            }
        }
    }
}
