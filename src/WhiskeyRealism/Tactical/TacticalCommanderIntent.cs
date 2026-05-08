using System;
using WhiskeyRealism.Strategic;

namespace WhiskeyRealism.Tactical
{
    public enum CommanderIntent
    {
        AllOutAttack = 0,
        Attack = 1,
        ProbeIntent = 2,    // disambiguates from TacticalSectorMission.Probe in the same namespace
        Defend = 3,
        Hold = 4,
        HoldToLast = 5
    }

    public readonly struct TacticalIntentInput
    {
        public TacticalIntentInput(
            OperationPosture operationPosture,
            bool hasPlan,
            int vanillaMacro,
            float commanderInitiative01,
            float oddsConfidence,
            bool weakPointConfirmed)
        {
            OperationPosture = operationPosture;
            HasPlan = hasPlan;
            VanillaMacro = vanillaMacro;
            CommanderInitiative01 = Clamp01(commanderInitiative01);
            OddsConfidence = Clamp01(oddsConfidence);
            WeakPointConfirmed = weakPointConfirmed;
        }

        public OperationPosture OperationPosture { get; }
        public bool HasPlan { get; }
        public int VanillaMacro { get; }
        public float CommanderInitiative01 { get; }
        public float OddsConfidence { get; }
        public bool WeakPointConfirmed { get; }

        private static float Clamp01(float v)
        {
            // 0.5 mid-band default for confidence/initiative inputs: a NaN must not flip an attacker to no-confidence behavior. Sibling Sanitize/Clamp01 helpers in odds/sector ledgers clamp to 0 because they represent strength, not bounded probabilities.
            if (float.IsNaN(v) || float.IsInfinity(v)) return 0.5f;
            if (v < 0f) return 0f;
            if (v > 1f) return 1f;
            return v;
        }
    }

    public readonly struct TacticalIntentDecision
    {
        public TacticalIntentDecision(CommanderIntent intent, bool allowsCharge, string reason)
        {
            Intent = intent;
            AllowsCharge = allowsCharge;
            Reason = string.IsNullOrEmpty(reason) ? "unknown" : reason;
        }

        public CommanderIntent Intent { get; }
        public bool AllowsCharge { get; }
        public string Reason { get; }
    }

    public static class TacticalCommanderIntentResolver
    {
        public static TacticalIntentDecision Resolve(TacticalIntentInput input)
        {
            if (!input.HasPlan)
                return ResolveFromMacro(input);

            switch (input.OperationPosture)
            {
                case OperationPosture.ConcentratedAttack:
                    if (input.WeakPointConfirmed && input.CommanderInitiative01 >= 0.6f)
                        return new TacticalIntentDecision(CommanderIntent.AllOutAttack, true, "concentrated-attack-weak-point");
                    return new TacticalIntentDecision(CommanderIntent.Attack, true, "concentrated-attack");
                case OperationPosture.ExploitBreakthrough:
                    if (input.OddsConfidence < 0.55f)
                        return new TacticalIntentDecision(CommanderIntent.Attack, true, "exploit-low-confidence");
                    return new TacticalIntentDecision(CommanderIntent.AllOutAttack, true, "exploit-breakthrough");
                case OperationPosture.Counterstroke:
                    return new TacticalIntentDecision(CommanderIntent.Defend, true, "counterstroke");
                case OperationPosture.ProbeAndDevelop:
                    return new TacticalIntentDecision(CommanderIntent.ProbeIntent, false, "probe-and-develop");
                case OperationPosture.ScreenAndDelay:
                    return new TacticalIntentDecision(CommanderIntent.Defend, false, "screen-and-delay");
                case OperationPosture.ReinforceAndHold:
                    return new TacticalIntentDecision(CommanderIntent.Hold, false, "reinforce-and-hold");
                case OperationPosture.Recover:
                    return new TacticalIntentDecision(CommanderIntent.HoldToLast, false, "recover");
                case OperationPosture.Inherit:
                default:
                    return ResolveFromMacro(input);
            }
        }

        private static TacticalIntentDecision ResolveFromMacro(TacticalIntentInput input)
        {
            switch (input.VanillaMacro)
            {
                case 0: return new TacticalIntentDecision(CommanderIntent.Attack, true, "macro-assault");
                case 1: return new TacticalIntentDecision(CommanderIntent.Attack, true, "macro-attack");
                case 2: return new TacticalIntentDecision(CommanderIntent.Defend, false, "macro-defend");
                case 3: return new TacticalIntentDecision(CommanderIntent.HoldToLast, false, "macro-retreat-vanilla-owns");
                default: return new TacticalIntentDecision(CommanderIntent.Hold, false, "macro-dynamic");
            }
        }
    }
}
