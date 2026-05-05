using System;
using System.Collections.Generic;

namespace WhiskeyRealism.Strategic
{
    /// <summary>
    /// Pure static builder that consumes a <see cref="DefenseIntentInput"/> snapshot and
    /// emits a <see cref="DefenseIntentLedgerOutput"/> with ranked responses for every active
    /// threat source and every unguarded coastal/river asset.  No I/O, no Unity.
    /// Side effects: (1) writes counter state on the supplied
    /// <see cref="DefenseCooldownTable"/>, (2) populates
    /// <see cref="DefenseCandidate.EffectiveStrength"/> and
    /// <see cref="DefenseCandidate.Score"/> on each candidate during scoring.
    /// </summary>
    public static class DefenseIntentLedger
    {
        // Fixed desired-strength for CoastalGuard (no live threat; approximates
        // aiminimumstrengthformovement * 1.5 ≈ 3000).
        private const float CoastalGuardDesired = 3000f;

        // Minimum desired strength for a Raid-scale response.
        private const float RaidMinDesired = 1500f;

        // ---------------------------------------------------------------------------
        // Public entry point
        // ---------------------------------------------------------------------------

        public static DefenseIntentLedgerOutput Build(DefenseIntentInput input)
        {
            var output = new DefenseIntentLedgerOutput();
            if (input == null) return output;

            output.AllianceId = input.AllianceId;

            // 1. Short-circuit for player-controlled alliances.
            if (input.PlayerIsCIC) return output;

            float caution    = input.CICPersonality.Caution;
            float aggression = input.CICPersonality.Aggression;

            // Track which unit ids have been assigned (consumed) across all responses so
            // that the guard-budget cap and global pool depletion work correctly.
            var assignedUnitIds = new HashSet<int>();

            // Track which asset names are already covered by a threat-source response so
            // we don't double-emit a guard response for the same asset.
            var coveredAssets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Track cumulative effective strength already committed to guard responses
            // for budget-cap enforcement.
            float guardCumulativeEffective = 0f;
            float guardBudget = Math.Max(0f, input.TotalAllianceEffectiveStrength * input.GuardBudgetFraction);

            // 2. Threat-source responses.
            foreach (var src in input.Threats)
            {
                if (src == null) continue;

                string sig = BuildSignature(src);
                DefensePosture posture = DerivePosture(src);
                ThreatScale scale = DeriveScale(src);
                float desired = DeriveDesiredStrength(src, scale);

                // Track asset name so we skip the guard pass for it.
                if (!string.IsNullOrEmpty(src.AssetName))
                    coveredAssets.Add(src.AssetName);

                // Cooldown bookkeeping for active / recovered postures.
                if (posture == DefensePosture.Recovered)
                {
                    // Call MarkRecovered only on the transition from Active state
                    // (i.e. when the cooldown was set by MarkActive, not yet by MarkRecovered).
                    // We identify this by checking whether the cooldown is currently active
                    // (was set via MarkActive) but we haven't yet recorded a Recovered marker.
                    // Implementation: always call MarkRecovered on first Recovered encounter;
                    // subsequent ticks with the same sig leave the counter alone until it
                    // expires.  The caller ticks the table externally between builds.
                    if (!input.Cooldown.IsActive(sig))
                    {
                        // Cooldown already expired — emit an empty Recovered response so the
                        // runtime can release any prior assignment via the threat signature.
                        var emptyResponse = BuildEmptyRecoveredResponse(sig, posture, scale, src, desired);
                        output.Responses.Add(emptyResponse);
                        continue;
                    }
                    // Cooldown is still active from a previous MarkActive call.
                    // Mark the recovered start (resets counter to CooldownDays) only once.
                    // We use a sentinel: if IsActive and the sig was previously MarkActive'd,
                    // calling MarkRecovered resets the clock to CooldownDays.
                    input.Cooldown.MarkRecovered(sig, input.CooldownDays);

                    var recoveredResponse = BuildEmptyRecoveredResponse(sig, posture, scale, src, desired);
                    recoveredResponse.Adequate = true; // hold the slot
                    output.Responses.Add(recoveredResponse);
                    continue;
                }
                else if (posture == DefensePosture.ActiveInvasion ||
                         posture == DefensePosture.InvasionWatch  ||
                         posture == DefensePosture.ContainAndCounterattack)
                {
                    input.Cooldown.MarkActive(sig, input.CooldownDays);
                }

                // Pre-suppression + aggregator pass.
                var response = BuildThreatResponse(
                    input, src, sig, posture, scale, desired,
                    caution, aggression, assignedUnitIds);

                // Mark units selected in this response as consumed.
                foreach (var c in response.SelectedPackage)
                    assignedUnitIds.Add(c.UnitInstanceId);

                output.Responses.Add(response);
            }

            // 3. Guard pass for assets not covered by a threat source.
            foreach (var asset in input.GuardCandidateAssets)
            {
                if (asset == null) continue;
                if (!string.IsNullOrEmpty(asset.Name) && coveredAssets.Contains(asset.Name)) continue;

                string assetSig = $"guard:{asset.Kind}:{(asset.Name ?? "<no-name>")}";

                // Pre-filter candidates: only Local/SameTheater; exclude cross-map.
                var guardPool = FilterGuardCandidates(input.Candidates, assignedUnitIds,
                    postureCap: CandidateTier.SameTheater);

                if (guardBudget <= 0f || guardCumulativeEffective >= guardBudget)
                {
                    // Budget exhausted before we even score — emit cap-reached for all local candidates.
                    var capResponse = BuildCapReachedResponse(assetSig, asset, guardPool, CoastalGuardDesired);
                    output.Responses.Add(capResponse);
                    continue;
                }

                // Run aggregator with remaining budget headroom.
                var pkgResult = DefensePackageAggregator.Select(guardPool, CoastalGuardDesired, caution, aggression);

                // Check whether adding this package would exceed the budget.
                if (guardCumulativeEffective + pkgResult.CumulativeEffective > guardBudget &&
                    pkgResult.SelectedPackage.Count > 0)
                {
                    // Over budget — emit cap-reached for all candidates that would have been picked.
                    var capResponse = BuildCapReachedResponse(assetSig, asset, pkgResult.SelectedPackage, CoastalGuardDesired);
                    // Also carry the aggregator's existing suppressed list.
                    foreach (var s in pkgResult.Suppressed)
                        capResponse.Suppressed.Add(s);
                    output.Responses.Add(capResponse);
                    continue;
                }

                var guardResponse = new DefenseResponse
                {
                    Threat = new DefenseThreat
                    {
                        Signature = assetSig,
                        Posture   = DefensePosture.CoastalGuard,
                        Scale     = ThreatScale.None,
                        AssetName = asset.Name,
                        X         = asset.X,
                        Z         = asset.Z,
                        DesiredStrength = CoastalGuardDesired,
                        ResponseRadius  = CandidateTier.SameTheater
                    },
                    Adequate     = pkgResult.Adequate,
                    Understrength = pkgResult.Understrength
                };

                foreach (var c in pkgResult.SelectedPackage)
                {
                    guardResponse.SelectedPackage.Add(c);
                    assignedUnitIds.Add(c.UnitInstanceId);
                }
                foreach (var s in pkgResult.Suppressed)
                    guardResponse.Suppressed.Add(s);

                // Also suppress cross-map candidates that were pre-filtered (for test transparency).
                foreach (var c in input.Candidates)
                {
                    if (c == null || assignedUnitIds.Contains(c.UnitInstanceId)) continue;
                    if (c.Tier == CandidateTier.CrossMap)
                    {
                        guardResponse.Suppressed.Add(new DefenseSuppression
                        {
                            UnitInstanceId = c.UnitInstanceId,
                            Reason = "forbidden-cross-map"
                        });
                    }
                }

                guardResponse.TelemetrySignature =
                    $"{DefensePosture.CoastalGuard}|{assetSig}" +
                    $"|{(int)(CoastalGuardDesired / 1000)}b" +
                    $"|{guardResponse.SelectedPackage.Count}u|{CandidateTier.SameTheater}";

                guardCumulativeEffective += pkgResult.CumulativeEffective;
                output.Responses.Add(guardResponse);
            }

            // 4. Build stable output signature.
            output.Signature = BuildOutputSignature(output.Responses);

            return output;
        }

