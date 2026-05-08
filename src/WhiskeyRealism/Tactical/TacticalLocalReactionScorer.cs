using System;

namespace WhiskeyRealism.Tactical
{
    public enum LocalReaction
    {
        MaintainLine = 0,
        Screen = 1,
        ProbeRange = 2,
        RefuseFlank = 3,
        LimitedCounterstroke = 4,
        DenyCharge = 5,
        PermitCharge = 6,
        LineReliefRequest = 7,
        LocalFallbackPressure = 8
    }

    public readonly struct TacticalLocalReactionInput
    {
        public TacticalLocalReactionInput(
            CommanderIntent intent,
            TacticalLocalReactionPolicy playbookPolicy,
            TacticalSectorMission sectorMission,
            float sectorOdds,
            float sectorConfidence,
            bool targetVisible,
            bool targetBroken,
            bool targetStrongPoint,
            float morale01,
            float ammoRatio01,
            float casualtyRatio01,
            bool flankRisk,
            bool wlOwnershipSafe,
            bool chargeCooldownReady,
            bool stalenessActive,
            bool pathRiskActive)
        {
            Intent = intent;
            PlaybookPolicy = playbookPolicy;
            SectorMission = sectorMission;
            SectorOdds = SanitizeNonnegative(sectorOdds);
            SectorConfidence = Clamp01(sectorConfidence);
            TargetVisible = targetVisible;
            TargetBroken = targetBroken;
            TargetStrongPoint = targetStrongPoint;
            Morale01 = Clamp01(morale01);
            AmmoRatio01 = Clamp01(ammoRatio01);
            CasualtyRatio01 = Clamp01(casualtyRatio01);
            FlankRisk = flankRisk;
            WlOwnershipSafe = wlOwnershipSafe;
            ChargeCooldownReady = chargeCooldownReady;
            StalenessActive = stalenessActive;
            PathRiskActive = pathRiskActive;
        }

        public CommanderIntent Intent { get; }
        public TacticalLocalReactionPolicy PlaybookPolicy { get; }
        public TacticalSectorMission SectorMission { get; }
        public float SectorOdds { get; }
        public float SectorConfidence { get; }
        public bool TargetVisible { get; }
        public bool TargetBroken { get; }
        public bool TargetStrongPoint { get; }
        public float Morale01 { get; }
        public float AmmoRatio01 { get; }
        public float CasualtyRatio01 { get; }
        public bool FlankRisk { get; }
        public bool WlOwnershipSafe { get; }
        public bool ChargeCooldownReady { get; }
        public bool StalenessActive { get; }
        public bool PathRiskActive { get; }

        private static float SanitizeNonnegative(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 0f;
            return Math.Max(0f, value);
        }

        private static float Clamp01(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 0f;
            if (value < 0f) return 0f;
            if (value > 1f) return 1f;
            return value;
        }
    }

    public readonly struct TacticalLocalReactionDecision
    {
        public TacticalLocalReactionDecision(
            LocalReaction reaction,
            bool reliefRequested,
            float confidence,
            string reason)
        {
            Reaction = reaction;
            ReliefRequested = reliefRequested;
            Confidence = Clamp01(confidence);
            Reason = string.IsNullOrEmpty(reason) ? "unknown" : reason;
        }

        public LocalReaction Reaction { get; }
        public bool ReliefRequested { get; }
        public float Confidence { get; }
        public string Reason { get; }

        private static float Clamp01(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 0f;
            if (value < 0f) return 0f;
            if (value > 1f) return 1f;
            return value;
        }
    }

