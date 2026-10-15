// Infantry charge doctrine (battle-core BC6a/BC6b, spec §4b, plan decision D10). Pure: no Unity or
// vanilla types. The producer (BC6b, Producers/ChargeIntentProducer(.Plan).cs) fills ChargeInput
// from the BattleSnapshot and its per-regiment memory and submits the result on the
// CombatBehavior channel, PerRegiment, Priority.Executor, IntentSource.ChargeDoctrine.
//
// Vanilla (Assembly-CSharp build 14703111):
// - AI infantry never charges on its own. MicroAICheckForCharges (4905, sole caller 5647) compares
//   the GameObject groupaiobject with the Regiment aigroup at 4910 and never proceeds; micro cycle 6
//   (5617-5638) sets the group's combat behavior to 2 for stances 1-4 and never to 3. Only the
//   player UI sets 3 (203769).
// - Regiment.UpdateChargeTarget arms charge mode at 122214: combatbehavior == 3 && movementmode == 0
//   && regimentpaths > 0 -> SetMovementMode(3). In mode 3 (122226) it takes unitrange.enemyinrangereg[0]
//   (122228), skips routed/in-melee targets (122245, 122253), checks the distance from its parent
//   group's position against GamePrefs.maxchargeradius with a strict < (122257), then its own odds
//   check strength ratio > GamePrefs.microaitriggerforcharge (122259, weights
//   weightingfactorsformicroaicharge[0..3], 54777-54781). Pass: infantry with behavior 3 fires a
//   volley first (122270: StopRegiment, which zeroes regimentpaths at 132397 and keeps movementmode
//   3), then SetMovementMode(6) (122286). Fail: SetMovementMode() back to 0 and lastaichargetime
//   stamped (122290-122292).
// - SetMovementMode(3) zeroes combatbehavior and combatbehaviorordered of infantry/cavalry
//   (124770-124774). So the 122214 gate fires once per behavior-3 write: an armed regiment reads
//   behavior 0 in modes 3/6 (the producer remembers who armed it), a refusal does not re-arm
//   (behavior is 0, not 3) and leaves the regiment at behavior 0 until it is re-ordered (the
//   doctrine's revert restores 2), and the volley-before-the-charge branch (122267, needs
//   combatbehavior == 3 while in mode 3) is unreachable for an AI regiment armed through the gate.
// - The assault ends 6 -> 3: target routed (122298-122302) or target reached (122318-122326, old
//   path restored). Mode 3 then stays armed: the 122226 evaluation does not read combatbehavior, and
//   vanilla's intended disarm (4922-4924, movementmode == 3 && stance != 4 -> SetMovementMode())
//   is unreachable behind the dead 4910 compare.
//
// So the doctrine only writes behavior 3 into the 122214 gate on a regiment (never the group: cycle
// 6 re-orders a group whose combatbehaviorordered != 2), adds Whiskey's margins on top, and leaves
// vanilla's odds check as the final word: Decide has no strength input and never writes mode 6.
// A revert in mode 3 is Disarm (behavior 2 plus SetMovementMode(0)), because behavior alone does not
// leave charge mode; a revert in mode 6 defers the disarm until vanilla returns 6 -> 3.

using System;

namespace WhiskeyRealism.Tactical.Battle
{
    public struct ChargeInput
    {
        public bool ConfigOn;
        public bool PlayerOwned;
        public bool IsInfantryRegiment;        // unittyp == 0
        public int GroupAiStance;              // the regiment's group ai_stance
        public bool TargetVisible;
        public bool TargetIsFirstInRange;      // the target is unitrange.enemyinrangereg[0] (122228)
        public float DistanceToTarget;         // from the regiment's parent group position, as at 122257
        public float VanillaMaxChargeRadius;   // GamePrefs.maxchargeradius (53232)
        public float OwnMorale, TargetMorale, OwnFatigue;
        public bool TargetDisordered;
        public bool TargetFlanked;
        public bool LaneBlockedByFriendly;
        public bool Moving;                    // regimentpaths > 0
        public int MovementMode;
        // The producer saw this regiment's charge cycle end since the doctrine armed it: a
        // ChargeCycleEnded transition between ticks, a new refusal stamp lastaichargetime (122292;
        // arm and refusal can both happen between two ticks), or mode 0 without behavior 3.
        public bool ChargeResolved;
        // Producer memory: the executor issued the doctrine's behavior 3 to this regiment and
        // nothing has reverted it. Not combatbehaviorordered, which vanilla zeroes on arming
        // (124770-124774).
        public bool WhiskeyArmed;
        public bool IsLeading;                 // IsLeading over the group's per-regiment target distances
        // Producer flag: the doctrine reverted a regiment it armed while in mode 6 and has not yet
        // disarmed it (PendingDisarmAfter).
        public bool WhiskeyArmedPendingDisarm;
        // Doctrine-clock seconds since the producer saw vanilla refuse a charge on odds (a new
        // lastaichargetime value, 122292); +Infinity when it never has.
        public double SecondsSinceLastAiChargeRefusal;
        // RefusalBackoffSeconds(GamePrefs.timetorenewaichargecheck); below the floor (0 when
        // unset) RearmAfterRefusalBattleSeconds applies.
        public double RefusalBackoffSeconds;
        // permanentlydetached, isrouted, markedforrout or a feuding group: MicroAICheckForCharges'
        // own unit filter (4917, 4922) bars arming and the stance-drop cancel.
        public bool ExcludedByVanillaFilter;
        // GamePrefs.nomicroaichargeifunitisoutflanked (54784, "outflanked or receiving
        // flanking fire") is set and the regiment's own outflanked or firefromflank is
        // non-zero: no new charge.
        public bool OutflankedBarsCharge;
    }

