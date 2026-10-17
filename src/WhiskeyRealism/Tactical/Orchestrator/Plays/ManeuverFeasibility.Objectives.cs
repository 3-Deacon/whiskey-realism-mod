using System;
using WhiskeyRealism.Tactical.Battle;

namespace WhiskeyRealism.Tactical.Orchestrator.Plays
{
    /// <summary>
    /// The objective table's working set for one evaluation (BC14O requirement-objective-feasibility): the rows, the one hypothetical main body that
    /// every objective is priced against, and the pressure of the clock. Pooled, reused by every evaluation.
    /// </summary>
    internal sealed class ObjectiveWork
    {
        public readonly ObjectiveEstimate[] Table = new ObjectiveEstimate[ManeuverFeasibility.MaxObjectives];
        /// <summary>The input's fact each row was made from.</summary>
        public readonly int[] FactOf = new int[ManeuverFeasibility.MaxObjectives];
        public int Count, Next;
        public ObjectivePressure Pressure;
        public bool BodyPrepared;
        /// <summary>The child slots whose routes price the objectives: the strongest <see cref="ObjectiveScoring.MaxObjectiveMembers"/> of the main body.</summary>
        public readonly int[] Members = new int[ObjectiveScoring.MaxObjectiveMembers];
        public int MemberCount;
        public float BodyStrength, BodyMorale;

        public bool Done => Next >= Count;

        public void Clear()
        {
            Count = 0;
            Next = 0;
            Pressure = default(ObjectivePressure);
            BodyPrepared = false;
            MemberCount = 0;
            BodyStrength = 0f;
            BodyMorale = 0f;
        }
    }

    // BC14O requirement-objective-value, -victory-pressure and -objective-feasibility (spec § 7B): the ledger's objective table. Each live objective the
    // side does not hold (and each own-held one under threat) gets a feasibility penalty for the army's one hypothetical main body, by the model's own
    // functions: the route screen, the strike time against the daylight, and the detail term with the objective as the strike point.
    public static partial class ManeuverFeasibility
    {
        public const int MaxObjectives = 16;

        /// <summary>Starts the table for an evaluation: the pressure, and a row for each objective that needs a penalty. With the flag off there are none.</summary>
        internal static void BeginObjectives(FeasibilityJob job)
        {
            ObjectiveWork w = job.Objectives;
            w.Clear();
            if (!Plugin.ObjectiveDoctrineEnabled()) return;
            if (job.Input.HasClock) w.Pressure = ObjectiveScoring.Pressure(in job.Input.Clock, job.Input.Army.Agg01);
            ObjectiveFact[] facts = job.Input.Objectives;
            int n = facts == null ? 0 : Math.Min(Math.Max(job.Input.ObjectiveCount, 0), Math.Min(MaxObjectives, facts.Length));
            float max = 0f;
            for (int i = 0; i < n; i++) if (facts[i].Points > max) max = facts[i].Points;
            for (int i = 0; i < n; i++)
            {
                // An own-held objective nobody threatens needs no penalty: it is not taken, and nothing is about to take it.
                if (facts[i].OwnHeld && !facts[i].Threatened) continue;
                float v = ObjectiveScoring.Normalised(facts[i].Points, max);
                w.Table[w.Count] = new ObjectiveEstimate
                {
                    Id = facts[i].Id, V = v, Value = ObjectiveScoring.Value(v, facts[i].OwnHeld),
                    Hold = facts[i].OwnHeld ? ObjectiveScoring.HoldWeight * w.Pressure.H : 0f,
                };
                w.FactOf[w.Count++] = i;
            }
        }

