using System;
using System.Collections.Generic;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Strategic.Planner
{
    // The AI side's spotting marks (foresight F4, D11 requirement-marks-ai-side): vanilla gives the AI no mark channel (AddUnit returns for
    // the player's alliance only, 142938), so each AI alliance keeps its own ledger with vanilla's formula on its own grid. One entry per
    // enemy group key holds the newest mark: where it was shown, its offset from the truth, the floating date and day it was made and the
    // intelligence it was made at. Pure and deterministic given the caller's System.Random (MarkRules.RngFor); the runtime supplies the
    // pref values, the live group and the masked grid. Per campaign, not persisted: a load restarts the marks from the next review.
    internal sealed class MarkLedger
    {
        private sealed class Entry
        {
            public float X, Z;               // the mark's position (the group's true position then, plus the offset)
            public float OffsetX, OffsetZ;   // the offset, for the next mark's blend (142706)
            public float DateYears;          // GameVars.GetCampaignFloatingDate when it was made
            public int Day;                  // the review day it was made
            public float Intel;              // the intelligence at its position (the radius reads this, not today's)
            public float LastX, LastZ;        // the group's true position at the last evaluation (the path the interval's gate is measured on)
            public int MissedReviews;
        }

        private readonly Dictionary<string, Entry> _entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
        private readonly List<string> _scratch = new List<string>();

        internal int Count => _entries.Count;

        // Foresight F4b: the marks made and the hidden groups judged since BeginReview, for the [EnemyBeliefs] marksNew= / hiddenGroups= counts.
        internal int Created { get; private set; }
        internal int Judged { get; private set; }

        internal void BeginReview()
        {
            Created = 0;
            Judged = 0;
        }

        // One hidden enemy group, once per review. True while a newest mark is held, new or old; mark then carries position, day, radius and
        // the date's day (the runtime fills strength, type, id and traits). gridHidden is the masked one-argument UnitIsHidden of the side's
        // grid (true: hidden from the side); null means the grid is missing or broken: no new mark, the stored one is returned.
        // Order of events as vanilla's UpdateMarks (142773-142906): the grid seeing the newest mark drops it (142800-142807) and ends the
        // evaluation; otherwise the variation draw (142819), the chance, the roll (142866) and, on a hit past the distance gate, the offset
        // draws (angle, magnitude; 142700-142701) and the refusal when the grid sees the new position (142724).
        internal bool Evaluate(string unitKey, float x, float z, int daySerial, float dateYears, in MarkInputs inputs, Random rng,
                               Func<float, float, bool> gridHidden, out EnemyDetection mark)
        {
            mark = default(EnemyDetection);
            if (unitKey == null) return false;
            Judged++;
            _entries.TryGetValue(unitKey, out Entry entry);
            if (entry != null && gridHidden != null && !gridHidden(entry.X, entry.Z))
            {
                _entries.Remove(unitKey);
                return false;
            }
            if (gridHidden != null && rng != null)
            {
                float draw = MarkRules.VariationDraw(rng, inputs.SpottingVariation);
                float elapsed = dateYears - (entry != null ? entry.DateYears : 0f);
                float fromX = entry != null ? entry.X : 0f, fromZ = entry != null ? entry.Z : 0f;   // vanilla's default(Vector3) before the first mark
                float moved = (float)Math.Sqrt((x - fromX) * (x - fromX) + (z - fromZ) * (z - fromZ));
                float chance;
                bool pastGate;
                if (inputs.UseInterval)
                {
                    // The variation draw is taken at its mean (1); it is still consumed above so a group's stream is the same either way.
                    float gate = entry != null ? MarkRules.GateFraction(entry.LastX, entry.LastZ, x, z, entry.X, entry.Z, inputs.MinDistance) : 0f;
                    float timeConstant = inputs.Intel * inputs.Spotting1 + (1f - inputs.Intel) * inputs.Spotting0;
                    chance = MarkRules.IntervalChance(elapsed - inputs.IntervalYears, inputs.IntervalYears, inputs.Ticks, timeConstant, inputs.PerkFactor, inputs.Cap, gate);
                    pastGate = true;   // the ticks past the gate are already in the chance
                }
                else
                {
                    chance = MarkRules.CreationChance(elapsed, inputs.Intel, inputs.Spotting0, inputs.Spotting1, draw, inputs.PerkFactor, inputs.Cap);
                    pastGate = moved >= inputs.MinDistance;
                }
                if (!inputs.Active || inputs.Garrison || inputs.Fleet || !inputs.HasClosestEnemy) chance = 0f;
                double roll = rng.NextDouble();
                if (chance > roll && pastGate)
                {
                    float angle = inputs.HasClosestEnemy ? MarkRules.ReverseBearing(x, z, inputs.ClosestEnemyX, inputs.ClosestEnemyZ) : 0f;
                    MarkRules.Offset(rng, inputs.Intel, new[] { inputs.DistanceVariation0, inputs.DistanceVariation1 }, inputs.HasClosestEnemy, angle,
                        entry != null, entry != null ? dateYears - entry.DateYears : 0f, entry != null ? entry.OffsetX : 0f, entry != null ? entry.OffsetZ : 0f,
                        out float dx, out float dz);
                    if (gridHidden(x + dx, z + dz))
                    {
                        if (entry == null) { entry = new Entry(); _entries[unitKey] = entry; }
                        entry.X = x + dx;
                        entry.Z = z + dz;
                        entry.OffsetX = dx;
                        entry.OffsetZ = dz;
                        entry.DateYears = dateYears;
                        entry.Day = daySerial;
                        entry.Intel = inputs.Intel;
                        Created++;
                    }
                }
            }
            if (entry == null) return false;
            entry.LastX = x;
            entry.LastZ = z;
            entry.MissedReviews = 0;
            mark = new EnemyDetection
            {
                UnitKey = unitKey,
                X = entry.X,
                Z = entry.Z,
                Source = DetectionSource.Mark,
                MarkDay = entry.Day,
                MarkRadius = MarkRules.MarkRadius(entry.Intel, new[] { inputs.DistanceVariation0, inputs.DistanceVariation1 })
            };
            return true;
        }

        // The group was detected by rule a-d or entered battle: vanilla removes every mark (142800-142807).
        internal void Drop(string unitKey)
        {
            if (unitKey != null) _entries.Remove(unitKey);
        }

        // An entry whose group is absent from the enemy list for PlannerState.PruneAfterMissedReviews reviews in a row is dropped.
        internal void PruneUnseen(ICollection<string> presentKeys)
        {
            _scratch.Clear();
            foreach (var pair in _entries)
            {
                if (presentKeys != null && presentKeys.Contains(pair.Key)) continue;
                pair.Value.MissedReviews++;
                if (pair.Value.MissedReviews >= PlannerState.PruneAfterMissedReviews) _scratch.Add(pair.Key);
            }
            for (int i = 0; i < _scratch.Count; i++) _entries.Remove(_scratch[i]);
        }

        internal void Clear()
        {
            _entries.Clear();
        }
    }

    // The per-alliance mark ledgers (alliances 0 and 1 only). Per campaign: cleared by CampaignScope.
    internal static class MarkLedgers
    {
        private static readonly MarkLedger[] Ledgers = { new MarkLedger(), new MarkLedger() };

        static MarkLedgers()
        {
            CampaignScope.Register("campaign-planner-mark-ledgers", Reset);
        }

        private static void Reset()
        {
            for (int a = 0; a < Ledgers.Length; a++) Ledgers[a].Clear();
        }

        internal static MarkLedger For(int alliance)
        {
            if (alliance < 0 || alliance >= Ledgers.Length) return null;
            return Ledgers[alliance];
        }
    }
}
