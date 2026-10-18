using System;
using System.Collections.Generic;
using System.Globalization;

namespace WhiskeyRealism.Strategic.Planner
{
    // The force allocation of one AI alliance for one daily review (spec component 6; plan G4): every
    // own campaign formation gets exactly one Assignment (role, sector, target, reason, whether it is
    // withheld from vanilla's offensive pool). Pure: snapshot, sectors and TheaterPlan in, Allocation
    // out; no randomness, no clock, no static state (determinism is tested sliced and unsliced).
    //
    // Fog-honest: the only enemy inputs are the plan's published numbers (SectorPlan.HoldNeed, the
    // mass, the capital floor) and snapshot beliefs. Vanilla's busy lists are read from the
    // snapshot's flags, never from vanilla.
    //
    // Roles, in the order they are handed out:
    //   pinned        the builder of a Wished depot task (DepotTaskLedger.CollectHeldBuilders, the Pinned input): DepotBuilder at the
    //                 wish position, withheld, until the task is Built or lost, whatever the plan does with the task (G5c verify r3 F7).
    //                 Handed out before everything else; never a formation the planner may not move.
    //   immobile      Hold in place, never moved: a formation that is busy (battle, retreat, raid,
    //                 sea invasion, embarked, fort or depot builder, in transfer), not a combat force,
    //                 or the W&L player's own command or CIC side (stage F's chain classes through
    //                 WlChainRules; spec C4).
    //   CapitalGuard  the capital reserve G3 set aside before massing (ReserveRules.Capital over the
    //                 plan's CapitalFloorStrength): the massable formations nearest the capital.
    //                 Held at the capital, withheld from vanilla's pools.
    //   Hold          the hold reserve G3 set aside per threatened sector (ReserveRules.Hold over
    //                 each SectorPlan.HoldNeed, never recomputed: G3 verify F9), less what G3 released
    //                 into the mass, plus the remainder (below).
    //   Recover       a movable formation below RecoverSupplyBelow raw supply or the movement morale
    //                 minimum (spec C21: ReserveRules.Recovers), walked toward the nearest supply
    //                 source; withheld. It counts 0 toward its sector's hold (it is leaving).
    //   DepotBuilder  the smallest fighting formation in a depot task's sector; withheld.
    //   Recon         ReconPlanner.PickFormation per recon task, highest priority first; the Recon
    //                 assignment copies the task's SectorId and hold point so ReconMissionLedger.Sync
    //                 can pair them back (G1b contract); withheld. A staged main effort with no free scout draws one from a sector's hold
    //                 reserve (never a secession pledge; never one that would leave a sector holding against a believed army short: see
    //                 SectorMayGiveScout), once per main effort across reviews.
    //   MainEffort    exactly TheaterPlan.MassInstanceIds (G3 judged the odds on that set), less any
    //                 formation a Recon or DepotBuilder task took from it because no formation outside
    //                 the mass qualified, and less the Reserve below. Target = the main sector's target.
    //   Reserve       force above ReserveSurplusMargin x the required force at the main objective (the
    //                 plan's MainRequiredRatio x TheaterPlan.MainEnemyStrength, the believed enemy G3
    //                 judged the odds against at the objective itself, not at the clamped frontier
    //                 point in SectorPlan.TargetX/Z), farthest from that gather point first, stopping
    //                 at the first formation the main effort cannot spare; withheld. None when no
    //                 enemy is believed at the objective.
    //   Support       the remainder in a Support (or the main) sector: toward the main target.
    //   Garrison      both sorts of garrison post, under the one role and the one withhold (plan G8 on top of D8). D8's secession members are
    //                 seated early (AssignGarrisons: TheaterPlanner pre-picked them after the mass); an ordinary post's incumbent is claimed
    //                 right after them (ClaimPostIncumbents), ahead of the depot and recon steps; the rest of each ordinary post's need
    //                 (TheaterPlan.Garrisons, Kind other than Secession) is seated once the reserve split is known (AssignGarrisonPosts), from
    //                 the formations that would otherwise just hold and the Reserve surplus, never the mass, at most two formations a post.
    //                 A member marches as Garrison, withheld; at its post it holds in place, withheld (steering centres its theater box on a
    //                 Hold formation). Allocation.Seats says who holds which.
    //   Hold (rest)   the remainder anywhere else, Hold in place (EconomyOfForce garrison).
    //   Withheld      a sector's Hold formations are ordered by distance to the sector's nearest
    //                 believed land enemy (centroid when none), with a HomeDepartmentDiscount for
    //                 a formation whose home department (PlannerState, D6 detail) is the sector's theater; the shortest prefix whose
    //                 strength reaches max(HoldNeed, MinGarrisonStrength) is withheld from vanilla's
    //                 offensive gather (CheckOffensiveMovements 14257-14275), the rest is not.
    //   Gave up       a formation the move tracker gave up on at the target its role would send it to (P8: two misses,
    //                 MoveTracker.GaveUp) loses that role: the depot and recon picks take the next candidate, any other
    //                 role becomes Hold in place with ReasonGaveUp, until the role's target changes. The set is an
    //                 input (GaveUpMove), so the pass stays pure and deterministic.
    //   Player        CarryingGroup (a player below map level) is an ordinary movable formation (the
    //                 player it carries is sent no order). The map-group player's group counts
    //                 toward the hold of the sector where its last waypoint lies, at strength x morale,
    //                 as vanilla counts it (WlChainRules.CountsTowardCoverage; integrator decision
    //                 2026-10-15): the AI formations that would hold that sector are released while he
    //                 stands in it, and the sector reports its shortfall (and keeps or gets an AI
    //                 holder) the day he marches away. Counting is not committing: he is never mass,
    //                 capital guard, Recover, depot builder or reserve, never withheld, never a
    //                 surplus; his sector's role reaches him as an order only (Support toward the main
    //                 target in a Support or main sector, otherwise Hold in place). The player's own
    //                 command and a CIC side count nothing. A CIC player's own side gets an empty
    //                 allocation. The reliability and the run of unreliable days are telemetry and
    //                 expectations (the [Allocation] row), not inputs.
    // Shortfalls list the formations that carry an unmet need (the main effort below its required
    // force, the capital guard below the floor, a Hold sector below its target), worst first: an AI
    // formation, except that a threatened sector no AI formation holds names the map-group player's
    // formation holding there as the position (G5b never routes to a player formation); Surpluses the Reserve and the EconomyOfForce Hold formations beyond their
    // garrison, largest first. G5b routes recruits and brigade transfers from one to the other.
    public static class ForceAllocator
    {
        public const float MinGarrisonStrength = 5000f;
        public const float UnreliableBelow = 0.5f; // telemetry: the [Allocation] row's run of unreliable days
        // D6 detail (c): a formation whose home department is the sector's theater is treated as this much closer to the sector's army
        // (0.05 when the home was only a historical hint; the department is now the planner's own stable assignment).
        public const float HomeDepartmentDiscount = 0.25f;
        public const float ReserveSurplusMargin = 1.3f;
        // Smoke 2026-10-17: the most scouts a staged main effort draws from the sectors' hold reserves in one review. One pair of eyes
        // answers its question (the picture of the objective); more would turn the presumption's hold into a screen.
        internal const int MaxReserveScouts = 1;
        // Allocation.ReconUnfilledWhy: the hold reserve could have given a scout, and every candidate was refused because its sector
        // holds against a believed enemy force that the draw would leave uncovered.
        internal const string ReasonScoutAgainstThreat = "believed-threat";
        // Allocation.ReconUnfilledWhy: why a recon task has no scout (smoke 2026-10-18: tasks=6 unfilled=6 why=- for twelve days). The first that applies:
        //   believed-threat   the draw from the hold reserve was refused (above).
        //   mass-kept         the only formation the pick found was the main effort's last body, which it keeps.
        //   unsuited          a formation could go and nothing claimed it, but the kind's own rules turned it down (an advance guard or a probe
        //                     bigger than a quarter of the main body, a picket whose sector would be left short, a cavalry group the main body keeps).
        //   claimed-<what>    every formation that could go already has a role (what: the hold reserve's reason, or the role) before the recon step.
        //   gave-up           the able formations all gave up on this hold point.
        //   none-able         no formation meets the kind's movement standard at all (too weak for a screen, unready, short of morale, busy, in a
        //                     vanilla operations list, or not the planner's to order).
        internal const string ReasonUnfilledMassKept = "mass-kept", ReasonUnfilledUnsuited = "unsuited", ReasonUnfilledGaveUp = "gave-up",
                              ReasonUnfilledNoneAble = "none-able", ReasonUnfilledClaimedPrefix = "claimed-";

