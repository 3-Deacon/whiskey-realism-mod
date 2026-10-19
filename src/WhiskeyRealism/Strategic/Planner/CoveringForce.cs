using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using WhiskeyRealism.Telemetry;

namespace WhiskeyRealism.Strategic.Planner
{
    // The covering corridor between the two capitals (foresight F6b-1, D11 requirements covering-post and covering-size's axis shares;
    // decision 4): where a force that covers the road between them would stand, and which sectors hold against enemy elements on that road.
    // Pure geometry over the snapshot and the published picture; it takes no formation. The covering force proper (its need, members, verdict
    // and lifecycle) is F6b-2's, built over these functions.
    //
    // The axis is the segment own capital -> enemy capital. Two points on it come from vanilla's own capital-defence radius, the relocation
    // range R (AssignUnitToDefendCapital counts an enemy only inside it, 11719-11727): the forward point at R from the enemy capital, the
    // nearest the force can stand without entering the enemy capital's defence radius, and the rear point at R from the own capital, the edge
    // of the radius inside which vanilla's own capital defence already acts. Each post is the own-held town (BattleUnits.towns, Town.Owner;
    // vanilla's offensive aims at towns first, GetClosestObjectivePosOfArea 14587-14632, so a town is where an advance on the capital is met)
    // nearest its point within R of it, by straight distance, the lower place index on a tie. Small towns (BattleUnits.smalltowns), rail
    // lines and any distance fitted to a town name are no part of the rule: the posts are whatever it gives.
    //
    // Foresight F6b-2 adds the force itself over those functions: Need and Verdict (pure rules), Prepare (the hold pass's input from the review),
    // TakeIncumbents and PickMembers (called by ReserveRules.Hold, which is where the members are chosen: the axis sectors' own hold reserves,
    // counted in the sector that reserved them), Settle (the verdict published for the review), LeaveBehind, Publish and WriteState (the
    // lifecycle in TheaterPlanner.Plan) and Row ([CoveringForce]).
    internal static class CoveringForce
    {
        // Why Kind is None (CoveringPlan.Reason).
        internal const string ReasonNoCapital = "no-capital", ReasonNoCorridorPlace = "no-corridor-place", ReasonNoAxisSector = "no-axis-sector",
                              ReasonFlagOff = "flag-off", ReasonCicSide = "cic-side", ReasonEnemyCapitalPush = "main-effort-enemy-capital";
        // The post key of a day with no force: any real key differs from it, so the first post that appears takes the raw verdict.
        internal const string UnboundPostKey = "none|none";

        // Both points clamp to the segment, so an axis under R long puts the forward point at the own capital and the rear point at the
        // enemy capital. An axis between R and 2R puts the forward point nearer the own capital than the rear point (002/A: 189 against 200
        // from Richmond), accepted. Precondition: both capitals exist (s.HasCapital and s.HasEnemyCapital).
        internal static void Points(CampaignSnapshot s, out float fx, out float fz, out float rx, out float rz)
        {
            float dx = s.EnemyCapitalX - s.CapitalX, dz = s.EnemyCapitalZ - s.CapitalZ;
            float length = (float)Math.Sqrt(dx * dx + dz * dz);
            if (length <= 0f)
            {
                fx = rx = s.CapitalX;
                fz = rz = s.CapitalZ;
                return;
            }
            float forward = SnapshotRules.Clamp01((length - s.RelocationRange) / length);
            float rear = SnapshotRules.Clamp01(s.RelocationRange / length);
            fx = s.CapitalX + dx * forward;
            fz = s.CapitalZ + dz * forward;
            rx = s.CapitalX + dx * rear;
            rz = s.CapitalZ + dz * rear;
        }

        // The own-held town nearest (x, z) within R of it, as an index into s.Places; -1 when there is none. Neither capital is a post (a town
        // within TheaterPlanner.SameTargetRadius of either is the capital), "within" is <=, a tie goes to the lower place index.
        internal static int PostNear(CampaignSnapshot s, float x, float z)
        {
            float reachSq = s.RelocationRange * s.RelocationRange;
            int best = -1;
            float bestSq = float.MaxValue;
            for (int i = 0; i < s.Places.Count; i++)
            {
                var place = s.Places[i];
                if (place.Owner != s.Alliance || IsCapital(s, in place)) continue;
                float distanceSq = ReserveRules.DistanceSq(place.X, place.Z, x, z);
                if (distanceSq > reachSq || distanceSq >= bestSq) continue;
                bestSq = distanceSq;
                best = i;
            }
            return best;
        }