    public enum ChargeAction
    {
        None,
        Charge,
        Revert,
        Disarm,
    }

    public static class ChargeDoctrine
    {
        public const int AssaultStance = 4;
        public const int ChargeBehavior = 3;
        public const int RevertBehavior = 2; // vanilla cycle-6 value for stance >= 1
        public const float MoraleMargin = 0.15f;
        public const float OwnMoraleFloor = 0.5f;
        public const float OwnFatigueCeiling = 0.6f;
        public const int MaxLeadingRegimentsPerGroup = 2;
        public const double RearmAfterRefusalBattleSeconds = 120.0;

        // The refusal back-off: vanilla's intended renew interval
        // (GamePrefs.timetorenewaichargecheck, game hours; battleprefs "a renewed charge
        // order is not given if it was broken up before due to bad conditions", shipped
        // 0.5 h) with RearmAfterRefusalBattleSeconds as the floor. Vanilla's own check
        // (4917) is dead code and compares lastaichargetime < now + renew, which always
        // holds. A non-finite or non-positive renew keeps the floor.
        public static double RefusalBackoffSeconds(float vanillaRenewHours)
        {
            double renew = (double)vanillaRenewHours * 3600.0;
            return renew > RearmAfterRefusalBattleSeconds && !double.IsInfinity(renew) ? renew : RearmAfterRefusalBattleSeconds;
        }

        public const int NormalMovementMode = 0;
        public const int ArmedMovementMode = 3;   // set by the 122214 gate
        public const int AssaultMovementMode = 6; // set after vanilla's odds check passes (122286)

        // Phased on who armed the regiment and its movement mode:
        // - not armed by the doctrine: Charge only at movementmode 0 while moving (the 122214 gate)
        //   with every condition met and the refusal backoff elapsed; mode 3 with the group out of
        //   assault is Disarm (vanilla's dead 4922-4924 cancel); any other mode 3/6 the doctrine
        //   did not arm is vanilla's own (4919's intent: AI regiments charge in assault stance).
        // - armed, any mode: revert on a resolved cycle or a demoted stance (charge denial wins).
        // - armed, mode 6: hold; vanilla's odds check already passed and the assault resolves itself.
        // - armed, mode 0 (courier carrying the order) or 3 (armed, including the stationary volley
        //   pause): hold while the conditions still hold, otherwise revert.
        // - armed, any other mode (retreat, march): revert.
        // A revert is Disarm in mode 3 (behavior 2 alone leaves charge mode armed) and Revert
        // elsewhere; a Revert in mode 6 never aborts the assault, and the producer's
        // WhiskeyArmedPendingDisarm (PendingDisarmAfter) turns it into a Disarm once vanilla
        // returns 6 -> 3. Config off still releases what the doctrine armed.
        // Player-owned units are never touched, not even reverted: the player UI sets 3 itself.
        public static ChargeAction Decide(in ChargeInput input)
        {
            if (input.PlayerOwned || !input.IsInfantryRegiment)
                return ChargeAction.None;

            if (input.WhiskeyArmedPendingDisarm)
                return input.MovementMode == ArmedMovementMode ? ChargeAction.Disarm : ChargeAction.None;

            bool armed = input.WhiskeyArmed;
            if (!input.ConfigOn)
                return armed ? RevertAt(input.MovementMode) : ChargeAction.None;

            if (!armed)
            {
                if (input.ExcludedByVanillaFilter)
                    return ChargeAction.None;
                // Vanilla's intended stance-drop cancel (MicroAICheckForCharges 4922-4924, dead
                // behind 4910): any AI regiment of the group in mode 3 while the group is out of
                // assault. It also catches a charge courier that lands after the doctrine's
                // revert (the 122214 gate fires on the next slow tick).
                if (input.MovementMode == ArmedMovementMode && input.GroupAiStance >= 0 && input.GroupAiStance != AssaultStance)
                    return ChargeAction.Disarm;
                bool atGate = input.MovementMode == NormalMovementMode && input.Moving;
                // Vanilla's own renew check (timetorenewaichargecheck, 4917) is dead code, so a
                // fresh behavior 3 right after a refusal (122290-122292) would re-arm the 122214
                // gate at once.
                double backoff = input.RefusalBackoffSeconds > RearmAfterRefusalBattleSeconds
                    ? input.RefusalBackoffSeconds
                    : RearmAfterRefusalBattleSeconds;
                bool backoffElapsed = input.SecondsSinceLastAiChargeRefusal >= backoff;
                return atGate && backoffElapsed && !input.OutflankedBarsCharge && input.GroupAiStance == AssaultStance && ConditionsHold(in input)
                    ? ChargeAction.Charge
                    : ChargeAction.None;
            }

            if (input.ChargeResolved || input.GroupAiStance != AssaultStance)
                return RevertAt(input.MovementMode);

            switch (input.MovementMode)
            {
                case AssaultMovementMode:
                    return ChargeAction.None;
                case NormalMovementMode:
                case ArmedMovementMode:
                    return ConditionsHold(in input) ? ChargeAction.None : RevertAt(input.MovementMode);
                default:
                    return ChargeAction.Revert;
            }
        }

