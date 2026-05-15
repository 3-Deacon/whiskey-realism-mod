using System;
using System.Collections.Generic;

namespace WhiskeyRealism.Telemetry
{
    internal struct TelemetryTagRoute
    {
        internal TelemetryTagRoute(
            string tag,
            TelemetryLayer layer,
            TelemetryCategory category,
            string eventName,
            TelemetrySeverity severity,
            bool routeToSidecar,
            bool allowMainLog)
        {
            Tag = TelemetryEvent.Safe(tag);
            Layer = layer;
            Category = category;
            EventName = TelemetryEvent.Safe(eventName);
            Severity = severity;
            RouteToSidecar = routeToSidecar;
            AllowMainLog = allowMainLog;
        }

        internal string Tag { get; private set; }
        internal TelemetryLayer Layer { get; private set; }
        internal TelemetryCategory Category { get; private set; }
        internal string EventName { get; private set; }
        internal TelemetrySeverity Severity { get; private set; }
        internal bool RouteToSidecar { get; private set; }
        internal bool AllowMainLog { get; private set; }
    }

    internal static class TelemetryTagPolicy
    {
        private static readonly Dictionary<string, TelemetryTagRoute> Routes =
            new Dictionary<string, TelemetryTagRoute>(StringComparer.OrdinalIgnoreCase);

