using System;
using System.Collections.Generic;

namespace WhiskeyRealism.Tactical.Battle.Producers
{
    // Infantry charge producer, pure half (BC6b; runtime half ChargeIntentProducer.cs,
    // doctrine ChargeDoctrine.cs). Reads the BattleSnapshot, the producer's per-regiment
    // memory and the executor's last outcome; never a live unit.
    //
    // Per AI infantry regiment (own, not a group) it fills ChargeInput and submits what
    // ChargeDoctrine.Decide returns on the CombatBehavior channel, PerRegiment,
    // Priority.Executor, IntentSource.ChargeDoctrine: Charge = behavior 3, Revert =
    // behavior 2, Disarm = behavior 2 + IntentFlags.LeaveChargeMode (the executor then
    // calls SetMovementMode(0) on a regiment still in charge mode 3).
    //
    // Inputs (vanilla anchors, decompile build 14703111):
    //   target     the regiment's own unitrange.enemyinrangereg[0] (UnitRecord
    //              .FirstEnemyInRangeId), the unit UpdateChargeTarget charges (122228), and
    //              only while this side sees it (snapshot Visible observation). The list is
    //              range-only and unsorted (122840), so nothing else is substituted.
    //   distance   from the regiment's command parent (ParentIndex: the group whose
    //              transform holds it, plan D5) to the target, as vanilla measures from
    //              transform.parent (122257, unitprefs "calculated from next group
    //              commander"); strictly below GamePrefs.maxchargeradius. No parent, no
    //              charge.
    //   stance     the parent group's stance: 4 only while both ai_stance and
    //              ai_stanceordered are 4 (the charge demotion writes ai_stanceordered at
    //              once, ai_stance follows the courier, ChangeStance 90787/90814), so a
    //              demotion reverts on the next tick and a pending assault order arms
    //              nothing yet.
    //   target     morale, outflanked and disorder from the target's own record, read
    //   state      only for a visible target (BC5a TargetGarrisoned / EnemyAdvancing
    //              precedent). outflanked (UpdateFlanking 122959-123175) is computed from
    //              the target's facing and this side's own units in its flank sectors,
    //              both of which this side sees; caveat: a sector is cleared when one of
    //              the target's own friendlies stands closer in it (123077-123110), and
    //              those friendlies may be hidden from this side. Disordered = routing
    //              (markedforrout), on retreat, or in a fallback/retreat movement mode (2/4).
    //   own flank  battleprefs "No micro ai charge if unit is outflanked or receiving
    //              flanking fire" (GamePrefs.nomicroaichargeifunitisoutflanked, 54784;
    //              shipped true, never read by vanilla): when set, a regiment whose own
    //              outflanked (111488) or firefromflank (111492, fire it received from a
    //              flank sector, 123208-123264) is non-zero is not armed.
    //   filter     MicroAICheckForCharges' unit filter (4917 arm, 4922 cancel):
    //              permanentlydetached, routed, markedforrout, or Feud, which the snapshot
    //              propagates from the AI group / command ancestors
    //              (PlayerOwnership.ApplyFeudGroups); the feud instance owns those units and
    //              the write guard refuses them.
    //   lane       an own standing regiment (not a group, not this one) whose projection
    //              falls strictly between the regiment and the target and lies within
    //              WaypointValidator.FriendlyFireClearanceMeters (40 m) of that segment.
    //   leading    ChargeDoctrine.IsLeading over the parent's direct children's distances
    //              to their own visible first target (+Infinity without one).
    //   refusal    vanilla stamps lastaichargetime = currenttimefromstart when its odds
    //              check refuses (122290-122292). A change of the stamp since the last tick
    //              is a refusal seen now, on the doctrine clock; the first value seen is
    //              only the baseline, so a fresh unit's 0 is never a refusal.
    //
    // Armed is producer memory, not combatbehaviorordered: SetMovementMode(3) zeroes
    // combatbehavior and combatbehaviorordered of infantry/cavalry (124770-124774), so an
    // armed regiment reads 0 once vanilla's 122214 gate fires. The doctrine counts a
    // regiment armed when the executor reports its Charge Issued (last outcome on
    // (regiment, CombatBehavior), source ChargeDoctrine, at or after the submission);
    // without that report nothing is armed (fail closed). A behavior 3 seen on an AI regiment
    // is the doctrine's (no other AI writer), so a charge courier landing after a revert
    // re-arms the memory. A late courier the 122214 gate consumes between two ticks is never
    // seen; ChargeDoctrine's restored stance-drop cancel (4922-4924) disarms it when the group
    // left assault. The cycle is resolved
    // (ChargeDoctrine reverts) when the movement mode leaves the cycle
    // (ChargeDoctrine.ChargeCycleEnded), on a refusal, or when the regiment is back at
    // mode 0 without behavior 3 (the gate fired and the cycle ended between ticks, or
    // vanilla's cycle-6 cascade re-ordered the regiment, 5617-5638). A Revert whose
    // behavior 2 is already ordered has nothing to write: the regiment is released.
    internal sealed partial class ChargeIntentProducer
    {
        internal const string ChargeReason = "charge-doctrine";
        internal const string RevertReason = "charge-revert";
        internal const string DisarmReason = "charge-disarm";

