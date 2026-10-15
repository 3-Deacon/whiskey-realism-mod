using System;
using WhiskeyRealism.Strategic;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    public enum TerrainKind
    {
        Open = 0,
        Wooded = 1,
        River = 2,
        Mountain = 3,
    }

    /// <summary>
    /// Per-axis personality match weights for a playbook. Each axis is in [-1, 1]
    /// where +1 means "strongly prefers this trait." Scoring is a 3-D dot in
    /// (Aggression, Caution, Audacity) mapped to [0, 1].
    /// </summary>
    public readonly struct PersonalityFit
    {
        public PersonalityFit(float aggression, float caution, float audacity)
        {
            Aggression = Clamp(aggression);
            Caution = Clamp(caution);
            Audacity = Clamp(audacity);
        }

        public float Aggression { get; }
        public float Caution { get; }
        public float Audacity { get; }

        /// <summary>
        /// Cosine-style similarity in 3 dims, mapped to [0, 1]. Max possible dot
        /// in 3 dims with values in [-1, 1] is 3; min is -3. So (dot + 3) / 6
        /// gives a [0, 1] score where 1.0 means perfect alignment.
        /// </summary>
        public float Score(PersonalityVector v)
        {
            float dot = Aggression * v.Aggression + Caution * v.Caution + Audacity * v.Audacity;
            float normalized = (dot + 3f) / 6f;
            if (normalized < 0f) return 0f;
            if (normalized > 1f) return 1f;
            return normalized;
        }

        private static float Clamp(float x) => Math.Max(-1f, Math.Min(1f, x));
    }

    /// <summary>
    /// Weight map by terrain kind. Each weight is in [0, 1]; Score returns the
    /// matching weight for the supplied terrain.
    /// </summary>
    public readonly struct TerrainPreference
    {
        public TerrainPreference(float open, float wooded, float river, float mountain)
        {
            Open = Clamp01(open);
            Wooded = Clamp01(wooded);
            River = Clamp01(river);
            Mountain = Clamp01(mountain);
        }

        public float Open { get; }
        public float Wooded { get; }
        public float River { get; }
        public float Mountain { get; }

        public float Score(TerrainKind k)
        {
            switch (k)
            {
                case TerrainKind.Open: return Open;
                case TerrainKind.Wooded: return Wooded;
                case TerrainKind.River: return River;
                case TerrainKind.Mountain: return Mountain;
                default: return 0f;
            }
        }

        private static float Clamp01(float x) => x < 0f ? 0f : (x > 1f ? 1f : x);
    }

    /// <summary>
    /// Inclusive odds band. Score returns 1 when odds are inside [Min, Max] and
    /// decays as 1 / (1 + 2 * distance) outside the band.
    /// </summary>
    public readonly struct OddsRange
    {
        public OddsRange(float min, float max)
        {
            Min = min;
            Max = max;
        }

        public float Min { get; }
        public float Max { get; }

        public float Score(float odds)
        {
            if (odds >= Min && odds <= Max) return 1f;
            float distance = odds < Min ? (Min - odds) : (odds - Max);
            return 1f / (1f + distance * 2f);
        }
    }

    /// <summary>
    /// Inputs the catalog passes to each playbook's <see cref="TacticalPlaybook.Instantiate"/>
    /// when the catalog selects it. Carries the army CO's personality, current
    /// terrain hint, current odds, opposing-CO hint, default main-effort sector,
    /// and a deterministic jitter seed.
    /// </summary>
    public readonly struct PlaybookContext
    {
        public PlaybookContext(
            PersonalityVector commanderPersonality,
            TerrainKind terrain,
            float currentOdds,
            float opposingCommanderHint,
            int defaultMainEffortSector,
            int jitterSeed,
            bool envelopmentPressure = false,
            int allianceId = -1,
            float cavalryFraction = 1f,
            float infantryFraction = 1f,
            float artilleryFraction = 1f,
            bool delayingDefensePressure = false,
            bool defensivePressure = false,
            BattleStance stance = BattleStance.Meeting)
        {
            CommanderPersonality = commanderPersonality;
            Terrain = terrain;
            CurrentOdds = currentOdds;
            OpposingCommanderHint = opposingCommanderHint;
            DefaultMainEffortSector = defaultMainEffortSector;
            JitterSeed = jitterSeed;
            EnvelopmentPressure = envelopmentPressure;
            AllianceId = allianceId;
            CavalryFraction = cavalryFraction;
            InfantryFraction = infantryFraction;
            ArtilleryFraction = artilleryFraction;
            DelayingDefensePressure = delayingDefensePressure;
            DefensivePressure = defensivePressure;
            Stance = stance;
        }

        public PersonalityVector CommanderPersonality { get; }
        public TerrainKind Terrain { get; }
        public float CurrentOdds { get; }
        public float OpposingCommanderHint { get; }
        public int DefaultMainEffortSector { get; }
        public int JitterSeed { get; }

        /// <summary>
        /// Set by the orchestrator when the reinforcement-opportunity doctrine
        /// returns AttackNow with a clear (>=1.5x) standing advantage. Biases
        /// the catalog toward envelopment-affinity playbooks (Lee, Jackson,
        /// Hooker, Sherman) and away from frontal-only ones (Hood, Burnside).
        /// </summary>
        public bool EnvelopmentPressure { get; }

        /// <summary>
        /// Set by the orchestrator when the reinforcement-opportunity doctrine
        /// returns DelayingDefense (outnumbered, no near parity, but still
        /// defensible). Biases the catalog toward delay-and-skirmish archetypes
        /// (DelayingDefense, ReverseSlopeDefense, CavalryScreeningDelay).
        /// </summary>
        public bool DelayingDefensePressure { get; }

        /// <summary>
        /// Set by the orchestrator when the reinforcement-opportunity doctrine
        /// returns ANY defensive outcome (DelayingDefense, DefensiveHold, or
        /// WaitAndConsolidate). Stronger and broader signal than
        /// <see cref="DelayingDefensePressure"/> — used to bias the catalog
        /// away from offensive archetypes whenever doctrine doesn't want an
        /// attack, regardless of why. Prevents the smoke-observed
        /// "doctrine=DefensiveHold but playbook=ManeuverAndFix" mismatch
        /// where brigades got offensive roles while macroai=2 (defend) was
        /// set — leading to lone brigades attacking and supporting brigades
        /// holding, with no coordination.
        /// </summary>
        public bool DefensivePressure { get; }

        /// <summary>
        /// Battle stance from vanilla <c>GetBattleType(alliance)</c>. When set
        /// to Attacker or Defender, the catalog HARD-FILTERS playbooks
        /// incompatible with the stance — defenders cannot pick offensive
        /// archetypes regardless of personality fit; attackers cannot pick
        /// pure-delay archetypes. Default Meeting = no filter.
        /// </summary>
        public BattleStance Stance { get; }

        /// <summary>
        /// Commander's alliance (0=Union, 1=CSA in HistoricalFigureRegistry
        /// convention). Used by the catalog's FactionMismatchPenalty to
        /// prefer historically-matching playbooks. Default -1 = unknown,
        /// which disables the penalty (preserves pre-2026-05-19 behavior
        /// for call sites that haven't been updated).
        /// </summary>
        public int AllianceId { get; }

        /// <summary>
        /// Fraction of own-side force strength that is cavalry (mounted).
        /// Used by playbook preconditions to filter out cavalry-specific
        /// plays (Buford screen, JebStuart raid, Forrest raid) when the
        /// force has no cavalry. Default 1f = unknown/permissive — older
        /// call sites that don't populate this preserve pre-gate behavior.
        /// Computed by ArmyOrchestrator.TryPickPlan from completeunitlist.
        /// </summary>
        public float CavalryFraction { get; }
        public float InfantryFraction { get; }
        public float ArtilleryFraction { get; }
    }

    /// <summary>
    /// Abstract base class for tactical battle playbooks. Concrete playbooks
    /// (Lee envelopment, Jackson valley shuffle, etc.) override <see cref="Instantiate"/>
    /// to produce a <see cref="TacticalBattlePlan"/> from a <see cref="PlaybookContext"/>.
    /// </summary>
    public abstract class TacticalPlaybook
    {
        protected TacticalPlaybook(
            BattlePlanId id,
            string historicalLabel,
            PersonalityFit fit,
            TerrainPreference terrainFit,
            OddsRange preferredOdds,
            float reserveCommitTriggerOdds)
        {
            Id = id;
            HistoricalLabel = historicalLabel ?? "";
            Fit = fit;
            TerrainFit = terrainFit;
            PreferredOdds = preferredOdds;
            ReserveCommitTriggerOdds = reserveCommitTriggerOdds;
        }

        public BattlePlanId Id { get; }
        public string HistoricalLabel { get; }
        public PersonalityFit Fit { get; }
        public TerrainPreference TerrainFit { get; }
        public OddsRange PreferredOdds { get; }
        public float ReserveCommitTriggerOdds { get; }

        public abstract TacticalBattlePlan Instantiate(PlaybookContext ctx);

        /// <summary>
        /// Precondition gate for playbook eligibility (2026-05-21). Returns
        /// true when the playbook can be executed given the force composition
        /// + faction. Default is true (no precondition). Cavalry-specific and
        /// faction-locked playbooks override to enforce hard filtering BEFORE
        /// the catalog's weighted scoring runs.
        ///
        /// Smoke evidence motivating this gate: CavalryScreeningDelay was
        /// selected for a force with zero cavalry (see Side 0 in the
        /// 2026-05-21 23:31 log). The catalog's FactionMismatchPenalty is a
        /// soft penalty (0.10 magnitude) that personality-fit (0.50 weight)
        /// can override. Composition mismatches must be hard-filtered.
        ///
        /// Returning false removes the playbook from the selection pool
        /// entirely. Generic plays + MeetingEngagement remain unconditional
        /// fallbacks so every commander has at least one eligible play.
        /// </summary>
        public virtual bool MatchesPreconditions(PlaybookContext ctx) => true;
    }
}
