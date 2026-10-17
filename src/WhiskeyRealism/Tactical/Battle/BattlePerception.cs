// Battle perception: mode decision and enemy memory (BC2a, plan
// docs/superpowers/plans/2026-10-15-battle-core.md, spec §2, R1/R2 in
// docs/vanilla-vision-and-movement.md). Pure: BattlePerceptionProbe reads vanilla and
// hands this file a PerceptionProbeResult; Publish writes the result into BC1's
// BattleSnapshot through its perception setters.
//
// Vanilla anchors (build 14703111):
// - FogOfWar keeps one grid per side; an AI-run side's units hold fows[1] in Regiment.fow
//   (CreateFOWObjects 116505-116518). The AI grid is painted every frame but no vanilla
//   decision reads it (UpdateFOW 100675-100714).
// - InitFOW returns before painting anything when !GameVars.usefow (100755), and only the
//   drawn grid (fows[0]) gets the init ClearFOW (100785), so fows[1] can show everything for
//   the first cycle. firstrunfinished flips after the first full state-machine pass
//   (100690-100694). Hence the startup window below.
// - In battle with GameVars.hqinthesaddle every viewer outside the W&L player's command
//   paints radius 0 (UpdateViewRange 101312), so fows[1] stays blank.
// - Regiment visibility tick 127209-127239: iswithinfog is "hidden from the player side"
//   (tested against fows[0] at 127225); melee always reveals (127216, 127231). A
//   player-alliance AI side (W&L, ai_vs_ai) therefore reads !enemy.iswithinfog.
// - Vanilla hides a cell only after fow_hideunhideconfirmations (5) cycles of hysteresis
//   (100880-100897); HideConfirmTicks mirrors that on doctrine ticks.

