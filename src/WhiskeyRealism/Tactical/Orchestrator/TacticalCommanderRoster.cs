using System.Collections.Generic;
using WhiskeyRealism.Strategic;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    public sealed class CommanderRosterEntry
    {
        public string Name { get; set; }
        public EchelonKind Echelon { get; set; }
        public int AllianceId { get; set; }
        public bool MatchedHistoricalRegistry { get; set; }
        public PersonalityVector PersonalityVector { get; set; }
    }

    public readonly struct SyntheticCommanderInput
    {
        public SyntheticCommanderInput(string name, EchelonKind echelon, int allianceId)
        {
            Name = name; Echelon = echelon; AllianceId = allianceId;
        }
        public string Name { get; }
        public EchelonKind Echelon { get; }
        public int AllianceId { get; }
    }

    public readonly struct VanillaCommanderInput
    {
        public VanillaCommanderInput(object commanderObj, string nameHint, EchelonKind echelon, int allianceId)
        {
            CommanderObj = commanderObj;
            NameHint = nameHint;
            Echelon = echelon;
            AllianceId = allianceId;
        }
        public object CommanderObj { get; }
        public string NameHint { get; }
        public EchelonKind Echelon { get; }
        public int AllianceId { get; }
    }

    /// <summary>
    /// Maintains a per-battle roster of commander entries partitioned by name and
    /// alliance. Two construction paths are supported:
    ///   - FromSynthetic: test-friendly path using faction defaults + rank-tier bias.
    ///     Does not touch vanilla objects; MatchedHistoricalRegistry is always false.
    ///   - FromVanilla: runtime path that calls HistoricalFigureRegistry.Resolve on
    ///     the vanilla commander object. Implemented in TacticalCommanderRosterRuntime.cs
    ///     (excluded from the test assembly). If matched, uses the historical personality
    ///     vector without rank-tier bias. If unmatched, falls back to faction default
    ///     + rank-tier bias.
    /// </summary>
    public sealed partial class TacticalCommanderRoster
    {
        private readonly Dictionary<string, CommanderRosterEntry> _byName = new Dictionary<string, CommanderRosterEntry>();
        private readonly Dictionary<int, List<CommanderRosterEntry>> _bySide = new Dictionary<int, List<CommanderRosterEntry>>();

        public static TacticalCommanderRoster BuildFromSynthetic(IEnumerable<SyntheticCommanderInput> inputs)
        {
            var roster = new TacticalCommanderRoster();
            if (inputs == null) return roster;
            foreach (var input in inputs)
            {
                roster.Add(FromSynthetic(input.Name, input.Echelon, input.AllianceId));
            }
            return roster;
        }

        public void Add(CommanderRosterEntry entry)
        {
            if (entry == null || string.IsNullOrEmpty(entry.Name)) return;
            _byName[entry.Name] = entry;
            if (!_bySide.TryGetValue(entry.AllianceId, out var sideList))
            {
                sideList = new List<CommanderRosterEntry>();
                _bySide[entry.AllianceId] = sideList;
            }
            sideList.Add(entry);
        }

        public CommanderRosterEntry GetByName(string name)
        {
            return name != null && _byName.TryGetValue(name, out var entry) ? entry : null;
        }

        public IReadOnlyList<CommanderRosterEntry> GetSide(int allianceId)
        {
            return _bySide.TryGetValue(allianceId, out var list)
                ? (IReadOnlyList<CommanderRosterEntry>)list
                : new List<CommanderRosterEntry>();
        }

        public int Count => _byName.Count;

        public int UnknownCount
        {
            get
            {
                int n = 0;
                foreach (var kv in _byName)
                    if (!kv.Value.MatchedHistoricalRegistry) n++;
                return n;
            }
        }

        // ---- Entry constructors ----

        /// <summary>
        /// Synthetic input: no historical registry lookup. Vector = faction default + rank-tier bias.
        /// Used by tests and as a placeholder until real vanilla commander discovery wires up.
        /// </summary>
        public static CommanderRosterEntry FromSynthetic(string name, EchelonKind echelon, int allianceId)
        {
            var basis = FactionProfiles.For(allianceId);
            var biased = ApplyRankBias(basis, echelon);
            return new CommanderRosterEntry
            {
                Name = name,
                Echelon = echelon,
                AllianceId = allianceId,
                MatchedHistoricalRegistry = false,
                PersonalityVector = biased,
            };
        }

        // ---- Internals ----

        internal static PersonalityVector ApplyRankBias(PersonalityVector basis, EchelonKind echelon)
        {
            float dAgg = 0f, dCaut = 0f, dAud = 0f, dCas = 0f, dPol = 0f;
            switch (echelon)
            {
                case EchelonKind.Corps:
                    dCaut = 0.10f; // corps commanders trend more cautious / methodical
                    break;
                case EchelonKind.Brigade:
                    dAgg = 0.05f;  // brigade commanders trend slightly more aggressive
                    break;
            }
            return new PersonalityVector(
                PersonalityVector.Clamp(basis.Aggression + dAgg),
                PersonalityVector.Clamp(basis.Caution + dCaut),
                PersonalityVector.Clamp(basis.Audacity + dAud),
                PersonalityVector.Clamp(basis.CasualtyTolerance + dCas),
                PersonalityVector.Clamp(basis.PoliticalResponsiveness + dPol)
            );
        }
    }
}
