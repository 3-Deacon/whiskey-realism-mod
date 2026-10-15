using System;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    /// <summary>
    /// Pure, cheap, stable, testable DTO for tactical battle state change detection.
    /// Lives in Orchestrator/ as the gate input for deciding heavy (evidence + vision + doctrine) vs frequent cheap path.
    ///
    /// Pure contract: only System.* types. No UnityEngine, no vanilla (AIBattle, Regiment, GameVars, BattleUnits, etc.).
    /// The runtime adapter (TacticalBattleSnapshotBuilder + partial in TacticalBattleCoordinatorRuntime) supplies the values
    /// from reflection once per allowed heavy tick.
    ///
    /// Fields (reconciled from design spec §5.2 and plan Task 2 authoritative list):
    /// - ActiveUnitCount: total active units across sides (coarse total)
    /// - Side0/1ActiveForce: side totals for active force strength (cheap diff without full vision)
    /// - Side0/1MacroAI: macro AI mode/state per side (vanilla macroai enum int)
    /// - AnySideInRetreatOrEOD: any side in end-of-day or retreat cycle (affects planning)
    /// - MajorObjectiveAnchorHash: coarse hash of top 1-2 objective names/ids (cheap, no heavy vision)
    /// - AnyInterruptedPathsOrNewContact: lightweight flag for path interrupts or fresh contacts
    /// - BattleHourBucket: coarse bucket of battle time (GameVars.currenttimefromstart hours) for signature stability across ticks
    ///
    /// SignatureEquals follows DirectChildEvidence pattern exactly (src/.../DirectChildContracts.cs:89):
    ///   compares only heavy-planning-relevant fields for change detection.
    ///   BattleHourBucket is EXCLUDED from SignatureEquals (it is a time-bucket for gate stability, like Confidence01;
    ///   the min/max time gate in TacticalHeavyPathGate handles time-based refresh separately).
    ///   This keeps the signature focused on content changes that warrant re-running expensive planners.
    ///
    /// Computed before any heavy reflection/vision in the frequent tick path.
    /// </summary>
    public readonly struct TacticalBattleStateSignature
    {
        public TacticalBattleStateSignature(
            int activeUnitCount,
            int side0ActiveForce,
            int side1ActiveForce,
            int side0MacroAI,
            int side1MacroAI,
            bool anySideInRetreatOrEOD,
            int majorObjectiveAnchorHash,
            int interruptedContactBucket,
            int battleHourBucket)
        {
            ActiveUnitCount = activeUnitCount < 0 ? 0 : activeUnitCount;
            Side0ActiveForce = side0ActiveForce < 0 ? 0 : side0ActiveForce;
            Side1ActiveForce = side1ActiveForce < 0 ? 0 : side1ActiveForce;
            Side0MacroAI = side0MacroAI;
            Side1MacroAI = side1MacroAI;
            AnySideInRetreatOrEOD = anySideInRetreatOrEOD;
            MajorObjectiveAnchorHash = majorObjectiveAnchorHash;
            InterruptedContactBucket = interruptedContactBucket < 0 ? 0 : interruptedContactBucket;
            BattleHourBucket = battleHourBucket;
        }

        public int ActiveUnitCount { get; }
        public int Side0ActiveForce { get; }
        public int Side1ActiveForce { get; }
        public int Side0MacroAI { get; }
        public int Side1MacroAI { get; }
        public bool AnySideInRetreatOrEOD { get; }
        public int MajorObjectiveAnchorHash { get; }
        // Bucket of (count of units with pathinterrupted OR groupenemiesinrange>0) from
        // a bounded scan. Replaces the prior volatile bool (2026-05-22) — the bool
        // flipped every cycle once contact was made because groupenemiesinrange
        // oscillates as units enter/exit fire range, causing heavy-gate churn.
        // Bucket changes only on meaningful contact-density shifts.
        // Bucket ranges: 0 = 0 contacts, 1 = 1-3, 2 = 4-10, 3 = 11+.
        public int InterruptedContactBucket { get; }
        public int BattleHourBucket { get; }

        public bool SignatureEquals(TacticalBattleStateSignature other)
        {
            // Compare significant heavy-planning-relevant fields only.
            // Follows DirectChildEvidence.SignatureEquals exactly (see DirectChildContracts.cs:89-96).
            // BattleHourBucket is excluded (coarse time bucket for signature stability across frequent ticks;
            // the separate minInterval/maxInterval + pending logic in TacticalHeavyPathGate handles time-based
            // refresh and starvation prevention; bucket change alone does not indicate content change warranting
            // re-execution of ArmyEvidenceBuilder + vision + doctrine planners).
            return ActiveUnitCount == other.ActiveUnitCount
                && Side0ActiveForce == other.Side0ActiveForce
                && Side1ActiveForce == other.Side1ActiveForce
                && Side0MacroAI == other.Side0MacroAI
                && Side1MacroAI == other.Side1MacroAI
                && AnySideInRetreatOrEOD == other.AnySideInRetreatOrEOD
                && MajorObjectiveAnchorHash == other.MajorObjectiveAnchorHash
                && InterruptedContactBucket == other.InterruptedContactBucket;
        }
    }
}