        internal const string ReasonBusy = "busy";
        internal const string ReasonPlayerCommand = "player-command";
        internal const string ReasonNonCombat = "non-combat";
        internal const string ReasonCapitalFloor = "capital-floor";
        internal const string ReasonHoldReserve = "hold-reserve";
        internal const string ReasonRecover = "recover";
        internal const string ReasonDepotTask = "depot-task";
        internal const string ReasonMass = "mass";
        internal const string ReasonReserve = "reserve-surplus";
        internal const string ReasonReinforcement = "reinforcement";
        internal const string ReasonGarrison = "garrison";
        internal const string ReasonSecession = "secession-garrison";
        internal const string ReasonGarrisonPost = "garrison-post";
        internal const string ReasonPlayerMapGroup = "player-map-group";
        internal const string ReasonGaveUp = "gave-up";

        public static Allocation Allocate(TheaterPlan plan, CampaignSnapshot s, SectorModel sectors, Func<string, Theater> homeDepartment,
                                          IReadOnlyList<ReconTask> reconTasks, ReconMissionLedger missions, IReadOnlyList<GaveUpMove> gaveUp = null,
                                          IReadOnlyList<HeldBuilder> heldBuilders = null)
        {
            var allocation = new Allocation { Alliance = s != null ? s.Alliance : -1 };
            if (s == null || plan == null || sectors == null || s.Alliance < 0 || s.Alliance > 1 || s.Own.Count == 0) return allocation;
            if (StandsDown(s)) return allocation;
            new Pass(plan, s, sectors, homeDepartment, reconTasks, missions, gaveUp, heldBuilders).Run(allocation);
            return allocation;
        }

        // The CIC player's own side: every formation is his (stage F's CicSide). The enemy side of a
        // CIC career keeps its planner (Chain None), though the snapshot's Player class is global.
        internal static bool StandsDown(CampaignSnapshot s)
        {
            if (s.Player != PlayerCommandClass.CommanderInChief) return false;
            for (int i = 0; i < s.Own.Count; i++)
                if (s.Own[i].Chain == WlChainClass.CicSide) return true;
            return false;
        }

        // Telemetry only (the [Allocation] row): one count per review day of consecutive unreliability; a day that goes backwards (a load)
        // restarts it. Runtime state, per alliance, in CampaignPlannerRuntime.
        public sealed class PlayerNonCompliance
        {
            private int _lastDay = int.MinValue;
            public int Days { get; private set; }

            public void Observe(int daySerial, bool playerMapGroupPresent, float reliability01)
            {
                if (daySerial == _lastDay) return;
                if (daySerial < _lastDay) Days = 0;
                _lastDay = daySerial;
                if (playerMapGroupPresent && reliability01 < UnreliableBelow) Days++;
                else Days = 0;
            }

            public int LastDay => _lastDay;

            public void Reset()
            {
                _lastDay = int.MinValue;
                Days = 0;
            }

            // The sidecar's run: restored with its last review day, so the first review after a load neither counts that day twice
            // nor restarts the run (a day that goes backwards still does, Observe).
            public void Restore(int days, int lastDay)
            {
                Days = days < 0 ? 0 : days;
                _lastDay = lastDay;
            }
        }

        // The [Allocation] row (plan G4 Steps), changing only when the allocation does.
        internal static string Summary(Allocation a, TheaterPlan plan, PlayerCommandClass player, float reliability01, int unreliableDays)
        {
            int main = 0, support = 0, hold = 0, reserve = 0, capital = 0, depot = 0, recover = 0, recon = 0, busy = 0;
            for (int i = 0; i < a.Assignments.Count; i++)
            {
                var x = a.Assignments[i];
                switch (x.Role)
                {
                    case FormationRole.MainEffort: main++; break;
                    case FormationRole.Support: support++; break;
                    case FormationRole.Reserve: reserve++; break;
                    case FormationRole.CapitalGuard: capital++; break;
                    case FormationRole.DepotBuilder: depot++; break;
                    case FormationRole.Recover: recover++; break;
                    case FormationRole.Recon: recon++; break;
                    default:
                        if (IsImmobileReason(x.Reason)) busy++;
                        else hold++;
                        break;
                }
            }
            return "[Allocation] alliance=" + a.Alliance
                + " main=" + main + " support=" + support + " hold=" + hold + " reserve=" + reserve + " capital=" + capital
                + " depot=" + depot + " recover=" + recover + " recon=" + recon + " busy=" + busy
                + " player=" + player
                + " reliability=" + reliability01.ToString("0.00", CultureInfo.InvariantCulture)
                + " unreliableDays=" + unreliableDays
                + " mass=" + (plan != null ? plan.MassStrength : 0f).ToString("0", CultureInfo.InvariantCulture)
                + " need=" + (plan != null ? plan.MainNeedStrength : 0f).ToString("0", CultureInfo.InvariantCulture)
                + " cap=" + (plan != null ? plan.MainCapacityStrength : 0f).ToString("0", CultureInfo.InvariantCulture)
                + " staged=" + (plan != null && plan.MainStaged ? "yes" : "no")
                + " garrison=" + GarrisonText(plan)
                + " short=" + a.Shortfalls.Count + " surplus=" + a.Surpluses.Count;
        }

        // D8: the secession garrisons, "<unit>@<State>:<in|moving>[+n]" joined by ';', or "none" while no live secession state of ours has one.
        // The ordinary posts (G8) have their own rows ([Garrison], [GarrisonPost]) and are not in this field.
        internal static string GarrisonText(TheaterPlan plan)
        {
            if (plan == null) return "none";
            var sb = new System.Text.StringBuilder();
            for (int k = 0; k < plan.Garrisons.Count; k++)
            {
                var g = plan.Garrisons[k];
                if (g.Kind != GarrisonPostKind.Secession) continue;
                if (sb.Length > 0) sb.Append(';');
                sb.Append(g.InstanceIds.Count > 0 ? (g.Name ?? "?").Replace(' ', '_') : g.MassPresence ? "mass-presence" : g.PlayerPresence ? "player-presence" : g.InState ? "town-garrison" : "nobody")
                  .Append('@').Append(g.StateName)
                  .Append(g.Marching ? (g.InState ? ":in+moving/" : ":moving/") : ":in/").Append(g.Need.ToString("0", CultureInfo.InvariantCulture));
                if (g.Shortfall > 0f) sb.Append("!short").Append(g.Shortfall.ToString("0", CultureInfo.InvariantCulture));
                if (g.InstanceIds.Count > 1) sb.Append('+').Append(g.InstanceIds.Count - 1);
            }
            return sb.Length == 0 ? "none" : sb.ToString();
        }