        internal readonly struct Settings
        {
            public Settings(bool configOn, float maxChargeRadius, float vanillaRenewHours, bool noChargeIfOutflanked = false)
            {
                ConfigOn = configOn;
                MaxChargeRadius = maxChargeRadius;
                VanillaRenewHours = vanillaRenewHours;
                NoChargeIfOutflanked = noChargeIfOutflanked;
            }

            public bool ConfigOn { get; }
            public float MaxChargeRadius { get; }          // GamePrefs.maxchargeradius (53232)
            public float VanillaRenewHours { get; }        // GamePrefs.timetorenewaichargecheck (54820)
            public bool NoChargeIfOutflanked { get; }       // GamePrefs.nomicroaichargeifunitisoutflanked (54784)
        }

        // What the producer remembers about one regiment between ticks.
        internal struct RegimentMemory
        {
            public bool Seen;
            public int PreviousMode;
            public bool Armed;
            public bool PendingDisarm;
            public float LastAiChargeTime;
            public bool HasRefusal;
            public double RefusalAtHours;
            public ChargeAction Submitted;
            public double SubmittedAtHours;
            public int SubmittedMode;
        }

        // One side's memory and per-tick scratch. BattleScope resets it (runtime half).
        internal sealed class ChargeMemory
        {
            internal readonly Dictionary<int, RegimentMemory> ById = new Dictionary<int, RegimentMemory>(256);
            internal readonly Dictionary<int, int> VisibleObservation = new Dictionary<int, int>(256);
            internal readonly Dictionary<int, int> EnemyRecord = new Dictionary<int, int>(256);
            // Per own index, sized to the roster (grow-only).
            internal float[] TargetDistance = new float[BattleSnapshot.InitialCapacity];
            internal float[] SiblingDistance = new float[BattleSnapshot.InitialCapacity];
            internal int[] Children = new int[BattleSnapshot.InitialCapacity];

            internal void Clear()
            {
                ById.Clear();
                VisibleObservation.Clear();
                EnemyRecord.Clear();
            }
        }

