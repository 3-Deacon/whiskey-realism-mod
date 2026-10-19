using System.Collections.Generic;

namespace WhiskeyRealism.Strategic.Planner
{
    // The two reserves G3 sets aside before it masses (the capital reserve and each threatened
    // sector's hold reserve) and the belief readings they use. They are rows 6 and 7 of the ownership
    // table (ClaimTable): they write Capital and Hold claims, TheaterPlanner.MassFor subtracts them
    // from the massable set, and ForceAllocator (G4) assigns CapitalGuard and Hold from those very
    // claims without re-running them, so the mass the odds were judged on and the roles the
    // allocator hands out cannot diverge (G3 verify r1 F4/F9). Pure: snapshot in, claims out.
    internal static class ReserveRules
    {
        // A belief's live weight: strength x confidence, 0 once expired.
        internal static float Live(in EnemyBelief b)
        {
            return b.Confidence01 > 0f ? System.Math.Max(0f, b.Strength) * System.Math.Min(1f, b.Confidence01) : 0f;
        }

        // Vanilla's own weighting of the enemy near a place it defends (AssignUnitToDefendCapital 11733-11740): each army within range counts
        // live strength / max(10, distance)^0.3 x 0.5. The capital floor (TheaterPlanner.CapitalThreat) and a garrison post's threat
        // (GarrisonPlanner) both read it here, on beliefs, so there is one formula. An afloat army counts at its straight distance (it
        // cannot be stopped by a river); a land army at the distance the probe gives (the straight one with no probe).
        internal const float VanillaThreatMinDistance = 10f, VanillaThreatExponent = 0.3f, VanillaThreatScale = 0.5f;

        internal static float VanillaThreat(CampaignSnapshot s, float x, float z, float range, ITerrainLineProbe probe)
        {
            float weighted = 0f;
            for (int i = 0; i < s.Beliefs.Count; i++)
            {
                var b = s.Beliefs[i];
                float live = Live(b);
                float straight = RiverLines.Distance(b.X, b.Z, x, z);
                if (live <= 0f || straight >= range) continue;
                float d = Afloat(b) ? straight : RiverLines.EffectiveDistance(b.X, b.Z, x, z, probe, s.Crossings);
                weighted += live / (float)System.Math.Pow(System.Math.Max(VanillaThreatMinDistance, d), VanillaThreatExponent) * VanillaThreatScale;
            }
            return weighted;
        }

        // The same weight over the enemy picture (foresight F3, D11 requirement-capital-window): every element of all five kinds (the live and
        // faded masses of beliefs, afloat or ashore, and the anchors' shares) within `reach` of the place, mass / max(10, d)^0.3 x 0.5, d the
        // river-effective distance for a land element and the straight one for an afloat element. VanillaThreat stays for G8's posts, which
        // keep their belief-only threat.
        internal static float PictureThreat(CampaignSnapshot s, EnemyPicture picture, float x, float z, float reach, ITerrainLineProbe probe)
        {
            float weighted = 0f;
            for (int i = 0; i < picture.Elements.Count; i++)
            {
                var e = picture.Elements[i];
                float straight = RiverLines.Distance(e.X, e.Z, x, z);
                if (e.Mass <= 0f || straight >= reach) continue;
                bool afloat = e.Kind == PictureElementKind.AfloatLive || e.Kind == PictureElementKind.AfloatFaded;
                float d = afloat ? straight : RiverLines.EffectiveDistance(e.X, e.Z, x, z, probe, s.Crossings);
                weighted += e.Mass / (float)System.Math.Pow(System.Math.Max(VanillaThreatMinDistance, d), VanillaThreatExponent) * VanillaThreatScale;
            }
            return weighted;
        }

        // Afloat armies cannot be engaged by land (EstablishCampaignBattle 80632-80635).
        internal static bool Afloat(in EnemyBelief b)
        {
            return (b.Traits & EnemyTraits.Embarked) != 0;
        }

        // The sector a formation stands in: its closest AI area's sector, else the point lookup.
        internal static int SectorOf(SectorModel sectors, in FormationRecord f)
        {
            int sector = f.AreaIndex >= 0 ? sectors.SectorOfArea(f.AreaIndex) : -1;
            return sector >= 0 ? sector : sectors.SectorAt(f.X, f.Z);
        }

