using System;
using System.Collections.Generic;
using WhiskeyRealism.Tactical.Orchestrator;
using WhiskeyRealism.Tactical.Orchestrator.Plays;

namespace WhiskeyRealism.Tactical.Battle
{
    /// <summary>One live objective (gameObject.activeInHierarchy), read once per evaluation. Its state is public on the map: no fog applies.</summary>
    public struct ObjectiveFact
    {
        /// <summary>The capture's <c>ObjectiveId</c>, the director's own string.</summary>
        public string Id;
        public float X, Z, Points;
        public bool OwnHeld, EnemyHeld;
        /// <summary>A perceived enemy sample stands within <see cref="ObjectiveScoring.ThreatRadiusMeters"/> of it (the samples, never vanilla's omniscient distance test).</summary>
        public bool Threatened;
    }

    /// <summary>
    /// The victory clock (requirement-victory-pressure): each side's held share of the live points, the victory bar as the player sees it and the
    /// hours of daylight left.
    /// </summary>
    public struct VictoryClock
    {
        public float ShareOwn, ShareEnemy, Bar, DaylightHours;
    }

    /// <summary>What the clock asks of a commander: how far behind and ahead, how late, the urgency to take risk and the bias to hold.</summary>
    public struct ObjectivePressure
    {
        public float Behind, Ahead, Late, U, H;
    }

    /// <summary>
    /// One table row of the ledger: an objective's value, its feasibility penalty for the army's main body, the prior and the hold term. The
    /// director's own terms are not here; <see cref="Score"/> is without them.
    /// </summary>
    public struct ObjectiveEstimate
    {
        public string Id;
        public float V, Value, Penalty, Prior, Hold, Score;
        /// <summary>Where the objective stands and whose it is (the allocator screens a secondary one at its place).</summary>
        public float X, Z;
        public bool OwnHeld, EnemyHeld, Threatened;
        public bool Feasible;
        /// <summary>A static literal: why the objective is infeasible, or the largest penalty term; null with none.</summary>
        public string Reason;
    }

    /// <summary>
    /// What the TacticalMainEffort row says of the table beside the pick: the highest-valued objective the side could take (a row whose penalty r is
    /// below <see cref="ManeuverFeasibility.RiskyPenalty"/>) and the nearest one it does not hold, the choice vanilla's dynamic chain would make.
    /// </summary>
    public struct ObjectiveContrast
    {
        public string MaxFeasibleId, NearestId;
        public float MaxFeasibleValue;
    }

    /// <summary>A secondary objective the allocator may screen: where it stands, whether the side holds it, and its score (best first).</summary>
    public struct SecondaryObjective
    {
        public string Id;
        public float X, Z, Score;
        public bool OwnHeld;
    }

    /// <summary>
    /// BC14O objective doctrine (spec § 7B), the pure rules. An objective's value comes from vanilla's own points, normalised across the
    /// live objectives; everything here is a function of those numbers and of nothing else (no Unity or vanilla types).
    /// </summary>
    public static class ObjectiveScoring
    {
        /// <summary>Value = ValueFloor + ValueSpan x v, so the best objective keeps the 0.80 the director has always given an enemy-held one.</summary>
        public const float ValueFloor = 0.2f, ValueSpan = 0.6f;

        /// <summary>An own-held objective is worth this much of the same value: today's 0.45 / 0.80.</summary>
        public const float OwnHeldFactor = 0.5625f;

        /// <summary>The day is late when fewer than this many hours of daylight are left.</summary>
        public const float LateDayHours = 4f;

        /// <summary>A perceived enemy sample this close to an own-held objective threatens it (vanilla's defend test, 7473-7481, with samples).</summary>
        public const float ThreatRadiusMeters = 250f;

        /// <summary>A side that is ahead adds this times H to a holding play (Defensive and Neutral) in the catalog's selection.</summary>
        public const float HoldBiasWeight = 0.2f;

        /// <summary>A designer chain's next objective gains this once it is due: the size of half the value range, a strong hint and not an override.</summary>
        public const float HistoricalPriorWeight = 0.3f;

