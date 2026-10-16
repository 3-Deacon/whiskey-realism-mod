using System;
using System.Collections.Generic;

namespace WhiskeyRealism.Strategic
{
    public enum CoordinatedOperationIntent { Probe, Attack, Reinforce, Continuation }
    public enum CoordinatedOperationDecision { None, SingleLead, CoordinateAttack, Reinforce, Delay, Recover }
    public enum CoordinatedCommitMode { DirectMovement, WlCurrentOrder, BlockedWlPlayerChain }

    public sealed class CoordinatedOperationOptions
    {
        public float RequiredAttackRatio = 1.25f;
        public float RequiredReinforceRatio = 0.85f;
        public int MaxSupportUnits = 2;
        public float MaxSupportEffectiveStrength = 12500f;
        public bool AllowRemoteTier;
        public bool AllowEmptyTargetPackage;
        public float NearbyRange = 80f;
        public float RemoteRange = 180f;
        public float MinimumReadiness = 0.55f;
        public float MinimumMorale = 0.35f;
        public float MinimumAmmo = 0.25f;
        public float MinimumSupply = 0.35f;

        public static CoordinatedOperationOptions StableDefaults(float desiredStrength)
        {
            return new CoordinatedOperationOptions
            {
                RequiredAttackRatio = 1.25f,
                RequiredReinforceRatio = 0.85f,
                MaxSupportUnits = 2,
                MaxSupportEffectiveStrength = Math.Max(0f, desiredStrength) * 1.25f,
                AllowRemoteTier = false
            };
        }

        /// <summary>
        /// Vanilla's commander-initiative factor on offensive gathering (BC8;
        /// AICampaign.CheckOffensiveMovements 14250 / 14281, applied to the dominance test
        /// 14256 / 14298): the required attack and reinforce ratios × (1 + (1 − initiative) ×
        /// commanderinfluenceinitiativeforoperations), campaignprefs 0.25 ("additional factor
        /// needed for offensive operations if commander initiative is 0"). An unreadable
        /// initiative reads as 0 (most cautious); influence 0 leaves the options unchanged.
        /// Returns this instance.
        /// </summary>
        public CoordinatedOperationOptions ScaledByCommanderInitiative(float initiative01, float influence)
        {
            float initiative = float.IsNaN(initiative01) || float.IsInfinity(initiative01) ? 0f
                : initiative01 < 0f ? 0f : initiative01 > 1f ? 1f : initiative01;
            float weight = float.IsNaN(influence) || float.IsInfinity(influence) || influence < 0f ? 0f : influence;
            float factor = 1f + (1f - initiative) * weight;
            RequiredAttackRatio *= factor;
            RequiredReinforceRatio *= factor;
            return this;
        }

        public static CoordinatedOperationOptions FromDirector(float desiredStrength, DirectorPosture posture)
        {
            var options = StableDefaults(desiredStrength);
            if (posture == null) return options;
            if (posture.Pace == CampaignPace.TooQuiet || posture.Pace == CampaignPace.Stalemated)
            {
                options.RequiredAttackRatio = 1.15f;
                options.RequiredReinforceRatio = 0.75f;
                options.MaxSupportUnits = 3;
                options.MaxSupportEffectiveStrength = Math.Max(0f, desiredStrength) * 1.50f;
                options.AllowRemoteTier = true;
            }
            if (posture.Pace == CampaignPace.Overheated ||
                posture.Pace == CampaignPace.TooFastCollapse ||
                posture.Risk >= CollapseRisk.Critical)
            {
                options.RequiredAttackRatio = 1.40f;
                options.RequiredReinforceRatio = 1.00f;
                options.MaxSupportUnits = 1;
                options.MaxSupportEffectiveStrength = Math.Max(0f, desiredStrength) * 0.75f;
                options.AllowRemoteTier = false;
            }
            return options;
        }
    }

    public sealed class CoordinatedOperationCandidate
    {
        public int StableUnitId;
        public string DisplayUnitKey;
        public int AllianceId;
        public FormationLevel Level;
        public FormationDirective Directive;
        public string AreaKey;
        public string SectorKey;
        public float X;
        public float Z;
        public float CombatAvailability;
        public float ExchangePressure;
        public float LocalFriendlySupport;
        public float LocalEnemyStrength;
        public float Readiness;
        public float Morale;
        public float Ammo;
        public float Supply;
        public float Fatigue;
        public bool OffensiveAllowed;
        public bool DefensiveAllowed;
        public bool TransferDonorAllowed;
        public bool DirectMovementAllowed;
        public bool InheritsFromParent;
        public bool CriticalSector;
        public FrontPosture FrontPosture;
        public bool InOffensiveOperation;
        public bool InDefensiveOperation;
        public bool ConstructingSupplyDepot;
        public CoordinatedCommitMode CommitMode;
    }

