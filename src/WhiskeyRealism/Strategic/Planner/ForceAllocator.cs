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
    //                 can pair them back (G1b contract); withheld.
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
    //   Hold (rest)   the remainder anywhere else, Hold in place (EconomyOfForce garrison).
    //   Withheld      a sector's Hold formations are ordered by distance to the sector's nearest
    //                 believed land enemy (centroid when none), with a HomeTheaterTieBreak discount for
    //                 a formation whose home theater is the sector's; the shortest prefix whose
    //                 strength reaches max(HoldNeed, MinGarrisonStrength) is withheld from vanilla's
    //                 offensive gather (CheckOffensiveMovements 14257-14275), the rest is not.
    //   Gave up       a formation the move tracker gave up on at the target its role would send it to (P8: two misses,
    //                 MoveTracker.GaveUp) loses that role: the depot and recon picks take the next candidate, any other
    //                 role becomes Hold in place with ReasonGaveUp, until the role's target changes. The set is an
    //                 input (GaveUpMove), so the pass stays pure and deterministic.
    //   Player        CarryingGroup (a player below map level) is an ordinary movable formation (F's
    //                 MoveUnitTo Postfix dispatches its march). The map-group player's group counts
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
        public const float HomeTheaterTieBreak = 0.05f;
        public const float ReserveSurplusMargin = 1.3f;

        internal const string ReasonBusy = "busy";
        internal const string ReasonPlayerCommand = "player-command";
        internal const string ReasonNonCombat = "non-combat";
        internal const string ReasonCapitalFloor = "capital-floor";
        internal const string ReasonHoldReserve = "hold-reserve";
        internal const string ReasonRecover = "recover";
        internal const string ReasonDepotTask = "depot-task";
        internal const string ReasonMass = "mass";
        internal const string ReasonReserve = "reserve-surplus";
        internal const string ReasonSupport = "support";
        internal const string ReasonGarrison = "garrison";
        internal const string ReasonPlayerMapGroup = "player-map-group";
        internal const string ReasonGaveUp = "gave-up";

        public static Allocation Allocate(TheaterPlan plan, CampaignSnapshot s, SectorModel sectors, Func<string, Theater> homeTheater,
                                          IReadOnlyList<ReconTask> reconTasks, ReconMissionLedger missions, IReadOnlyList<GaveUpMove> gaveUp = null,
                                          IReadOnlyList<HeldBuilder> heldBuilders = null)
        {
            var allocation = new Allocation { Alliance = s != null ? s.Alliance : -1 };
            if (s == null || plan == null || sectors == null || s.Alliance < 0 || s.Alliance > 1 || s.Own.Count == 0) return allocation;
            if (StandsDown(s)) return allocation;
            new Pass(plan, s, sectors, homeTheater, reconTasks, missions, gaveUp, heldBuilders).Run(allocation);
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

            public void Reset()
            {
                _lastDay = int.MinValue;
                Days = 0;
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
                + " short=" + a.Shortfalls.Count + " surplus=" + a.Surpluses.Count;
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

            private readonly bool[] _done;
            private readonly bool[] _inMass;
            private readonly FormationRole[] _role;
            private readonly int[] _sector;    // the sector the formation stands in; the map-group player's is the sector his last waypoint lies in (HoldSectorOf)
            private readonly int[] _roleSector;
            private readonly float[] _tx, _tz;
            private readonly string[] _reason;
            private readonly bool[] _withheld;

            private readonly float[] _need, _nearX, _nearZ;  // per sector: hold need, the army it faces

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
                AssignImmobile();
                ReserveRules.Capital(_s, _plan.CapitalFloorStrength, capitalReserved, pinnedBuilder);
                ReserveRules.Hold(_s, _sectors, _need, _nearX, _nearZ, capitalReserved, holdReserved);
                MarkMass();
                AssignCapitalGuards(capitalReserved);
                AssignHoldReserve(holdReserved);
                AssignRecover();
                AssignDepotBuilders();
                AssignRecon();
                SplitReserve(out float mainRequired, out float mainStrength);
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
                        ReconMissionId = -1,
                        Withheld = _withheld[i]
                    });
                }
                ListShortfalls(allocation, mainRequired, mainStrength);
                ListSurpluses(allocation);
            }

            // ---- inputs ----

            private void ReadSectorFacts()
            {
                for (int k = 0; k < _plan.Sectors.Count; k++)
                {
                    var sp = _plan.Sectors[k];
                    if (sp != null && sp.SectorId >= 0 && sp.SectorId < _sectorCount) _need[sp.SectorId] = sp.HoldNeed;
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
                    if (_gaveUp[k].InstanceId == id && _gaveUp[k].Matches(tx, tz)) return true;
                return false;
            }

            private static bool Marches(FormationRole role)
            {
                return role == FormationRole.MainEffort || role == FormationRole.Support || role == FormationRole.CapitalGuard
                    || role == FormationRole.Recover || role == FormationRole.DepotBuilder || role == FormationRole.Recon;
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
                    int pick = SmallestIn(task, false);
                    if (pick < 0) pick = SmallestIn(task, true);
                    if (pick < 0) continue;
                    Assign(pick, FormationRole.DepotBuilder, task.SectorId, task.X, task.Z, ReasonDepotTask, true);
                }
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
                if (_recon == null || _recon.Count == 0) return;
                var taken = new HashSet<int>();
                var takenAndMass = new HashSet<int>();
                for (int i = 0; i < _n; i++)
                {
                    if (_done[i]) taken.Add(_s.Own[i].InstanceId);
                    if (_done[i] || _inMass[i]) takenAndMass.Add(_s.Own[i].InstanceId);
                }
                bool mainHasScreen = false;
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
                    // G3 obligation: take the formation from outside the mass while one qualifies; the
                    // incumbent of an active mission keeps its post, and a formation of a worse tier
                    // (cavalry screens first) does not replace a better one just to spare the mass.
                    if (pick >= 0 && _inMass[pick] && !(_missions != null && _missions.IsRecon(id)))
                    {
                        int outside = ReconPlanner.PickFormation(_s, _sectors, _plan, _missions, task, takenAndMass, mainHasScreen);
                        int outsideIndex = outside >= 0 ? _s.IndexOfOwn(outside) : -1;
                        if (outsideIndex >= 0 && ReconTier(outsideIndex) <= ReconTier(pick)) pick = outsideIndex;
                    }
                    if (skippedTaken != null)
                    {
                        for (int k = 0; k < skippedTaken.Count; k++) taken.Remove(skippedTaken[k]);
                        for (int k = 0; k < skippedMass.Count; k++) takenAndMass.Remove(skippedMass[k]);
                    }
                    if (pick < 0 || _done[pick]) continue;
                    int pickedId = _s.Own[pick].InstanceId;
                    Assign(pick, FormationRole.Recon, task.SectorId, task.HoldX, task.HoldZ, task.Reason, true);
                    taken.Add(pickedId);
                    takenAndMass.Add(pickedId);
                    if (task.Kind == ReconKind.Screen && task.SectorId == _plan.MainSectorId) mainHasScreen = true;
                }
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

            private void AssignMainEffort()
            {
                var sp = MainSector();
                if (sp == null) return;
                for (int i = 0; i < _n; i++)
                    if (_inMass[i] && !_done[i])
                        Assign(i, FormationRole.MainEffort, sp.SectorId, sp.TargetX, sp.TargetZ, ReasonMass, false);
            }

            private SectorPlan PlanOf(int sector)
            {
                if (sector < 0) return null;
                for (int k = 0; k < _plan.Sectors.Count; k++)
                    if (_plan.Sectors[k] != null && _plan.Sectors[k].SectorId == sector) return _plan.Sectors[k];
                return null;
            }

            private void AssignRemainder()
            {
                for (int i = 0; i < _n; i++)
                {
                    if (_done[i]) continue;
                    var f = _s.Own[i];
                    var sp = PlanOf(_sector[i]);
                    if (sp != null && (sp.Posture == TheaterPosture.Support || sp.Posture == TheaterPosture.MainEffort))
                    {
                        Assign(i, FormationRole.Support, sp.SectorId, sp.TargetX, sp.TargetZ, ReasonSupport, false);
                        continue;
                    }
                    string reason = f.Chain == WlChainClass.PlayerMapGroup ? ReasonPlayerMapGroup : ReasonGarrison;
                    Assign(i, FormationRole.Hold, _sector[i], f.X, f.Z, reason, false);
                }
            }

            // ---- hold sums ----

            private float Target(int sector)
            {
                return Math.Max(_need[sector], MinGarrisonStrength);
            }

            private float Counted(int sector)
            {
                float sum = 0f;
                for (int i = 0; i < _n; i++)
                    if (_role[i] == FormationRole.Hold && _sector[i] == sector && _roleSector[i] == sector)
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
            // theater of the formation discounting it by HomeTheaterTieBreak.
            private float HoldKey(int i, int sector)
            {
                float dx = _s.Own[i].X - _nearX[sector];
                float dz = _s.Own[i].Z - _nearZ[sector];
                float d = (float)Math.Sqrt(dx * dx + dz * dz);
                if (_home != null && sector >= 0 && sector < _sectorCount)
                {
                    var home = _home(_s.Own[i].Name);
                    if (home != Theater.Unknown && home == _sectors.TheaterOf(sector)) d *= 1f - HomeTheaterTieBreak;
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
