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
    // Vanilla AICampaign.UpdateCompanyFoundations consumes
    // alliance.bestiipplacesprob inside the method and can return immediately
    // after starting construction. This Prefix biases already-valid candidate
    // probabilities before vanilla selects, while leaving construction,
    // funding, rating, ownership, and placement validation to vanilla.
    [HarmonyPatch(typeof(AICampaign), "UpdateCompanyFoundations")]
    internal static class EconomyConstructionPatch
    {
        private static readonly Dictionary<int, BiasStamp> _biasedCandidates = new Dictionary<int, BiasStamp>();

        [HarmonyPrefix]
        internal static void Prefix(int alliancerunthrough)
        {
            try
            {
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

                OnceLog.Info("economy-construction", "EconomyConstructionPatch wired (Prefix on UpdateCompanyFoundations)");

                int count = Math.Min(state.bestiipplaces.Length, state.bestiipplacesprob.Length);
                count = Math.Min(count, GameVars.buildingtypes.Count);

                for (int buildingType = 0; buildingType < state.bestiipplacesprob.Length; buildingType++)
                {
                    int key = Key(alliance, buildingType);

                    float oldProb = state.bestiipplacesprob[buildingType];
                    var oldStatus = ConstructionProbabilitySanitizer.Classify(oldProb);
                    if (oldStatus == ConstructionProbabilityStatus.Invalid)
                    {
                        state.bestiipplacesprob[buildingType] = 0f;
                        _biasedCandidates.Remove(key);
                        OnceLog.Warning("economy-construction:invalid-probability", "[Patch:EconomyConstruction] invalid bestiipplaces probability sanitized to 0");
                        continue;
                    }
                    if (oldStatus == ConstructionProbabilityStatus.Skip)
                    {
                        _biasedCandidates.Remove(key);
                        continue;
                    }

                    if (buildingType >= count) continue;

                    var place = state.bestiipplaces[buildingType];
                    if (place == null) continue;

                    var type = GameVars.buildingtypes[buildingType];
                    if (!CandidateEligibleForVanillaPath(alliance, state, place, type)) continue;

                    float mult = FiscalConstructionScorer.Multiplier(intent, alliance, type.name, type.subsidytype);
                    string constructionReason = "fiscal-only";
                    if (ConstructionSteeringEnabled())
                    {
                        var steering = ConstructionSteeringScorer.DecidePrivateMultiplier(
                            GetConstructionIntent(alliance),
                            buildingType,
                            type.name,
                            mult);
                        mult = steering.Multiplier;
                        constructionReason = steering.Reason;
                    }
                    var directorPosture = GetDirectorPosture(alliance);
                    if (directorPosture != null)
                    {
                        mult = FiscalConstructionScorer.ApplyDirectorBiases(mult, type.name, directorPosture);
                        mult = ConstructionSteeringScorer.ApplyDirectorBiases(mult, buildingType, type.name, directorPosture);
                    }

                    float originalProb = oldProb;
                    BiasStamp previous;
                    bool hasPrevious = _biasedCandidates.TryGetValue(key, out previous) &&
                        ReferenceEquals(previous.Place, place);

                    if (hasPrevious && NearlyEqual(oldProb, previous.BiasedProbability))
                    {
                        if (NearlyEqual(mult, previous.Multiplier) &&
                            previous.Posture == intent.Posture &&
                            previous.SupplyProtection == intent.SupplyProtection &&
                            previous.LogisticsExpansion == intent.LogisticsExpansion)
                            continue;

                        originalProb = previous.OriginalProbability;
                    }

                    if (Math.Abs(mult - 1f) < 0.01f)
                    {
                        if (hasPrevious && NearlyEqual(oldProb, previous.BiasedProbability))
                            state.bestiipplacesprob[buildingType] = originalProb;

                        _biasedCandidates.Remove(key);
                        continue;
                    }

                    float newProb = originalProb * mult;
                    if (ConstructionProbabilitySanitizer.Classify(newProb) != ConstructionProbabilityStatus.Valid)
                    {
                        state.bestiipplacesprob[buildingType] = 0f;
                        _biasedCandidates.Remove(key);
                        OnceLog.Warning("economy-construction:invalid-probability", "[Patch:EconomyConstruction] invalid bestiipplaces probability sanitized to 0");
                        continue;
                    }

                    state.bestiipplacesprob[buildingType] = newProb;
                    _biasedCandidates[key] = new BiasStamp(
                        place,
                        originalProb,
                        newProb,
                        mult,
                        intent.Posture,
                        intent.SupplyProtection,
                        intent.LogisticsExpansion);

                    if (Plugin.Instance.VerboseLogging.Value)
                    {
                        EmitConstructionTelemetry(
                            alliance,
                            type.name,
                            oldProb,
                            originalProb,
                            newProb,
                            intent.Posture,
                            constructionReason);
                    }
                }
            }
            catch (Exception ex)
            {
                OnceLog.Warning("economy-construction:prefix", "[Patch:EconomyConstruction] prefix failed: " + ex.Message);
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

        private static int Key(int alliance, int buildingType)
        {
            return alliance * 10000 + buildingType;
        }

        private static bool NearlyEqual(float left, float right)
        {
            return Math.Abs(left - right) < 0.0001f;
        }

        private struct BiasStamp
        {
            internal readonly IIP Place;
            internal readonly float OriginalProbability;
            internal readonly float BiasedProbability;
            internal readonly float Multiplier;
            internal readonly FiscalPosture Posture;
            internal readonly bool SupplyProtection;
            internal readonly bool LogisticsExpansion;

            internal BiasStamp(
                IIP place,
                float originalProbability,
                float biasedProbability,
                float multiplier,
                FiscalPosture posture,
                bool supplyProtection,
                bool logisticsExpansion)
            {
                Place = place;
                OriginalProbability = originalProbability;
                BiasedProbability = biasedProbability;
                Multiplier = multiplier;
                Posture = posture;
                SupplyProtection = supplyProtection;
                LogisticsExpansion = logisticsExpansion;
            }
        }
    }
}
