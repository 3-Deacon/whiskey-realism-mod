using System;

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
        public TacticalIntentInput(int vanillaMacro, float commanderInitiative01)
        {
            VanillaMacro = vanillaMacro;
            CommanderInitiative01 = Clamp01(commanderInitiative01);
        }

        public int VanillaMacro { get; }
        public float CommanderInitiative01 { get; }

        private static float Clamp01(float v)
        {
            // 0.5 mid-band default for the initiative input: a NaN must not flip an attacker to no-confidence behavior. Sibling Sanitize/Clamp01 helpers in odds/sector ledgers clamp to 0 because they represent strength, not bounded probabilities.
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

    // The legacy B6a intent resolver (telemetry only with the army orchestrator on). BC13 deleted its
    // operation-posture switch: the observer never had a plan, so every call already took the macro path.
    public static class TacticalCommanderIntentResolver
    {
        public static TacticalIntentDecision Resolve(TacticalIntentInput input)
        {
            // Commander initiative modulates intent on the macro path:
            // high-initiative commanders (Hood, Jackson, Grant) push attack into
            // all-out-attack; low-initiative commanders (McClellan, Johnston)
            // de-escalate macro=attack to probe. Defensive and retreat macros
            // stay vanilla-owned. Boundaries chosen so the mid-band 0.5 default
            // produces the same behavior as before (no behavior change for
            // unmatched commanders).
            switch (input.VanillaMacro)
            {
                case 0:
                    if (input.CommanderInitiative01 < 0.30f)
                        return new TacticalIntentDecision(CommanderIntent.Attack, true, "macro-assault-cautious-commander");
                    return new TacticalIntentDecision(CommanderIntent.AllOutAttack, true, "macro-assault");
                case 1:
                    if (input.CommanderInitiative01 >= 0.75f)
                        return new TacticalIntentDecision(CommanderIntent.AllOutAttack, true, "macro-attack-aggressive-commander");
                    if (input.CommanderInitiative01 < 0.30f)
                        return new TacticalIntentDecision(CommanderIntent.ProbeIntent, false, "macro-attack-cautious-commander");
                    return new TacticalIntentDecision(CommanderIntent.Attack, true, "macro-attack");
                case 2:
                    return new TacticalIntentDecision(CommanderIntent.Defend, false, "macro-defend");
                case 3:
                    return new TacticalIntentDecision(CommanderIntent.HoldToLast, false, "macro-retreat-vanilla-owns");
                default:
                    return new TacticalIntentDecision(CommanderIntent.Hold, false, "macro-dynamic");
            }
        }
    }
}
