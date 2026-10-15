using System;
using WhiskeyRealism.Strategic.Fiscal;
using WhiskeyRealism.Strategic.Policies;
using WhiskeyRealism.Strategic.Projects;

namespace WhiskeyRealism.Strategic
{
    // One alliance's campaign state as the daily review reads it (CampaignNeedRuntime).
    // Own-alliance and public values only: the government does not see the enemy's books.
    public sealed class NeedSnapshot
    {
        public int Alliance;
        public EraStage Era;
        public FiscalPosture FiscalPosture = FiscalPosture.BalancedWar;
        public float AnnualBalance;           // GetBalance(); negative = deficit
        public float AnnualSpending;          // |army| + |navy| + |recruitment| + |interest| + |depots| per year
        public float RecruitsAvailable;       // volunteersavailable + draftsavailable
        public float TroopsFielded;
        public float TroopsInRecruitment;
        public float ContractExpiries;        // recruitscontractexpiries
        public int RecruitableStates = -1;    // states able to raise a max-size unit (GetAIPersonality 62716); -1 unread
        public float NationalMorale = 1f;
        public float AverageSupport = 1f;
        public float InterventionProbability; // max(GetInterventionProbability(2), (3)) — public diplomacy
        public float OwnBlockadeRatio;        // own harbors (GetAverageBlockadeRatio 93766)
        public float CsaBlockadeRatio;        // CSA harbors: the blockade the Union is building
        public float WeaponStock;             // weapons in the alliance's stock
        public float PeakTransportUse;        // max transportcapacitypeak over the three transport types
        public float SupplyPressure;          // front supply stress (FiscalRuntime)
        public bool EmergencyCredit;          // vanilla credit-emergency personality active
        public bool EmergencyManpower;        // vanilla recruiting-emergency personality active
    }

    // A government's needs, each in [0, 1], indexed by NeedKind.
    public sealed class NeedVector
    {
        public const int EmaDays = 14;

        private readonly float[] _values = new float[PolicyEffectCatalog.NeedKindCount];

        public float this[NeedKind kind]
        {
            get => _values[(int)kind];
            internal set => _values[(int)kind] = value;
        }

        public float Max
        {
            get
            {
                float max = 0f;
                for (int i = 0; i < _values.Length; i++)
                    if (_values[i] > max) max = _values[i];
                return max;
            }
        }

        public float[] ToArray() => (float[])_values.Clone();

        // Sidecar restore; values are re-clamped, a short or missing array reads as zero.
        public static NeedVector FromArray(float[] values)
        {
            var v = new NeedVector();
            if (values == null) return v;
            for (int i = 0; i < v._values.Length && i < values.Length; i++)
                v._values[i] = CampaignNeedBuilder.Clamp01(values[i]);
            return v;
        }
    }

    public static class CampaignNeedBuilder
    {
        private const float ManpowerReserveShare = 0.25f;   // recruits on hand / fielded at which the need is 0
        private const int RecruitingEmergencyStates = 4;     // GamePrefs.emergencyairecruitingtrigger default
        private const int ComfortableRecruitingStates = 8;
        private const float ContractExpiryShare = 0.2f;
        private const float TransportComfortableUse = 0.6f;

        public static NeedVector Raw(NeedSnapshot s)
        {
            var v = new NeedVector();
            if (s == null) return v;

            v[NeedKind.Credit] = CreditStress(s.FiscalPosture);
            v[NeedKind.Treasury] = s.AnnualSpending > 0f ? Clamp01(-s.AnnualBalance / s.AnnualSpending) : 0f;

            float reserveTerm = s.TroopsFielded > 0f
                ? Clamp01(1f - (s.RecruitsAvailable / s.TroopsFielded) / ManpowerReserveShare)
                : 0f;
            float stateTerm = s.RecruitableStates >= 0
                ? Clamp01((ComfortableRecruitingStates - s.RecruitableStates) / (float)(ComfortableRecruitingStates - RecruitingEmergencyStates))
                : 0f;
            v[NeedKind.Manpower] = Math.Max(reserveTerm, stateTerm);
            v[NeedKind.ContractStability] = s.TroopsFielded > 0f
                ? Clamp01((s.ContractExpiries / s.TroopsFielded) / ContractExpiryShare)
                : 0f;

            v[NeedKind.Morale] = IsFinite(s.NationalMorale) ? Clamp01(1f - s.NationalMorale) : 0f;
            v[NeedKind.Support] = IsFinite(s.AverageSupport) ? Clamp01(1f - s.AverageSupport) : 0f;
            v[NeedKind.Recognition] = Clamp01(s.InterventionProbability);
            v[NeedKind.Blockade] = s.Alliance == 1
                ? Clamp01(s.OwnBlockadeRatio)
                : (IsFinite(s.CsaBlockadeRatio) ? Clamp01(1f - s.CsaBlockadeRatio) : 0f);
            v[NeedKind.Industry] = s.TroopsInRecruitment > 0f
                ? Clamp01(1f - s.WeaponStock / s.TroopsInRecruitment)
                : 0f;
            v[NeedKind.Agriculture] = Clamp01(s.SupplyPressure);
            v[NeedKind.Logistics] = Clamp01((s.PeakTransportUse - TransportComfortableUse) / (1f - TransportComfortableUse));
            v[NeedKind.Supply] = Clamp01(s.SupplyPressure);
            return v;
        }