        // ---------------------------------------------------------------------------
        // Private helpers
        // ---------------------------------------------------------------------------

        private static string BuildSignature(DefenseThreatSource src)
        {
            switch (src.Kind)
            {
                case DefenseThreatSourceKind.SeaInvasion:
                    return DefenseThreatSignature.ForSeaInvasion(
                        src.InvasionForceInstanceId, src.SpotName, src.SourcePortName);
                case DefenseThreatSourceKind.RaidForce:
                    return DefenseThreatSignature.ForRaid(
                        src.RaidGroupInstanceId, src.AssetName);
                case DefenseThreatSourceKind.AssetProximity:
                    return DefenseThreatSignature.ForAsset(
                        src.AssetKind, src.AssetName, src.EnemyInstanceIds, topN: 3);
                default:
                    return $"unknown:{src.Kind}";
            }
        }

        private static DefensePosture DerivePosture(DefenseThreatSource src)
        {
            switch (src.Kind)
            {
                case DefenseThreatSourceKind.SeaInvasion:
                    if (src.VanillaCollapsed) return DefensePosture.Recovered;
                    return src.LandedSignal
                        ? DefensePosture.ActiveInvasion
                        : DefensePosture.InvasionWatch;

                case DefenseThreatSourceKind.RaidForce:
                    return src.RaidCurrentState >= 5
                        ? DefensePosture.Recovered
                        : DefensePosture.ActiveInvasion;

                case DefenseThreatSourceKind.AssetProximity:
                    return DefensePosture.ActiveInvasion;

                default:
                    return DefensePosture.NotEvaluated;
            }
        }