        // Index of the live land belief nearest (x, z), -1 when none: a sector's hold reserve and
        // hold order are measured from the army that sector faces.
        internal static int NearestLandBelief(CampaignSnapshot s, float x, float z)
        {
            int best = -1;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < s.Beliefs.Count; i++)
            {
                var b = s.Beliefs[i];
                if (Live(b) <= 0f || Afloat(b)) continue;
                float d = DistanceSq(b.X, b.Z, x, z);
                if (d >= bestDistance) continue;
                bestDistance = d;
                best = i;
            }
            return best;
        }

        // Indexes into s.Own ordered by squared distance to (x, z), the index breaking ties.
        internal static void SortByDistance(CampaignSnapshot s, List<int> order, float x, float z)
        {
            order.Sort((a, b) =>
            {
                int d = DistanceSq(s.Own[a].X, s.Own[a].Z, x, z).CompareTo(DistanceSq(s.Own[b].X, s.Own[b].Z, x, z));
                return d != 0 ? d : a.CompareTo(b);
            });
        }

        // The point a formation holds ground from: its position, except the map-group player's group,
        // which holds where its last waypoint lies (vanilla's own capital count reads GetLastWaypointPos,
        // AssignUnitToDefendCapital 11733-11740), so a player marching away stops covering the sector
        // he is leaving the day his order is given.
        internal static void HoldPoint(in FormationRecord f, out float x, out float z)
        {
            bool byWaypoint = f.Chain == WlChainClass.PlayerMapGroup && IsFinite(f.WaypointX) && IsFinite(f.WaypointZ);
            x = byWaypoint ? f.WaypointX : f.X;
            z = byWaypoint ? f.WaypointZ : f.Z;
        }

        // The sector a formation holds: SectorOf for the AI, the sector of the hold point for the
        // map-group player (falling back to his position when his waypoint is not a usable point, e.g. NaN).
        internal static int HoldSectorOf(SectorModel sectors, in FormationRecord f)
        {
            if (f.Chain != WlChainClass.PlayerMapGroup) return SectorOf(sectors, f);
            HoldPoint(f, out float x, out float z);
            int sector = sectors.SectorAt(x, z);
            return sector >= 0 ? sector : SectorOf(sectors, f);
        }

        // What a formation adds to a hold sum (WlChainRules.CountsTowardCoverage decides who counts): its
        // strength, and for the map-group player strength x morale, vanilla's own measure (11733-11740).
        internal static float HoldStrength(in FormationRecord f)
        {
            float strength = System.Math.Max(0f, f.Strength);
            return f.Chain == WlChainClass.PlayerMapGroup ? strength * SnapshotRules.Clamp01(f.Morale) : strength;
        }

        private static bool IsFinite(float v)
        {
            return !float.IsNaN(v) && !float.IsInfinity(v);
        }

        // A formation the allocator sends to Recover: movable, and below the raw supply floor (spec C21)
        // or the movement morale minimum. It is leaving for a supply source, so it holds nothing.
        internal static bool Recovers(in FormationRecord f, float minMorale)
        {
            return TheaterPlanner.Movable(f) && WlChainRules.MayMove(f.Chain)
                && (f.Supply01 < TheaterPlanner.RecoverSupplyBelow || f.Morale < minMorale);
        }

        // The mask of the formations (indexes into s.Own) whose Wished depot task the ledger holds: the capital reserve takes no guard
        // from them, in G3's plan and in the allocator alike (one rule). Null when there are none.
        internal static bool[] WishedBuilders(CampaignSnapshot s, IReadOnlyList<HeldBuilder> held)
        {
            if (held == null || held.Count == 0) return null;
            bool[] mask = null;
            for (int k = 0; k < held.Count; k++)
            {
                if (!held[k].Wished) continue;
                int i = s.IndexOfOwn(held[k].InstanceId);
                if (i < 0 || !WlChainRules.MayMove(s.Own[i].Chain)) continue;
                if (mask == null) mask = new bool[s.Own.Count];
                mask[i] = true;
            }
            return mask;
        }

