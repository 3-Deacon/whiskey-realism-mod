using System;
using System.Collections;
using System.Collections.Generic;

namespace WhiskeyRealism.Patches
{
    // Prefix-snapshot / Finalizer-restore state for the campaign logistics patches
    // (docs/bug-fixes/vanilla-campaign-logistics.md). No [HarmonyPatch]; generic over the
    // vanilla types through accessors, so the harness runs every save, write and restore
    // on stand-ins. Main thread only; one instance per patch. A second Apply while one is
    // active is refused (vanilla leaves the nested call alone). Restore undoes only what is
    // still ours and returns the first failure message (null when clean).

    // A static value (a GamePrefs field) replaced for one vanilla call:
    // RaidSupplyThresholdPatch (minimumsupplyoffensivetargets), WinterOffensiveModifierPatch
    // (winterdefensiveoperationsmodifier).
    internal sealed class ValueSwap<T>
    {
        private readonly Func<T> _get;
        private readonly Action<T> _set;
        private bool _active;
        private T _saved;
        private T _written;

        internal ValueSwap(Func<T> get, Action<T> set)
        {
            _get = get;
            _set = set;
        }

        internal bool Active => _active;

        internal bool Apply(T value)
        {
            if (_active) return false;
            _saved = _get();
            _written = value;
            _active = true;
            _set(value);
            return true;
        }

        internal string Restore()
        {
            if (!_active) return null;
            string failure = null;
            try
            {
                if (EqualityComparer<T>.Default.Equals(_get(), _written)) _set(_saved);
            }
            catch (Exception ex)
            {
                failure = ex.Message;
            }
            _active = false;
            _saved = default(T);
            _written = default(T);
            return failure;
        }
    }

    // One unit's fog flag cleared for one vanilla call (FoggedCampPatch, FoggedEmbarkationPatch),
    // optionally with a reference field swapped for a stand-in (the camp's sound_objectref,
    // muting the encamp bugle). Restore puts the swapped field back where it still holds the
    // stand-in, and sets the fog flag again unless vanilla recomputed it during the call
    // (CheckModelReplacement's removal branch re-runs CheckRegimentVisibility, 116467).
    internal sealed class UnitFogFlip<TUnit, TField> where TUnit : class where TField : class
    {
        private readonly Func<TUnit, bool> _getFog;
        private readonly Action<TUnit, bool> _setFog;
        private readonly Func<TUnit, TField> _getField;
        private readonly Action<TUnit, TField> _setField;
        private readonly TField _standIn;
        private TUnit _unit;
        private TField _savedField;
        private bool _fieldSwapped;

        internal UnitFogFlip(Func<TUnit, bool> getFog, Action<TUnit, bool> setFog)
            : this(getFog, setFog, null, null, null)
        {
        }

        internal UnitFogFlip(Func<TUnit, bool> getFog, Action<TUnit, bool> setFog,
            Func<TUnit, TField> getField, Action<TUnit, TField> setField, TField standIn)
        {
            _getFog = getFog;
            _setFog = setFog;
            _getField = getField;
            _setField = setField;
            _standIn = standIn;
        }

        internal bool Active => _unit != null;

        internal bool Apply(TUnit unit)
        {
            if (unit == null || _unit != null) return false;
            _unit = unit;
            if (_getField != null && _setField != null)
            {
                _savedField = _getField(unit);
                _fieldSwapped = true;
                _setField(unit, _standIn);
            }
            _setFog(unit, false);
            return true;
        }

        internal string Restore(bool vanillaRecomputedFog)
        {
            if (_unit == null) return null;
            string failure = null;
            try
            {
                if (_fieldSwapped && ReferenceEquals(_getField(_unit), _standIn)) _setField(_unit, _savedField);
            }
            catch (Exception ex)
            {
                failure = ex.Message;
            }
            try
            {
                if (!vanillaRecomputedFog && !_getFog(_unit)) _setFog(_unit, true);
            }
            catch (Exception ex)
            {
                failure = failure ?? ex.Message;
            }
            _unit = null;
            _savedField = null;
            _fieldSwapped = false;
            return failure;
        }
    }

    // Sets a model's active state (camp, entrenchment, transport model). Returns -1 when it
    // hid the model, +1 when it showed it, 0 when there was nothing to do.
    internal static class ModelActiveSync
    {
        internal static int Apply<TModel>(TModel model, Func<TModel, bool> missing,
            Func<TModel, bool> getActive, Action<TModel, bool> setActive, bool active)
        {
            if (missing(model) || getActive(model) == active) return 0;
            setActive(model, active);
            return active ? 1 : -1;
        }
    }

