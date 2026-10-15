namespace WhiskeyRealism.Tactical
{
    // Single-owner rules for Whiskey ai_stance writes (WS T4, B-stance3 / BR-M4).
    //
    // Vanilla anchors (decompile, build 14703111):
    //   AIBattle.AdjustGroupAIStance (4221-4278): iterates the AI group pool
    //     (unitsused), computes a stance per group from macroai + group strength
    //     and, when it differs from ai_stanceordered, calls
    //     ChangeStance(go, s, immediate:false, overwriteaigroups:false) and then
    //     sets ai_stance, ai_stanceordered and lastaistancechangetime itself.
    //     The re-evaluation guard is `lastaistancechangetime < now +
    //     GamePrefs.updatetimeforresettingofaistance` (4264). battleprefs.txt
    //     documents that value (1 game-hour) as "time to wait for resetting of
    //     new ai stances once set", but the comparison is inverted, so vanilla
    //     re-decides every macro cycle. A stamp in the future is the only way
    //     to make vanilla honour a hold.
    //   BattleUnits.ChangeStance (90772): dedups on ai_stanceordered; stance > 0
    //     sets permanentlydetached, so it must only target AI groups.
    //   UpdateAITasks (5857): AdjustGroupAIStance runs before
    //     AdjustGroupFormations, so #45 (Postfix on the former) writes before
    //     #61 (Postfix on the latter) in the same macro cycle.
    //   groupstancename (65061): 0 none, 1 screen, 2 defend, 3 attack, 4 assault.
    //     Assault (4) is vanilla's own strength-triggered micro decision (4239),
    //     and none (0) restores the vanilla AI group (RestoreOldAIGroup) — Whiskey
    //     writes neither.
    //   macroai == 3 (army retreat): vanilla forces every group to screen (4258).
    //
    // Ownership: a Whiskey write or claim stamps the vanilla hold so
    // AdjustGroupAIStance leaves the group alone while Whiskey keeps an opinion.
    // Inside the hold a lower-priority Whiskey source (#45 scorer) cannot flip a
    // stance the posture executor (#61) set, and the same source needs a minimum
    // dwell before changing its own opinion. Once Whiskey stops claiming, the
    // hold lapses and vanilla resumes.
    public enum StanceWriteSource
    {
        None = 0,
        Scorer = 1,
        Executor = 2
    }

    public enum StanceWriteAction
    {
        Refuse,
        Claim,
        Write
    }

    public readonly struct StanceHoldRecord
    {
        public StanceHoldRecord(int stance, float writtenAtHours, StanceWriteSource source)
        {
            Stance = stance;
            WrittenAtHours = writtenAtHours;
            Source = source;
            HasValue = source != StanceWriteSource.None;
        }

        public int Stance { get; }
        public float WrittenAtHours { get; }
        public StanceWriteSource Source { get; }
        public bool HasValue { get; }
    }

    public readonly struct StanceWriteInput
    {
        public StanceWriteInput(
            bool isAiGroup,
            int targetStance,
            int currentOrderedStance,
            int macroAi,
            float nowHours,
            StanceHoldRecord record,
            StanceWriteSource source)
        {
            IsAiGroup = isAiGroup;
            TargetStance = targetStance;
            CurrentOrderedStance = currentOrderedStance;
            MacroAi = macroAi;
            NowHours = nowHours;
            Record = record;
            Source = source == StanceWriteSource.None ? StanceWriteSource.Scorer : source;
        }

        public bool IsAiGroup { get; }
        public int TargetStance { get; }
        public int CurrentOrderedStance { get; }
        public int MacroAi { get; }
        public float NowHours { get; }
        public StanceHoldRecord Record { get; }
        public StanceWriteSource Source { get; }
    }

    public readonly struct StanceWriteDecision
    {
        public StanceWriteDecision(StanceWriteAction action, string reason)
        {
            Action = action;
            Reason = string.IsNullOrEmpty(reason) ? "unspecified" : reason;
        }

        public StanceWriteAction Action { get; }
        public string Reason { get; }
    }

    public static class TacticalStanceOwnership
    {
        // 15 game-minutes: long enough that a courier stance order is delivered
        // and acted on, short enough that vanilla regains a group soon after
        // Whiskey drops its opinion.
        public const float HoldHours = 0.25f;

        // 3 game-minutes before a source may change its own opinion.
        public const float MinSelfChangeHours = 0.05f;

        public static StanceWriteDecision Decide(in StanceWriteInput input)
        {
            if (!input.IsAiGroup) return Refuse("not-ai-group");
            if (input.TargetStance == 0) return Refuse("no-opinion");
            if (input.TargetStance == 4) return Refuse("assault-vanilla-only");
            if (input.TargetStance < 1 || input.TargetStance > 3) return Refuse("invalid-stance");
            if (input.MacroAi < 0) return Refuse("macro-inactive");
            if (input.MacroAi == 3) return Refuse("vanilla-retreat");

            StanceHoldRecord record = input.Record;
            if (record.HasValue && IsInsideHold(record, input.NowHours))
            {
                if (record.Stance == input.TargetStance)
                {
                    return input.CurrentOrderedStance == input.TargetStance
                        ? new StanceWriteDecision(StanceWriteAction.Claim, "held")
                        : new StanceWriteDecision(StanceWriteAction.Write, "reassert");
                }

                if (input.Source > record.Source)
                    return new StanceWriteDecision(StanceWriteAction.Write, "priority-override");
                if (input.Source == record.Source &&
                    input.NowHours - record.WrittenAtHours >= MinSelfChangeHours)
                    return new StanceWriteDecision(StanceWriteAction.Write, "opinion-change");

                return Refuse("whiskey-hold");
            }

            if (input.CurrentOrderedStance == input.TargetStance)
                return new StanceWriteDecision(StanceWriteAction.Claim, "already-ordered");

            return new StanceWriteDecision(StanceWriteAction.Write, "write");
        }

        public static StanceHoldRecord Next(in StanceWriteDecision decision, in StanceWriteInput input)
        {
            StanceHoldRecord record = input.Record;
            switch (decision.Action)
            {
                case StanceWriteAction.Write:
                    return new StanceHoldRecord(input.TargetStance, input.NowHours, input.Source);
                case StanceWriteAction.Claim:
                    if (record.HasValue &&
                        IsInsideHold(record, input.NowHours) &&
                        record.Stance == input.TargetStance &&
                        record.Source > input.Source)
                        return record;
                    return new StanceHoldRecord(input.TargetStance, input.NowHours, input.Source);
                default:
                    return record;
            }
        }

        // lastaistancechangetime value that makes vanilla 4264 skip the group
        // until nowHours + holdHours.
        public static float VanillaHoldStamp(float nowHours, float holdHours, float vanillaResetHours)
        {
            float reset = vanillaResetHours > 0f ? vanillaResetHours : 0f;
            float hold = holdHours > 0f ? holdHours : 0f;
            return nowHours + hold + reset;
        }

        // Mirror of the vanilla 4264 guard.
        public static bool VanillaWouldReevaluate(float lastStanceChangeHours, float nowHours, float vanillaResetHours)
        {
            return lastStanceChangeHours < nowHours + vanillaResetHours;
        }

        private static bool IsInsideHold(StanceHoldRecord record, float nowHours)
        {
            float age = nowHours - record.WrittenAtHours;
            return age >= 0f && age < HoldHours;
        }

        private static StanceWriteDecision Refuse(string reason)
        {
            return new StanceWriteDecision(StanceWriteAction.Refuse, reason);
        }
    }
}