        /// <summary>The chosen main-effort objective changes only when a rival scores this much more.</summary>
        public const float ObjectiveSwitchMargin = 0.1f;

        /// <summary>The weight of the hold term: an own-held objective under threat gains this times H, the hold bias of a side that is ahead.</summary>
        public const float HoldWeight = 1f;

        /// <summary>How much of an objective's strike-leg state value enters its penalty.</summary>
        public const float ContestedWeight = 0.6f;

        /// <summary>The most objectives that draw a screen: economy of force, the main effort goes to one.</summary>
        public const int MaxSecondaryScreens = 2;

        /// <summary>The most direct children whose routes price an objective: the same member bound as a candidate play's.</summary>
        public const int MaxObjectiveMembers = 5;

        /// <summary>
        /// <c>v</c>: an objective's points over the largest of the live objectives' points. With every live objective at 0 points
        /// (<paramref name="maxPoints"/> not above 0) nothing tells them apart and every <c>v</c> is 0.5.
        /// </summary>
        public static float Normalised(float points, float maxPoints)
        {
            if (!(maxPoints > 0f)) return 0.5f;
            float v = points / maxPoints;
            return v < 0f ? 0f : (v > 1f ? 1f : v);
        }

        /// <summary>The objective's value for the side: <see cref="ValueFloor"/> + <see cref="ValueSpan"/> x v, times <see cref="OwnHeldFactor"/> for one it holds.</summary>
        public static float Value(float v, bool ownHeld)
        {
            float value = ValueFloor + ValueSpan * v;
            return ownHeld ? value * OwnHeldFactor : value;
        }

        /// <summary>
        /// The live objectives as the evaluation reads them: the captures whose <c>gameObject.activeInHierarchy</c> is true (an inactive objective is in
        /// neither the table nor the shares) and whose position is usable, owned by <paramref name="alliance"/> (OwnHeld), by another alliance
        /// (EnemyHeld) or by none. An own-held objective is Threatened by a perceived sample within <see cref="ThreatRadiusMeters"/>. Past the size of
        /// <paramref name="into"/> the most valuable are kept. Returns how many were written.
        /// </summary>
        internal static int FactsOf(IReadOnlyList<MapObjectiveCapture> captures, int alliance, BattleSnapshot snapshot, ObjectiveFact[] into)
        {
            int n = 0;
            for (int i = 0; captures != null && i < captures.Count; i++)
            {
                MapObjectiveCapture c = captures[i];
                if (!c.LiveInHierarchy || !TacticalVisionRuntimeAdapter.IsUsableMapPoint(c.Point)) continue;
                var fact = new ObjectiveFact
                {
                    Id = c.ObjectiveId, X = c.Point.X, Z = c.Point.Z, Points = c.Points,
                    OwnHeld = c.Owner == alliance, EnemyHeld = c.Owner >= 0 && c.Owner != alliance,
                };
                fact.Threatened = fact.OwnHeld && Threatened(snapshot, fact.X, fact.Z);
                if (n < into.Length) { into[n++] = fact; continue; }
                int least = 0;
                for (int k = 1; k < n; k++) if (into[k].Points < into[least].Points) least = k;
                if (fact.Points > into[least].Points) into[least] = fact;
            }
            return n;
        }

        /// <summary>
        /// The clock from the live objectives: the held shares are Σ the points of the live objectives a side holds over max(1, Σ the live points) (the
        /// 0.01 per held objective and the inactive objectives that vanilla's own <c>objectivepointsheld</c> counts are not here). In continuous mode
        /// the shares are the accrued totals instead, each side's over max(1, both).
        /// </summary>
        public static VictoryClock Clock(ObjectiveFact[] facts, int count, bool continuous, float accruedOwn, float accruedEnemy, float bar, float daylightHours)
        {
            var clock = new VictoryClock { Bar = bar, DaylightHours = daylightHours };
            if (continuous)
            {
                float both = Math.Max(1f, accruedOwn + accruedEnemy);
                clock.ShareOwn = accruedOwn / both;
                clock.ShareEnemy = accruedEnemy / both;
                return clock;
            }
            float live = 0f, own = 0f, enemy = 0f;
            for (int i = 0; i < count; i++)
            {
                live += facts[i].Points;
                if (facts[i].OwnHeld) own += facts[i].Points;
                else if (facts[i].EnemyHeld) enemy += facts[i].Points;
            }
            float total = Math.Max(1f, live);
            clock.ShareOwn = own / total;
            clock.ShareEnemy = enemy / total;
            return clock;
        }

