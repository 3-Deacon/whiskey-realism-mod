using System.Collections.Generic;
using WhiskeyRealism.Strategic;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    // Runtime partial: contains the vanilla-commander construction path which depends on
    // HistoricalFigureRegistry and by extension Util/Reflection (both of which use Plugin.Log).
    // This file is compiled into the main plugin DLL but is intentionally excluded from the
    // test csproj's <Compile Include> list to avoid the Plugin.Log dependency in test builds.
    public sealed partial class TacticalCommanderRoster
    {
        /// <summary>
        /// Vanilla commander object (e.g., GameVars.commander[i]), resolved registry first
        /// (CommanderPersonalityRuntime.Resolve, BC13): a full-name match in HistoricalFigureRegistry gives the
        /// list's vector without rank-tier bias; otherwise, with Enable Per-Commander Personality on, the vector is
        /// derived from the commander's stats plus the rank-tier bias; otherwise (stats unreadable, or the flag off)
        /// the faction default + rank-tier bias. The nameHint is used both as the display name AND as a fallback
        /// key for registry matching when combinedname fails or returns a non-matching string.
        /// </summary>
        public static CommanderRosterEntry FromVanilla(object commanderObj, string nameHint, EchelonKind echelon, int allianceId)
        {
            var (vector, source) = WhiskeyRealism.Tactical.Battle.CommanderPersonalityRuntime.Resolve(commanderObj, allianceId, nameHint, echelon);
            return new CommanderRosterEntry
            {
                Name = nameHint,
                Echelon = echelon,
                AllianceId = allianceId,
                MatchedHistoricalRegistry = source == WhiskeyRealism.Tactical.Battle.PersonalitySource.Registry,
                PersonalityVector = vector,
                Source = source,
            };
        }

        /// <summary>
        /// Runtime construction path that walks vanilla commander inputs and resolves each through
        /// the historical figure registry. Replaces BuildFromSynthetic at the runtime call site
        /// (TacticalBattleCoordinatorRuntime.OnBattleStart) so registered historical figures
        /// like Hood/Jackson/Beauregard/Hunter actually pick up their personality vector instead of
        /// being silently degraded to faction defaults (the pre-2026-05-18 stub behavior).
        /// </summary>
        public static TacticalCommanderRoster BuildFromVanilla(IEnumerable<VanillaCommanderInput> inputs)
        {
            var roster = new TacticalCommanderRoster();
            if (inputs == null) return roster;
            foreach (var input in inputs)
            {
                roster.Add(FromVanilla(input.CommanderObj, input.NameHint, input.Echelon, input.AllianceId));
            }
            return roster;
        }
    }
}
