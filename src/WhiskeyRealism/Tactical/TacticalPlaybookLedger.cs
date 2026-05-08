using System;
using System.Collections.Generic;

namespace WhiskeyRealism.Tactical
{
    public enum TacticalPlaybook
    {
        HighGroundDefense = 0,
        CombinedArmsDefense = 1,
        RefuseRight = 2,
        RefuseLeft = 3,
        ProbeAndFix = 4,
        WeakPointPressure = 5,
        ReserveHeldCenter = 6,
        LineRelief = 7
    }

    public enum TacticalRefusedFlank
    {
        None = 0,
        Left = 1,
        Right = 2
    }

    public enum TacticalSectorPosition
    {
        Unknown = 0,
        Left = 1,
        Center = 2,
        Right = 3
    }

    public enum TacticalReservePolicy
    {
        HoldReserve = 0,
        PrepareRelief = 1,
        RelieveBatteredLine = 2,
        FlankGuard = 3,
        ExploitWeakPoint = 4
    }

    public enum TacticalLocalReactionPolicy
    {
        Conservative = 0,
        Standard = 1,
        Aggressive = 2
    }

    public readonly struct TacticalPlaybookSectorView
    {
        public TacticalPlaybookSectorView(
            int sectorId,
            TacticalSectorMission mission,
            TacticalSectorPosition position,
            float ownStrength,
            float enemyStrength,
            float confidence,
            bool strongPoint,
            bool flankRisk,
            float ownerSubordinateShare01)
        {
            SectorId = sectorId;
            Mission = mission;
            Position = position;
            OwnStrength = Sanitize(ownStrength);
            EnemyStrength = Sanitize(enemyStrength);
            Confidence = Clamp01(confidence);
            StrongPoint = strongPoint;
            FlankRisk = flankRisk;
            OwnerSubordinateShare01 = Clamp01(ownerSubordinateShare01);
        }

        public int SectorId { get; }
        public TacticalSectorMission Mission { get; }
        public TacticalSectorPosition Position { get; }
        public float OwnStrength { get; }
        public float EnemyStrength { get; }
        public float Confidence { get; }
        public bool StrongPoint { get; }
        public bool FlankRisk { get; }
        public float OwnerSubordinateShare01 { get; }

        private static float Sanitize(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 0f;
            return Math.Max(0f, value);
        }

        private static float Clamp01(float v)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) return 0f;
            if (v < 0f) return 0f;
            if (v > 1f) return 1f;
            return v;
        }
    }

    public readonly struct TacticalPlaybookInput
    {
        public TacticalPlaybookInput(
            CommanderIntent intent,
            int decisiveSectorId,
            TacticalPlaybookSectorView[] sectors,
            bool hasReserveAvailable,
            bool anchoredFlankLeft,
            bool anchoredFlankRight,
            float stalenessPressure)
        {
            Intent = intent;
            DecisiveSectorId = decisiveSectorId;
            Sectors = sectors ?? Array.Empty<TacticalPlaybookSectorView>();
            HasReserveAvailable = hasReserveAvailable;
            AnchoredFlankLeft = anchoredFlankLeft;
            AnchoredFlankRight = anchoredFlankRight;
            StalenessPressure = Math.Max(0f, stalenessPressure);
        }

        public CommanderIntent Intent { get; }
        public int DecisiveSectorId { get; }
        public TacticalPlaybookSectorView[] Sectors { get; }
        public bool HasReserveAvailable { get; }
        public bool AnchoredFlankLeft { get; }
        public bool AnchoredFlankRight { get; }
        public float StalenessPressure { get; }
    }

    public readonly struct TacticalPlaybookDecision
    {
        public TacticalPlaybookDecision(
            TacticalPlaybook playbook,
            int mainEffortSectorId,
            TacticalRefusedFlank refusedFlank,
            int[] probeSectorIds,
            int[] fixSectorIds,
            int[] holdSectorIds,
            TacticalReservePolicy reservePolicy,
            TacticalLocalReactionPolicy localReactionPolicy,
            float confidence,
            string reason)
        {
            Playbook = playbook;
            MainEffortSectorId = mainEffortSectorId;
            RefusedFlank = refusedFlank;
            ProbeSectorIds = probeSectorIds ?? Array.Empty<int>();
            FixSectorIds = fixSectorIds ?? Array.Empty<int>();
            HoldSectorIds = holdSectorIds ?? Array.Empty<int>();
            ReservePolicy = reservePolicy;
            LocalReactionPolicy = localReactionPolicy;
            Confidence = Clamp01(confidence);
            Reason = string.IsNullOrEmpty(reason) ? "unknown" : reason;
        }

        public TacticalPlaybook Playbook { get; }
        public int MainEffortSectorId { get; }
        public TacticalRefusedFlank RefusedFlank { get; }
        public int[] ProbeSectorIds { get; }
        public int[] FixSectorIds { get; }
        public int[] HoldSectorIds { get; }
        public TacticalReservePolicy ReservePolicy { get; }
        public TacticalLocalReactionPolicy LocalReactionPolicy { get; }
        public float Confidence { get; }
        public string Reason { get; }

        private static float Clamp01(float v)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) return 0f;
            if (v < 0f) return 0f;
            if (v > 1f) return 1f;
            return v;
        }
    }

    public static class TacticalPlaybookLedger
    {
        public static TacticalPlaybookDecision Decide(TacticalPlaybookInput input)
        {
            if (input.Sectors.Length == 0)
                return Empty(input.Intent, "no-sectors");

            var refused = ChooseRefusedFlank(input);
            int main = ChooseMainEffort(input, refused);

            switch (input.Intent)
            {
                case CommanderIntent.ProbeIntent:
                    return BuildProbeAndFix(input, refused);
                case CommanderIntent.HoldToLast:
                    return BuildLineHold(input, refused, TacticalReservePolicy.HoldReserve, "hold-to-last");
                case CommanderIntent.Hold:
                    return BuildLineHold(input, refused, TacticalReservePolicy.HoldReserve, "hold");
                case CommanderIntent.Defend:
                    return BuildDefense(input, refused, main);
                case CommanderIntent.Attack:
                case CommanderIntent.AllOutAttack:
                    return BuildAttack(input, refused, main);
                default:
                    return BuildLineHold(input, refused, TacticalReservePolicy.HoldReserve, "default-hold");
            }
        }

        private static TacticalRefusedFlank ChooseRefusedFlank(TacticalPlaybookInput input)
        {
            TacticalPlaybookSectorView left = default, right = default;
            bool foundL = false, foundR = false;
            for (int i = 0; i < input.Sectors.Length; i++)
            {
                if (input.Sectors[i].Position == TacticalSectorPosition.Left) { left = input.Sectors[i]; foundL = true; }
                if (input.Sectors[i].Position == TacticalSectorPosition.Right) { right = input.Sectors[i]; foundR = true; }
            }

            float leftRisk = foundL && left.FlankRisk && !input.AnchoredFlankLeft ? 1f : 0f;
            float rightRisk = foundR && right.FlankRisk && !input.AnchoredFlankRight ? 1f : 0f;

            if (leftRisk > 0f && leftRisk >= rightRisk) return TacticalRefusedFlank.Left;
            if (rightRisk > 0f) return TacticalRefusedFlank.Right;
            return TacticalRefusedFlank.None;
        }

        private static int ChooseMainEffort(TacticalPlaybookInput input, TacticalRefusedFlank refused)
        {
            if (input.DecisiveSectorId < 0) return -1;

            for (int i = 0; i < input.Sectors.Length; i++)
            {
                var s = input.Sectors[i];
                if (s.SectorId != input.DecisiveSectorId) continue;
                if (s.OwnerSubordinateShare01 > 0.5f) return -1;
                if (refused == TacticalRefusedFlank.Left && s.Position == TacticalSectorPosition.Left) return -1;
                if (refused == TacticalRefusedFlank.Right && s.Position == TacticalSectorPosition.Right) return -1;
                return s.SectorId;
            }
            return -1;
        }

        private static TacticalPlaybookDecision BuildProbeAndFix(TacticalPlaybookInput input, TacticalRefusedFlank refused)
        {
            var probe = new List<int>();
            var hold = new List<int>();
            for (int i = 0; i < input.Sectors.Length; i++)
            {
                var s = input.Sectors[i];
                if (s.Confidence < 0.55f) probe.Add(s.SectorId);
                else hold.Add(s.SectorId);
            }
            return new TacticalPlaybookDecision(
                TacticalPlaybook.ProbeAndFix,
                mainEffortSectorId: -1,
                refusedFlank: refused,
                probeSectorIds: probe.ToArray(),
                fixSectorIds: Array.Empty<int>(),
                holdSectorIds: hold.ToArray(),
                reservePolicy: TacticalReservePolicy.HoldReserve,
                localReactionPolicy: TacticalLocalReactionPolicy.Conservative,
                confidence: 0.6f,
                reason: "probe-intent");
        }

        private static TacticalPlaybookDecision BuildLineHold(TacticalPlaybookInput input, TacticalRefusedFlank refused, TacticalReservePolicy policy, string reason)
        {
            var hold = new List<int>();
            for (int i = 0; i < input.Sectors.Length; i++)
                hold.Add(input.Sectors[i].SectorId);
            return new TacticalPlaybookDecision(
                TacticalPlaybook.HighGroundDefense,
                mainEffortSectorId: -1,
                refusedFlank: refused,
                probeSectorIds: Array.Empty<int>(),
                fixSectorIds: Array.Empty<int>(),
                holdSectorIds: hold.ToArray(),
                reservePolicy: policy,
                localReactionPolicy: TacticalLocalReactionPolicy.Conservative,
                confidence: 0.65f,
                reason: reason);
        }

        private static TacticalPlaybookDecision BuildDefense(TacticalPlaybookInput input, TacticalRefusedFlank refused, int mainEffort)
        {
            var fix = new List<int>();
            var hold = new List<int>();
            for (int i = 0; i < input.Sectors.Length; i++)
            {
                var s = input.Sectors[i];
                if (s.SectorId == mainEffort) fix.Add(s.SectorId);
                else hold.Add(s.SectorId);
            }
            return new TacticalPlaybookDecision(
                refused == TacticalRefusedFlank.None ? TacticalPlaybook.CombinedArmsDefense
                    : (refused == TacticalRefusedFlank.Right ? TacticalPlaybook.RefuseRight : TacticalPlaybook.RefuseLeft),
                mainEffortSectorId: mainEffort,
                refusedFlank: refused,
                probeSectorIds: Array.Empty<int>(),
                fixSectorIds: fix.ToArray(),
                holdSectorIds: hold.ToArray(),
                reservePolicy: input.HasReserveAvailable ? TacticalReservePolicy.FlankGuard : TacticalReservePolicy.HoldReserve,
                localReactionPolicy: TacticalLocalReactionPolicy.Standard,
                confidence: 0.7f,
                reason: "defend");
        }

        private static TacticalPlaybookDecision BuildAttack(TacticalPlaybookInput input, TacticalRefusedFlank refused, int mainEffort)
        {
            if (mainEffort < 0)
                return BuildProbeAndFix(input, refused);

            var fix = new List<int>();
            var hold = new List<int>();
            int main = mainEffort;
            for (int i = 0; i < input.Sectors.Length; i++)
            {
                var s = input.Sectors[i];
                if (s.SectorId == main) continue;
                if (s.Confidence >= 0.55f && !s.StrongPoint) fix.Add(s.SectorId);
                else hold.Add(s.SectorId);
            }
            return new TacticalPlaybookDecision(
                TacticalPlaybook.WeakPointPressure,
                mainEffortSectorId: main,
                refusedFlank: refused,
                probeSectorIds: Array.Empty<int>(),
                fixSectorIds: fix.ToArray(),
                holdSectorIds: hold.ToArray(),
                reservePolicy: input.HasReserveAvailable ? TacticalReservePolicy.ExploitWeakPoint : TacticalReservePolicy.HoldReserve,
                localReactionPolicy: TacticalLocalReactionPolicy.Aggressive,
                confidence: 0.7f,
                reason: input.Intent == CommanderIntent.AllOutAttack ? "all-out-attack" : "attack");
        }

        private static TacticalPlaybookDecision Empty(CommanderIntent intent, string reason)
        {
            return new TacticalPlaybookDecision(
                TacticalPlaybook.HighGroundDefense,
                mainEffortSectorId: -1,
                refusedFlank: TacticalRefusedFlank.None,
                probeSectorIds: Array.Empty<int>(),
                fixSectorIds: Array.Empty<int>(),
                holdSectorIds: Array.Empty<int>(),
                reservePolicy: TacticalReservePolicy.HoldReserve,
                localReactionPolicy: TacticalLocalReactionPolicy.Conservative,
                confidence: 0f,
                reason: reason);
        }
    }
}