        private static bool IsImmobileReason(string reason)
        {
            return ReferenceEquals(reason, ReasonBusy) || ReferenceEquals(reason, ReasonPlayerCommand) || ReferenceEquals(reason, ReasonNonCombat);
        }

        // ---- the pass ----

        private sealed class Pass
        {
            private readonly TheaterPlan _plan;
            private readonly CampaignSnapshot _s;
            private readonly SectorModel _sectors;
            private readonly Func<string, Theater> _home;
            private readonly IReadOnlyList<ReconTask> _recon;
            private readonly ReconMissionLedger _missions;
            private readonly int _n;
            private readonly int _sectorCount;
            private readonly IReadOnlyList<GaveUpMove> _gaveUp;
            private readonly IReadOnlyList<HeldBuilder> _pinned;
            private bool[] _pinnedMask;   // the formations AssignPinnedBuilders pinned

            private readonly bool[] _done;
            private readonly bool[] _inMass;
            private readonly FormationRole[] _role;
            private readonly int[] _sector;    // the sector the formation stands in; the map-group player's is the sector his last waypoint lies in (HoldSectorOf)
            private readonly int[] _roleSector;
            private readonly float[] _tx, _tz;
            private readonly string[] _reason;
            private readonly bool[] _withheld;
            private readonly List<GarrisonSeat> _seats = new List<GarrisonSeat>();   // who holds which garrison post (either sort)
            private float[] _seatedStrength = new float[0];   // per post of the plan: the strength seated at it so far
            private int[] _seatedCount = new int[0];

            private readonly float[] _need, _nearX, _nearZ;  // per sector: hold need, the army it faces
            private readonly float[] _believed;              // per sector: the believed term of the hold need (SectorPlan.BelievedHold)
            private readonly ReserveRules.HoldTally _held;   // what ReserveRules.Hold sized each sector's reserve on (unready, busy, capital guard, reserve)
            private bool _scoutRefusedForThreat;
            private int _reconFree;
            private readonly List<string> _unfilledWhy = new List<string>();   // one entry per recon task with no scout and no refusal, in task order

            internal Pass(TheaterPlan plan, CampaignSnapshot s, SectorModel sectors,
                          Func<string, Theater> home, IReadOnlyList<ReconTask> recon, ReconMissionLedger missions, IReadOnlyList<GaveUpMove> gaveUp,
                          IReadOnlyList<HeldBuilder> pinned)
            {
                _gaveUp = gaveUp;
                _pinned = pinned;
                _plan = plan;
                _s = s;
                _sectors = sectors;
                _home = home;
                _recon = recon;
                _missions = missions;
                _n = s.Own.Count;
                _sectorCount = sectors.Count;
                _done = new bool[_n];
                _inMass = new bool[_n];
                _role = new FormationRole[_n];
                _sector = new int[_n];
                _roleSector = new int[_n];
                _tx = new float[_n];
                _tz = new float[_n];
                _reason = new string[_n];
                _withheld = new bool[_n];
                _need = new float[_sectorCount];
                _nearX = new float[_sectorCount];
                _nearZ = new float[_sectorCount];
                _believed = new float[_sectorCount];
                _held = new ReserveRules.HoldTally(_sectorCount, _n);

                for (int i = 0; i < _n; i++)
                {
                    _sector[i] = ReserveRules.HoldSectorOf(sectors, s.Own[i]); // the map-group player's group is where its last waypoint lies
                }
            }

            internal void Run(Allocation allocation)
            {
                ReadSectorFacts();
                var holdReserved = new int[_n];
                var capitalReserved = new bool[_n];

                var pinnedBuilder = AssignPinnedBuilders();
                _pinnedMask = pinnedBuilder;
                AssignImmobile();
                ReserveRules.Capital(_s, _plan.CapitalFloorStrength, capitalReserved, pinnedBuilder);
                ReserveRules.Hold(_s, _sectors, _need, _nearX, _nearZ, capitalReserved, holdReserved, _held);
                MarkMass();
                AssignCapitalGuards(capitalReserved);
                AssignHoldReserve(holdReserved);
                AssignRecover();
                AssignGarrisons();
                _seatedStrength = new float[_plan.Garrisons.Count];
                _seatedCount = new int[_plan.Garrisons.Count];
                ClaimPostIncumbents();
                AssignDepotBuilders();
                AssignRecon();
                SplitReserve(out float mainRequired, out float mainStrength);
                AssignGarrisonPosts();
                AssignMainEffort();
                AssignRemainder();
                WithholdHoldPrefixes();

                for (int i = 0; i < _n; i++)
                {
                    allocation.Assignments.Add(new Assignment
                    {
                        InstanceId = _s.Own[i].InstanceId,
                        Role = _role[i],
                        SectorId = _roleSector[i],
                        TargetX = _tx[i],
                        TargetZ = _tz[i],
                        Reason = _reason[i],
                        Withheld = _withheld[i]
                    });
                }
                allocation.Seats.AddRange(_seats);
                ListShortfalls(allocation, mainRequired, mainStrength);
                ListSurpluses(allocation);
                allocation.ReconUnfilledWhy = UnfilledWhy();
                allocation.ReconFree = _reconFree;
            }

            // ---- inputs ----

            private void ReadSectorFacts()
            {
                for (int k = 0; k < _plan.Sectors.Count; k++)
                {
                    var sp = _plan.Sectors[k];
                    if (sp == null || sp.SectorId < 0 || sp.SectorId >= _sectorCount) continue;
                    _need[sp.SectorId] = sp.HoldNeed;
                    _believed[sp.SectorId] = sp.BelievedHold;
                }
                for (int k = 0; k < _sectorCount; k++)
                {
                    _sectors.Centroid(k, out float cx, out float cz);
                    int near = ReserveRules.NearestLandBelief(_s, cx, cz);
                    _nearX[k] = near >= 0 ? _s.Beliefs[near].X : cx;
                    _nearZ[k] = near >= 0 ? _s.Beliefs[near].Z : cz;
                }
            }

            private void MarkMass()
            {
                for (int k = 0; k < _plan.MassInstanceIds.Count; k++)
                {
                    int i = _s.IndexOfOwn(_plan.MassInstanceIds[k]);
                    if (i >= 0 && !_done[i]) _inMass[i] = true;
                }
            }

            // ---- roles ----

            // Whether the tracker gave up on sending formation i to (tx, tz).
            private bool GaveUpAt(int i, float tx, float tz)
            {
                if (_gaveUp == null) return false;
                int id = _s.Own[i].InstanceId;
                for (int k = 0; k < _gaveUp.Count; k++)
                    if (_gaveUp[k].InstanceId == id && MoveTracker.SameTarget(_gaveUp[k].TargetX, _gaveUp[k].TargetZ, tx, tz)) return true;
                return false;
            }

            private static bool Marches(FormationRole role)
            {
                return role == FormationRole.MainEffort || role == FormationRole.Support || role == FormationRole.CapitalGuard
                    || role == FormationRole.Recover || role == FormationRole.DepotBuilder || role == FormationRole.Recon || role == FormationRole.Garrison;
            }

            private void Assign(int i, FormationRole role, int roleSector, float tx, float tz, string reason, bool withheld)
            {
                if (Marches(role) && GaveUpAt(i, tx, tz))
                {
                    role = FormationRole.Hold;
                    roleSector = _sector[i];
                    tx = _s.Own[i].X;
                    tz = _s.Own[i].Z;
                    reason = ReasonGaveUp;
                    withheld = false;
                }
                _done[i] = true;
                _inMass[i] = false;
                _role[i] = role;
                _roleSector[i] = roleSector;
                _tx[i] = tx;
                _tz[i] = tz;
                _reason[i] = reason;
                _withheld[i] = withheld;
            }

