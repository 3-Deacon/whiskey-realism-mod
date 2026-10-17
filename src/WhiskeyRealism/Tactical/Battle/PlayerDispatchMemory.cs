using System.Collections.Generic;

namespace WhiskeyRealism.Tactical.Battle
{
    // What the executor remembers about the W&L dispatches it made for a group (battle-scoped; the runtime owns the
    // instance and resets it with the battle). Pure.
    //
    // Two facts, both about orders the player's panel would otherwise be told twice or too early
    // (2026-10-17 smoke: the division's FormUp task flipped every 18-36 s and each executed move of an ancestor of
    // the player's command was announced):
    //
    //   - the last target a dispatch was queued for, per group: a new dispatch for a target within 110 m of it
    //     (vanilla's own same-order distance, AIBattle.CheckCurrentOrderUpdate 8643) is the same order again and
    //     is not sent, whatever its type;
    //   - since when the group's task has been the task it has now. Every executed write of a group notes its
    //     task; the task "has held" once it has been the same for the task-hold time. A queued dispatch is
    //     delivered only while the group's task is still the one it was dispatched for.
    internal sealed class PlayerDispatchMemory
    {
        // Vanilla's same-order distance (8643), the courier queue's en-route radius.
        public const float SameTargetMeters = 110f;

        private struct Target
        {
            public float X, Z;
        }

        private struct TaskNote
        {
            public int Task;
            public double Since;
        }

        private readonly Dictionary<int, Target> _lastTarget = new Dictionary<int, Target>(64);
        private readonly Dictionary<int, TaskNote> _task = new Dictionary<int, TaskNote>(128);

        // A dispatch for `groupId` to (x, z) is the last one's target again (flat XZ, 110 m inclusive).
        public bool IsRepeat(int groupId, float x, float z)
        {
            if (!_lastTarget.TryGetValue(groupId, out Target last)) return false;
            float dx = last.X - x;
            float dz = last.Z - z;
            return dx * dx + dz * dz <= SameTargetMeters * SameTargetMeters;
        }

        public void RecordDispatch(int groupId, float x, float z)
        {
            _lastTarget[groupId] = new Target { X = x, Z = z };
        }

        // The dispatch for this group is gone without reaching the panel (superseded, lost, abandoned, cleared): the
        // next dispatch to the same place is a new attempt, not a repeat.
        public void ForgetDispatch(int groupId)
        {
            _lastTarget.Remove(groupId);
        }

        // An executed write of the group under `task` (CommandTaskType as an int; 0 = none, ignored). The task's
        // "since" starts when the task differs from the one noted last.
        public void NoteTask(int groupId, int task, double nowHours)
        {
            if (task == 0) return;
            if (_task.TryGetValue(groupId, out TaskNote note) && note.Task == task) return;
            _task[groupId] = new TaskNote { Task = task, Since = nowHours };
        }

        // The group's task is still `task`: when it has held for `holdHours` (<= now means it has). False when the
        // group has since been given another task, or has none noted.
        public bool TaskHolds(int groupId, int task, double holdHours, out double heldAtHours)
        {
            heldAtHours = 0.0;
            if (!_task.TryGetValue(groupId, out TaskNote note) || note.Task != task) return false;
            heldAtHours = note.Since + holdHours;
            return true;
        }

        public void Clear()
        {
            _lastTarget.Clear();
            _task.Clear();
        }
    }
}
