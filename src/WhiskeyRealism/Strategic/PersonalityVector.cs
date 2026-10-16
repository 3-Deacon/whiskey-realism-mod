using System;

namespace WhiskeyRealism.Strategic
{
    public struct PersonalityVector
    {
        public float Aggression;
        public float Caution;
        public float Audacity;
        public float CasualtyTolerance;
        public float PoliticalResponsiveness;

        public PersonalityVector(float agg, float caut, float aud, float cas, float pol)
        {
            Aggression = agg;
            Caution = caut;
            Audacity = aud;
            CasualtyTolerance = cas;
            PoliticalResponsiveness = pol;
        }

        public static PersonalityVector Compose(
            PersonalityVector officer,
            PersonalityVector era,
            PersonalityVector faction)
        {
            return new PersonalityVector(
                Clamp(officer.Aggression              + era.Aggression              + faction.Aggression),
                Clamp(officer.Caution                 + era.Caution                 + faction.Caution),
                Clamp(officer.Audacity                + era.Audacity                + faction.Audacity),
                Clamp(officer.CasualtyTolerance       + era.CasualtyTolerance       + faction.CasualtyTolerance),
                Clamp(officer.PoliticalResponsiveness + era.PoliticalResponsiveness + faction.PoliticalResponsiveness));
        }

        // The alliance's standing disposition: its era stage and its faction profile. The retired CIC added a
        // hardcoded officer vector and the locked-difficulty modifier on top; the planner takes its commanders from
        // the vanilla records instead (CommanderProfiles), so this is what the defence ledger and the perk
        // scorer read.
        internal static PersonalityVector ForAlliance(int allianceId, EraStageManager era)
        {
            return Compose(default(PersonalityVector), era != null ? era.StageVector : default(PersonalityVector), FactionProfiles.For(allianceId));
        }

        public static PersonalityVector Add(PersonalityVector a, PersonalityVector b)
        {
            return new PersonalityVector(
                Clamp(a.Aggression              + b.Aggression),
                Clamp(a.Caution                 + b.Caution),
                Clamp(a.Audacity                + b.Audacity),
                Clamp(a.CasualtyTolerance       + b.CasualtyTolerance),
                Clamp(a.PoliticalResponsiveness + b.PoliticalResponsiveness));
        }

        public static float Clamp(float v) => Math.Max(-1f, Math.Min(1f, v));

        public float[] ToArray() => new[] { Aggression, Caution, Audacity, CasualtyTolerance, PoliticalResponsiveness };

        public override string ToString()
            => $"P(agg={Aggression:F2} caut={Caution:F2} aud={Audacity:F2} cas={CasualtyTolerance:F2} pol={PoliticalResponsiveness:F2})";
    }
}
