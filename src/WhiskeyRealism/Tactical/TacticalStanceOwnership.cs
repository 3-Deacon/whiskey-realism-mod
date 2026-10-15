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
    //     Assault (4) is vanilla's own strength-triggered micro decision (4240),
    //     and none (0) restores the vanilla AI group (RestoreOldAIGroup) — Whiskey
    //     writes neither.
    //   macroai == 3 (army retreat): vanilla forces every group to screen (4258).
    //
    // Two clocks (BC4): hold records (StanceHoldRecord.WrittenAtHours, HoldHours,
    // MinSelfChangeHours) run on the doctrine clock (TacticalDoctrineTime), which
    // is frozen while vanilla's night end-of-day cycle jumps the raw clock ~9 h
    // (CheckTimeIssues 86368-86490); a raw-clock record expired every hold
    // overnight. lastaistancechangetime stays on GameVars.currenttimefromstart
    // because vanilla compares it with that clock (4264): VanillaHoldStamp and
    // ReleaseStamp take the raw clock (StanceWriteInput.VanillaNowHours).
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
    // defend/screen fires on its next macro cycle. Vanilla's assault
    // escalation (4240 → 4) also gets a yield, but only when Whiskey's own
    // target is attack (3): escalation agrees with that intent. When Whiskey
    // wants screen or defend, it keeps holding. Army retreat (macroai 3)
    // yields the same way (4258; MicroAICheckForRetreats needs macroai 3 and
    // ai_stance 1, 5641).
    //
    // Not in the carve-out (side-level lines, not group valves):
    //   4248 macroai 2 → every group defends: a pure function of macroai,
    //     which has its own single owner (#44).
    //   4256 macroai 1 && flag && local stance != 2 && strength > 0 → screen.
    //     flag is computed once per call for the whole side (4229:
    //     sideinformation[sideofai].forcebalance < GetScreeningTrigger(),
    //     4216 = forcebalancetriggerattack * 0.5); the only per-group input is
    //     "in combat" (strength > 0). Vanilla only sits at macroai 1 below
    //     that ratio through the meeting/attacker branches of
    //     CheckGlobalAIStrategy (6400 battleType 0, 6412-6424 attacker with
    //     no retreat), so 4256 is vanilla's side-wide "outnumbered — screen
    //     everything engaged" response. Yielding to it would hand every
    //     engaged group of that side back to vanilla, i.e. replace Whiskey's
    //     own force-balance doctrine wholesale; the side-level answer to being
    //     outnumbered is #44's macroai (2, or vanilla's own 3), under which
    //     4248 / 4258 apply.
    //
    // Design cost (remaining): while Whiskey holds a group, vanilla's
    // per-group re-decision is suppressed except through the yields above —
    // so a group Whiskey holds at screen or defend never escalates to assault.
    // The visible effect of stance 4 is the micro AI's assault placement
    // (5694: distanceofmaxformicroaiplacement[ai_stance]); AI group charges
    // via MicroAICheckForCharges are effectively dead in vanilla (4910
    // compares the groupaiobject GameObject with the aigroup Regiment, never
    // equal). A vanilla assault is never replaced except by an explicit
    // charge denial. The caller's steady-state short-circuit
    // re-checks the carve-out at most every HoldHours * 0.5, so a group that
    // turns weak may wait that long before Whiskey yields.
    //
    // Inside the hold a lower-priority Whiskey source (#45 scorer) can neither
    // flip a stance the posture executor (#61) set nor yield it to vanilla;
    // only the owner or a higher source may. A weak group held by #61 goes
    // back to vanilla on #61's next pass or when the record ages past
    // HoldHours. The same source needs a minimum dwell before changing its
    // own opinion. Once Whiskey stops claiming, the hold lapses and vanilla
    // resumes.
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
            int vanillaGroupStance = TacticalStanceOwnership.VanillaStanceUnknown,
            float vanillaNowHours = float.NaN)
        {
            IsAiGroup = isAiGroup;
            TargetStance = targetStance;
            CurrentOrderedStance = currentOrderedStance;
            MacroAi = macroAi;
            NowHours = nowHours;
            Record = record;
            Source = source == StanceWriteSource.None ? StanceWriteSource.Scorer : source;
            VanillaGroupStance = vanillaGroupStance;
            VanillaNowHours = float.IsNaN(vanillaNowHours) ? nowHours : vanillaNowHours;
        }

        public bool IsAiGroup { get; }
        public int TargetStance { get; }
        public int CurrentOrderedStance { get; }
        public int MacroAi { get; }
        // Doctrine hours (TacticalDoctrineTime, frozen across vanilla's night
        // end-of-day cycle): every hold record and dwell is on this clock.
        public float NowHours { get; }
        // GameVars.currenttimefromstart: the clock vanilla compares
        // lastaistancechangetime with (4264). Only stamps use it.
        public float VanillaNowHours { get; }
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

        // Battle-core intent sources on the ownership ladder: the explicit charge
        // denial outranks everything, the #45 scorer is the lowest rung, every
        // other battle-core source holds a stance like the executor (#61).
        public static StanceWriteSource SourceFor(Battle.IntentSource source)
        {
            switch (source)
            {
                case Battle.IntentSource.ChargeDemotion:
                    return StanceWriteSource.ChargeDemotion;
                case Battle.IntentSource.GroupStanceScorer:
                    return StanceWriteSource.Scorer;
                default:
                    return StanceWriteSource.Executor;
            }
        }

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

        // The vanilla per-group stances Whiskey yields to: defend and screen
        // whenever Whiskey wants something else; assault only when Whiskey
        // wants attack (escalation agrees with its intent).
        public static bool YieldsToVanilla(int vanillaGroupStance, int targetStance)
        {
            if (vanillaGroupStance == 4) return targetStance == 3;
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
            // An explicit charge denial (#45 DemoteCharge) precedes every yield:
            // vanilla only reaches 4 on a strong group (4240), so its per-group
            // stance is 4 at demotion time and the escalation yield would
            // otherwise swallow the demotion.
            if (input.CurrentOrderedStance == 4 && input.Source == StanceWriteSource.ChargeDemotion)
                return new StanceWriteDecision(StanceWriteAction.Write, "charge-demotion");
            if (YieldsToVanilla(input.VanillaGroupStance, input.TargetStance))
            {
                // A source may only give away a hold it owns or outranks.
                if (input.Record.HasValue && IsInsideHold(input.Record, input.NowHours) &&
                    input.Record.Source > input.Source)
                    return Refuse("whiskey-hold");
                return new StanceWriteDecision(
                    StanceWriteAction.Yield,
                    input.VanillaGroupStance == 4 ? "vanilla-assault-escalation" : "vanilla-weak-group");
            }
            if (input.CurrentOrderedStance == 4) return Refuse("vanilla-assault");

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

        // The vanilla hold stamp for a claim or write: on the raw clock
        // (input.VanillaNowHours), never the doctrine clock the record uses.
        public static float VanillaHoldStamp(in StanceWriteInput input, float vanillaResetHours)
        {
            return VanillaHoldStamp(input.VanillaNowHours, HoldHours, vanillaResetHours);
        }

        // lastaistancechangetime value that makes vanilla 4264 skip the group
        // until nowHours + holdHours.
        public static float VanillaHoldStamp(float nowHours, float holdHours, float vanillaResetHours)
        {
            float reset = vanillaResetHours > 0f ? vanillaResetHours : 0f;
            float hold = holdHours > 0f ? holdHours : 0f;
            return nowHours + hold + reset;
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
