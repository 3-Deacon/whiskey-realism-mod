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
        /// Vanilla commander object (e.g., GameVars.commander[i]): historical registry lookup via
        /// HistoricalFigureRegistry.Resolve. If matched (isHistorical == true), vector uses the
        /// historical personality without rank-tier bias. If unmatched, falls back to faction default
        /// + rank-tier bias. The nameHint is used both as the display name AND as a fallback key for
        /// registry matching when combinedname fails or returns a non-matching string.
        /// </summary>
        public static CommanderRosterEntry FromVanilla(object commanderObj, string nameHint, EchelonKind echelon, int allianceId)
        {
            var (vector, isHistorical) = HistoricalFigureRegistry.Resolve(commanderObj, allianceId, nameHint);
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