        /// <summary>
        /// Picks the one hypothetical main body, once the candidates are evaluated (the selected plan's reserve rect is known by then): every direct
        /// child that is offered, is not the deployed reserve, is not the selected plan's reserve rect, is not screening a secondary objective and holds
        /// no player-owned strength. Its strength and morale are over all of them; its routes are priced from the strongest five.
        /// </summary>
        internal static void PrepareObjectiveBody(FeasibilityJob job)
        {
            ObjectiveWork w = job.Objectives;
            if (w.BodyPrepared) return;
            w.BodyPrepared = true;
            float strength = 0f, moraleSum = 0f;
            for (int c = 0; c < job.ChildCount; c++)
            {
                ChildFact child = job.Children[c];
                if (!child.Offered || child.DeployedReserve || child.HasPlayerOwned) continue;
                if (job.Target != null && job.Target.BackIsReserveRect(child.Id)) continue;
                strength += child.Strength;
                moraleSum += child.Morale * child.Strength;
                // Keep the strongest MaxObjectiveMembers by insertion.
                int at = Math.Min(w.MemberCount, w.Members.Length - 1);
                if (w.MemberCount == w.Members.Length && child.Strength <= job.Children[w.Members[at]].Strength) continue;
                if (w.MemberCount < w.Members.Length) w.MemberCount++;
                while (at > 0 && job.Children[w.Members[at - 1]].Strength < child.Strength)
                {
                    w.Members[at] = w.Members[at - 1];
                    at--;
                }
                w.Members[at] = c;
            }
            w.BodyStrength = strength;
            w.BodyMorale = strength > 0f ? moraleSum / strength : 0f;
        }

        /// <summary>Prices the next row of the table. One unit of work: at most one route search per member and one for the enemy's shift.</summary>
        internal static void StepObjective(FeasibilityJob job)
        {
            ObjectiveWork w = job.Objectives;
            int row = w.Next++;
            ref ObjectiveEstimate e = ref w.Table[row];
            ObjectiveFact f = job.Input.Objectives[w.FactOf[row]];
            e.Feasible = false;
            e.Penalty = 1f;
            if (!job.FrameValid) { e.Reason = "no-axis"; return; }
            if (w.MemberCount == 0) { e.Reason = "no-body"; return; }

            // The route screen and the strike time T_i: the latest member's courier and march, then the deploy hours.
            float latest = 0f, contested = 0f;
            bool noRoute = false, refused = false;
            for (int m = 0; m < w.MemberCount; m++)
            {
                ChildFact child = job.Children[w.Members[m]];
                RouteResult route = job.Graph.Route(child.X, child.Z, f.X, f.Z, child.BaseSpeed, child.MeanFatigue, false);
                float march;
                if (route.Found)
                {
                    march = route.Hours;
                    contested = Math.Max(contested, route.Contested * StrikeLegWeight);
                }
                else if (route.NoRoute)
                {
                    noRoute = true;
                    refused |= route.Refused;
                    continue;
                }
                else march = StraightHours(job, child.X, child.Z, f.X, f.Z, in child);
                float courier = child.InContact || child.Moving ? 0f : child.CourierHours;
                latest = Math.Max(latest, courier + march);
            }
            if (noRoute) { e.Reason = refused ? "no-route" : "no-crossing"; return; }

            float strikeHours = latest + DeployHours(in job.Input.Rules);
            bool holds = f.OwnHeld;
            bool assault = !holds && job.WorksActive && job.Works.InWorksBand(f.X, f.Z);
            var b = new BodyEstimate
            {
                Main = true, Strength = w.BodyStrength, Morale = w.BodyMorale, StrikeX = f.X, StrikeZ = f.Z, TargetX = f.X, TargetZ = f.Z,
                Daylight = strikeHours > job.Input.DaylightHours - DaylightMarginHours ? 1f : 0f,
                Contested = contested, AssaultsWorks = assault, StrikeOnWorks = assault,
                SupportHours = WorksRules.AssaultHours, ExposureWindowHours = WorksRules.AssaultHours,
            };
            // Taking an objective is an attack (own 1); holding an own one is a defence from its ground.
            PriceDetail(job, ref b, 0f, assault, false, holds ? MainOwnRatio(job, in b) : 1f, Bar(in job.Input.Army), 1);
            e.Penalty = ObjectiveScoring.Penalty(b.Daylight, b.Detail, contested);
            e.Score = ObjectiveScoring.Score(in e, in w.Pressure);
            e.Feasible = true;
            e.Reason = ReasonOf(in b);
        }
    }
}
