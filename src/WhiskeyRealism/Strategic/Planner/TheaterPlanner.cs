using System;
using WhiskeyRealism.Telemetry;
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
    // (PlanTriggers.BattleInvalidatesPlan). A trigger keeps the current main effort
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
        public const float SameTargetRadius = 1f;          // two target records closer than this are one target (the latch's identity)
        public const float CapitalThreatRelease = 0.4f;   // the latch re-arms below this (F2)
        public const int MaxTraceCandidates = 12;
        // Vanilla refuses to march a formation of readiness step 0 or 1 onto enemy ground: SetWaypoint returns without a waypoint and
        // without a message for the AI (91336-91355: readiness step <= 1, target on enemy soil outside regroupment range), and the
        // AI's own offensive availability gate asks step >= 2 (IsUnitAvailableForOffensiveOperations 14091). The smoke of 2026-10-16
        // sent such formations anyway: 4 of the Union's 6 moves "failed", were retried and given up (the mover's P8 tracker), and
        // each give-up flipped the main effort to the other objective, whose mass still counted them.
        public const int MinReadinessStep = 2;
        // Scout first (smoke amendments 2026-10-16, D1/D2). A belief at or above this confidence is a fresh picture: ContactEvidenceLedger's
        // own bar for acting on a belief (a probe never escalates on less, ReconPlanner.ProbeStaleConfidence).
        public const float ScoutFreshConfidence = ContactEvidenceLedger.MinEscalationConfidence;
        // The presumed enemy at an unscouted place is the smallest force vanilla's AI moves at all (aiminimumstrengthformovement,
        // campaignprefs 1396-1397; TheaterPlan.MinGarrisonStrength), scaled up by the importance of the enemy-held ground nearby: a
        // more valuable objective is presumed better held, up to twice the floor.
        public const float PresumedImportanceGain = 1f;
        // The minimum strength when the scenario's read is not positive or not finite (Config/campaignprefs.txt 1397): the one place the
        // number 5000 stands for it. TheaterPlan.MinGarrisonStrength is the definition site everything else derives from.
        internal const float MinStrengthFallback = 5000f;
        // D3: the mass is the force the objective needs: the estimated enemy x the required ratio x (1 + margin). The margin is
        // MassMarginBase, shrunk by a bold commander (AttackBias, as the balance term scales by it) and grown for a cautious one.
        public const float MassMarginBase = 0.25f, MassMarginBiasScale = 0.5f;
        // D6: a formation from outside the main effort's theater joins only if it can be at the target within this many days
        // (MarchTime.EstimateDays, which credits a rail leg where userails and a train allow it): a reinforcement, not a gather.
        public const float ReinforcementMaxDays = 10f;
        // D8 (smoke amendments 2026-10-16): the CSA's value for a live secession objective (Louisville, St.Louis: the Union takes a national
        // morale hit, the CSA a state at secedecsasupport) and for the other targets in that state (clearing the Union presence out of it, which
        // is what the objective's accomplishment needs). Added to the target's value term (weight 0.30), so 0.25 is worth 0.075 of a score:
        // under the 0.12 switch margin and near the history prior's cap, a weight and never a must.
        public const float SecessionValueBonus = 0.25f, SecessionStateBonus = 0.10f;
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
            public float Ratio, Required, Mass, Enemy, Presumed, Need;
            public int Anchors;        // foresight F3: the undisplaced anchors holding men within the relocation range of the target
            public bool Eligible, InReach, Retained, Scouted;
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
            // Foresight F3: the enemy picture, built once a review before the first Evaluate (Evaluate builds it when null); the numbers the
            // capital threat leaves for the plan's [CapitalThreat] row; and the minimum strength every floor derives from (the scenario's live
            // minimum, set by Plan from PlanMinimum; the fallback for a hand-built input; MinStrength(Inputs) is the one reader).
            internal EnemyPicture Picture;
            internal float MinStrength = MinStrengthFallback;
            internal float CapitalWeighted, CapitalReach, CapitalReliefDays, CapitalCorpsPerDay;
            internal ReviewTerrain Terrain;   // marks the questions' order; null once scoring runs without rivers
            internal SectorFacts Facts;
            internal bool[] CapitalReserved;
            internal int[] HoldReserved;      // sector whose hold keeps the formation, -1 none
            internal float[] RoadPerDay;
            internal HashSet<int> OwnIips;
            internal readonly List<int> Scratch = new List<int>();
            // P8: moves the tracker gave up on, each with the objective it was issued for; MassFor leaves them out of that objective's mass.
            internal IReadOnlyList<GaveUpMove> GaveUp;
            // G5c: the builders the depot ledger holds (Marching and Wished tasks): MassFor counts none of them, and the capital reserve
            // skips a Wished one exactly as ForceAllocator does. Null: none.
            internal IReadOnlyList<HeldBuilder> Held;
            // D6: the decision's reinforcement lock (the plan's PlannerState); null in a bare Evaluate (no lock).
            internal PlannerState State;
            // D6 detail: each own formation's home department (PlannerState.Observe), by index into s.Own; null in a bare Evaluate, where
            // the theater of the sector a formation stands in stands in for it.
            internal Theater[] Department;
            // D8: the formations a secession garrison keeps (index into s.Own) and the posts they keep; chosen after the mass (PostFromOutside),
            // so MassFor never reads them.
            internal bool[] GarrisonReserved;
            internal readonly List<GarrisonPost> Posts = new List<GarrisonPost>();
            // The posts that still need formations from outside the state, chosen once the mass is known (PostFromOutside).
            internal readonly List<PostWork> Pending = new List<PostWork>();
            // MassFor's foreign pick of its last call (index into s.Own), -1 none; the capacity list holds natives and then this one.
            internal int Foreign = -1;
            internal readonly List<int> ForeignScratch = new List<int>();
        }

        // A garrison post that wants formations from outside the state: how many (1 when nobody is present, unlimited while the need is
        // unmet when only the flag shows presence) and whether the flag alone is the reason (a shortfall is then recorded).
        internal sealed class PostWork
        {
            internal GarrisonPost Post;
            internal int Limit;
            internal bool FlagAlone;
            internal bool Flag;          // vanilla's alliancetroops[0] is set: a garrison already stands in the state
            internal List<int> Inside;   // the own formations standing in the state, nearest the town first (indexes into s.Own)
        }

        // Per-sector facts shared by the reserves, the postures and the published HoldNeed.
        internal sealed class SectorFacts
        {
            public float[] CX, CZ, Radius, Threat, Need, Own, Supply, Morale, NearX, NearZ, Presumed;
            public float[] Assigned; // foresight F3: the Faded and Anchor mass EnemyPicture.AssignSectors gave each sector (weighted)
            public int[] Count, Strongest;
            public bool[] Facing;  // D2: the sector faces enemy-held ground or a believed army
            public int[] Orderable; // formations the planner can order (WlChainRules.MayMove): the base of the Supply/Morale averages
        }

        internal static TheaterPlan Plan(CampaignSnapshot s, SectorModel sectors, Func<int, CommanderProfile> sectorProfile, PlannerState state,
                                         IReadOnlyList<BattleHistoryRecord> battles, ITerrainLineSource terrain, in MarchSpeedInputs speed, out string trace,
                                         IReadOnlyList<GaveUpMove> gaveUp = null, IReadOnlyList<HeldBuilder> heldBuilders = null)
        {
            return Plan(s, sectors, sectorProfile, state, battles, terrain, in speed, out trace, out _, gaveUp, heldBuilders);
        }

        // The same, and the enemy picture the review was planned on (null when the plan was not built), for the [EnemyPicture] row.
        internal static TheaterPlan Plan(CampaignSnapshot s, SectorModel sectors, Func<int, CommanderProfile> sectorProfile, PlannerState state,
                                         IReadOnlyList<BattleHistoryRecord> battles, ITerrainLineSource terrain, in MarchSpeedInputs speed, out string trace,
                                         out EnemyPicture picture, IReadOnlyList<GaveUpMove> gaveUp = null, IReadOnlyList<HeldBuilder> heldBuilders = null)
        {
            var plan = new TheaterPlan { Alliance = s != null ? s.Alliance : -1, DaySerial = s != null ? s.DaySerial : 0 };
            trace = "";
            picture = null;
            if (s == null || sectors == null || s.Alliance < 0 || s.Alliance > 1) return plan;
            if (state == null) state = new PlannerState();

            var review = new ReviewTerrain(terrain, s.Areas);
            var inputs = new Inputs { S = s, Sectors = sectors, Profile = sectorProfile, Probe = review, Terrain = review, Speed = speed, GaveUp = gaveUp, Held = heldBuilders, State = state };
            plan.MinGarrisonStrength = inputs.MinStrength = PlanMinimum(s);
            AssignDepartments(inputs, state);
            // The picture is built once, before the first Evaluate: displacement is judged on straight distances and needs no terrain probe, so
            // both passes of a terrain-filling review plan on one picture.
            picture = inputs.Picture = EnemyPicture.Build(s, (x, z) => IsScouted(inputs, x, z));
            plan.DepartmentCounts = DepartmentCountsOf(inputs);
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
            plan.CapitalReach = inputs.CapitalReach;
            plan.CapitalWeighted = inputs.CapitalWeighted;
            plan.CapitalThreat01 = inputs.CapitalThreat01;
            plan.CapitalReliefDays = inputs.CapitalReliefDays;
            plan.CapitalCorpsPerDay = inputs.CapitalCorpsPerDay;
            NoteEnemyCapitalEstimates(inputs);

            int main = Decide(inputs, candidates, state, battles, out string reason);
            plan.SwitchReason = reason;
            float axisFromX = 0f, axisFromZ = 0f, mainX = 0f, mainZ = 0f;
            if (main >= 0)
            {
                var m = candidates[main];
                var force = s.Own[m.Force];
                var profile = ProfileOf(inputs, m.Force);
                plan.MainObjectiveId = m.ObjectiveId;
                plan.MainObjectiveX = m.X;
                plan.MainObjectiveZ = m.Z;
                plan.MainSectorId = m.SectorId;
                plan.MainRequiredRatio = m.Required;
                plan.PriorId = m.PriorId;
                plan.MainEnemyStrength = m.Enemy;
                // F1 (verify round 1): once a decision's target has been scouted it stays scouted for that decision, so a march released on
                // a fresh picture is not pulled back to a start line when the belief decays; a new decision starts unscouted.
                // Smoke 2026-10-17: the latch is for the scouted record. A retarget to another record of the same objective (same decision, switch=hold)
                // starts unscouted.
                plan.MainScoutLatched = !m.Scouted && state.ScoutedDecision == state.DecisionSerial && IsScoutedTarget(state, m.X, m.Z);
                if (m.Scouted)
                {
                    state.ScoutedDecision = state.DecisionSerial;
                    state.ScoutedTargetX = m.X;
                    state.ScoutedTargetZ = m.Z;
                }
                plan.MainScouted = m.Scouted || plan.MainScoutLatched;
                plan.MainStaged = !plan.MainScouted;
                // D3: the capacity the odds were judged on, then the part of it the objective needs.
                plan.MainCapacityStrength = MassFor(inputs, m.X, m.Z, m.SectorId, m.ObjectiveId, inputs.Scratch);
                plan.MainNeedStrength = m.Need;
                plan.MassStrength = SelectMass(inputs, inputs.Scratch, m.X, m.Z, m.Need, plan);
                if (plan.MainStaged) SetStartLine(inputs, plan, m.X, m.Z);
                NoteReinforcement(inputs, plan, state);
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
            PostFromOutside(inputs, plan);   // after the mass: the garrison takes only what the mass does not
            for (int k = 0; k < inputs.Posts.Count; k++) plan.Garrisons.Add(inputs.Posts[k]);
            AssignPostures(inputs, plan, axisFromX, axisFromZ, mainX, mainZ);
            trace = Trace(inputs, candidates, plan) + (unknown > 0 ? " terrain=filling unknown=" + unknown + " probed=" + probed : " terrain=complete");
            return plan;
        }

        // Whether (x, z) is the objective record the latch was recorded for, or none was recorded (a file written before the field: the
        // latch then speaks for the decision's target as it did).
        private static bool IsScoutedTarget(PlannerState state, float x, float z)
        {
            if (float.IsNaN(state.ScoutedTargetX) || float.IsNaN(state.ScoutedTargetZ)) return true;
            return Math.Abs(state.ScoutedTargetX - x) <= SameTargetRadius && Math.Abs(state.ScoutedTargetZ - z) <= SameTargetRadius;
        }

        // D1: an unscouted main effort assembles where an own formation already stands: the forward-most member of the selected mass
        // (nearest the objective, the lower instance id on a tie; the reinforcement from another theater only if it is the whole mass).
        // No formation is sent past a formation of its own side, and the point is inside the supply that member stands in.
        private static void SetStartLine(Inputs c, TheaterPlan plan, float tx, float tz)
        {
            int best = -1;
            float bestDistance = float.MaxValue;
            for (int pass = 0; pass < 2 && best < 0; pass++)
            {
                for (int k = 0; k < plan.MassInstanceIds.Count; k++)
                {
                    int i = c.S.IndexOfOwn(plan.MassInstanceIds[k]);
                    if (i < 0 || ((plan.MassInstanceIds[k] == plan.ReinforcementInstanceId) == (pass == 0))) continue;
                    var f = c.S.Own[i];
                    float d = DistanceSq(f.X, f.Z, tx, tz);
                    if (d > bestDistance || (d == bestDistance && best >= 0 && f.InstanceId > c.S.Own[best].InstanceId)) continue;
                    bestDistance = d;
                    best = i;
                }
            }
            if (best < 0) return;
            plan.StartLineX = c.S.Own[best].X;
            plan.StartLineZ = c.S.Own[best].Z;
        }

        // The capital threat, the sector facts, the reserves, then every candidate target scored
        // (see the header). Asks its terrain questions through c.Probe.
        internal static void Evaluate(Inputs c, List<Candidate> into)
        {
            into.Clear();
            var s = c.S;
            float range = s.RelocationRange;
            float mass = s.Tempo != null ? s.Tempo.MassRatioModifier : 0f;
            EnsurePicture(c);
            c.OwnIips = MarchTime.OwnIips(s);   // before CapitalThreat: its relief race estimates marches over the own IIPs
            c.Terrain?.Mark(ReviewTerrain.GroupCapital, 0, s.CapitalX, s.CapitalZ);
            CapitalThreat(c, out c.CapitalFloor, out c.CapitalThreat01);
            c.Facts = BuildSectorFacts(c);
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
                cand.Mass = MassFor(c, o.X, o.Z, cand.SectorId, o.ObjectiveId, c.Scratch);
                cand.Force = PickForce(s, c.Scratch, o.X, o.Z);
                var profile = ProfileOf(c, cand.Force);
                cand.Required = Math.Max(0f, s.OffensiveDominance * (1f + mass) + profile.RequiredOddsShift);

                // Scout first (D1): a target no fresh belief or own viewer speaks for is unscouted and its odds are judged on the
                // believed enemy or the presumed threat, whichever is larger, never on "none seen".
                cand.Scouted = IsScouted(c, o.X, o.Z);
                float believed = LandThreatNear(c, o.X, o.Z, range);
                float enemy;
                if (PlannerSettings.ForesightPicture)
                {
                    // D11: the layers are disjoint, so they add (the believed army and the men presumed at anchors and fading beliefs near the
                    // target), floored at the plain minimum raised by the importance of an undisplaced anchor there, scouted or not; the
                    // scouted flag still drives D1's staging.
                    cand.Presumed = c.Picture.Presumed(s, o.X, o.Z, range, c.Probe);
                    cand.Anchors = c.Picture.AnchorsNear(o.X, o.Z, range);
                    enemy = Math.Max(believed + cand.Presumed, Floor(c, o.X, o.Z, range));
                }
                else
                {
                    cand.Presumed = cand.Scouted ? 0f : PresumedAt(c, o.X, o.Z, range);
                    enemy = Math.Max(believed, cand.Presumed);
                }
                float own = cand.Mass;
                cand.Enemy = enemy;
                cand.Ratio = enemy > 0f ? own / enemy : (cand.Force >= 0 ? float.MaxValue : 0f);
                cand.Need = NeedFor(enemy, cand.Required, profile.AttackBias, MinStrength(c));
                // The balance term ranks targets by what is known (the believed enemy); the presumption gates the odds above and
                // sizes the push (Need), and being the same floor everywhere it would only blur the ranking.
                float balance = own + believed > 0f ? own / (own + believed) : 0f;
                cand.Balance = SnapshotRules.Clamp01(balance * (1f + AttackBiasBalanceScale * profile.AttackBias));
                float area01 = area >= 0 && maxAreaImportance > 0f ? SnapshotRules.Clamp01(s.Areas[area].Importance / maxAreaImportance) : 0f;
                cand.Value = SnapshotRules.Clamp01(0.5f * o.Importance01 + 0.25f * area01 + 0.25f * Role01(o.Roles) + SecessionValue(s, o));
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
                // D11: besides D3's odds rule, a capacity of at least the minimum strength vanilla moves at all (14099).
                cand.Eligible = cand.Force >= 0 && cand.Ratio >= cand.Required && cand.Mass >= MinStrength(c);
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
            Retain(cands, current, state, MinStrength(c));
            int best = Best(cands);
            // The record the main effort is on (hysteresis between records), and the best record of the current objective: the objective's
            // own value, which a challenger objective is compared against (the kept record may trail it by up to the record window).
            int held = HeldRecord(cands, current, state);
            int heldBest = BestOf(cands, current);

            if (best < 0)
            {
                state.CurrentMainObjective = -1;
                state.BelowOddsDays = 0;
                ResetChallenger(state);
                ResetRecordChallenger(state);
                state.CurrentTargetX = state.CurrentTargetZ = float.NaN;
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
                bool currentStillBest = heldBest >= 0 && cands[heldBest].Score >= cands[best].Score - Epsilon;
                chosen = currentStillBest ? held : Draw(c, cands, best, state);
                reason = cands[chosen].ObjectiveId != current ? trigger : "hold";
            }
            else if (cands[best].ObjectiveId != current && cands[best].Score - cands[heldBest].Score > SwitchMargin)
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
                ResetRecordChallenger(state);
            }
            state.CurrentTargetX = cands[chosen].X;
            state.CurrentTargetZ = cands[chosen].Z;
            return chosen;
        }

        // The odds deadband (F1): the current main effort keeps its eligibility while its odds are
        // within OddsDeadband below its RequiredRatio, for fewer than SwitchDays consecutive reviews.
        internal static void Retain(List<Candidate> cands, int current, PlannerState state, float minStrength)
        {
            // The record the main effort is on has the deadband, not whichever record of the objective scores best today.
            int index = KeptRecord(cands, current, state, anyEligibility: true);
            if (index < 0)
                for (int i = 0; i < cands.Count; i++)
                    if (cands[i].ObjectiveId == current && cands[i].Force >= 0 && (index < 0 || cands[i].Score > cands[index].Score)) index = i;
            if (index < 0) return;
            var cand = cands[index];
            if (cand.Eligible)
            {
                state.BelowOddsDays = 0;
                return;
            }
            // A capacity below the minimum strength is no push: it returns before the odds counter, so Decide drops the objective at once.
            if (cand.Mass < minStrength) return;
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
                    if (!PlanTriggers.BattleInvalidatesPlan(record, query)) continue;
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

        // The record of the current objective the main effort is on (PlannerState.CurrentTarget), -1 when none stands: no record kept, or it is no
        // longer offered. anyEligibility false asks for an eligible record only (one that is still valid: not accomplished, reachable, within odds).
        private static int KeptRecord(List<Candidate> cands, int objectiveId, PlannerState state, bool anyEligibility)
        {
            if (objectiveId < 0 || float.IsNaN(state.CurrentTargetX) || float.IsNaN(state.CurrentTargetZ)) return -1;
            for (int i = 0; i < cands.Count; i++)
            {
                if (cands[i].ObjectiveId != objectiveId || (!anyEligibility && !cands[i].Eligible) || cands[i].Force < 0) continue;
                if (SamePoint(cands[i].X, cands[i].Z, state.CurrentTargetX, state.CurrentTargetZ)) return i;
            }
            return -1;
        }

        private static bool SamePoint(float ax, float az, float bx, float bz)
        {
            return Math.Abs(ax - bx) <= SameTargetRadius && Math.Abs(az - bz) <= SameTargetRadius;
        }

        // The target record the current objective's main effort uses today. An objective has several records, and the best of them changing
        // must not move the main effort (smoke 2026-10-17: 29@3 -> 29@2 -> 29@6 in two reviews, switch=hold): the record kept stays until
        // another record of the objective beats it by SwitchMargin on SwitchDays consecutive reviews (the objective switch's own rule), or the
        // kept record is invalid at once (no longer offered, accomplished, unreachable, below its odds): then the best record is taken.
        private static int HeldRecord(List<Candidate> cands, int objectiveId, PlannerState state)
        {
            int best = BestOf(cands, objectiveId);
            if (best < 0) return -1;
            int kept = KeptRecord(cands, objectiveId, state, anyEligibility: false);
            if (kept < 0 || kept == best)
            {
                ResetRecordChallenger(state);
                return kept < 0 ? best : kept;
            }
            if (cands[best].Score - cands[kept].Score <= SwitchMargin)
            {
                ResetRecordChallenger(state);
                return kept;
            }
            if (state.RecordChallengerDays > 0 && SamePoint(cands[best].X, cands[best].Z, state.RecordChallengerX, state.RecordChallengerZ)) state.RecordChallengerDays++;
            else
            {
                state.RecordChallengerX = cands[best].X;
                state.RecordChallengerZ = cands[best].Z;
                state.RecordChallengerDays = 1;
            }
            if (state.RecordChallengerDays < SwitchDays) return kept;
            ResetRecordChallenger(state);
            return best;
        }

        private static void ResetRecordChallenger(PlannerState state)
        {
            state.RecordChallengerX = state.RecordChallengerZ = float.NaN;
            state.RecordChallengerDays = 0;
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
                else if (x.Facing[sector]) posture = own[sector] >= x.Need[sector] ? TheaterPosture.Hold : TheaterPosture.Screen; // D2: faces enemy ground, nothing believed
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
                    HoldNeed = x.Need[sector],
                    BelievedHold = HoldRatioNow(s) * x.Threat[sector]
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
                && f.ReadinessStep >= MinReadinessStep
                && f.Supply01 >= RecoverSupplyBelow && f.Morale >= minMorale;
        }

        // D8, the CSA side: a live secession objective (vanilla's IsDeactivated is false: the state is not the CSA's and the CSA's support there is at
        // least 0.45) is worth more as a candidate, and so is every other target in its state (clearing the Union presence out of it, which the
        // objective's accomplishment needs). Read from this review's snapshot, never cached: the next review reads vanilla again.
        internal static float SecessionValue(CampaignSnapshot s, in ObjectiveRecord o)
        {
            if (s.Alliance != SecessionRules.CsaAlliance) return 0f;
            float bonus = 0f;
            for (int i = 0; i < s.Secessions.Count; i++)
            {
                var rec = s.Secessions[i];
                if (!rec.Live) continue;
                if (o.ObjectiveId == rec.ObjectiveId) return SecessionValueBonus;
                if (o.StateId == rec.StateId) bonus = SecessionStateBonus;
            }
            return bonus;
        }

        // The D8 garrison post's hold need at a point (x, z) whose reach is `reach` (the relocation range). Foresight: max(HoldRatio' x (the
        // believed land threat + the presumed men no facing sector was assigned), Floor); the post competes for no element a sector holds
        // against (requirement-hold-need). Picture off: D2's rule (LegacyHoldNeedAt). The sector facts size their hold with the same
        // formula, except that a sector takes the elements assigned to it.
        internal static float HoldNeedAt(Inputs c, float x, float z, float reach, float threat, float holdRatio, out float presumed)
        {
            if (!PlannerSettings.ForesightPicture) return LegacyHoldNeedAt(c, x, z, reach, threat, holdRatio, out presumed);
            presumed = UnassignedPresumed(c, x, z, reach);
            return Math.Max(holdRatio * (threat + presumed), Floor(c, x, z, reach));
        }

        // D2's hold need: max(HoldRatio' x the believed land threat, MinGarrisonStrength, and while nothing fresh is believed within that reach
        // the presumed threat). Kept behind Enable Strategic Foresight Picture (the rollback).
        private static float LegacyHoldNeedAt(Inputs c, float x, float z, float reach, float threat, float holdRatio, out float presumed)
        {
            presumed = FreshLandBeliefWithin(c.S, x, z, reach) ? 0f : PresumedAt(c, x, z, reach);
            return Math.Max(Math.Max(holdRatio * threat, MinStrength(c)), presumed);
        }

        // The Faded and Anchor elements within `reach` of a point that no facing sector was assigned, weighted as the picture weights them.
        private static float UnassignedPresumed(Inputs c, float x, float z, float reach)
        {
            float total = 0f;
            var elements = c.Picture.Elements;
            for (int i = 0; i < elements.Count; i++)
            {
                var e = elements[i];
                if (e.AssignedSector >= 0 || (e.Kind != PictureElementKind.Faded && e.Kind != PictureElementKind.Anchor)) continue;
                total += e.Mass * c.Picture.WeightWithin(e.X, e.Z, x, z, reach, c.Probe);
            }
            return total;
        }

        internal static float MinStrength(Inputs c)
        {
            return c.MinStrength;
        }

        // The plan's minimum strength (D11 requirement-live-min-strength): the scenario's read when it is positive and finite, else the 5000
        // fallback; with the picture flag off always the fallback, so rolling the picture back alone never leaves the live floor under D2's
        // old presumption.
        internal static float PlanMinimum(CampaignSnapshot s)
        {
            if (!PlannerSettings.ForesightPicture) return MinStrengthFallback;
            return ReadIsUsable(s.MinStrengthForMovement) ? s.MinStrengthForMovement : MinStrengthFallback;
        }

        // Whether the scenario's raw read can be a minimum; the review warns when it cannot, whatever the flag says.
        internal static bool ReadIsUsable(float raw)
        {
            return raw > 0f && !float.IsNaN(raw) && !float.IsInfinity(raw);
        }

        private static void EnsurePicture(Inputs c)
        {
            if (c.Picture == null) c.Picture = EnemyPicture.Build(c.S, (x, z) => IsScouted(c, x, z));
        }

        // D11 Floor(x, z, r) = MinStrength x (1 + PresumedImportanceGain x I), I the largest importance of an undisplaced anchor within r (0 with
        // none: a place whose anchors are all scouted floors at the plain minimum).
        internal static float Floor(Inputs c, float x, float z, float r)
        {
            EnsurePicture(c);
            return MinStrength(c) * (1f + PresumedImportanceGain * c.Picture.UndisplacedImportance(x, z, r));
        }

        // The enemy capital's estimate: the believed army near it plus the men presumed there (the number the covering force's expected
        // short is read against).
        private static void NoteEnemyCapitalEstimates(Inputs c)
        {
            var p = c.Picture;
            if (p == null || !p.HasEnemyCapital) return;
            float range = c.S.RelocationRange;
            float believed = LandThreatNear(c, p.EnemyCapitalX, p.EnemyCapitalZ, range);
            p.EnemyCapitalEstProp = believed + p.Presumed(c.S, p.EnemyCapitalX, p.EnemyCapitalZ, range, c.Probe);
        }

        private static float HoldRatioNow(CampaignSnapshot s)
        {
            return HoldRatio + (s.Tempo != null ? s.Tempo.MinimumHoldRatioModifier : 0f);
        }

        // D8, the Union side (the state's owner): while a secession objective is live a Union campaign group standing in the state denies it
        // (nation.alliancetroops[0], 81654-81660, 179266 / 179281). Vanilla's flag counts every visible Union group of unittyp > 13, the
        // state's garrisons included (82940-82943: a garrison unit with its reference is visible), so the Union is present when the flag is
        // set (SecessionRecord.OwnTroops, read for the Union side only) or an own formation stands in the state. The formations already
        // standing in it, nearest the post (the objective's town) first, are the pledged garrison until their strength meets the D2 hold need
        // at the post (a small force when nothing is believed near: the floor, or the presumption); the first of them always counts. When
        // nobody is present, the nearest ready formation not already kept for the capital or a hold is posted there; when the flag alone
        // blocks it, nobody is posted. Every member is kept out of the mass.
        // The hold already kept against the army that threatens the town: the strength of the sector hold reserves whose reference belief (the
        // army their sector faces) lies within the relocation range of the town. Counted once, not again as a garrison.
        private static float HoldFacing(Inputs c, float x, float z)
        {
            float sum = 0f;
            for (int i = 0; i < c.S.Own.Count; i++)
            {
                int sector = c.HoldReserved[i];
                if (sector < 0 || !WlChainRules.CountsTowardCoverage(c.S.Own[i].Chain)) continue;
                if (RiverLines.Distance(c.Facts.NearX[sector], c.Facts.NearZ[sector], x, z) <= c.S.RelocationRange) sum += ReserveRules.HoldStrength(c.S.Own[i]);
            }
            return sum;
        }

        // The second half of the garrison (verify, landing check and r2): formations are pledged and posted once the mass is known, so the
        // garrison can never strip or stop it. A mass member (or the reinforcement) standing inside the state counts as the Union's presence
        // but is neither pledged nor pinned; the other formations inside are pledged, nearest the town first, until the need is met (the first
        // always). When the mass is the only presence, one replacement is posted from outside at once, so there is no unguarded gap between
        // the mass leaving and the next review; that review, with the mass gone, finds it inside and pledges it.
        // For the formations from outside the state:  The pool is the formations that would otherwise just hold, sit in reserve or have no task: not in
        // the mass (marching with it or released to it, the reinforcement included), not a capital guard, a sector's hold reserve, a builder
        // or a member of another post. Nearest the town first. A need only the mass could meet is posted as far as the pool goes and the
        // rest recorded as the post's Shortfall.
        private static void PostFromOutside(Inputs c, TheaterPlan plan)
        {
            var s = c.S;
            for (int w = 0; w < c.Pending.Count; w++)
            {
                var work = c.Pending[w];
                var post = work.Post;
                bool massInside = false;
                float held = 0f;
                int pledged = 0;
                for (int k = 0; k < work.Inside.Count; k++)
                {
                    int i = work.Inside[k];
                    if (plan.MassInstanceIds.Contains(s.Own[i].InstanceId))
                    {
                        massInside = true;
                        continue;
                    }
                    if (pledged > 0 && held >= post.Need) break;
                    pledged++;
                    c.GarrisonReserved[i] = true;
                    if (WlChainRules.CountsTowardCoverage(s.Own[i].Chain)) held += ReserveRules.HoldStrength(s.Own[i]);
                    if (WlChainRules.MayMove(s.Own[i].Chain)) post.InstanceIds.Add(s.Own[i].InstanceId);
                }
                if (post.InstanceIds.Count == 0 && work.Inside.Count > 0)
                {
                    post.MassPresence = massInside;
                    post.PlayerPresence = !massInside;
                }
                // The mass is the only presence (no flag, nobody else inside): it will march on, so one replacement is posted now, through the
                // ordinary path (never from the mass), and there is no unguarded gap between its leaving and the next review.
                int limit = post.MassPresence && !work.Flag ? Math.Max(work.Limit, 1) : work.Limit;
                float posted = 0f;
                int count = 0;
                while (count < limit && posted < post.Need)
                {
                    int best = -1;
                    float bestDistance = float.MaxValue;
                    for (int i = 0; i < s.Own.Count; i++)
                    {
                        var f = s.Own[i];
                        if (!MassEligible(f, s.MinMoraleForMovement) || c.CapitalReserved[i] || c.HoldReserved[i] >= 0 || c.GarrisonReserved[i] || IsHeldBuilder(c, f.InstanceId)) continue;
                        if (f.UnitTyp <= 13 || plan.MassInstanceIds.Contains(f.InstanceId) || !WithinHorizon(c, i, post.X, post.Z)) continue;
                        float d = DistanceSq(f.X, f.Z, post.X, post.Z);
                        if (d > bestDistance || (d == bestDistance && best >= 0 && f.InstanceId > s.Own[best].InstanceId)) continue;
                        bestDistance = d;
                        best = i;
                    }
                    if (best < 0) break;
                    c.GarrisonReserved[best] = true;
                    post.InstanceIds.Add(s.Own[best].InstanceId);
                    post.Marching = true;
                    posted += Math.Max(0f, s.Own[best].Strength);
                    count++;
                }
                if (work.FlagAlone && posted < post.Need) post.Shortfall = post.Need - posted;
                if (post.Name == null && post.InstanceIds.Count > 0) post.Name = s.Own[s.IndexOfOwn(post.InstanceIds[0])].Name;
            }
        }

        private static void Garrisons(Inputs c)
        {
            var s = c.S;
            c.Posts.Clear();
            c.Pending.Clear();
            c.GarrisonReserved = new bool[s.Own.Count];
            for (int r = 0; r < s.Secessions.Count; r++)
            {
                var rec = s.Secessions[r];
                if (!rec.Live || !rec.HasTown || rec.StateAlliance != s.Alliance) continue;
                var post = new GarrisonPost { StateName = SecessionRules.NameOf(rec.StateId), StateId = rec.StateId, X = rec.TownX, Z = rec.TownZ };
                float threat = LandThreatNear(c, post.X, post.Z, s.RelocationRange);

                var inside = new List<int>();
                for (int i = 0; i < s.Own.Count; i++)
                    if (s.Own[i].StateId == rec.StateId && s.Own[i].UnitTyp > 13) inside.Add(i);
                post.InState = inside.Count > 0 || rec.OwnTroops;   // vanilla's flag counts garrisons too: a garrison alone already blocks
                // The flag alone (a town garrison, no own formation inside) says the state is held, not that it is safe: what is posted is the
                // believed threat's hold less the garrison's own strength, nothing when no enemy is believed near. With nobody present the
                // full D2 need applies (the floor and the presumption), and so it does for the formations inside.
                bool flagAlone = rec.OwnTroops && inside.Count == 0;
                // The hold already posted against that same army (the sector hold reserves facing it) is not counted twice.
                post.Need = flagAlone
                    ? Math.Max(0f, HoldRatioNow(s) * threat - HoldFacing(c, post.X, post.Z) - Math.Max(0f, rec.GarrisonStrength))
                    : HoldNeedAt(c, post.X, post.Z, s.RelocationRange, threat, HoldRatioNow(s), out _);
                inside.Sort((a, b) =>
                {
                    int d = DistanceSq(s.Own[a].X, s.Own[a].Z, post.X, post.Z).CompareTo(DistanceSq(s.Own[b].X, s.Own[b].Z, post.X, post.Z));
                    return d != 0 ? d : s.Own[a].InstanceId.CompareTo(s.Own[b].InstanceId);
                });
                // The formations inside are pledged, and the formations from outside the state posted, once the mass is known
                // (PostFromOutside): the main effort's mass is never stopped by a garrison. Outside: one when nobody is present, as many as
                // the need takes when only the flag shows presence.
                int limit = flagAlone ? int.MaxValue : !post.InState ? 1 : 0;
                c.Pending.Add(new PostWork { Post = post, Limit = post.Need > 0f ? limit : 0, FlagAlone = flagAlone, Flag = rec.OwnTroops, Inside = inside });
                c.Posts.Add(post);
            }
        }

        // D6 detail: every own formation has a home department from its first review on (PlannerState.Observe: the theater of the sector it
        // stands in then, kept until a deliberate transfer).
        internal static void AssignDepartments(Inputs c, PlannerState state)
        {
            var s = c.S;
            c.Department = new Theater[s.Own.Count];
            state?.BeginObservation();
            for (int i = 0; i < s.Own.Count; i++)
            {
                var f = s.Own[i];
                Theater standing = c.Sectors.TheaterOf(ReserveRules.HoldSectorOf(c.Sectors, f));
                c.Department[i] = state != null ? state.Observe(f.InstanceId, f.UnitKey, standing) : standing;
            }
            if (state != null && s.Own.Count > 0) state.PruneUnobserved();   // an empty snapshot (a degraded read) prunes nothing
        }

        private static string DepartmentCountsOf(Inputs c)
        {
            var counts = new int[6];
            for (int i = 0; i < c.Department.Length; i++)
            {
                int d = (int)c.Department[i];
                if (d >= 0 && d < counts.Length) counts[d]++;
            }
            var sb = new StringBuilder();
            for (int d = 0; d < counts.Length; d++)
            {
                if (counts[d] == 0) continue;
                if (sb.Length > 0) sb.Append(',');
                sb.Append((Theater)d).Append(':').Append(counts[d]);
            }
            return sb.ToString();
        }

        internal static Theater DepartmentOf(Inputs c, int i)
        {
            if (c.Department != null && i >= 0 && i < c.Department.Length) return c.Department[i];
            return c.Sectors.TheaterOf(ReserveRules.HoldSectorOf(c.Sectors, c.S.Own[i]));
        }

        // The unit key of the decision's reinforcement, null when this decision has none yet.
        private static string LockedReinforcement(Inputs c)
        {
            return c.State != null && c.State.ReinforcementDecision == c.State.DecisionSerial ? c.State.ReinforcementKey : null;
        }

        // D6 detail (d): the losing department's D2 hold is still met without the formation: the hold needs of the sectors of that
        // theater (SectorFacts.Need, the one hold function) against what the department's other formations hold there.
        internal static bool DepartmentHoldMet(Inputs c, Theater department, int excluded)
        {
            float need = DepartmentNeed(c, department);
            float have = 0f;
            for (int j = 0; j < c.S.Own.Count; j++)
            {
                if (j == excluded || DepartmentOf(c, j) != department) continue;
                var f = c.S.Own[j];
                if (WlChainRules.CountsTowardCoverage(f.Chain)) have += ReserveRules.HoldStrength(f);
            }
            return have >= need;
        }

        // The hold a department needs. Foresight (D11 requirement-hold-need, amending D6 (d)): max(the sum of Floor over the department's facing
        // sectors, HoldRatio' x the land elements (Live, Faded, Anchor) assigned, each to its one nearest facing sector, to the department's
        // sectors), so an element counts in one department once and the sector needs of a department never hold one army twice over. Picture off:
        // the sum of the sectors' needs (SectorFacts.Need), as before.
        internal static float DepartmentNeed(Inputs c, Theater department)
        {
            if (!PlannerSettings.ForesightPicture)
            {
                float sum = 0f;
                for (int k = 0; k < c.Sectors.Count; k++)
                    if (c.Sectors.TheaterOf(k) == department) sum += c.Facts.Need[k];
                return sum;
            }
            float floors = 0f;
            for (int k = 0; k < c.Sectors.Count; k++)
                if (c.Sectors.TheaterOf(k) == department && c.Facts.Facing[k])
                    floors += Floor(c, c.Facts.CX[k], c.Facts.CZ[k], c.Facts.Radius[k] + c.S.RelocationRange);
            float assigned = 0f;
            var elements = c.Picture.Elements;
            for (int i = 0; i < elements.Count; i++)
            {
                var e = elements[i];
                if (e.AssignedSector < 0 || c.Sectors.TheaterOf(e.AssignedSector) != department) continue;
                assigned += e.Mass * e.Weight;
            }
            return Math.Max(floors, HoldRatioNow(c.S) * assigned);
        }

        // The capacity for a target in targetSector (indexes into s.Own, written to `into`) and its strength: what the planner could
        // concentrate there. A formation the move tracker gave up on for this objective (P8, GaveUpMove.ObjectiveId) is left out like a
        // busy one, so a capacity whose members all gave up is empty: the existing no-force rule drops the main effort and the odds are
        // judged on the force that can still march. D6 (armies are territorial): the capacity is the target theater's own formations
        // (the theater of the sector each stands in) plus at most one reinforcement from elsewhere (ReinforcementPick); the part of it
        // the objective needs is SelectMass.
        internal static float MassFor(Inputs c, float tx, float tz, int targetSector, int objectiveId, List<int> into)
        {
            var s = c.S;
            into.Clear();
            c.ForeignScratch.Clear();
            c.Foreign = -1;
            float total = 0f;
            Theater targetTheater = targetSector >= 0 ? c.Sectors.TheaterOf(targetSector) : Theater.Unknown;
            string locked = LockedReinforcement(c);
            for (int i = 0; i < s.Own.Count; i++)
            {
                var f = s.Own[i];
                if (!MassEligible(f, s.MinMoraleForMovement) || c.CapitalReserved[i] || GaveUpOn(c, f.InstanceId, objectiveId) || IsHeldBuilder(c, f.InstanceId)) continue;
                int holds = c.HoldReserved[i];
                if (holds >= 0 && holds != targetSector && !HoldThreatAtTarget(c, holds, tx, tz)) continue;
                // The main effort draws from its own department: the target's theater. A formation of another department is a
                // candidate for the one reinforcement; so is the decision's reinforcement once its transfer has changed its department.
                bool pick = locked != null && string.Equals(f.UnitKey, locked, StringComparison.Ordinal);
                if (targetTheater != Theater.Unknown && (pick || DepartmentOf(c, i) != targetTheater))
                {
                    c.ForeignScratch.Add(i);
                    continue;
                }
                if (!WithinHorizon(c, i, tx, tz)) continue;
                into.Add(i);
                total += Math.Max(0f, f.Strength);
            }
            int foreign = ReinforcementPick(c, tx, tz);
            if (foreign >= 0)
            {
                c.Foreign = foreign;
                into.Add(foreign);
                total += Math.Max(0f, s.Own[foreign].Strength);
            }
            return total;
        }

        // D6: the one reinforcement. A decision that already has one keeps only that formation (by unit key, so it survives a load); a
        // new decision picks the formation that can be at the target soonest (MarchTime.EstimateDays, rail leg included), within
        // ReinforcementMaxDays, the lower instance id on a tie. Its own front's D2 hold is met without it by construction: a formation the
        // hold reserve keeps is not in the foreign list.
        private static int ReinforcementPick(Inputs c, float tx, float tz)
        {
            var s = c.S;
            string locked = LockedReinforcement(c);
            int best = -1;
            float bestDays = float.MaxValue;
            for (int k = 0; k < c.ForeignScratch.Count; k++)
            {
                int i = c.ForeignScratch[k];
                var f = s.Own[i];
                if (locked != null && !string.Equals(f.UnitKey, locked, StringComparison.Ordinal)) continue;
                // A new transfer leaves its department's hold met (a locked one already moved).
                if (locked == null && !DepartmentHoldMet(c, DepartmentOf(c, i), i)) continue;
                float distance = RiverLines.Distance(f.X, f.Z, tx, tz);
                float days = MarchTime.EstimateDays(c.Speed, f, tx, tz, distance, s, null, c.OwnIips);
                if (days > ReinforcementMaxDays) continue;
                if (days > bestDays || (days == bestDays && best >= 0 && f.InstanceId > s.Own[best].InstanceId)) continue;
                bestDays = days;
                best = i;
            }
            return best;
        }

        // D3: the force an objective needs against `enemy` at `required` odds: enemy x required x (1 + margin), the margin
        // MassMarginBase x (1 - MassMarginBiasScale x AttackBias) (bold: leaner, cautious: heavier), never below the smallest force
        // the AI moves at all (the plan's minimum strength).
        internal static float NeedFor(float enemy, float required, float attackBias, float minimum)
        {
            float margin = MassMarginBase * (1f - MassMarginBiasScale * Math.Max(-1f, Math.Min(1f, attackBias)));
            return Math.Max(minimum, enemy * required * (1f + margin));
        }

        // D3: the part of the capacity the objective needs, nearest the target first (the lower instance id on a tie), the
        // reinforcement last and only if the natives fall short. An army's corps go together (D6 detail (e)): the natives are taken by army,
        // an army being the formations that share a ParentInstanceId (a formation with no parent is its own), the army nearest the target
        // first and all its capacity members once it is entered, so an army is never split between the push and the front it came from
        // (a corps its department's hold needs is not in the capacity at all). Written to the plan (MassInstanceIds,
        // ReinforcementInstanceId); returns its strength. Formations left out keep their own front's role (ForceAllocator).
        private static float SelectMass(Inputs c, List<int> capacity, float tx, float tz, float need, TheaterPlan plan)
        {
            var s = c.S;
            var natives = new List<int>(capacity.Count);
            for (int k = 0; k < capacity.Count; k++)
                if (capacity[k] != c.Foreign) natives.Add(capacity[k]);
            Comparison<int> byDistance = (a, b) =>
            {
                int d = DistanceSq(s.Own[a].X, s.Own[a].Z, tx, tz).CompareTo(DistanceSq(s.Own[b].X, s.Own[b].Z, tx, tz));
                return d != 0 ? d : s.Own[a].InstanceId.CompareTo(s.Own[b].InstanceId);
            };
            natives.Sort(byDistance);
            var armies = new List<List<int>>();
            for (int k = 0; k < natives.Count; k++)
            {
                int parent = s.Own[natives[k]].ParentInstanceId;
                List<int> army = null;
                if (parent >= 0)
                    for (int a = 0; a < armies.Count && army == null; a++)
                        if (s.Own[armies[a][0]].ParentInstanceId == parent) army = armies[a];
                if (army == null)
                {
                    army = new List<int>();
                    armies.Add(army);
                }
                army.Add(natives[k]);   // natives are sorted, so an army's members and the armies themselves are nearest first
            }
            float total = 0f;
            for (int a = 0; a < armies.Count && total < need; a++)
                for (int k = 0; k < armies[a].Count; k++)
                {
                    plan.MassInstanceIds.Add(s.Own[armies[a][k]].InstanceId);
                    total += Math.Max(0f, s.Own[armies[a][k]].Strength);
                }
            if (c.Foreign >= 0 && total < need)
            {
                plan.MassInstanceIds.Add(s.Own[c.Foreign].InstanceId);
                plan.ReinforcementInstanceId = s.Own[c.Foreign].InstanceId;
                total += Math.Max(0f, s.Own[c.Foreign].Strength);
            }
            return total;
        }

        // D6: a reinforcement the mass took binds to this decision (once; a decision never takes a second one), and the transfer changes
        // its home department to the main objective's (PlannerState.Transfer): it is that department's formation from now on.
        private static void NoteReinforcement(Inputs c, TheaterPlan plan, PlannerState state)
        {
            if (plan.ReinforcementInstanceId < 0 || state == null) return;
            int i = c.S.IndexOfOwn(plan.ReinforcementInstanceId);
            if (i < 0) return;
            plan.ReinforcementName = c.S.Own[i].Name;
            plan.ReinforcementTo = c.Sectors.TheaterOf(plan.MainSectorId);
            if (state.ReinforcementDecision == state.DecisionSerial && state.ReinforcementKey != null) return;
            plan.ReinforcementFrom = DepartmentOf(c, i);
            state.ReinforcementKey = c.S.Own[i].UnitKey;
            state.ReinforcementDecision = state.DecisionSerial;
            state.Transfer(c.S.Own[i].UnitKey, plan.ReinforcementTo);
        }

        // A formation the depot ledger holds as a builder is the depot's, not the mass's (G5c): the published held set, one source.
        private static bool IsHeldBuilder(Inputs c, int instanceId)
        {
            var list = c.Held;
            if (list == null) return false;
            for (int k = 0; k < list.Count; k++)
                if (list[k].InstanceId == instanceId) return true;
            return false;
        }

        private static bool GaveUpOn(Inputs c, int instanceId, int objectiveId)
        {
            var list = c.GaveUp;
            if (list == null) return false;
            for (int k = 0; k < list.Count; k++)
                if (list[k].InstanceId == instanceId && list[k].ObjectiveId == objectiveId) return true;
            return false;
        }

        // A sector's hold is released for a target that is the very army the hold faces: attacking it is holding against it. Foresight (D11
        // requirement-hold-release): only when every threat element the sector faces lies within the relocation range of the target, each
        // live land belief and each anchor holding men inside the sector's reach; a sector facing none releases nothing. Picture off: the
        // nearest believed army within range of the target (one sighting 156 from Romney released the whole Manassas hold).
        internal static bool HoldThreatAtTarget(Inputs c, int sector, float tx, float tz)
        {
            if (!PlannerSettings.ForesightPicture)
                return RiverLines.Distance(c.Facts.NearX[sector], c.Facts.NearZ[sector], tx, tz) <= c.S.RelocationRange;
            float range = c.S.RelocationRange;
            float cx = c.Facts.CX[sector], cz = c.Facts.CZ[sector];
            float reach = c.Facts.Radius[sector] + range;
            bool faced = false;
            var beliefs = c.S.Beliefs;
            for (int i = 0; i < beliefs.Count; i++)
            {
                var b = beliefs[i];
                if (Live(b) <= 0f || Afloat(b) || RiverLines.Distance(b.X, b.Z, cx, cz) > reach) continue;
                faced = true;
                if (RiverLines.Distance(b.X, b.Z, tx, tz) > range) return false;
            }
            var anchors = c.Picture.Anchors;
            for (int i = 0; i < anchors.Count; i++)
            {
                var a = anchors[i];
                if (a.Share <= 0f || RiverLines.Distance(a.X, a.Z, cx, cz) > reach) continue;
                faced = true;
                if (RiverLines.Distance(a.X, a.Z, tx, tz) > range) return false;
            }
            return faced;
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
        internal static SectorFacts BuildSectorFacts(Inputs c)
        {
            EnsurePicture(c);
            var s = c.S;
            int n = c.Sectors.Count;
            var x = new SectorFacts
            {
                CX = new float[n], CZ = new float[n], Radius = new float[n], Threat = new float[n], Need = new float[n],
                Own = new float[n], Supply = new float[n], Morale = new float[n], NearX = new float[n], NearZ = new float[n],
                Count = new int[n], Strongest = new int[n], Orderable = new int[n], Presumed = new float[n], Facing = new bool[n], Assigned = new float[n]
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
            bool foresight = PlannerSettings.ForesightPicture;
            for (int k = 0; k < n; k++)
            {
                c.Terrain?.Mark(ReviewTerrain.GroupSector, k, x.CX[k], x.CZ[k]);
                float reach = x.Radius[k] + s.RelocationRange;
                x.Threat[k] = SectorThreat(c, x.CX[k], x.CZ[k], x.Radius[k]);
                // A sector faces enemy ground when it faces a believed army, an enemy-held objective within its radius plus the relocation
                // range, or (foresight) an anchor holding men there.
                x.Facing[k] = x.Threat[k] > 0f || EnemyGroundWithin(s, x.CX[k], x.CZ[k], reach)
                    || (foresight && c.Picture.AnchorElementWithin(x.CX[k], x.CZ[k], reach));
                int near = NearestLandBelief(s, x.CX[k], x.CZ[k]);
                x.NearX[k] = near >= 0 ? s.Beliefs[near].X : x.CX[k];
                x.NearZ[k] = near >= 0 ? s.Beliefs[near].Z : x.CZ[k];
            }
            // Foresight: each presumed element counts in one facing sector only (its nearest), the assignment re-run every pass.
            if (foresight) c.Picture.AssignSectors(x, c.Sectors, s.RelocationRange, c.Probe);
            for (int k = 0; k < n; k++)
            {
                float reach = x.Radius[k] + s.RelocationRange;
                if (foresight)
                {
                    // D11 requirement-hold-need: unseen men are held against at the same ratio as seen ones, never below the floor.
                    x.Presumed[k] = x.Assigned[k];
                    x.Need[k] = x.Facing[k]
                        ? Math.Max(holdRatio * (x.Threat[k] + x.Assigned[k]), Floor(c, x.CX[k], x.CZ[k], reach))
                        : holdRatio * x.Threat[k];
                }
                else
                {
                    // D2: a sector facing enemy-held ground or a believed army keeps a hold of max(HoldRatio x believed, MinGarrison, presumed), the
                    // presumption only while nothing there is scouted (no fresh land belief within that reach).
                    x.Need[k] = x.Facing[k]
                        ? LegacyHoldNeedAt(c, x.CX[k], x.CZ[k], reach, x.Threat[k], holdRatio, out x.Presumed[k])
                        : holdRatio * x.Threat[k];
                }
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
            ReserveRules.Capital(c.S, c.CapitalFloor, c.CapitalReserved, ReserveRules.WishedBuilders(c.S, c.Held));
            ReserveRules.Hold(c.S, c.Sectors, c.Facts.Need, c.Facts.NearX, c.Facts.NearZ, c.CapitalReserved, c.HoldReserved);
            Garrisons(c);
        }

        private static CommanderProfile ProfileOf(Inputs c, int formation)
        {
            if (formation < 0 || c.Profile == null) return default(CommanderProfile);
            return c.Profile(c.S.Own[formation].CommanderId);
        }

        private static bool EnemyGroundWithin(CampaignSnapshot s, float x, float z, float radius)
        {
            for (int i = 0; i < s.Objectives.Count; i++)
            {
                var o = s.Objectives[i];
                if (o.Accomplished || o.HolderAlliance < 0 || o.HolderAlliance == s.Alliance) continue;
                if (RiverLines.Distance(o.X, o.Z, x, z) <= radius) return true;
            }
            return false;
        }

        private static bool FreshLandBeliefWithin(CampaignSnapshot s, float x, float z, float radius)
        {
            for (int i = 0; i < s.Beliefs.Count; i++)
            {
                var b = s.Beliefs[i];
                // A mark belief never scouts (foresight F4): it is where a hidden group was last shown, within a mark radius of the truth.
                if (b.LastSource == DetectionSource.Mark) continue;
                if (b.Confidence01 >= ScoutFreshConfidence && !Afloat(b) && RiverLines.Distance(b.X, b.Z, x, z) <= radius) return true;
            }
            return false;
        }

        // D1: the place is scouted when a fresh land belief lies within the relocation range of it (the radius its odds are read on)
        // or an own formation's sight radius covers it (what the viewer sees, it sees: an empty target in view is a scouted one).
        // Fog-honest: beliefs and own viewers only.
        internal static bool IsScouted(Inputs c, float x, float z)
        {
            var s = c.S;
            if (FreshLandBeliefWithin(s, x, z, s.RelocationRange)) return true;
            for (int i = 0; i < s.Own.Count; i++)
            {
                var f = s.Own[i];
                if (f.SightRadius > 0f && RiverLines.Distance(f.X, f.Z, x, z) <= f.SightRadius) return true;
            }
            return false;
        }

        // D2: the presumed enemy within `radius` of (x, z) when nothing there is scouted, from fog-honest inputs only:
        //   max( MinGarrisonStrength x (1 + PresumedImportanceGain x I),  S )
        // where I is the largest importance of an enemy-held objective within the radius (public ownership and importance) and S the
        // strongest army the side's own beliefs remember there at a confidence below ScoutFreshConfidence (an aged-out sighting: at its
        // last-seen strength, not the confidence-weighted figure that makes an aging army look smaller by the day).
        internal static float PresumedAt(Inputs c, float x, float z, float radius)
        {
            var s = c.S;
            float importance = 0f;
            for (int i = 0; i < s.Objectives.Count; i++)
            {
                var o = s.Objectives[i];
                if (o.Accomplished || o.HolderAlliance < 0 || o.HolderAlliance == s.Alliance) continue;
                if (RiverLines.Distance(o.X, o.Z, x, z) <= radius) importance = Math.Max(importance, o.Importance01);
            }
            float remembered = 0f;
            for (int i = 0; i < s.Beliefs.Count; i++)
            {
                var b = s.Beliefs[i];
                if (b.Confidence01 <= 0f || b.Confidence01 >= ScoutFreshConfidence || Afloat(b)) continue;
                if (RiverLines.Distance(b.X, b.Z, x, z) <= radius) remembered = Math.Max(remembered, Math.Max(0f, b.Strength));
            }
            return Math.Max(MinStrength(c) * (1f + PresumedImportanceGain * importance), remembered);
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

        // The capital floor and threat from one computation. P7: floor = CapitalFloorRatio x vanilla's weight (see the header). Foresight
        // (D11 requirement-capital-window and -floor): the weight is taken over the picture's elements inside the capital reach, a race
        // against the capital's own department (CapitalRace), and the floor is the minimum strength plus that weight, never below #4's
        // VanillaNeed on this snapshot; the threat that trips the 0.6 capital trigger excludes the baseline, so it never fires on an empty map.
        // Picture off: P7's numbers over the relocation range.
        internal static void CapitalThreat(Inputs c, out float floor, out float threat01)
        {
            floor = 0f;
            threat01 = 0f;
            var s = c.S;
            c.CapitalWeighted = 0f;
            c.CapitalReach = s.RelocationRange;
            c.CapitalReliefDays = 0f;
            c.CapitalCorpsPerDay = 0f;
            if (!s.HasCapital) return;
            EnsurePicture(c);
            float range = s.RelocationRange;
            float weighted;
            if (PlannerSettings.ForesightPicture)
            {
                CapitalRace(c, out c.CapitalReliefDays, out c.CapitalCorpsPerDay, out c.CapitalReach);
                weighted = ReserveRules.PictureThreat(s, c.Picture, s.CapitalX, s.CapitalZ, c.CapitalReach, c.Probe);
                float vanillaNeed = CapitalFloorPolicy.VanillaNeed(MinStrength(c), s.Beliefs, 1f, s.CapitalX, s.CapitalZ, range, s.Own);
                floor = Math.Max(MinStrength(c) + CapitalFloorRatio * weighted, vanillaNeed);
            }
            else
            {
                weighted = ReserveRules.VanillaThreat(s, s.CapitalX, s.CapitalZ, range, c.Probe);
                floor = CapitalFloorRatio * weighted;
            }
            c.CapitalWeighted = weighted;
            if (CapitalFloorRatio * weighted <= 0f) return;
            float own = 0f;
            for (int i = 0; i < s.Own.Count; i++)
            {
                var f = s.Own[i];
                if (!WlChainRules.CountsTowardCoverage(f.Chain)) continue;
                ReserveRules.HoldPoint(f, out float hx, out float hz); // vanilla reads GetLastWaypointPos (11733-11740)
                if (RiverLines.Distance(hx, hz, s.CapitalX, s.CapitalZ) >= range) continue;
                own += Math.Max(0f, Math.Min(f.Strength, f.Strength * f.Morale));
            }
            threat01 = SnapshotRules.Clamp01(CapitalFloorRatio * weighted / Math.Max(1f, own));
        }

        // The capital reach is a race, not a constant: max(RelocationRange, ReliefDays x CorpsRoadPerDay). ReliefDays is the largest march
        // time to the own capital among the own MassEligible formations whose home department is the capital sector's theater (the slowest
        // formation of the department sets the race, so one far corps stretches it); CorpsRoadPerDay a corps' road rate at vanilla's group
        // speed ceiling (the unit-type-15 base speed as the group speed). Rails are credited to neither side: the estimate is called with no
        // snapshot, so the rail leg is skipped (one fast own train must not shrink the reach and drop Washington again), and a formation
        // with no speed (an estimate of float.MaxValue) is skipped. With no such formation, or no speed table, the reach is the relocation range.
        private static void CapitalRace(Inputs c, out float reliefDays, out float corpsPerDay, out float reach)
        {
            var s = c.S;
            reliefDays = 0f;
            corpsPerDay = 0f;
            var baseSpeed = c.Speed.MountedBaseSpeed;
            if (baseSpeed != null && baseSpeed.Length > CorpsUnitTyp)
                corpsPerDay = MarchTime.SpeedPerDay(c.Speed, CorpsUnitTyp, baseSpeed[CorpsUnitTyp], c.Speed.RoadCost);
            int sector = c.Sectors.SectorAt(s.CapitalX, s.CapitalZ);
            Theater department = sector >= 0 ? c.Sectors.TheaterOf(sector) : Theater.Unknown;
            if (department != Theater.Unknown)
            {
                for (int i = 0; i < s.Own.Count; i++)
                {
                    var f = s.Own[i];
                    if (!MassEligible(f, s.MinMoraleForMovement) || DepartmentOf(c, i) != department) continue;
                    float days = MarchTime.EstimateDays(c.Speed, f, s.CapitalX, s.CapitalZ, RiverLines.Distance(f.X, f.Z, s.CapitalX, s.CapitalZ), null, null, c.OwnIips);
                    if (days >= float.MaxValue) continue;
                    reliefDays = Math.Max(reliefDays, days);
                }
            }
            reach = Math.Max(s.RelocationRange, reliefDays * corpsPerDay);
        }

        private const int CorpsUnitTyp = 15;

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

        // "<theater>/<objective>" of the main effort, "none" with no main effort: the [CampaignPlan] row's main field
        // and the monthly heartbeat's.
        internal static string MainLabel(TheaterPlan plan)
        {
            if (plan == null || plan.MainObjectiveId < 0) return "none";
            Theater theater = Theater.Unknown;
            for (int i = 0; i < plan.Sectors.Count; i++)
                if (plan.Sectors[i].Posture == TheaterPosture.MainEffort) theater = plan.Sectors[i].Theater;
            return theater + "/" + plan.MainObjectiveId;
        }

        // The [CampaignPlan] row (plan G3 Steps): changes only when the decision does.
        internal static string Summary(TheaterPlan plan)
        {
            int support = 0, hold = 0, eof = 0, screen = 0, recover = 0, main = 0;
            for (int i = 0; i < plan.Sectors.Count; i++)
            {
                switch (plan.Sectors[i].Posture)
                {
                    case TheaterPosture.MainEffort: main++; break;
                    case TheaterPosture.Support: support++; break;
                    case TheaterPosture.Hold: hold++; break;
                    case TheaterPosture.EconomyOfForce: eof++; break;
                    case TheaterPosture.Screen: screen++; break;
                    case TheaterPosture.Recover: recover++; break;
                }
            }
            return "[CampaignPlan] alliance=" + plan.Alliance
                + " main=" + MainLabel(plan)
                + " ratio=" + plan.MainRequiredRatio.ToString("0.00", CultureInfo.InvariantCulture)
                + " sectors=main:" + main + ",support:" + support + ",hold:" + hold + ",eof:" + eof + ",screen:" + screen + ",recover:" + recover
                + " switch=" + (plan.SwitchReason ?? "none")
                + " prior=" + (plan.PriorId ?? "none")
                + " depots=" + plan.DepotTasks.Count
                + " scouted=" + (plan.MainObjectiveId < 0 ? "none" : plan.MainScoutLatched ? "latched" : plan.MainScouted ? "yes" : "no")
                + " staged=" + (plan.MainStaged ? "yes" : "no")
                + " depts=" + (plan.DepartmentCounts.Length == 0 ? "none" : plan.DepartmentCounts)
                + " xdept=" + (plan.ReinforcementInstanceId < 0 ? "none" : TelemetryLegacyParser.Quote(plan.ReinforcementName + (plan.ReinforcementFrom != Theater.Unknown ? ":" + plan.ReinforcementFrom + ">" + plan.ReinforcementTo : ":" + plan.ReinforcementTo)));
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
                  .Append(" at=").Append(x.X.ToString("0", CultureInfo.InvariantCulture)).Append(',').Append(x.Z.ToString("0", CultureInfo.InvariantCulture))
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
                  .Append(x.Scouted ? " scouted" : " unscouted")
                  .Append(" est=").Append(x.Enemy.ToString("0", CultureInfo.InvariantCulture))
                  .Append(" pres=").Append(x.Presumed.ToString("0", CultureInfo.InvariantCulture))
                  .Append(" anchors=").Append(x.Anchors)
                  .Append(" mass=").Append(x.Mass.ToString("0", CultureInfo.InvariantCulture))
                  .Append(x.Retained ? " retained" : x.Eligible ? "" : " ineligible");
            }
            return sb.ToString();
        }
    }
}
