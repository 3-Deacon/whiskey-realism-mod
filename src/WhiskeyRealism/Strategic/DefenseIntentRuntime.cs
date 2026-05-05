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
            float totalAllianceEffectiveStrength)
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

            int aifactionIndex = ResolveAifactionIndex(allianceId);
            if (aifactionIndex < 0) return input;

            var faction = AICampaignReflect.GetFaction(aifactionIndex);
            if (faction == null) return input;

            // Cache GamePrefs values once per call to avoid repeated reflection overhead
            // in the per-unit inner loops.
            float defOpsRadius = ReadGamePrefsFloat("maxdistancedefensiveoperations", 250f);
            float seaSpotRadius = ReadGamePrefsFloat("seainvasionspotsunitrange", 100f);

            try { ExtractSeaInvasionThreats(input, allianceId, aifactionIndex, map, seaSpotRadius, defOpsRadius); }
            catch (Exception ex) { OnceLog.Warning("defense-intent:runtime:sif", "[DefenseIntent] sif extract failed: " + ex.Message); }

            try { ExtractRaidForceThreats(input, allianceId, aifactionIndex, map, defOpsRadius); }
            catch (Exception ex) { OnceLog.Warning("defense-intent:runtime:raid", "[DefenseIntent] raid extract failed: " + ex.Message); }

            try { ExtractAssetProximityThreats(input, allianceId, faction, map, seaSpotRadius); }
            catch (Exception ex) { OnceLog.Warning("defense-intent:runtime:proximity", "[DefenseIntent] proximity extract failed: " + ex.Message); }

            try { ExtractCandidateUnits(input, allianceId, faction, map); }
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
            CampaignMapLedger map, float seaSpotRadius, float defOpsRadius)
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

                int forceStrength = ReadInt(invasionForce, "groupstrengthactive", 0);

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
                    AssetRole = nearestAsset?.StrategicRole ?? AssetStrategicRole.None
                });
            }
        }

        // ---------------------------------------------------------------------------
        // ExtractRaidForceThreats
        // ---------------------------------------------------------------------------

        private static void ExtractRaidForceThreats(
            DefenseIntentInput input, int allianceId, int aifactionIndex,
            CampaignMapLedger map, float defOpsRadius)
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

                int raidStrength = ReadInt(raidGroup, "groupstrengthactive", 0);

                input.Threats.Add(new DefenseThreatSource
                {
                    Kind = DefenseThreatSourceKind.RaidForce,
                    RaidGroupInstanceId = raidGroupInstanceId,
                    RaidCurrentState = currentState,
                    AssetName = nearestAsset.Name,
                    AssetKind = nearestAsset.Kind,
                    AssetRole = nearestAsset.StrategicRole,
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
            CampaignMapLedger map, float seaSpotRadius)
        {
            // Build set of asset names already covered by sif/raid threats to avoid duplicates.
            var coveredAssets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var threat in input.Threats)
            {
                if (!string.IsNullOrEmpty(threat.AssetName))
                    coveredAssets.Add(threat.AssetName);
            }

            // Read enemy units from the faction.
            var enemyUnits = AccessTools.Field(faction.GetType(), "enemyunits")?.GetValue(faction) as IList;
            if (enemyUnits == null) return;

            // Snapshot enemy positions + instance ids + strengths for the inner loop.
            var enemies = new List<(Vector3 pos, int instanceId, float strength, float morale)>();
            for (int j = 0; j < enemyUnits.Count; j++)
            {
                var enemy = enemyUnits[j];
                if (enemy == null) continue;
                Vector3 ep = ObjectPosition(enemy);
                int eid = 0;
                try { eid = ((UnityEngine.Object)enemy).GetInstanceID(); } catch { }
                float estr = (float)ReadInt(enemy, "groupstrengthactive", 0);
                float emor = ReadFloat(enemy, "groupmorale", 1f);
                enemies.Add((ep, eid, estr, emor));
            }

            foreach (var asset in map.Assets)
            {
                if (asset == null) continue;
                if (asset.Owner != allianceId) continue;
                if (coveredAssets.Contains(asset.Name ?? string.Empty)) continue;

                // Same "interesting asset" filter as ExtractGuardCandidateAssets.
                if (!IsInterestingAsset(asset)) continue;

                Vector3 assetPos = new Vector3(asset.X, 0f, asset.Z);

                // Find enemy units within seaSpotRadius of this asset.
                float totalEnemyStrength = 0f;
                var nearEnemyIds = new List<int>();
                for (int j = 0; j < enemies.Count; j++)
                {
                    var (ep, eid, estr, emor) = enemies[j];
                    float dist = GetXZDistance(ep, assetPos);
                    if (dist < seaSpotRadius)
                    {
                        // Use ComputeEffective for a readiness-adjusted strength estimate.
                        // Readiness not available on enemy units — default to 2f (full).
                        totalEnemyStrength += DefenseForceSizer.ComputeEffective(estr, emor, 2f);
                        nearEnemyIds.Add(eid);
                    }
                }

                if (nearEnemyIds.Count == 0) continue;

                // Sort ascending and cap at top 5.
                nearEnemyIds.Sort();
                int cap = Math.Min(5, nearEnemyIds.Count);
                var topIds = new int[cap];
                for (int k = 0; k < cap; k++) topIds[k] = nearEnemyIds[k];

                input.Threats.Add(new DefenseThreatSource
                {
                    Kind = DefenseThreatSourceKind.AssetProximity,
                    AssetName = asset.Name,
                    AssetKind = asset.Kind,
                    AssetRole = asset.StrategicRole,
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
            CampaignMapLedger map)
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
            var raidIsRaidUnit = AccessTools.Method(AccessTools.TypeByName("RaidForce"), "IsRaidUnit");
            var sifGetReference = AccessTools.Method(AccessTools.TypeByName("SeaInvasionForce"), "GetSeaInvasionForceReference");
            var getReadiness = AccessTools.Method(AccessTools.TypeByName("CampaignArmyPanel"), "GetReadinessStep");

            for (int i = 0; i < ownUnits.Count; i++)
            {
                var unit = ownUnits[i];
                if (unit == null) continue;

                // Skip retreating or in-battle units.
                if (ReadBool(unit, "onretreat", false)) continue;
                if (ReadBool(unit, "inbattle", false)) continue;

                // Skip units building supply depots.
                if (constructingDepots != null && constructingDepots.Contains(unit)) continue;

                // Skip raid assets.
                if (raidIsRaidUnit != null)
                {
                    try
                    {
                        bool isRaid = Convert.ToBoolean(raidIsRaidUnit.Invoke(null, new object[] { unit }));
                        if (isRaid) continue;
                    }
                    catch { }
                }

                // Skip sea-invasion force members.
                if (sifGetReference != null)
                {
                    try
                    {
                        var sifRef = sifGetReference.Invoke(null, new object[] { unit });
                        if (sifRef != null) continue;
                    }
                    catch { }
                }

                Vector3 unitPos = ObjectPosition(unit);
                Theater unitTheater = TheaterClassifier.FromPosition(unitPos.x, unitPos.z);

                // Readiness step.
                float readinessStep = 2f;
                if (getReadiness != null)
                {
                    try
                    {
                        readinessStep = Convert.ToSingle(getReadiness.Invoke(null, new object[] { unit }));
                    }
                    catch { readinessStep = 2f; }
                }

                // Player-controlled check (DLC_WL; may not be present in non-W&L scenarios).
                bool playerControlled = false;
                try
                {
                    var isMovedByPlayer = AccessTools.Method(AccessTools.TypeByName("DLC_WL"), "IsMovedByPlayer");
                    if (isMovedByPlayer != null)
                        playerControlled = Convert.ToBoolean(isMovedByPlayer.Invoke(null, new object[] { unit }));
                }
                catch { }

                // Tier and distance-to-threat classification.
                CandidateTier tier = CandidateTier.CrossMap;
                float minDist = float.MaxValue;

                if (threatPositions.Count == 0)
                {
                    tier = CandidateTier.Local;
                    minDist = 0f;
                }
                else
                {
                    // Compute the method handle for GetXZDistance once per unit is fine
                    // since it's just a field cache lookup inside AccessTools.
                    for (int t = 0; t < threatPositions.Count; t++)
                    {
                        var (tx, tz, threatTheater) = threatPositions[t];
                        float dist = GetXZDistance(unitPos, new Vector3(tx, 0f, tz));
                        if (dist < minDist)
                        {
                            minDist = dist;
                            // Tier is derived from the closest threat.
                        }
                    }

                    // Derive tier from the closest threat.
                    for (int t = 0; t < threatPositions.Count; t++)
                    {
                        var (tx, tz, threatTheater) = threatPositions[t];
                        float dist = GetXZDistance(unitPos, new Vector3(tx, 0f, tz));
                        if (dist != minDist) continue;

                        // Use GamePrefs defOpsRadius for Local threshold — read once here to avoid
                        // re-reflecting in a tight loop; accept that we re-read from the field accessor.
                        float localRadius = ReadGamePrefsFloat("maxdistancedefensiveoperations", 250f);
                        if (dist < localRadius)
                        {
                            tier = CandidateTier.Local;
                        }
                        else if (unitTheater == threatTheater)
                        {
                            tier = CandidateTier.SameTheater;
                        }
                        else if (unitTheater != Theater.Unknown && threatTheater != Theater.Unknown &&
                                 TheaterAdjacency.TryGetValue(unitTheater, out var adj) &&
                                 adj.Contains(threatTheater))
                        {
                            tier = CandidateTier.AdjacentTheater;
                        }
                        else
                        {
                            tier = CandidateTier.CrossMap;
                        }
                        break;
                    }
                }

                int unitInstanceId = 0;
                try { unitInstanceId = ((UnityEngine.Object)unit).GetInstanceID(); } catch { }

                string unitName = null;
                try { unitName = ((UnityEngine.Object)unit).name; } catch { }

                int strength = ReadInt(unit, "groupstrengthactive", 0);
                float morale = ReadFloat(unit, "groupmorale", 1f);

                bool inOffensive = inOffensiveOps != null && inOffensiveOps.Contains(unit);

                input.Candidates.Add(new DefenseCandidate
                {
                    UnitInstanceId = unitInstanceId,
                    UnitName = unitName,
                    X = unitPos.x,
                    Z = unitPos.z,
                    ActiveStrength = (float)strength,
                    Morale = morale,
                    ReadinessStep = readinessStep,
                    Theater = unitTheater,
                    Tier = tier,
                    InOffensiveOperation = inOffensive,
                    PlayerControlled = playerControlled,
                    CriticalFront = false, // wired in a later slice
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

        // XZ-plane distance via Tools.GetXZDistance; falls back to manual calc on failure.
        private static float GetXZDistance(Vector3 a, Vector3 b)
        {
            try
            {
                var toolsType = AccessTools.TypeByName("Tools");
                var method = AccessTools.Method(toolsType, "GetXZDistance");
                if (method != null)
                    return Convert.ToSingle(method.Invoke(null, new object[] { a, b }));
            }
            catch { }
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
    }
}