        private static bool IsCapital(CampaignSnapshot s, in PlaceRecord place)
        {
            return (s.HasCapital && TheaterPlanner.SamePoint(place.X, place.Z, s.CapitalX, s.CapitalZ))
                || (s.HasEnemyCapital && TheaterPlanner.SamePoint(place.X, place.Z, s.EnemyCapitalX, s.EnemyCapitalZ));
        }

        // A sector's axis share: the part of its hold need that is held against enemy elements on the axis, min(HoldNeed, holdRatio x the land
        // elements of its need whose position lies within R of the axis segment). Its believed part is the shipped LandThreatNear of
        // requirement-hold-need restricted to beliefs within R of the axis (a seen army near two axis sectors counts in both shares, which
        // loosens the cap; no sector gives more than its own share), its presumed part the Faded and Anchor elements the picture assigned to the
        // sector, each at the weight the assignment gave it. The sectors with a share above 0 are the axis sectors; a sector holding against an
        // army off the axis (the Peninsula's Fort Monroe) has none, however near its centroid lies. into (one per sector) is overwritten.
        internal static void AxisShares(CampaignSnapshot s, SectorModel sectors, EnemyPicture picture, float[] holdNeed, float holdRatio,
                                        ITerrainLineProbe probe, float[] into)
        {
            for (int k = 0; k < into.Length; k++) into[k] = 0f;
            if (!s.HasCapital || !s.HasEnemyCapital || picture == null) return;
            float band = s.RelocationRange;
            for (int k = 0; k < sectors.Count && k < into.Length && k < holdNeed.Length; k++)
            {
                if (holdNeed[k] <= 0f) continue;
                sectors.Centroid(k, out float cx, out float cz);
                float reach = sectors.Radius(k) + band;
                float onAxis = 0f;
                for (int i = 0; i < s.Beliefs.Count; i++)
                {
                    var b = s.Beliefs[i];
                    float live = ReserveRules.Live(b);
                    if (live <= 0f || ReserveRules.Afloat(b) || RiverLines.Distance(b.X, b.Z, cx, cz) > reach) continue;
                    if (TheaterPlanner.SegmentDistance(b.X, b.Z, s.CapitalX, s.CapitalZ, s.EnemyCapitalX, s.EnemyCapitalZ) > band) continue;
                    float effective = RiverLines.EffectiveDistance(b.X, b.Z, cx, cz, probe, s.Crossings);
                    onAxis += live * (effective > reach ? reach / effective : 1f);
                }
                for (int i = 0; i < picture.Elements.Count; i++)
                {
                    var e = picture.Elements[i];
                    if (e.AssignedSector != k || (e.Kind != PictureElementKind.Faded && e.Kind != PictureElementKind.Anchor)) continue;
                    if (TheaterPlanner.SegmentDistance(e.X, e.Z, s.CapitalX, s.CapitalZ, s.EnemyCapitalX, s.EnemyCapitalZ) > band) continue;
                    onAxis += e.Mass * e.Weight;
                }
                into[k] = Math.Min(holdNeed[k], holdRatio * onAxis);
            }
        }

