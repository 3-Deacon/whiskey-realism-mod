namespace WhiskeyRealism.Strategic
{
    internal static class FactionProfiles
    {
        internal static readonly PersonalityVector CSA   = new PersonalityVector(+0.2f,  0f,    +0.3f,  0f,   -0.1f);
        internal static readonly PersonalityVector Union = new PersonalityVector( 0f,   +0.2f, -0.1f, -0.1f, +0.3f);

        internal static PersonalityVector For(int allianceId)
        {
            return allianceId == 1 ? CSA : Union;
        }
    }
}
