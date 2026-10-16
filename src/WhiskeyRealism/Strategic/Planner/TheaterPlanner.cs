using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using static WhiskeyRealism.Strategic.Planner.ReserveRules;

namespace WhiskeyRealism.Strategic.Planner
{
    // The live theater campaign plan of one AI alliance for one daily review (spec component 4;
    // plan G3, P3-P5). Pure: snapshot in, TheaterPlan out; PlannerState carries the hysteresis.
    //
    // Candidates: every target of an objective vanilla offers the alliance (ObjectiveRecord
    // .Available = GetAvailableObjectives 178827-178866, not accomplished) that another alliance
    // holds. Each is scored with terms normalized to [0, 1] (P3):
    //   value    0.5 x the target's importance (vanilla's own per-target term, UpdateImportanceValues
    //            14905-14995) + 0.25 x its area's importancevalues + 0.25 x its role (capital 1;
    //            rail hub, port, industry 0.5 each).
    //   balance  massable own strength M / (M + believed land enemy near the target), scaled by the
    //            force commander's AttackBias (x (1 + 0.5 x bias)). M (MassFor, G3 verify F4) is the
    //            strength G4 can concentrate: own formations that are movable (not busy), committed
    //            (WlChainRules.MayMove: never the W&L player's command or map
    //            group), not Recover (raw Supply01 >= 0.4, morale >= aiminimummoraleformovement) and
    //            within MarchHorizonDays of the target, less the capital reserve (the formations
    //            nearest the capital up to CapitalFloorStrength: G4's CapitalGuard rule) and less every
    //            other sector's hold reserve (its formations nearest the sector's believed enemy up
    //            to its HoldNeed, after the formations that hold there anyway: G4's Hold withholding
    //            rule). The main effort's set is published as TheaterPlan.MassInstanceIds.
    //   reach    LogisticsGate.Evaluate for the candidate force with its commander's SupplySlack
    //            (vanilla's per-unit supply rule 114272-114306): 0 out of reach, else 0.5 + 0.5 x slack.
    //   march    1 - the candidate force's march-time estimate (MarchTime, G0 landing B) / 30 days.
    //   exposure 1 - believed enemy near the axis flank (within half the relocation range of the
    //            force -> target segment, outside the target's own radius) / (that + force strength).
    //   capital  own capital threat x closeness of the target to the own capital, or 1 for an
    //            enemy capital.
    //   + PriorLambda x HistoricalPriorTable.PriorFor (<= PriorMaxContribution).
    // The candidate force is the nearest member of that set with at least MainBodyShare of its
    // largest member's strength. Eligible for the main effort only when M / believed enemy near the
    // target reaches RequiredRatio = aidominanceforoffensiveoperations x (1 + Tempo.MassRatioModifier)
    // + CommanderProfile.RequiredOddsShift (vanilla's own offensive dominance, 14249-14250, C17).
    //
    // Fog (spec component 2): only snapshot beliefs (confidence-weighted) are enemy input. A land
    // belief counts where its straight distance is inside the radius, weighted min(1, radius /
    // RiverLines.EffectiveDistance): a river line delays a threat, it never removes it. An afloat
    // belief (EnemyTraits.Embarked) cannot be engaged by land (EstablishCampaignBattle 80632-80635)
    // and moves at the cost-1 river/sea rate: it counts at its straight distance in the capital
    // threat and the axis exposure, never in a land balance or a sector's hold need.
    //
    // Capital (P7): floor = CapitalFloorRatio x vanilla's own threat weighting on beliefs
    // (AssignUnitToDefendCapital 11712-11741: strength / max(10, d)^0.3 x 0.5 for every enemy within
    // relocation range), threat = floor / own sum of min(str, str x morale) within relocation range of
    // the capital, clamped to [0, 1]. (Vanilla instead subtracts str x morale of its own units whose
    // last waypoint is nearer the capital than the nearest enemy, 11744-11749; Whiskey reads current
    // positions, except the W&L map-group player's group, which counts where its last waypoint lies
    // as vanilla counts it: ReserveRules.HoldPoint, decision 2026-10-15.)
    //
    // River lines are judged through ReviewTerrain (G3 verify F3): a review uses them only when every
    // line and river point it asks about is already known; otherwise it is scored with none (straight
    // distances, no river corridor) and the unknown questions are probed in a fixed order.
    //
    // Hysteresis (P4): the main effort switches when a challenger beats it by more than SwitchMargin
    // on SwitchDays consecutive reviews, or at once on a trigger: the onset of a capital threat (it
    // crosses CapitalThreatTrigger; latched in PlannerState until it falls below CapitalThreatRelease,
    // and while it stands the margin rule applies as normal), the current objective accomplished /
    // no longer offered / below its odds (below RequiredRatio - OddsDeadband, or below RequiredRatio
    // on SwitchDays consecutive reviews), or a decisive battle in its theater or near it
    // (CicReviewRouter.BattleInvalidatesPlan, the R4 rule). A trigger keeps the current main effort
    // while it is within Epsilon of the best.
    // Randomness (P5) only at a decision: softmax (Temperature) over the candidates within Epsilon of
    // the best, System.Random seeded with CampaignSeed ^ alliance x 7919 ^ DecisionSerial.
    //
    // Postures: MainEffort the main target's sector; then, for sectors with own formations or a
    // believed land threat: Recover (own formations average raw Supply01 < 0.4 or morale below
    // aiminimummoraleformovement), Hold (believed threat and own >= hold need = (HoldRatio +
    // Tempo.MinimumHoldRatioModifier) x threat), Screen (believed threat above what own can hold,
    // or no own formations), Support (centroid within relocation range of the main axis),
    // EconomyOfForce (own formations, no threat). SectorPlan.HoldNeed carries each sector's need.
    internal static class TheaterPlanner
    {
        public const float ValueWeight = 0.30f, BalanceWeight = 0.25f, ReachWeight = 0.15f, DistanceWeight = 0.10f, ExposureWeight = 0.10f, CapitalThreatWeight = 0.10f;
        public const float PriorLambda = 0.08f, PriorMaxContribution = 0.08f, SwitchMargin = 0.12f, Epsilon = 0.04f, Temperature = 0.02f, CapitalThreatTrigger = 0.6f;
        public const int SwitchDays = 3;
        public const float HoldRatio = 0.6f;
        public const float RecoverSupplyBelow = 0.4f; // raw Supply01 (vanilla's halved scale: 0.2)
        public const float MarchHorizonDays = 30f;
        public const float CapitalFloorRatio = 1f;    // P7
        public const float MainBodyShare = 0.5f;
        public const float AttackBiasBalanceScale = 0.5f;
        public const float OddsDeadband = 0.10f;          // ratio units below RequiredRatio (F1)
        public const float CapitalThreatRelease = 0.4f;   // the latch re-arms below this (F2)
        public const int MaxTraceCandidates = 12;
        private const float VanillaThreatMinDistance = 10f, VanillaThreatExponent = 0.3f, VanillaThreatScale = 0.5f; // 11735
        // The flags that keep a formation out of the massable set (ForceAllocator keeps them in place).
        // InDefensiveOps (G5a, chapters decision 5): vanilla's defensive operations may take a deferred
        // mass member (the offense-only withhold leaves it to vanilla's defence, 13716-13717); a formation
        // in one is busy until vanilla ends it (13782-13848), so G3 drops it from the mass and G4 keeps it
        // Hold. Not InOffensiveOps: the mover lists its own dispatched units there, and that flag would
        // freeze the mass after its first dispatch.
        internal const FormationFlags Busy = FormationFlags.InBattle | FormationFlags.OnRetreat | FormationFlags.RaidForce | FormationFlags.SeaInvasion
            | FormationFlags.Embarked | FormationFlags.FortBuilder | FormationFlags.VanillaDepotBuilder | FormationFlags.InTransfer
            | FormationFlags.InDefensiveOps;