    public sealed class CoordinatedOperationInput
    {
        public int AllianceId;
        public bool IsPlayerCic;
        public CoordinatedOperationIntent Intent;
        public string TargetName;
        public string TargetAreaKey;
        public string TargetSectorKey;
        public float TargetX;
        public float TargetZ;
        public float TargetEnemyStrength;
        public int PreferredLeadStableUnitId;
        public CoordinatedOperationOptions Options;
        public List<CoordinatedOperationCandidate> Candidates = new List<CoordinatedOperationCandidate>();
    }

    public sealed class CoordinatedOperationSuppression
    {
        public int StableUnitId;
        public string DisplayUnitKey;
        public string Reason;
    }

    public sealed class CoordinatedOperationOutput
    {
        public CoordinatedOperationDecision Decision;
        public string Reason;
        public int LeadStableUnitId;
        public string LeadDisplayUnitKey;
        public List<int> SupportStableUnitIds = new List<int>();
        public List<string> SupportDisplayUnitKeys = new List<string>();
        public List<CoordinatedOperationSuppression> Suppressed = new List<CoordinatedOperationSuppression>();
        public float PackageEffectiveStrength;
        public float TargetEnemyStrength;
        public float Ratio;
        public string TargetName;

        public string Signature()
        {
            return ((int)Decision) + "|" + LeadStableUnitId + "|" +
                string.Join(",", SupportStableUnitIds) + "|" +
                (Reason ?? "-") + "|" + (TargetName ?? "-") + "|" +
                Math.Round(Ratio, 2);
        }
    }

    public static class CoordinatedOperationPackageLedger
    {
        public static CoordinatedOperationOutput Build(CoordinatedOperationInput input)
        {
            var output = new CoordinatedOperationOutput();
            if (input == null) return NoOp(output, "missing-input");
            output.TargetName = input.TargetName;
            output.TargetEnemyStrength = Math.Max(0f, input.TargetEnemyStrength);
            if (input.IsPlayerCic) return NoOp(output, "player-cic");
            if (input.Candidates == null || input.Candidates.Count == 0) return NoOp(output, "no-candidates");

            var options = input.Options ?? CoordinatedOperationOptions.StableDefaults(output.TargetEnemyStrength);
            var eligible = new List<CoordinatedOperationCandidate>();
            foreach (var c in input.Candidates)
            {
                string reason;
                if (!EligibleLead(c, input, options, out reason))
                {
                    Suppress(output, c, reason);
                    continue;
                }
                eligible.Add(c);
            }
            eligible.Sort(CompareLead);
            if (eligible.Count == 0) return NoOp(output, "no-eligible-lead");

            var lead = input.PreferredLeadStableUnitId > 0
                ? eligible.Find(c => c.StableUnitId == input.PreferredLeadStableUnitId) ?? eligible[0]
                : eligible[0];

            output.LeadStableUnitId = lead.StableUnitId;
            output.LeadDisplayUnitKey = lead.DisplayUnitKey;
            output.PackageEffectiveStrength = Math.Max(0f, lead.CombatAvailability);

            if (output.TargetEnemyStrength <= 0f && !options.AllowEmptyTargetPackage)
                return Finish(output, CoordinatedOperationDecision.SingleLead, "empty-target-single-lead");

            if (Ratio(output.PackageEffectiveStrength, output.TargetEnemyStrength) >= options.RequiredAttackRatio)
            {
                foreach (var c in eligible)
                {
                    if (c.StableUnitId == lead.StableUnitId) continue;
                    string reason;
                    Suppress(output, c, EligibleSupport(c, lead, input, options, out reason) ? "lead-overmatch" : reason);
                }
                return Finish(output, CoordinatedOperationDecision.SingleLead, "lead-overmatch");
            }

            var supports = new List<CoordinatedOperationCandidate>();
            foreach (var c in eligible)
            {
                if (c.StableUnitId == lead.StableUnitId) continue;
                string reason;
                if (!EligibleSupport(c, lead, input, options, out reason))
                {
                    Suppress(output, c, reason);
                    continue;
                }
                supports.Add(c);
            }
            supports.Sort((a, b) =>
            {
                int d = DistanceBucket(a, input).CompareTo(DistanceBucket(b, input));
                if (d != 0) return d;
                return a.StableUnitId.CompareTo(b.StableUnitId);
            });

            float supportEffective = 0f;
            foreach (var s in supports)
            {
                if (output.SupportStableUnitIds.Count >= options.MaxSupportUnits)
                {
                    Suppress(output, s, "support-unit-cap");
                    continue;
                }
                if (supportEffective + Math.Max(0f, s.CombatAvailability) > options.MaxSupportEffectiveStrength)
                {
                    Suppress(output, s, "support-strength-cap");
                    continue;
                }
                if (Ratio(output.PackageEffectiveStrength, output.TargetEnemyStrength) >= options.RequiredAttackRatio &&
                    output.SupportStableUnitIds.Count > 0)
                {
                    Suppress(output, s, "overmatch");
                    continue;
                }
                output.SupportStableUnitIds.Add(s.StableUnitId);
                output.SupportDisplayUnitKeys.Add(s.DisplayUnitKey);
                supportEffective += Math.Max(0f, s.CombatAvailability);
                output.PackageEffectiveStrength += Math.Max(0f, s.CombatAvailability);
            }

            output.Ratio = Ratio(output.PackageEffectiveStrength, output.TargetEnemyStrength);
            if (output.Ratio >= options.RequiredAttackRatio && output.SupportStableUnitIds.Count > 0)
                return Finish(output, CoordinatedOperationDecision.CoordinateAttack, "attack-ratio-passed");
            if (output.Ratio >= options.RequiredReinforceRatio && output.SupportStableUnitIds.Count > 0)
                return Finish(output, CoordinatedOperationDecision.Reinforce, "reinforce-ratio-passed");
            if (lead.Morale < options.MinimumMorale || lead.Readiness < options.MinimumReadiness)
                return Finish(output, CoordinatedOperationDecision.Recover, "lead-health-low");
            // A lone lead commits only at the reinforce ratio; below it vanilla's own
            // dominance test (CheckOffensiveMovements, decompile 14290) would refuse too.
            // Probes (OperationalProbeLedger) are contact-making by design and exempt.
            if (output.SupportStableUnitIds.Count == 0 &&
                (input.Intent == CoordinatedOperationIntent.Probe || output.Ratio >= options.RequiredReinforceRatio))
                return Finish(output, CoordinatedOperationDecision.SingleLead, "single-committable-lead");
            if (output.SupportStableUnitIds.Count == 0)
                return Finish(output, CoordinatedOperationDecision.Delay, "lead-understrength");
            return Finish(output, CoordinatedOperationDecision.Delay, "package-understrength");
        }