    // FoggedEmbarkationPatch: detaches a unit's own SoundEngine sound objects (go = null, so
    // UpdateSoundObject turns them off and leaves them out of the nearest-source pick,
    // 146438-146513) and reattaches them to their original GameObject (oldgo, vanilla's own
    // restore in ClearGameObjectRefToPosition 146765). Only objects whose oldgo is the
    // owner are touched: units without campaign sounds keep a zero-filled sound_objectref
    // (InitSounds 124025) whose entries point at another unit's object. Returns the number
    // of objects changed.
    internal static class SoundObjectDetach
    {
        internal static int Apply(IList objects, int[] refs, int[] purposes, object owner, bool detach,
            Func<object, object> getGo, Func<object, object> getOldGo, Action<object, object> setGo)
        {
            if (objects == null || refs == null || purposes == null || owner == null) return 0;
            int changed = 0;
            for (int i = 0; i < purposes.Length; i++)
            {
                int purpose = purposes[i];
                if (purpose < 0 || purpose >= refs.Length) continue;
                int id = refs[purpose];
                if (id < 0 || id >= objects.Count) continue;
                object soundObject = objects[id];
                if (soundObject == null || !ReferenceEquals(getOldGo(soundObject), owner)) continue;
                object go = getGo(soundObject);
                if (detach)
                {
                    if (!ReferenceEquals(go, owner)) continue;
                    setGo(soundObject, null);
                    changed++;
                }
                else if (ReferenceEquals(go, null))
                {
                    setGo(soundObject, owner);
                    changed++;
                }
            }
            return changed;
        }
    }

    // Replaces a vanilla list's contents with the desired members when they differ as a
    // set (SupplyDepotAiFixPatch's builder list). Returns true when it changed the list.
    internal static class ListContents
    {
        internal static bool ReplaceIfDifferent<T>(List<T> target, List<T> desired) where T : class
        {
            if (target == null || desired == null) return false;
            bool same = target.Count == desired.Count;
            for (int i = 0; same && i < desired.Count; i++)
                same = target.Contains(desired[i]);
            if (same) return false;
            target.Clear();
            target.AddRange(desired);
            return true;
        }
    }

    // A field of several vanilla objects replaced for one call (SupplyDepotAiFixPatch hides
    // finished own-started depots from the builder loop via allianceconstructionstarted).
    // Restore puts each value back where the object still holds the mask value.
    internal sealed class FieldMask<TItem, TValue> where TItem : class
    {
        private readonly Func<TItem, TValue> _get;
        private readonly Action<TItem, TValue> _set;
        private readonly TValue _mask;
        private readonly List<TItem> _items = new List<TItem>();
        private readonly List<TValue> _saved = new List<TValue>();

        internal FieldMask(Func<TItem, TValue> get, Action<TItem, TValue> set, TValue mask)
        {
            _get = get;
            _set = set;
            _mask = mask;
        }

        internal bool Active => _items.Count > 0;

        internal int Mask(IList<TItem> candidates, Func<TItem, bool> shouldMask)
        {
            if (candidates == null || _items.Count > 0) return 0;
            for (int i = 0; i < candidates.Count; i++)
            {
                TItem item = candidates[i];
                if (item == null || !shouldMask(item)) continue;
                _items.Add(item);
                _saved.Add(_get(item));
                _set(item, _mask);
            }
            return _items.Count;
        }

        internal string Restore(Func<TItem, bool> missing)
        {
            string failure = null;
            for (int i = 0; i < _items.Count; i++)
            {
                try
                {
                    TItem item = _items[i];
                    if (!missing(item) && EqualityComparer<TValue>.Default.Equals(_get(item), _mask)) _set(item, _saved[i]);
                }
                catch (Exception ex)
                {
                    failure = failure ?? ex.Message;
                }
            }
            _items.Clear();
            _saved.Clear();
            return failure;
        }
    }

    // FoggedEmbarkationPatch: a land unit's transport model never fires (Ship.AnimateFire's
    // only callers, 19058/19283/19672, use fleet vessels' models), yet each Ship.Initialize
    // appends 20 SoundObjects to SoundEngine lists that never shrink (AddPurposeArray
    // 146747-146756). While a fogged AI land unit's model is created inside the patched
    // call, the first array is kept per SoundEngine and its index reused for later models.
    internal sealed class SharedArrayIndex
    {
        private object _owner;
        private int _index = -1;

        internal bool TryGet(object owner, out int index)
        {
            index = -1;
            if (owner == null || !ReferenceEquals(owner, _owner) || _index < 0) return false;
            index = _index;
            return true;
        }

        internal void Remember(object owner, int index)
        {
            if (owner == null || index < 0) return;
            _owner = owner;
            _index = index;
        }
    }
}
