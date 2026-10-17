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

    // The marked regiments, per alliance, by GameObject instance id (the snapshot's key). Rebuilt by the facing-pulse producer on every pass and read by the
    // patch; main thread only, allocation-free after warm-up. A mark expires ValidHours of battle time after its pass (the arbiter stopped, a side stood
    // down), and a stamp from the future (a reload) is none.
    internal sealed class ApproachLineTable
    {
        public const double ValidHours = 0.1;

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
            return age >= 0.0 && age < ValidHours && _marked[alliance].Contains(instanceId);
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
