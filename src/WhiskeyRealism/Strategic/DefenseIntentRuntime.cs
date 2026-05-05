// DefenseIntentRuntime — reflection-based extractor that snapshots live AICampaign
// state into a DefenseIntentInput for consumption by DefenseIntentLedger.Build().
//
// Vanilla anchors used:
//   AICampaign.aifaction (private static list of AIFaction inner class)
//   AICampaign.seainvasionforce (public static List<SeaInvasionForce>)
//   AICampaign.raidforces (public static List<RaidForce>)
//   SeaInvasionForce fields: aifactionused, invasionforce, assignedescort,
//       seainvasionspot, currentstate, sourceport
//   SeaInvasion.GetUnitStrengthInRange(int allianceid, bool returnbalance, int shipweight)
//   RaidForce fields: raidgroup, currentstate, aifactionused, closestarea
//   RaidForce.IsRaidUnit(Regiment) — static helper
//   SeaInvasionForce.GetSeaInvasionForceReference(Regiment) — static helper
//   AIFaction fields: ownunits, enemyunits, unitsinoffensiveoperations,
//       unitsindefensiveoperations, unitsconstructingsupplydepots, allianceid, ownports
//   Regiment: groupstrengthactive (int), groupmorale (float), regimentpaths (int),
//       inbattle (bool), onretreat (bool)
//   CampaignArmyPanel.GetReadinessStep(Regiment) — static/instance helper
//   DLC_WL.IsMovedByPlayer(Regiment) — static helper
//   Tools.GetXZDistance(Vector3, Vector3) — static helper
//   GamePrefs.maxdistancedefensiveoperations — static float field
//   GamePrefs.seainvasionspotsunitrange — static float field