        internal struct Candidate
        {
            public int ObjectiveId;
            public float X, Z;
            public int SectorId;
            public int Force;          // own formation index, -1 none
            public float Value, Balance, Reach, March, Exposure, Capital, PriorTerm, Score;
            public float Ratio, Required, Mass, Enemy;
            public bool Eligible, InReach, Retained;
            public string PriorId;
        }

        internal sealed class Inputs
        {
            public CampaignSnapshot S;
            public SectorModel Sectors;
            public Func<int, CommanderProfile> Profile;
            public ITerrainLineProbe Probe;
            public MarchSpeedInputs Speed;
            public float CapitalFloor, CapitalThreat01;
            internal ReviewTerrain Terrain;   // marks the questions' order; null once scoring runs without rivers
            internal SectorFacts Facts;
            internal bool[] CapitalReserved;
            internal int[] HoldReserved;      // sector whose hold keeps the formation, -1 none
            internal float[] RoadPerDay;
            internal HashSet<int> OwnIips;
            internal readonly List<int> Scratch = new List<int>();
        }

        // Per-sector facts shared by the reserves, the postures and the published HoldNeed.
        internal sealed class SectorFacts
        {
            public float[] CX, CZ, Radius, Threat, Need, Own, Supply, Morale, NearX, NearZ;
            public int[] Count, Strongest;
            public int[] Orderable; // formations the planner can order (WlChainRules.MayMove): the base of the Supply/Morale averages
        }

        internal static TheaterPlan Plan(CampaignSnapshot s, SectorModel sectors, Func<int, CommanderProfile> sectorProfile, PlannerState state,
                                         IReadOnlyList<BattleHistoryRecord> battles, ITerrainLineSource terrain, in MarchSpeedInputs speed, out string trace)
        {
            var plan = new TheaterPlan { Alliance = s != null ? s.Alliance : -1, DaySerial = s != null ? s.DaySerial : 0 };
            trace = "";
            if (s == null || sectors == null || s.Alliance < 0 || s.Alliance > 1) return plan;
            if (state == null) state = new PlannerState();

            var review = new ReviewTerrain(terrain, s.Areas);
            var inputs = new Inputs { S = s, Sectors = sectors, Profile = sectorProfile, Probe = review, Terrain = review, Speed = speed };
            var candidates = new List<Candidate>();
            Evaluate(inputs, candidates);
            int unknown = review.Unknown, probed = 0;
            if (unknown > 0)
            {
                // Some line or river point is not known yet: no river term this review, for any
                // candidate (order-independent); fill the memo in the fixed order (F3).
                probed = review.ProbeUnknown();
                inputs.Probe = null;
                inputs.Terrain = null;
                Evaluate(inputs, candidates);
            }
            plan.CapitalFloorStrength = inputs.CapitalFloor;

            int main = Decide(inputs, candidates, state, battles, out string reason);
            plan.SwitchReason = reason;
            float axisFromX = 0f, axisFromZ = 0f, mainX = 0f, mainZ = 0f;
            if (main >= 0)
            {
                var m = candidates[main];
                var force = s.Own[m.Force];
                var profile = ProfileOf(inputs, m.Force);
                plan.MainObjectiveId = m.ObjectiveId;
                plan.MainSectorId = m.SectorId;
                plan.MainRequiredRatio = m.Required;
                plan.PriorId = m.PriorId;
                plan.MainEnemyStrength = m.Enemy;
                plan.MassStrength = MassFor(inputs, m.X, m.Z, m.SectorId, inputs.Scratch);
                for (int k = 0; k < inputs.Scratch.Count; k++) plan.MassInstanceIds.Add(s.Own[inputs.Scratch[k]].InstanceId);
                axisFromX = force.X;
                axisFromZ = force.Z;
                mainX = m.X;
                mainZ = m.Z;
                if (!m.InReach)
                {
                    // Don't outrun supply: the movers get the farthest in-reach point, the depot
                    // tasks carry the axis on (spec component 5).
                    if (!LogisticsGate.ClampToReach(s, force.X, force.Z, ref mainX, ref mainZ, force.CommandRange, profile.SupplySlack))
                    {
                        mainX = force.X;
                        mainZ = force.Z;
                    }
                    LogisticsGate.AddDepotTasks(s, plan, sectors, mainX, mainZ, m.X, m.Z, force.CommandRange, profile.SupplySlack);
                }
            }
            AssignPostures(inputs, plan, axisFromX, axisFromZ, mainX, mainZ);
            trace = Trace(inputs, candidates, plan) + (unknown > 0 ? " terrain=filling unknown=" + unknown + " probed=" + probed : " terrain=complete");
            return plan;
        }

