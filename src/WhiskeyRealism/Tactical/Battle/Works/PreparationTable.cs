using System;
using WhiskeyRealism.Tactical.Orchestrator;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Tactical.Battle
{
    /// <summary>
    /// BC14W (spec § 7A requirement-focal-execution, "the preparation floor"): the doctrine hour at which an own battery first passed
    /// <c>BattleWriterPolicy.TryBombardStartIntent</c> for the focal assault's focal point, per alliance and per battle. The artillery doctrine producer is its
    /// one writer (<see cref="Note"/>); BC11's runtime only reads it (<see cref="TryFloor"/>). The column's release, and its Turn to Attack, is no earlier than
    /// that hour plus <see cref="WorksRules.PreparationHours"/>; with no battery passed there is no floor and no wait.
    /// </summary>
    public sealed class PreparationTable
    {
        private static readonly PreparationTable[] _byAlliance = { new PreparationTable(), new PreparationTable() };

        static PreparationTable()
        {
            BattleScope.Register("preparation-table", ResetAll);
        }

        private bool _has;
        private BattlePlanId _plan;
        private double _activatedHours, _firstPassHours;
        private float _focalX, _focalZ;

        /// <summary>Bound-checked: alliances 0 and 1 only; Europe (2) and anything else → null.</summary>
        public static PreparationTable ForAlliance(int alliance)
            => alliance >= 0 && alliance < _byAlliance.Length ? _byAlliance[alliance] : null;

        /// <summary>
        /// A battery passed the start test for the plan's focal point at <paramref name="nowHours"/>. The first pass of a key stands; a pass for another
        /// plan, activation hour or focal point (moved by more than <see cref="WorksRules.FocalSampleMeters"/>) replaces the record, so an earlier assault's
        /// floor never floors a new one.
        /// </summary>
        public void Note(BattlePlanId plan, double activatedHours, float focalX, float focalZ, double nowHours)
        {
            if (_has && Matches(plan, activatedHours, focalX, focalZ)) return;
            _has = true;
            _plan = plan;
            _activatedHours = activatedHours;
            _focalX = focalX;
            _focalZ = focalZ;
            _firstPassHours = nowHours;
        }

        /// <summary>The floor for this plan, activation hour and focal point: the first pass plus <see cref="WorksRules.PreparationHours"/>; false with none recorded for the key.</summary>
        public bool TryFloor(BattlePlanId plan, double activatedHours, float focalX, float focalZ, out double floorHours)
        {
            floorHours = 0.0;
            if (!_has || !Matches(plan, activatedHours, focalX, focalZ)) return false;
            floorHours = _firstPassHours + WorksRules.PreparationHours;
            return true;
        }

        private bool Matches(BattlePlanId plan, double activatedHours, float focalX, float focalZ)
        {
            if (plan != _plan || activatedHours != _activatedHours) return false;
            float dx = focalX - _focalX, dz = focalZ - _focalZ;
            return Math.Sqrt(dx * dx + dz * dz) <= WorksRules.FocalSampleMeters;
        }

        private void Reset() => _has = false;

        private static void ResetAll()
        {
            for (int i = 0; i < _byAlliance.Length; i++) _byAlliance[i].Reset();
        }
    }
}