using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Patches;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Strategic
{
    internal static class DefenseIntentRuntime
    {
        // Theater adjacency used by ExtractCandidateUnits Tier classification.
        private static readonly Dictionary<Theater, HashSet<Theater>> TheaterAdjacency =
            new Dictionary<Theater, HashSet<Theater>>
            {
                { Theater.East,      new HashSet<Theater> { Theater.West, Theater.Coast } },
                { Theater.West,      new HashSet<Theater> { Theater.East, Theater.TransMiss, Theater.River } },
                { Theater.TransMiss, new HashSet<Theater> { Theater.West, Theater.River } },
                { Theater.Coast,     new HashSet<Theater> { Theater.East } },
                { Theater.River,     new HashSet<Theater> { Theater.West, Theater.TransMiss } }
            };

        // ---------------------------------------------------------------------------
        // Public entry point
        // ---------------------------------------------------------------------------

        internal static DefenseIntentInput BuildInput(
            int allianceId,
            CIC cic,
            EraStageManager era,
            CampaignMapLedger map,
            DefenseCooldownTable cooldown,
            float totalAllianceEffectiveStrength,
            GrandStrategyProfile profile = null,
            HashSet<string> previousSifSignatures = null)
        {
            if (cooldown == null)
            {
                OnceLog.Warning(
                    "defense-intent:runtime:no-cooldown:" + allianceId,
                    $"[DefenseIntent] no persistent cooldown table supplied for alliance={allianceId}; state is discarded each cycle");
                cooldown = new DefenseCooldownTable();
            }

            var input = new DefenseIntentInput
            {
                AllianceId = allianceId,
                PlayerIsCIC = cic == null,
                CICPersonality = cic != null && era != null
                    ? cic.Effective(era)
                    : default(PersonalityVector),
                Cooldown = cooldown,
                CooldownDays = 4,
                GuardBudgetFraction = 0.10f,
                TotalAllianceEffectiveStrength = totalAllianceEffectiveStrength
            };

            if (input.PlayerIsCIC) return input;

            try
            {
                var coordinator = StrategicCoordinator.Instance;
                if (coordinator != null && allianceId >= 0)
                {
                    if (coordinator.Fronts != null && allianceId < coordinator.Fronts.Length)
                        input.FrontLedger = coordinator.Fronts[allianceId];
                    if (coordinator.FormationDirectives != null && allianceId < coordinator.FormationDirectives.Length)
                        input.FormationDirectives = coordinator.FormationDirectives[allianceId];

                    // Scale GuardBudgetFraction from director posture.  Default 0.10f is preserved
                    // when DirectorMemories is absent or posture is null.
                    var posture = coordinator.DirectorMemories != null && allianceId < coordinator.DirectorMemories.Length
                        ? coordinator.DirectorMemories[allianceId]?.LastPosture
                        : null;
                    input.GuardBudgetFraction = Clamp(0.10f + (posture?.GuardBudgetFractionModifier ?? 0f), 0.05f, 0.15f);
                }
            }
            catch { }

            int aifactionIndex = ResolveAifactionIndex(allianceId);
            if (aifactionIndex < 0) return input;

            var faction = AICampaignReflect.GetFaction(aifactionIndex);
            if (faction == null) return input;

            // Cache GamePrefs values once per call to avoid repeated reflection overhead
            // in the per-unit inner loops.
            float defOpsRadius = ReadGamePrefsFloat("maxdistancedefensiveoperations", 250f);
            float seaSpotRadius = ReadGamePrefsFloat("seainvasionspotsunitrange", 100f);

            // Find our capital position once for AssetRoleScorer.capitalDistance computations.
            // Scan owned towns with IsCapital; fall back to 9999f if none found (CapitalApproach
            // gate will simply not fire, which is safer than crashing).
            Vector3 capitalPos = Vector3.zero;
            bool capitalFound = false;
            if (map != null)
            {
                foreach (var town in map.Towns)
                {
                    if (town != null && town.IsCapital && town.Owner == allianceId)
                    {
                        capitalPos = new Vector3(town.X, 0f, town.Z);
                        capitalFound = true;
                        break;
                    }
                }
            }

            // Build enemy unit position snapshot once, shared across all extractors that
            // need frontDistance for AssetRoleScorer.  Rebuilding this inside each extractor
            // independently would give subtly different snapshots; one snapshot per BuildInput
            // call is the correct semantics.
            var enemyPositions = new List<Vector3>();
            try
            {
                var enemyUnitsField = AccessTools.Field(faction.GetType(), "enemyunits");
                var enemyList = enemyUnitsField?.GetValue(faction) as IList;
                if (enemyList != null)
                {
                    for (int j = 0; j < enemyList.Count; j++)
                    {
                        var enemy = enemyList[j];
                        if (enemy != null)
                            enemyPositions.Add(enemy is Component comp ? comp.transform.position : ObjectPosition(enemy));
                    }
                }
            }
            catch { }

            try { ExtractSeaInvasionThreats(input, allianceId, aifactionIndex, map, seaSpotRadius, defOpsRadius, profile, capitalPos, capitalFound, enemyPositions, previousSifSignatures); }
            catch (Exception ex) { OnceLog.Warning("defense-intent:runtime:sif", "[DefenseIntent] sif extract failed: " + ex.Message); }

            try { ExtractRaidForceThreats(input, allianceId, aifactionIndex, map, defOpsRadius, profile, capitalPos, capitalFound, enemyPositions); }
            catch (Exception ex) { OnceLog.Warning("defense-intent:runtime:raid", "[DefenseIntent] raid extract failed: " + ex.Message); }

            try { ExtractAssetProximityThreats(input, allianceId, faction, map, defOpsRadius, profile, capitalPos, capitalFound, enemyPositions); }
            catch (Exception ex) { OnceLog.Warning("defense-intent:runtime:proximity", "[DefenseIntent] proximity extract failed: " + ex.Message); }

            try { ExtractCandidateUnits(input, allianceId, faction, map, defOpsRadius); }
            catch (Exception ex) { OnceLog.Warning("defense-intent:runtime:candidates", "[DefenseIntent] candidate extract failed: " + ex.Message); }

            try { ExtractGuardCandidateAssets(input, allianceId, map); }
            catch (Exception ex) { OnceLog.Warning("defense-intent:runtime:guard", "[DefenseIntent] guard asset extract failed: " + ex.Message); }

            return input;
        }

        // ---------------------------------------------------------------------------
        // ResolveAifactionIndex
        // ---------------------------------------------------------------------------

        private static int ResolveAifactionIndex(int allianceId)
        {
            try
            {
                var aicType = AccessTools.TypeByName("AICampaign");
                var list = AccessTools.Field(aicType, "aifaction")?.GetValue(null) as IList;
                if (list == null) return -1;
                for (int i = 0; i < list.Count; i++)
                {
                    int aid = AICampaignReflect.GetAllianceId(i);
                    if (aid == allianceId) return i;
                }
            }
            catch { }
            return -1;
        }

        // ---------------------------------------------------------------------------
        // ExtractSeaInvasionThreats
        // ---------------------------------------------------------------------------

        private static void ExtractSeaInvasionThreats(
            DefenseIntentInput input, int allianceId, int aifactionIndex,
            CampaignMapLedger map, float seaSpotRadius, float defOpsRadius,
            GrandStrategyProfile profile, Vector3 capitalPos, bool capitalFound,
            List<Vector3> enemyPositions,
            HashSet<string> previousSifSignatures)
        {
            var aicType = AccessTools.TypeByName("AICampaign");
            var sifList = AccessTools.Field(aicType, "seainvasionforce")?.GetValue(null) as IList;
            if (sifList == null) return;

            var sifType = AccessTools.TypeByName("SeaInvasionForce");
            if (sifType == null)
            {
                OnceLog.Warning("defense-intent:runtime:sif:no-type", "[DefenseIntent] SeaInvasionForce type not found via reflection");
                return;
            }

            // TODO(Task 14): wire previousSifSignatures into a tombstone pass when
            // StrategicCoordinator owns the per-alliance seen-set. For now, the
            // parameter is accepted and threaded in so the Task 14 caller can pass
            // a real set without any signature change.

            for (int i = 0; i < sifList.Count; i++)
            {
                var entry = sifList[i];
                if (entry == null) continue;

                // Skip own invasion forces — we care about enemy invasions targeting us.
                int entryFactionIndex = ReadInt(entry, "aifactionused", -1);
                if (entryFactionIndex == aifactionIndex) continue;

                // Verify the invading faction is hostile (different alliance).
                int invadingAlliance = AICampaignReflect.GetAllianceId(entryFactionIndex);
                if (invadingAlliance == allianceId) continue;

                var invasionForce = ReadObject(entry, "invasionforce");
                if (invasionForce == null) continue;

                var seaSpot = ReadObject(entry, "seainvasionspot");
                var sourcePort = ReadObject(entry, "sourceport");
                int currentState = ReadInt(entry, "currentstate", 0);

                // Position of the invasion force.
                Vector3 forcePos = ObjectPosition(invasionForce);

                // LandedSignal: any of three conditions.
                bool landedSignal = false;

                // Condition 1: state >= 2 AND close to landing spot.
                if (!landedSignal && currentState >= 2 && seaSpot != null)
                {
                    Vector3 spotPos = ObjectPosition(seaSpot);
                    float distToSpot = GetXZDistance(forcePos, spotPos);
                    if (distToSpot < seaSpotRadius)
                        landedSignal = true;
                }

                // Condition 2: regimentpaths > 0 AND close to nearest coastal asset.
                if (!landedSignal)
                {
                    int regimentPaths = ReadInt(invasionForce, "regimentpaths", 0);
                    if (regimentPaths > 0)
                    {
                        float nearestAssetDist = NearestAssetDistance(forcePos, allianceId, map);
                        if (nearestAssetDist < defOpsRadius)
                            landedSignal = true;
                    }
                }

                // Condition 3: enemy strength reported at the sea invasion spot.
                if (!landedSignal && seaSpot != null)
                {
                    try
                    {
                        var getStrength = AccessTools.Method(AccessTools.TypeByName("SeaInvasion"), "GetUnitStrengthInRange");
                        if (getStrength != null)
                        {
                            float enemyAtSpot = Convert.ToSingle(
                                getStrength.Invoke(seaSpot, new object[] { allianceId, false, 1000 }));
                            if (enemyAtSpot > 0f)
                                landedSignal = true;
                        }
                    }
                    catch { }
                }

                // Find nearest owned asset to use as asset metadata.
                CampaignMapAsset nearestAsset = FindNearestAsset(forcePos, allianceId, map, defOpsRadius);

                // Instance ID for invasion force (UnityEngine.Object).
                int invasionForceInstanceId = 0;
                try { invasionForceInstanceId = ((UnityEngine.Object)invasionForce).GetInstanceID(); }
                catch { }

                string spotName = null;
                try { spotName = ((UnityEngine.Object)seaSpot)?.name; } catch { }

                string portName = null;
                try { portName = ((UnityEngine.Object)sourcePort)?.name; } catch { }

                int forceStrength = invasionForce is Regiment invasionRegiment
                    ? invasionRegiment.groupstrengthactive
                    : ReadInt(invasionForce, "groupstrengthactive", 0);

                // Derive profile-weighted asset role; OR with catalog role so both flow through.
                AssetStrategicRole catalogRole = nearestAsset?.StrategicRole ?? AssetStrategicRole.None;
                AssetStrategicRole assetRole = catalogRole;
                if (profile != null && nearestAsset != null)
                {
                    float capitalDist = capitalFound
                        ? GetXZDistance(new Vector3(nearestAsset.X, 0f, nearestAsset.Z), capitalPos)
                        : 9999f;
                    float frontDist = ComputeFrontDistance(new Vector3(nearestAsset.X, 0f, nearestAsset.Z), enemyPositions);
                    assetRole |= AssetRoleScorer.Score(nearestAsset, profile, capitalDist, frontDist);
                }

                input.Threats.Add(new DefenseThreatSource
                {
                    Kind = DefenseThreatSourceKind.SeaInvasion,
                    InvasionForceInstanceId = invasionForceInstanceId,
                    SpotName = spotName,
                    SourcePortName = portName,
                    X = forcePos.x,
                    Z = forcePos.z,
                    EnemyStrength = (float)forceStrength,
                    LandedSignal = landedSignal,
                    VanillaCollapsed = false,
                    AssetName = nearestAsset?.Name,
                    AssetKind = nearestAsset?.Kind ?? CampaignMapAssetKind.Unknown,
                    AssetRole = assetRole
                });
            }
        }

        // ---------------------------------------------------------------------------
        // ExtractRaidForceThreats
        // ---------------------------------------------------------------------------

        private static void ExtractRaidForceThreats(
            DefenseIntentInput input, int allianceId, int aifactionIndex,
            CampaignMapLedger map, float defOpsRadius,
            GrandStrategyProfile profile, Vector3 capitalPos, bool capitalFound,
            List<Vector3> enemyPositions)
        {
            var aicType = AccessTools.TypeByName("AICampaign");
            var raidList = AccessTools.Field(aicType, "raidforces")?.GetValue(null) as IList;
            if (raidList == null) return;

            for (int i = 0; i < raidList.Count; i++)
            {
                var entry = raidList[i];
                if (entry == null) continue;

                // Skip own raid forces.
                int entryFactionIndex = ReadInt(entry, "aifactionused", -1);
                if (entryFactionIndex == aifactionIndex) continue;

                var raidGroup = ReadObject(entry, "raidgroup");
                if (raidGroup == null) continue;

                int currentState = ReadInt(entry, "currentstate", 0);
                Vector3 raidPos = ObjectPosition(raidGroup);

                // Only include if near one of our assets.
                CampaignMapAsset nearestAsset = FindNearestAsset(raidPos, allianceId, map, defOpsRadius);
                if (nearestAsset == null) continue;

                int raidGroupInstanceId = 0;
                try { raidGroupInstanceId = ((UnityEngine.Object)raidGroup).GetInstanceID(); }
                catch { }

                int raidStrength = raidGroup is Regiment raidRegiment
                    ? raidRegiment.groupstrengthactive
                    : ReadInt(raidGroup, "groupstrengthactive", 0);

                // Derive profile-weighted asset role; OR with catalog role so both flow through.
                AssetStrategicRole catalogRole = nearestAsset.StrategicRole;
                AssetStrategicRole assetRole = catalogRole;
                if (profile != null)
                {
                    float capitalDist = capitalFound
                        ? GetXZDistance(new Vector3(nearestAsset.X, 0f, nearestAsset.Z), capitalPos)
                        : 9999f;
                    float frontDist = ComputeFrontDistance(new Vector3(nearestAsset.X, 0f, nearestAsset.Z), enemyPositions);
                    assetRole |= AssetRoleScorer.Score(nearestAsset, profile, capitalDist, frontDist);
                }

                input.Threats.Add(new DefenseThreatSource
                {
                    Kind = DefenseThreatSourceKind.RaidForce,
                    RaidGroupInstanceId = raidGroupInstanceId,
                    RaidCurrentState = currentState,
                    AssetName = nearestAsset.Name,
                    AssetKind = nearestAsset.Kind,
                    AssetRole = assetRole,
                    X = raidPos.x,
                    Z = raidPos.z,
                    EnemyStrength = (float)raidStrength,
                    LandedSignal = true
                });
            }
        }

        // ---------------------------------------------------------------------------
        // ExtractAssetProximityThreats
        // ---------------------------------------------------------------------------

        private static void ExtractAssetProximityThreats(
            DefenseIntentInput input, int allianceId, object faction,
            CampaignMapLedger map, float defOpsRadius,
            GrandStrategyProfile profile, Vector3 capitalPos, bool capitalFound,
            List<Vector3> enemyPositions)
        {
            // Proximity radius and frontline gate: vanilla's defensive-operation radius
            // is the same scale candidate units use to volunteer; using
            // seainvasionspotsunitrange (the much larger landing-spot radius) caused
            // every CSA unit on the Virginia frontier to register as a "proximity threat"
            // against Maryland and New York ports during Slice 1 smoke. The frontline-side
            // check rejects enemies who haven't actually crossed into our territory.

            // Build set of asset names already covered by sif/raid threats to avoid duplicates.
            var coveredAssets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var threat in input.Threats)
            {
                if (!string.IsNullOrEmpty(threat.AssetName))
                    coveredAssets.Add(threat.AssetName);
            }

            // Read enemy units from the faction for per-asset proximity checks.
            // enemyPositions (shared snapshot from BuildInput) supplies positions for
            // ComputeFrontDistance; we need instanceId and strength here too, so we
            // re-read the list.  Two list reads per BuildInput call is acceptable; the
            // alternative would be a richer shared snapshot type that is harder to read.
            var enemyUnits = AccessTools.Field(faction.GetType(), "enemyunits")?.GetValue(faction) as IList;
            if (enemyUnits == null) return;

            // Snapshot enemy positions + instance ids + strengths for the inner loop.
            var enemies = new List<(Vector3 pos, int instanceId, float strength, float morale)>();
            for (int j = 0; j < enemyUnits.Count; j++)
            {
                var enemy = enemyUnits[j];
                if (enemy == null) continue;
                var regiment = enemy as Regiment;
                Vector3 ep = regiment != null
                    ? ((Component)regiment).transform.position
                    : ObjectPosition(enemy);
                int eid = 0;
                try { eid = ((UnityEngine.Object)enemy).GetInstanceID(); } catch { }
                float estr = regiment != null
                    ? regiment.groupstrengthactive
                    : (float)ReadInt(enemy, "groupstrengthactive", 0);
                float emor = regiment != null
                    ? regiment.groupmorale
                    : ReadFloat(enemy, "groupmorale", 1f);
                enemies.Add((ep, eid, estr, emor));
            }

            // "Very close" radius: absolute proximity that bypasses the frontline check.
            float veryCloseRadius = defOpsRadius * 0.4f;
            var frontline = ResolveReadyFrontline();

            foreach (var asset in map.Assets)
            {
                if (asset == null) continue;
                if (asset.Owner != allianceId) continue;
                if (coveredAssets.Contains(asset.Name ?? string.Empty)) continue;

                // Same "interesting asset" filter as ExtractGuardCandidateAssets.
                if (!IsInterestingAsset(asset)) continue;

                Vector3 assetPos = new Vector3(asset.X, 0f, asset.Z);

                // Find enemy units within defOpsRadius of this asset, applying a
                // frontline-side gate so distant enemies on their own side of the line
                // don't register as threats against rear-area ports.
                float totalEnemyStrength = 0f;
                float maxRawEnemyStrength = 0f;
                var nearEnemyIds = new List<int>();
                for (int j = 0; j < enemies.Count; j++)
                {
                    var (ep, eid, estr, emor) = enemies[j];
                    float dist = GetXZDistance(ep, assetPos);
                    if (dist >= defOpsRadius) continue;

                    // Gate: enemy must be very close (< 0.4x defOpsRadius) OR confirmed
                    // on our side of the front line. Skip enemies across the line that
                    // only fall within the wider defOpsRadius cone.
                    bool veryClose = dist < veryCloseRadius;
                    bool onOurSide = !veryClose && IsOnFrontlineSide(frontline, ep, allianceId);
                    if (!veryClose && !onOurSide) continue;

                    // Use ComputeEffective for a readiness-adjusted strength estimate.
                    // Readiness not available on enemy units — default to 2f (full).
                    totalEnemyStrength += DefenseForceSizer.ComputeEffective(estr, emor, 2f);
                    if (estr > maxRawEnemyStrength) maxRawEnemyStrength = estr;
                    nearEnemyIds.Add(eid);
                }

                if (nearEnemyIds.Count == 0) continue;

                // Minimum-strength floor: suppress single-scout artifacts.
                // Require meaningful aggregate effective strength AND at least one
                // individually sized unit to distinguish a real force from a stray.
                if (totalEnemyStrength < 1500f) continue;
                if (maxRawEnemyStrength < 500f) continue;

                // Sort ascending and cap at top 3 for a tight dedup key.
                nearEnemyIds.Sort();
                int cap = Math.Min(3, nearEnemyIds.Count);
                var topIds = new int[cap];
                for (int k = 0; k < cap; k++) topIds[k] = nearEnemyIds[k];

                // Derive profile-weighted asset role; OR with catalog role so both flow through.
                AssetStrategicRole catalogRole = asset.StrategicRole;
                AssetStrategicRole assetRole = catalogRole;
                if (profile != null)
                {
                    float capitalDist = capitalFound
                        ? GetXZDistance(assetPos, capitalPos)
                        : 9999f;
                    float frontDist = ComputeFrontDistance(assetPos, enemyPositions);
                    assetRole |= AssetRoleScorer.Score(asset, profile, capitalDist, frontDist);
                }

                input.Threats.Add(new DefenseThreatSource
                {
                    Kind = DefenseThreatSourceKind.AssetProximity,
                    AssetName = asset.Name,
                    AssetKind = asset.Kind,
                    AssetRole = assetRole,
                    X = asset.X,
                    Z = asset.Z,
                    EnemyStrength = totalEnemyStrength,
                    EnemyInstanceIds = topIds,
                    LandedSignal = true
                });

                coveredAssets.Add(asset.Name ?? string.Empty);
            }
        }

        // ---------------------------------------------------------------------------
        // ExtractCandidateUnits
        // ---------------------------------------------------------------------------

        private static void ExtractCandidateUnits(
            DefenseIntentInput input, int allianceId, object faction,
            CampaignMapLedger map, float defOpsRadius)
        {
            var ownUnits = AccessTools.Field(faction.GetType(), "ownunits")?.GetValue(faction) as IList;
            if (ownUnits == null) return;

            var inOffensiveOps = AccessTools.Field(faction.GetType(), "unitsinoffensiveoperations")
                ?.GetValue(faction) as IList;
            var constructingDepots = AccessTools.Field(faction.GetType(), "unitsconstructingsupplydepots")
                ?.GetValue(faction) as IList;

            // Precompute threat positions for Tier classification.
            var threatPositions = new List<(float x, float z, Theater theater)>();
            foreach (var threat in input.Threats)
            {
                if (threat == null) continue;
                threatPositions.Add((threat.X, threat.Z,
                    TheaterClassifier.FromPosition(threat.X, threat.Z)));
            }

            // Resolve static method handles once.
            var raidType = ResolveAICampaignNestedType("RaidForce");
            var sifType = ResolveAICampaignNestedType("SeaInvasionForce");
            var raidIsRaidUnit = raidType != null
                ? AccessTools.Method(raidType, "IsRaidUnit", new[] { typeof(Regiment) })
                : null;
            var sifGetReference = sifType != null
                ? AccessTools.Method(sifType, "GetSeaInvasionForceReference", new[] { typeof(Regiment) })
                : null;
            var getReadiness = AccessTools.Method(AccessTools.TypeByName("CampaignArmyPanel"), "GetReadinessStep");
            var isMovedByPlayer = AccessTools.Method(AccessTools.TypeByName("DLC_WL"), "IsMovedByPlayer", new[] { typeof(Regiment) });

            // Look up the front ledger for this alliance once; used for CriticalFront classification.
            FrontSectorLedger frontLedger = input.FrontLedger;
            FormationDirectiveLedger formationLedger = input.FormationDirectives;

            for (int i = 0; i < ownUnits.Count; i++)
            {
                var unit = ownUnits[i];
                if (unit == null) continue;
                var regiment = unit as Regiment;

                // Skip retreating or in-battle units.
                if (regiment != null)
                {
                    if (regiment.onretreat) continue;
                    if (regiment.inbattle) continue;
                }
                else
                {
                    if (ReadBool(unit, "onretreat", false)) continue;
                    if (ReadBool(unit, "inbattle", false)) continue;
                }

                // Skip units building supply depots.
                if (constructingDepots != null && constructingDepots.Contains(unit)) continue;

                // Skip raid assets.
                if (raidIsRaidUnit != null && regiment != null)
                {
                    try
                    {
                        bool isRaid = Convert.ToBoolean(raidIsRaidUnit.Invoke(null, new object[] { regiment }));
                        if (isRaid) continue;
                    }
                    catch { }
                }

                // Skip sea-invasion force members.
                if (sifGetReference != null && regiment != null)
                {
                    try
                    {
                        var sifRef = sifGetReference.Invoke(null, new object[] { regiment });
                        if (sifRef != null) continue;
                    }
                    catch { }
                }

                Vector3 unitPos = regiment != null
                    ? ((Component)regiment).transform.position
                    : ObjectPosition(unit);
                Theater unitTheater = TheaterClassifier.FromPosition(unitPos.x, unitPos.z);
                string areaKey = ArmyAreaRuntime.AreaKey(unitPos);
                string sectorKey = FrontSectorRuntime.SectorKey(unitPos);

                // Readiness step.
                float readinessStep = 2f;
                if (getReadiness != null)
                {
                    try
                    {
                        readinessStep = Convert.ToSingle(getReadiness.Invoke(null, new object[] { regiment ?? unit }));
                    }
                    catch { readinessStep = 2f; }
                }

                // Player-controlled check (DLC_WL; may not be present in non-W&L scenarios).
                bool playerControlled = false;
                if (isMovedByPlayer != null && regiment != null)
                {
                    try { playerControlled = Convert.ToBoolean(isMovedByPlayer.Invoke(null, new object[] { regiment })); }
                    catch { }
                }

                // Single-pass tier + closest-threat classification.
                // Tracks (minDist, closestIndex) to eliminate the float-equality fragility of a
                // two-pass approach where the second pass does dist != minDist.
                CandidateTier tier = CandidateTier.CrossMap;
                float minDist = float.MaxValue;
                int closestIndex = -1;

                if (threatPositions.Count == 0)
                {
                    tier = CandidateTier.Local;
                    minDist = 0f;
                }
                else
                {
                    for (int t = 0; t < threatPositions.Count; t++)
                    {
                        var (tx, tz, _) = threatPositions[t];
                        float dist = GetXZDistance(unitPos, new Vector3(tx, 0f, tz));
                        if (dist < minDist)
                        {
                            minDist = dist;
                            closestIndex = t;
                        }
                    }

                    if (closestIndex >= 0)
                    {
                        var (_, _, closestTheater) = threatPositions[closestIndex];
                        if (minDist < defOpsRadius)
                        {
                            tier = CandidateTier.Local;
                        }
                        else if (unitTheater == closestTheater)
                        {
                            tier = CandidateTier.SameTheater;
                        }
                        else if (unitTheater != Theater.Unknown && closestTheater != Theater.Unknown &&
                                 TheaterAdjacency.TryGetValue(unitTheater, out var adj) &&
                                 adj.Contains(closestTheater))
                        {
                            tier = CandidateTier.AdjacentTheater;
                        }
                        else
                        {
                            tier = CandidateTier.CrossMap;
                        }
                    }
                }

                // CriticalFront: true when the unit's theater maps to a sector that is IsCritical
                // OR is under a Hold posture (meaning the line must not give ground there).
                // Gracefully degrades to false when the front ledger is absent.
                bool criticalFront = false;
                if (frontLedger != null && unitTheater != Theater.Unknown)
                {
                    foreach (var sector in frontLedger.Sectors)
                    {
                        if (sector == null) continue;
                        if (sector.Theater == unitTheater &&
                            (sector.IsCritical || sector.Posture == FrontPosture.Hold))
                        {
                            criticalFront = true;
                            break;
                        }
                    }
                }

                int unitInstanceId = 0;
                try { unitInstanceId = ((UnityEngine.Object)unit).GetInstanceID(); } catch { }

                string unitName = null;
                try { unitName = ((UnityEngine.Object)unit).name; } catch { }

                string unitKey = UnitKey(unit, unitName);
                var directive = formationLedger?.GetAssignment(unitKey);

                int strength = regiment != null
                    ? regiment.groupstrengthactive
                    : ReadInt(unit, "groupstrengthactive", 0);
                float morale = regiment != null
                    ? regiment.groupmorale
                    : ReadFloat(unit, "groupmorale", 1f);

                bool inOffensive = inOffensiveOps != null && inOffensiveOps.Contains(unit);

                input.Candidates.Add(new DefenseCandidate
                {
                    UnitInstanceId = unitInstanceId,
                    UnitKey = unitKey,
                    UnitName = unitName,
                    AreaKey = areaKey,
                    SectorKey = sectorKey,
                    X = unitPos.x,
                    Z = unitPos.z,
                    ActiveStrength = (float)strength,
                    Morale = morale,
                    ReadinessStep = readinessStep,
                    Theater = unitTheater,
                    Tier = tier,
                    InOffensiveOperation = inOffensive,
                    PlayerControlled = playerControlled,
                    CriticalFront = criticalFront,
                    HasFormationDirective = directive != null,
                    DefensiveAllowed = directive == null || directive.DefensiveAllowed,
                    TransferDonorAllowed = directive == null || directive.TransferDonorAllowed,
                    DirectMovementAllowed = directive == null || directive.DirectMovementAllowed,
                    DistanceToThreat = minDist == float.MaxValue ? 0f : minDist
                });
            }
        }

        // ---------------------------------------------------------------------------
        // ExtractGuardCandidateAssets
        // ---------------------------------------------------------------------------

        private static void ExtractGuardCandidateAssets(
            DefenseIntentInput input, int allianceId, CampaignMapLedger map)
        {
            // Assets already covered by an active threat don't need a guard slot.
            var coveredByThreat = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var threat in input.Threats)
            {
                if (!string.IsNullOrEmpty(threat.AssetName))
                    coveredByThreat.Add(threat.AssetName);
            }

            foreach (var asset in map.Assets)
            {
                if (asset == null) continue;
                if (asset.Owner != allianceId) continue;
                if (coveredByThreat.Contains(asset.Name ?? string.Empty)) continue;
                if (!IsInterestingAsset(asset)) continue;

                input.GuardCandidateAssets.Add(asset);
            }
        }

        // ---------------------------------------------------------------------------
        // Shared helpers
        // ---------------------------------------------------------------------------

        // Compute the minimum XZ distance from a position to any known enemy unit.
        // Used to approximate frontDistance for AssetRoleScorer.Score().
        // Returns 9999f when the enemy list is empty (scorer gates treat this as "far from front").
        private static float ComputeFrontDistance(Vector3 pos, List<Vector3> enemyPositions)
        {
            float best = 9999f;
            for (int i = 0; i < enemyPositions.Count; i++)
            {
                float d = GetXZDistance(pos, enemyPositions[i]);
                if (d < best) best = d;
            }
            return best;
        }

        // Returns true for assets worth guarding: any non-None strategic role, or
        // a Fort/Harbor with Level >= 2.  Keeps guard-pass focused on meaningful assets.
        private static bool IsInterestingAsset(CampaignMapAsset asset)
        {
            if (asset.StrategicRole != AssetStrategicRole.None) return true;
            if (asset.Level >= 2 &&
                (asset.Kind == CampaignMapAssetKind.Fort ||
                 asset.Kind == CampaignMapAssetKind.SeaHarbor ||
                 asset.Kind == CampaignMapAssetKind.RiverHarbor))
                return true;
            return false;
        }

        // Find the nearest CampaignMapAsset owned by allianceId within maxDist; null if none.
        private static CampaignMapAsset FindNearestAsset(
            Vector3 pos, int allianceId, CampaignMapLedger map, float maxDist)
        {
            CampaignMapAsset best = null;
            float bestDist = maxDist;
            foreach (var asset in map.Assets)
            {
                if (asset == null) continue;
                if (asset.Owner != allianceId) continue;
                float dist = GetXZDistance(pos, new Vector3(asset.X, 0f, asset.Z));
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = asset;
                }
            }
            return best;
        }

        // Distance to nearest owned asset (returns float.MaxValue if no assets).
        private static float NearestAssetDistance(
            Vector3 pos, int allianceId, CampaignMapLedger map)
        {
            float best = float.MaxValue;
            foreach (var asset in map.Assets)
            {
                if (asset == null || asset.Owner != allianceId) continue;
                float dist = GetXZDistance(pos, new Vector3(asset.X, 0f, asset.Z));
                if (dist < best) best = dist;
            }
            return best;
        }

        // Extract position from a game object: Component cast first, then reflection fallback.
        private static Vector3 ObjectPosition(object obj)
        {
            if (obj is Component comp) return comp.transform.position;
            try
            {
                var m = AccessTools.Method(obj.GetType(), "GetPosition");
                if (m != null) return (Vector3)m.Invoke(obj, null);
            }
            catch { }
            return Vector3.zero;
        }

        // XZ-plane distance. This sits in defense-runtime inner loops, so do not
        // use reflection here; resolving Tools.GetXZDistance per call caused
        // multi-second once-per-day campaign freezes.
        private static float GetXZDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }

        // Read a static float field from GamePrefs; returns fallback on any failure.
        private static float ReadGamePrefsFloat(string fieldName, float fallback)
        {
            try
            {
                var gpType = AccessTools.TypeByName("GamePrefs");
                var field = AccessTools.Field(gpType, fieldName);
                if (field != null) return Convert.ToSingle(field.GetValue(null));
            }
            catch { }
            return fallback;
        }

        private static string UnitKey(object unit, string unitName)
        {
            string name = string.IsNullOrEmpty(unitName) ? "<unknown>" : unitName;
            int commander = ReadInt(unit, "commander", -1);
            return name + ":" + commander.ToString();
        }

        // Read an int field from an object via reflection; returns fallback on failure.
        private static int ReadInt(object obj, string fieldName, int fallback)
        {
            if (obj == null) return fallback;
            try
            {
                var field = AccessTools.Field(obj.GetType(), fieldName);
                if (field == null) return fallback;
                return Convert.ToInt32(field.GetValue(obj));
            }
            catch { return fallback; }
        }

        // Read a float field from an object via reflection; returns fallback on failure.
        private static float ReadFloat(object obj, string fieldName, float fallback)
        {
            if (obj == null) return fallback;
            try
            {
                var field = AccessTools.Field(obj.GetType(), fieldName);
                if (field == null) return fallback;
                return Convert.ToSingle(field.GetValue(obj));
            }
            catch { return fallback; }
        }

        // Read a bool field from an object via reflection; returns fallback on failure.
        private static bool ReadBool(object obj, string fieldName, bool fallback)
        {
            if (obj == null) return fallback;
            try
            {
                var field = AccessTools.Field(obj.GetType(), fieldName);
                if (field == null) return fallback;
                return Convert.ToBoolean(field.GetValue(obj));
            }
            catch { return fallback; }
        }

        // Read any reference field from an object via reflection; returns null on failure.
        private static object ReadObject(object obj, string fieldName)
        {
            if (obj == null) return null;
            try
            {
                var field = AccessTools.Field(obj.GetType(), fieldName);
                return field?.GetValue(obj);
            }
            catch { return null; }
        }

        private static float Clamp(float v, float lo, float hi) => v < lo ? lo : (v > hi ? hi : v);

        private static Type ResolveAICampaignNestedType(string typeName)
        {
            return AccessTools.Inner(typeof(AICampaign), typeName)
                ?? AccessTools.TypeByName("AICampaign+" + typeName)
                ?? AccessTools.TypeByName(typeName);
        }

        private static Frontline2 ResolveReadyFrontline()
        {
            try
            {
                var battleUnits = UnityEngine.GameObject.Find("GameController")
                    ?.GetComponent<BattleUnits>();
                if (battleUnits == null ||
                    battleUnits.frontline2 == null ||
                    battleUnits.frontline2.numberofupdates <= 0)
                    return null;
                return battleUnits.frontline2;
            }
            catch { return null; }
        }

        // Returns false when frontline2 is unavailable or has no updates, which
        // means the caller's onOurSide check also returns false — a safe default
        // that does NOT emit a spurious proximity threat.
        private static bool IsOnFrontlineSide(Frontline2 frontline, Vector3 pos, int allianceId)
        {
            if (frontline == null) return false;
            try { return frontline.GetSideOnPosition(pos) == allianceId; }
            catch { return false; }
        }
    }
}