        // The capital reserve (row 6 of the ownership table): vanilla's own capital defence (AssignUnitToDefendCapital 11725 and 11731-11741) counts
        // and draws only within GamePrefs.aimaximumdistancetosearchforunitrelocations (CampaignSnapshot.RelocationRange) of the capital, and so does
        // this reserve; nothing beyond that range is ever counted or drawn, and a floor the range cannot meet stays short (the [Reserves]
        // capital=held/floor row shows the shortfall) rather than pulling a formation across the map (the capital-floor pump, 2026-10-19).
        //   1. What already stands within the range (the map-group player's group at its last waypoint, HoldPoint) counts first, ready or not:
        //      every formation that counts toward coverage, is not leaving for supply (Recovers), and is no other owner's (a committed formation, a
        //      department move and a pinned builder are not counted; a busy one is, and so is a formation nobody owns). Nearest first, the ones the
        //      planner may move (WlChainRules.MayMove) are claimed Capital until the floor is met, so a guard that has lost readiness on arriving
        //      stays a guard.
        //   2. Only if that falls short of the floor are more drawn: the massable formations within the range, nearest first, from the claimants
        //      row 6 may take from (ClaimTable.Takes: no owner, committed, department move; home defence first).
        // Every Capital claim stays where the formation stands (the claim's target is its own place): a guard within the range is never marched
        // into the city, and one that was marching there is halted where it has got to. A formation the player commands is counted nowhere here
        // (CountsTowardCoverage) and claimed never.
        internal static void Capital(CampaignSnapshot s, float capitalFloor, Claim[] claims)
        {
            if (!s.HasCapital || capitalFloor <= 0f) return;
            float reachSq = s.RelocationRange * s.RelocationRange;
            var standing = new List<int>();
            var drawable = new List<int>();
            for (int i = 0; i < s.Own.Count; i++)
            {
                var f = s.Own[i];
                HoldPoint(f, out float hx, out float hz);
                if (DistanceSq(hx, hz, s.CapitalX, s.CapitalZ) > reachSq) continue;
                Claimant owner = claims[i].Claimant;
                if ((owner == Claimant.None || owner == Claimant.Immobile) && WlChainRules.CountsTowardCoverage(f.Chain) && !Recovers(f, s.MinMoraleForMovement))
                    standing.Add(i);
                if (TheaterPlanner.MassEligible(f, s.MinMoraleForMovement) && ClaimTable.Takes(ClaimTable.RowCapital, owner)) drawable.Add(i);
            }
            SortByDistance(s, standing, s.CapitalX, s.CapitalZ);
            float held = 0f;
            for (int k = 0; k < standing.Count && held < capitalFloor; k++)
            {
                int i = standing[k];
                held += HoldStrength(s.Own[i]);
                if (claims[i].Claimant != Claimant.None || !TheaterPlanner.Movable(s.Own[i]) || !WlChainRules.MayMove(s.Own[i].Chain)) continue;
                claims[i].Claimant = Claimant.Capital;
            }
            if (held >= capitalFloor) return;
            SortByDistance(s, drawable, s.CapitalX, s.CapitalZ);
            for (int k = 0; k < drawable.Count && held < capitalFloor; k++)
            {
                int i = drawable[k];
                if (claims[i].Claimant == Claimant.Capital) continue;
                claims[i].Claimant = Claimant.Capital;
                held += System.Math.Max(0f, s.Own[i].Strength);
            }
        }