        static TelemetryTagPolicy()
        {
            AddMainLog("WhiskeyRealism", TelemetryLayer.System, TelemetryCategory.Health);
            AddMainLog("Telemetry", TelemetryLayer.System, TelemetryCategory.Health);

            AddTactical("TacticalDecisionMatrix", TelemetryCategory.State);
            AddTactical("TacticalCommandAssignment", TelemetryCategory.Decision);
            AddTactical("TacticalCommandPosture", TelemetryCategory.Decision);
            AddTactical("TacticalPostureSummary", TelemetryCategory.State);
            AddTactical("TacticalOpsLedger", TelemetryCategory.State);
            AddTactical("TacticalCommandTree", TelemetryCategory.State);
            AddTactical("TacticalCommanderRoster", TelemetryCategory.State);
            AddTactical("TacticalCommanderUnknown", TelemetryCategory.Failure, TelemetrySeverity.Warning);
            AddTactical("TacticalCommanderMode", TelemetryCategory.Health);
            AddTactical("TacticalPlan", TelemetryCategory.Decision);
            AddTactical("TacticalPlaybook", TelemetryCategory.Decision);
            AddTactical("TacticalIntent", TelemetryCategory.Decision);
            AddTactical("TacticalLocalReaction", TelemetryCategory.Decision);
            AddTactical("TacticalReplan", TelemetryCategory.Decision);
            AddTactical("TacticalCascade", TelemetryCategory.Trace);
            AddTactical("TacticalMacro", TelemetryCategory.State);
            AddTactical("TacticalMacroDecision", TelemetryCategory.Decision);
            AddTactical("TacticalGroup", TelemetryCategory.State);
            AddTactical("TacticalGroupDecision", TelemetryCategory.Decision);
            AddTactical("TacticalSector", TelemetryCategory.State);
            AddTactical("TacticalOdds", TelemetryCategory.State);
            AddTactical("TacticalFormationChange", TelemetryCategory.Write);
            AddTactical("TacticalPathShape", TelemetryCategory.Trace);
            AddTactical("TacticalWaypointDrift", TelemetryCategory.Trace);
            AddTactical("TacticalOrder", TelemetryCategory.Write);
            AddTactical("TacticalCommand", TelemetryCategory.Write);
            AddTactical("TacticalPlayerOrder", TelemetryCategory.Decision);
            AddTactical("TacticalCurrentOrder", TelemetryCategory.Decision);
            AddTactical("TacticalCourierQueue", TelemetryCategory.State);
            AddTactical("TacticalReserve", TelemetryCategory.Gate);
            AddTactical("TacticalFallback", TelemetryCategory.Gate);
            AddTactical("TacticalCharge", TelemetryCategory.Gate);
            AddTactical("TacticalArtillery", TelemetryCategory.Gate);
            AddTactical("TacticalReserveIntent", TelemetryCategory.Decision);
            AddTactical("TacticalReserveMove", TelemetryCategory.Write);
            AddTactical("TacticalReserveDrift", TelemetryCategory.Trace);
            AddTactical("TacticalReserveCommitGate", TelemetryCategory.Gate);
            AddTactical("TacticalReserveOrderDelayGuard", TelemetryCategory.Gate);
            AddTactical("TacticalChargeDeny", TelemetryCategory.Gate);
            AddTactical("TacticalChargePreserved", TelemetryCategory.Gate);
            AddTactical("TacticalChargeGuard", TelemetryCategory.Gate);
            AddTactical("TacticalDoctrineCharge", TelemetryCategory.Gate);
            AddTactical("TacticalOrchestratorChargeGate", TelemetryCategory.Gate);
            AddTactical("TacticalFeud", TelemetryCategory.Gate);
            AddTactical("TacticalFeudGuard", TelemetryCategory.Gate);
            AddTactical("TacticalDirectChildDiscovery", TelemetryCategory.State);
            AddTactical("TacticalDirectChildIntent", TelemetryCategory.Decision);
            AddTactical("TacticalDirectChildGate", TelemetryCategory.Gate);
            AddTactical("TacticalDeploymentPhase", TelemetryCategory.State);
            AddTactical("TacticalRegimentDiagnostics", TelemetryCategory.Trace);
            AddTactical("TacticalRegimentTrace", TelemetryCategory.Trace);
            AddTactical("TacticalObjectiveGuard", TelemetryCategory.Gate);
            AddTactical("TacticalObjectiveMove", TelemetryCategory.Write);
            AddTactical("TacticalObjectiveMutation", TelemetryCategory.Write);
            AddTactical("TacticalOrchestrator", TelemetryCategory.State);
            AddTactical("TacticalPathfinderDiscipline", TelemetryCategory.Gate);
            AddTactical("TacticalHqLinkGuard", TelemetryCategory.Gate);
            AddTactical("TacticalDiagnostic", TelemetryCategory.Trace);
            AddTactical("TacDeployObs", TelemetryCategory.State);
            AddTactical("TacDeployObsMove", TelemetryCategory.Write);
            AddTactical("TacDeployTerrain", TelemetryCategory.State);
            AddTactical("TacDeployTerrainAdvice", TelemetryCategory.Gate);

            AddCampaign("Heartbeat", TelemetryCategory.Health);
            AddCampaign("DailyOps", TelemetryCategory.State);
            AddCampaign("DailyOps:Perf", TelemetryCategory.Performance);
            AddCampaign("HistoricalOperation", TelemetryCategory.Decision);
            AddCampaign("DefenseIntent", TelemetryCategory.State);
            AddCampaign("DefenseIntent:asset", TelemetryCategory.State);
            AddCampaign("FiscalIntent", TelemetryCategory.Decision);
            AddCampaign("FiscalTelemetry", TelemetryCategory.State);
            AddCampaign("ConstructionIntent", TelemetryCategory.Decision);
            AddCampaign("ConstructionTelemetry", TelemetryCategory.State);
            AddCampaign("FormationDirective", TelemetryCategory.State);
            AddCampaign("FrontLedger", TelemetryCategory.State);
            AddCampaign("ArmyArea", TelemetryCategory.State);
            AddCampaign("CampaignMap", TelemetryCategory.State);
            AddCampaign("CampaignPace", TelemetryCategory.State);
            AddCampaign("CollapseRisk", TelemetryCategory.State);
            AddCampaign("Director", TelemetryCategory.Decision);
            AddCampaign("Director:trace", TelemetryCategory.Trace);
            AddCampaign("OperationalProbe", TelemetryCategory.Decision);
            AddCampaign("CoordinatedOps", TelemetryCategory.Write);
            AddCampaign("CoordinatedOps:Perf", TelemetryCategory.Performance);
            AddCampaign("ProjectDoctrine", TelemetryCategory.Decision);
            AddCampaign("ProjectAppointed", TelemetryCategory.State);
            AddCampaign("ProjectUnlock", TelemetryCategory.State);
            AddCampaign("W&LStartSelection", TelemetryCategory.Decision);
            AddCampaign("W&LCamp", TelemetryCategory.Trace);
            AddCampaign("Succession", TelemetryCategory.Decision);
            AddCampaign("Plan", TelemetryCategory.Decision);
        }

        internal static TelemetryTagRoute Route(string line)
        {
            string tag = ExtractTag(line);
            if (tag == "-")
                return new TelemetryTagRoute(tag, TelemetryLayer.System, TelemetryCategory.Health, tag, TelemetrySeverity.Info, false, true);

            TelemetryTagRoute route;
            if (Routes.TryGetValue(tag, out route))
                return IsSeriousLine(line) ? Serious(route) : route;

            if (tag.StartsWith("Tactical", StringComparison.OrdinalIgnoreCase))
                return IsSeriousLine(line)
                    ? Serious(Sidecar(tag, TelemetryLayer.Tactical, TelemetryCategory.Trace, TelemetrySeverity.Info))
                    : Sidecar(tag, TelemetryLayer.Tactical, TelemetryCategory.Trace, TelemetrySeverity.Info);

            if (tag.StartsWith("Succession", StringComparison.OrdinalIgnoreCase)
                || tag.StartsWith("Plan", StringComparison.OrdinalIgnoreCase))
                return IsSeriousLine(line)
                    ? Serious(Sidecar(tag, TelemetryLayer.Campaign, TelemetryCategory.Decision, TelemetrySeverity.Info))
                    : Sidecar(tag, TelemetryLayer.Campaign, TelemetryCategory.Decision, TelemetrySeverity.Info);

            if (IsCampaignDefault(tag))
                return IsSeriousLine(line)
                    ? Serious(Sidecar(tag, TelemetryLayer.Campaign, TelemetryCategory.Trace, TelemetrySeverity.Info))
                    : Sidecar(tag, TelemetryLayer.Campaign, TelemetryCategory.Trace, TelemetrySeverity.Info);

            if (tag.StartsWith("Patch:", StringComparison.OrdinalIgnoreCase)
                || tag.IndexOf("failed", StringComparison.OrdinalIgnoreCase) >= 0
                || tag.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return new TelemetryTagRoute(tag, TelemetryLayer.System, TelemetryCategory.Failure, tag, TelemetrySeverity.Warning, false, true);
            }

            return new TelemetryTagRoute(tag, TelemetryLayer.System, TelemetryCategory.Health, tag, TelemetrySeverity.Info, false, true);
        }