        // Daily EMA with alpha = 2 / (EmaDays + 1), applied once per elapsed day in closed form
        // so a review that skipped days lands where daily reviews would have. A same-day rerun
        // counts as one day; a missing previous vector takes the raw reading as is.
        public static NeedVector Smooth(NeedVector previous, NeedVector raw, int daysElapsed)
        {
            if (raw == null) return previous ?? new NeedVector();
            if (previous == null) return NeedVector.FromArray(raw.ToArray());
            float alpha = 2f / (NeedVector.EmaDays + 1);
            float carry = (float)Math.Pow(1f - alpha, Math.Max(1, daysElapsed));
            var result = new NeedVector();
            for (int k = 0; k < PolicyEffectCatalog.NeedKindCount; k++)
            {
                var kind = (NeedKind)k;
                result[kind] = Clamp01(raw[kind] + (previous[kind] - raw[kind]) * carry);
            }
            return result;
        }

        // The review's step: smooth, then force the need behind a vanilla emergency
        // personality (GetAIPersonality 62716) to 1 so policies and projects answer it now.
        public static NeedVector Next(NeedVector previous, NeedSnapshot s, int daysElapsed)
        {
            var v = Smooth(previous, Raw(s), daysElapsed);
            if (s != null && s.EmergencyCredit) v[NeedKind.Credit] = 1f;
            if (s != null && s.EmergencyManpower) v[NeedKind.Manpower] = 1f;
            return v;
        }

        // ProjectSelectionPatch's doctrine signals from the shared need vector. Before the
        // first review (no snapshot) the blockade and port signals stay neutral (0.5).
        // ArtilleryDeficit, NavalDeficit and OffensiveTempoNeed stay 0 and the late-war collapse
        // risk has no strength term: their only sources are enemy internals (weapon stats,
        // tonnage; AICampaign.GetStrengthRatio 12366-12402 counts every enemy unit plus the
        // enemy's troopsinrecruitment), which are not fog-honest. A fog-honest enemy strength
        // estimate is stage G's EnemyArmyModel; wire it here when it exists.
        public static ProjectDoctrineSignals ToProjectSignals(NeedSnapshot s, NeedVector v)
        {
            if (s == null || v == null)
            {
                return new ProjectDoctrineSignals
                {
                    Alliance = s != null ? s.Alliance : 0,
                    BlockadePressure = 0.5f,
                    PortViability = 0.5f
                };
            }

            var signals = new ProjectDoctrineSignals
            {
                Alliance = s.Alliance,
                Era = s.Era,
                FiscalPosture = s.FiscalPosture,
                WeaponDeficit = v[NeedKind.Industry],
                BlockadePressure = v[NeedKind.Blockade],
                PortViability = IsFinite(s.OwnBlockadeRatio) ? Clamp01(1f - s.OwnBlockadeRatio) : 0.5f,
                CreditStress = v[NeedKind.Credit],
                ManpowerStress = v[NeedKind.Manpower],
                LogisticsTempoNeed = Math.Max(v[NeedKind.Supply], v[NeedKind.Logistics]),
                IndustryGap = v[NeedKind.Industry],
                AgricultureFoodStress = v[NeedKind.Agriculture],
                CivilOrderRisk = v[NeedKind.Support],
                RecognitionWindow = v[NeedKind.Recognition]
            };

            signals.LateWarCollapseRisk = s.Era == EraStage.TotalWar1864
                ? Clamp01((0.4f * signals.CreditStress) + (0.4f * signals.ManpowerStress))
                : 0f;
            return signals;
        }

        internal static float CreditStress(FiscalPosture posture)
        {
            switch (posture)
            {
                case FiscalPosture.EmergencySolvency: return 1f;
                case FiscalPosture.CreditDefense: return 0.75f;
                case FiscalPosture.BalancedWar: return 0.25f;
                default: return 0f;
            }
        }

        internal static float Clamp01(float value)
        {
            if (!IsFinite(value)) return 0f;
            if (value < 0f) return 0f;
            if (value > 1f) return 1f;
            return value;
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