        /// <summary>
        /// The pressure of the clock on a commander (<paramref name="agg01"/>, 0 to 1): behind by the points or by the bar, ahead likewise; late as the
        /// daylight runs under <see cref="LateDayHours"/>; urgency <c>behind x late x (0.5 + agg01)</c>, a side behind with the day running out and more
        /// so under an aggressive commander; hold <c>ahead x (1 - agg01)</c>, a side ahead holding what it has unless its commander is aggressive.
        /// The bar is read 0.5 as even, whatever produced that.
        /// </summary>
        public static ObjectivePressure Pressure(in VictoryClock clock, float agg01)
        {
            var p = new ObjectivePressure();
            p.Behind = Math.Max(Clamp01(clock.ShareEnemy - clock.ShareOwn), 2f * Math.Max(0f, 0.5f - clock.Bar));
            p.Ahead = Math.Max(Clamp01(clock.ShareOwn - clock.ShareEnemy), 2f * Math.Max(0f, clock.Bar - 0.5f));
            p.Late = Clamp01(1f - clock.DaylightHours / LateDayHours);
            p.U = Clamp01(p.Behind * p.Late * (0.5f + agg01));
            p.H = p.Ahead * (1f - agg01);
            return p;
        }

        /// <summary>
        /// An objective's score without the director's own terms: <c>Value x (1 + U) - FeasibilityWeight x (1 - 0.5 U) x Penalty + Prior + Hold</c>. A side
        /// behind with the day running out values the points more and fears the risk less.
        /// </summary>
        public static float Score(in ObjectiveEstimate e, in ObjectivePressure p)
            => e.Value * (1f + p.U) - SelectRiskWeight(in p) * e.Penalty + e.Prior + e.Hold;

        /// <summary>The weight of the feasibility penalty in an objective's score and in the catalog's selection: <c>FeasibilityWeight x (1 - 0.5 U)</c>.</summary>
        public static float SelectRiskWeight(in ObjectivePressure p) => ManeuverFeasibility.FeasibilityWeight * (1f - 0.5f * p.U);

        /// <summary>The catalog's bias to a holding play for a side that is ahead: <see cref="HoldBiasWeight"/> x H for a Defensive or Neutral play, else 0.</summary>
        public static float HoldBias(in ObjectivePressure p, TacticalPlaybookCatalog.PlaybookStance stance)
            => stance == TacticalPlaybookCatalog.PlaybookStance.Offensive ? 0f : HoldBiasWeight * p.H;

        /// <summary>
        /// The main-effort objective: the feasible row with the highest <c>Score + directorTerms</c> (the director's own terms beside it, by index), the
        /// first of equals. The one named <paramref name="currentId"/> (null: none) keeps the main effort unless a rival beats its total by
        /// <see cref="ObjectiveSwitchMargin"/>, so the main effort does not flicker. -1 when no row is feasible.
        /// </summary>
        public static int PickMainEffort(ReadOnlySpan<ObjectiveEstimate> table, ReadOnlySpan<float> directorTerms, string currentId)
        {
            int best = -1, current = -1;
            float bestTotal = float.NegativeInfinity, currentTotal = float.NegativeInfinity;
            for (int i = 0; i < table.Length; i++)
            {
                if (!table[i].Feasible) continue;
                float total = table[i].Score + (i < directorTerms.Length ? directorTerms[i] : 0f);
                if (currentId != null && string.Equals(table[i].Id, currentId, StringComparison.Ordinal))
                {
                    current = i;
                    currentTotal = total;
                }
                if (total > bestTotal)
                {
                    best = i;
                    bestTotal = total;
                }
            }
            if (current >= 0 && best != current && bestTotal < currentTotal + ObjectiveSwitchMargin) return current;
            return best;
        }

