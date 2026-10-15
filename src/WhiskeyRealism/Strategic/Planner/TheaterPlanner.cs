using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

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
    //   balance  own strength near the target (all own formations within relocation range, plus the
    //            candidate force) / (own + believed land enemy near it), scaled by the force
    //            commander's AttackBias (x (1 + 0.5 x bias)).
    //   reach    LogisticsGate.Evaluate for the candidate force with its commander's SupplySlack
    //            (vanilla's per-unit supply rule 114272-114306): 0 out of reach, else 0.5 + 0.5 x slack.
    //   march    1 - the candidate force's march-time estimate (MarchTime, G0 landing B) / 30 days.
    //   exposure 1 - believed enemy near the axis flank (within half the relocation range of the
    //            force -> target segment, outside the target's own radius) / (that + force strength).
    //   capital  own capital threat x closeness of the target to the own capital, or 1 for an
    //            enemy capital.
    //   + PriorLambda x HistoricalPriorTable.PriorFor (<= PriorMaxContribution).
    // The candidate force is the nearest movable fighting formation of at least MainBodyShare of the
    // largest one (movable: not in battle, retreating, raiding, invading, afloat, building, in
    // transfer, and a chain class Whiskey moves or orders, WlChainRules.MayMove/OrderOnly).
    // Eligible for the main effort only when own / believed enemy near the target reaches
    // RequiredRatio = aidominanceforoffensiveoperations x (1 + Tempo.MassRatioModifier) +
    // CommanderProfile.RequiredOddsShift (vanilla's own offensive dominance, 14249-14250, C17).
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
    // relocation range), threat = floor / own strength x morale within relocation range (vanilla
    // weighs its own side unweighted, 11744-11749), clamped to [0, 1].
    //
    // Hysteresis (P4): the main effort switches when a challenger beats it by more than SwitchMargin
    // on SwitchDays consecutive reviews, or at once on a trigger: capital threat >= CapitalThreatTrigger,
    // the current objective accomplished / no longer offered / below its required odds, or a
    // decisive battle in its theater or near it (CicReviewRouter.BattleInvalidatesPlan, the R4 rule).
    // Randomness (P5) only at a decision: softmax (Temperature) over the candidates within Epsilon of
    // the best, System.Random seeded with CampaignSeed ^ alliance x 7919 ^ DecisionSerial.
    //
    // Postures: MainEffort the main target's sector; then, for sectors with own formations or a
    // believed land threat: Recover (own formations average raw Supply01 < 0.4 or morale below
    // aiminimummoraleformovement), Hold (believed threat and own >= hold need = (HoldRatio +
    // Tempo.MinimumHoldRatioModifier) x threat), Screen (believed threat above what own can hold,
    // or no own formations), Support (centroid within relocation range of the main axis),
    // EconomyOfForce (own formations, no threat).
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
        public const int MaxTraceCandidates = 12;
        private const float VanillaThreatMinDistance = 10f, VanillaThreatExponent = 0.3f, VanillaThreatScale = 0.5f; // 11735
        private const FormationFlags Busy = FormationFlags.InBattle | FormationFlags.OnRetreat | FormationFlags.RaidForce | FormationFlags.SeaInvasion
            | FormationFlags.Embarked | FormationFlags.FortBuilder | FormationFlags.VanillaDepotBuilder | FormationFlags.InTransfer;

        internal struct Candidate
        {
            public int ObjectiveId;
            public float X, Z;
            public int SectorId;
            public int Force;          // own formation index, -1 none
            public float Value, Balance, Reach, March, Exposure, Capital, PriorTerm, Score;
            public float Ratio, Required;
            public bool Eligible, InReach;
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
        }

        internal static TheaterPlan Plan(CampaignSnapshot s, SectorModel sectors, Func<int, CommanderProfile> sectorProfile, PlannerState state,
                                         IReadOnlyList<BattleHistoryRecord> battles, ITerrainLineProbe probe, in MarchSpeedInputs speed, out string trace)
        {
            var plan = new TheaterPlan { Alliance = s != null ? s.Alliance : -1, DaySerial = s != null ? s.DaySerial : 0 };
            trace = "";
            if (s == null || sectors == null || s.Alliance < 0 || s.Alliance > 1) return plan;
            if (state == null) state = new PlannerState();

            var inputs = new Inputs { S = s, Sectors = sectors, Profile = sectorProfile, Probe = probe, Speed = speed };
            CapitalThreat(inputs, out inputs.CapitalFloor, out inputs.CapitalThreat01);
            plan.CapitalFloorStrength = inputs.CapitalFloor;

            var candidates = new List<Candidate>();
            Evaluate(inputs, candidates);

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
                    LogisticsGate.AddDepotTasks(s, plan, sectors, force.X, force.Z, m.X, m.Z, force.CommandRange, profile.SupplySlack);
                }
            }
            AssignPostures(inputs, plan, axisFromX, axisFromZ, mainX, mainZ);
            trace = Trace(inputs, candidates, plan);
            return plan;
        }

        // Scores every candidate target (see the header).
        internal static void Evaluate(Inputs c, List<Candidate> into)
        {
            into.Clear();
            var s = c.S;
            float range = s.RelocationRange;
            float mass = s.Tempo != null ? s.Tempo.MassRatioModifier : 0f;
            float maxAreaImportance = 0f;
            for (int i = 0; i < s.Areas.Count; i++) maxAreaImportance = Math.Max(maxAreaImportance, s.Areas[i].Importance);
            float maxMovable = 0f;
            for (int i = 0; i < s.Own.Count; i++)
                if (Movable(s.Own[i])) maxMovable = Math.Max(maxMovable, s.Own[i].Strength);

            for (int i = 0; i < s.Objectives.Count; i++)
            {
                var o = s.Objectives[i];
                if (!o.Available || o.Accomplished || o.HolderAlliance == s.Alliance) continue;
                var cand = new Candidate { ObjectiveId = o.ObjectiveId, X = o.X, Z = o.Z, SectorId = c.Sectors.SectorAt(o.X, o.Z) };
                cand.Force = PickForce(s, o.X, o.Z, maxMovable);
                var profile = ProfileOf(c, cand.Force);
                cand.Required = Math.Max(0f, s.OffensiveDominance * (1f + mass) + profile.RequiredOddsShift);

                float enemy = LandThreatNear(c, o.X, o.Z, range);
                float own = OwnStrengthWithin(s, o.X, o.Z, range);
                if (cand.Force >= 0 && RiverLines.Distance(s.Own[cand.Force].X, s.Own[cand.Force].Z, o.X, o.Z) > range)
                    own += Math.Max(0f, s.Own[cand.Force].Strength);
                cand.Ratio = enemy > 0f ? own / enemy : (own > 0f ? float.MaxValue : 0f);
                float balance = own + enemy > 0f ? own / (own + enemy) : 0f;
                cand.Balance = SnapshotRules.Clamp01(balance * (1f + AttackBiasBalanceScale * profile.AttackBias));
                cand.Value = SnapshotRules.Clamp01(0.5f * o.Importance01 + 0.25f * AreaImportance01(s, o.X, o.Z, maxAreaImportance) + 0.25f * Role01(o.Roles));
                cand.Capital = CapitalTerm(c, o);

                if (cand.Force >= 0)
                {
                    var f = s.Own[cand.Force];
                    var reach = LogisticsGate.Evaluate(s, o.X, o.Z, f.CommandRange, profile.SupplySlack);
                    cand.InReach = reach.InReach;
                    cand.Reach = reach.InReach ? 0.5f + 0.5f * reach.Slack01 : 0f;
                    float distance = RiverLines.EffectiveDistance(f.X, f.Z, o.X, o.Z, c.Probe, s.Crossings);
                    float days = MarchTime.EstimateDays(c.Speed, f, o.X, o.Z, distance, s, c.Probe);
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
        }

        // ---- decision (P4, P5) ----

        private static int Decide(Inputs c, List<Candidate> cands, PlannerState state, IReadOnlyList<BattleHistoryRecord> battles, out string reason)
        {
            int best = Best(cands);
            int current = state.CurrentMainObjective;
            int held = BestOf(cands, current);

            if (best < 0)
            {
                state.CurrentMainObjective = -1;
                state.CurrentMainSector = -1;
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
            else if (Trigger(c, cands, current, held, battles, out string trigger))
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
                ResetChallenger(state);
            }
            state.CurrentMainSector = cands[chosen].SectorId;
            return chosen;
        }

        // A trigger switches at once (P4). `held` is the current objective's best eligible target, -1
        // when it is accomplished, no longer offered or below its required odds.
        private static bool Trigger(Inputs c, List<Candidate> cands, int current, int held, IReadOnlyList<BattleHistoryRecord> battles, out string reason)
        {
            reason = null;
            if (held < 0)
            {
                reason = ObjectiveAccomplished(c.S, current) ? "accomplished" : Offered(cands, current) ? "odds" : "unavailable";
                return true;
            }
            if (c.CapitalThreat01 >= CapitalThreatTrigger)
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
            float holdRatio = HoldRatio + (s.Tempo != null ? s.Tempo.MinimumHoldRatioModifier : 0f);

            var own = new float[n];
            var count = new int[n];
            var supply = new float[n];
            var morale = new float[n];
            var strongest = new int[n];
            for (int k = 0; k < n; k++) strongest[k] = -1;
            for (int i = 0; i < s.Own.Count; i++)
            {
                var f = s.Own[i];
                int sector = SectorOf(c.Sectors, f);
                if (sector < 0 || sector >= n) continue;
                own[sector] += Math.Max(0f, f.Strength);
                count[sector]++;
                supply[sector] += f.Supply01;
                morale[sector] += f.Morale;
                if (strongest[sector] < 0 || f.Strength > s.Own[strongest[sector]].Strength) strongest[sector] = i;
            }

            var radius = new float[n];
            for (int i = 0; i < s.Areas.Count; i++)
            {
                int sector = c.Sectors.SectorOfArea(s.Areas[i].Index);
                if (sector < 0 || sector >= n) continue;
                c.Sectors.Centroid(sector, out float cx, out float cz);
                radius[sector] = Math.Max(radius[sector], RiverLines.Distance(cx, cz, s.Areas[i].X, s.Areas[i].Z));
            }

            for (int sector = 0; sector < n; sector++)
            {
                c.Sectors.Centroid(sector, out float cx, out float cz);
                bool hasOwn = count[sector] > 0;
                float threat = LandThreatNear(c, cx, cz, radius[sector] + range);
                TheaterPosture posture;
                if (sector == plan.MainSectorId) posture = TheaterPosture.MainEffort;
                else if (hasOwn && (supply[sector] / count[sector] < RecoverSupplyBelow || morale[sector] / count[sector] < s.MinMoraleForMovement))
                    posture = TheaterPosture.Recover;
                else if (threat > 0f) posture = hasOwn && own[sector] >= holdRatio * threat ? TheaterPosture.Hold : TheaterPosture.Screen;
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
                        : Math.Max(0f, s.OffensiveDominance * (1f + mass) + ProfileOf(c, strongest[sector]).RequiredOddsShift)
                });
            }
        }

        private static int SectorOf(SectorModel sectors, in FormationRecord f)
        {
            int sector = f.AreaIndex >= 0 ? sectors.SectorOfArea(f.AreaIndex) : -1;
            return sector >= 0 ? sector : sectors.SectorAt(f.X, f.Z);
        }

        // ---- terms ----

        internal static bool Movable(in FormationRecord f)
        {
            return (f.Flags & FormationFlags.FightingForce) != 0 && (f.Flags & Busy) == 0
                && (WlChainRules.MayMove(f.Chain) || WlChainRules.OrderOnly(f.Chain));
        }

        private static int PickForce(CampaignSnapshot s, float x, float z, float maxMovable)
        {
            int best = -1;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < s.Own.Count; i++)
            {
                var f = s.Own[i];
                if (!Movable(f) || f.Strength < MainBodyShare * maxMovable) continue;
                float d = RiverLines.Distance(f.X, f.Z, x, z);
                if (d >= bestDistance) continue;
                bestDistance = d;
                best = i;
            }
            return best;
        }

        private static CommanderProfile ProfileOf(Inputs c, int formation)
        {
            if (formation < 0 || c.Profile == null) return default(CommanderProfile);
            return c.Profile(c.S.Own[formation].CommanderId);
        }

        private static float OwnStrengthWithin(CampaignSnapshot s, float x, float z, float range)
        {
            float total = 0f;
            for (int i = 0; i < s.Own.Count; i++)
                if (RiverLines.Distance(s.Own[i].X, s.Own[i].Z, x, z) <= range) total += Math.Max(0f, s.Own[i].Strength);
            return total;
        }

        private static float Live(in EnemyBelief b)
        {
            return b.Confidence01 > 0f ? Math.Max(0f, b.Strength) * Math.Min(1f, b.Confidence01) : 0f;
        }

        private static bool Afloat(in EnemyBelief b)
        {
            return (b.Traits & EnemyTraits.Embarked) != 0;
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
                if (RiverLines.Distance(f.X, f.Z, s.CapitalX, s.CapitalZ) >= range) continue;
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

        private static float AreaImportance01(CampaignSnapshot s, float x, float z, float maxAreaImportance)
        {
            if (maxAreaImportance <= 0f) return 0f;
            int position = AreaGeometry.AreaAt(s.Areas, x, z);
            return position >= 0 ? SnapshotRules.Clamp01(s.Areas[position].Importance / maxAreaImportance) : 0f;
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
                  .Append(x.Eligible ? "" : " ineligible");
            }
            return sb.ToString();
        }
    }
}