        private static bool EligibleLead(CoordinatedOperationCandidate c, CoordinatedOperationInput input, CoordinatedOperationOptions options, out string reason)
        {
            reason = null;
            if (c == null) { reason = "null-candidate"; return false; }
            if (c.AllianceId != input.AllianceId) { reason = "wrong-alliance"; return false; }
            if (c.InheritsFromParent) { reason = "inherits-parent"; return false; }
            if (c.CommitMode == CoordinatedCommitMode.BlockedWlPlayerChain) { reason = "blocked-commit-mode"; return false; }
            if (c.CommitMode == CoordinatedCommitMode.DirectMovement && !c.DirectMovementAllowed) { reason = "direct-movement-blocked"; return false; }
            if (input.Intent == CoordinatedOperationIntent.Reinforce)
            {
                if (!c.DefensiveAllowed) { reason = "defensive-blocked"; return false; }
            }
            else if (!c.OffensiveAllowed) { reason = "offensive-blocked"; return false; }
            if (c.InOffensiveOperation) { reason = "in-offensive-operation"; return false; }
            if (c.InDefensiveOperation) { reason = "in-defensive-operation"; return false; }
            if (c.ConstructingSupplyDepot) { reason = "constructing-supply-depot"; return false; }
            if (c.Directive == FormationDirective.Guard || c.Directive == FormationDirective.Hold ||
                c.Directive == FormationDirective.Recover || c.Directive == FormationDirective.Concede ||
                c.Directive == FormationDirective.Screen) // on a planner recon mission: not this layer's to move
            { reason = "directive-blocked"; return false; }
            if (c.CriticalSector) { reason = "critical-sector"; return false; }
            if (c.Readiness < options.MinimumReadiness) { reason = "low-readiness"; return false; }
            if (c.Morale < options.MinimumMorale) { reason = "low-morale"; return false; }
            if (c.Ammo < options.MinimumAmmo) { reason = "low-ammo"; return false; }
            if (c.Supply < options.MinimumSupply) { reason = "low-supply"; return false; }
            int bucket = DistanceBucket(c, input);
            if (bucket > 1 && !options.AllowRemoteTier) { reason = "remote-tier-blocked"; return false; }
            if (bucket > 2) { reason = "outside-range"; return false; }
            return true;
        }

