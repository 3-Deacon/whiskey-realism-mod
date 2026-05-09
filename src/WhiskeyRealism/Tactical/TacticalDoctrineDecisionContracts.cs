using System;

namespace WhiskeyRealism.Tactical
{
    public enum TacticalDoctrineDecisionKind
    {
        Skip = 0,
        Apply = 1
    }

    public readonly struct TacticalMacroDecisionInput
    {
        public TacticalMacroDecisionInput(
            int vanillaMacro,
            bool debugOverrideActive,
            bool saveRestoreMacroActive,
            bool vanillaRetreatActive,
            float commanderAggression01,
            TacticalOddsAssessment odds)
        {
            VanillaMacro = vanillaMacro;
            DebugOverrideActive = debugOverrideActive;
            SaveRestoreMacroActive = saveRestoreMacroActive;
            VanillaRetreatActive = vanillaRetreatActive;
            CommanderAggression01 = Clamp01(commanderAggression01);
            Odds = odds;
        }

        public int VanillaMacro { get; }
        public bool DebugOverrideActive { get; }
        public bool SaveRestoreMacroActive { get; }
        public bool VanillaRetreatActive { get; }
        public float CommanderAggression01 { get; }
        public TacticalOddsAssessment Odds { get; }

        private static float Clamp01(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 0.5f;
            if (value < 0f) return 0f;
            if (value > 1f) return 1f;
            return value;
        }
    }

    public readonly struct TacticalMacroDecision
    {
        public TacticalMacroDecision(TacticalDoctrineDecisionKind kind, int macroAi, string reason)
        {
            Kind = kind;
            MacroAi = macroAi;
            Reason = string.IsNullOrWhiteSpace(reason) ? "unknown" : reason.Trim();
        }

        public TacticalDoctrineDecisionKind Kind { get; }
        public int MacroAi { get; }
        public string Reason { get; }
    }

    public readonly struct TacticalGroupStanceDecisionInput
    {
        public TacticalGroupStanceDecisionInput(
            int vanillaStance,
            int macroAi,
            TacticalSectorAssessment sector,
            bool orderFrictionAllowsChange,
            bool wlAllowsControl)
        {
            VanillaStance = vanillaStance;
            MacroAi = macroAi;
            Sector = sector;
            OrderFrictionAllowsChange = orderFrictionAllowsChange;
            WlAllowsControl = wlAllowsControl;
        }

        public int VanillaStance { get; }
        public int MacroAi { get; }
        public TacticalSectorAssessment Sector { get; }
        public bool OrderFrictionAllowsChange { get; }
        public bool WlAllowsControl { get; }
    }

    public readonly struct TacticalGroupStanceDecision
    {
        public TacticalGroupStanceDecision(TacticalDoctrineDecisionKind kind, int groupStance, string reason)
        {
            Kind = kind;
            GroupStance = groupStance;
            Reason = string.IsNullOrWhiteSpace(reason) ? "unknown" : reason.Trim();
        }

        public TacticalDoctrineDecisionKind Kind { get; }
        public int GroupStance { get; }
        public string Reason { get; }
    }
}