        // "[CoveringCorridor]" (State, first review and on change; the runtime dates it): every own-held town within R of either point as
        // "name:dForward:dRear:dOwn:dEnemy" (whole distances, straight; to the forward and rear points and to the own and the enemy capital),
        // joined by ';' and quoted as one value, no length cap; the forward and rear posts by name ("none" with no town); and the axis sectors.
        // Null when the side or the enemy has no capital: no axis, no corridor. The posts are whatever the rule gives; nothing is tuned to a name.
        internal static string CorridorRow(CampaignSnapshot s, SectorModel sectors, EnemyPicture picture, TheaterPlan plan, ITerrainLineProbe probe)
        {
            if (!s.HasCapital || !s.HasEnemyCapital) return null;
            var inv = CultureInfo.InvariantCulture;
            Points(s, out float fx, out float fz, out float rx, out float rz);
            float reachSq = s.RelocationRange * s.RelocationRange;
            var candidates = new StringBuilder();
            for (int i = 0; i < s.Places.Count; i++)
            {
                var place = s.Places[i];
                if (place.Owner != s.Alliance || IsCapital(s, in place)) continue;
                if (ReserveRules.DistanceSq(place.X, place.Z, fx, fz) > reachSq && ReserveRules.DistanceSq(place.X, place.Z, rx, rz) > reachSq) continue;
                if (candidates.Length > 0) candidates.Append(';');
                candidates.Append(place.Name).Append(':')
                    .Append(RiverLines.Distance(place.X, place.Z, fx, fz).ToString("0", inv)).Append(':')
                    .Append(RiverLines.Distance(place.X, place.Z, rx, rz).ToString("0", inv)).Append(':')
                    .Append(RiverLines.Distance(place.X, place.Z, s.CapitalX, s.CapitalZ).ToString("0", inv)).Append(':')
                    .Append(RiverLines.Distance(place.X, place.Z, s.EnemyCapitalX, s.EnemyCapitalZ).ToString("0", inv));
            }
            var need = new float[sectors.Count];
            for (int k = 0; k < plan.Sectors.Count; k++)
            {
                var sp = plan.Sectors[k];
                if (sp != null && sp.SectorId >= 0 && sp.SectorId < need.Length) need[sp.SectorId] = sp.HoldNeed;
            }
            var share = new float[sectors.Count];
            AxisShares(s, sectors, picture, need, TheaterPlanner.HoldRatioNow(s), probe, share);
            var axis = new StringBuilder();
            for (int k = 0; k < share.Length; k++)
            {
                if (share[k] <= 0f) continue;
                if (axis.Length > 0) axis.Append(',');
                axis.Append(k);
            }
            return "[CoveringCorridor] alliance=" + s.Alliance
                + " candidates=" + TelemetryLegacyParser.Quote(candidates.ToString())
                + " forward=" + NameOf(s, PostNear(s, fx, fz)) + " rear=" + NameOf(s, PostNear(s, rx, rz))
                + " axis=" + (axis.Length == 0 ? "none" : axis.ToString());
        }

        // The strength the force is sized to (D11 requirement-covering-size): the threat out of the enemy capital, HoldRatio' x the estimate there,
        // never below the live minimum strength, capped at what the axis sectors hold against on the axis (the sum of their shares). With no
        // axis sector (a sum of 0) there is no force and no need.
        internal static float Need(float estimateAtEnemyCapital, float holdRatio, float minStrength, float sumAxisShare)
        {
            if (sumAxisShare <= 0f) return 0f;
            return Math.Min(Math.Max(holdRatio * estimateAtEnemyCapital, minStrength), sumAxisShare);
        }

        // One review's verdict step (D11 requirement-covering-post-choice): what the review publishes (Published: forward or rear post; the
        // members' target and the Kind) and what the plan stores afterwards (Forward, Days, Known). forwardMet is the raw verdict: the counted
        // strength at the forward post reaches the need.
        //   - With one post (the forward and the rear point give the same town, or only one gives a town) there is no hysteresis: the raw
        //     verdict is the kind each review, the count stays 0, the stored verdict is written raw.
        //   - On the first review, or when the post key changed (a town changed hands, a capital moved, the force was unbound in between),
        //     the raw verdict is published and stored with the count restarted at 0.
        //   - Otherwise the stored verdict is published; a raw verdict equal to it resets the count, a different one adds a day and flips the
        //     STORED verdict at TheaterPlanner.SwitchDays (D4's hysteresis, the ChallengerDays pattern). The flip review still publishes the
        //     old verdict: the pick and the verdict never depend on each other within one review, and the new post applies from the next.
        internal static VerdictStep Verdict(bool forwardMet, string postKey, bool onePost, PlannerState state)
        {
            bool sameKey = state != null && state.CoveringVerdictKnown && string.Equals(state.CoveringPostKey, postKey, StringComparison.Ordinal);
            if (onePost || !sameKey)
                return new VerdictStep { Published = forwardMet, Forward = forwardMet, Days = 0, Known = true };
            if (forwardMet == state.CoveringForward)
                return new VerdictStep { Published = state.CoveringForward, Forward = state.CoveringForward, Days = 0, Known = true };
            int days = state.CoveringVerdictDays + 1;
            bool flips = days >= TheaterPlanner.SwitchDays;
            return new VerdictStep
            {
                Published = state.CoveringForward,
                Forward = flips ? forwardMet : state.CoveringForward,
                Days = flips ? 0 : days,
                Known = true
            };
        }

        internal struct VerdictStep
        {
            public bool Published, Forward, Known;
            public int Days;
        }

        // ---- the hold pass's input ----

