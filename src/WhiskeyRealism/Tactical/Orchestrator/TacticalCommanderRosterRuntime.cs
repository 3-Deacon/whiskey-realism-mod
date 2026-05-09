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
        /// Vanilla commander object (e.g., GameVars.commander[i]): historical registry lookup via
        /// HistoricalFigureRegistry.Resolve. If matched (isHistorical == true), vector uses the
        /// historical personality without rank-tier bias. If unmatched, falls back to faction default
        /// + rank-tier bias.
        /// </summary>
        public static CommanderRosterEntry FromVanilla(object commanderObj, string nameHint, EchelonKind echelon, int allianceId)
        {
            var (vector, isHistorical) = HistoricalFigureRegistry.Resolve(commanderObj, allianceId);
            var resolved = isHistorical ? vector : ApplyRankBias(FactionProfiles.For(allianceId), echelon);
            return new CommanderRosterEntry
            {
                Name = nameHint,
                Echelon = echelon,
                AllianceId = allianceId,
                MatchedHistoricalRegistry = isHistorical,
                PersonalityVector = resolved,
            };
        }
    }
}
