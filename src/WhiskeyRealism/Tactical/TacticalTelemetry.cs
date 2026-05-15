using System;
using System.Collections.Generic;

namespace WhiskeyRealism.Tactical
{
    public static class TacticalTelemetry
    {
        public static string MacroName(int macroAi)
        {
            switch (macroAi)
            {
                case -1: return "dynamic";
                case 0: return "assault";
                case 1: return "attack";
                case 2: return "defend";
                case 3: return "retreat";
                default: return "unknown";
            }
        }

        public static string SectorSourceName(TacticalSectorSource source)
        {
            switch (source)
            {
                case TacticalSectorSource.ObjectiveChain: return "objective-chain";
                case TacticalSectorSource.AngleSlice: return "angle-slice";
                case TacticalSectorSource.VisibleLineContact: return "visible-line-contact";
                default: return "none";
            }
        }

        public static string Summary(TacticalObservedEvent eventType, TacticalBattleContext context)
        {
            if (context == null) context = TacticalBattleContext.Empty();

            return Prefix(eventType) +
                   " side=" + context.Side +
                   " alliance=" + context.Alliance +
                   " macro=" + MacroName(context.MacroAi) +
                   " groups=" + ClampCount(context.GroupCount) +
                   " charging=" + ClampCount(context.ChargingCount) +
                   " feud=" + ClampCount(context.FeudGroupCount) +
                   " reserves=" + ClampCount(context.ReserveGroupCount) +
                   " artillery=" + ClampCount(context.ArtilleryGroupCount) +
                   " fallback=" + ClampCount(context.FallbackCount) +
                   " retreating=" + ClampCount(context.RetreatingCount) +
                   " visibleEnemy=" + ClampCount(context.VisibleEnemyCount) +
                   " chains=" + ClampCount(context.ObjectiveChainCount) +
                   " sectorSource=" + SectorSourceName(context.SectorSource) +
                   " forceBalance=" + FormatFloat(context.ForceBalance) +
                   " reinf24h=" + FormatFloat(context.ReinforcementsWithin24Hours) +
                   " currentOdds=" + FormatFloat(context.CurrentGlobalOdds) +
                   " projectedOdds=" + FormatFloat(context.ProjectedGlobalOdds) +
                   " decisive=" + context.DecisiveSectorId +
                   " sectorSig=" + Safe(context.SectorSignature) +
                   " orderSig=" + Safe(context.OrderSignature) +
                   " commandSig=" + Safe(context.CommandSignature) +
                   " oddsSig=" + Safe(context.OddsSignature) +
                   " odds=" + Safe(context.OddsSummary);
        }

        public static string Delta(TacticalObserverSnapshot before, TacticalObserverSnapshot after)
        {
            if (before == null) before = TacticalObserverSnapshot.Empty();
            if (after == null) after = TacticalObserverSnapshot.Empty();

            return "groups=" + before.GroupCount + "->" + after.GroupCount +
                   " charging=" + before.ChargingCount + "->" + after.ChargingCount +
                   " feud=" + before.FeudGroupCount + "->" + after.FeudGroupCount +
                   " reserves=" + before.ReserveGroupCount + "->" + after.ReserveGroupCount +
                   " artillery=" + before.ArtilleryGroupCount + "->" + after.ArtilleryGroupCount +
                   " fallback=" + before.FallbackCount + "->" + after.FallbackCount +
                   " retreating=" + before.RetreatingCount + "->" + after.RetreatingCount;
        }

        public static string Signature(TacticalObservedEvent eventType, TacticalBattleContext context)
        {
            if (context == null) context = TacticalBattleContext.Empty();

            return ((int)eventType).ToString() +
                   "|" + context.Side +
                   "|" + context.Alliance +
                   "|" + context.MacroAi +
                   "|" + context.GroupCount +
                   "|" + context.ChargingCount +
                   "|" + context.FeudGroupCount +
                   "|" + context.ReserveGroupCount +
                   "|" + context.ArtilleryGroupCount +
                   "|" + context.FallbackCount +
                   "|" + context.RetreatingCount +
                   "|" + context.VisibleEnemyCount +
                   "|" + context.ObjectiveChainCount +
                   "|" + ((int)context.SectorSource) +
                   "|" + Bucket(context.ForceBalance) +
                   "|" + Bucket(context.ReinforcementsWithin24Hours) +
                   "|" + Safe(context.SectorSignature) +
                   "|" + Safe(context.OrderSignature) +
                   "|" + Safe(context.CommandSignature) +
                   "|" + Bucket(context.CurrentGlobalOdds) +
                   "|" + Bucket(context.ProjectedGlobalOdds) +
                   "|" + context.DecisiveSectorId +
                   "|" + Safe(context.OddsSignature);
        }

        public static string DecisionInputSignature(string eventName, params object[] inputs)
        {
            var parts = new List<string>();
            parts.Add(Safe(eventName));
            if (inputs == null || inputs.Length == 0)
                return string.Join("|", parts.ToArray());

            for (int i = 0; i < inputs.Length; i++)
                parts.Add(FormatSignatureValue(inputs[i]));

            return string.Join("|", parts.ToArray());
        }

        public static bool ShouldEmit(
            IDictionary<string, float> lastEmittedAt,
            string key,
            string signature,
            float nowSeconds,
            float minSeconds,
            bool verbose)
        {
            string fullKey = key + "|" + Safe(signature);
            if (verbose) return true;
            if (lastEmittedAt == null) return true;
            if (!lastEmittedAt.TryGetValue(fullKey, out var last))
            {
                lastEmittedAt[fullKey] = nowSeconds;
                return true;
            }

            if (nowSeconds - last >= minSeconds)
            {
                lastEmittedAt[fullKey] = nowSeconds;
                return true;
            }

            return false;
        }

        private static string Prefix(TacticalObservedEvent eventType)
        {
            switch (eventType)
            {
                case TacticalObservedEvent.Macro: return "[TacticalMacro]";
                case TacticalObservedEvent.Group: return "[TacticalGroup]";
                case TacticalObservedEvent.Charge: return "[TacticalCharge]";
                case TacticalObservedEvent.Feud: return "[TacticalFeud]";
                case TacticalObservedEvent.Sector: return "[TacticalSector]";
                case TacticalObservedEvent.Order: return "[TacticalOrder]";
                case TacticalObservedEvent.Reserve: return "[TacticalReserve]";
                case TacticalObservedEvent.Artillery: return "[TacticalArtillery]";
                case TacticalObservedEvent.Fallback: return "[TacticalFallback]";
                case TacticalObservedEvent.PlayerOrder: return "[TacticalPlayerOrder]";
                case TacticalObservedEvent.Command: return "[TacticalCommand]";
                case TacticalObservedEvent.Odds: return "[TacticalOdds]";
                default: return "[Tactical]";
            }
        }

        private static int ClampCount(int value)
        {
            return value < 0 ? 0 : value;
        }

        private static string FormatFloat(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return "0.00";
            return value.ToString("0.00");
        }

        private static string Bucket(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return "0.0";
            return (Math.Round(value * 2f) / 2f).ToString("0.0");
        }

        private static string Bucket(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return "0.0";
            return (Math.Round(value * 2.0) / 2.0).ToString("0.0");
        }

        private static string FormatSignatureValue(object value)
        {
            if (value == null) return "-";
            if (value is float f) return Bucket(f);
            if (value is double d) return Bucket(d);
            return Safe(value.ToString());
        }

        private static string Safe(string value)
        {
            return string.IsNullOrEmpty(value) ? "-" : value.Replace(' ', '_');
        }
    }
}