        // The covering force's input to ClaimTable.Build for this pass, or null with the reason there is none. Reads the review's sector facts
        // (the hold needs), the picture (the axis shares and the enemy capital's estimate), the planner state (the stored verdict and the
        // incumbents) and the departments. The terrain-filling review plans twice: each pass prepares afresh, the state is written once after Decide.
        internal static CoveringInput Prepare(TheaterPlanner.Inputs c, out string reason)
        {
            var s = c.S;
            reason = null;
            if (!PlannerSettings.CoveringForce) { reason = ReasonFlagOff; return null; }
            if (ForceAllocator.StandsDown(s)) { reason = ReasonCicSide; return null; }
            if (!s.HasCapital || !s.HasEnemyCapital) { reason = ReasonNoCapital; return null; }
            Points(s, out float fx, out float fz, out float rx, out float rz);
            int forwardTown = PostNear(s, fx, fz), rearTown = PostNear(s, rx, rz);
            if (forwardTown < 0 && rearTown < 0) { reason = ReasonNoCorridorPlace; return null; }

            var share = new float[c.Sectors.Count];
            float holdRatio = TheaterPlanner.HoldRatioNow(s);
            AxisShares(s, c.Sectors, c.Picture, c.Facts.Need, holdRatio, c.Probe, share);
            float sum = 0f;
            for (int k = 0; k < share.Length; k++) sum += share[k];
            if (sum <= 0f) { reason = ReasonNoAxisSector; return null; }

            var input = new CoveringInput
            {
                Radius = s.RelocationRange,
                AxisShare = share,
                Need = Need(TheaterPlanner.EnemyCapitalEstimate(c), holdRatio, TheaterPlanner.MinStrength(c), sum),
                GaveUp = c.GaveUp,
                ForwardPlace = forwardTown,
                RearPlace = rearTown,
                OnePost = forwardTown < 0 || rearTown < 0 || forwardTown == rearTown,
                PostKey = PostKey(s, forwardTown, rearTown)
            };
            // With one post it is both; the forward post is the one the raw verdict counts at, the stored verdict's post the one members are ordered by.
            int forwardPost = forwardTown >= 0 ? forwardTown : rearTown, rearPost = rearTown >= 0 ? rearTown : forwardTown;
            input.ForwardX = s.Places[forwardPost].X;
            input.ForwardZ = s.Places[forwardPost].Z;
            var state = c.State;
            bool stored = state != null && state.CoveringVerdictKnown && string.Equals(state.CoveringPostKey, input.PostKey, StringComparison.Ordinal);
            int orderPost = input.OnePost || !stored || state.CoveringForward ? forwardPost : rearPost;
            input.OrderX = s.Places[orderPost].X;
            input.OrderZ = s.Places[orderPost].Z;
            input.PostTheater = c.Sectors.TheaterOf(c.Sectors.SectorAt(input.OrderX, input.OrderZ));

            int n = s.Own.Count;
            input.IncumbentSector = new int[n];
            input.IncumbentArea = new int[n];
            input.Department = new Theater[n];
            for (int i = 0; i < n; i++)
            {
                input.IncumbentSector[i] = -1;
                input.IncumbentArea[i] = -1;
                input.Department[i] = TheaterPlanner.DepartmentOf(c, i);
                if (state != null && s.Own[i].UnitKey != null && state.CoveringKeys.TryGetValue(s.Own[i].UnitKey, out int area))
                {
                    input.IncumbentArea[i] = area;
                    input.IncumbentSector[i] = c.Sectors.SectorOfArea(area);
                }
            }
            return input;
        }

        // The forward and rear towns by name and rounded position, "none" for a missing post: the key the stored verdict belongs to (a town that
        // changes hands or a capital that moves changes it).
        internal static string PostKey(CampaignSnapshot s, int forwardTown, int rearTown)
        {
            return KeyOf(s, forwardTown) + "|" + KeyOf(s, rearTown);
        }

        private static string KeyOf(CampaignSnapshot s, int town)
        {
            if (town < 0) return "none";
            var inv = CultureInfo.InvariantCulture;
            return s.Places[town].Name + "@" + ((int)Math.Round(s.Places[town].X)).ToString(inv) + "," + ((int)Math.Round(s.Places[town].Z)).ToString(inv);
        }

        // ---- the verdict and the lifecycle (called by TheaterPlanner) ----