        /// <summary>
        /// Submits this tick's charge intents for the side's AI infantry regiments and
        /// updates the memory. Returns how many Charge orders the executor confirmed since
        /// the last tick (the first-fire signal).
        /// </summary>
        internal static int Plan(BattleSnapshot s, in Settings settings, ChargeMemory memory, OrderExecutor executor, IIntentSink sink)
        {
            if (s == null || memory == null || sink == null) return 0;
            int n = s.OwnCount;
            UnitRecord[] own = s.Own;
            IndexEnemies(s, memory);
            if (memory.TargetDistance.Length < n)
            {
                int size = Math.Max(n, memory.TargetDistance.Length * 2);
                memory.TargetDistance = new float[size];
                memory.SiblingDistance = new float[size];
                memory.Children = new int[size];
            }
            for (int i = 0; i < n; i++)
                memory.TargetDistance[i] = IsInfantryRegiment(own[i]) ? DistanceToVisibleFirstTarget(s, memory, i) : float.PositiveInfinity;

            int confirmed = 0;
            double now = s.DoctrineHours;
            for (int i = 0; i < n; i++)
            {
                ref UnitRecord r = ref own[i];
                if (!IsInfantryRegiment(r)) continue;
                memory.ById.TryGetValue(r.InstanceId, out RegimentMemory m);
                int mode = r.MovementMode;
                int previous = m.Seen ? m.PreviousMode : mode;

                bool refused = false;
                if (!m.Seen)
                {
                    m.LastAiChargeTime = r.LastAiChargeTime;
                }
                else if (r.LastAiChargeTime != m.LastAiChargeTime)
                {
                    m.LastAiChargeTime = r.LastAiChargeTime;
                    m.HasRefusal = true;
                    m.RefusalAtHours = now;
                    refused = true;
                }

                if (m.Submitted != ChargeAction.None)
                {
                    if (IssuedSince(executor, r.InstanceId, m.SubmittedAtHours))
                    {
                        if (m.Submitted == ChargeAction.Charge) confirmed++;
                        m.Armed = m.Submitted == ChargeAction.Charge;
                        m.PendingDisarm = ChargeDoctrine.PendingDisarmAfter(m.PendingDisarm, m.Submitted, m.SubmittedMode);
                    }
                    m.Submitted = ChargeAction.None;
                }

                // Behavior 3 on an AI regiment has no other writer (the player UI 203769 and
                // AssignChargeTarget(usechargeorder) 167621 are player-side; SetAIStance writes
                // 0-2, 122099-122120): a charge courier that landed after a revert is the
                // doctrine's again.
                if (!m.Armed && r.CombatBehaviorOrdered == ChargeDoctrine.ChargeBehavior && (r.Flags & UnitFlags.PlayerOwned) == 0)
                    m.Armed = true;

                ChargeInput input = BuildInput(s, memory, i, in settings, in m, previous, refused, now);
                ChargeAction action = ChargeDoctrine.Decide(in input);
                if (action == ChargeAction.Revert && r.CombatBehaviorOrdered == ChargeDoctrine.RevertBehavior)
                {
                    // Behavior 2 is already ordered: nothing to write, the regiment is released.
                    m.Armed = false;
                    m.PendingDisarm = ChargeDoctrine.PendingDisarmAfter(m.PendingDisarm, action, mode);
                    action = ChargeAction.None;
                }
                else if (action != ChargeAction.None)
                {
                    sink.Submit(Intent(r.InstanceId, action));
                    m.Submitted = action;
                    m.SubmittedAtHours = now;
                    m.SubmittedMode = mode;
                }
                else
                {
                    m.PendingDisarm = ChargeDoctrine.PendingDisarmAfter(m.PendingDisarm, action, mode);
                }

                m.Seen = true;
                m.PreviousMode = mode;
                memory.ById[r.InstanceId] = m;
            }
            return confirmed;
        }

        internal static BattleOrderIntent Intent(int regimentId, ChargeAction action)
        {
            var intent = new BattleOrderIntent
            {
                GroupId = regimentId,
                Channel = OrderChannel.CombatBehavior,
                Value = action == ChargeAction.Charge ? ChargeDoctrine.ChargeBehavior : ChargeDoctrine.RevertBehavior,
                Heading = -1f,
                Priority = IntentPriority.Executor,
                Source = IntentSource.ChargeDoctrine,
                Flags = IntentFlags.PerRegiment,
                Reason = action == ChargeAction.Charge ? ChargeReason : RevertReason,
            };
            if (action == ChargeAction.Disarm)
            {
                intent.Flags |= IntentFlags.LeaveChargeMode;
                intent.Reason = DisarmReason;
            }
            return intent;
        }

