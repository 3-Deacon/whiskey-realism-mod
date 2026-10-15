using System;
using WhiskeyRealism.Tactical.Operations;

namespace WhiskeyRealism.Tactical.Battle
{
    // Withdrawal producer, pure half (BC5c; runtime half WithdrawalIntentProducer.cs).
    // Reads the BattleSnapshot only.
    //
    // Per AI infantry/cavalry regiment: the B8 withdrawal doctrine (morale pressure,
    // quadrant threat, fatigue → TacticalWithdrawalDoctrine) picks vanilla's own
    // tactical fallback shape:
    //   RearGuard   → CheckLineFallbacks 5118-5180 (path 5176): path standardfallbackdistance along
    //                 the retreat angle, final facing retreat angle + 180, then
    //                 combat behavior 2 (5179-5180) and movement mode 2 (5178);
    //   FullRetreat → MicroAICheckForRetreats 4817-4898 (path 4858, mode 4898): path standardretreatdistance
    //                 along the retreat angle keeping the current facing, mode 2.
    // Eligibility is CheckLineFallbacks' unit filter (5137) via
    // TacticalWithdrawalDoctrine.PlanTacticalFallback, under a commanding group
    // with an AI stance (>= 0, UpdateMicroAI 5609). The move is a Safety
    // in-contact withdrawal (immediate, line-checked by WaypointValidator); the
    // behavior is a per-regiment CombatBehavior intent (the executor's
    // TacticalCombatBehaviorWriter), never a field write.
    //
    // Inputs re-derived from the snapshot (fog-honest), not read from the Regiment:
    //   retreat angle   vanilla's unitrange.retreatangle is the bearing away from the
    //                   closest enemy (UpdateUnitRangeFast 122833); here away from the
    //                   closest VISIBLE enemy, else away from the front axis, else none;
    //   outflanked      any fog-honest flank pressure (left/right/rear) > 0, not vanilla's
    //                   outflanked code (BC2a: the flank slots differ);
    //   under fire      the unit's first range-list enemy (enemyinrangereg[0]) is visible,
    //                   for vanilla's ReceivedFireFromUnit(closest);
    //   cover           not in the snapshot: treated as no cover;
    //   friendly routed not used: it only reaches UnderPressure (Stabilize/Screen, no move),
    //                   and vanilla's own friendlyroutednear is <= 0 (128253) anyway.
    // The rear-pressure quadrant reads the snapshot's slice pressure with vanilla's
    // slice width (SlicePressureBuilder bins by floor(bearing / sliceCount), 122732).
    //
    // Stalled fallback (was #56 TryClearStalledFallbackOrder): a group whose ledger
    // doctrine order is FallBackToLine with a fallback target, that is not moving
    // (no paths, no courier order in flight) and stands farther than DedupeMeters
    // from the target, gets a courier re-issue of that target at Support, below
    // #61's Executor-priority posture move.
    internal sealed partial class WithdrawalIntentProducer
    {
        // B8's default when the group's stance is outside moraletriggerforfallbackifenemyclose.
        internal const float DefaultMoraleTrigger = 0.4f;

        private static float[] _slices = new float[0];

        /// <summary>Vanilla distances and the morale trigger per group stance (GamePrefs).</summary>
        internal readonly struct Settings
        {
            public Settings(float fallbackDistance, float retreatDistance, float[] moraleTriggerByStance)
            {
                FallbackDistance = fallbackDistance;
                RetreatDistance = retreatDistance;
                MoraleTriggerByStance = moraleTriggerByStance;
            }

            public float FallbackDistance { get; }
            public float RetreatDistance { get; }
            public float[] MoraleTriggerByStance { get; }
        }