        // Run once the hold pass has chosen the members: judges the raw verdict (the counted strength at the forward post reaches the need),
        // steps the stored verdict, and points every member at the post of the verdict the review publishes (the stored one, D11: on a flip the
        // new post applies from the next review). Held is the strength counted at that post.
        internal static void Settle(TheaterPlanner.Inputs c)
        {
            var cov = c.Covering;
            if (cov == null) return;
            var s = c.S;
            cov.MemberStrength = MemberStrengthSoFar(c.Claims, cov);
            float forwardCounted = cov.MemberStrength + cov.StandingNear(cov.ForwardX, cov.ForwardZ, cov.Radius);
            cov.Step = Verdict(forwardCounted >= cov.Need, cov.PostKey, cov.OnePost, c.State);
            cov.PublishedForward = cov.Step.Published;
            int forwardTown = cov.ForwardPlace >= 0 ? cov.ForwardPlace : cov.RearPlace, rearTown = cov.RearPlace >= 0 ? cov.RearPlace : cov.ForwardPlace;
            cov.PostTown = cov.PublishedForward ? forwardTown : rearTown;
            cov.PostX = s.Places[cov.PostTown].X;
            cov.PostZ = s.Places[cov.PostTown].Z;
            for (int k = 0; k < cov.Members.Count; k++)
            {
                c.Claims[cov.Members[k]].TargetX = cov.PostX;
                c.Claims[cov.Members[k]].TargetZ = cov.PostZ;
            }
            cov.Held = cov.MemberStrength + cov.StandingNear(cov.PostX, cov.PostZ, cov.Radius);
        }

        // The day the main effort is aimed at the enemy capital (D11 requirement-covering-hold: the one push that may take the force that covers
        // that road): the members the mass did not take become ordinary hold reserves where they stand, so nothing is sent to an unbound post.
        // Their share stays where the hold pass put it for that one review (accepted and pinned); the stored state is left untouched, so the
        // incumbents return when the push ends.
        internal static void LeaveBehind(TheaterPlanner.Inputs c, bool enemyCapitalPush)
        {
            var cov = c.Covering;
            if (cov == null || !enemyCapitalPush) return;
            for (int k = 0; k < cov.Members.Count; k++)
            {
                int i = cov.Members[k];
                if (c.Claims[i].Claimant != Claimant.Hold || !IsCoveringReason(c.Claims[i].Reason)) continue;   // the mass took it
                c.Claims[i].Reason = null;
                c.Claims[i].SectorId = ReserveRules.HoldSectorOf(c.Sectors, c.S.Own[i]);
                c.Claims[i].TargetX = c.S.Own[i].X;
                c.Claims[i].TargetZ = c.S.Own[i].Z;
            }
        }

        // The published covering force of this review (never null): the post of the verdict published, the members the table still gives
        // covering-force, and the defence flag. Kind None carries why.
        internal static CoveringPlan Publish(TheaterPlanner.Inputs c, bool enemyCapitalPush)
        {
            var plan = new CoveringPlan { Radius = c.S.RelocationRange };
            var cov = c.Covering;
            if (cov == null)
            {
                plan.Reason = c.CoveringReason;
                return plan;
            }
            if (enemyCapitalPush)
            {
                plan.Reason = ReasonEnemyCapitalPush;
                return plan;
            }
            plan.Kind = cov.PublishedForward ? CoveringKind.Forward : CoveringKind.Rear;
            plan.PostName = c.S.Places[cov.PostTown].Name;
            plan.PostX = cov.PostX;
            plan.PostZ = cov.PostZ;
            plan.Need = cov.Need;
            plan.Held = cov.Held;
            for (int k = 0; k < cov.Members.Count; k++)
            {
                int i = cov.Members[k];
                if (c.Claims[i].Claimant == Claimant.Hold && IsCoveringReason(c.Claims[i].Reason)) plan.InstanceIds.Add(c.S.Own[i].InstanceId);
            }
            plan.ThreatNear = ThreatNear(c.S, plan);
            return plan;
        }

        // Whether an advance into the side's territory stands near the post (D11 requirement-covering-hold): a live land belief of the side's
        // own, of any source (a mark is a strength reading, not a scouting), lies on own soil within R of the post. The enemy army at its own
        // capital does not open it. The one fact the defence clause reads: never the enemy position vanilla passes (BUG-AI-002, 13610).
        internal static bool ThreatNear(CampaignSnapshot s, CoveringPlan plan)
        {
            for (int i = 0; i < s.Beliefs.Count; i++)
            {
                var b = s.Beliefs[i];
                if (ReserveRules.Live(b) <= 0f || ReserveRules.Afloat(b) || (b.Traits & EnemyTraits.OnOwnSoil) == 0) continue;
                if (RiverLines.Distance(b.X, b.Z, plan.PostX, plan.PostZ) <= plan.Radius) return true;
            }
            return false;
        }

