using System;
using WhiskeyRealism.Strategic;

namespace WhiskeyRealism.Patches
{
    // Prefix-write / Finalizer-restore state for the W&L economy patches (no
    // [HarmonyPatch]; free of Unity and game types, so the harness compiles and
    // runs it). Every writer follows the same order: the Prefix stores the state
    // in __state first (Prepare), then writes (Commit); the Finalizer calls
    // Restore unconditionally, and Restore puts back only what is still ours.

    // One cumulative-effect slot (DLC_WL.cumulativeeffectsrunning[id]).
    internal struct EffectSlotWrite
    {
        internal float[] Array;
        internal int Id;
        internal float Saved;
        internal float Written;
        internal bool Active;

        // Records the write; nothing is written yet. Inactive when the slot is
        // missing or already holds the value.
        internal static EffectSlotWrite Prepare(float[] array, int id, float value)
        {
            if (array == null || id < 0 || id >= array.Length || array[id].Equals(value)) return default;
            return new EffectSlotWrite { Array = array, Id = id, Saved = array[id], Written = value, Active = true };
        }

        internal void Commit()
        {
            if (Active) Array[Id] = Written;
        }

        // current: the live array now (it may have been replaced meanwhile).
        internal void Restore(float[] current)
        {
            if (!Active) return;
            bool same = ReferenceEquals(current, Array);
            if (WlEconomyFixes.ShouldRestore(same, same ? current[Id] : float.NaN, Written))
                current[Id] = Saved;
            Active = false;
        }
    }

    // Several slots set to one value for a call (the army panel's six effects).
    internal sealed class EffectSlotSet
    {
        private readonly int[] _ids;
        private readonly float[] _saved;
        private readonly bool[] _written;
        private float[] _array;
        private float _value;

        internal EffectSlotSet(int[] ids)
        {
            _ids = ids;
            _saved = new float[ids.Length];
            _written = new bool[ids.Length];
        }

        internal bool Active { get; private set; }

        internal void Prepare(float[] array, float value)
        {
            _array = array;
            _value = value;
            Active = false;
            for (int i = 0; i < _ids.Length; i++)
            {
                int id = _ids[i];
                _written[i] = array != null && id >= 0 && id < array.Length && !array[id].Equals(value);
                if (!_written[i]) continue;
                _saved[i] = array[id];
                Active = true;
            }
        }

        internal void Commit()
        {
            if (!Active) return;
            for (int i = 0; i < _ids.Length; i++)
                if (_written[i]) _array[_ids[i]] = _value;
        }

        internal void Restore(float[] current)
        {
            if (!Active) return;
            bool same = ReferenceEquals(current, _array);
            for (int i = 0; i < _ids.Length; i++)
            {
                if (!_written[i]) continue;
                int id = _ids[i];
                if (WlEconomyFixes.ShouldRestore(same, same ? current[id] : float.NaN, _value))
                    current[id] = _saved[i];
            }
            Active = false;
        }
    }

    // A player-ownership flag (Regiment.dlcw_isundercommander) raised for one call.
    internal struct OwnershipRaise<TUnit> where TUnit : class
    {
        internal TUnit Unit;

        internal static OwnershipRaise<TUnit> Prepare(TUnit unit) => new OwnershipRaise<TUnit> { Unit = unit };

        internal bool Active => !ReferenceEquals(Unit, null);

        internal void Commit(Action<TUnit, bool> setOwned)
        {
            if (Active) setOwned(Unit, true);
        }

        // The flag was false when raised (vanilla's slow-tick refresh is not inside the call).
        internal void Restore(Action<TUnit, bool> setOwned)
        {
            if (!Active) return;
            setOwned(Unit, false);
            Unit = null;
        }
    }
}
