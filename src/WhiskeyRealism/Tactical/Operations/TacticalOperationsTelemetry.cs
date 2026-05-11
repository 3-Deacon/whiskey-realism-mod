using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using WhiskeyRealism.Tactical.Orchestrator;

namespace WhiskeyRealism.Tactical.Operations
{
    public static class TacticalOperationsTelemetry
    {
        private static readonly ConditionalWeakTable<IDictionary<string, float>, Dictionary<string, string>> _intervalSignatures =
            new ConditionalWeakTable<IDictionary<string, float>, Dictionary<string, string>>();

        public static string OpsLedger(
            int side,
            TacticalCommanderMode mode,
            OperationRecord operation,
            StrategicBattleIntentSnapshot strategic,
            int commandCount)
        {
            return "[TacticalOpsLedger] side=" + side
                + " mode=" + mode
                + " shape=" + operation.Shape
                + " phase=" + operation.Phase
                + " primary=" + SafeToken(operation.PrimaryObjectiveId)
                + " commandCount=" + ClampCount(commandCount)
                + " theater=" + SafeToken(strategic.TheaterIntent)
                + " campaign=" + SafeToken(strategic.CampaignIntent)
                + " campaignObjective=" + SafeToken(strategic.CampaignObjectiveId)
                + " casualtyPressure=" + FormatFloat(strategic.CasualtyPressure)
                + " timePressure=" + FormatFloat(strategic.TimePressure)
                + " priority=" + FormatFloat(strategic.TheaterPriority)
                + " preserve=" + FormatFloat(strategic.PreserveForceBias);
        }

        public static string CommandAssignment(
            int side,
            CommandNodeOperationalState state,
            OperationRecord operation)
        {
            return "[TacticalCommandAssignment] side=" + side
                + " node=" + SafeToken(state.NodeId)
                + " echelon=" + state.Echelon
                + " role=" + state.Role
                + " task=" + state.Task
                + " taskState=" + state.TaskState
                + " objective=" + SafeToken(operation.PrimaryObjectiveId)
                + " shape=" + operation.Shape
                + " phase=" + operation.Phase;
        }

        public static string CommandPosture(
            int side,
            CommandNodeOperationalState state,
            PostureExecutionDecision decision,
            TacticalIdleClassification idle)
        {
            return "[TacticalCommandPosture] side=" + side
                + " node=" + SafeToken(state.NodeId)
                + " task=" + state.Task
                + " decision=" + decision.Action
                + " reason=" + SafeToken(decision.Reason)
                + " target=" + decision.Target
                + " clearInterruptedPaths=" + decision.ClearInterruptedPaths
                + " idle=" + idle;
        }

        public static string PostureSummary(
            int side,
            int validIdle,
            int illegalIdle,
            int recoveringStuck,
            int activeAttacks,
            int reservesWaiting)
        {
            return "[TacticalPostureSummary] side=" + side
                + " validIdle=" + ClampCount(validIdle)
                + " illegalIdle=" + ClampCount(illegalIdle)
                + " recoveringStuck=" + ClampCount(recoveringStuck)
                + " activeAttacks=" + ClampCount(activeAttacks)
                + " reservesWaiting=" + ClampCount(reservesWaiting);
        }

        public static string OpsLedgerSignature(
            int side,
            TacticalCommanderMode mode,
            OperationRecord operation,
            StrategicBattleIntentSnapshot strategic,
            int commandCount)
        {
            return side + "|" + mode
                + "|" + operation.Shape
                + "|" + operation.Phase
                + "|" + SafeToken(operation.PrimaryObjectiveId)
                + "|" + ClampCount(commandCount)
                + "|" + SafeToken(strategic.TheaterIntent)
                + "|" + SafeToken(strategic.CampaignIntent)
                + "|" + SafeToken(strategic.CampaignObjectiveId)
                + "|" + Bucket(strategic.CasualtyPressure)
                + "|" + Bucket(strategic.TimePressure)
                + "|" + Bucket(strategic.TheaterPriority)
                + "|" + Bucket(strategic.CasualtyTolerance)
                + "|" + Bucket(strategic.PreserveForceBias);
        }

