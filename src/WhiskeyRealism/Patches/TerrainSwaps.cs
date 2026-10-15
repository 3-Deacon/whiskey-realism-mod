using System;
using System.Collections.Generic;

namespace WhiskeyRealism.Patches
{
    // Prefix-snapshot / Finalizer-restore state for the terrain and engineer bug fixes (no
    // [HarmonyPatch]; generic so the harness runs it on stand-ins). Main thread only; one
    // instance per patch or caller.

    // Writes one field on several items for one vanilla call. Restore puts each saved value
    // back, only where the item still holds the value written, and returns the first failure
    // message (null when clean).
    internal sealed class FieldValueSwap<TItem, TValue> where TItem : class
    {
        private readonly Func<TItem, TValue> _get;
        private readonly Action<TItem, TValue> _set;
        private readonly List<TItem> _items = new List<TItem>();
        private readonly List<TValue> _saved = new List<TValue>();
        private readonly List<TValue> _written = new List<TValue>();

        internal FieldValueSwap(Func<TItem, TValue> get, Action<TItem, TValue> set)
        {
            _get = get;
            _set = set;
        }

        internal bool Active => _items.Count > 0;

        internal void Set(TItem item, TValue value)
        {
            _items.Add(item);
            _saved.Add(_get(item));
            _written.Add(value);
            _set(item, value);
        }

        internal string Restore()
        {
            string failure = null;
            var comparer = EqualityComparer<TValue>.Default;
            for (int i = 0; i < _items.Count; i++)
            {
                try
                {
                    TItem item = _items[i];
                    if (item != null && comparer.Equals(_get(item), _written[i]))
                        _set(item, _saved[i]);
                }
                catch (Exception ex)
                {
                    failure = failure ?? ex.Message;
                }
            }
            _items.Clear();
            _saved.Clear();
            _written.Clear();
            return failure;
        }
    }

    // Points a static reference (a GamePrefs array) at a replacement for one vanilla call.
    // Restore puts the original back only if the static still holds the replacement.
    internal sealed class StaticRefSwap<T> where T : class
    {
        private readonly Func<T> _get;
        private readonly Action<T> _set;
        private T _saved;
        private T _ours;

        internal StaticRefSwap(Func<T> get, Action<T> set)
        {
            _get = get;
            _set = set;
        }

        internal bool Active => _ours != null;

        internal void Set(T replacement)
        {
            if (replacement == null) return;
            _saved = _get();
            _ours = replacement;
            _set(replacement);
        }

        internal string Restore()
        {
            if (_ours == null) return null;
            string failure = null;
            try
            {
                if (ReferenceEquals(_get(), _ours)) _set(_saved);
            }
            catch (Exception ex)
            {
                failure = ex.Message;
            }
            _ours = null;
            _saved = null;
            return failure;
        }
    }
}
