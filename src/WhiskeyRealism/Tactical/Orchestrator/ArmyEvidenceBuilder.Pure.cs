using System;
using System.Collections.Generic;
using WhiskeyRealism.Tactical.Battle;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    /// <summary>
    /// Pure half of <see cref="ArmyEvidenceBuilder"/>: the unit conversions and
    /// predicates the runtime reader applies to raw vanilla fields. Compiled into
    /// the test harness so the vanilla semantics are pinned by tests.
    /// </summary>
    internal static partial class ArmyEvidenceBuilder
    {
        private const int VanillaArtilleryUnitTyp = 2;

        /// <summary>
        /// Effective ammunition level on [0, 1] from vanilla <c>Regiment.ammo</c>
        /// (<c>float[]</c>, decompile 111498; initialised to 1f per slot at 112873,
        /// clamped to [0, 1] by <c>SetAmmoVariable</c> 117800). Vanilla reads
        /// <c>ammo[0]</c> for every non-artillery unit and <c>SumUp(ammo) / 3</c>
        /// for artillery (GetROF 21031/21038, fire loop 119609/119620, morale
        /// 128423, unit icon 121766). We apply the same rule.
        /// </summary>
        internal static float NormalizeAmmo01(float[] ammo, int unittyp, float fallback)
        {
            if (ammo == null || ammo.Length == 0) return fallback;

            float value;
            if (unittyp == VanillaArtilleryUnitTyp)
            {
                float sum = 0f;
                for (int i = 0; i < ammo.Length; i++) sum += ammo[i];
                value = sum / 3f;
            }
            else
            {
                value = ammo[0];
            }

            if (float.IsNaN(value) || float.IsInfinity(value)) return fallback;
            if (value < 0f) return 0f;
            if (value > 1f) return 1f;
            return value;
        }

        /// <summary>
        /// Leaf-unit "alive and on the field" predicate. Mirrors vanilla's
        /// <c>unitsonfield</c> count (84800: activeInHierarchy && !isrouted) plus
        /// a positive head count. <c>groupstrengthactive</c> must not be used on
        /// leaf regiments: vanilla only fills it via <c>UpdateGroupStrength</c>,
        /// which is called on groups (5890, 6517, 84828...).
        /// </summary>
        internal static bool IsAliveCombatUnit(int strength, bool activeInHierarchy, bool routed)
        {
            return strength > 0 && activeInHierarchy && !routed;
        }

        /// <summary>
        /// Hours until a scheduled reinforcement lands. Vanilla
        /// <c>ScheduledArrival.durationleft</c> is already in game hours: it is
        /// decremented by <c>currenttimefromstart - lastreinforcementarrivalcheck</c>
        /// (CheckReinforcementArrivals 87562-87584), and currenttimefromstart is
        /// in game hours.
        /// </summary>
        internal static float ArrivalHoursFromDurationLeft(float durationLeft)
        {
            if (float.IsNaN(durationLeft) || float.IsInfinity(durationLeft) || durationLeft < 0f) return 0f;
            return durationLeft;
        }

        /// <summary>
        /// Strength a scheduled reinforcement brings. Vanilla's own estimate is
        /// <c>regimentrefreg.groupstrength</c>, refreshed every side-stat cycle by
        /// <c>UpdateGroupStrength(overridecalc: true, ignoreactivestate: true)</c>
        /// (84828-84833 -> sideinformation.strengthtoarrive). The aigroup
        /// figure is only a fallback for formations vanilla has not summed yet.
        /// </summary>
        internal static float ArrivalStrength(int groupStrength, int groupStrengthAiGroup)
        {
            if (groupStrength > 0) return groupStrength;
            if (groupStrengthAiGroup > 0) return groupStrengthAiGroup;
            return 0f;
        }

        /// <summary>
        /// Default main-effort sector: the center band of the front (sectors
        /// are lateral bands, <see cref="TacticalFrontAxis"/>).
        /// </summary>
        internal static int DefaultMainEffortSector(TacticalFrontAxis axis)
            => axis.IsValid && axis.BandCount > 0 ? axis.CenterSector : TacticalFrontAxis.DefaultBandCount / 2;

        /// <summary>Lateral band of a world point; -1 when the axis has no bands.</summary>
        internal static int BandOfPoint(TacticalFrontAxis axis, float x, float z)
            => axis.IsValid && axis.BandCount > 0 ? axis.SectorOf(x, z) : -1;

        /// <summary>
        /// Index into <c>BattleUnits.aibattle[]</c> of the AIBattle that plays
        /// <paramref name="side"/>: <c>sideofai == side</c> and not the feud
        /// instance (<c>isplayeraiorfeud == 2</c>). Vanilla setup 85203-85212:
        /// aibattle[1] = player side, aibattle[0] = the other side, aibattle[2] =
        /// feud on side 0 (SetSide 3408). Each AIBattle's objectivechain only
        /// holds its own side's groups, so own reserves must come from this one.
        /// </summary>
        internal static int SelectAiBattleIndexForSide(int[] sideOfAi, int[] isPlayerAiOrFeud, int side)
        {
            if (sideOfAi == null) return -1;
            for (int i = 0; i < sideOfAi.Length; i++)
            {
                if (sideOfAi[i] != side) continue;
                if (isPlayerAiOrFeud != null && i < isPlayerAiOrFeud.Length && isPlayerAiOrFeud[i] == 2) continue;
                return i;
            }
            return -1;
        }

        /// <summary>
        /// Army morale relative to the start of the battle, on [0, 1]. Vanilla
        /// <c>sideinformation.averagemorale</c> is absolute (84771-84792), so a
        /// green army can start below an absolute floor; each regiment's
        /// <c>battlestartmorale</c> (110758, set at its first morale update to
        /// Max(morale, abovemoraleslowimprovement), 128154-128156) is the
        /// baseline. <paramref name="fallback"/> when no baseline is set yet.
        /// </summary>
        internal static float NormalizedMorale01(float sumMorale, float sumBattleStartMorale, float fallback)
        {
            if (!(sumBattleStartMorale > 0f) || float.IsNaN(sumMorale) || float.IsInfinity(sumMorale)) return fallback;
            float r = sumMorale / sumBattleStartMorale;
            if (r < 0f) return 0f;
            if (r > 1f) return 1f;
            return r;
        }

        // Vanilla terrain ids (BattlefieldSetup.GetCurrentTerrainOnPos 26727,
        // battleprefs "TERRAIN SPECIFICS"): meshes 0 grass, 3 creek, 4 river,
        // 5 river crossing, 6 bridge, 7 road, 8 fences/walls, 9-13 pontoons;
        // textures 14 forest, 15 swamp, 16 field.
        private const int TerrainCreek = 3;
        private const int TerrainRiver = 4;
        private const int TerrainRiverCrossing = 5;
        private const int TerrainForest = 14;
        private const int TerrainSwamp = 15;

        /// <summary>Forest/swamp share of the sampled ground that makes it a wooded fight.</summary>
        internal const float WoodedShare = 0.35f;

        /// <summary>
        /// Water share (creek/river/crossing) that makes a river the dominant
        /// feature: rivers are thin, so even a modest share of samples on water
        /// means the field is cut by one.
        /// </summary>
        internal const float RiverShare = 0.08f;

        /// <summary>Height range (world units) of the sampled ground that reads as mountainous.</summary>
        internal const float MountainRelief = 60f;

        /// <summary>
        /// Battlefield terrain for playbook selection, from terrain ids sampled
        /// over the side's front and the height range of those samples.
        /// </summary>
        internal static TerrainKind ClassifyTerrain(int[] terrainIds, float heightRange)
        {
            if (heightRange >= MountainRelief) return TerrainKind.Mountain;
            if (terrainIds == null || terrainIds.Length == 0) return TerrainKind.Open;
            int water = 0, wooded = 0;
            for (int i = 0; i < terrainIds.Length; i++)
            {
                int t = terrainIds[i];
                if (t == TerrainCreek || t == TerrainRiver || t == TerrainRiverCrossing) water++;
                else if (t == TerrainForest || t == TerrainSwamp) wooded++;
            }
            float n = terrainIds.Length;
            if (water / n >= RiverShare) return TerrainKind.River;
            if (wooded / n >= WoodedShare) return TerrainKind.Wooded;
            return TerrainKind.Open;
        }
    
        /// <summary>
        /// Army-wide odds against the enemy this side has had in range:
        /// 1 + confidence × (raw − 1), raw = own / estimate capped at
        /// <see cref="EnemyVisibleSector.MaxKnowableOdds"/>. Monotone in the
        /// ledger's confidence (observation coverage × recency), with no size
        /// floor and no cliff: a small enemy fully observed reads its real odds,
        /// a picket under low coverage reads close to unknown. The blend pulls
        /// an outnumbered side toward 1 under low confidence as well.
        /// </summary>
        internal static float KnownEnemyOdds(float ownStrength, TacticalEnemyContactLedger ledger, float nowDoctrineHours)
        {
            if (ledger == null || !(ownStrength > 0f)) return 1f;
            float enemyKnown = ledger.EstimatedEnemyStrength;
            if (!(enemyKnown > 0f)) return 1f;
            float raw = Math.Min(EnemyVisibleSector.MaxKnowableOdds, ownStrength / enemyKnown);
            return 1f + ledger.Confidence(nowDoctrineHours) * (raw - 1f);
        }

        /// <summary>
        /// The two ledger reads BuildForceBalance feeds the opportunity
        /// doctrine: the enemy estimate and the intel confidence in it.
        /// </summary>
        internal static void ForceBalanceIntel(TacticalEnemyContactLedger ledger, float nowDoctrineHours,
            out float enemyDeployed, out float intelConfidence)
        {
            enemyDeployed = ledger == null ? 0f : ledger.EstimatedEnemyStrength;
            intelConfidence = ledger == null ? 0f : ledger.Confidence(nowDoctrineHours);
        }

        /// <summary>
        /// Vanilla's weight for a <c>Regiment.outflanked</c> state (set 123151-123175:
        /// 1/2 one flank, 3 both flanks, 4-6 rear, 7 surrounded), as
        /// UpdateGroupStrength adds it to <c>flanksthreated</c> (120568-120580).
        /// </summary>
        internal static float VanillaOutflankedWeight(int outflanked)
        {
            switch (outflanked)
            {
                case 1: case 2: return 1f;
                case 3: return 2f;
                case 4: case 5: case 6: return 3f;
                case 7: return 4f;
                default: return 0f;
            }
        }

        // ---------- snapshot consumers (BC2b) ----------

        /// <summary>
        /// Enemy samples for one evidence build, from the snapshot's observations only (the
        /// side's fog-honest picture, BC2a). <paramref name="visible"/>: visible enemies at full
        /// strength, RecentFire set when the id is in <paramref name="engagedIds"/> (an own
        /// unit's fire-range list). <paramref name="sectorWeighted"/>: visible at full strength
        /// plus remembered weighted by confidence, the band strengths.
        /// <paramref name="known"/>: every visible or remembered position (coverage bands).
        /// Reinforcement columns are off the map and feed none of them. Lists are cleared first.
        /// </summary>
        internal static void CollectEnemySamples(IObservationSource source, HashSet<int> engagedIds,
            List<FrontUnitSample> visible, List<FrontUnitSample> sectorWeighted, List<FrontUnitSample> known)
        {
            visible.Clear();
            sectorWeighted.Clear();
            known.Clear();
            if (source == null) return;
            EnemyObservation[] observations = source.Observations;
            for (int i = 0; i < source.ObservationCount; i++)
            {
                ref EnemyObservation o = ref observations[i];
                if (o.IsReinforcement) continue;
                known.Add(new FrontUnitSample(o.InstanceId, o.X, o.Z, 0f, false));
                if (o.State == ObservationState.Visible)
                {
                    bool engaged = engagedIds != null && engagedIds.Contains(o.InstanceId);
                    visible.Add(new FrontUnitSample(o.InstanceId, o.X, o.Z, o.Strength, engaged));
                    sectorWeighted.Add(new FrontUnitSample(o.InstanceId, o.X, o.Z, o.Strength, engaged));
                }
                else
                {
                    sectorWeighted.Add(new FrontUnitSample(o.InstanceId, o.X, o.Z, o.Strength * o.Confidence01, false));
                }
            }
        }

        /// <summary>
        /// Army-wide average fatigue and ammo (vanilla rule already applied by the snapshot,
        /// UnitRecord.Ammo01) and morale relative to battle start, over alive combat
        /// regiments (unittyp 0-3; strength &gt; 0, active, not routed: vanilla unitsonfield,
        /// 84800). Defaults 0.2 / 0.9 / 1 when nothing is sampled.
        /// </summary>
        internal static void OwnAverages(UnitRecord[] own, int count, out float avgFatigue01, out float avgAmmo01, out float moraleRatio01)
        {
            avgFatigue01 = 0.2f;
            avgAmmo01 = 0.9f;
            moraleRatio01 = 1f;
            if (own == null) return;
            if (count > own.Length) count = own.Length;
            float fatigueSum = 0f, ammoSum = 0f, moraleSum = 0f, startMoraleSum = 0f;
            int sampled = 0;
            for (int i = 0; i < count; i++)
            {
                ref UnitRecord u = ref own[i];
                if (u.UnitTyp > 3) continue;
                if (!IsAliveCombatUnit(u.Strength, (u.Flags & UnitFlags.Active) != 0, (u.Flags & UnitFlags.Routed) != 0)) continue;
                float fatigue = u.Fatigue;
                if (float.IsNaN(fatigue) || float.IsInfinity(fatigue)) continue;
                fatigueSum += fatigue < 0f ? 0f : fatigue > 1f ? 1f : fatigue;
                ammoSum += u.Ammo01;
                if (u.BattleStartMorale > 0f && !float.IsNaN(u.Morale))
                {
                    moraleSum += u.Morale;
                    startMoraleSum += u.BattleStartMorale;
                }
                sampled++;
            }
            if (sampled == 0) return;
            avgFatigue01 = fatigueSum / sampled;
            avgAmmo01 = ammoSum / sampled;
            moraleRatio01 = NormalizedMorale01(moraleSum, startMoraleSum, 1f);
        }

        /// <summary>
        /// Own strength due within the AI's retreat-decision window: vanilla sums own units
        /// whose arrival is in (0, GamePrefs.reinforcementmaxtimeforairetreatdecisions) hours
        /// (CalculateSideStats 84764-84767). The side knows its own schedule.
        /// </summary>
        internal static float ReinforcementsArrivingWithin(IReadOnlyList<ReinforcementArrival> arrivals, float maxHours)
        {
            if (arrivals == null) return 0f;
            float total = 0f;
            for (int i = 0; i < arrivals.Count; i++)
            {
                float hours = arrivals[i].HoursUntilArrival;
                if (hours > 0f && hours < maxHours) total += arrivals[i].EstimatedStrength;
            }
            return total;
        }

        /// <summary>The soonest own arrival (hours, strength); (0, 0) when none is scheduled.</summary>
        internal static void NearestReinforcement(IReadOnlyList<ReinforcementArrival> arrivals, out float hours, out float strength)
        {
            hours = 0f;
            strength = 0f;
            if (arrivals == null || arrivals.Count == 0) return;
            float best = float.MaxValue;
            for (int i = 0; i < arrivals.Count; i++)
            {
                if (arrivals[i].HoursUntilArrival >= best) continue;
                best = arrivals[i].HoursUntilArrival;
                strength = arrivals[i].EstimatedStrength;
            }
            hours = best;
        }

        /// <summary>
        /// Vanilla's outflanked state (set 123151-123175) as a bit mask over the three flank
        /// slots: 1 one side, 2 the other, 4 rear, so 3 = both sides and 7 = surrounded.
        /// Here the slots are the snapshot's perceived flank pressure, not vanilla's
        /// range-list enemies (UpdateUnitRangeFast 122990-123015).
        /// </summary>
        internal static int OutflankedState(float leftPressure, float rightPressure, float rearPressure)
        {
            return (leftPressure > 0f ? 1 : 0) | (rightPressure > 0f ? 2 : 0) | (rearPressure > 0f ? 4 : 0);
        }

        /// <summary>
        /// Per own group, its flank threat on vanilla's 0-4 scale: the mean
        /// <see cref="VanillaOutflankedWeight"/> of its unrouted combat regiments at any depth
        /// (the descendants vanilla's group pass walks through allattachedunits, 120148), each
        /// regiment's state taken from the snapshot's flank pressure. Vanilla's own
        /// <c>flanksthreated</c> cannot be used: UpdateGroupStrength adds the group's OWN
        /// outflanked weight once per active subordinate (120273, 120568-120582), so a corps of
        /// forty read as surrounded whenever its HQ had one enemy on a flank. One walk up the
        /// parent chain per regiment. <paramref name="mean"/> and
        /// <paramref name="count"/> must hold OwnCount entries; units with no sampled
        /// regiment read 0.
        /// </summary>
        internal static void MeanFlankThreatByGroup(BattleSnapshot s, float[] mean, int[] count)
        {
            if (s == null || mean == null || count == null) return;
            int n = Math.Min(s.OwnCount, Math.Min(mean.Length, count.Length));
            Array.Clear(mean, 0, n);
            Array.Clear(count, 0, n);
            UnitRecord[] own = s.Own;
            for (int i = 0; i < n; i++)
            {
                ref UnitRecord u = ref own[i];
                if (u.UnitTyp > TacticalUnitType.MaxCombat || u.Strength <= 0 || (u.Flags & UnitFlags.Routed) != 0) continue;
                float weight = VanillaOutflankedWeight(OutflankedState(s.FlankPressure(i, 0), s.FlankPressure(i, 1), s.FlankPressure(i, 2)));
                int parent = u.ParentIndex;
                for (int steps = 0; parent >= 0 && parent < n && steps < n; steps++)
                {
                    mean[parent] += weight;
                    count[parent]++;
                    parent = own[parent].ParentIndex;
                }
            }
            for (int i = 0; i < n; i++)
                if (count[i] > 0) mean[i] /= count[i];
        }

        /// <summary>Men per bucket of known enemy strength in the heavy-gate signature.</summary>
        internal const float KnownEnemyStrengthBucketMen = 500f;

        /// <summary>
        /// The heavy-gate signature's enemy term: the side's visible enemy strength in
        /// <see cref="KnownEnemyStrengthBucketMen"/> buckets, so only a meaningful change in
        /// what is seen re-runs the heavy path (the enemy's true totalactiveforce is unknowable).
        /// </summary>
        internal static int KnownEnemyStrengthBucket(float visibleEnemyStrength)
        {
            if (!(visibleEnemyStrength > 0f) || float.IsInfinity(visibleEnemyStrength)) return 0;
            return (int)(visibleEnemyStrength / KnownEnemyStrengthBucketMen);
        }

        /// <summary>
        /// The heavy-gate signature's contact term over the side's own units: a unit is in
        /// contact when in melee or when the first enemy of its range list
        /// (UnitRecord.FirstEnemyInRangeId, enemyinrangereg[0]) is visible to the side.
        /// Buckets: 0 none, 1 for 1-3, 2 for 4-10, 3 above.
        /// </summary>
        internal static int ContactBucket(BattleSnapshot s)
        {
            if (s == null) return 0;
            int contacts = 0;
            UnitRecord[] own = s.Own;
            for (int i = 0; i < s.OwnCount; i++)
            {
                if ((own[i].Flags & UnitFlags.InMelee) != 0
                    || (own[i].FirstEnemyInRangeId != 0 && s.IsVisible(own[i].FirstEnemyInRangeId)))
                    contacts++;
            }
            if (contacts <= 0) return 0;
            if (contacts <= 3) return 1;
            if (contacts <= 10) return 2;
            return 3;
        }

        /// <summary>
        /// Enemy pressure around one own unit: the sum of its snapshot slice pressure (visible
        /// enemies plus discounted remembered ones within aidefensivemaxrange, BC2a). Replaces
        /// the total of vanilla's unfiltered per-unit angle slices (122707-122732).
        /// </summary>
        internal static float SlicePressureTotal(BattleSnapshot s, int ownIndex)
        {
            if (s == null || ownIndex < 0 || ownIndex >= s.OwnCount) return 0f;
            float total = 0f;
            for (int k = 0; k < s.SliceCount; k++) total += s.SlicePressure(ownIndex, k);
            return total;
        }

        /// <summary>
        /// Any perceived pressure on the unit's left, right or rear flank point. Replaces
        /// vanilla's flanksthreated / outflanked, which come from unfiltered range lists.
        /// </summary>
        internal static bool HasFlankPressure(BattleSnapshot s, int ownIndex)
        {
            if (s == null || ownIndex < 0 || ownIndex >= s.OwnCount) return false;
            return s.FlankPressure(ownIndex, 0) > 0f || s.FlankPressure(ownIndex, 1) > 0f || s.FlankPressure(ownIndex, 2) > 0f;
        }
    }
}