        private static ChargeInput BuildInput(BattleSnapshot s, ChargeMemory memory, int i, in Settings settings,
            in RegimentMemory m, int previousMode, bool refused, double now)
        {
            UnitRecord[] own = s.Own;
            ref UnitRecord r = ref own[i];
            int parent = r.ParentIndex;
            bool hasParent = parent >= 0 && parent < s.OwnCount;
            int mode = r.MovementMode;

            var input = new ChargeInput
            {
                ConfigOn = settings.ConfigOn,
                PlayerOwned = (r.Flags & UnitFlags.PlayerOwned) != 0,
                IsInfantryRegiment = true,
                GroupAiStance = hasParent ? GroupStance(own[parent]) : -1,
                OwnMorale = r.Morale,
                OwnFatigue = r.Fatigue,
                Moving = r.RegimentPaths > 0,
                MovementMode = mode,
                WhiskeyArmed = m.Armed,
                WhiskeyArmedPendingDisarm = m.PendingDisarm,
                RefusalBackoffSeconds = ChargeDoctrine.RefusalBackoffSeconds(settings.VanillaRenewHours),
                ExcludedByVanillaFilter = (r.Flags & (UnitFlags.PermanentlyDetached | UnitFlags.Routed | UnitFlags.MarkedForRout | UnitFlags.Feud)) != 0,
                OutflankedBarsCharge = settings.NoChargeIfOutflanked && (r.Outflanked != 0 || r.FireFromFlank != 0),
                SecondsSinceLastAiChargeRefusal = m.HasRefusal
                    ? (now - m.RefusalAtHours) * TacticalBattleClock.BattleSecondsPerHour
                    : double.PositiveInfinity,
                DistanceToTarget = float.NaN,
                TargetMorale = float.MaxValue,
            };
            input.ChargeResolved = m.Armed && (refused
                || ChargeDoctrine.ChargeCycleEnded(previousMode, mode)
                || (mode == ChargeDoctrine.NormalMovementMode && r.CombatBehaviorOrdered != ChargeDoctrine.ChargeBehavior));

            int targetId = r.FirstEnemyInRangeId;
            if (targetId == 0 || !memory.VisibleObservation.TryGetValue(targetId, out int k)) return input;
            EnemyObservation target = s.Observations[k];
            input.TargetVisible = true;
            input.TargetIsFirstInRange = true;
            if (hasParent) input.DistanceToTarget = Distance(own[parent].X, own[parent].Z, target.X, target.Z);
            input.VanillaMaxChargeRadius = settings.MaxChargeRadius;
            input.LaneBlockedByFriendly = LaneBlocked(s, i, target.X, target.Z);
            input.IsLeading = IsLeading(s, memory, i, parent, hasParent);
            if (memory.EnemyRecord.TryGetValue(targetId, out int e))
            {
                ref UnitRecord t = ref s.EnemyRecords[e];
                input.TargetMorale = t.Morale;
                input.TargetFlanked = t.Outflanked != 0;
                input.TargetDisordered = Disordered(t);
            }
            return input;
        }

        // 4 only while both the ordered and the current stance are 4.
        internal static int GroupStance(in UnitRecord group)
        {
            if (group.AiStanceOrdered != ChargeDoctrine.AssaultStance) return group.AiStanceOrdered;
            return group.AiStance;
        }

        private static bool Disordered(in UnitRecord target)
        {
            if ((target.Flags & (UnitFlags.MarkedForRout | UnitFlags.OnRetreat)) != 0) return true;
            return target.MovementMode == 2 || target.MovementMode == 4;
        }