using System;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Tactical.Battle
{
    /// <summary>
    /// One tick of raw visibility, filled by the runtime probe. Arrays are parallel and
    /// valid on [0, Count). Positions and strengths are the probe's true values; only
    /// rows with <see cref="Visible"/> set may reach memory.
    /// </summary>
    public struct PerceptionProbeResult
    {
        public int Count;
        public int[] EnemyIds;
        public bool[] Visible;
        public float[] X, Z, Strength;
        public int[] UnitTyp;
        public bool[] IsReinforcement;
        public bool[] Routed;          // visible routed enemies leave memory (out of the fight)
        public float[] ArrivalHours;   // reinforcement rows: ScheduledArrival.durationleft
        public float[] Experience;     // true Experience() of the row (BC8); memory keeps its star value when visible
        public float[] Morale;         // true morale of the row (BC8); memory keeps it when visible
        // BC10: the row's facing, fire range, cover and standing flag (no paths); memory keeps
        // them while the row is visible.
        public float[] Heading, FireRange, CoverValue;
        public int[] CoverObject;
        public bool[] Standing;

        public static PerceptionProbeResult Create(int capacity)
        {
            var result = new PerceptionProbeResult();
            EnsureCapacity(ref result, capacity);
            return result;
        }

        public int Capacity => EnemyIds == null ? 0 : EnemyIds.Length;

        /// <summary>Grow-only; keeps rows [0, Count).</summary>
        public static void EnsureCapacity(ref PerceptionProbeResult result, int capacity)
        {
            if (result.Capacity >= capacity) return;
            int size = Math.Max(capacity, Math.Max(16, result.Capacity * 2));
            Array.Resize(ref result.EnemyIds, size);
            Array.Resize(ref result.Visible, size);
            Array.Resize(ref result.X, size);
            Array.Resize(ref result.Z, size);
            Array.Resize(ref result.Strength, size);
            Array.Resize(ref result.UnitTyp, size);
            Array.Resize(ref result.IsReinforcement, size);
            Array.Resize(ref result.Routed, size);
            Array.Resize(ref result.ArrivalHours, size);
            Array.Resize(ref result.Experience, size);
            Array.Resize(ref result.Morale, size);
            Array.Resize(ref result.Heading, size);
            Array.Resize(ref result.FireRange, size);
            Array.Resize(ref result.CoverValue, size);
            Array.Resize(ref result.CoverObject, size);
            Array.Resize(ref result.Standing, size);
        }
    }

    /// <summary>
    /// Per side, the fog grid already seen to have finished its first full pass
    /// (FogOfWar.firstrunfinished, 100690-100694), so the probe stops its boxed reflected
    /// read once the answer is true. One slot per side: under W&L or ai_vs_ai both sides
    /// perceive through different grids (fows[0] / fows[1], 116505-116518), and a shared
    /// slot would alternate between them and re-read every tick.
    /// </summary>
    public sealed class PaintedGridLatch
    {
        private readonly object[] _painted;

        public PaintedGridLatch(int sides)
        {
            _painted = new object[Math.Max(0, sides)];
        }

        public bool IsPainted(int side, object grid)
        {
            return grid != null && side >= 0 && side < _painted.Length && ReferenceEquals(_painted[side], grid);
        }

        public void MarkPainted(int side, object grid)
        {
            if (side >= 0 && side < _painted.Length) _painted[side] = grid;
        }

        public void Reset()
        {
            Array.Clear(_painted, 0, _painted.Length);
        }
    }

    /// <summary>
    /// BC15 decision 2(a): where observations that were still blocking a group left memory since the tier pass last read it (an enemy seen
    /// routing; an expiry at 90 minutes is below the blocking confidence and is no change). A dropped entry cannot carry a latch, so its
    /// last position is logged here, bounded: more than <see cref="MaxEntries"/> sets <see cref="Overflow"/> and the tier pass wakes
    /// every group. Allocated once per side.
    /// </summary>
    public sealed class ObservationDropLog
    {
        public const int MaxEntries = 128;

        public readonly float[] X = new float[MaxEntries];
        public readonly float[] Z = new float[MaxEntries];
        public int Count;
        public bool Overflow;

        public void Add(float x, float z)
        {
            if (Count < MaxEntries)
            {
                X[Count] = x;
                Z[Count] = z;
                Count++;
            }
            else
            {
                Overflow = true;
            }
        }

        public void Clear()
        {
            Count = 0;
            Overflow = false;
        }
    }

    public static class BattlePerception
    {
        public const float FogStartupSeconds = 5f;
        public const float RememberedHalfLifeMinutes = 20f;
        public const float RememberedMaxMinutes = 90f;
        public const int HideConfirmTicks = 2;
        public const float RememberedWeightFloor = 0.05f; // confidence floor until the 90-minute cap

        // Marks an entry for removal inside Update; vanilla instance ids are never 0.
        private const int DroppedId = 0;

        // Only the FogGrid probe asks UnitIsHidden for an AI side about the player's units; the
        // player-alliance side (OwnSideWithinFog) reads vanilla's own verdict, which the
        // player-intelligence incident (PlayerIntelIncidents) is meant to open.
        internal static bool MasksPlayerIntelIncidents(PerceptionMode mode, bool dlcScenarioActive)
        {
            return PlayerIntelIncidents.Masks(mode == PerceptionMode.FogGrid, dlcScenarioActive);
        }

        /// <summary>
        /// Order: config off, fog off, player-alliance side (vanilla's own iswithinfog, which
        /// already applies the HQ-in-the-saddle rule and is cleared at init), HQ in the
        /// saddle, grid not painted yet, else the AI fog grid.
        /// </summary>
        public static PerceptionMode DecideMode(bool configOn, bool useFow, bool hqInTheSaddleBattle, bool gridFirstRunFinished,
            double secondsSinceBattleStart, bool sideIsPlayerAlliance, out PerceptionFallbackReason reason)
        {
            reason = PerceptionFallbackReason.None;
            if (!configOn)
            {
                reason = PerceptionFallbackReason.ConfigOff;
                return PerceptionMode.RangeOnly;
            }
            if (!useFow)
            {
                reason = PerceptionFallbackReason.FogDisabled;
                return PerceptionMode.RangeOnly;
            }
            if (sideIsPlayerAlliance)
                return PerceptionMode.OwnSideWithinFog;
            if (hqInTheSaddleBattle)
            {
                reason = PerceptionFallbackReason.HqInTheSaddle;
                return PerceptionMode.RangeOnly;
            }
            if (!gridFirstRunFinished || secondsSinceBattleStart < FogStartupSeconds)
            {
                reason = PerceptionFallbackReason.GridNotPainted;
                return PerceptionMode.RangeOnly;
            }
            return PerceptionMode.FogGrid;
        }

        /// <summary>
        /// OnceLog key for the first [BattlePerception] row in a mode. Static literals with the
        /// "tactical" prefix, so OnceLog.ResetTactical re-arms them at battle end.
        /// </summary>
        public static string ModeLogKey(PerceptionMode mode)
        {
            switch (mode)
            {
                case PerceptionMode.FogGrid: return "tactical.battle-perception:mode:FogGrid";
                case PerceptionMode.OwnSideWithinFog: return "tactical.battle-perception:mode:OwnSideWithinFog";
                default: return "tactical.battle-perception:mode:RangeOnly";
            }
        }

        /// <summary>
        /// RangeOnly reach of one own unit: the range vanilla's AI enemy lists use,
        /// max(raycastrange[0] = maximumlineofsight[unittyp], distancetoenemytocancelinterruption)
        /// (InitMaxUnitRanges 122508-122513, UpdateUnitRangeFast 122608).
        /// </summary>
        public static float RangeOnlyReach(float lineOfSight, float cancelInterruptionDistance)
        {
            return Math.Max(lineOfSight, cancelInterruptionDistance);
        }

        /// <summary>
        /// A group's enemies-in-range reach (#44 sector strength, the executor's
        /// formation policy): RangeOnlyReach over the widest combat line of sight
        /// a group holds (unit types infantry..skirmisher; GamePrefs.maximumlineofsight
        /// is per unittyp). Vanilla's groupenemiesinrange reads the group's own
        /// enemiesinrange list (UpdateGroupStrength 120558/120563).
        /// </summary>
        public static float GroupRangeReach(float[] lineOfSightByType, float cancelInterruptionDistance)
        {
            float sight = 0f;
            if (lineOfSightByType != null)
            {
                for (int t = 0; t <= WhiskeyRealism.Tactical.TacticalUnitType.Skirmisher && t < lineOfSightByType.Length; t++)
                    if (lineOfSightByType[t] > sight) sight = lineOfSightByType[t];
            }
            return RangeOnlyReach(sight, cancelInterruptionDistance);
        }

        /// <summary>True when (x, z) lies within any own unit's reach, own units [0, count).</summary>
        public static bool WithinAnyReach(float[] ownX, float[] ownZ, float[] reach, int count, float x, float z)
        {
            for (int i = 0; i < count; i++)
            {
                float dx = ownX[i] - x;
                float dz = ownZ[i] - z;
                if (dx * dx + dz * dz <= reach[i] * reach[i]) return true;
            }
            return false;
        }

        /// <summary>0.5^(minutes / 20), never below <see cref="RememberedWeightFloor"/>; 0 at or
        /// beyond 90 minutes, which is the only point a remembered enemy is dropped.</summary>
        public static float Confidence(double sinceSeenHours)
        {
            double minutes = sinceSeenHours * 60.0;
            if (minutes <= 0.0) return 1f;
            if (minutes >= RememberedMaxMinutes) return 0f;
            return Math.Max(RememberedWeightFloor, (float)Math.Pow(0.5, minutes / RememberedHalfLifeMinutes));
        }

        /// <summary>
        /// Folds one probe into the side's memory. Every entry first counts one hidden tick;
        /// visible probe rows then refresh their entry (or add one). An entry becomes
        /// Remembered after <see cref="HideConfirmTicks"/> hidden ticks, keeps its last
        /// visible position and strength, decays on the doctrine clock and is dropped at
        /// <see cref="RememberedMaxMinutes"/>. A hidden enemy never seen stays unknown, a
        /// routed enemy seen routing leaves memory, and a reinforcement column is known only
        /// while its entry point is visible. Allocation-free; new entries beyond
        /// memory.Length are not recorded.
        /// </summary>
        public static void Update(EnemyObservation[] memory, ref int memoryCount, in PerceptionProbeResult probe, double nowHours,
            ObservationDropLog drops = null)
        {
            if (memory == null) return;
            int count = Math.Min(memoryCount, memory.Length);

            for (int j = 0; j < count; j++)
            {
                if (memory[j].HiddenTicks < int.MaxValue) memory[j].HiddenTicks++;
            }

            int cursor = 0;
            for (int i = 0; i < probe.Count; i++)
            {
                if (!probe.Visible[i]) continue;
                int id = probe.EnemyIds[i];
                if (id == DroppedId) continue;
                int index = Find(memory, count, id, ref cursor);
                if (probe.Routed[i])
                {
                    if (index >= 0)
                    {
                        NoteDrop(drops, in memory[index]);
                        memory[index].InstanceId = DroppedId;
                    }
                    continue;
                }
                // BC15 decision 2(a): a new entry, or one that was only Remembered, is a perception change.
                bool changed = index < 0 || memory[index].State != ObservationState.Visible;
                if (index < 0)
                {
                    if (count >= memory.Length) continue;
                    index = count++;
                    memory[index].InstanceId = id;
                }
                if (changed) memory[index].ChangedSinceLastTier = true;
                memory[index].UnitTyp = probe.UnitTyp[i];
                memory[index].X = probe.X[i];
                memory[index].Z = probe.Z[i];
                memory[index].Strength = probe.Strength[i];
                memory[index].State = ObservationState.Visible;
                memory[index].LastSeenHours = nowHours;
                memory[index].Confidence01 = 1f;
                memory[index].HiddenTicks = 0;
                memory[index].IsReinforcement = probe.IsReinforcement[i];
                // Fog rule (BC8): only a visible regiment's stars and morale reach memory.
                memory[index].Experience = probe.IsReinforcement[i] ? 0f : TroopQuality.PerceivedExperience(probe.Experience[i]);
                memory[index].Morale = probe.IsReinforcement[i] ? 0f : probe.Morale[i];
                memory[index].Heading = probe.Heading[i];
                memory[index].FireRange = probe.FireRange[i];
                memory[index].CoverValue = probe.CoverValue[i];
                memory[index].CoverObject = probe.CoverObject[i];
                memory[index].Standing = probe.Standing[i];
            }

            int write = 0;
            for (int j = 0; j < count; j++)
            {
                EnemyObservation entry = memory[j];
                if (entry.InstanceId == DroppedId) continue;
                if (entry.HiddenTicks > 0)
                {
                    if (entry.IsReinforcement) continue;
                    if (entry.HiddenTicks >= HideConfirmTicks)
                    {
                        if (entry.State == ObservationState.Visible) entry.ChangedSinceLastTier = true;
                        entry.State = ObservationState.Remembered;
                    }
                }
                if (entry.State == ObservationState.Remembered)
                {
                    entry.Confidence01 = Confidence(nowHours - entry.LastSeenHours);
                    if (entry.Confidence01 <= 0f) continue;
                }
                memory[write++] = entry;
            }
            memoryCount = write;
        }

        // The entry stopped blocking: log where, when it was Visible or a memory at or above the blocking confidence.
        private static void NoteDrop(ObservationDropLog drops, in EnemyObservation entry)
        {
            if (drops == null || entry.IsReinforcement) return;
            if (entry.State == ObservationState.Visible || entry.Confidence01 >= EnemySamples.RememberedMinConfidence)
                drops.Add(entry.X, entry.Z);
        }

        /// <summary>
        /// The tier pass has read the changes: clears every latch and the drop log. Called once per arbiter tick after BattleLod.Classify
        /// (never from Update, so a build between two arbiter ticks does not lose a change).
        /// </summary>
        public static void ClearChangeLatch(EnemyObservation[] memory, int memoryCount, ObservationDropLog drops)
        {
            if (memory != null)
            {
                int count = Math.Min(memoryCount, memory.Length);
                for (int j = 0; j < count; j++) memory[j].ChangedSinceLastTier = false;
            }
            drops?.Clear();
        }

        /// <summary>
        /// Writes one tick of perception into a snapshot the builder has just begun: mode,
        /// one observation per memory entry, the enemy summary (visible strength, remembered
        /// strength weighted by confidence, strength-weighted visible centroid; reinforcement
        /// columns excluded), the perceived enemy quality (BC8) and the reinforcement summary (columns at visible entry points,
        /// from this tick's probe because arrival hours are not part of an observation).
        /// Call once per build: observations are appended.
        /// </summary>
        public static void Publish(BattleSnapshot s, PerceptionMode mode, PerceptionFallbackReason reason,
            EnemyObservation[] memory, int memoryCount, in PerceptionProbeResult probe, ObservationDropLog drops = null)
        {
            if (s == null) return;
            s.SetPerception(mode, reason);
            s.SetDroppedObservations(drops);

            int count = memory == null ? 0 : Math.Min(memoryCount, memory.Length);
            float visible = 0f, remembered = 0f, sumX = 0f, sumZ = 0f;
            for (int i = 0; i < count; i++)
            {
                s.AddObservation(in memory[i]);
                if (memory[i].IsReinforcement) continue;
                float strength = memory[i].Strength;
                if (memory[i].State == ObservationState.Visible)
                {
                    visible += strength;
                    sumX += memory[i].X * strength;
                    sumZ += memory[i].Z * strength;
                }
                else
                {
                    remembered += strength * memory[i].Confidence01;
                }
            }
            s.SetEnemySummary(visible, remembered, visible > 0f ? sumX / visible : 0f, visible > 0f ? sumZ / visible : 0f);
            s.SetPerceivedEnemyQuality(TroopQuality.PerceivedQuality(memory, count));

            // Starts from BeginBuild's "none seen" value, so the sentinel lives in one place.
            float reinforcements = 0f;
            float nearestHours = s.NearestVisibleReinforcementHours;
            for (int i = 0; i < probe.Count; i++)
            {
                if (!probe.IsReinforcement[i] || !probe.Visible[i]) continue;
                reinforcements += probe.Strength[i];
                if (probe.ArrivalHours[i] < nearestHours) nearestHours = probe.ArrivalHours[i];
            }
            s.SetReinforcementSummary(reinforcements, nearestHours);
        }

        // Probe rows arrive in the roster's stable order, so the next match is usually just
        // past the previous one.
        private static int Find(EnemyObservation[] memory, int count, int id, ref int cursor)
        {
            for (int k = 0; k < count; k++)
            {
                int j = cursor + k;
                if (j >= count) j -= count;
                if (memory[j].InstanceId != id) continue;
                cursor = j + 1;
                return j;
            }
            return -1;
        }
    }
}