        /// <summary>
        /// The secondary objectives from the ledger's table: every feasible row that is enemy-held or an own-held one under threat, other than the main
        /// effort (<paramref name="mainId"/>), best score first, at most <paramref name="into"/>.Length. Returns how many.
        /// </summary>
        public static int Secondaries(IFeasibilityLookup ledger, string mainId, SecondaryObjective[] into)
        {
            if (into == null || into.Length == 0) return 0;
            int n = 0;
            for (int r = 0; ledger != null && r < ledger.ObjectiveCount; r++)
            {
                if (!ledger.TryObjective(r, out ObjectiveEstimate row) || !row.Feasible) continue;
                if (!row.EnemyHeld && !(row.OwnHeld && row.Threatened)) continue;
                if (mainId != null && string.Equals(row.Id, mainId, StringComparison.Ordinal)) continue;
                var s = new SecondaryObjective { Id = row.Id, X = row.X, Z = row.Z, Score = row.Score, OwnHeld = row.OwnHeld };
                if (n == into.Length && s.Score <= into[n - 1].Score) continue;   // no better than the worst kept
                int at = n < into.Length ? n++ : n - 1;
                into[at] = s;
                while (at > 0 && into[at - 1].Score < into[at].Score)
                {
                    SecondaryObjective t = into[at - 1];
                    into[at - 1] = into[at];
                    into[at] = t;
                    at--;
                }
            }
            return n;
        }

        /// <summary>
        /// The table's contrast for the main-effort row: the highest <c>Value</c> among feasible rows with r below <see cref="ManeuverFeasibility.RiskyPenalty"/>
        /// (first wins a tie), and the row the side does not hold that is nearest the army's centre (<paramref name="fromX"/>, <paramref name="fromZ"/>).
        /// The ids are null with no such row.
        /// </summary>
        public static ObjectiveContrast Contrast(IFeasibilityLookup ledger, float fromX, float fromZ)
        {
            var contrast = new ObjectiveContrast { MaxFeasibleValue = float.NegativeInfinity };
            float nearest = float.PositiveInfinity;
            for (int r = 0; ledger != null && r < ledger.ObjectiveCount; r++)
            {
                if (!ledger.TryObjective(r, out ObjectiveEstimate row)) continue;
                if (row.Feasible && row.Penalty < ManeuverFeasibility.RiskyPenalty && row.Value > contrast.MaxFeasibleValue)
                {
                    contrast.MaxFeasibleValue = row.Value;
                    contrast.MaxFeasibleId = row.Id;
                }
                float dx = row.X - fromX, dz = row.Z - fromZ, d2 = dx * dx + dz * dz;
                if (!row.OwnHeld && d2 < nearest)
                {
                    nearest = d2;
                    contrast.NearestId = row.Id;
                }
            }
            if (contrast.MaxFeasibleId == null) contrast.MaxFeasibleValue = 0f;
            return contrast;
        }

        /// <summary>The objective's feasibility penalty r: <c>1 - (1 - daylight)(1 - detail)(1 - ContestedWeight x contested)</c>.</summary>
        public static float Penalty(float daylight, float detail, float contested)
            => 1f - (1f - daylight) * (1f - detail) * (1f - ContestedWeight * contested);

        /// <summary>
        /// A perceived enemy sample within <see cref="ThreatRadiusMeters"/> of the point. Only the snapshot's samples count: an enemy regiment the side
        /// does not perceive threatens nothing it cannot know of.
        /// </summary>
        public static bool Threatened(BattleSnapshot snapshot, float x, float z)
        {
            if (snapshot == null) return false;
            EnemyObservation[] obs = snapshot.Observations;
            float r2 = ThreatRadiusMeters * ThreatRadiusMeters;
            for (int i = 0; i < snapshot.ObservationCount; i++)
            {
                if (!EnemySamples.Accepts(in obs[i])) continue;
                float dx = obs[i].X - x, dz = obs[i].Z - z;
                if (dx * dx + dz * dz <= r2) return true;
            }
            return false;
        }

        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
    }
}