            // The builder of a Wished depot task keeps its role whatever the plan does (verify r3 F7): the wish is paid and the depot
            // rises only while the builder stays within its bugle range (96626-96648), so the formation is out of the free pool and
            // stays DepotBuilder at the wish position (the site, for an Ahead wish whose builder is still marching to it) until the
            // ledger stops listing it. Handed out first: nothing else, the mass and the reserves included, may take it. The one
            // place this rule lives; the mover reads the assignment like any other. A formation the planner may not move (the W&L
            // player's, a CIC side's) is never a builder, so a pin that names one is ignored. The returned mask keeps the capital
            // reserve (ReserveRules.Capital, which picks the nearest massable formations until the floor) from counting a pinned builder
            // as a guard it can raise; the hold reserve needs none, a pinned builder counts at its strength there either way.
            private bool[] AssignPinnedBuilders()
            {
                var mask = ReserveRules.WishedBuilders(_s, _pinned);   // the one rule for who is pinned: G3's plan reads it too
                if (mask == null) return null;
                for (int k = 0; k < _pinned.Count; k++)
                {
                    int i = _s.IndexOfOwn(_pinned[k].InstanceId);
                    if (i < 0 || !mask[i] || _done[i]) continue;
                    Assign(i, FormationRole.DepotBuilder, _pinned[k].SectorId, _pinned[k].TargetX, _pinned[k].TargetZ, ReasonDepotTask, true);
                }
                return mask;
            }

            private void AssignImmobile()
            {
                for (int i = 0; i < _n; i++)
                {
                    var f = _s.Own[i];
                    if (TheaterPlanner.Movable(f)) continue;
                    string reason = (f.Flags & TheaterPlanner.Busy) != 0 ? ReasonBusy
                        : (f.Flags & FormationFlags.FightingForce) == 0 ? ReasonNonCombat
                        : ReasonPlayerCommand;
                    Assign(i, FormationRole.Hold, _sector[i], f.X, f.Z, reason, false);
                }
            }

            private void AssignCapitalGuards(bool[] capitalReserved)
            {
                int capitalSector = _s.HasCapital ? _sectors.SectorAt(_s.CapitalX, _s.CapitalZ) : -1;
                for (int i = 0; i < _n; i++)
                    if (capitalReserved[i] && !_done[i])
                        Assign(i, FormationRole.CapitalGuard, capitalSector, _s.CapitalX, _s.CapitalZ, ReasonCapitalFloor, true);
            }

            private void AssignHoldReserve(int[] holdReserved)
            {
                for (int i = 0; i < _n; i++)
                    if (holdReserved[i] >= 0 && !_done[i] && !_inMass[i])
                        Assign(i, FormationRole.Hold, holdReserved[i], _s.Own[i].X, _s.Own[i].Z, ReasonHoldReserve, false);
            }

            // D8: the secession garrison the plan names (TheaterPlan.Garrisons, picked by TheaterPlanner.Garrisons from the very same snapshot):
            // a member already standing in the state holds there, withheld from vanilla's offence and defence pools so the state keeps a
            // visible group of ours (vanilla's alliancetroops, 81654-81660); a member from outside marches to the post, the objective's
            // town. A formation another role already took (the capital guard, a hold reserve, a recover) keeps that role: the post still
            // names it, since a hold reserve inside the state denies it just the same.
            private void AssignGarrisons()
            {
                for (int g = 0; g < _plan.Garrisons.Count; g++)
                {
                    var post = _plan.Garrisons[g];
                    if (post.Kind != GarrisonPostKind.Secession) continue;   // the ordinary posts are seated later, once the reserve split is known
                    int postSector = _sectors.SectorAt(post.X, post.Z);
                    for (int k = 0; k < post.InstanceIds.Count; k++)
                    {
                        int i = _s.IndexOfOwn(post.InstanceIds[k]);
                        if (i < 0 || _done[i] || !WlChainRules.MayMove(_s.Own[i].Chain) || !TheaterPlanner.Movable(_s.Own[i])) continue;
                        var f = _s.Own[i];
                        bool inState = f.StateId == post.StateId;
                        if (inState)
                            Assign(i, FormationRole.Hold, _sector[i], f.X, f.Z, ReasonSecession, true);
                        else
                            Assign(i, FormationRole.Garrison, postSector, post.X, post.Z, ReasonSecession, true);
                        if (ReferenceEquals(_reason[i], ReasonSecession)) _seats.Add(new GarrisonSeat { PostIndex = g, InstanceId = f.InstanceId, AtPost = inState });
                    }
                }
            }

            // G8: an ordinary post's incumbents (every member the ledger remembers) keep it. Claimed right after D8's members, ahead of the depot builder and the recon pick, which
            // would otherwise take a seated garrison (SmallestIn favours the smallest formation of the sector, which is often the garrison) and
            // leave the post to be refilled from elsewhere. Only an incumbent that is still fit and free (PostCandidate: outside the mass,
            // not claimed by a role of its own, not given up on) is claimed; the rest of a post's need is met after the reserve split.
            private void ClaimPostIncumbents()
            {
                var members = new List<int>();
                for (int g = 0; g < _plan.Garrisons.Count; g++)
                {
                    var post = _plan.Garrisons[g];
                    if (post.Kind == GarrisonPostKind.Secession) continue;
                    members.Clear();
                    GarrisonPlanner.Incumbents(_s, post, PostPool(post), members);
                    for (int k = 0; k < members.Count; k++) SeatAtPost(g, post, members[k]);
                }
            }

            // G8: the rest of the ordinary posts' needs (TheaterPlan.Garrisons, Kind other than Secession; D8's were seated by AssignGarrisons).
            // Run after the reserve split, so the pool is known: a formation nothing else has claimed (it would otherwise just hold) or a
            // Reserve surplus formation, never a member of the mass (_inMass is set by MarkMass before any garrison step) and never one
            // already in a role of its own (capital guard, hold reserve, recover, depot builder, recon, immobile, a secession member or another
            // post's). A formation the tracker gave up on at the post is not asked. GarrisonPlanner.PickFormations names at most
            // MaxFormationsPerPost formations per post, the incumbent counted; Assign marks a seated formation done, so no formation holds two
            // posts. A post nobody can reach stays unmanned and one the pool cannot meet stays short; the ledger counts both.
            private void AssignGarrisonPosts()
            {
                var picks = new List<int>();
                for (int g = 0; g < _plan.Garrisons.Count; g++)
                {
                    var post = _plan.Garrisons[g];
                    if (post.Kind == GarrisonPostKind.Secession) continue;
                    picks.Clear();
                    GarrisonPlanner.PickFormations(_s, post, PostPool(post), _seatedStrength[g], GarrisonPlanner.MaxFormationsPerPost - _seatedCount[g], picks);
                    for (int k = 0; k < picks.Count; k++) SeatAtPost(g, post, picks[k]);
                }
            }

            private bool[] PostPool(GarrisonPost post)
            {
                var pool = new bool[_n];
                for (int i = 0; i < _n; i++) pool[i] = PostCandidate(i, post);
                return pool;
            }

            // Marches to the post as Garrison, or holds in place as Hold once there; either way withheld.
            private void SeatAtPost(int g, GarrisonPost post, int i)
            {
                var f = _s.Own[i];
                bool atPost = GarrisonPlanner.AtPost(f, post.X, post.Z);
                if (atPost)
                    Assign(i, FormationRole.Hold, _sector[i], f.X, f.Z, ReasonGarrisonPost, true);
                else
                    Assign(i, FormationRole.Garrison, _sectors.SectorAt(post.X, post.Z), post.X, post.Z, ReasonGarrisonPost, true);
                _seats.Add(new GarrisonSeat { PostIndex = g, InstanceId = f.InstanceId, AtPost = atPost });   // never a give-up: PostCandidate left those out
                _seatedStrength[g] += Math.Max(0f, f.Strength);
                _seatedCount[g]++;
            }