    public static class TacticalLocalReactionScorer
    {
        public static TacticalLocalReactionDecision Score(TacticalLocalReactionInput input)
        {
            if (!input.WlOwnershipSafe)
                return Decision(LocalReaction.MaintainLine, false, input, "wl-ownership-blocked");
            if (input.StalenessActive)
                return Decision(LocalReaction.MaintainLine, false, input, "request-new-intent");

            switch (input.Intent)
            {
                case CommanderIntent.HoldToLast:
                    return Decision(LocalReaction.MaintainLine, false, input, "hold-to-last");
                case CommanderIntent.Hold:
                    return ReliefOrMaintain(input, "hold");
                case CommanderIntent.Defend:
                    if (input.FlankRisk)
                        return ReliefTriggered(input)
                            ? Decision(LocalReaction.LineReliefRequest, true, input, "line-relief")
                            : Decision(LocalReaction.RefuseFlank, false, input, "flank-risk");
                    return Defend(input);
                case CommanderIntent.ProbeIntent:
                    if (input.FlankRisk)
                        return Decision(LocalReaction.RefuseFlank, false, input, "flank-risk");
                    return input.SectorConfidence < 0.55f
                        ? Decision(LocalReaction.ProbeRange, false, input, "probe-low-confidence")
                        : Decision(LocalReaction.Screen, false, input, "probe-screen");
                case CommanderIntent.Attack:
                case CommanderIntent.AllOutAttack:
                    if (input.FlankRisk)
                        return Decision(LocalReaction.RefuseFlank, false, input, "flank-risk");
                    return Attack(input);
                default:
                    return Decision(LocalReaction.MaintainLine, false, input, "default-maintain");
            }
        }

        private static TacticalLocalReactionDecision Defend(TacticalLocalReactionInput input)
        {
            if (input.TargetVisible &&
                !input.TargetStrongPoint &&
                input.SectorOdds >= 1.20f &&
                input.SectorConfidence >= 0.55f &&
                !input.PathRiskActive)
            {
                return Decision(LocalReaction.LimitedCounterstroke, false, input, "limited-counterstroke");
            }

            return ReliefOrMaintain(input, "defend-maintain");
        }

        private static TacticalLocalReactionDecision Attack(TacticalLocalReactionInput input)
        {
            if (input.PathRiskActive)
                return Decision(LocalReaction.MaintainLine, false, input, "path-risk");

            if (input.SectorMission == TacticalSectorMission.Fix ||
                input.SectorMission == TacticalSectorMission.EconomyOfForce)
                return Decision(LocalReaction.Screen, false, input, "screen-after-denied-charge");

            if (input.SectorMission != TacticalSectorMission.AttackWeakPoint)
                return Decision(LocalReaction.MaintainLine, false, input, "non-attack-sector");

            if (input.TargetVisible &&
                !input.TargetStrongPoint &&
                input.ChargeCooldownReady &&
                input.WlOwnershipSafe &&
                input.SectorConfidence >= 0.55f)
            {
                return Decision(LocalReaction.PermitCharge, false, input, "charge-permitted");
            }

            if (input.TargetStrongPoint)
                return Decision(LocalReaction.MaintainLine, false, input, "strongpoint");
            if (!input.ChargeCooldownReady)
                return Decision(LocalReaction.MaintainLine, false, input, "cooldown-active");

            return Decision(LocalReaction.MaintainLine, false, input, "charge-denied");
        }

        private static TacticalLocalReactionDecision ReliefOrMaintain(TacticalLocalReactionInput input, string maintainReason)
        {
            if (ReliefTriggered(input))
                return Decision(LocalReaction.LineReliefRequest, true, input, "line-relief");

            return Decision(LocalReaction.MaintainLine, false, input, maintainReason);
        }

        private static bool ReliefTriggered(TacticalLocalReactionInput input)
        {
            return input.CasualtyRatio01 >= 0.40f ||
                input.Morale01 <= 0.35f ||
                input.AmmoRatio01 <= 0.20f ||
                (input.FlankRisk && input.Morale01 <= 0.55f);
        }

        private static TacticalLocalReactionDecision Decision(
            LocalReaction reaction,
            bool reliefRequested,
            TacticalLocalReactionInput input,
            string reason)
        {
            return new TacticalLocalReactionDecision(reaction, reliefRequested, input.SectorConfidence, reason);
        }
    }
}