        private static ThreatScale DeriveScale(DefenseThreatSource src)
        {
            // RaidForce is always Raid regardless of strength.
            if (src.Kind == DefenseThreatSourceKind.RaidForce)
                return ThreatScale.Raid;

            float str = src.EnemyStrength;
            if (str < 3000f) return ThreatScale.Raid;
            if (str < 8000f) return ThreatScale.Landing;

            // >= 8000: decisive only when asset has high-value role.
            const AssetStrategicRole decisiveRoles =
                AssetStrategicRole.CapitalApproach |
                AssetStrategicRole.KeyFort          |
                AssetStrategicRole.BlockadeRunnerPort |
                AssetStrategicRole.UnionForwardBase;

            return (src.AssetRole & decisiveRoles) != 0
                ? ThreatScale.DecisiveLanding
                : ThreatScale.MajorLanding;
        }

        private static float DeriveDesiredStrength(DefenseThreatSource src, ThreatScale scale)
        {
            switch (scale)
            {
                case ThreatScale.Raid:
                    return Math.Max(RaidMinDesired, src.EnemyStrength * 1.0f);
                case ThreatScale.Landing:
                    return src.EnemyStrength * 1.5f;
                case ThreatScale.MajorLanding:
                    return src.EnemyStrength * 2.0f;
                case ThreatScale.DecisiveLanding:
                    return src.EnemyStrength * 2.25f;
                default:
                    return RaidMinDesired;
            }
        }

        private static DefenseResponse BuildThreatResponse(
            DefenseIntentInput input,
            DefenseThreatSource src,
            string sig,
            DefensePosture posture,
            ThreatScale scale,
            float desired,
            float caution,
            float aggression,
            HashSet<int> assignedUnitIds)
        {
            // Partition candidates into normal pool and critical-front candidates.
            var normalPool    = new List<DefenseCandidate>();
            var criticalFront = new List<DefenseCandidate>();
            var preSuppressed = new List<DefenseSuppression>();

            foreach (var c in input.Candidates)
            {
                if (c == null) continue;
                if (assignedUnitIds.Contains(c.UnitInstanceId)) continue;

                // Pre-suppression: player-controlled.
                if (c.PlayerControlled)
                {
                    preSuppressed.Add(new DefenseSuppression
                    {
                        UnitInstanceId = c.UnitInstanceId,
                        Reason = "player-controlled"
                    });
                    continue;
                }

                // Pre-suppression: cross-map forbidden for guard or raid scale.
                if (c.Tier == CandidateTier.CrossMap &&
                    (posture == DefensePosture.CoastalGuard || scale == ThreatScale.Raid))
                {
                    preSuppressed.Add(new DefenseSuppression
                    {
                        UnitInstanceId = c.UnitInstanceId,
                        Reason = "forbidden-cross-map"
                    });
                    continue;
                }

                // Pre-suppression: critical-front when scale < decisive.
                if (c.CriticalFront && scale != ThreatScale.DecisiveLanding)
                {
                    // Defer to critical-front list; suppress now, re-evaluate later.
                    criticalFront.Add(c);
                    continue;
                }

                normalPool.Add(c);
            }

            // Run aggregator without critical-front candidates first.
            var pkgResult = DefensePackageAggregator.Select(normalPool, desired, caution, aggression);

            // Escalation: for decisive landing, check if we need cross-theater candidates.
            // The aggregator already handles AdjacentTheater via tier scoring; we just note
            // the escalation reason when an adjacent-theater unit is in the package.
            string escalationReason = null;
            if (scale == ThreatScale.DecisiveLanding)
            {
                foreach (var c in pkgResult.SelectedPackage)
                {
                    if (c.Tier == CandidateTier.AdjacentTheater || c.Tier == CandidateTier.CrossMap)
                    {
                        escalationReason = "cross-theater";
                        break;
                    }
                }
            }

            // Handle critical-front candidates: suppress normally unless decisive + no
            // adequate package without them.
            bool needsCritical = scale == ThreatScale.DecisiveLanding && !pkgResult.Adequate;
            foreach (var cf in criticalFront)
            {
                if (needsCritical)
                {
                    // Add to normal pool and re-run aggregator.
                    normalPool.Add(cf);
                    // We don't add a suppression record for this candidate.
                }
                else
                {
                    preSuppressed.Add(new DefenseSuppression
                    {
                        UnitInstanceId = cf.UnitInstanceId,
                        Reason = "critical-front"
                    });
                }
            }

            // Re-run aggregator if any critical-front candidates were admitted.
            if (needsCritical && criticalFront.Count > 0)
            {
                pkgResult = DefensePackageAggregator.Select(normalPool, desired, caution, aggression);
                // Re-check escalation reason.
                foreach (var c in pkgResult.SelectedPackage)
                {
                    if (c.Tier == CandidateTier.AdjacentTheater || c.Tier == CandidateTier.CrossMap)
                    {
                        escalationReason = "cross-theater";
                        break;
                    }
                }
            }

            // Determine response radius.
            CandidateTier radius = CandidateTier.Local;
            foreach (var c in pkgResult.SelectedPackage)
                if (c.Tier > radius) radius = c.Tier;

            var response = new DefenseResponse
            {
                Threat = new DefenseThreat
                {
                    Signature       = sig,
                    Posture         = posture,
                    Scale           = scale,
                    AssetName       = src.AssetName,
                    X               = src.X,
                    Z               = src.Z,
                    EnemyStrength   = src.EnemyStrength,
                    DesiredStrength = desired,
                    ResponseRadius  = radius,
                    EscalationReason = escalationReason
                },
                Adequate     = pkgResult.Adequate,
                Understrength = pkgResult.Understrength
            };

            // Merge pre-suppressions first, then aggregator suppressions.
            foreach (var s in preSuppressed)
                response.Suppressed.Add(s);
            foreach (var s in pkgResult.Suppressed)
                response.Suppressed.Add(s);

            foreach (var c in pkgResult.SelectedPackage)
                response.SelectedPackage.Add(c);

            response.TelemetrySignature =
                $"{response.Threat.Posture}|{response.Threat.Signature}" +
                $"|{(int)(response.Threat.DesiredStrength / 1000)}b" +
                $"|{response.SelectedPackage.Count}u|{response.Threat.ResponseRadius}";

            return response;
        }