        // The capital threat, the sector facts, the reserves, then every candidate target scored
        // (see the header). Asks its terrain questions through c.Probe.
        internal static void Evaluate(Inputs c, List<Candidate> into)
        {
            into.Clear();
            var s = c.S;
            float range = s.RelocationRange;
            float mass = s.Tempo != null ? s.Tempo.MassRatioModifier : 0f;
            c.Terrain?.Mark(ReviewTerrain.GroupCapital, 0, s.CapitalX, s.CapitalZ);
            CapitalThreat(c, out c.CapitalFloor, out c.CapitalThreat01);
            c.Facts = BuildSectorFacts(c);
            c.OwnIips = MarchTime.OwnIips(s);
            c.RoadPerDay = new float[s.Own.Count];
            for (int i = 0; i < s.Own.Count; i++)
                c.RoadPerDay[i] = MarchTime.SpeedPerDay(c.Speed, s.Own[i].UnitTyp, s.Own[i].GroupSpeed, c.Speed.RoadCost);
            Reserve(c);
            float maxAreaImportance = 0f;
            for (int i = 0; i < s.Areas.Count; i++) maxAreaImportance = Math.Max(maxAreaImportance, s.Areas[i].Importance);

            for (int i = 0; i < s.Objectives.Count; i++)
            {
                var o = s.Objectives[i];
                if (!o.Available || o.Accomplished || o.HolderAlliance == s.Alliance) continue;
                int area = AreaGeometry.AreaAt(s.Areas, o.X, o.Z); // SectorModel.SectorAt's own lookup, done once
                var cand = new Candidate { ObjectiveId = o.ObjectiveId, X = o.X, Z = o.Z, SectorId = area >= 0 ? c.Sectors.SectorOfArea(s.Areas[area].Index) : -1 };
                c.Terrain?.Mark(ReviewTerrain.GroupCandidate, o.ObjectiveId, o.X, o.Z);
                cand.Mass = MassFor(c, o.X, o.Z, cand.SectorId, c.Scratch);
                cand.Force = PickForce(s, c.Scratch, o.X, o.Z);
                var profile = ProfileOf(c, cand.Force);
                cand.Required = Math.Max(0f, s.OffensiveDominance * (1f + mass) + profile.RequiredOddsShift);

                float enemy = LandThreatNear(c, o.X, o.Z, range);
                float own = cand.Mass;
                cand.Enemy = enemy;
                cand.Ratio = enemy > 0f ? own / enemy : (cand.Force >= 0 ? float.MaxValue : 0f);
                float balance = own + enemy > 0f ? own / (own + enemy) : 0f;
                cand.Balance = SnapshotRules.Clamp01(balance * (1f + AttackBiasBalanceScale * profile.AttackBias));
                float area01 = area >= 0 && maxAreaImportance > 0f ? SnapshotRules.Clamp01(s.Areas[area].Importance / maxAreaImportance) : 0f;
                cand.Value = SnapshotRules.Clamp01(0.5f * o.Importance01 + 0.25f * area01 + 0.25f * Role01(o.Roles));
                cand.Capital = CapitalTerm(c, o);

                if (cand.Force >= 0)
                {
                    var f = s.Own[cand.Force];
                    var reach = LogisticsGate.Evaluate(s, o.X, o.Z, f.CommandRange, profile.SupplySlack);
                    cand.InReach = reach.InReach;
                    cand.Reach = reach.InReach ? 0.5f + 0.5f * reach.Slack01 : 0f;
                    float distance = RiverLines.EffectiveDistance(f.X, f.Z, o.X, o.Z, c.Probe, s.Crossings);
                    float days = MarchTime.EstimateDays(c.Speed, f, o.X, o.Z, distance, s, c.Probe, c.OwnIips);
                    cand.March = days < float.MaxValue ? 1f - SnapshotRules.Clamp01(days / MarchHorizonDays) : 0f;
                    float flank = FlankThreat(c, f.X, f.Z, o.X, o.Z, range);
                    cand.Exposure = flank + f.Strength > 0f ? flank / (flank + Math.Max(0f, f.Strength)) : 0f;
                }
                else cand.Exposure = 1f;

                float prior = HistoricalPriorTable.PriorFor(s.Alliance, o.ObjectiveId, s.DaySerial, out cand.PriorId);
                cand.PriorTerm = Math.Min(PriorMaxContribution, PriorLambda * prior);
                cand.Score = ValueWeight * cand.Value + BalanceWeight * cand.Balance + ReachWeight * cand.Reach + DistanceWeight * cand.March
                    + ExposureWeight * (1f - cand.Exposure) + CapitalThreatWeight * cand.Capital + cand.PriorTerm;
                cand.Eligible = cand.Force >= 0 && cand.Ratio >= cand.Required;
                into.Add(cand);
            }
            // A fixed candidate order (objective id, then position), so neither the tie-break nor the
            // seeded draw depends on the order vanilla lists objectives in.
            into.Sort(CompareCandidates);
        }