        private static bool IsLeading(BattleSnapshot s, ChargeMemory memory, int i, int parent, bool hasParent)
        {
            if (!hasParent) return false;
            // Children, SiblingDistance and TargetDistance are sized to the roster in Plan.
            int children = s.DirectChildren(parent, memory.Children);
            int self = -1;
            for (int c = 0; c < children; c++)
            {
                int child = memory.Children[c];
                memory.SiblingDistance[c] = memory.TargetDistance[child];
                if (child == i) self = c;
            }
            return ChargeDoctrine.IsLeading(new ReadOnlySpan<float>(memory.SiblingDistance, 0, children), self);
        }

        private static bool LaneBlocked(BattleSnapshot s, int i, float tx, float tz)
        {
            UnitRecord[] own = s.Own;
            float ax = own[i].X;
            float az = own[i].Z;
            float vx = tx - ax;
            float vz = tz - az;
            float len2 = vx * vx + vz * vz;
            if (len2 <= 0f) return false;
            float r2 = WaypointValidator.FriendlyFireClearanceMeters * WaypointValidator.FriendlyFireClearanceMeters;
            for (int j = 0; j < s.OwnCount; j++)
            {
                if (j == i) continue;
                UnitFlags f = own[j].Flags;
                if ((f & UnitFlags.IsGroup) != 0 || (f & UnitFlags.Alive) == 0 || (f & UnitFlags.Active) == 0 || (f & UnitFlags.Routed) != 0)
                    continue;
                float t = ((own[j].X - ax) * vx + (own[j].Z - az) * vz) / len2;
                if (t <= 0f || t >= 1f) continue;
                float px = ax + t * vx - own[j].X;
                float pz = az + t * vz - own[j].Z;
                if (px * px + pz * pz < r2) return true;
            }
            return false;
        }

        private static float DistanceToVisibleFirstTarget(BattleSnapshot s, ChargeMemory memory, int i)
        {
            ref UnitRecord r = ref s.Own[i];
            if (r.FirstEnemyInRangeId == 0 || !memory.VisibleObservation.TryGetValue(r.FirstEnemyInRangeId, out int k))
                return float.PositiveInfinity;
            return Distance(r.X, r.Z, s.Observations[k].X, s.Observations[k].Z);
        }

        private static void IndexEnemies(BattleSnapshot s, ChargeMemory memory)
        {
            memory.VisibleObservation.Clear();
            EnemyObservation[] obs = s.Observations;
            for (int k = 0; k < s.ObservationCount; k++)
                if (obs[k].State == ObservationState.Visible && obs[k].InstanceId != 0) memory.VisibleObservation[obs[k].InstanceId] = k;
            memory.EnemyRecord.Clear();
            UnitRecord[] enemy = s.EnemyRecords;
            for (int e = 0; e < s.EnemyRecordCount; e++)
                if (enemy[e].InstanceId != 0 && memory.VisibleObservation.ContainsKey(enemy[e].InstanceId)) memory.EnemyRecord[enemy[e].InstanceId] = e;
        }

        private static bool IssuedSince(OrderExecutor executor, int regimentId, double submittedAtHours)
        {
            return executor != null
                && executor.TryGetLastOutcome(regimentId, OrderChannel.CombatBehavior, out OrderOutcome outcome)
                && outcome.Kind == OrderOutcomeKind.Issued
                && outcome.Source == IntentSource.ChargeDoctrine
                && outcome.AtHours >= submittedAtHours;
        }

        private static bool IsInfantryRegiment(in UnitRecord r)
        {
            return (r.Flags & UnitFlags.IsGroup) == 0 && r.UnitTyp == TacticalUnitType.Infantry;
        }

        private static float Distance(float ax, float az, float bx, float bz)
        {
            float dx = ax - bx;
            float dz = az - bz;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }
    }
}
