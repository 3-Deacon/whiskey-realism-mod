using System.Collections.Generic;
using WhiskeyRealism.Tactical.Operations;

namespace WhiskeyRealism.Tactical.Battle
{
    // What the marking rule reads about one regiment and the group whose intent it follows (the nearest group above it with a recorded posture).
    internal readonly struct ApproachLineInput
    {
        public ApproachLineInput(UnitFlags flags, CommandTaskType groupTask, bool groupRouteColumn, int groupFormation, bool withdrawing, bool hasEnemy,
            float enemyDistance, bool groupRecorded)
        {
            Flags = flags;
            GroupTask = groupTask;
            GroupRouteColumn = groupRouteColumn;
            GroupFormation = groupFormation;
            Withdrawing = withdrawing;
            HasEnemy = hasEnemy;
            EnemyDistance = enemyDistance;
            GroupRecorded = groupRecorded;
        }

        public UnitFlags Flags { get; }
        public CommandTaskType GroupTask { get; }
        /// <summary>The posture pipeline put the group on a BC10 road route, or is waiting for its column (GroupPosture.RouteColumn).</summary>
        public bool GroupRouteColumn { get; }
        /// <summary>The group's groupformation (4 is the route's march column).</summary>
        public int GroupFormation { get; }
        public bool Withdrawing { get; }
        /// <summary>The group's closest perceived enemy exists, and its distance.</summary>
        public bool HasEnemy { get; }
        public float EnemyDistance { get; }
        public bool GroupRecorded { get; }
    }

    // What the ancestor walk reads about the groups above a regiment (the producer's host reads the posture table and the executor's outcomes).
    internal interface IGroupWalkHost
    {
        /// <summary>The group at <paramref name="index"/> has a recorded posture row this tick.</summary>
        bool HasPosture(int index);
        /// <summary>The task of the posture row at <paramref name="index"/>.</summary>
        CommandTaskType TaskOf(int index);
        /// <summary>A withdrawal move was issued for the unit at <paramref name="index"/> inside the withdrawal window.</summary>
        bool WithdrawalRecent(int index);
    }

    // The approach in line (unit churn, user-approved Prefix-skip 2026-10-17, ApproachLineAdvancePatch).
    //
    // Vanilla anchors (decompile build 14703111):
    //   CheckFormationChange 127625-127682, run from the regular cycle (112277, while changetomarchcolumnactive) and on a manual stop (132370): a
    //     regiment with a path whose last corner is further than mindistanceformarchcolumn (60 m) and no enemy within distancetoenemyforinterruption
    //     (100 m) takes the march column (127662-127668); inside those 100 m it deploys (127677-127682), facing the way it was walking.
    // A brigade Whiskey advances or holds in line near the enemy was columned again a moment after every deployment, and met the enemy in column. The
    // approach table marks the regiments the patch leaves in their formation: AI infantry of a group whose recorded intent is a line advance or hold, not a
    // road route, with the group's closest perceived enemy inside the assault range. Pure.
    internal static class ApproachLine
    {
        /// <summary>BC16's assault range (plan § BC16 decision 1): the distance at which an advance forms for the assault, outside rifle range. One constant.</summary>
        public const float AssaultRangeMeters = 500f;

        /// <summary>How many ancestors above a regiment the walk visits: regiment, brigade, division, army, with one to spare for a nested command group.</summary>
        public const int MaxGroupHops = 4;

        /// <summary>
        /// The groups above a regiment: <paramref name="group"/> is the nearest one with a recorded posture row (the intent the regiment follows), -1 when
        /// none is within <see cref="MaxGroupHops"/>; <paramref name="withdrawing"/> when a withdrawal task is recorded on any of them, or a withdrawal move
        /// was issued recently for any of them or for the regiment. A parent index outside the roster ends the walk.
        /// </summary>
        public static void Walk<THost>(UnitRecord[] own, int ownCount, int regiment, ref THost host, out int group, out bool withdrawing)
            where THost : struct, IGroupWalkHost
        {
            group = -1;
            withdrawing = false;
            int a = own[regiment].ParentIndex;
            for (int hops = 0; a >= 0 && a < ownCount && hops < MaxGroupHops; hops++, a = own[a].ParentIndex)
            {
                if (host.HasPosture(a))
                {
                    if (group < 0) group = a;
                    if (ColumnDeployment.WithdrawalTask(host.TaskOf(a))) withdrawing = true;
                }
                if (host.WithdrawalRecent(a)) withdrawing = true;
            }
            if (host.WithdrawalRecent(regiment)) withdrawing = true;
        }