        // The planner state's covering fields, written once in Plan after Decide (D11 requirement-covering-post-choice). A posted day stores the
        // members and the incumbents briefly unavailable (each with the area it was recorded in) and the verdict step; a day on which the push
        // on the enemy capital took the force leaves everything untouched; any other day without a force writes the unbound post key.
        internal static void WriteState(TheaterPlanner.Inputs c, bool enemyCapitalPush, PlannerState state)
        {
            var cov = c.Covering;
            if (state == null || (cov != null && enemyCapitalPush)) return;
            if (cov == null)
            {
                state.CoveringPostKey = UnboundPostKey;
                return;
            }
            var keys = new List<KeyValuePair<string, int>>();
            for (int k = 0; k < cov.Members.Count; k++) AddKey(c, cov, cov.Members[k], keys, c.Claims[cov.Members[k]].SectorId);
            for (int k = 0; k < cov.Retained.Count; k++) AddKey(c, cov, cov.Retained[k], keys, cov.IncumbentSector[cov.Retained[k]]);
            state.SetCoveringKeys(keys);
            state.CoveringForward = cov.Step.Forward;
            state.CoveringVerdictDays = cov.Step.Days;
            state.CoveringVerdictKnown = cov.Step.Known;
            state.CoveringPostKey = cov.PostKey;
        }

        // A member keeps the area it was recorded in (an incumbent) or the area it stands in (a new one); one with no area is not remembered.
        private static void AddKey(TheaterPlanner.Inputs c, CoveringInput cov, int i, List<KeyValuePair<string, int>> keys, int sector)
        {
            string key = c.S.Own[i].UnitKey;
            if (string.IsNullOrEmpty(key)) return;
            int area = cov.IncumbentArea[i] >= 0 ? cov.IncumbentArea[i] : c.S.Own[i].AreaIndex;
            if (area < 0 || c.Sectors.SectorOfArea(area) != sector) return;
            keys.Add(new KeyValuePair<string, int>(key, area));
        }

        // Whether the claim is a covering member: a Hold claim of reason covering-force (a Mass claim the push on the enemy capital took from the
        // force has lost the reason).
        internal static bool IsMember(in Claim claim)
        {
            return claim.Claimant == Claimant.Hold && IsCoveringReason(claim.Reason);
        }

        private static bool IsCoveringReason(string reason)
        {
            return string.Equals(reason, ForceAllocator.ReasonCoveringForce, StringComparison.Ordinal);
        }

        // ---- the members (called by ReserveRules.Hold) ----

        // Run first in the hold pass: clears the pass's output, collects the formations that stand at the post and cannot be massed, and takes the
        // incumbents that are still claimable as members before any sector chooses its reserve. Returns the incumbents taken (by snapshot index):
        // the sector pass skips them where they stand and counts them in their recorded sector.
        //   An incumbent is taken when it is claimable (claimant None, MassEligible), no held depot builder of either kind and not given up on at
        //   the post, stands within R of the axis, and its recorded sector still has an axis share. Taken in recorded-sector order, within each
        //   sector's share (the body that crosses it is taken) and until the counted strength reaches the need. One that is only briefly
        //   unavailable (claimed Committed or Immobile, or failing only MassEligible) keeps its entry this review and counts only as the
        //   non-massable count counts it; one the sector no longer faces on the axis, or that another row took, is an ordinary candidate.
        internal static bool[] TakeIncumbents(CampaignSnapshot s, Claim[] claims, CoveringInput c, IReadOnlyList<HeldBuilder> held)
        {
            int n = s.Own.Count;
            c.Members.Clear();
            c.Retained.Clear();
            c.NonMassable.Clear();
            c.Seeded = new float[c.AxisShare.Length];
            c.Given = new float[c.AxisShare.Length];
            CollectStanding(s, claims, c, held);
            var taken = new bool[n];
            var queue = new List<int>();
            for (int i = 0; i < n; i++)
            {
                int k = c.IncumbentSector[i];
                if (k < 0 || k >= c.AxisShare.Length || c.AxisShare[k] <= 0f) continue;
                var f = s.Own[i];
                if (IsHeld(held, f.InstanceId) || GaveUpAtPost(c, f.InstanceId) || OffAxis(s, f)) continue;
                Claimant owner = claims[i].Claimant;
                if (owner == Claimant.None && TheaterPlanner.MassEligible(f, s.MinMoraleForMovement)) queue.Add(i);
                else if (owner == Claimant.Committed || owner == Claimant.Immobile || (owner == Claimant.None && !TheaterPlanner.MassEligible(f, s.MinMoraleForMovement)))
                    c.Retained.Add(i);
            }
            queue.Sort((a, b) =>
            {
                int bySector = c.IncumbentSector[a].CompareTo(c.IncumbentSector[b]);
                if (bySector != 0) return bySector;
                int byPost = PostDistance(s, c, a).CompareTo(PostDistance(s, c, b));
                return byPost != 0 ? byPost : s.Own[a].InstanceId.CompareTo(s.Own[b].InstanceId);
            });
            float counted = c.StandingNear(c.OrderX, c.OrderZ, c.Radius);
            for (int q = 0; q < queue.Count; q++)
            {
                int i = queue[q];
                int k = c.IncumbentSector[i];
                if (counted >= c.Need || c.Given[k] >= c.AxisShare[k]) continue;
                counted += Enlist(s, claims, c, i, k);
                c.Seeded[k] += claims[i].Share;
                taken[i] = true;
            }
            return taken;
        }

