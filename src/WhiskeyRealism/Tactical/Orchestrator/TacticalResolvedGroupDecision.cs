namespace WhiskeyRealism.Tactical.Orchestrator
{
    /// <summary>
    /// Pre-resolved action for the vanillaOrdered==4 (charge) branch in
    /// <c>BattleGroupStancePatch</c>. The "cheap per-group" refactor moves
    /// doctrine + ledger + reaction resolution to the heavy-gate-fire
    /// boundary; at apply time the patch reads this enum and dispatches
    /// without re-walking the orchestrator state.
    ///
    /// Values:
    ///   None             — no charge answer (ledger / scorer entries). A
    ///                       vanilla assault (4) at apply time is preserved.
    ///   PreserveVanilla  — TacticalChargeDenial keeps the assault (flag off,
    ///                       doctrine Allow, or no denial raised). Patch logs
    ///                       the reason and returns without writing.
    ///   DemoteToHold     — TacticalChargeDenial demotes the assault; the patch
    ///                       writes FinalStance (doctrine Deny: 2, legacy
    ///                       DenyCharge: 3) via <c>DemoteCharge</c>. Applies
    ///                       whenever vanilla is at 4 at apply time, including
    ///                       a doctrine entry vanilla escalated after caching.
    ///   DenyExplicit     — reserved for future denial reasons that don't
    ///                       demote; currently treated as PreserveVanilla at
    ///                       apply time.
    /// </summary>
    internal enum ChargeOverrideAction
    {
        None = 0,
        PreserveVanilla = 1,
        DemoteToHold = 2,
        DenyExplicit = 3
    }

    /// <summary>
    /// Per-group resolved decision cached at the heavy-gate-fire boundary
    /// and consumed by <c>BattleGroupStancePatch.ApplyResolvedDecisionFast</c>.
    /// Bundles every doctrine/ledger/charge answer the patch needs so the
    /// per-tick apply path becomes O(1) per group — no resolver walks, no
    /// sector rebuild, no <c>DecideGroupStance</c> recompute.
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
