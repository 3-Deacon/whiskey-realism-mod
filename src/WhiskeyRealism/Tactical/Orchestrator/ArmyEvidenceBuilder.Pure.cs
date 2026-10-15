using System;

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
        /// One value per side per frame for one battle. Build runs from up to
        /// four call sites per side per coordinator tick (replan, heavy path,
        /// snapshot, parity cycle); the memo lets every site after the first
        /// share the result instead of re-walking completeunitlist.
        /// </summary>
        internal sealed class PerSideFrameMemo<T>
        {
            private readonly T[] _values = new T[2];
            private readonly int[] _frames = { -1, -1 };
            private readonly WeakReference<object>[] _owners = new WeakReference<object>[2];

            internal bool TryGet(object owner, int side, int frame, out T value)
            {
                value = default(T);
                if (owner == null || side < 0 || side > 1 || frame < 0) return false;
                if (_frames[side] != frame || _owners[side] == null) return false;
                if (!_owners[side].TryGetTarget(out var cached) || !ReferenceEquals(cached, owner)) return false;
                value = _values[side];
                return true;
            }

            internal void Store(object owner, int side, int frame, T value)
            {
                if (owner == null || side < 0 || side > 1 || frame < 0) return;
                _values[side] = value;
                _frames[side] = frame;
                _owners[side] = new WeakReference<object>(owner);
            }

            internal void Reset()
            {
                for (int i = 0; i < 2; i++)
                {
                    _values[i] = default(T);
                    _frames[i] = -1;
                    _owners[i] = null;
                }
            }
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

        /// <summary>
        /// A group's flank threat on vanilla's 0-4 scale: the mean outflanked
        /// weight over its combat regiments. Vanilla's own
        /// <c>flanksthreated</c> cannot be used: UpdateGroupStrength adds the
        /// group's OWN outflanked weight once per active subordinate
        /// (120273 loop over allattachedunits, 120568-120582), so it scales with
        /// the size of the formation, and a corps of forty read as surrounded
        /// whenever its HQ had one enemy on a flank.
        /// </summary>
        internal static float MeanOutflankedWeight(int[] outflankedStates, int count)
        {
            if (outflankedStates == null || count <= 0) return 0f;
            if (count > outflankedStates.Length) count = outflankedStates.Length;
            float sum = 0f;
            for (int i = 0; i < count; i++) sum += VanillaOutflankedWeight(outflankedStates[i]);
            return sum / count;
        }
    }
}