        private static int CompareCandidates(Candidate a, Candidate b)
        {
            int c = a.ObjectiveId.CompareTo(b.ObjectiveId);
            if (c == 0) c = a.X.CompareTo(b.X);
            if (c == 0) c = a.Z.CompareTo(b.Z);
            return c;
        }

        // ---- decision (P4, P5) ----

        private static int Decide(Inputs c, List<Candidate> cands, PlannerState state, IReadOnlyList<BattleHistoryRecord> battles, out string reason)
        {
            bool capitalOnset = CapitalOnset(state, c.CapitalThreat01);
            int current = state.CurrentMainObjective;
            Retain(cands, current, state);
            int best = Best(cands);
            int held = BestOf(cands, current);

            if (best < 0)
            {
                state.CurrentMainObjective = -1;
                state.CurrentMainSector = -1;
                state.BelowOddsDays = 0;
                ResetChallenger(state);
                reason = current >= 0 ? "no-candidate" : "none";
                return -1;
            }

            int chosen;
            if (current < 0)
            {
                chosen = Draw(c, cands, best, state);
                reason = "initial";
            }
            else if (Trigger(c, cands, current, held, battles, capitalOnset, out string trigger))
            {
                // A standing trigger (a capital threat lasts for days) never re-draws among near
                // equals: the current main effort stays while it is within Epsilon of the best.
                bool currentStillBest = held >= 0 && cands[held].Score >= cands[best].Score - Epsilon;
                chosen = currentStillBest ? held : Draw(c, cands, best, state);
                reason = cands[chosen].ObjectiveId != current ? trigger : "hold";
            }
            else if (cands[best].ObjectiveId != current && cands[best].Score - cands[held].Score > SwitchMargin)
            {
                if (state.ChallengerObjective == cands[best].ObjectiveId) state.ChallengerDays++;
                else
                {
                    state.ChallengerObjective = cands[best].ObjectiveId;
                    state.ChallengerDays = 1;
                }
                if (state.ChallengerDays >= SwitchDays)
                {
                    chosen = Draw(c, cands, best, state);
                    reason = "margin";
                }
                else
                {
                    chosen = held;
                    reason = "hold";
                }
            }
            else
            {
                ResetChallenger(state);
                chosen = held;
                reason = "hold";
            }

            if (cands[chosen].ObjectiveId != current)
            {
                state.CurrentMainObjective = cands[chosen].ObjectiveId;
                state.DecisionSerial++;
                state.BelowOddsDays = 0;
                ResetChallenger(state);
            }
            state.CurrentMainSector = cands[chosen].SectorId;
            return chosen;
        }

        // The odds deadband (F1): the current main effort keeps its eligibility while its odds are
        // within OddsDeadband below its RequiredRatio, for fewer than SwitchDays consecutive reviews.
        private static void Retain(List<Candidate> cands, int current, PlannerState state)
        {
            int index = -1;
            for (int i = 0; i < cands.Count; i++)
                if (cands[i].ObjectiveId == current && cands[i].Force >= 0 && (index < 0 || cands[i].Score > cands[index].Score)) index = i;
            if (index < 0) return;
            var cand = cands[index];
            if (cand.Eligible)
            {
                state.BelowOddsDays = 0;
                return;
            }
            if (cand.Ratio < cand.Required - OddsDeadband) return;
            state.BelowOddsDays++;
            if (state.BelowOddsDays >= SwitchDays) return;
            cand.Eligible = true;
            cand.Retained = true;
            cands[index] = cand;
        }

        // The capital trigger fires on onset only (F2): latched until the threat falls below
        // CapitalThreatRelease.
        private static bool CapitalOnset(PlannerState state, float threat01)
        {
            if (state.CapitalThreatLatched)
            {
                if (threat01 < CapitalThreatRelease) state.CapitalThreatLatched = false;
                return false;
            }
            if (threat01 < CapitalThreatTrigger) return false;
            state.CapitalThreatLatched = true;
            return true;
        }

        // A trigger switches at once (P4). `held` is the current objective's best eligible target, -1
        // when it is accomplished, no longer offered or below its odds (past the deadband).
        private static bool Trigger(Inputs c, List<Candidate> cands, int current, int held, IReadOnlyList<BattleHistoryRecord> battles,
                                    bool capitalOnset, out string reason)
        {
            reason = null;
            if (held < 0)
            {
                reason = ObjectiveAccomplished(c.S, current) ? "accomplished" : Offered(cands, current) ? "odds" : "unavailable";
                return true;
            }
            if (capitalOnset)
            {
                reason = "capital-threat";
                return true;
            }
            if (battles != null)
            {
                var target = cands[held];
                var query = new BattleReplanQuery
                {
                    HasTarget = true,
                    TargetX = target.X,
                    TargetZ = target.Z,
                    TargetTheater = TheaterClassifier.FromPosition(target.X, target.Z),
                    Radius = c.S.RelocationRange
                };
                for (int i = 0; i < battles.Count; i++)
                {
                    var record = battles[i];
                    if (record == null) continue;
                    query.BattleTheater = TheaterClassifier.FromPosition(record.PositionX, record.PositionZ);
                    if (!CicReviewRouter.BattleInvalidatesPlan(record, query)) continue;
                    reason = "battle";
                    return true;
                }
            }
            return false;
        }

