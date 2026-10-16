using System;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Tactical.Orchestrator;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Tactical.Battle
{
    // BC13 runtime half of the commander personality (spec § 4, requirement-runtime-wiring): reads the raw
    // stats off GameVars.Commander, resolves each commander through the historical list first, builds and
    // extends the per-side CommanderPersonalityTable, and reads the live values the decisions need.
    //
    // Vanilla anchors (decompile build 14703111): GameVars.Commander 58926 (fields status, leadership,
    // initiative, administration, cunning, veteran, westpoint, political; the instances are the static
    // List<Commander> GameVars.commander); Commander.Load 59701 divides each stat by 100, so the fields hold 0-1;
    // GetCommanderInitiative 61173 returns clamp01(initiative x outOfDuty x experience x parentInfluence + W&L
    // staff influence) with outOfDuty = GamePrefs.reductionfactorcommanderoutofduty when status != 0.
    //
    // The fields are read directly (the user-approved exception to the reflection rule for this slice: they are
    // public fields of the shipped class and a rename should fail the build); any exception degrades to the
    // faction default with one bounded warning. Never throws.
    internal static class CommanderPersonalityRuntime
    {
        private static readonly CommanderPersonalityTable[] Tables = { new CommanderPersonalityTable(), new CommanderPersonalityTable() };

        static CommanderPersonalityRuntime()
        {
            BattleScope.Register("commander-personality", ResetForBattleEnd);
        }

        internal static void ResetForBattleEnd()
        {
            for (int i = 0; i < Tables.Length; i++) Tables[i].Clear();
        }

        /// <summary>The side's table; null for an alliance other than 0 and 1 (Europe has no battle side).</summary>
        internal static CommanderPersonalityTable TableFor(int alliance)
        {
            return alliance >= 0 && alliance < Tables.Length ? Tables[alliance] : null;
        }

        /// <summary>
        /// Registry first (full-name match, the list's vector as it is), then the derivation from the commander's
        /// stats plus the echelon's rank bias, then the ranked faction profile.
        /// </summary>
        internal static (PersonalityVector vector, PersonalitySource source) Resolve(object commander, int alliance, string nameHint, EchelonKind echelon)
        {
            try
            {
                var (registryVector, registered) = HistoricalFigureRegistry.Resolve(commander, alliance, nameHint);
                bool derive = Plugin.PerCommanderPersonalityEnabled();
                CommanderStats stats = !registered && derive ? ReadStats(commander as GameVars.Commander) : default;
                var source = CommanderPersonalityDerivation.Resolve(registered, registryVector, derive, stats, alliance, echelon, out PersonalityVector vector);
                if (source == PersonalitySource.Default && derive)
                    OnceLog.Warning("tactical-personality:default", "[CommanderPersonality] a commander's stats could not be read; he takes the ranked faction profile");
                return (vector, source);
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-personality:resolve", "[CommanderPersonality] resolve failed: " + ex.GetType().Name + " " + ex.Message);
                return (CommanderPersonalityDerivation.Default(alliance, echelon), PersonalitySource.Default);
            }
        }

        /// <summary>
        /// Seeds the side's table with its army commander (the roster entry already resolved him), then adds every
        /// own group commander the snapshot shows. With Enable Per-Commander Personality off the table stays empty.
        /// </summary>
        internal static void BuildTable(int alliance, BattleSnapshot snapshot, int armyCommanderIndex, in PersonalityVector armyVector, PersonalitySource armySource)
        {
            CommanderPersonalityTable table = TableFor(alliance);
            if (table == null || !Plugin.PerCommanderPersonalityEnabled()) return;
            table.Clear();
            AddWithRow(table, alliance, armyCommanderIndex, EchelonKind.Army, armyVector, armySource);
            Extend(alliance, snapshot);
            // The share by source (criterion-personality-coverage; smoke item 2.44 reads it).
            TelemetryRouter.LegacyInfo("[TacticalCommanderPersonality] summary side=" + alliance + " commanders=" + table.Count
                + " registry=" + table.CountBySource(PersonalitySource.Registry)
                + " derived=" + table.CountBySource(PersonalitySource.Derived)
                + " default=" + table.CountBySource(PersonalitySource.Default), TelemetryLayer.Tactical);
        }

        /// <summary>
        /// Adds every own corps, division and brigade commander without an entry (reinforcements, a roster change).
        /// O(groups) lookups into a grow-only array; allocates only for a commander never seen before.
        /// </summary>
        internal static void Extend(int alliance, BattleSnapshot snapshot)
        {
            CommanderPersonalityTable table = TableFor(alliance);
            if (table == null || snapshot == null || !Plugin.PerCommanderPersonalityEnabled()) return;
            try
            {
                var own = snapshot.Own;
                for (int i = 0; i < snapshot.OwnCount; i++)
                {
                    int unitTyp = own[i].UnitTyp;
                    int index = own[i].Commander;
                    if (unitTyp < 14 || index < 0 || table.TryGet(index, out _, out _)) continue;
                    EchelonKind echelon = CommanderPersonalityDerivation.EchelonOf(unitTyp, own[i].ParentIndex < 0);
                    var (vector, source) = Resolve(CommanderObject(index), alliance, null, echelon);
                    AddWithRow(table, alliance, index, echelon, vector, source);
                }
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-personality:extend", "[CommanderPersonality] table extension failed: " + ex.GetType().Name + " " + ex.Message);
            }
        }

        // One TacticalCommanderPersonality State row per commander per battle (the table's first write).
        private static void AddWithRow(CommanderPersonalityTable table, int alliance, int commanderIndex, EchelonKind echelon, in PersonalityVector vector, PersonalitySource source)
        {
            if (!table.Add(commanderIndex, vector, source)) return;
            PersonalityVector v = vector;
            int status = Status(commanderIndex);
            float agg01 = CommanderPersonalityDerivation.To01(v.Aggression);
            string band = agg01 < 0.30f ? "low" : agg01 >= 0.70f ? "high" : "mid";
            TelemetryRouter.Emit(TelemetryLayer.Tactical, TelemetryCategory.State, "TacticalCommanderPersonality", TelemetrySeverity.Info,
                ev => ev
                    .WithSide(alliance)
                    .WithDecision("TacticalCommanderPersonality", source.ToString(), "side=" + alliance + "|commander=" + commanderIndex)
                    .WithField("commander", commanderIndex)
                    .WithField("echelon", echelon.ToString())
                    .WithField("source", source.ToString())
                    .WithField("aggression", v.Aggression)
                    .WithField("caution", v.Caution)
                    .WithField("audacity", v.Audacity)
                    .WithField("casualtyTolerance", v.CasualtyTolerance)
                    .WithField("politicalResponsiveness", v.PoliticalResponsiveness)
                    .WithField("agg01Band", band)
                    .WithField("status", status));
        }

        private static GameVars.Commander CommanderObject(int index)
        {
            var list = GameVars.commander;
            return list != null && index >= 0 && index < list.Count ? list[index] : null;
        }

        private static CommanderStats ReadStats(GameVars.Commander c)
        {
            if (c == null) return default;
            try
            {
                return new CommanderStats
                {
                    Initiative = c.initiative,
                    Cunning = c.cunning,
                    Leadership = c.leadership,
                    Administration = c.administration,
                    Veteran = c.veteran,
                    WestPoint = c.westpoint,
                    Political = c.political,
                    Valid = !float.IsNaN(c.initiative) && !float.IsNaN(c.cunning) && !float.IsNaN(c.leadership) && !float.IsNaN(c.administration),
                };
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-personality:read", "[CommanderPersonality] stat read failed: " + ex.GetType().Name + " " + ex.Message);
                return default;
            }
        }

        /// <summary>
        /// The commander's live initiative, the value vanilla's own battle AI reads (GetCommanderInitiative 61173);
        /// 0.5 when the commander cannot be read.
        /// </summary>
        internal static float LiveInitiative(int commanderIndex)
        {
            try
            {
                GameVars.Commander c = CommanderObject(commanderIndex);
                if (c == null) return 0.5f;
                float v = c.GetCommanderInitiative();
                if (float.IsNaN(v) || float.IsInfinity(v)) return 0.5f;
                return v < 0f ? 0f : v > 1f ? 1f : v;
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-personality:initiative", "[CommanderPersonality] live initiative read failed: " + ex.GetType().Name + " " + ex.Message);
                return 0.5f;
            }
        }

        /// <summary>The commander's live status (0 in duty, 2 captured, 3 wounded, 4 killed); 0 when unreadable.</summary>
        internal static int Status(int commanderIndex)
        {
            try
            {
                GameVars.Commander c = CommanderObject(commanderIndex);
                return c != null ? c.status : 0;
            }
            catch
            {
                return 0;
            }
        }
    }
}