        /// <summary>The tasks that advance or hold in line. Not the marches (assembly, form up, release, fall back, delay) and not the recon screens.</summary>
        public static bool TaskKeepsLine(CommandTaskType task)
        {
            // The attack tasks are the mapper's (attack stance 3: AttackObjective, SupportAttack, FixEnemy and a Defender's bounded counterstroke).
            if (TacticalCommandTaskToStanceMapper.Map(task) == TacticalCommandTaskToStanceMapper.StanceAttack) return true;
            switch (task)
            {
                case CommandTaskType.HoldObjective:
                case CommandTaskType.HoldChoke:
                case CommandTaskType.GuardFlank:
                case CommandTaskType.Consolidate:
                case CommandTaskType.ReserveWait:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// The regiment is marked this pass: every arm holds. Each arm reads a stable input. The task is the one the operations ledger committed to the
        /// group (<see cref="GroupPosture.LedgerTask"/>), which changes when the ledger changes its mind and not when a per-tick rewrite's input flickers
        /// (the 2026-10-18 smoke's FixEnemy, Screen, FixEnemy, Screen, Probe in seven seconds was the reconnaissance rule reading one tick of sight). The enemy is
        /// the closest one the side perceives, seen or remembered (<see cref="ClosestPerceivedEnemy"/>): perception keeps an enemy that dropped out of sight.
        /// The ownership, route and withdrawal arms are state, not signals. The table is rebuilt every pass, so a regiment that fails any arm is unmarked at once.
        /// </summary>
        public static bool Marks(in ApproachLineInput i)
        {
            const UnitFlags needed = UnitFlags.Alive | UnitFlags.Active | UnitFlags.IsInfantry;
            const UnitFlags refused = UnitFlags.IsGroup | UnitFlags.PlayerOwned | UnitFlags.Feud | UnitFlags.PermanentlyDetached | UnitFlags.Routed |
                UnitFlags.MarkedForRout | UnitFlags.OnRetreat | UnitFlags.Mounted;
            if ((i.Flags & needed) != needed || (i.Flags & refused) != 0) return false;
            if (!i.GroupRecorded || !TaskKeepsLine(i.GroupTask)) return false;
            if (i.GroupRouteColumn || i.GroupFormation == RoadRoutePlanner.ColumnGroupFormation || i.Withdrawing) return false;
            return i.HasEnemy && i.EnemyDistance <= AssaultRangeMeters;
        }

        /// <summary>
        /// The closest enemy the side perceives to the unit at <paramref name="ownIndex"/>, seen now or remembered (the snapshot's observations are
        /// BattlePerception's fog-honest memory: an enemy that dropped out of sight stays a Remembered observation at the place it was seen). Index of the
        /// observation, or -1. <see cref="BattleSnapshot.ClosestVisibleEnemy"/> reads sight alone and loses the enemy for a tick behind a rise.
        /// </summary>
        public static int ClosestPerceivedEnemy(BattleSnapshot s, int ownIndex, out float distance)
        {
            distance = 0f;
            if (s == null || ownIndex < 0 || ownIndex >= s.OwnCount) return -1;
            float x = s.Own[ownIndex].X, z = s.Own[ownIndex].Z;
            int best = -1;
            float bestSq = float.MaxValue;
            EnemyObservation[] observations = s.Observations;
            for (int k = 0; k < s.ObservationCount; k++)
            {
                float dx = observations[k].X - x, dz = observations[k].Z - z;
                float sq = dx * dx + dz * dz;
                if (sq < bestSq)
                {
                    bestSq = sq;
                    best = k;
                }
            }
            if (best >= 0) distance = (float)System.Math.Sqrt(bestSq);
            return best;
        }

        /// <summary>The Prefix's cheap tests, run first for every regiment on every regular cycle: its flag is on, and the regiment is infantry that is not already in column.</summary>
        public static bool Eligible(bool flagOn, int unitTyp, int formation)
        {
            return flagOn && unitTyp == TacticalUnitType.Infantry && formation != ColumnDeployment.RegimentMarchColumn;
        }

        /// <summary>The Prefix's decision: true skips vanilla's CheckFormationChange for this regiment; every other case runs vanilla unchanged.</summary>
        public static bool Skips(bool flagOn, bool commanderModeActive, bool canWrite, int unitTyp, int formation, bool marked)
        {
            return Eligible(flagOn, unitTyp, formation) && commanderModeActive && canWrite && marked;
        }
    }

    // The marked regiments, per alliance, by GameObject instance id (the snapshot's key). Rebuilt by the facing-pulse producer on every pass (a regiment
    // that fails any arm, or that the pass does not look at, is not in it) and read by the patch; main thread only, allocation-free after warm-up. It carries
    // no hold: a mark is only what the last pass found. FreshHours is not a hold but a guard on a pass that never came: a table whose last pass is older than
    // that was not maintained (the producer stopped, a side stood down) and marks nothing; a stamp from the future (a reload) is none.
    internal sealed class ApproachLineTable
    {
        public const double FreshHours = 0.1;

        internal static readonly ApproachLineTable Shared = new ApproachLineTable();

        private readonly HashSet<int>[] _marked = { new HashSet<int>(), new HashSet<int>() };
        private readonly double[] _stamp = { double.NegativeInfinity, double.NegativeInfinity };

        static ApproachLineTable()
        {
            WhiskeyRealism.Util.BattleScope.Register("approach-line-table", Shared.Clear);
        }

        public void Begin(int alliance, double nowHours)
        {
            if (alliance < 0 || alliance >= _marked.Length) return;
            _marked[alliance].Clear();
            _stamp[alliance] = nowHours;
        }

        public void Mark(int alliance, int instanceId)
        {
            if (alliance < 0 || alliance >= _marked.Length) return;
            _marked[alliance].Add(instanceId);
        }

        public bool Contains(int alliance, int instanceId, double nowHours)
        {
            if (alliance < 0 || alliance >= _marked.Length) return false;
            double age = nowHours - _stamp[alliance];
            return age >= 0.0 && age < FreshHours && _marked[alliance].Contains(instanceId);
        }

        public void Clear()
        {
            for (int a = 0; a < _marked.Length; a++)
            {
                _marked[a].Clear();
                _stamp[a] = double.NegativeInfinity;
            }
        }
    }
}