        // The eligible candidate with the highest score (lower objective id, then list order, on a tie).
        private static int Best(List<Candidate> cands)
        {
            int best = -1;
            for (int i = 0; i < cands.Count; i++)
            {
                if (!cands[i].Eligible) continue;
                if (best < 0 || cands[i].Score > cands[best].Score
                    || (cands[i].Score == cands[best].Score && cands[i].ObjectiveId < cands[best].ObjectiveId))
                    best = i;
            }
            return best;
        }

        private static int BestOf(List<Candidate> cands, int objectiveId)
        {
            int best = -1;
            if (objectiveId < 0) return best;
            for (int i = 0; i < cands.Count; i++)
                if (cands[i].Eligible && cands[i].ObjectiveId == objectiveId && (best < 0 || cands[i].Score > cands[best].Score))
                    best = i;
            return best;
        }

        // Softmax over the eligible candidates within Epsilon of the best (P5).
        private static int Draw(Inputs c, List<Candidate> cands, int best, PlannerState state)
        {
            float top = cands[best].Score;
            double total = 0;
            int count = 0;
            for (int i = 0; i < cands.Count; i++)
            {
                if (!cands[i].Eligible || cands[i].Score < top - Epsilon) continue;
                total += Math.Exp((cands[i].Score - top) / Temperature);
                count++;
            }
            if (count <= 1) return best;

            long seed = state.CampaignSeed ^ ((long)c.S.Alliance * 7919L) ^ state.DecisionSerial;
            var rng = new Random(unchecked((int)(seed ^ (seed >> 32))));
            double pick = rng.NextDouble() * total;
            int last = best;
            for (int i = 0; i < cands.Count; i++)
            {
                if (!cands[i].Eligible || cands[i].Score < top - Epsilon) continue;
                last = i;
                pick -= Math.Exp((cands[i].Score - top) / Temperature);
                if (pick <= 0) return i;
            }
            return last;
        }

        private static void ResetChallenger(PlannerState state)
        {
            state.ChallengerObjective = -1;
            state.ChallengerDays = 0;
        }

        private static bool ObjectiveAccomplished(CampaignSnapshot s, int objectiveId)
        {
            for (int i = 0; i < s.Objectives.Count; i++)
                if (s.Objectives[i].ObjectiveId == objectiveId && s.Objectives[i].Accomplished) return true;
            return false;
        }

        private static bool Offered(List<Candidate> cands, int objectiveId)
        {
            for (int i = 0; i < cands.Count; i++)
                if (cands[i].ObjectiveId == objectiveId) return true;
            return false;
        }

        // ---- postures ----

        private static void AssignPostures(Inputs c, TheaterPlan plan, float axisFromX, float axisFromZ, float mainX, float mainZ)
        {
            var s = c.S;
            int n = c.Sectors.Count;
            if (n == 0) return;
            float range = s.RelocationRange;
            float mass = s.Tempo != null ? s.Tempo.MassRatioModifier : 0f;

            var x = c.Facts;
            var own = x.Own;
            var count = x.Count;
            var supply = x.Supply;
            var morale = x.Morale;
            var orderable = x.Orderable;
            var strongest = x.Strongest;

            for (int sector = 0; sector < n; sector++)
            {
                float cx = x.CX[sector], cz = x.CZ[sector];
                bool hasOwn = count[sector] > 0;
                float threat = x.Threat[sector];
                TheaterPosture posture;
                if (sector == plan.MainSectorId) posture = TheaterPosture.MainEffort;
                // Recover is decided by the formations the planner can order: the player's supply and morale
                // (his own command's, the map-group player's) cannot send a sector to Recover, though his
                // strength still counts toward its hold (G4 verify r4 V2).
                else if (orderable[sector] > 0 && (supply[sector] / orderable[sector] < RecoverSupplyBelow || morale[sector] / orderable[sector] < s.MinMoraleForMovement))
                    posture = TheaterPosture.Recover;
                else if (threat > 0f) posture = hasOwn && own[sector] >= x.Need[sector] ? TheaterPosture.Hold : TheaterPosture.Screen;
                else if (!hasOwn) continue;
                else if (plan.MainObjectiveId >= 0 && SegmentDistance(cx, cz, axisFromX, axisFromZ, mainX, mainZ) <= range)
                    posture = TheaterPosture.Support;
                else posture = TheaterPosture.EconomyOfForce;

                bool followsMain = posture == TheaterPosture.MainEffort || posture == TheaterPosture.Support;
                plan.Sectors.Add(new SectorPlan
                {
                    SectorId = sector,
                    Theater = c.Sectors.TheaterOf(sector),
                    Posture = posture,
                    TargetObjectiveId = followsMain ? plan.MainObjectiveId : -1,
                    TargetX = followsMain ? mainX : cx,
                    TargetZ = followsMain ? mainZ : cz,
                    RequiredRatio = posture == TheaterPosture.MainEffort ? plan.MainRequiredRatio
                        : Math.Max(0f, s.OffensiveDominance * (1f + mass) + ProfileOf(c, strongest[sector]).RequiredOddsShift),
                    HoldNeed = x.Need[sector]
                });
            }
        }