        public static string CommandAssignmentSignature(
            int side,
            CommandNodeOperationalState state,
            OperationRecord operation)
        {
            return side
                + "|" + SafeToken(state.NodeId)
                + "|" + state.Echelon
                + "|" + state.Role
                + "|" + state.Task
                + "|" + state.TaskState
                + "|" + SafeToken(operation.PrimaryObjectiveId)
                + "|" + operation.Shape
                + "|" + operation.Phase;
        }

        public static string CommandPostureSignature(
            int side,
            CommandNodeOperationalState state,
            PostureExecutionDecision decision,
            TacticalIdleClassification idle)
        {
            return side
                + "|" + SafeToken(state.NodeId)
                + "|" + state.Task
                + "|" + decision.Action
                + "|" + SafeToken(decision.Reason)
                + "|" + decision.Target
                + "|" + decision.ClearInterruptedPaths
                + "|" + idle;
        }

        public static bool ShouldEmitSignatureChange(
            IDictionary<string, string> lastSignatures,
            string key,
            string signature)
        {
            if (lastSignatures == null) return true;
            string safeKey = SafeToken(key);
            string safeSignature = SafeToken(signature);
            if (lastSignatures.TryGetValue(safeKey, out var last) && last == safeSignature)
            {
                return false;
            }

            lastSignatures[safeKey] = safeSignature;
            return true;
        }

        public static bool ShouldEmitInterval(
            IDictionary<string, float> lastEmittedAt,
            string key,
            float nowSeconds,
            float minSeconds,
            bool verbose)
        {
            if (verbose) return true;
            if (lastEmittedAt == null) return true;
            string safeKey = SafeToken(key);
            float window = minSeconds <= 0f || float.IsNaN(minSeconds) || float.IsInfinity(minSeconds)
                ? 1f
                : minSeconds;
            if (!lastEmittedAt.TryGetValue(safeKey, out var last))
            {
                lastEmittedAt[safeKey] = nowSeconds;
                return true;
            }

            if (nowSeconds - last >= window)
            {
                lastEmittedAt[safeKey] = nowSeconds;
                return true;
            }

            return false;
        }

        public static bool ShouldEmitChangedAfterInterval(
            IDictionary<string, string> emittedSignatures,
            IDictionary<string, string> pendingSignatures,
            IDictionary<string, float> lastEmittedAt,
            string key,
            string signature,
            float nowSeconds,
            float minSeconds,
            bool verbose)
        {
            if (emittedSignatures == null || pendingSignatures == null || lastEmittedAt == null)
                return true;

            string safeKey = SafeToken(key);
            string safeSignature = SafeToken(signature);
            if (emittedSignatures.TryGetValue(safeKey, out var lastEmitted) && lastEmitted == safeSignature)
            {
                pendingSignatures.Remove(safeKey);
                return false;
            }

            pendingSignatures[safeKey] = safeSignature;
            if (!ShouldEmitInterval(lastEmittedAt, safeKey, nowSeconds, minSeconds, verbose))
                return false;

            emittedSignatures[safeKey] = safeSignature;
            pendingSignatures.Remove(safeKey);
            return true;
        }

        public static bool ShouldEmitChangedAfterInterval(
            IDictionary<string, float> lastEmittedAt,
            string key,
            string signature,
            float nowSeconds,
            float minSeconds)
        {
            if (lastEmittedAt == null) return true;

            string safeKey = SafeToken(key);
            string safeSignature = SafeToken(signature);
            var signatures = _intervalSignatures.GetValue(lastEmittedAt, _ => new Dictionary<string, string>());

            if (!signatures.TryGetValue(safeKey, out var lastSignature) || lastSignature != safeSignature)
            {
                signatures[safeKey] = safeSignature;
                lastEmittedAt[safeKey] = nowSeconds;
                return true;
            }

            return ShouldEmitInterval(lastEmittedAt, safeKey, nowSeconds, minSeconds, verbose: false);
        }

        public static string SafeToken(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "-";
            var sb = new StringBuilder(value.Trim().Length);
            foreach (char c in value.Trim())
            {
                sb.Append(char.IsWhiteSpace(c) || c == '|' ? '_' : c);
            }
            return sb.Length == 0 ? "-" : sb.ToString();
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
            return (Math.Round(value * 4f) / 4f).ToString("0.00");
        }
    }
}
