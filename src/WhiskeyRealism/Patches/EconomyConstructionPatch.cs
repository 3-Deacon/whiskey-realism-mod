using System;
using System.Collections.Generic;
using System.Globalization;
using HarmonyLib;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Strategic.Construction;
using WhiskeyRealism.Strategic.Fiscal;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Vanilla AICampaign.UpdateCompanyFoundations (decompile 15000-15080) picks, per
    // subsidy type, the highest alliance.bestiipplacesprob candidate (15016-15036), then
    // among non-subsidy candidates the highest prob*num3 and builds it when
    // Random < prob*timediff*50 (15056). UpdateCompanyFoundationList (15082; 15253-15261,
    // driven from AICampaign.Update 11200) replaces a slot only when a new site's raw prob
    // beats the STORED value.
    //
    // This Prefix biases valid candidates for vanilla's selection only (rolled group is
    // capped at vanilla's own best: no build-rate inflation; each subsidy type keeps its
    // raw max so the un-reset num threshold between the loops stays vanilla's) and the
    // Finalizer restores the raw values for every slot vanilla did
    // not consume, so stale inflated probabilities never block better sites.
    [HarmonyPatch(typeof(AICampaign), "UpdateCompanyFoundations")]
    internal static class EconomyConstructionPatch
    {
        private static readonly List<PendingRestore> _pending = new List<PendingRestore>();
        private static int _pendingAlliance = -1;

        [HarmonyPrefix]
        internal static void Prefix(int alliancerunthrough)
        {
            using (TelemetryPerf.Scope("campaign.patch.economy-construction.prefix", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            try
            {
                _pending.Clear();
                _pendingAlliance = -1;

                int alliance = alliancerunthrough;
                if (Plugin.Instance == null || !Plugin.Instance.Enabled.Value) return;
                if (StrategicCoordinator.Instance == null) return;
                if (alliance < 0 || alliance >= 2) return;
                if (GameVars.frame <= 50) return;
                if (GameVars.alliance == null || alliance >= GameVars.alliance.Length) return;
                if (GameVars.buildingtypes == null || GameVars.buildingtypes.Count <= 0) return;

                var fiscalIntents = StrategicCoordinator.Instance.FiscalIntents;
                if (fiscalIntents == null || alliance >= fiscalIntents.Length) return;
                var intent = fiscalIntents[alliance];
                if (intent == null) return;

                var state = GameVars.alliance[alliance];
                if (state == null || state.bestiipplaces == null || state.bestiipplacesprob == null) return;

                OnceLog.Info("economy-construction", "EconomyConstructionPatch wired (Prefix/Finalizer on UpdateCompanyFoundations)");

                int slots = state.bestiipplacesprob.Length;
                int count = Math.Min(state.bestiipplaces.Length, slots);
                count = Math.Min(count, GameVars.buildingtypes.Count);

                var raw = new float[slots];
                var mult = new float[slots];
                var groups = new int[slots];
                var reasons = new string[slots];

                for (int buildingType = 0; buildingType < slots; buildingType++)
                {
                    mult[buildingType] = 1f;
                    groups[buildingType] = ConstructionProbabilityBias.NotCandidate;
                    float oldProb = state.bestiipplacesprob[buildingType];
                    var oldStatus = ConstructionProbabilitySanitizer.Classify(oldProb);
                    if (oldStatus == ConstructionProbabilityStatus.Invalid)
                    {
                        state.bestiipplacesprob[buildingType] = 0f;
                        OnceLog.Warning("economy-construction:invalid-probability", "[Patch:EconomyConstruction] invalid bestiipplaces probability sanitized to 0");
                        continue;
                    }
                    raw[buildingType] = oldProb;
                    if (oldStatus == ConstructionProbabilityStatus.Skip) continue;
                    if (buildingType >= count) continue;

                    var place = state.bestiipplaces[buildingType];
                    if (place == null) continue;

                    var type = GameVars.buildingtypes[buildingType];
                    // Mirrors vanilla's candidate filters: subsidy loop 15022, rolled loop 15046/15049.
                    if (type != null && type.aiplacement && place.allianceowner == alliance)
                    {
                        if (type.subsidytype >= 0)
                            groups[buildingType] = type.subsidytype;
                        else if (!type.needsunitforplacement)
                            groups[buildingType] = ConstructionProbabilityBias.RolledGroup;
                    }

                    if (!CandidateEligibleForVanillaPath(alliance, state, place, type)) continue;

                    float m = FiscalConstructionScorer.Multiplier(intent, alliance, type.name, type.subsidytype);
                    string constructionReason = "fiscal-only";
                    if (ConstructionSteeringEnabled())
                    {
                        var steering = ConstructionSteeringScorer.DecidePrivateMultiplier(
                            GetConstructionIntent(alliance),
                            buildingType,
                            type.name,
                            m);
                        m = steering.Multiplier;
                        constructionReason = steering.Reason;
                    }
                    var directorPosture = GetDirectorPosture(alliance);
                    if (directorPosture != null)
                    {
                        m = FiscalConstructionScorer.ApplyDirectorBiases(m, type.name, directorPosture);
                        m = ConstructionSteeringScorer.ApplyDirectorBiases(m, buildingType, type.name, directorPosture);
                    }
                    mult[buildingType] = m;
                    reasons[buildingType] = constructionReason;
                }

                var biased = ConstructionProbabilityBias.Bias(raw, mult, groups);
                for (int buildingType = 0; buildingType < slots; buildingType++)
                {
                    float rawProb = raw[buildingType];
                    float newProb = biased[buildingType];
                    if (rawProb <= 0f || NearlyEqual(rawProb, newProb)) continue;
                    if (ConstructionProbabilitySanitizer.Classify(newProb) != ConstructionProbabilityStatus.Valid) continue;

                    state.bestiipplacesprob[buildingType] = newProb;
                    _pending.Add(new PendingRestore(buildingType, state.bestiipplaces[buildingType], rawProb, newProb));

                    if (Plugin.Instance.VerboseLogging.Value && buildingType < count)
                    {
                        EmitConstructionTelemetry(
                            alliance,
                            GameVars.buildingtypes[buildingType]?.name,
                            rawProb,
                            rawProb,
                            newProb,
                            intent.Posture,
                            reasons[buildingType] ?? "rate-cap");
                    }
                }

                if (_pending.Count > 0) _pendingAlliance = alliance;
            }
            catch (Exception ex)
            {
                OnceLog.Warning("economy-construction:prefix", "[Patch:EconomyConstruction] prefix failed: " + ex.Message);
            }
        }

        [HarmonyFinalizer]
        internal static void Finalizer(int alliancerunthrough)
        {
            using (TelemetryPerf.Scope("campaign.patch.economy-construction.finalizer", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            try
            {
                if (_pendingAlliance < 0 || _pendingAlliance != alliancerunthrough || _pending.Count == 0) return;
                var state = GameVars.alliance != null && alliancerunthrough < GameVars.alliance.Length
                    ? GameVars.alliance[alliancerunthrough]
                    : null;
                if (state == null || state.bestiipplacesprob == null || state.bestiipplaces == null) return;

                for (int i = 0; i < _pending.Count; i++)
                {
                    var p = _pending[i];
                    if (p.Index >= state.bestiipplacesprob.Length || p.Index >= state.bestiipplaces.Length) continue;
                    bool samePlace = ReferenceEquals(state.bestiipplaces[p.Index], p.Place);
                    if (ConstructionProbabilityBias.ShouldRestore(state.bestiipplacesprob[p.Index], p.Biased, samePlace))
                        state.bestiipplacesprob[p.Index] = p.Raw;
                }
            }
            catch (Exception ex)
            {
                OnceLog.Warning("economy-construction:finalizer", "[Patch:EconomyConstruction] finalizer restore failed: " + ex.Message);
            }
            finally
            {
                _pending.Clear();
                _pendingAlliance = -1;
            }
        }

        private static bool ConstructionSteeringEnabled()
        {
            try
            {
                return Plugin.Instance != null &&
                    Plugin.Instance.EnableConstructionSiteSteering != null &&
                    Plugin.Instance.EnableConstructionSiteSteering.Value;
            }
            catch
            {
                return false;
            }
        }

        private static ConstructionOutput GetConstructionIntent(int alliance)
        {
            try
            {
                var coordinator = StrategicCoordinator.Instance;
                if (coordinator == null || coordinator.ConstructionIntents == null)
                    return null;

                return alliance >= 0 && alliance < coordinator.ConstructionIntents.Length
                    ? coordinator.ConstructionIntents[alliance]
                    : null;
            }
            catch (Exception ex)
            {
                OnceLog.Warning("economy-construction:construction-intent", "[Patch:EconomyConstruction] construction intent read failed: " + ex.Message);
                return null;
            }
        }

        private static void EmitConstructionTelemetry(
            int alliance,
            string buildingName,
            float oldProbability,
            float originalProbability,
            float newProbability,
            FiscalPosture posture,
            string reason)
        {
            string safeBuilding = string.IsNullOrWhiteSpace(buildingName) ? "-" : buildingName;
            string safeReason = string.IsNullOrWhiteSpace(reason) ? "-" : reason;
            string signature = "alliance=" + alliance +
                "|building=" + safeBuilding +
                "|posture=" + posture +
                "|reason=" + safeReason +
                "|old=" + oldProbability.ToString("0.000", CultureInfo.InvariantCulture) +
                "|new=" + newProbability.ToString("0.000", CultureInfo.InvariantCulture);
            TelemetryRouter.Emit(TelemetryLayer.Campaign, TelemetryCategory.Trace, "ConstructionTelemetry", TelemetrySeverity.Info, ev => ev
                .WithAlliance(alliance)
                .WithDecision("bias-probability", safeReason, signature)
                .WithField("building", safeBuilding)
                .WithField("oldProb", oldProbability)
                .WithField("originalProb", originalProbability)
                .WithField("newProb", newProbability)
                .WithField("posture", posture.ToString())
                .WithField("constructionReason", safeReason));
        }

        private static DirectorPosture GetDirectorPosture(int alliance)
        {
            try
            {
                var memories = StrategicCoordinator.Instance?.DirectorMemories;
                if (memories == null || alliance < 0 || alliance >= memories.Length) return null;
                return memories[alliance]?.LastPosture;
            }
            catch (Exception ex)
            {
                OnceLog.Warning("economy-construction:director-posture", "[Patch:EconomyConstruction] director posture read failed: " + ex.Message);
                return null;
            }
        }

        private static bool CandidateEligibleForVanillaPath(
            int alliance,
            GameVars.Alliance state,
            IIP place,
            GameVars.BuildingType type)
        {
            if (state == null || place == null || type == null || !type.aiplacement) return false;
            if (place.allianceowner != alliance) return false;

            if (type.subsidytype >= 0)
            {
                if (alliance == GameVars.playeralliance && !GameVars.ai_vs_ai && !GameVars.automanageconstructionp)
                    return false;

                return UseSubsidyForPurpose(alliance, type.subsidytype) &&
                    SubsidyFundingAvailable(alliance, place, type);
            }

            if (type.needsunitforplacement) return false;
            if (alliance == GameVars.playeralliance && !GameVars.ai_vs_ai && !GameVars.automanageconstructiong)
                return false;

            return RatingOkForConstruction(state);
        }

        private static bool UseSubsidyForPurpose(int alliance, int subsidyType)
        {
            try
            {
                return AICampaign.UseSubsidyForPurpose(alliance, subsidyType) == 1;
            }
            catch (Exception ex)
            {
                OnceLog.Warning("economy-construction:subsidy-purpose", "[Patch:EconomyConstruction] UseSubsidyForPurpose failed: " + ex.Message);
                return false;
            }
        }

        private static bool SubsidyFundingAvailable(int alliance, IIP place, GameVars.BuildingType type)
        {
            try
            {
                if (place == null || type == null) return false;
                return type.GetMissingSubsidyFundingCost(
                    alliance,
                    0,
                    ((UnityEngine.Component)place).transform.position,
                    place) >= 0f;
            }
            catch (Exception ex)
            {
                OnceLog.Warning("economy-construction:subsidy-funding", "[Patch:EconomyConstruction] GetMissingSubsidyFundingCost failed: " + ex.Message);
                return false;
            }
        }

        private static bool RatingOkForConstruction(GameVars.Alliance state)
        {
            try
            {
                return state != null && state.IsRatingOkForConstruction();
            }
            catch (Exception ex)
            {
                OnceLog.Warning("economy-construction:rating", "[Patch:EconomyConstruction] IsRatingOkForConstruction failed: " + ex.Message);
                return false;
            }
        }

        private static bool NearlyEqual(float left, float right)
        {
            return Math.Abs(left - right) < 0.0001f;
        }

        private struct PendingRestore
        {
            internal readonly int Index;
            internal readonly IIP Place;
            internal readonly float Raw;
            internal readonly float Biased;

            internal PendingRestore(int index, IIP place, float raw, float biased)
            {
                Index = index;
                Place = place;
                Raw = raw;
                Biased = biased;
            }
        }
    }
}