        // ---- terms ----

        internal static bool Movable(in FormationRecord f)
        {
            return (f.Flags & FormationFlags.FightingForce) != 0 && (f.Flags & Busy) == 0
                && (WlChainRules.MayMove(f.Chain) || WlChainRules.OrderOnly(f.Chain));
        }

        // A formation G4 may concentrate (see the header).
        internal static bool MassEligible(in FormationRecord f, float minMorale)
        {
            return Movable(f) && WlChainRules.MayMove(f.Chain)
                && f.Supply01 >= RecoverSupplyBelow && f.Morale >= minMorale;
        }

        // The massable set for a target in targetSector (indexes into s.Own, written to `into`) and
        // its strength.
        internal static float MassFor(Inputs c, float tx, float tz, int targetSector, List<int> into)
        {
            var s = c.S;
            into.Clear();
            float total = 0f;
            for (int i = 0; i < s.Own.Count; i++)
            {
                var f = s.Own[i];
                if (!MassEligible(f, s.MinMoraleForMovement) || c.CapitalReserved[i]) continue;
                int holds = c.HoldReserved[i];
                if (holds >= 0 && holds != targetSector && !HoldThreatAtTarget(c, holds, tx, tz)) continue;
                if (!WithinHorizon(c, i, tx, tz)) continue;
                into.Add(i);
                total += Math.Max(0f, f.Strength);
            }
            return total;
        }

        // A sector's hold is released for a target that is the very army the hold faces (its nearest
        // believed army within relocation range of the target): attacking it is holding against it.
        private static bool HoldThreatAtTarget(Inputs c, int sector, float tx, float tz)
        {
            return RiverLines.Distance(c.Facts.NearX[sector], c.Facts.NearZ[sector], tx, tz) <= c.S.RelocationRange;
        }

        // March time within MarchHorizonDays: the road rate first, the rail leg only when the road
        // alone is too slow (MarchTime.EstimateDays on the straight distance; no river questions).
        private static bool WithinHorizon(Inputs c, int i, float tx, float tz)
        {
            var f = c.S.Own[i];
            float road = c.RoadPerDay[i];
            if (road <= 0f) return false;
            float distance = RiverLines.Distance(f.X, f.Z, tx, tz);
            if (distance / road <= MarchHorizonDays) return true;
            if (!f.UseRails) return false;
            return MarchTime.EstimateDays(c.Speed, f, tx, tz, distance, c.S, null, c.OwnIips) <= MarchHorizonDays;
        }

        // The nearest member of the massable set with at least MainBodyShare of its largest strength.
        private static int PickForce(CampaignSnapshot s, List<int> mass, float x, float z)
        {
            float largest = 0f;
            for (int k = 0; k < mass.Count; k++) largest = Math.Max(largest, s.Own[mass[k]].Strength);
            int best = -1;
            float bestDistance = float.MaxValue;
            for (int k = 0; k < mass.Count; k++)
            {
                var f = s.Own[mass[k]];
                if (f.Strength < MainBodyShare * largest) continue;
                float d = DistanceSq(f.X, f.Z, x, z);
                if (d >= bestDistance) continue;
                bestDistance = d;
                best = mass[k];
            }
            return best;
        }

        // Per sector: centroid, radius, own strength / supply / morale, river-weighted believed land
        // threat (the one threat function: G4's hold need reads SectorPlan.HoldNeed, F9), hold need,
        // and the believed army nearest the centroid (the hold reserve's ordering point).
        private static SectorFacts BuildSectorFacts(Inputs c)
        {
            var s = c.S;
            int n = c.Sectors.Count;
            var x = new SectorFacts
            {
                CX = new float[n], CZ = new float[n], Radius = new float[n], Threat = new float[n], Need = new float[n],
                Own = new float[n], Supply = new float[n], Morale = new float[n], NearX = new float[n], NearZ = new float[n],
                Count = new int[n], Strongest = new int[n], Orderable = new int[n]
            };
            float holdRatio = HoldRatio + (s.Tempo != null ? s.Tempo.MinimumHoldRatioModifier : 0f);
            for (int k = 0; k < n; k++)
            {
                x.Strongest[k] = -1;
                c.Sectors.Centroid(k, out x.CX[k], out x.CZ[k]);
            }
            for (int i = 0; i < s.Own.Count; i++)
            {
                var f = s.Own[i];
                // The sector this formation counts in, for every fact below: where it stands, except the
                // map-group player, who counts where his last waypoint lies (ReserveRules.HoldSectorOf),
                // so Own and Count agree on whether a sector has a holder and a player bound for an empty
                // threatened sector makes it Hold. Supply and Morale are averaged over the formations the
                // planner can order only.
                int sector = ReserveRules.HoldSectorOf(c.Sectors, f);
                if (sector < 0 || sector >= n) continue;
                if (WlChainRules.CountsTowardCoverage(f.Chain)) x.Own[sector] += ReserveRules.HoldStrength(f);
                x.Count[sector]++;
                if (WlChainRules.MayMove(f.Chain))
                {
                    x.Orderable[sector]++;
                    x.Supply[sector] += f.Supply01;
                    x.Morale[sector] += f.Morale;
                }
                if (x.Strongest[sector] < 0 || f.Strength > s.Own[x.Strongest[sector]].Strength) x.Strongest[sector] = i;
            }
            for (int i = 0; i < s.Areas.Count; i++)
            {
                int sector = c.Sectors.SectorOfArea(s.Areas[i].Index);
                if (sector < 0 || sector >= n) continue;
                x.Radius[sector] = Math.Max(x.Radius[sector], RiverLines.Distance(x.CX[sector], x.CZ[sector], s.Areas[i].X, s.Areas[i].Z));
            }
            for (int k = 0; k < n; k++)
            {
                c.Terrain?.Mark(ReviewTerrain.GroupSector, k, x.CX[k], x.CZ[k]);
                x.Threat[k] = SectorThreat(c, x.CX[k], x.CZ[k], x.Radius[k]);
                x.Need[k] = holdRatio * x.Threat[k];
                int near = NearestLandBelief(s, x.CX[k], x.CZ[k]);
                x.NearX[k] = near >= 0 ? s.Beliefs[near].X : x.CX[k];
                x.NearZ[k] = near >= 0 ? s.Beliefs[near].Z : x.CZ[k];
            }
            return x;
        }

