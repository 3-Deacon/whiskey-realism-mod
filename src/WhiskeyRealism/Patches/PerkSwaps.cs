using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using WhiskeyRealism.Strategic;

namespace WhiskeyRealism.Patches
{
    // Prefix-snapshot / Finalizer-restore state for the perk-id patches (no [HarmonyPatch];
    // generic over the unit and perk types so the harness runs it on stand-ins). Main thread
    // only; one instance per patch.

    // Points a unit's perks list at an empty list for one vanilla call, so every HasPerk there
    // answers false. Restore puts each list back, only where the unit still holds the empty
    // list, and returns the first failure message (null when clean).
    internal sealed class PerkListSwap<TUnit, TPerk> where TUnit : class
    {
        private readonly List<TPerk> _noPerks = new List<TPerk>();
        private readonly Func<TUnit, List<TPerk>> _get;
        private readonly Action<TUnit, List<TPerk>> _set;
        private readonly List<TUnit> _units = new List<TUnit>();
        private readonly List<List<TPerk>> _saved = new List<List<TPerk>>();

        internal PerkListSwap(Func<TUnit, List<TPerk>> get, Action<TUnit, List<TPerk>> set)
        {
            _get = get;
            _set = set;
        }

        internal bool Active => _units.Count > 0;

        internal void Hide(TUnit unit)
        {
            _units.Add(unit);
            _saved.Add(_get(unit));
            _set(unit, _noPerks);
        }

        internal string Restore()
        {
            string failure = null;
            for (int i = 0; i < _units.Count; i++)
            {
                try
                {
                    TUnit unit = _units[i];
                    if (unit != null && ReferenceEquals(_get(unit), _noPerks))
                        _set(unit, _saved[i]);
                }
                catch (Exception ex)
                {
                    failure = failure ?? ex.Message;
                }
            }
            _units.Clear();
            _saved.Clear();
            return failure;
        }
    }

    // CampaignGroupPerkPatch state: gives one regiment, for one UpdateCampaignUnitStats call, a
    // perks list holding only the campaign Ambulance Corps / Foot Cavalry stand-ins at level 0,
    // and points the two vanilla factor arrays at arrays filled with the group's factor.
    // Restore undoes all three, each only where it is still ours.
    internal sealed class CampaignWoundedSwap<TUnit, TPerk> where TUnit : class
    {
        private readonly Func<TUnit, List<TPerk>> _getPerks;
        private readonly Action<TUnit, List<TPerk>> _setPerks;
        private readonly Func<float[]> _getAmbulance;
        private readonly Action<float[]> _setAmbulance;
        private readonly Func<float[]> _getFootCavalry;
        private readonly Action<float[]> _setFootCavalry;
        private readonly TPerk _ambulancePerk;
        private readonly TPerk _footCavalryPerk;

        private readonly List<TPerk> _scratchPerks = new List<TPerk>(2);
        private readonly float[] _ambulanceFactors = new float[3];
        private readonly float[] _footCavalryFactors = new float[3];

        private TUnit _unit;
        private List<TPerk> _savedPerks;
        private float[] _savedAmbulance;
        private float[] _savedFootCavalry;

        internal CampaignWoundedSwap(
            Func<TUnit, List<TPerk>> getPerks, Action<TUnit, List<TPerk>> setPerks,
            Func<float[]> getAmbulance, Action<float[]> setAmbulance,
            Func<float[]> getFootCavalry, Action<float[]> setFootCavalry,
            TPerk ambulancePerk, TPerk footCavalryPerk)
        {
            _getPerks = getPerks;
            _setPerks = setPerks;
            _getAmbulance = getAmbulance;
            _setAmbulance = setAmbulance;
            _getFootCavalry = getFootCavalry;
            _setFootCavalry = setFootCavalry;
            _ambulancePerk = ambulancePerk;
            _footCavalryPerk = footCavalryPerk;
        }

        internal bool Active => _unit != null || _savedAmbulance != null || _savedFootCavalry != null;

        internal void Apply(TUnit unit, CampaignWoundedPerkPlan plan)
        {
            if (unit == null || !plan.Rewrite) return;
            _scratchPerks.Clear();
            if (plan.AmbulanceFactor != 1f && _getAmbulance() != null)
            {
                Fill(_ambulanceFactors, plan.AmbulanceFactor);
                _savedAmbulance = _getAmbulance();
                _setAmbulance(_ambulanceFactors);
                _scratchPerks.Add(_ambulancePerk);
            }
            if (plan.FootCavalryFactor != 1f && _getFootCavalry() != null)
            {
                Fill(_footCavalryFactors, plan.FootCavalryFactor);
                _savedFootCavalry = _getFootCavalry();
                _setFootCavalry(_footCavalryFactors);
                _scratchPerks.Add(_footCavalryPerk);
            }
            _savedPerks = _getPerks(unit);
            _unit = unit;
            _setPerks(unit, _scratchPerks);
        }

        internal string Restore()
        {
            string failure = null;
            try
            {
                if (_unit != null && ReferenceEquals(_getPerks(_unit), _scratchPerks))
                    _setPerks(_unit, _savedPerks);
                if (_savedAmbulance != null && ReferenceEquals(_getAmbulance(), _ambulanceFactors))
                    _setAmbulance(_savedAmbulance);
                if (_savedFootCavalry != null && ReferenceEquals(_getFootCavalry(), _footCavalryFactors))
                    _setFootCavalry(_savedFootCavalry);
            }
            catch (Exception ex)
            {
                failure = ex.Message;
            }
            _unit = null;
            _savedPerks = null;
            _savedAmbulance = null;
            _savedFootCavalry = null;
            return failure;
        }

        private static void Fill(float[] factors, float value)
        {
            for (int i = 0; i < factors.Length; i++) factors[i] = value;
        }
    }

    // A compiled getter for a (possibly private, possibly on a private nested type) field, built
    // once: no per-call FieldInfo.GetValue. Expression trees compile on Unity's Mono as on .NET.
    internal static class CompiledFieldGetter
    {
        internal static Func<object, TValue> Build<TValue>(FieldInfo field)
        {
            if (field == null) return null;
            ParameterExpression instance = Expression.Parameter(typeof(object), "instance");
            Expression read = Expression.Field(Expression.Convert(instance, field.DeclaringType), field);
            if (field.FieldType != typeof(TValue)) read = Expression.Convert(read, typeof(TValue));
            return Expression.Lambda<Func<object, TValue>>(read, instance).Compile();
        }
    }
}
