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
    //
    // Ownership: a Whiskey write AND a Whiskey claim (agreement) stamp the
    // vanilla hold, so AdjustGroupAIStance leaves the group alone for
    // HoldHours after Whiskey last held an opinion. Stamping only on writes
    // made vanilla re-decide every 0.25 h, disagree, write (courier
    // ChangeStance) and Whiskey reassert (a second courier order) — a thrash
    // round trip per hold.
    //
    // Carve-out (yield): vanilla's own per-group safety valves win. Before a
    // claim or write, the caller ports vanilla's per-group stance chain
    // (VanillaPerGroupStance: 4239 s = 3, 4240 assault, 4244 defend, 4252
    // screen; GetGroupStrength 6025 already folds morale and experience into
    // the strength ratio — there are no separate morale/ammo conditions).
    // When that says defend (2) or screen (1) and Whiskey wants something
    // else, Whiskey yields: no stamp, the stamp is released (ReleaseStamp),
    // the record is dropped, and no reassert — so vanilla's weak-group
    // defend/screen fires on its next macro cycle. Army retreat (macroai 3)
    // yields the same way (4258; MicroAICheckForRetreats needs macroai 3 and
    // ai_stance 1, 5641).
    //
    // Not in the carve-out: the side-level lines 4248 (macroai 2 → every
    // group defends) and 4256 (macroai 1 with side forcebalance below the
    // screening trigger → screen). macroai has its own single owner (#44);
    // under its value Whiskey's group opinion stands.
    //
    // Design cost (remaining): while Whiskey holds a group, vanilla's
    // per-group re-decision is suppressed except through the yield above.
    // In particular vanilla's assault escalation (4240) cannot happen during
    // a Whiskey hold, and AI group charges need ai_stance 4
    // (MicroAICheckForCharges 4913). A vanilla assault set before Whiskey's
    // claim, or during a yield/release window, is never replaced except by an
    // explicit charge denial. Extending YieldsToVanilla to stance 4 would
    // restore the escalation. The caller's steady-state short-circuit
    // re-checks the carve-out at most every HoldHours * 0.5, so a group that
    // turns weak may wait that long before Whiskey yields.
    //
    // Inside the hold a lower-priority Whiskey source (#45 scorer) cannot flip
    // a stance the posture executor (#61) set, and the same source needs a
    // minimum dwell before changing its own opinion. Once Whiskey stops
    // claiming, the hold lapses and vanilla resumes.
    public enum StanceWriteSource
    {
        None = 0,
        Scorer = 1,
        Executor = 2,
        // Explicit charge denial (#45 DemoteCharge): the only source allowed
        // to replace a vanilla assault (4).
        ChargeDemotion = 3
    }

    public enum StanceWriteAction
    {
        Refuse,
        Claim,
        Write,
        // Hand the group back to vanilla: release the stamp, drop ownership.
        Yield
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
            StanceWriteSource source,
            int vanillaGroupStance = TacticalStanceOwnership.VanillaStanceUnknown)
        {
            IsAiGroup = isAiGroup;
            TargetStance = targetStance;
            CurrentOrderedStance = currentOrderedStance;
            MacroAi = macroAi;
            NowHours = nowHours;
            Record = record;
            Source = source == StanceWriteSource.None ? StanceWriteSource.Scorer : source;
            VanillaGroupStance = vanillaGroupStance;
        }

        public bool IsAiGroup { get; }
        public int TargetStance { get; }
        public int CurrentOrderedStance { get; }
        public int MacroAi { get; }
        public float NowHours { get; }
        public StanceHoldRecord Record { get; }
        public StanceWriteSource Source { get; }
        // Vanilla's per-group stance for this group right now
        // (TacticalStanceOwnership.VanillaPerGroupStance), or VanillaStanceUnknown.
        public int VanillaGroupStance { get; }
    }

    // Inputs of vanilla's per-group stance lines in AdjustGroupAIStance.
    public readonly struct VanillaGroupStanceInput
    {
        public VanillaGroupStanceInput(
            float groupStrength,
            int macroAi,
            bool lineGroup,
            bool defenderBattle,
            float assaultTrigger,
            float defendTrigger,
            float screenTrigger)
        {
            GroupStrength = groupStrength;
            MacroAi = macroAi;
            LineGroup = lineGroup;
            DefenderBattle = defenderBattle;
            AssaultTrigger = assaultTrigger;
            DefendTrigger = defendTrigger;
            ScreenTrigger = screenTrigger;
        }

        // GetGroupStrength(group, false, includemorale: true, includeexp: true, usestrengthofenemysurrounding: true).
        public float GroupStrength { get; }
        public int MacroAi { get; }
        // GetUnitSymbolData().oobunitsymbolid == 0.
        public bool LineGroup { get; }
        // bunits.GetBattleType(alliance, usereadinesswhendraw: true) == 1.
        public bool DefenderBattle { get; }
        // GamePrefs.strengthtriggerforassault_micro / fordefend_micro / forscreening_micro [macroai].
        public float AssaultTrigger { get; }
        public float DefendTrigger { get; }
        public float ScreenTrigger { get; }
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

        public const int VanillaStanceUnknown = -1;

        // Port of the per-group lines of AdjustGroupAIStance (4239-4252); the
        // side-level lines 4248 and 4256 and the retreat line 4258 are left out
        // (see header). Mirrors vanilla's local variable: 4252 tests the
        // already-reassigned local stance, not ai_stanceordered.
        public static int VanillaPerGroupStance(in VanillaGroupStanceInput input)
        {
            float strength = input.GroupStrength;
            int macro = input.MacroAi;
            if (float.IsNaN(strength) || float.IsInfinity(strength)) return 3;
            if (macro < 0 || macro > 3) return 3;
            bool offensiveMacro = macro == 0 || macro == 1;

            int stance = 3;
            if (strength > input.AssaultTrigger && offensiveMacro && input.LineGroup)
                stance = 4;
            if (strength < input.DefendTrigger && strength > 0f)
                stance = 2;
            if (offensiveMacro && (stance != 2 || input.DefenderBattle) &&
                strength < input.ScreenTrigger && strength > 0f)
                stance = 1;
            return stance;
        }

        // The vanilla per-group stances Whiskey yields to: defend and screen.
        public static bool YieldsToVanilla(int vanillaGroupStance, int targetStance)
        {
            return (vanillaGroupStance == 1 || vanillaGroupStance == 2) &&
                vanillaGroupStance != targetStance;
        }

        // True when Decide can reach a claim or write, i.e. when the caller
        // needs vanilla's per-group stance (GetGroupStrength is not free).
        public static bool NeedsVanillaGroupStance(bool isAiGroup, int targetStance, int macroAi)
        {
            return isAiGroup && targetStance >= 1 && targetStance <= 3 && macroAi >= 0 && macroAi <= 2;
        }

        public static StanceWriteDecision Decide(in StanceWriteInput input)
        {
            if (!input.IsAiGroup) return Refuse("not-ai-group");
            if (input.TargetStance == 0) return Refuse("no-opinion");
            if (input.TargetStance == 4) return Refuse("assault-vanilla-only");
            if (input.TargetStance < 1 || input.TargetStance > 3) return Refuse("invalid-stance");
            if (input.MacroAi < 0) return Refuse("macro-inactive");
            if (input.MacroAi == 3) return new StanceWriteDecision(StanceWriteAction.Yield, "vanilla-retreat");
            if (YieldsToVanilla(input.VanillaGroupStance, input.TargetStance))
                return new StanceWriteDecision(StanceWriteAction.Yield, "vanilla-weak-group");
            if (input.CurrentOrderedStance == 4 && input.Source != StanceWriteSource.ChargeDemotion)
                return Refuse("vanilla-assault");

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
                case StanceWriteAction.Yield:
                    return default(StanceHoldRecord);
                default:
                    return record;
            }
        }

        // A write always stamps. A claim stamps only when the caller could
        // check vanilla's per-group valves; otherwise vanilla stays free to
        // re-decide (thrash, but its valves intact).
        public static bool StampsVanillaHold(in StanceWriteDecision decision, in StanceWriteInput input)
        {
            if (decision.Action == StanceWriteAction.Write) return true;
            return decision.Action == StanceWriteAction.Claim &&
                input.VanillaGroupStance != VanillaStanceUnknown;
        }

        public static bool ReleasesVanillaHold(in StanceWriteDecision decision)
        {
            return decision.Action == StanceWriteAction.Yield;
        }

        // lastaistancechangetime value that lets vanilla 4264 re-evaluate on
        // its next cycle (yield / army retreat release).
        public static float ReleaseStamp(float nowHours)
        {
            return nowHours - MinSelfChangeHours;
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