        // Each threatened sector's hold reserve (row 7 of the ownership table): what holds there anyway counts first, then the massable
        // formations nearest the sector's believed army until the hold need is met. What holds counts the way vanilla counts it
        // (WlChainRules.CountsTowardCoverage, integrator decision 2026-10-15): a committed defender at its strength and the map-group
        // player's group at strength x morale, in the sector where its last waypoint lies (HoldSectorOf), so a player standing in a sector
        // releases the AI corps that would hold it and a player marching away stops covering it. The player's own command and a CIC side hold
        // nothing. A formation that Recovers is leaving for supply and counts 0 (G4 verify r2 N2). The player's group is never in the
        // massable list (TheaterPlanner.MassEligible), so it is never reserved.
        // Who counts, from the claims: a candidate is a massable formation nobody else owns (None) or a pinned builder; a formation that
        // holds anyway is an unowned, immobile or pinned formation the mass cannot use (the unready, the busy, the map-group player), and a
        // capital guard standing in the capital's own sector (a guard elsewhere marches to the capital and holds nothing where it stands).
        // A committed formation, a department move and a formation leaving for supply are counted in no sector, and neither is a Marching
        // depot builder (held): it is leaving for its site, and its claim stays None so the allocator's depot step finds it.
        // A reserve body is a Hold claim (a pinned builder keeps its claim and is counted); every formation the pass counts has its sector and
        // share written into its claim, the measure ForceAllocator tests a scout draw against. sectorHeld[k] receives, per sector with a need,
        // the sum of what holds there anyway plus the reserved formations.
        // covering is the covering force's input (foresight F6b-2, D11 requirement-covering-members), null when there is no force: the pass is then
        // the shipped one. With it the members are chosen here, in the same pass as the sector holds, from the axis sectors' own reserves:
        //   - the incumbents that are still claimable are taken first (CoveringForce.TakeIncumbents) and counted in their RECORDED sector wherever
        //     they stand, in no other: a sector that holds one stands short of the strength it lent and reserves another body in its place;
        //   - each axis sector (a share above 0) orders its other candidates by distance to the force's post, a formation of the post sector's
        //     home department closer by the allocator's discount, in place of the distance to the army it faces;
        //   - once every sector has its reserve, the members are taken across the axis sectors' bodies in one order, each sector lending at most its
        //     share (CoveringForce.PickMembers). A member stays claimed Hold by the sector that reserved it (reason covering-force), so no other
        //     front lost a formation to it and no sector is stripped.
        internal static void Hold(CampaignSnapshot s, SectorModel sectors, float[] need, float[] nearX, float[] nearZ, Claim[] claims,
                                  CoveringInput covering, IReadOnlyList<HeldBuilder> held, float[] sectorHeld)
        {
            int n = s.Own.Count;
            var order = new List<int>();
            int capitalSector = s.HasCapital ? sectors.SectorAt(s.CapitalX, s.CapitalZ) : -1;
            bool[] taken = covering != null ? CoveringForce.TakeIncumbents(s, claims, covering, held) : null;
            for (int sector = 0; sector < sectors.Count; sector++)
            {
                if (need[sector] <= 0f) continue;
                float total = covering != null && sector < covering.Seeded.Length ? covering.Seeded[sector] : 0f;
                order.Clear();
                for (int i = 0; i < n; i++)
                {
                    var f = s.Own[i];
                    if (taken != null && taken[i]) continue;
                    if (HoldSectorOf(sectors, f) != sector || IsMarchingBuilder(held, f.InstanceId)) continue;
                    Claimant owner = claims[i].Claimant;
                    if (TheaterPlanner.MassEligible(f, s.MinMoraleForMovement) && (owner == Claimant.None || owner == Claimant.PinnedBuilder))
                    {
                        order.Add(i);
                        continue;
                    }
                    bool holdsAnyway = owner == Claimant.None || owner == Claimant.Immobile || owner == Claimant.PinnedBuilder
                        || (owner == Claimant.Capital && sector == capitalSector);
                    if (holdsAnyway && WlChainRules.CountsTowardCoverage(f.Chain) && !Recovers(f, s.MinMoraleForMovement))
                    {
                        float share = HoldStrength(f);
                        total += share;
                        Count(ref claims[i], sector, share);
                    }
                }
                if (covering != null && sector < covering.AxisShare.Length && covering.AxisShare[sector] > 0f) CoveringForce.SortByPost(s, covering, order);
                else SortByDistance(s, order, nearX[sector], nearZ[sector]);
                for (int k = 0; k < order.Count && total < need[sector]; k++)
                {
                    float share = System.Math.Max(0f, s.Own[order[k]].Strength);
                    total += share;
                    if (claims[order[k]].Claimant == Claimant.None) claims[order[k]].Claimant = Claimant.Hold;
                    Count(ref claims[order[k]], sector, share);
                }
                sectorHeld[sector] = total;
            }
            if (covering != null) CoveringForce.PickMembers(s, claims, covering, held);
        }

        private static void Count(ref Claim claim, int sector, float share)
        {
            claim.SectorId = sector;
            claim.Share = share;
        }

        // Whether the depot ledger holds the formation as a builder of either kind (Marching or Wished): the depot's, not a mass's.
        internal static bool IsHeldBuilder(IReadOnlyList<HeldBuilder> held, int instanceId)
        {
            if (held == null) return false;
            for (int k = 0; k < held.Count; k++)
                if (held[k].InstanceId == instanceId) return true;
            return false;
        }

        // Whether the depot ledger holds the formation as a builder still on its way (Wished false): the hold counts it nowhere.
        internal static bool IsMarchingBuilder(IReadOnlyList<HeldBuilder> held, int instanceId)
        {
            if (held == null) return false;
            for (int k = 0; k < held.Count; k++)
                if (held[k].InstanceId == instanceId && !held[k].Wished) return true;
            return false;
        }

        internal static float DistanceSq(float ax, float az, float bx, float bz)
        {
            float dx = ax - bx;
            float dz = az - bz;
            return dx * dx + dz * dz;
        }
    }
}