        // Disarm = behavior 2 plus SetMovementMode(0), mirroring vanilla's own unreachable disarm
        // at 4922-4924 (mode 3 only; mode 6 resolves through 122298-122326).
        private static ChargeAction RevertAt(int movementMode)
        {
            return movementMode == ArmedMovementMode ? ChargeAction.Disarm : ChargeAction.Revert;
        }

        private static bool ConditionsHold(in ChargeInput input)
        {
            if (!input.IsLeading || !input.TargetVisible || !input.TargetIsFirstInRange)
                return false;
            if (!(input.DistanceToTarget < input.VanillaMaxChargeRadius))
                return false;
            bool targetWeaker = input.TargetMorale + MoraleMargin < input.OwnMorale
                || input.TargetDisordered
                || input.TargetFlanked;
            return targetWeaker
                && input.OwnMorale >= OwnMoraleFloor
                && input.OwnFatigue <= OwnFatigueCeiling
                && !input.LaneBlockedByFriendly;
        }

        // A charge cycle ends when the regiment leaves charge modes 3/6 (odds refused 3 -> 0 at
        // 122290, retreat, march) or returns 6 -> 3 after the assault (122302 routed target,
        // 122326 target reached), because mode 3 re-arms rather than ending the cycle.
        public static bool ChargeCycleEnded(int previousMovementMode, int movementMode)
        {
            if (previousMovementMode == AssaultMovementMode)
                return movementMode != AssaultMovementMode;
            return previousMovementMode == ArmedMovementMode
                && movementMode != ArmedMovementMode
                && movementMode != AssaultMovementMode;
        }

        // The producer's per-regiment WhiskeyArmedPendingDisarm after this tick's action: set by a
        // Revert issued in mode 6 (the assault ends 6 -> 3 and mode 3 keeps charging, 122226 ->
        // 122286, although behavior is 2), cleared by the Disarm or once the regiment leaves 3/6.
        public static bool PendingDisarmAfter(bool pending, ChargeAction action, int movementMode)
        {
            if (action == ChargeAction.Disarm)
                return false;
            if (action == ChargeAction.Revert && movementMode == AssaultMovementMode)
                return true;
            return pending && (movementMode == ArmedMovementMode || movementMode == AssaultMovementMode);
        }

        // The leading regiments are the MaxLeadingRegimentsPerGroup closest to their own target
        // (per-regiment distance; ties go to the lower index). Pass +Infinity for a regiment with
        // no target; a non-finite own distance never leads.
        public static bool IsLeading(ReadOnlySpan<float> distancesToTarget, int index)
        {
            if (index < 0 || index >= distancesToTarget.Length)
                return false;
            float own = distancesToTarget[index];
            if (float.IsNaN(own) || float.IsInfinity(own))
                return false;
            int closer = 0;
            for (int i = 0; i < distancesToTarget.Length; i++)
            {
                float other = distancesToTarget[i];
                if (other < own || (other == own && i < index))
                    closer++;
            }
            return closer < MaxLeadingRegimentsPerGroup;
        }
    }
}