        /// <summary>
        /// The fallback for one own regiment: a Safety Move (and for RearGuard a
        /// CombatBehavior 2 intent), or None. <paramref name="behavior"/> has GroupId 0
        /// when there is none.
        /// </summary>
        internal static TacticalWithdrawalDoctrine.FallbackKind PlanUnit(BattleSnapshot s, int index, in Settings settings,
            out BattleOrderIntent move, out BattleOrderIntent behavior)
        {
            move = default;
            behavior = default;
            if (s == null || index < 0 || index >= s.OwnCount) return TacticalWithdrawalDoctrine.FallbackKind.None;
            UnitRecord rec = s.Own[index];
            UnitFlags f = rec.Flags;
            if ((f & UnitFlags.IsGroup) != 0) return TacticalWithdrawalDoctrine.FallbackKind.None;
            if ((f & (UnitFlags.PlayerOwned | UnitFlags.Feud)) != 0) return TacticalWithdrawalDoctrine.FallbackKind.None;
            if ((f & UnitFlags.Alive) == 0 || (f & UnitFlags.Active) == 0) return TacticalWithdrawalDoctrine.FallbackKind.None;

            // CheckLineFallbacks runs only for a group with an AI stance (UpdateMicroAI 5609).
            int stance = CommandingStance(s, rec);
            if (stance < 0) return TacticalWithdrawalDoctrine.FallbackKind.None;

            var kind = TacticalWithdrawalDoctrine.PlanTacticalFallback(Decide(s, index, rec, stance, settings), new TacticalWithdrawalDoctrine.FallbackUnitState(
                rec.UnitTyp,
                (f & UnitFlags.Routed) != 0,
                (f & UnitFlags.MarkedForRout) != 0,
                (f & UnitFlags.PermanentlyDetached) != 0,
                (f & UnitFlags.OnRetreat) != 0,
                rec.RegimentPaths,
                rec.MovementMode,
                hasChargeTarget: false, // a charging unit is movementmode 3, refused above
                (f & UnitFlags.InMelee) != 0 ? 1f : 0f));
            if (kind == TacticalWithdrawalDoctrine.FallbackKind.None) return kind;

            bool fallback = kind == TacticalWithdrawalDoctrine.FallbackKind.Fallback;
            float distance = fallback ? settings.FallbackDistance : settings.RetreatDistance;
            if (!(distance > 0f) || !TryRetreatBearing(s, index, out float bearing)) return TacticalWithdrawalDoctrine.FallbackKind.None;

            double radians = bearing * Math.PI / 180.0;
            move = new BattleOrderIntent
            {
                GroupId = rec.InstanceId,
                Channel = OrderChannel.Move,
                TargetX = rec.X + (float)(Math.Sin(radians) * distance),
                TargetZ = rec.Z + (float)(Math.Cos(radians) * distance),
                Heading = fallback ? Normalize(bearing + 180f) : rec.RotationY,
                Priority = IntentPriority.Safety,
                Source = IntentSource.Withdrawal,
                Flags = IntentFlags.InContactWithdrawal,
                Task = CommandTaskType.FallBackToLine,
                Reason = fallback ? "withdrawal-fallback" : "withdrawal-retreat",
            };
            if (fallback)
            {
                behavior = new BattleOrderIntent
                {
                    GroupId = rec.InstanceId,
                    Channel = OrderChannel.CombatBehavior,
                    Value = 2,
                    Heading = -1f,
                    Priority = IntentPriority.Safety,
                    Source = IntentSource.Withdrawal,
                    Flags = IntentFlags.PerRegiment,
                    Reason = "withdrawal-fallback-behavior",
                };
            }
            return kind;
        }

        /// <summary>Bearing to fall back along (degrees, 0 = +z, 90 = +x): away from the
        /// closest visible enemy, else away from the front axis.</summary>
        internal static bool TryRetreatBearing(BattleSnapshot s, int index, out float bearing)
        {
            bearing = 0f;
            if (s == null || index < 0 || index >= s.OwnCount) return false;
            int closest = s.ClosestVisibleEnemy(index, out _);
            if (closest >= 0 && closest < s.ObservationCount)
            {
                EnemyObservation enemy = s.Observations[closest];
                bearing = SlicePressureBuilder.Bearing(s.Own[index].X - enemy.X, s.Own[index].Z - enemy.Z);
                return true;
            }
            if (!s.FrontAxis.IsValid) return false;
            bearing = Normalize(s.FrontAxis.HeadingDegrees + 180f);
            return true;
        }

