using System.Collections.Generic;

namespace WhiskeyRealism.Strategic.Planner
{
    // What one sighting shows of a group's arms (plan G9, decision 3): the strength-weighted WSF of its armed units, split by weapon
    // lane. Vanilla's own group reading (GetWeaponStrengthFactor(group) 16195-16212) averages both lanes together and divides by
    // zero for a group with no armed unit; this keeps the lanes apart and answers 0 for a lane nothing in the group carries.
    // The runtime (EnemyDetectionRuntime) feeds it each unit's model WSF and strength at the sighting; nothing else is read.
    public struct SightedQuality
    {
        private float _rifleSum, _rifleStrength, _gunSum, _gunStrength;

        // A unit with no strength, an unreadable WSF or a negative strength adds nothing.
        public void Add(bool isGun, float wsf, float strength)
        {
            if (float.IsNaN(wsf) || float.IsInfinity(wsf) || float.IsNaN(strength) || float.IsInfinity(strength) || strength <= 0f) return;
            if (isGun)
            {
                _gunSum += wsf * strength;
                _gunStrength += strength;
            }
            else
            {
                _rifleSum += wsf * strength;
                _rifleStrength += strength;
            }
        }

        public float Rifle => _rifleStrength > 0f ? _rifleSum / _rifleStrength : 0f;

        public float Gun => _gunStrength > 0f ? _gunSum / _gunStrength : 0f;
    }

    // The alliance's belief about the enemy's lane quality: the confidence-weighted mean of the lane readings of its beliefs. A belief
    // with no reading in the lane (a garrison, a cavalry group, a group never seen armed) is left out of the mean rather than counted
    // as 0, which would drag the lane toward 0 and hand out an edge nobody earned. No reading anywhere: 0, which gives no edge term.
    public static class BelievedQuality
    {
        public static float Of(IReadOnlyList<EnemyBelief> beliefs, bool guns)
        {
            if (beliefs == null) return 0f;
            float sum = 0f, weight = 0f;
            for (int i = 0; i < beliefs.Count; i++)
            {
                float reading = guns ? beliefs[i].GunQuality : beliefs[i].RifleQuality;
                float confidence = beliefs[i].Confidence01;
                if (float.IsNaN(reading) || float.IsInfinity(reading) || reading <= 0f) continue;
                if (float.IsNaN(confidence) || confidence <= 0f) continue;
                sum += reading * confidence;
                weight += confidence;
            }
            return weight > 0f ? sum / weight : 0f;
        }
    }
}
