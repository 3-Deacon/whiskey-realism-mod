using System;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    /// <summary>
    /// Pure half of <see cref="ArmyEvidenceBuilder"/>: the unit conversions and
    /// predicates the runtime reader applies to raw vanilla fields. Compiled into
    /// the test harness so the vanilla semantics are pinned by tests.
    /// </summary>
    internal static partial class ArmyEvidenceBuilder
    {
        private const int VanillaArtilleryUnitTyp = 2;

        /// <summary>
        /// Effective ammunition level on [0, 1] from vanilla <c>Regiment.ammo</c>
        /// (<c>float[]</c>, decompile 111498; initialised to 1f per slot at 112873,
        /// clamped to [0, 1] by <c>SetAmmoVariable</c> 117800). Vanilla reads
        /// <c>ammo[0]</c> for every non-artillery unit and <c>SumUp(ammo) / 3</c>
        /// for artillery (GetROF 21031/21038, fire loop 119609/119620, morale
        /// 128423, unit icon 121766). We apply the same rule.
        /// </summary>
        internal static float NormalizeAmmo01(float[] ammo, int unittyp, float fallback)
        {
            if (ammo == null || ammo.Length == 0) return fallback;

            float value;
            if (unittyp == VanillaArtilleryUnitTyp)
            {
                float sum = 0f;
                for (int i = 0; i < ammo.Length; i++) sum += ammo[i];
                value = sum / 3f;
            }
            else
            {
                value = ammo[0];
            }

            if (float.IsNaN(value) || float.IsInfinity(value)) return fallback;
            if (value < 0f) return 0f;
            if (value > 1f) return 1f;
            return value;
        }

        /// <summary>
        /// Leaf-unit "alive and on the field" predicate. Mirrors vanilla's
        /// <c>unitsonfield</c> count (84800: activeInHierarchy && !isrouted) plus
        /// a positive head count. <c>groupstrengthactive</c> must not be used on
        /// leaf regiments: vanilla only fills it via <c>UpdateGroupStrength</c>,
        /// which is called on groups (5890, 6517, 84828...).
        /// </summary>
        internal static bool IsAliveCombatUnit(int strength, bool activeInHierarchy, bool routed)
        {
            return strength > 0 && activeInHierarchy && !routed;
        }

        /// <summary>
        /// Hours until a scheduled reinforcement lands. Vanilla
        /// <c>ScheduledArrival.durationleft</c> is already in game hours: it is
        /// decremented by <c>currenttimefromstart - lastreinforcementarrivalcheck</c>
        /// (CheckReinforcementArrivals 87562-87584), and currenttimefromstart is
        /// in game hours.
        /// </summary>
        internal static float ArrivalHoursFromDurationLeft(float durationLeft)
        {
            if (float.IsNaN(durationLeft) || float.IsInfinity(durationLeft) || durationLeft < 0f) return 0f;
            return durationLeft;
        }

        /// <summary>
        /// Strength a scheduled reinforcement brings. Vanilla's own estimate is
        /// <c>regimentrefreg.groupstrength</c>, refreshed every side-stat cycle by
        /// <c>UpdateGroupStrength(overridecalc: true, ignoreactivestate: true)</c>
        /// (84828-84833 -> sideinformation.strengthtoarrive). The aigroup
        /// figure is only a fallback for formations vanilla has not summed yet.
        /// </summary>
        internal static float ArrivalStrength(int groupStrength, int groupStrengthAiGroup)
        {
            if (groupStrength > 0) return groupStrength;
            if (groupStrengthAiGroup > 0) return groupStrengthAiGroup;
            return 0f;
        }
    }
}
