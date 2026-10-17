using WhiskeyRealism.Tactical.Operations;

namespace WhiskeyRealism.Tactical.Orchestrator
{
    /// <summary>
    /// Pre-resolved action for a vanilla assault (ai_stanceordered 4), read by
    /// the battle-core ChargeDemotionIntentProducer. Doctrine + ledger +
    /// reaction resolution runs at the heavy-gate-fire boundary; the producer
    /// reads this enum without re-walking the orchestrator state.
    ///
    /// Values:
    ///   None             — no charge answer (ledger / scorer entries). A
    ///                       vanilla assault (4) is preserved.
    ///   PreserveVanilla  — TacticalChargeDenial keeps the assault (flag off,
    ///                       doctrine Allow, or no denial raised). No intent.
    ///   DemoteToHold     — TacticalChargeDenial demotes the assault; the
    ///                       producer submits a Safety Stance intent to
    ///                       FinalStance (doctrine Deny: 2, legacy DenyCharge:
    ///                       3). Applies whenever vanilla is at 4, including a
    ///                       doctrine entry vanilla escalated after caching.
    /// </summary>
    internal enum ChargeOverrideAction
    {
        None = 0,
        PreserveVanilla = 1,
        DemoteToHold = 2
    }

    /// <summary>
    /// Per-group resolved decision cached at the heavy-gate-fire boundary
    /// and consumed by the battle-core GroupStanceIntentProducer and
    /// ChargeDemotionIntentProducer. Bundles every doctrine/ledger/charge
    /// answer they need so each arbiter tick is O(1) per group — no resolver
    /// walks, no sector rebuild, no <c>DecideGroupStance</c> recompute.
    ///
    /// Construction happens on the main thread inside
    /// <c>TacticalBattleCoordinatorRuntime.PopulateResolvedDecisionsForSide</c>
    /// after the heavy gate fires. The cache is reset on
    /// <c>ResetRuntimeTickState</c> (battle end) so stale entries never
    /// leak between battles.
    ///
    /// Per-tick safety gates that depend on live state (player W&amp;L
    /// control, order-friction, fresh vanilla writes, config-flag changes)
    /// stay on the apply path — they're cheap and the cached final-stance
    /// answer assumes they pass.
    /// </summary>
    internal readonly struct TacticalResolvedGroupDecision
    {
        public TacticalResolvedGroupDecision(
            int finalStance,
            TacticalDoctrineDecisionKind kind,
            string reason,
            bool fromDoctrine,
            bool fromLedger,
            bool isChargeOverride,
            ChargeOverrideAction chargeAction)
        {
            FinalStance = finalStance;
            Kind = kind;
            Reason = string.IsNullOrEmpty(reason) ? "unknown" : reason;
            FromDoctrine = fromDoctrine;
            FromLedger = fromLedger;
            IsChargeOverride = isChargeOverride;
            ChargeAction = chargeAction;
        }

        /// <summary>
        /// BC14W: #45's cached answer for a group the works hold guard holds, from its doctrine order (<paramref name="fromDoctrine"/>) or its ledger
        /// state. The posture executor owns the stance of such a group (stance 2, <see cref="WhiskeyRealism.Tactical.Battle.IntentFlags.WorksHold"/>), so
        /// #45 writes none: the entry is a Skip whose only content is the charge demotion, the explicit denial of a vanilla assault (4) that the doctrine
        /// Deny of a hold makes (to stance 2), when charge denial is on.
        /// </summary>
        public static TacticalResolvedGroupDecision ForWorksHold(int vanillaOrdered, bool chargeDenialOn, bool orchestratorArmyOn, bool fromDoctrine)
        {
            ChargeDenialDecision ifAssault = TacticalChargeDenial.DecideForVanillaAssault(new ChargeDenialInput(
                chargeDenialOn, orchestratorArmyOn, DoctrineConsumerAction.Deny, false));
            return new TacticalResolvedGroupDecision(
                finalStance: ifAssault.DemoteStance,
                kind: TacticalDoctrineDecisionKind.Skip,
                reason: "works-hold",
                fromDoctrine: fromDoctrine,
                fromLedger: !fromDoctrine,
                isChargeOverride: vanillaOrdered == TacticalCommandTaskToStanceMapper.StanceAssault,
                chargeAction: ifAssault.Demotes ? ChargeOverrideAction.DemoteToHold : ChargeOverrideAction.PreserveVanilla);
        }

        /// <summary>
        /// Final stance to write at apply time (0..3), or -1 when the
        /// resolved decision is "no override" (e.g. doctrine + ledger both
        /// produced Observe and the default scorer returned Skip). When
        /// vanilla is at assault (4) at apply time, the apply path consults
        /// ChargeAction and FinalStance is the demotion stance.
        /// </summary>
        public int FinalStance { get; }

        /// <summary>
        /// Apply vs. Skip semantics — Apply means the patch should write
        /// FinalStance (subject to per-tick safety gates), Skip means the
        /// patch should leave vanilla alone.
        /// </summary>
        public TacticalDoctrineDecisionKind Kind { get; }

        /// <summary>Telemetry-only reason string carried from the originating resolver.</summary>
        public string Reason { get; }

        /// <summary>True if the doctrine resolver produced this decision.</summary>
        public bool FromDoctrine { get; }

        /// <summary>True if the operations-ledger resolver produced this decision.</summary>
        public bool FromLedger { get; }

        /// <summary>True if vanillaOrdered was 4 (charge) at cache time; skips the apply fast path.</summary>
        public bool IsChargeOverride { get; }

        /// <summary>What to do with a vanilla assault (4) found at apply time.</summary>
        public ChargeOverrideAction ChargeAction { get; }
    }
}
