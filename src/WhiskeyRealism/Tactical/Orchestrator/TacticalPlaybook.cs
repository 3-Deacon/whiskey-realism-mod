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
            int jitterSeed)
        {
            CommanderPersonality = commanderPersonality;
            Terrain = terrain;
            CurrentOdds = currentOdds;
            OpposingCommanderHint = opposingCommanderHint;
            DefaultMainEffortSector = defaultMainEffortSector;
            JitterSeed = jitterSeed;
        }

        public PersonalityVector CommanderPersonality { get; }
        public TerrainKind Terrain { get; }
        public float CurrentOdds { get; }
        public float OpposingCommanderHint { get; }
        public int DefaultMainEffortSector { get; }
        public int JitterSeed { get; }
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
    }
}