        internal static string ExtractTag(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return "-";

            int index = 0;
            while (index < line.Length)
            {
                int start = line.IndexOf('[', index);
                if (start < 0)
                    return "-";

                int end = line.IndexOf(']', start + 1);
                if (end < 0)
                    return "-";

                string tag = line.Substring(start + 1, end - start - 1).Trim();
                if (!tag.StartsWith("once:", StringComparison.OrdinalIgnoreCase) && tag.Length > 0)
                    return tag;

                index = end + 1;
            }

            return "-";
        }

        private static bool IsCampaignDefault(string tag)
        {
            return tag.StartsWith("DefenseIntent", StringComparison.OrdinalIgnoreCase)
                || tag.StartsWith("Construction", StringComparison.OrdinalIgnoreCase)
                || tag.StartsWith("Fiscal", StringComparison.OrdinalIgnoreCase)
                || tag.StartsWith("DailyOps", StringComparison.OrdinalIgnoreCase)
                || tag.StartsWith("CoordinatedOps", StringComparison.OrdinalIgnoreCase)
                || tag.StartsWith("Director", StringComparison.OrdinalIgnoreCase)
                || tag.StartsWith("W&L", StringComparison.OrdinalIgnoreCase)
                || tag.StartsWith("Project", StringComparison.OrdinalIgnoreCase)
                || tag.StartsWith("Succession", StringComparison.OrdinalIgnoreCase)
                || tag.StartsWith("Plan", StringComparison.OrdinalIgnoreCase)
                || tag.StartsWith("Campaign", StringComparison.OrdinalIgnoreCase)
                || string.Equals(tag, "FrontLedger", StringComparison.OrdinalIgnoreCase)
                || string.Equals(tag, "ArmyArea", StringComparison.OrdinalIgnoreCase)
                || string.Equals(tag, "FormationDirective", StringComparison.OrdinalIgnoreCase)
                || string.Equals(tag, "OperationalProbe", StringComparison.OrdinalIgnoreCase)
                || string.Equals(tag, "HistoricalOperation", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsSeriousLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return false;

            return line.IndexOf("failed", StringComparison.OrdinalIgnoreCase) >= 0
                || line.IndexOf("failure", StringComparison.OrdinalIgnoreCase) >= 0
                || line.IndexOf("exception", StringComparison.OrdinalIgnoreCase) >= 0
                || line.IndexOf("missing-anchor", StringComparison.OrdinalIgnoreCase) >= 0
                || line.IndexOf("missing required", StringComparison.OrdinalIgnoreCase) >= 0
                || line.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0
                || line.IndexOf("fatal", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static TelemetryTagRoute Serious(TelemetryTagRoute route)
        {
            return new TelemetryTagRoute(
                route.Tag,
                route.Layer,
                TelemetryCategory.Failure,
                route.EventName,
                TelemetrySeverity.Warning,
                route.RouteToSidecar,
                true);
        }

        private static void AddTactical(string tag, TelemetryCategory category)
        {
            AddTactical(tag, category, TelemetrySeverity.Info);
        }

        private static void AddTactical(string tag, TelemetryCategory category, TelemetrySeverity severity)
        {
            Routes[tag] = Sidecar(tag, TelemetryLayer.Tactical, category, severity);
        }

        private static void AddCampaign(string tag, TelemetryCategory category)
        {
            Routes[tag] = Sidecar(tag, TelemetryLayer.Campaign, category, TelemetrySeverity.Info);
        }

        private static void AddMainLog(string tag, TelemetryLayer layer, TelemetryCategory category)
        {
            Routes[tag] = new TelemetryTagRoute(tag, layer, category, tag, TelemetrySeverity.Info, false, true);
        }

        private static TelemetryTagRoute Sidecar(
            string tag,
            TelemetryLayer layer,
            TelemetryCategory category,
            TelemetrySeverity severity)
        {
            return new TelemetryTagRoute(tag, layer, category, tag, severity, true, false);
        }
    }
}