        // Run last in the hold pass, after every sector chose its reserve: the other members, across all the axis sectors' reserve bodies in one
        // order (distance to the post, the home department's discount applied), each sector giving at most its share (the body that crosses it is
        // taken) until the counted strength reaches the need. The map-group player counts at the post but is never the only holder: with no member
        // taken and he counted there, the first reserve body of the axis sectors is added even though the need is met.
        internal static void PickMembers(CampaignSnapshot s, Claim[] claims, CoveringInput c, IReadOnlyList<HeldBuilder> held)
        {
            var bodies = new List<int>();
            for (int i = 0; i < claims.Length; i++)
            {
                int k = claims[i].SectorId;
                if (claims[i].Claimant != Claimant.Hold || claims[i].Reason != null || k < 0 || k >= c.AxisShare.Length || c.AxisShare[k] <= 0f) continue;
                if (GaveUpAtPost(c, s.Own[i].InstanceId)) continue;
                bodies.Add(i);
            }
            SortByPost(s, c, bodies);
            float counted = c.StandingNear(c.OrderX, c.OrderZ, c.Radius) + MemberStrengthSoFar(claims, c);
            for (int q = 0; q < bodies.Count && counted < c.Need; q++)
            {
                int i = bodies[q];
                int k = claims[i].SectorId;
                if (c.Given[k] >= c.AxisShare[k]) continue;
                counted += Enlist(s, claims, c, i, k);
            }
            if (c.Members.Count == 0 && MapGroupPlayerAtPost(c))
                for (int q = 0; q < bodies.Count; q++)
                    if (claims[bodies[q]].Reason == null)
                    {
                        Enlist(s, claims, c, bodies[q], claims[bodies[q]].SectorId);
                        break;
                    }
        }

        // Indexes into s.Own ordered by distance to the force's post (the home department's discount applied), the instance id breaking ties.
        internal static void SortByPost(CampaignSnapshot s, CoveringInput c, List<int> order)
        {
            order.Sort((a, b) =>
            {
                int byPost = PostDistance(s, c, a).CompareTo(PostDistance(s, c, b));
                return byPost != 0 ? byPost : s.Own[a].InstanceId.CompareTo(s.Own[b].InstanceId);
            });
        }

        // Makes formation i a member counted in sector k: Hold, reason covering-force, its strength given to the sector. Returns the strength.
        private static float Enlist(CampaignSnapshot s, Claim[] claims, CoveringInput c, int i, int k)
        {
            float strength = ReserveRules.HoldStrength(s.Own[i]);
            claims[i].Claimant = Claimant.Hold;
            claims[i].Reason = ForceAllocator.ReasonCoveringForce;
            claims[i].SectorId = k;
            claims[i].Share = strength;
            claims[i].TargetX = c.OrderX;
            claims[i].TargetZ = c.OrderZ;
            c.Members.Add(i);
            c.Given[k] += strength;
            return strength;
        }

        private static float MemberStrengthSoFar(Claim[] claims, CoveringInput c)
        {
            float sum = 0f;
            for (int k = 0; k < c.Members.Count; k++) sum += claims[c.Members[k]].Share;
            return sum;
        }

        // The formations that count at the post without being massable: row 7's counted-anyway predicate over the claims, less its Capital term
        // (so a staged push never fills the force): claimed None, Immobile or PinnedBuilder, refused by MassEligible, not recovering, counted
        // toward coverage, and no depot builder still on its way (it counts nowhere).
        private static void CollectStanding(CampaignSnapshot s, Claim[] claims, CoveringInput c, IReadOnlyList<HeldBuilder> held)
        {
            for (int i = 0; i < claims.Length; i++)
            {
                var f = s.Own[i];
                Claimant owner = claims[i].Claimant;
                if (owner != Claimant.None && owner != Claimant.Immobile && owner != Claimant.PinnedBuilder) continue;
                if (TheaterPlanner.MassEligible(f, s.MinMoraleForMovement) || ReserveRules.Recovers(f, s.MinMoraleForMovement)) continue;
                if (!WlChainRules.CountsTowardCoverage(f.Chain) || ReserveRules.IsMarchingBuilder(held, f.InstanceId)) continue;
                ReserveRules.HoldPoint(f, out float x, out float z);
                c.NonMassable.Add(new CoveringInput.Standing { Strength = ReserveRules.HoldStrength(f), X = x, Z = z, MapGroupPlayer = f.Chain == WlChainClass.PlayerMapGroup });
            }
        }