            // The formations the post may ask for (GarrisonPlanner.PickFormation's pool): not one the tracker gave up on at the post; a
            // formation nothing else has claimed; a Reserve surplus formation; and the post's own incumbent when the sector's hold reserve
            // has already claimed it in the post's own sector. That last case keeps the post from changing hands every day: the hold reserve
            // takes the formations nearest the believed army, which is where a post's garrison stands. Taking it back is free for the sector
            // (it stays a Hold formation of the same sector, still counted in its hold), so the sector's hold is unchanged.
            private bool PostCandidate(int i, GarrisonPost post)
            {
                if (GaveUpAt(i, post.X, post.Z)) return false;
                if (Free(i) && !_inMass[i]) return true;
                var f = _s.Own[i];
                if (!_done[i] || !WlChainRules.MayMove(f.Chain) || !TheaterPlanner.Movable(f)) return false;
                if (_role[i] == FormationRole.Reserve) return true;
                return _role[i] == FormationRole.Hold && ReferenceEquals(_reason[i], ReasonHoldReserve)
                    && post.InstanceIds.Contains(f.InstanceId) && _sector[i] == _sectors.SectorAt(post.X, post.Z);
            }

            // Spec C21: the supply threshold is the raw metric.
            private void AssignRecover()
            {
                for (int i = 0; i < _n; i++)
                {
                    if (_done[i] || _inMass[i]) continue;
                    var f = _s.Own[i];
                    if (!ReserveRules.Recovers(f, _s.MinMoraleForMovement)) continue;
                    if (!LogisticsGate.NearestSource(_s, f.X, f.Z, out float sx, out float sz))
                    {
                        sx = f.X;
                        sz = f.Z;
                    }
                    Assign(i, FormationRole.Recover, _sector[i], sx, sz, ReasonRecover, true);
                }
            }

            private bool Free(int i)
            {
                return !_done[i] && WlChainRules.MayMove(_s.Own[i].Chain) && TheaterPlanner.Movable(_s.Own[i]);
            }

            private void AssignDepotBuilders()
            {
                for (int t = 0; t < _plan.DepotTasks.Count; t++)
                {
                    var task = _plan.DepotTasks[t];
                    if (HasPinnedBuilder(task)) continue;   // one task, one builder
                    int pick = SmallestIn(task, false);
                    if (pick < 0) pick = SmallestIn(task, true);
                    if (pick < 0) continue;
                    Assign(pick, FormationRole.DepotBuilder, task.SectorId, task.X, task.Z, ReasonDepotTask, true);
                }
            }

            // Whether a Wished task's pinned builder already serves this plan task (its site within the ledger's site match radius): the
            // plan keeps listing the depot until it stands, and a second formation sent to it would idle at the site (verify r4 F11).
            private bool HasPinnedBuilder(DepotTask task)
            {
                if (_pinnedMask == null) return false;
                for (int k = 0; k < _pinned.Count; k++)
                {
                    if (!_pinned[k].Wished) continue;
                    int i = _s.IndexOfOwn(_pinned[k].InstanceId);
                    if (i < 0 || !_pinnedMask[i]) continue;
                    float dx = _pinned[k].SiteX - task.X, dz = _pinned[k].SiteZ - task.Z;
                    if (dx * dx + dz * dz <= DepotTaskLedger.SiteMatchRadius * DepotTaskLedger.SiteMatchRadius) return true;
                }
                return false;
            }

            // The smallest fighting formation of the sector (lower instance id on a tie); a formation
            // of the mass only when allowMass (G3 obligation: take from outside the mass while one qualifies).
            private int SmallestIn(DepotTask task, bool allowMass)
            {
                int sector = task.SectorId;
                int best = -1;
                for (int i = 0; i < _n; i++)
                {
                    if (!Free(i) || _sector[i] != sector || (_inMass[i] && !allowMass) || GaveUpAt(i, task.X, task.Z)) continue;
                    if (best < 0 || _s.Own[i].Strength < _s.Own[best].Strength
                        || (_s.Own[i].Strength == _s.Own[best].Strength && _s.Own[i].InstanceId < _s.Own[best].InstanceId))
                        best = i;
                }
                return best;
            }

            private void AssignRecon()
            {
                _reconFree = FreeScouts();
                if (_recon == null || _recon.Count == 0) return;
                var taken = new HashSet<int>();
                var takenAndMass = new HashSet<int>();
                for (int i = 0; i < _n; i++)
                {
                    if (_done[i]) taken.Add(_s.Own[i].InstanceId);
                    if (_done[i] || _inMass[i]) takenAndMass.Add(_s.Own[i].InstanceId);
                }
                bool mainHasScreen = false;
                int drawn = StandingMainScouts();
                bool mainFilled = false;
                for (int t = 0; t < _recon.Count; t++)
                {
                    var task = _recon[t];
                    // A formation the tracker gave up on at this hold point is not a candidate for this task: the next one is tried.
                    List<int> skippedTaken = null, skippedMass = null;
                    if (_gaveUp != null && _gaveUp.Count > 0)
                    {
                        skippedTaken = new List<int>();
                        skippedMass = new List<int>();
                        for (int i = 0; i < _n; i++)
                        {
                            if (!GaveUpAt(i, task.HoldX, task.HoldZ)) continue;
                            int gid = _s.Own[i].InstanceId;
                            if (taken.Add(gid)) skippedTaken.Add(gid);
                            if (takenAndMass.Add(gid)) skippedMass.Add(gid);
                        }
                    }
                    int id = ReconPlanner.PickFormation(_s, _sectors, _plan, _missions, task, taken, mainHasScreen);
                    int pick = id >= 0 ? _s.IndexOfOwn(id) : -1;
                    bool massKept = false;
                    // G3 obligation: take the formation from outside the mass while one qualifies; the
                    // incumbent of an active mission keeps its post, and a formation of a worse tier
                    // (cavalry screens first) does not replace a better one just to spare the mass.
                    if (pick >= 0 && _inMass[pick] && !(_missions != null && _missions.IsRecon(id)))
                    {
                        int outside = ReconPlanner.PickFormation(_s, _sectors, _plan, _missions, task, takenAndMass, mainHasScreen);
                        int outsideIndex = outside >= 0 ? _s.IndexOfOwn(outside) : -1;
                        if (outsideIndex >= 0 && ReconTier(outsideIndex) <= ReconTier(pick)) pick = outsideIndex;
                    }
                    // Smoke 2026-10-17: the main effort keeps its last body. The pick could only return the mass's own last free formation,
                    // and a push with no formation left is not a push (the mover reports it undispatchable and the plan keeps naming it).
                    // A cavalry group is the exception D7 made on purpose: it screens although the plan massed it (ReconTier 0).
                    if (pick >= 0 && _inMass[pick] && ReconTier(pick) != 0 && FreeMassBodies() <= 1)
                    {
                        pick = -1;
                        massKept = true;
                    }
                    // Smoke 2026-10-17: the formations nothing else claimed are the only ones the pick sees, and the hold reserve claims every
                    // ready formation before it (ReserveRules.Hold takes the massable ones nearest the army a sector faces until its need is
                    // met, and an unscouted sector needs the presumption), so a staged push had nobody to scout with and stayed staged for
                    // good. It draws one scout from the hold reserve, and only for the tasks that see its own objective.
                    bool fromReserve = false, refusedHere = false;
                    if (pick < 0 && _plan.MainStaged && ReconPlanner.ScoutsMainObjective(task))
                    {
                        // One standing scout per main effort, across reviews: with one out, only that formation (the incumbent of its
                        // mission) may be drawn again; a second is never opened beside it.
                        pick = DrawFromHoldReserve(task, taken, mainHasScreen, incumbentsOnly: drawn >= MaxReserveScouts, out refusedHere);
                        fromReserve = pick >= 0;
                    }
                    if (pick < 0 && !refusedHere) _unfilledWhy.Add(WhyUnfilled(task, massKept));
                    if (skippedTaken != null)
                    {
                        for (int k = 0; k < skippedTaken.Count; k++) taken.Remove(skippedTaken[k]);
                        for (int k = 0; k < skippedMass.Count; k++) takenAndMass.Remove(skippedMass[k]);
                    }
                    if (pick < 0 || (_done[pick] && !fromReserve)) continue;
                    int pickedId = _s.Own[pick].InstanceId;
                    Assign(pick, FormationRole.Recon, task.SectorId, task.HoldX, task.HoldZ, task.Reason, true);
                    if (fromReserve && drawn < MaxReserveScouts) drawn++;
                    taken.Add(pickedId);
                    takenAndMass.Add(pickedId);
                    if (task.Kind == ReconKind.Screen && task.SectorId == _plan.MainSectorId) mainHasScreen = true;
                    if (ReconPlanner.ScoutsMainObjective(task)) mainFilled = true;
                }
                // A refusal is the reason a scout is missing; with one out (drawn now, or standing) or a main task filled by any pick
                // there is nothing to explain.
                if (drawn >= MaxReserveScouts || mainFilled) _scoutRefusedForThreat = false;
            }

