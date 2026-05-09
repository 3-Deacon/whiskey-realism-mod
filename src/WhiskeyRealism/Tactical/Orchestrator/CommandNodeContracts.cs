using System;
using System.Collections.Generic;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    internal readonly struct CommandNodeSnapshot
    {
        public CommandNodeSnapshot(
            string nodeId,
            string parentNodeId,
            int instanceId,
            int parentInstanceId,
            int allianceId,
            int rawUnitTyp,
            int commandHierarchyShift,
            string displayName,
            bool active,
            bool synthetic,
            int depth)
        {
            NodeId = string.IsNullOrWhiteSpace(nodeId) ? "node-unknown" : nodeId;
            ParentNodeId = string.IsNullOrWhiteSpace(parentNodeId) ? string.Empty : parentNodeId;
            InstanceId = instanceId;
            ParentInstanceId = parentInstanceId;
            AllianceId = allianceId;
            RawUnitTyp = rawUnitTyp;
            CommandHierarchyShift = commandHierarchyShift;
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? NodeId : displayName.Trim();
            Active = active;
            Synthetic = synthetic;
            Depth = Math.Max(0, depth);
        }

        public string NodeId { get; }
        public string ParentNodeId { get; }
        public int InstanceId { get; }
        public int ParentInstanceId { get; }
        public int AllianceId { get; }
        public int RawUnitTyp { get; }
        public int CommandHierarchyShift { get; }
        public string DisplayName { get; }
        public bool Active { get; }
        public bool Synthetic { get; }
        public int Depth { get; }
        public int EffectiveCommandLevel => RawUnitTyp - CommandHierarchyShift;
    }

    internal sealed class CommandTreeSnapshot
    {
        public static readonly CommandTreeSnapshot Empty = new CommandTreeSnapshot(
            -1,
            string.Empty,
            Array.Empty<CommandNodeSnapshot>(),
            0,
            0,
            string.Empty);

        public CommandTreeSnapshot(
            int allianceId,
            string rootNodeId,
            IReadOnlyList<CommandNodeSnapshot> nodes,
            int maxDepth,
            int missingParentCount,
            string rawUnitTypDistribution)
        {
            AllianceId = allianceId;
            RootNodeId = rootNodeId ?? string.Empty;
            Nodes = nodes ?? Array.Empty<CommandNodeSnapshot>();
            MaxDepth = Math.Max(0, maxDepth);
            MissingParentCount = Math.Max(0, missingParentCount);
            RawUnitTypDistribution = rawUnitTypDistribution ?? string.Empty;
        }

        public int AllianceId { get; }
        public string RootNodeId { get; }
        public IReadOnlyList<CommandNodeSnapshot> Nodes { get; }
        public int MaxDepth { get; }
        public int MissingParentCount { get; }
        public string RawUnitTypDistribution { get; }
        public bool HasNodes => Nodes.Count > 0;
    }

    internal readonly struct CommandNodeIntent
    {
        public CommandNodeIntent(
            string nodeId,
            string sourceNodeId,
            DirectChildRole role,
            DirectChildAxis axis,
            int primarySector,
            int supportPriority,
            float aggressionBias01,
            int depth)
        {
            NodeId = string.IsNullOrWhiteSpace(nodeId) ? "node-unknown" : nodeId;
            SourceNodeId = string.IsNullOrWhiteSpace(sourceNodeId) ? NodeId : sourceNodeId;
            Role = role;
            Axis = axis;
            PrimarySector = Math.Max(0, primarySector);
            SupportPriority = Math.Max(0, Math.Min(100, supportPriority));
            AggressionBias01 = float.IsNaN(aggressionBias01) || float.IsInfinity(aggressionBias01)
                ? 0f
                : Math.Max(0f, Math.Min(1f, aggressionBias01));
            Depth = Math.Max(0, depth);
        }

        public string NodeId { get; }
        public string SourceNodeId { get; }
        public DirectChildRole Role { get; }
        public DirectChildAxis Axis { get; }
        public int PrimarySector { get; }
        public int SupportPriority { get; }
        public float AggressionBias01 { get; }
        public int Depth { get; }
    }

    internal readonly struct CommandIntentResolution
    {
        public CommandIntentResolution(bool found, CommandNodeIntent intent, string reason)
        {
            Found = found;
            Intent = intent;
            Reason = string.IsNullOrWhiteSpace(reason) ? "unknown" : reason;
        }

        public bool Found { get; }
        public CommandNodeIntent Intent { get; }
        public string Reason { get; }
    }
}
