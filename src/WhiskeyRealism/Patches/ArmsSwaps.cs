using System;
using System.Collections.Generic;

namespace WhiskeyRealism.Patches
{
    // Prefix-snapshot / Finalizer-restore state for the armament patches (no [HarmonyPatch];
    // generic over the vanilla types through accessors, so the harness runs it on stand-ins).
    // Main thread only; one instance per patch. A second Apply while one is active is refused.
    //
    // A member removed from one list field of several objects for one vanilla call
    // (AiWeaponPurchaseOrderablePatch hides the alliance from the research list of models it
    // cannot order). Each object's field is pointed at a copy without the member; Restore
    // points it back at the original list where the object still holds that copy, and
    // returns the first failure message (null when clean).
    internal sealed class ListMemberMask<TOwner> where TOwner : class
    {
        private readonly Func<TOwner, List<int>> _get;
        private readonly Action<TOwner, List<int>> _set;
        private readonly List<(TOwner owner, List<int> original, List<int> copy)> _masked = new List<(TOwner, List<int>, List<int>)>();
        private bool _active;

        internal ListMemberMask(Func<TOwner, List<int>> get, Action<TOwner, List<int>> set)
        {
            _get = get;
            _set = set;
        }

        internal int Count => _masked.Count;

        // Starts a masking pass; false when one is already active.
        internal bool Begin()
        {
            if (_active) return false;
            _active = true;
            _masked.Clear();
            return true;
        }

        // Hides `member` from the owner's list for the call. False when there was nothing to hide.
        internal bool Hide(TOwner owner, int member)
        {
            if (!_active || owner == null) return false;
            var original = _get(owner);
            if (original == null || !original.Contains(member)) return false;
            var copy = new List<int>(original.Count);
            foreach (int value in original)
                if (value != member) copy.Add(value);
            _set(owner, copy);
            _masked.Add((owner, original, copy));
            return true;
        }

        internal string Restore()
        {
            if (!_active) return null;
            string failure = null;
            foreach (var (owner, original, copy) in _masked)
            {
                try
                {
                    if (ReferenceEquals(_get(owner), copy)) _set(owner, original);
                }
                catch (Exception ex)
                {
                    if (failure == null) failure = ex.Message;
                }
            }
            _masked.Clear();
            _active = false;
            return failure;
        }
    }
}