            // The mass's members no role has claimed yet: the bodies a scout pick could still take from the main effort.
            private int FreeMassBodies()
            {
                int n = 0;
                for (int i = 0; i < _n; i++)
                    if (_inMass[i] && !_done[i]) n++;
                return n;
            }

            // The active reconnaissance missions that see the main objective (the probe, the advance guard, the main-axis screen) whose
            // formation is still ours: the scouts already out. The draw counts them, so the cap is per main effort, not per review.
            private int StandingMainScouts()
            {
                if (_missions == null) return 0;
                int n = 0;
                var list = _missions.Missions;
                for (int m = 0; m < list.Count; m++)
                    if (ReconMissionLedger.IsActive(list[m].State) && ReconPlanner.ScoutsMainObjective(list[m].Kind, list[m].Reason)
                        && _s.IndexOfOwn(list[m].InstanceId) >= 0) n++;
                return n;
            }

            // A ready formation the hold reserve kept for its sector (not a capital guard or a recover): the cheapest thing a scout can be
            // drawn from, since the sector's hold is a standing need and the scout is a detachment. Never a formation the plan pledged to a
            // secession garrison: the hold reserve claims before AssignGarrisons, so a Kentucky formation can carry both, and the pledge wins
            // (the plan's own list is read, the seats are written later).
            private bool IsHoldReserve(int i)
            {
                return _done[i] && _role[i] == FormationRole.Hold && ReferenceEquals(_reason[i], ReasonHoldReserve) && WlChainRules.MayMove(_s.Own[i].Chain)
                    && !IsSecessionPledge(_s.Own[i].InstanceId);
            }

            private bool IsSecessionPledge(int instanceId)
            {
                for (int g = 0; g < _plan.Garrisons.Count; g++)
                {
                    var post = _plan.Garrisons[g];
                    if (post.Kind != GarrisonPostKind.Secession) continue;
                    for (int k = 0; k < post.InstanceIds.Count; k++)
                        if (post.InstanceIds[k] == instanceId) return true;
                }
                return false;
            }

            // The draw never opens a shortfall against a believed enemy force. A sector gives a scout when (a) its hold need is set by the
            // floor or by the presumption of an unscouted front (no believed army drives it), or (b) its hold, less this formation, still
            // meets its target. The hold is the sum ReserveRules.Hold sized the reserve on (what holds there anyway, the unready, the busy and
            // the capital guard of the capital's own sector included, plus the reserved formations), less every formation of it that has
            // since been given a role that does not hold the sector (HoldLeaves): a depot builder, a secession garrison on the march, an
            // earlier scout. The remainder that stands in the sector is assigned after this pick. The ordinary garrison posts that come
            // later take no counted formation out of its sector: GarrisonPlanner seats only a massable formation (Seatable), and the
            // hold reserve's own incumbents it takes back stay in their sector. Otherwise the formation stays and the refusal is recorded
            // (Allocation.ReconUnfilledWhy).
            private bool SectorMayGiveScout(int i)
            {
                int k = _roleSector[i];
                if (k < 0 || k >= _sectorCount) return true;
                bool beliefDriven = _believed[k] > MinGarrisonStrength && _believed[k] >= _need[k] - 0.01f;
                return !beliefDriven || _held.Sector[k] - HoldLeaving(k) - Math.Max(0f, _s.Own[i].Strength) >= Target(k);
            }

            // The share of the sector's counted hold that no longer holds it: a counted formation already given a role other than holding
            // the sector (Hold or CapitalGuard; each is assigned in the sector the formation was counted in). A formation not yet placed stays:
            // AssignRemainder holds it.
            private float HoldLeaving(int sector)
            {
                float leaving = 0f;
                for (int j = 0; j < _n; j++)
                {
                    if (_held.ShareSector[j] != sector || !_done[j]) continue;
                    bool holds = _role[j] == FormationRole.Hold || _role[j] == FormationRole.CapitalGuard;   // both keep the formation's own sector, the one it was counted in
                    if (!holds) leaving += _held.Share[j];
                }
                return leaving;
            }

            // ReconPlanner.PickFormation over the hold reserve alone (everything else stays taken), so its own ranking (cavalry, then the
            // smallest group, nearest) and every minimum of the pick apply. -1 when no reserve formation qualifies. With incumbentsOnly
            // (a scout is already out) only the formation already on a mission may be drawn. The sector it came from shows the loss as a
            // shortfall (ListShortfalls) like any hold formation that leaves.
            private int DrawFromHoldReserve(in ReconTask task, HashSet<int> taken, bool mainHasScreen, bool incumbentsOnly, out bool refusedForThreat)
            {
                var open = new HashSet<int>(taken);
                bool refused = false;
                for (int i = 0; i < _n; i++)
                {
                    if (!IsHoldReserve(i) || GaveUpAt(i, task.HoldX, task.HoldZ)) continue;
                    if (incumbentsOnly && !(_missions != null && _missions.IsRecon(_s.Own[i].InstanceId))) continue;
                    if (!SectorMayGiveScout(i)) { refused = true; continue; }
                    open.Remove(_s.Own[i].InstanceId);
                }
                int id = ReconPlanner.PickFormation(_s, _sectors, _plan, _missions, task, open, mainHasScreen);
                int pick = id >= 0 && IsHoldReserve(_s.IndexOfOwn(id)) ? _s.IndexOfOwn(id) : -1;
                refusedForThreat = pick < 0 && refused;
                if (refusedForThreat) _scoutRefusedForThreat = true;
                return pick;
            }

            // The formations a detachment-standard scout pick could still take as the recon step begins (the [Recon] row's free=).
            private int FreeScouts()
            {
                int n = 0;
                for (int i = 0; i < _n; i++)
                    if (!_done[i] && ReconPlanner.CouldScout(_s, _s.Own[i], ReconKind.AdvanceGuard)) n++;
                return n;
            }