        private static DefenseResponse BuildEmptyRecoveredResponse(
            string sig, DefensePosture posture, ThreatScale scale,
            DefenseThreatSource src, float desired)
        {
            var r = new DefenseResponse
            {
                Threat = new DefenseThreat
                {
                    Signature       = sig,
                    Posture         = posture,
                    Scale           = scale,
                    AssetName       = src.AssetName,
                    X               = src.X,
                    Z               = src.Z,
                    EnemyStrength   = src.EnemyStrength,
                    DesiredStrength = desired
                }
            };
            r.TelemetrySignature = $"{posture}|{sig}|0b|0u|none";
            return r;
        }

        private static List<DefenseCandidate> FilterGuardCandidates(
            List<DefenseCandidate> all, HashSet<int> assignedIds, CandidateTier postureCap)
        {
            var result = new List<DefenseCandidate>();
            foreach (var c in all)
            {
                if (c == null) continue;
                if (assignedIds.Contains(c.UnitInstanceId)) continue;
                if (c.PlayerControlled) continue;
                if (c.Tier > postureCap) continue; // Cross-map excluded for guard
                result.Add(c);
            }
            return result;
        }

        private static DefenseResponse BuildCapReachedResponse(
            string assetSig, CampaignMapAsset asset,
            IEnumerable<DefenseCandidate> capCandidates,
            float desired)
        {
            var response = new DefenseResponse
            {
                Threat = new DefenseThreat
                {
                    Signature       = assetSig,
                    Posture         = DefensePosture.CoastalGuard,
                    Scale           = ThreatScale.None,
                    AssetName       = asset.Name,
                    X               = asset.X,
                    Z               = asset.Z,
                    DesiredStrength = desired,
                    ResponseRadius  = CandidateTier.SameTheater
                },
                Adequate     = false,
                Understrength = true
            };

            foreach (var c in capCandidates)
            {
                if (c == null) continue;
                response.Suppressed.Add(new DefenseSuppression
                {
                    UnitInstanceId = c.UnitInstanceId,
                    Reason = "cap-reached"
                });
            }

            response.TelemetrySignature = $"{DefensePosture.CoastalGuard}|{assetSig}|0b|0u|none";

            return response;
        }

        private static string BuildOutputSignature(List<DefenseResponse> responses)
        {
            if (responses == null || responses.Count == 0) return string.Empty;

            var parts = new List<string>(responses.Count);
            foreach (var r in responses)
            {
                if (r?.Threat == null) continue;
                parts.Add($"{r.Threat.Signature}:{r.SelectedPackage.Count}:{r.Threat.Posture}");
            }

            parts.Sort(StringComparer.Ordinal);
            return string.Join("|", parts);
        }
    }
}