        // The river-weighted believed land threat on a sector (centroid, radius): the threat its
        // Hold/Screen posture and its hold need are judged on.
        internal static float SectorThreat(Inputs c, float cx, float cz, float sectorRadius)
        {
            return LandThreatNear(c, cx, cz, sectorRadius + c.S.RelocationRange);
        }

        // G4's own pick rules, applied before massing (ReserveRules, shared with ForceAllocator): the
        // capital reserve (the massable formations nearest the capital until CapitalFloorStrength),
        // then each threatened sector's hold reserve (what holds there anyway, busy, not massable or in
        // the capital reserve, then its massable formations nearest the sector's believed army until
        // HoldNeed). A sector's hold is released for a target its own threat sits at (HoldThreatAtTarget).
        private static void Reserve(Inputs c)
        {
            int n = c.S.Own.Count;
            c.CapitalReserved = new bool[n];
            c.HoldReserved = new int[n];
            ReserveRules.Capital(c.S, c.CapitalFloor, c.CapitalReserved);
            ReserveRules.Hold(c.S, c.Sectors, c.Facts.Need, c.Facts.NearX, c.Facts.NearZ, c.CapitalReserved, c.HoldReserved);
        }

        private static CommanderProfile ProfileOf(Inputs c, int formation)
        {
            if (formation < 0 || c.Profile == null) return default(CommanderProfile);
            return c.Profile(c.S.Own[formation].CommanderId);
        }

        // Believed land enemy within radius (straight), each weighted min(1, radius / river distance).
        internal static float LandThreatNear(Inputs c, float x, float z, float radius)
        {
            var beliefs = c.S.Beliefs;
            float total = 0f;
            for (int i = 0; i < beliefs.Count; i++)
            {
                var b = beliefs[i];
                float live = Live(b);
                if (live <= 0f || Afloat(b) || RiverLines.Distance(b.X, b.Z, x, z) > radius) continue;
                float effective = RiverLines.EffectiveDistance(b.X, b.Z, x, z, c.Probe, c.S.Crossings);
                total += live * (effective > radius ? radius / effective : 1f);
            }
            return total;
        }

        // Land and afloat beliefs within half the relocation range of the force -> target segment and
        // outside the target's own radius (those are the target's balance).
        private static float FlankThreat(Inputs c, float fx, float fz, float tx, float tz, float range)
        {
            var beliefs = c.S.Beliefs;
            float band = 0.5f * range;
            float total = 0f;
            for (int i = 0; i < beliefs.Count; i++)
            {
                var b = beliefs[i];
                float live = Live(b);
                if (live <= 0f || RiverLines.Distance(b.X, b.Z, tx, tz) <= range) continue;
                ClosestOnSegment(b.X, b.Z, fx, fz, tx, tz, out float px, out float pz);
                float straight = RiverLines.Distance(b.X, b.Z, px, pz);
                if (straight > band) continue;
                float effective = Afloat(b) ? straight : RiverLines.EffectiveDistance(b.X, b.Z, px, pz, c.Probe, c.S.Crossings);
                total += live * (effective > band ? band / effective : 1f);
            }
            return total;
        }

        // P7 floor and the capital threat from one computation (see the header).
        private static void CapitalThreat(Inputs c, out float floor, out float threat01)
        {
            floor = 0f;
            threat01 = 0f;
            var s = c.S;
            if (!s.HasCapital) return;
            float range = s.RelocationRange;
            float weighted = 0f;
            for (int i = 0; i < s.Beliefs.Count; i++)
            {
                var b = s.Beliefs[i];
                float live = Live(b);
                float straight = RiverLines.Distance(b.X, b.Z, s.CapitalX, s.CapitalZ);
                if (live <= 0f || straight >= range) continue;
                float d = Afloat(b) ? straight : RiverLines.EffectiveDistance(b.X, b.Z, s.CapitalX, s.CapitalZ, c.Probe, s.Crossings);
                weighted += live / (float)Math.Pow(Math.Max(VanillaThreatMinDistance, d), VanillaThreatExponent) * VanillaThreatScale;
            }
            floor = CapitalFloorRatio * weighted;
            if (floor <= 0f) return;
            float own = 0f;
            for (int i = 0; i < s.Own.Count; i++)
            {
                var f = s.Own[i];
                if (!WlChainRules.CountsTowardCoverage(f.Chain)) continue;
                ReserveRules.HoldPoint(f, out float hx, out float hz); // vanilla reads GetLastWaypointPos (11733-11740)
                if (RiverLines.Distance(hx, hz, s.CapitalX, s.CapitalZ) >= range) continue;
                own += Math.Max(0f, Math.Min(f.Strength, f.Strength * f.Morale));
            }
            threat01 = SnapshotRules.Clamp01(floor / Math.Max(1f, own));
        }