            // Why the task has no scout, the first that applies (ReasonUnfilled*). Read before the gave-up formations are put back, from the
            // roles given so far: a formation that could go and is held by a role is claimed by it, one that nothing holds was turned down by
            // the kind's own rules (unsuited).
            private string WhyUnfilled(in ReconTask task, bool massKept)
            {
                if (massKept) return ReasonUnfilledMassKept;
                int claimedBy = -1, claimed = 0, gaveUp = 0, free = 0;
                for (int i = 0; i < _n; i++)
                {
                    if (!ReconPlanner.CouldScout(_s, _s.Own[i], task.Kind)) continue;
                    if (GaveUpAt(i, task.HoldX, task.HoldZ)) gaveUp++;
                    else if (_done[i])
                    {
                        if (claimedBy < 0) claimedBy = i;
                        claimed++;
                    }
                    else free++;
                }
                if (free > 0) return ReasonUnfilledUnsuited;
                if (claimed > 0) return ReasonUnfilledClaimedPrefix + RoleTag(claimedBy);
                if (gaveUp > 0) return ReasonUnfilledGaveUp;
                return ReasonUnfilledNoneAble;
            }

            // What holds a formation: the hold reserve's own reason for a Hold role, the role's name otherwise.
            private string RoleTag(int i)
            {
                if (_role[i] == FormationRole.Hold && _reason[i] != null) return _reason[i];
                string name = _role[i].ToString();
                var sb = new System.Text.StringBuilder(name.Length + 2);
                for (int k = 0; k < name.Length; k++)
                {
                    if (char.IsUpper(name[k]) && k > 0) sb.Append('-');
                    sb.Append(char.ToLowerInvariant(name[k]));
                }
                return sb.ToString();
            }

            // The row's why=: the distinct reasons, the refusal first, then the tasks' in their order.
            private string UnfilledWhy()
            {
                var reasons = new List<string>();
                if (_scoutRefusedForThreat) reasons.Add(ReasonScoutAgainstThreat);
                for (int k = 0; k < _unfilledWhy.Count; k++)
                    if (!reasons.Contains(_unfilledWhy[k])) reasons.Add(_unfilledWhy[k]);
                return reasons.Count == 0 ? null : string.Join(",", reasons);
            }

            // ReconPlanner.PickFormation's own ranking: cavalry-majority, then other combat groups.
            private int ReconTier(int i)
            {
                return _s.Own[i].CavalryShare >= ReconPlanner.MinCavalryShare ? 0 : 1;
            }

            // The force above ReserveSurplusMargin x the required force becomes Reserve, farthest from
            // the gather point first. The required force is the plan's MainRequiredRatio x the enemy G3
            // judged the odds against at the main objective (TheaterPlan.MainEnemyStrength): the same
            // point and the same river-weighted odds, however far the gather point was clamped back.
            private void SplitReserve(out float required, out float massStrength)
            {
                required = 0f;
                massStrength = 0f;
                var sp = MainSector();
                if (sp == null) return;
                var members = new List<int>();
                for (int i = 0; i < _n; i++)
                    if (_inMass[i] && !_done[i])
                    {
                        members.Add(i);
                        massStrength += Math.Max(0f, _s.Own[i].Strength);
                    }
                float enemy = _plan.MainEnemyStrength;
                required = _plan.MainRequiredRatio * enemy;
                if (enemy <= 0f || required <= 0f) return;

                members.Sort((a, b) =>
                {
                    int d = ReserveRules.DistanceSq(_s.Own[b].X, _s.Own[b].Z, sp.TargetX, sp.TargetZ)
                        .CompareTo(ReserveRules.DistanceSq(_s.Own[a].X, _s.Own[a].Z, sp.TargetX, sp.TargetZ));
                    return d != 0 ? d : b.CompareTo(a);
                });
                float keep = ReserveSurplusMargin * required;
                for (int k = 0; k < members.Count; k++)
                {
                    int i = members[k];
                    float strength = Math.Max(0f, _s.Own[i].Strength);
                    if (massStrength - strength < keep) break; // the reserve is a suffix by distance: never a nearer formation than one kept
                    massStrength -= strength;
                    Assign(i, FormationRole.Reserve, _sector[i], _s.Own[i].X, _s.Own[i].Z, ReasonReserve, true);
                }
            }

            private SectorPlan MainSector()
            {
                if (_plan.MainSectorId < 0) return null;
                for (int k = 0; k < _plan.Sectors.Count; k++)
                    if (_plan.Sectors[k] != null && _plan.Sectors[k].SectorId == _plan.MainSectorId) return _plan.Sectors[k];
                return null;
            }

            // D1: an unscouted main effort (TheaterPlan.MainStaged) gathers at the plan's start line, the forward-most mass member's
            // position; the march on the objective starts when the picture is scouted. The mass and its reinforcement share the point.
            private void MainTarget(SectorPlan sp, out float x, out float z)
            {
                x = _plan.MainStaged ? _plan.StartLineX : sp.TargetX;
                z = _plan.MainStaged ? _plan.StartLineZ : sp.TargetZ;
            }

            private void AssignMainEffort()
            {
                var sp = MainSector();
                if (sp == null) return;
                MainTarget(sp, out float tx, out float tz);
                // D6: the one formation from another theater is the push's Support: the mover waits for the launch with it into
                // enemy ground, as with any Support move. Its role sector is the main sector (the objective it was brought for).
                int reinforcement = _plan.ReinforcementInstanceId >= 0 ? _s.IndexOfOwn(_plan.ReinforcementInstanceId) : -1;
                if (reinforcement >= 0 && _inMass[reinforcement] && !_done[reinforcement])
                    Assign(reinforcement, FormationRole.Support, sp.SectorId, tx, tz, ReasonReinforcement, false);
                for (int i = 0; i < _n; i++)
                    if (_inMass[i] && !_done[i])
                        Assign(i, FormationRole.MainEffort, sp.SectorId, tx, tz, ReasonMass, false);
            }

            private void AssignRemainder()
            {
                for (int i = 0; i < _n; i++)
                {
                    if (_done[i]) continue;
                    // D3: a formation outside the mass keeps its own front's role, in the main and Support sectors too; it is not "the
                    // remainder to the main effort".
                    var f = _s.Own[i];
                    string reason = f.Chain == WlChainClass.PlayerMapGroup ? ReasonPlayerMapGroup : ReasonGarrison;
                    Assign(i, FormationRole.Hold, _sector[i], f.X, f.Z, reason, false);
                }
            }

            // ---- hold sums ----

            private float Target(int sector)
            {
                return Math.Max(_need[sector], MinGarrisonStrength);
            }

            // The hold a sector has by the roles assigned: its Hold formations, and the capital guards that stand in the capital's own sector
            // (a guard elsewhere is on the march and holds nothing where it stands), the way ReserveRules.Hold counts them.
            private float Counted(int sector)
            {
                float sum = 0f;
                for (int i = 0; i < _n; i++)
                    if ((_role[i] == FormationRole.Hold || _role[i] == FormationRole.CapitalGuard) && _sector[i] == sector && _roleSector[i] == sector)
                        sum += Counts(i);
                return sum;
            }

            // What formation i adds to a hold sum: 0 unless WlChainRules.CountsTowardCoverage (the player's own
            // command and a CIC side hold nothing), else ReserveRules.HoldStrength.
            private float Counts(int i)
            {
                return WlChainRules.CountsTowardCoverage(_s.Own[i].Chain) ? ReserveRules.HoldStrength(_s.Own[i]) : 0f;
            }

            // Distance to the sector's nearest believed land enemy (centroid when none), the home
            // department of the formation discounting it by HomeDepartmentDiscount.
            private float HoldKey(int i, int sector)
            {
                float dx = _s.Own[i].X - _nearX[sector];
                float dz = _s.Own[i].Z - _nearZ[sector];
                float d = (float)Math.Sqrt(dx * dx + dz * dz);
                if (_home != null && sector >= 0 && sector < _sectorCount)
                {
                    var home = _home(_s.Own[i].UnitKey);
                    if (home != Theater.Unknown && home == _sectors.TheaterOf(sector)) d *= 1f - HomeDepartmentDiscount;
                }
                return d;
            }

