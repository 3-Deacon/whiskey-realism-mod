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
        /// One pass over a regiment: <see cref="ApproachVerdict.Mark"/> when every arm holds, <see cref="ApproachVerdict.Release"/> when a hard arm fails (the
        /// regiment is not Whiskey's to hold in line, or its group is marching on purpose or withdrawing: the mark ends at once), and
        /// <see cref="ApproachVerdict.Hold"/> when only a soft arm fails (the group's recorded task is not an advance or hold this tick, the enemy is not
        /// perceived within the range this tick): the mark it already has is left to run out its latch. The soft arms flicker with the doctrine's
        /// per-second task swaps (FixEnemy, Screen, Probe, FormUp) and the perceived enemy, and a mark that fell with each flicker let vanilla column the
        /// regiment a second after its deploy (smoke 2026-10-18, the 1st Division at 05:06:19, 9 of 38 approach deploys).
        /// </summary>
        public static ApproachVerdict Judge(in ApproachLineInput i)
        {
            const UnitFlags needed = UnitFlags.Alive | UnitFlags.Active | UnitFlags.IsInfantry;
            const UnitFlags refused = UnitFlags.IsGroup | UnitFlags.PlayerOwned | UnitFlags.Feud | UnitFlags.PermanentlyDetached | UnitFlags.Routed |
                UnitFlags.MarkedForRout | UnitFlags.OnRetreat | UnitFlags.Mounted;
            if ((i.Flags & needed) != needed || (i.Flags & refused) != 0) return ApproachVerdict.Release;
            if (i.GroupRouteColumn || i.GroupFormation == RoadRoutePlanner.ColumnGroupFormation || i.Withdrawing) return ApproachVerdict.Release;
            if (!i.GroupRecorded || !TaskKeepsLine(i.GroupTask)) return ApproachVerdict.Hold;
            return i.HasEnemy && i.EnemyDistance <= AssaultRangeMeters ? ApproachVerdict.Mark : ApproachVerdict.Hold;
        }

        /// <summary>The regiment is marked this pass (every arm holds).</summary>
        public static bool Marks(in ApproachLineInput i)
        {
            return Judge(in i) == ApproachVerdict.Mark;
        }

        /// <summary>
        /// The deploy pass looks at a unit only if it is a live, active infantry regiment that is not a group, not the player's, not detached, not feuding
        /// and not mounted. Any other unit is released from the table before the pass moves on: Judge never sees it, and a mark left on a regiment that was
        /// detached or taken over would outlast it by the latch (the Prefix's write guard does not test detachment).
        /// </summary>
        public static bool PassEligible(UnitFlags f)
        {
            const UnitFlags barred = UnitFlags.PlayerOwned | UnitFlags.PermanentlyDetached | UnitFlags.Feud | UnitFlags.Mounted;
            return (f & UnitFlags.IsInfantry) != 0 && (f & UnitFlags.IsGroup) == 0 &&
                (f & (UnitFlags.Alive | UnitFlags.Active)) == (UnitFlags.Alive | UnitFlags.Active) && (f & barred) == 0;
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

    internal enum ApproachVerdict
    {
        /// <summary>A hard arm fails: end the regiment's mark now.</summary>
        Release = 0,
        /// <summary>Only a soft arm fails this pass: leave its mark, if any, to run out its latch.</summary>
        Hold,
        /// <summary>Every arm holds: mark it, or refresh its mark.</summary>
        Mark,
    }

    // The marked regiments, per alliance, by GameObject instance id (the snapshot's key). Filled by the facing-pulse producer on every pass and read by the
    // patch; main thread only, allocation-free after warm-up. A mark is the doctrine time of the last pass that marked the regiment and is read as set for
    // LatchHours after it: the latch carries a regiment through the doctrine's per-second task swaps and an enemy that drops out of perception for a pass,
    // and is also the expiry when the arbiter stops or a side stands down. A hard disqualifier releases the mark at once (Release), and a stamp from the
    // future (a reload) is none.
    internal sealed class ApproachLineTable
    {
        /// <summary>How long a mark outlives the pass that set it: 30 real seconds at 1x on the doctrine clock (600 s real per battle hour).</summary>
        public const double LatchHours = 0.05;

        internal static readonly ApproachLineTable Shared = new ApproachLineTable();

        private readonly Dictionary<int, double>[] _last = { new Dictionary<int, double>(), new Dictionary<int, double>() };
        private readonly double[] _pass = { double.NegativeInfinity, double.NegativeInfinity };
        private readonly List<int> _expired = new List<int>();

        static ApproachLineTable()
        {
            WhiskeyRealism.Util.BattleScope.Register("approach-line-table", Shared.Clear);
        }

        /// <summary>Starts a pass at <paramref name="nowHours"/>: marks set by this pass are stamped with it; marks that ran out their latch are dropped.</summary>
        public void Begin(int alliance, double nowHours)
        {
            if (alliance < 0 || alliance >= _last.Length) return;
            _pass[alliance] = nowHours;
            _expired.Clear();
            foreach (KeyValuePair<int, double> entry in _last[alliance])
            {
                double age = nowHours - entry.Value;
                if (age < 0.0 || age >= LatchHours) _expired.Add(entry.Key);
            }
            for (int k = 0; k < _expired.Count; k++) _last[alliance].Remove(_expired[k]);
        }

        public void Mark(int alliance, int instanceId)
        {
            if (alliance < 0 || alliance >= _last.Length) return;
            _last[alliance][instanceId] = _pass[alliance];
        }

        public void Release(int alliance, int instanceId)
        {
            if (alliance < 0 || alliance >= _last.Length) return;
            _last[alliance].Remove(instanceId);
        }

        public bool Contains(int alliance, int instanceId, double nowHours)
        {
            if (alliance < 0 || alliance >= _last.Length) return false;
            if (!_last[alliance].TryGetValue(instanceId, out double stamp)) return false;
            double age = nowHours - stamp;
            return age >= 0.0 && age < LatchHours;
        }

        public void Clear()
        {
            for (int a = 0; a < _last.Length; a++)
            {
                _last[a].Clear();
                _pass[a] = double.NegativeInfinity;
            }
        }
    }
}