        private static bool EligibleSupport(CoordinatedOperationCandidate c, CoordinatedOperationCandidate lead, CoordinatedOperationInput input, CoordinatedOperationOptions options, out string reason)
        {
            if (!EligibleLead(c, input, options, out reason)) return false;
            if (input.Intent == CoordinatedOperationIntent.Reinforce && c.StableUnitId != lead.StableUnitId && !c.TransferDonorAllowed)
            { reason = "transfer-donor-blocked"; return false; }
            int bucket = DistanceBucket(c, input);
            if (bucket > 1 && !options.AllowRemoteTier) { reason = "remote-tier-blocked"; return false; }
            if (bucket > 2) { reason = "outside-range"; return false; }
            return true;
        }

        private static int CompareLead(CoordinatedOperationCandidate a, CoordinatedOperationCandidate b)
        {
            int d = b.CombatAvailability.CompareTo(a.CombatAvailability);
            return d != 0 ? d : a.StableUnitId.CompareTo(b.StableUnitId);
        }

        private static int DistanceBucket(CoordinatedOperationCandidate c, CoordinatedOperationInput input)
        {
            float d = Distance(c.X, c.Z, input.TargetX, input.TargetZ);
            var options = input.Options ?? CoordinatedOperationOptions.StableDefaults(input.TargetEnemyStrength);
            if (StringEquals(c.SectorKey, input.TargetSectorKey) && d <= options.RemoteRange) return 0;
            if (StringEquals(c.AreaKey, input.TargetAreaKey) && d <= options.RemoteRange) return 1;
            if (d <= options.NearbyRange) return 1;
            if (d <= options.RemoteRange) return 2;
            return 3;
        }

        private static CoordinatedOperationOutput Finish(CoordinatedOperationOutput output, CoordinatedOperationDecision decision, string reason)
        {
            output.Decision = decision;
            output.Reason = reason;
            output.Ratio = Ratio(output.PackageEffectiveStrength, output.TargetEnemyStrength);
            return output;
        }

        private static CoordinatedOperationOutput NoOp(CoordinatedOperationOutput output, string reason)
        {
            output.Decision = CoordinatedOperationDecision.None;
            output.Reason = reason;
            return output;
        }

        private static void Suppress(CoordinatedOperationOutput output, CoordinatedOperationCandidate c, string reason)
        {
            if (c == null) return;
            output.Suppressed.Add(new CoordinatedOperationSuppression
            {
                StableUnitId = c.StableUnitId,
                DisplayUnitKey = c.DisplayUnitKey,
                Reason = reason
            });
        }

        private static float Ratio(float own, float enemy) => own / Math.Max(1f, enemy);

        private static float Distance(float ax, float az, float bx, float bz)
        {
            float dx = ax - bx;
            float dz = az - bz;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }

        private static bool StringEquals(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }

    // Vanilla AICampaign.CheckOffensiveMovements gates the coordinated package
    // must respect before it commits anything in vanilla's place.
    public enum TempoVerdict { Go, Wait, Error }

    public static class CoordinatedOffensiveVanillaGates
    {
        // Only a real failed roll ends the call (vanilla must not roll again for
        // the same target); a gate error leaves vanilla's call untouched.
        public static bool SuppressVanillaThisCall(TempoVerdict verdict)
        {
            return verdict == TempoVerdict.Wait;
        }

        // Early return at decompile 14200: no own units, timediff <= 0, null or
        // inactive lead, !recruitingzonesupdated (vanilla commits at most one
        // offensive per faction job cycle and clears the flag at 14458), or the
        // lead already in an offensive/defensive operation or building a depot.
        public static bool VanillaWouldEvaluate(int ownUnitCount, float timediff, bool leadPresent, bool leadActive,
            bool recruitingZonesUpdated, bool leadInOffensive, bool leadInDefensive, bool leadBuildingDepot)
        {
            if (ownUnitCount <= 0 || timediff <= 0f) return false;
            if (!leadPresent || !leadActive) return false;
            if (!recruitingZonesUpdated) return false;
            return !leadInOffensive && !leadInDefensive && !leadBuildingDepot;
        }

        // Vanilla num2 for the TARGET area (14229-14230):
        //   enemycampaignunitsstrengthclose * avgtroopmoraleinzone * aiimportanceenemyunits
        //   * enemycampaignunitsstrengthcloseweapon / GetNationalMoraleFactor.
        public static float TargetEnemyWeight(float enemyStrengthClose, float avgTroopMorale, float importanceEnemyUnits,
            float enemyWeapon, float nationalMoraleFactor)
        {
            return enemyStrengthClose * avgTroopMorale * importanceEnemyUnits * enemyWeapon / Math.Max(0.01f, nationalMoraleFactor);
        }

        // Vanilla returns when roll > tval unless debug_permanentaimovement or the
        // scripted 1861 Union push (001, objective 6 open, date > 1861.513) forces it.
        public static bool TempoPasses(float roll, float tempoProbability, bool forced)
        {
            return forced || roll <= tempoProbability;
        }
    }
}
