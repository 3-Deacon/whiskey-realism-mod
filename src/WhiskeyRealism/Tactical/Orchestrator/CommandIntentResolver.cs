using System;
using System.Collections.Generic;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    internal static class CommandIntentResolver
    {
        public static CommandIntentResolution ResolveForInstance(
            int instanceId,
            IReadOnlyList<CommandNodeIntent> intents)
        {
            return ResolveForInstance(instanceId, intents, null);
        }

        public static CommandIntentResolution ResolveForInstance(
            int instanceId,
            IReadOnlyList<CommandNodeIntent> intents,
            IReadOnlyList<DirectChildIntent> directChildIntents)
        {
            if (instanceId == 0 || intents == null || intents.Count == 0)
            {
                return ResolveDirectChildFallback(instanceId, directChildIntents, "no-command-intent");
            }

            var nodeId = "node-" + instanceId;
            for (var i = 0; i < intents.Count; i++)
            {
                if (string.Equals(intents[i].NodeId, nodeId, StringComparison.Ordinal))
                {
                    return new CommandIntentResolution(true, intents[i], "exact-command-node");
                }
            }

            return ResolveDirectChildFallback(instanceId, directChildIntents, "command-node-not-found");
        }

        public static CommandIntentResolution ResolveForInstance(
            int componentInstanceId,
            int gameObjectInstanceId,
            IReadOnlyList<CommandNodeIntent> intents,
            IReadOnlyList<DirectChildIntent> directChildIntents)
        {
            var primaryId = gameObjectInstanceId != 0 ? gameObjectInstanceId : componentInstanceId;
            var primary = ResolveForInstance(primaryId, intents, directChildIntents);
            if (primary.Found || componentInstanceId == 0 || componentInstanceId == primaryId)
            {
                return primary;
            }

            var fallback = ResolveForInstance(componentInstanceId, intents, directChildIntents);
            return fallback.Found ? fallback : primary;
        }

        private static CommandIntentResolution ResolveDirectChildFallback(
            int instanceId,
            IReadOnlyList<DirectChildIntent> directChildIntents,
            string missingReason)
        {
            if (instanceId == 0 || directChildIntents == null || directChildIntents.Count == 0)
            {
                return new CommandIntentResolution(false, default, missingReason);
            }

            string childId = "child-" + instanceId;
            string syntheticArmyId = "synth-army-" + instanceId;
            for (var i = 0; i < directChildIntents.Count; i++)
            {
                var direct = directChildIntents[i];
                if (!string.Equals(direct.ChildId, childId, StringComparison.Ordinal)
                    && !string.Equals(direct.ChildId, syntheticArmyId, StringComparison.Ordinal))
                {
                    continue;
                }

                return new CommandIntentResolution(
                    true,
                    new CommandNodeIntent(
                        "node-" + instanceId,
                        "node-" + instanceId,
                        direct.Role,
                        direct.Axis,
                        direct.PrimarySector,
                        (int)Math.Round(direct.SupportPriority01 * 100f),
                        direct.AggressionBias01,
                        depth: 0),
                    "o3-direct-child-fallback");
            }

            return new CommandIntentResolution(false, default, missingReason);
        }
    }
}