        /// <summary>The stalled FallBackToLine re-issue for one command group (Support, courier).</summary>
        internal static bool TryPlanStalledFallback(BattleSnapshot s, int groupIndex, in CommandDoctrineOrder order, out BattleOrderIntent move)
        {
            move = default;
            if (s == null || groupIndex < 0 || groupIndex >= s.OwnCount) return false;
            if (order.Task != CommandTaskType.FallBackToLine || !order.FallbackTarget.HasValue) return false;
            UnitRecord rec = s.Own[groupIndex];
            UnitFlags f = rec.Flags;
            if ((f & (UnitFlags.PlayerOwned | UnitFlags.Feud | UnitFlags.Routed)) != 0) return false;
            if ((f & UnitFlags.Alive) == 0 || (f & UnitFlags.Active) == 0) return false;
            if (rec.RegimentPaths > 0 || rec.QueuedOrders > 0) return false;

            float tx = order.FallbackTarget.X;
            float tz = order.FallbackTarget.Z;
            float dx = tx - rec.X;
            float dz = tz - rec.Z;
            if (dx * dx + dz * dz <= OrderArbiter.DedupeMeters * OrderArbiter.DedupeMeters) return false;

            move = new BattleOrderIntent
            {
                GroupId = rec.InstanceId,
                Channel = OrderChannel.Move,
                TargetX = tx,
                TargetZ = tz,
                Heading = -1f,
                Priority = IntentPriority.Support,
                Source = IntentSource.Withdrawal,
                Flags = IntentFlags.UseOrderDelay,
                Task = CommandTaskType.FallBackToLine,
                Reason = "withdrawal-stalled-fallback",
            };
            return true;
        }

        private static TacticalWithdrawalDoctrine.Decision Decide(BattleSnapshot s, int index, in UnitRecord rec, int stance, in Settings settings)
        {
            int sliceCount = s.SliceCount;
            if (_slices.Length != sliceCount) _slices = new float[sliceCount];
            for (int k = 0; k < sliceCount; k++) _slices[k] = s.SlicePressure(index, k);

            var inputs = new TacticalWithdrawalInputAdapter.Snapshot
            {
                Morale = rec.Morale,
                BattleStartMorale = rec.BattleStartMorale,
                BattleStartMoraleInitialized = rec.BattleStartMorale >= 0f,
                FallbackThreshold = MoraleTrigger(stance, settings),
                Outflanked = s.FlankPressure(index, 0) > 0f || s.FlankPressure(index, 1) > 0f || s.FlankPressure(index, 2) > 0f ? 1 : 0,
                ReceivedFireFromClosestFar = rec.FirstEnemyInRangeId != 0 && s.IsVisible(rec.FirstEnemyInRangeId),
                CoverValue = 0f,
                CoverObject = 0,
                AiFeudStance = -1, // Feud / PlayerOwned units were skipped
                IsPlayerAiOrFeud = 0,
                Fatigue = rec.Fatigue,
                EnemyStrengthWithinAngle = _slices,
                SliceWidthDegrees = sliceCount,
                UnitFacingDegrees = rec.RotationY,
            };
            var morale = TacticalMoralePressure.Score(TacticalWithdrawalInputAdapter.ToMoralePressureInput(inputs));
            var quadrant = TacticalQuadrantThreatScorer.Score(TacticalWithdrawalInputAdapter.ToQuadrantInput(inputs));

            return TacticalWithdrawalDoctrine.Score(new TacticalWithdrawalDoctrine.Input
            {
                MoralePressure = morale,
                RearPressureFlag = quadrant.RearPressureFlag,
                Fatigue = TacticalFatigueState.Score(inputs.Fatigue),
                AiFeudStance = -1,
                IsPlayerAiOrFeud = 0,
            });
        }

        // The commanding group's stance: CheckLineFallbacks reads
        // moraletriggerforfallbackifenemyclose[aigroup.ai_stance] (5167).
        private static int CommandingStance(BattleSnapshot s, in UnitRecord rec)
        {
            int parent = rec.ParentIndex;
            return parent >= 0 && parent < s.OwnCount ? s.Own[parent].AiStance : rec.AiStance;
        }

        private static float MoraleTrigger(int stance, in Settings settings)
        {
            float[] table = settings.MoraleTriggerByStance;
            return table != null && stance >= 0 && stance < table.Length ? table[stance] : DefaultMoraleTrigger;
        }

        private static float Normalize(float degrees)
        {
            degrees %= 360f;
            return degrees < 0f ? degrees + 360f : degrees;
        }
    }
}