        private static float CapitalTerm(Inputs c, in ObjectiveRecord o)
        {
            if ((o.Roles & ObjectiveRole.Capital) != 0) return 1f;
            var s = c.S;
            if (!s.HasCapital || c.CapitalThreat01 <= 0f || s.RelocationRange <= 0f) return 0f;
            float closeness = SnapshotRules.Clamp01(1f - RiverLines.Distance(o.X, o.Z, s.CapitalX, s.CapitalZ) / (2f * s.RelocationRange));
            return c.CapitalThreat01 * closeness;
        }

        private static float Role01(ObjectiveRole roles)
        {
            if ((roles & ObjectiveRole.Capital) != 0) return 1f;
            int count = 0;
            if ((roles & ObjectiveRole.RailHub) != 0) count++;
            if ((roles & ObjectiveRole.Port) != 0) count++;
            if ((roles & ObjectiveRole.Industry) != 0) count++;
            return Math.Min(1f, 0.5f * count);
        }

        private static void ClosestOnSegment(float x, float z, float ax, float az, float bx, float bz, out float px, out float pz)
        {
            float vx = bx - ax, vz = bz - az;
            float lengthSq = vx * vx + vz * vz;
            float t = lengthSq > 0f ? ((x - ax) * vx + (z - az) * vz) / lengthSq : 0f;
            t = Math.Max(0f, Math.Min(1f, t));
            px = ax + vx * t;
            pz = az + vz * t;
        }

        private static float SegmentDistance(float x, float z, float ax, float az, float bx, float bz)
        {
            ClosestOnSegment(x, z, ax, az, bx, bz, out float px, out float pz);
            return RiverLines.Distance(x, z, px, pz);
        }

        // ---- telemetry ----

        // The [CampaignPlan] row (plan G3 Steps): changes only when the decision does.
        internal static string Summary(TheaterPlan plan)
        {
            int support = 0, hold = 0, eof = 0, screen = 0, recover = 0, main = 0;
            Theater mainTheater = Theater.Unknown;
            for (int i = 0; i < plan.Sectors.Count; i++)
            {
                switch (plan.Sectors[i].Posture)
                {
                    case TheaterPosture.MainEffort: main++; mainTheater = plan.Sectors[i].Theater; break;
                    case TheaterPosture.Support: support++; break;
                    case TheaterPosture.Hold: hold++; break;
                    case TheaterPosture.EconomyOfForce: eof++; break;
                    case TheaterPosture.Screen: screen++; break;
                    case TheaterPosture.Recover: recover++; break;
                }
            }
            return "[CampaignPlan] alliance=" + plan.Alliance
                + " main=" + (plan.MainObjectiveId >= 0 ? mainTheater + "/" + plan.MainObjectiveId : "none")
                + " ratio=" + plan.MainRequiredRatio.ToString("0.00", CultureInfo.InvariantCulture)
                + " sectors=main:" + main + ",support:" + support + ",hold:" + hold + ",eof:" + eof + ",screen:" + screen + ",recover:" + recover
                + " switch=" + (plan.SwitchReason ?? "none")
                + " prior=" + (plan.PriorId ?? "none")
                + " depots=" + plan.DepotTasks.Count;
        }

        private static string Trace(Inputs c, List<Candidate> cands, TheaterPlan plan)
        {
            var order = new List<int>(cands.Count);
            for (int i = 0; i < cands.Count; i++) order.Add(i);
            order.Sort((a, b) => cands[b].Score != cands[a].Score ? cands[b].Score.CompareTo(cands[a].Score) : a.CompareTo(b));
            var sb = new StringBuilder();
            sb.Append("alliance=").Append(plan.Alliance)
              .Append(" capitalThreat=").Append(c.CapitalThreat01.ToString("0.00", CultureInfo.InvariantCulture))
              .Append(" floor=").Append(plan.CapitalFloorStrength.ToString("0", CultureInfo.InvariantCulture))
              .Append(" candidates=").Append(cands.Count);
            for (int k = 0; k < order.Count && k < MaxTraceCandidates; k++)
            {
                var x = cands[order[k]];
                sb.Append(" | ").Append(x.ObjectiveId).Append('@').Append(x.SectorId)
                  .Append(" s=").Append(x.Score.ToString("0.000", CultureInfo.InvariantCulture))
                  .Append(" v=").Append(x.Value.ToString("0.00", CultureInfo.InvariantCulture))
                  .Append(" b=").Append(x.Balance.ToString("0.00", CultureInfo.InvariantCulture))
                  .Append(" r=").Append(x.Reach.ToString("0.00", CultureInfo.InvariantCulture))
                  .Append(" m=").Append(x.March.ToString("0.00", CultureInfo.InvariantCulture))
                  .Append(" e=").Append(x.Exposure.ToString("0.00", CultureInfo.InvariantCulture))
                  .Append(" c=").Append(x.Capital.ToString("0.00", CultureInfo.InvariantCulture))
                  .Append(" p=").Append(x.PriorTerm.ToString("0.00", CultureInfo.InvariantCulture))
                  .Append(" odds=").Append(x.Ratio >= float.MaxValue ? "inf" : x.Ratio.ToString("0.00", CultureInfo.InvariantCulture))
                  .Append('/').Append(x.Required.ToString("0.00", CultureInfo.InvariantCulture))
                  .Append(" mass=").Append(x.Mass.ToString("0", CultureInfo.InvariantCulture))
                  .Append(x.Retained ? " retained" : x.Eligible ? "" : " ineligible");
            }
            return sb.ToString();
        }
    }
}