        private static bool MapGroupPlayerAtPost(CoveringInput c)
        {
            float reachSq = c.Radius * c.Radius;
            for (int k = 0; k < c.NonMassable.Count; k++)
                if (c.NonMassable[k].MapGroupPlayer && ReserveRules.DistanceSq(c.NonMassable[k].X, c.NonMassable[k].Z, c.OrderX, c.OrderZ) <= reachSq) return true;
            return false;
        }

        // Distance to the post, times 1 - HomeDepartmentDiscount for a formation whose home department is the post sector's theater (as the
        // allocator's HoldKey does for the army a sector faces).
        private static float PostDistance(CampaignSnapshot s, CoveringInput c, int i)
        {
            float d = RiverLines.Distance(s.Own[i].X, s.Own[i].Z, c.OrderX, c.OrderZ);
            if (c.PostTheater != Theater.Unknown && c.Department[i] == c.PostTheater) d *= 1f - ForceAllocator.HomeDepartmentDiscount;
            return d;
        }

        private static bool OffAxis(CampaignSnapshot s, in FormationRecord f)
        {
            return TheaterPlanner.SegmentDistance(f.X, f.Z, s.CapitalX, s.CapitalZ, s.EnemyCapitalX, s.EnemyCapitalZ) > s.RelocationRange;
        }

        private static bool IsHeld(IReadOnlyList<HeldBuilder> held, int instanceId)
        {
            if (held == null) return false;
            for (int k = 0; k < held.Count; k++)
                if (held[k].InstanceId == instanceId) return true;
            return false;
        }

        // The tracker gave up on sending the formation to the post (a new target starts afresh).
        private static bool GaveUpAtPost(CoveringInput c, int instanceId)
        {
            var list = c.GaveUp;
            if (list == null) return false;
            for (int k = 0; k < list.Count; k++)
                if (list[k].InstanceId == instanceId && MoveTracker.SameTarget(list[k].TargetX, list[k].TargetZ, c.OrderX, c.OrderZ)) return true;
            return false;
        }

        // "[CoveringForce]" (State, on change; the runtime dates it): the post (quoted, "none" with no force), the kind (forward, rear or none)
        // and why there is none, the need and the counted strength, the members' unit keys joined by '+' and quoted as one value, what the
        // axis sectors could not give (short), the defence flag and the verdict's count. The unit keys are the ones [CampaignMoverIssue] names, so
        // the smoke joins the two.
        internal static string Row(CoveringPlan p, CampaignSnapshot s, PlannerState state)
        {
            var inv = CultureInfo.InvariantCulture;
            var members = new StringBuilder();
            for (int k = 0; k < p.InstanceIds.Count; k++)
            {
                int i = s.IndexOfOwn(p.InstanceIds[k]);
                if (i < 0) continue;
                if (members.Length > 0) members.Append('+');
                members.Append(s.Own[i].UnitKey ?? p.InstanceIds[k].ToString(inv));
            }
            string kind;
            switch (p.Kind)
            {
                case CoveringKind.Forward: kind = "forward"; break;
                case CoveringKind.Rear: kind = "rear"; break;
                case CoveringKind.None:
                default: kind = "none"; break;
            }
            return "[CoveringForce] alliance=" + s.Alliance
                + " post=" + TelemetryLegacyParser.Quote(p.PostName ?? "none")
                + " kind=" + kind
                + " reason=" + (p.Reason ?? "-")
                + " need=" + p.Need.ToString("0", inv) + " held=" + p.Held.ToString("0", inv)
                + " members=" + TelemetryLegacyParser.Quote(members.ToString())
                + " short=" + Math.Max(0f, p.Need - p.Held).ToString("0", inv)
                + " threatNear=" + (p.ThreatNear ? 1 : 0)
                + " verdictDays=" + (state != null ? state.CoveringVerdictDays : 0);
        }

        private static string NameOf(CampaignSnapshot s, int place)
        {
            return place < 0 ? "none" : TelemetryLegacyParser.Quote(s.Places[place].Name);
        }
    }
}