            private void SortByHoldKey(List<int> order, int sector)
            {
                order.Sort((a, b) =>
                {
                    int c = HoldKey(a, sector).CompareTo(HoldKey(b, sector));
                    return c != 0 ? c : _s.Own[a].InstanceId.CompareTo(_s.Own[b].InstanceId);
                });
            }

            // What holds a sector anyway counts first: the immobile formations and the map-group player's
            // group at strength x morale. The mobile Hold formations are then withheld, nearest the threat
            // first, until the target is reached. An immobile formation is never withheld (it is not
            // Whiskey's to steer), nor is the player's group (a W&L dispatch moves it) or a formation that
            // counts nothing (the player's own command, a CIC side).
            private void WithholdHoldPrefixes()
            {
                for (int k = 0; k < _sectorCount; k++)
                {
                    var holds = new List<int>();
                    float sum = 0f;
                    for (int i = 0; i < _n; i++)
                    {
                        if (_role[i] != FormationRole.Hold || _sector[i] != k || _roleSector[i] != k
                            || !WlChainRules.CountsTowardCoverage(_s.Own[i].Chain)) continue;
                        if (IsImmobileReason(_reason[i]) || WlChainRules.OrderOnly(_s.Own[i].Chain)) sum += Counts(i);
                        else holds.Add(i);
                    }
                    if (holds.Count == 0) continue;
                    SortByHoldKey(holds, k);
                    float target = Target(k);
                    for (int p = 0; p < holds.Count && sum < target; p++)
                    {
                        int i = holds[p];
                        _withheld[i] = true;
                        sum += Counts(i);
                    }
                }
            }

            // ---- shortfalls and surpluses ----

            private void ListShortfalls(Allocation allocation, float mainRequired, float mainStrength)
            {
                var carriers = new List<KeyValuePair<int, float>>(); // instance id, deficit ratio
                var sp = MainSector();
                if (sp != null && mainRequired > 0f && mainStrength < mainRequired)
                {
                    int front = NearestWith(FormationRole.MainEffort, sp.TargetX, sp.TargetZ);
                    if (front >= 0) carriers.Add(new KeyValuePair<int, float>(_s.Own[front].InstanceId, (mainRequired - mainStrength) / mainRequired));
                }
                if (_s.HasCapital && _plan.CapitalFloorStrength > 0f)
                {
                    float guards = SumOf(FormationRole.CapitalGuard);
                    int guard = NearestWith(FormationRole.CapitalGuard, _s.CapitalX, _s.CapitalZ);
                    if (guard >= 0 && guards < _plan.CapitalFloorStrength)
                        carriers.Add(new KeyValuePair<int, float>(_s.Own[guard].InstanceId, (_plan.CapitalFloorStrength - guards) / _plan.CapitalFloorStrength));
                }
                for (int k = 0; k < _sectorCount; k++)
                {
                    var holds = new List<int>();
                    for (int i = 0; i < _n; i++)
                        if (_role[i] == FormationRole.Hold && _sector[i] == k && _roleSector[i] == k && WlChainRules.MayMove(_s.Own[i].Chain)
                            && !IsImmobileReason(_reason[i])) holds.Add(i);
                    float target = Target(k);
                    float counted = Counted(k);
                    if (counted >= target) continue;
                    if (holds.Count == 0)
                    {
                        // A threatened sector no AI formation holds, and the map-group player's group holding it
                        // (counted, but short of the target) marks the position; G5b never routes a
                        // reinforcement to a player formation.
                        int marker = _need[k] > 0f ? PlayerMarkerIn(k) : -1;
                        // Failing that, the nearest immobile holder (a formation in battle holds where it stands and counts): the short sector
                        // still names a carrier, so G5b sees it when its one mobile hold was drawn away as a scout.
                        if (marker < 0 && _need[k] > 0f) marker = ImmobileHolderIn(k);
                        if (marker >= 0) carriers.Add(new KeyValuePair<int, float>(_s.Own[marker].InstanceId, (target - counted) / target));
                        continue;
                    }
                    SortByHoldKey(holds, k);
                    carriers.Add(new KeyValuePair<int, float>(_s.Own[holds[0]].InstanceId, (target - counted) / target));
                }
                carriers.Sort((a, b) =>
                {
                    int c = b.Value.CompareTo(a.Value);
                    return c != 0 ? c : a.Key.CompareTo(b.Key);
                });
                for (int c = 0; c < carriers.Count; c++)
                    if (!allocation.Shortfalls.Contains(carriers[c].Key)) allocation.Shortfalls.Add(carriers[c].Key);
            }

            // The immobile Hold formation of the sector nearest the army it faces that counts toward its hold and that the planner may order,
            // -1 when none.
            private int ImmobileHolderIn(int sector)
            {
                var holders = new List<int>();
                for (int i = 0; i < _n; i++)
                    if (_role[i] == FormationRole.Hold && _sector[i] == sector && _roleSector[i] == sector && IsImmobileReason(_reason[i])
                        && WlChainRules.CountsTowardCoverage(_s.Own[i].Chain) && WlChainRules.MayMove(_s.Own[i].Chain)) holders.Add(i);
                if (holders.Count == 0) return -1;
                SortByHoldKey(holders, sector);
                return holders[0];
            }

            private int PlayerMarkerIn(int sector)
            {
                for (int i = 0; i < _n; i++)
                    if (_role[i] == FormationRole.Hold && _sector[i] == sector && _s.Own[i].Chain == WlChainClass.PlayerMapGroup) return i;
                return -1;
            }

            private float SumOf(FormationRole role)
            {
                float sum = 0f;
                for (int i = 0; i < _n; i++)
                    if (_role[i] == role) sum += Math.Max(0f, _s.Own[i].Strength);
                return sum;
            }

            private int NearestWith(FormationRole role, float x, float z)
            {
                int best = -1;
                float bestDistance = float.MaxValue;
                for (int i = 0; i < _n; i++)
                {
                    if (_role[i] != role || !WlChainRules.MayMove(_s.Own[i].Chain)) continue;
                    float d = ReserveRules.DistanceSq(_s.Own[i].X, _s.Own[i].Z, x, z);
                    if (d >= bestDistance) continue;
                    bestDistance = d;
                    best = i;
                }
                return best;
            }

            // Reserve, and the EconomyOfForce Hold formations the garrison prefix did not keep (a sector
            // with no believed threat): the strength vanilla may use or the transfers may draw on.
            private void ListSurpluses(Allocation allocation)
            {
                var list = new List<int>();
                for (int i = 0; i < _n; i++)
                {
                    if (!WlChainRules.MayMove(_s.Own[i].Chain)) continue;
                    bool reserve = _role[i] == FormationRole.Reserve;
                    bool released = _role[i] == FormationRole.Hold && !IsImmobileReason(_reason[i]) && !_withheld[i]
                        && _sector[i] >= 0 && _sector[i] < _sectorCount && _need[_sector[i]] <= 0f;
                    if (reserve || released) list.Add(i);
                }
                list.Sort((a, b) =>
                {
                    int c = _s.Own[b].Strength.CompareTo(_s.Own[a].Strength);
                    return c != 0 ? c : _s.Own[a].InstanceId.CompareTo(_s.Own[b].InstanceId);
                });
                for (int k = 0; k < list.Count; k++) allocation.Surpluses.Add(_s.Own[list[k]].InstanceId);
            }
        }
    }
}
