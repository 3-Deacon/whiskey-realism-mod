using System.Collections.Generic;

namespace WhiskeyRealism.Strategic
{
    internal static class FactionProfiles
    {
        internal static readonly PersonalityVector CSA   = new PersonalityVector(+0.2f,  0f,    +0.3f,  0f,   -0.1f);
        internal static readonly PersonalityVector Union = new PersonalityVector( 0f,   +0.2f, -0.1f, -0.1f, +0.3f);

        internal static readonly Dictionary<Theater, float> CSATheaterPreference = new Dictionary<Theater, float>
        {
            { Theater.East,      1.0f },
            { Theater.West,      0.6f },
            { Theater.TransMiss, 0.2f },
            { Theater.Coast,     0.4f },
            { Theater.River,     0.3f }
        };

        internal static readonly Dictionary<Theater, float> UnionTheaterPreference = new Dictionary<Theater, float>
        {
            { Theater.East,      1.0f },
            { Theater.West,      0.9f },
            { Theater.TransMiss, 0.3f },
            { Theater.Coast,     0.8f },
            { Theater.River,     1.0f }
        };

        internal static readonly Dictionary<int, float> ForeignRecognitionWeightByAlliance = new Dictionary<int, float>
        {
            { 0, 0.1f },
            { 1, 0.7f }
        };

        internal static PersonalityVector For(int allianceId)
        {
            return allianceId == 1 ? CSA : Union;
        }

        internal static float TheaterPreferenceFor(int allianceId, Theater theater)
        {
            var dict = (allianceId == 1) ? CSATheaterPreference : UnionTheaterPreference;
            return dict.TryGetValue(theater, out var v) ? v : 0.5f;
        }

        internal static float ForeignRecognitionWeightFor(int allianceId)
        {
            return ForeignRecognitionWeightByAlliance.TryGetValue(allianceId, out var v) ? v : 0.3f;
        }
    }
}
