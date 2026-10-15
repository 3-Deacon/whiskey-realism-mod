using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Patches;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Strategic.Planner
{
    // Builds one alliance's CampaignSnapshot for the daily review (plan G0, P2) from one pass over
    // vanilla state. Reads only; writes nothing vanilla. Runtime adapter: not in the test csproj.
    //
    // Own formations mirror AIFaction.SeparateUnits (10458-10515): BattleUnits.campaignunitlist
    // (79974-79994; visibility state != 0, GetUnitVisibilityStateOnCampaignMap 82934-82974) units
    // of this alliance that are active, have no garrisonreference and are not fleets. Busy flags
    // come from the faction's operation, depot and capital lists and the vanilla helpers that
    // IsUnitAvailableForOffensiveOperations (14080-14158) consults (FortConstructionOrder
    // .UnitAlreadyConstructing 9785, SeaInvasionForce.GetSeaInvasionForceReference 9296,
    // RaidForce.IsRaidUnit 8937), MoveQueued from DefensiveMovingOrder.OrderRunning (9933).
    // FightingForce mirrors UnitIsFightingForce (16867) without its group-strength recompute.
    // Areas copy only the own-side faction-indexed fields (importancevalues, ownpoints) and
    // supplycapacity (UpdateSupplyCapacity 10772-10802), averageposition and zoneedges; per-faction
    // arrays are sized by alliance count but indexed by faction index (10679-10692), so every read
    // is bound-checked. Vanilla's area aggregates of the other side are never copied (spec C25).
    // Enemy units are not read here at all: G1's EnemyDetectionRuntime fills Detections.
    //
    // Objectives: one record per Town / IIP target of every active objective of either alliance
    // (CampaignObjective.allcampaignobjectives 178548; scenario match and IsDeactivated as
    // GetAvailableObjectives 178827-178866 filters; IDAccomplished 178805). Importance is vanilla's
    // own per-target term for this faction (SnapshotRules.TargetImportance, UpdateImportanceValues
    // 14905-14995), normalized to the snapshot's largest. Garrison strength only for own and allied
    // forts: an enemy fort's garrison reaches the side only as a G1 belief (spec C14/C25).
    // Supply sources: every IIP (owner kept; LogisticsGate accepts own, non-invisible ones) and the
    // supply depots of this alliance or an ally (UpdateIIPSupplyLink 114272-114306). Crossings: the
    // ford and bridge cells of the campaign terrain grid (BattlefieldSetup.rivercrossingpositions,
    // 26628-26638). Commander records are read once per alliance and review day.
    // Rails: BattleUnits.railroad with StartConstruction(alliance, checkonly: true) (77818-77845).
    // Commanders: this alliance's GameVars.commander records with the live getters at their default
    // arguments (as vanilla 14250 calls them), GetAverageAttributeValue (59175) and vanilla's AI
    // appointment pool CanBePickedForAIReplacement(alliance, true) (60748). Rank bands:
    // GetMin/MaxRankForUnittyp (67003-67058; checkforpromotion false, as CheckSuitableRank 60579).
    // Army groups: BattleUnits.armygroups of this alliance (G2 appointment targets).
    internal static class CampaignSnapshotRuntime
    {
        private const int FactoryId = 5, FoundryId = 8, IronworksId = 10, ManufactoryId = 12; // CBuilding ids 95107-95113

        private static FieldInfo _offensiveField, _defensiveField, _depotBuildersField, _capitalField, _followedField;
        private static Type _factionType;
        private static Weather _weather;
        private static BattlefieldSetup _bfs;
        private static readonly List<CommanderRecord>[] CommanderCache = { new List<CommanderRecord>(), new List<CommanderRecord>() };
        private static readonly int[] CommanderCacheDay = { -1, -1 };

        static CampaignSnapshotRuntime()
        {
            CampaignScope.Register("campaign-planner-snapshot-cache", ResetCaches);
        }

        private static void ResetCaches()
        {
            for (int a = 0; a < CommanderCache.Length; a++)
            {
                CommanderCache[a].Clear();
                CommanderCacheDay[a] = -1;
            }
            _weather = null;
            _bfs = null;
            Rails.Clear();
            RailSource.Clear();
            AreaShapes.Clear();
            AreaSource.Clear();
            CrossingCache.Clear();
            CrossingSource.Clear();
            PositionCache.Clear();
        }

        private static readonly HashSet<Regiment> OffensiveSet = new HashSet<Regiment>();
        private static readonly HashSet<Regiment> DefensiveSet = new HashSet<Regiment>();
        private static readonly HashSet<Regiment> DepotBuilderSet = new HashSet<Regiment>();
        private static readonly HashSet<Regiment> CapitalSet = new HashSet<Regiment>();
        private static readonly List<Regiment> OwnUnits = new List<Regiment>();
        private static readonly Dictionary<Regiment, int> OwnIndex = new Dictionary<Regiment, int>();
        private static readonly List<int> ParentSize = new List<int>();
        private static readonly HashSet<int> AvailableObjectives = new HashSet<int>();
        private static readonly HashSet<Town> IndustryTowns = new HashSet<Town>();
        private static readonly Dictionary<UnityEngine.Object, float> GarrisonAt = new Dictionary<UnityEngine.Object, float>();
        private static readonly List<float> RawImportance = new List<float>();

        // Static map facts, read once per campaign (perf review P1) and shared by both alliances:
        // area shapes (index, centre, zone edges; InitializeAIAreas 11251-11301), railroad geometry
        // (ImportRailroads 82506-82516), crossings, and the positions of map objects that never move
        // (towns, IIPs, depots). Each list-backed cache rebuilds when its vanilla list is replaced or
        // changes size (StaticSourceKey); none of them holds ownership, construction or importance,
        // which are read daily, so an ownership change invalidates nothing. Reset per campaign.
        private static readonly RailGeometry Rails = new RailGeometry();
        private static StaticSourceKey RailSource;
        private static readonly List<AreaRecord> AreaShapes = new List<AreaRecord>();
        private static StaticSourceKey AreaSource;
        private static readonly List<CrossingRecord> CrossingCache = new List<CrossingRecord>();
        private static StaticSourceKey CrossingSource;
        private static readonly Dictionary<Component, Vector3> PositionCache = new Dictionary<Component, Vector3>();

        internal static CampaignSnapshot Build(int alliance, int factionIndex, int daySerial, PlayerCommandClass player,
                                               in WlChainContext chain, DirectorPosture tempo, CampaignSnapshot reuse)
        {
            using (TelemetryPerf.Scope("campaign.planner.snapshot", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
                return BuildCore(alliance, factionIndex, daySerial, player, in chain, tempo, reuse);
        }

        private static CampaignSnapshot BuildCore(int alliance, int factionIndex, int daySerial, PlayerCommandClass player,
                                                  in WlChainContext chain, DirectorPosture tempo, CampaignSnapshot reuse)
        {
            var s = reuse ?? new CampaignSnapshot();
            int playerCommand = (UnityEngine.Object)chain.Command != null ? chain.Command.GetInstanceID() : -1;
            s.Reset(alliance, factionIndex, daySerial, player, playerCommand, GameVars.usefow, tempo);
            try
            {
                object faction = AICampaignReflect.GetFaction(factionIndex);
                if (faction == null || !ResolveFactionFields(faction.GetType()))
                {
                    OnceLog.Warning("campaign-planner:snapshot-degraded", "[CampaignPlanner] AIFaction lists not readable; snapshot left empty");
                    return s;
                }
                ReadScalars(s, alliance);
                ReadAreas(s, factionIndex);
                ReadFormations(s, alliance, faction, in chain);
                ReadObjectives(s, alliance, faction);
                ReadSupply(s, alliance);
                ReadRails(s, alliance);
                ReadCrossings(s);
                ReadCommanders(s, alliance);
                ReadRankBands(s, alliance);
                ReadArmyGroups(s, alliance);
            }
            catch (Exception ex)
            {
                s.Reset(alliance, factionIndex, daySerial, player, playerCommand, GameVars.usefow, tempo);
                OnceLog.Warning("campaign-planner:snapshot-degraded", "[CampaignPlanner] snapshot build failed, snapshot left empty: " + ex.Message);
            }
            return s;
        }

        // AIFaction is a private nested class (10299); its lists are public fields.
        private static bool ResolveFactionFields(Type factionType)
        {
            if (_factionType != factionType)
            {
                _factionType = factionType;
                _offensiveField = AccessTools.Field(factionType, "unitsinoffensiveoperations");
                _defensiveField = AccessTools.Field(factionType, "unitsindefensiveoperations");
                _depotBuildersField = AccessTools.Field(factionType, "unitsconstructingsupplydepots");
                _capitalField = AccessTools.Field(factionType, "groupstodefendcapital");
            }
            return _offensiveField != null && _defensiveField != null && _depotBuildersField != null && _capitalField != null;
        }

        private static void ReadScalars(CampaignSnapshot s, int alliance)
        {
            var alliances = GameVars.alliance;
            if (alliances != null)
            {
                if (alliance >= 0 && alliance < alliances.Length && (UnityEngine.Object)alliances[alliance].capital != null)
                {
                    Vector3 own = PositionOf(alliances[alliance].capital);
                    s.CapitalX = own.x;
                    s.CapitalZ = own.z;
                    s.HasCapital = true;
                }
                int enemy = 1 - alliance; // the planner runs for alliances 0 and 1 only
                if (enemy >= 0 && enemy < alliances.Length && (UnityEngine.Object)alliances[enemy].capital != null)
                {
                    Vector3 other = PositionOf(alliances[enemy].capital);
                    s.EnemyCapitalX = other.x;
                    s.EnemyCapitalZ = other.z;
                    s.HasEnemyCapital = true;
                }
            }
            s.RelocationRange = GamePrefs.aimaximumdistancetosearchforunitrelocations;
            s.TheaterHalfExtentX = GamePrefs.theatersize.x;
            s.TheaterHalfExtentZ = GamePrefs.theatersize.y;
            s.OffensiveDominance = GamePrefs.aidominanceforoffensiveoperations;
            s.MinStrengthForMovement = GamePrefs.aiminimumstrengthformovement;
            s.MinMoraleForMovement = GamePrefs.aiminimummoraleformovement;
        }

        // Daily: the faction-indexed importancevalues and ownpoints and the supplycapacity. The shape
        // (index, centre, edges) is static and comes from AreaShapes.
        private static void ReadAreas(CampaignSnapshot s, int f)
        {
            var areas = AICampaign.aiarea;
            if (areas == null) return;
            if (!AreaSource.Matches(areas, areas.Count)) ReadAreaShapes(areas);
            for (int i = 0; i < AreaShapes.Count; i++)
            {
                var record = AreaShapes[i];
                var area = areas[i];
                if (record.Index < 0 || area == null) continue;
                record.Importance = At(area.importancevalues, f);
                record.OwnPoints = At(area.ownpoints, f);
                record.SupplyCapacity = area.supplycapacity;
                s.Areas.Add(record);
            }
        }

        // One entry per aiarea index (Index -1 for a null area). The edge arrays are shared by every
        // snapshot until the next load; consumers only read them (SectorModel clones what it keeps).
        private static void ReadAreaShapes(List<AICampaign.AIArea> areas)
        {
            AreaShapes.Clear();
            AreaSource.Clear();
            for (int i = 0; i < areas.Count; i++)
            {
                var area = areas[i];
                if (area == null)
                {
                    AreaShapes.Add(new AreaRecord { Index = -1 });
                    continue;
                }
                var edges = area.zoneedges;
                int edgeCount = edges != null ? edges.Length : 0;
                var record = new AreaRecord
                {
                    Index = i,
                    X = area.averageposition.x,
                    Z = area.averageposition.z,
                    EdgeX = new float[edgeCount],
                    EdgeZ = new float[edgeCount]
                };
                for (int e = 0; e < edgeCount; e++)
                {
                    record.EdgeX[e] = edges[e].x;
                    record.EdgeZ[e] = edges[e].z;
                }
                AreaShapes.Add(record);
            }
            AreaSource.Set(areas, areas.Count);
        }

        private static void ReadFormations(CampaignSnapshot s, int alliance, object faction, in WlChainContext chain)
        {
            Fill(OffensiveSet, _offensiveField.GetValue(faction) as IList);
            Fill(DefensiveSet, _defensiveField.GetValue(faction) as IList);
            Fill(DepotBuilderSet, _depotBuildersField.GetValue(faction) as IList);
            Fill(CapitalSet, _capitalField.GetValue(faction) as IList);

            OwnUnits.Clear();
            OwnIndex.Clear();
            var units = BattleUnits.campaignunitlist;
            if (units == null) return;
            for (int i = 0; i < units.Count; i++)
            {
                var reg = units[i];
                if ((UnityEngine.Object)reg == null || reg.alliance != alliance || reg.unittyp == 17) continue;
                if ((UnityEngine.Object)reg.garrisonreference != null || !((Component)reg).gameObject.activeInHierarchy) continue;
                if (BattleUnits.GetUnitVisibilityStateOnCampaignMap(reg) == 0) continue;
                OwnIndex[reg] = OwnUnits.Count;
                OwnUnits.Add(reg);
            }

            bool fixPerkIds = FixPerkIds();
            bool career = DLC_WL.dlc_scenarioactive;
            Weather weather = ResolveWeather();
            for (int i = 0; i < OwnUnits.Count; i++)
            {
                var reg = OwnUnits[i];
                try
                {
                    Vector3 position = reg.GetPosition();
                    Vector3 waypoint = reg.GetLastWaypointPos();
                    int unitTyp = reg.unittyp;
                    bool hasDirectCorps = unitTyp == 16
                        && reg.GetAttachedUnitsReg(excludedechainedunits: false, excludeskirmishers: true, searchonlytype: 15, directonly: true, includenonactiveunits: false).Length != 0;

                    s.Own.Add(new FormationRecord
                    {
                        InstanceId = reg.GetInstanceID(),
                        UnitKey = SnapshotRules.UnitKey(((UnityEngine.Object)reg).name, reg.commander),
                        UnitTyp = unitTyp,
                        X = position.x,
                        Z = position.z,
                        WaypointX = waypoint.x,
                        WaypointZ = waypoint.z,
                        Strength = reg.groupstrengthdirect,
                        Morale = reg.groupmorale,
                        Supply01 = SnapshotRules.RawSupply01(reg.groupsupplystate, reg.supplystate),
                        ReadinessStep = CampaignArmyPanel.GetReadinessStep(reg),
                        CommandRange = reg.commanderrange,
                        BugleRange = reg.buglerange,
                        CommanderId = reg.commander,
                        HasTheater = reg.theaterposition != default(Vector3),
                        TheaterX = reg.theaterposition.x,
                        TheaterZ = reg.theaterposition.z,
                        AreaIndex = AreaIndexAt(s, position.x, position.z),
                        Flags = FlagsOf(reg, unitTyp, hasDirectCorps, s.MinStrengthForMovement),
                        Name = ((UnityEngine.Object)reg).name,
                        Chain = WlStrategicOrderBridge.ChainClassOf(in chain, alliance, reg),
                        CavalryShare = SnapshotRules.CavalryShare(reg.groupstrengthperunittypcampaigndirect),
                        SightRadius = SightRadiusOf(reg, weather, fixPerkIds, career),
                        ParentInstanceId = -1,
                        UseRails = reg.userails,
                        UseRivers = reg.userivers,
                        GroupSpeed = reg.groupspeed
                    });
                }
                catch (Exception ex)
                {
                    OwnIndex.Remove(reg);
                    OnceLog.Warning("campaign-planner:snapshot-unit-skipped", "[CampaignPlanner] a formation could not be read and was left out: " + ex.Message);
                }
            }
            ResolveParents(s);
        }

        // The radius that paints the unit's fog grid (FogOfWar.UpdateViewRange 101296-101309), the
        // same for the snapshot's formations and G1's viewer check (EnemyDetectionRuntime).
        internal static float SightRadiusOf(Regiment reg, Weather weather, bool fixPerkIds, bool career)
        {
            int unitTyp = reg.unittyp;
            float los = At(GamePrefs.maximumlineofsight, unitTyp);
            bool hasEight = reg.HasPerk(VanillaPerkTables.BattleExpertScouts);
            float eightFactor = hasEight ? At(GamePrefs.perk_expertscoutsvisibilityrangefactor, reg.GetPerkLevel(VanillaPerkTables.BattleExpertScouts)) : 1f;
            float climate = (UnityEngine.Object)weather != null ? weather.GetClimateData(((Component)reg).transform.position, 105) : 0f;
            float incident63 = career && reg.dlcw_isundercommander && DLC_WL.IsIncidentActive(63) ? GamePrefs.dlcwl_incidentscoutingaheadrange : 1f;
            return SightRadiusFormula.Campaign(unitTyp, los, hasEight, eightFactor, climate, GamePrefs.influenceofrainsnowonvisibilitycampaign,
                reg.liedown, GamePrefs.fow_lineofsightreduction_layndown, incident63, fixPerkIds);
        }

        private static FormationFlags FlagsOf(Regiment reg, int unitTyp, bool hasDirectCorps, float minStrength)
        {
            var flags = FormationFlags.None;
            if (reg.inbattle) flags |= FormationFlags.InBattle;
            if (reg.onretreat) flags |= FormationFlags.OnRetreat;
            if (OffensiveSet.Contains(reg)) flags |= FormationFlags.InOffensiveOps;
            if (DefensiveSet.Contains(reg)) flags |= FormationFlags.InDefensiveOps;
            if (CapitalSet.Contains(reg)) flags |= FormationFlags.CapitalDefense;
            if (DepotBuilderSet.Contains(reg)) flags |= FormationFlags.VanillaDepotBuilder;
            if (AICampaign.FortConstructionOrder.UnitAlreadyConstructing(reg)) flags |= FormationFlags.FortBuilder;
            if (AICampaign.SeaInvasionForce.GetSeaInvasionForceReference(reg) != null) flags |= FormationFlags.SeaInvasion;
            if (AICampaign.RaidForce.IsRaidUnit(reg)) flags |= FormationFlags.RaidForce;
            if (reg.transfertime > 0f) flags |= FormationFlags.InTransfer;
            if (SnapshotRules.IsEmbarked(reg.embarkmentstarttime, reg.currentmodeltype)) flags |= FormationFlags.Embarked;
            if (SnapshotRules.IsFightingForce(unitTyp, hasDirectCorps, reg.groupstrengthdirect, minStrength)) flags |= FormationFlags.FightingForce;
            if (reg.GetLastTransmittedPath() >= 0) flags |= FormationFlags.HasPath;
            if (AICampaign.DefensiveMovingOrder.OrderRunning(reg)) flags |= FormationFlags.MoveQueued;
            return flags;
        }

        // The battle core's D5 rule on the campaign: a formation's parent is the own formation whose
        // allattachedunits (every descendant, 119840+) contains it with the fewest entries. Replaces
        // any transform.parent walk (AGENTS.md).
        private static void ResolveParents(CampaignSnapshot s)
        {
            ParentSize.Clear();
            for (int i = 0; i < s.Own.Count; i++) ParentSize.Add(int.MaxValue);
            for (int g = 0; g < OwnUnits.Count; g++)
            {
                var group = OwnUnits[g];
                if (!OwnIndex.TryGetValue(group, out int groupIndex)) continue;
                var descendants = group.allattachedunits;
                if (descendants == null) continue;
                for (int k = 0; k < descendants.Length; k++)
                {
                    var child = descendants[k];
                    if ((UnityEngine.Object)child == null || child == group || !OwnIndex.TryGetValue(child, out int childIndex)) continue;
                    if (descendants.Length >= ParentSize[childIndex]) continue;
                    ParentSize[childIndex] = descendants.Length;
                    var record = s.Own[childIndex];
                    record.ParentInstanceId = s.Own[groupIndex].InstanceId;
                    s.Own[childIndex] = record;
                }
            }
        }

        private static int AreaIndexAt(CampaignSnapshot s, float x, float z)
        {
            int position = AreaGeometry.AreaAt(s.Areas, x, z);
            return position >= 0 ? s.Areas[position].Index : -1;
        }

        private static void ReadObjectives(CampaignSnapshot s, int alliance, object faction)
        {
            var objectives = CampaignObjective.allcampaignobjectives;
            if (objectives == null) return;

            AvailableObjectives.Clear();
            var available = CampaignObjective.GetAvailableObjectives(alliance);
            if (available != null)
                for (int i = 0; i < available.Count; i++)
                    if ((UnityEngine.Object)available[i] != null) AvailableObjectives.Add(available[i].UniqueObjectiveID);

            IndustryTowns.Clear();
            GarrisonAt.Clear();
            var buildings = BattleUnits.cbuildings;
            if (buildings != null)
            {
                for (int i = 0; i < buildings.Count; i++)
                {
                    var b = buildings[i];
                    if ((UnityEngine.Object)b == null) continue;
                    int type = b.BuildingType;
                    if ((type == FactoryId || type == FoundryId || type == IronworksId || type == ManufactoryId) && (UnityEngine.Object)b.closesttown != null)
                        IndustryTowns.Add(b.closesttown);
                }
            }
            var forts = BattleUnits.fort;
            if (forts != null)
            {
                for (int i = 0; i < forts.Count; i++)
                {
                    var fort = forts[i];
                    if ((UnityEngine.Object)fort == null) continue;
                    if (!SnapshotRules.SideKnowsGarrison(alliance, fort.Owner, AlliedWith(alliance, fort.Owner))) continue;
                    if ((UnityEngine.Object)fort.garrisonunit == null) continue;
                    float strength = fort.garrisonunit.groupstrengthdirect;
                    AddGarrison(fort.closesttown, strength);
                    AddGarrison((UnityEngine.Object)fort.parentiip != null ? fort.parentiip : fort.closestiip, strength);
                }
            }
            EnsureRailGeometry();

            int followed = FollowedObjective(faction);
            int f = s.FactionIndex;
            string scenario = GamePrefs.leveltoload;
            float townRadius = GamePrefs.radiustowncentertocapture;
            RawImportance.Clear();
            float maxImportance = 0f;
            for (int i = 0; i < objectives.Count; i++)
            {
                var o = objectives[i];
                if ((UnityEngine.Object)o == null || o.objectives == null) continue;
                if (o.ObjectiveScenario == null || !o.ObjectiveScenario.Contains(scenario) || o.IsDeactivated()) continue;
                int id = o.UniqueObjectiveID;
                bool accomplished = CampaignObjective.IDAccomplished(id);
                for (int t = 0; t < o.objectives.Count; t++)
                {
                    var record = new ObjectiveRecord { ObjectiveId = id, Available = AvailableObjectives.Contains(id), Accomplished = accomplished };
                    float importance;
                    if (o.objectives[t] is Town town && (UnityEngine.Object)town != null)
                    {
                        Vector3 p = PositionOf(town);
                        record.X = p.x;
                        record.Z = p.z;
                        record.HolderAlliance = town.Owner;
                        record.Roles = SnapshotRules.ObjectiveRoles(town.IsCapital, false, Rails.LinesNearTown(town.GetInstanceID(), p.x, p.z, townRadius),
                            IndustryTowns.Contains(town));
                        record.GarrisonStrength = GarrisonOf(town);
                        importance = SnapshotRules.TargetImportance(true, town.representingpopulation, 0f, AtWar(town.Owner, alliance),
                            CampaignObjective.IsPartOfObjective(followed, town), IsCapitalOfIndex(town, f),
                            GamePrefs.aiimportancepopulation, GamePrefs.aiimportancetransportcapacityweight,
                            GamePrefs.nonwarparticipantsobjectimportanceweighting, GamePrefs.aitownweightfactorobjective, GamePrefs.aiimportancevaluescapital);
                    }
                    else if (o.objectives[t] is IIP iip && (UnityEngine.Object)iip != null)
                    {
                        Vector3 p = PositionOf(iip);
                        record.X = p.x;
                        record.Z = p.z;
                        record.HolderAlliance = iip.allianceowner;
                        record.Roles = SnapshotRules.ObjectiveRoles(false, iip.IIPType == IIP.id_harbor, Rails.LinesOfIip(iip.GetInstanceID()), false);
                        record.GarrisonStrength = GarrisonOf(iip);
                        importance = SnapshotRules.TargetImportance(false, 0f, iip.GetTransportCapacity(), AtWar(iip.allianceowner, alliance),
                            CampaignObjective.IsPartOfObjective(followed, null, iip), false,
                            GamePrefs.aiimportancepopulation, GamePrefs.aiimportancetransportcapacityweight,
                            GamePrefs.nonwarparticipantsobjectimportanceweighting, GamePrefs.aitownweightfactorobjective, GamePrefs.aiimportancevaluescapital);
                    }
                    else continue;
                    importance = Math.Max(0f, importance);
                    maxImportance = Math.Max(maxImportance, importance);
                    RawImportance.Add(importance);
                    s.Objectives.Add(record);
                }
            }
            for (int i = 0; i < s.Objectives.Count; i++)
            {
                var record = s.Objectives[i];
                record.Importance01 = maxImportance > 0f ? RawImportance[i] / maxImportance : 0f;
                s.Objectives[i] = record;
            }
        }

        // GameVars.alliance[holder].IsAtWarWith(faction alliance), the order vanilla tests (14917, 14958).
        private static bool AtWar(int holder, int alliance)
        {
            var alliances = GameVars.alliance;
            return alliances != null && holder >= 0 && holder < alliances.Length && alliances[holder].IsAtWarWith(alliance);
        }

        private static bool AlliedWith(int alliance, int other)
        {
            var alliances = GameVars.alliance;
            return alliances != null && alliance >= 0 && alliance < alliances.Length && alliances[alliance].IsInAllianceWith(other);
        }

        // Vanilla adds the capital bonus at array index l for the capital of alliance l (14970-14976);
        // the array is faction-indexed, so faction index f gets alliance f's capital (f = 0, 1).
        private static bool IsCapitalOfIndex(Town town, int factionIndex)
        {
            var alliances = GameVars.alliance;
            return factionIndex >= 0 && factionIndex < 2 && alliances != null && factionIndex < alliances.Length
                && (UnityEngine.Object)alliances[factionIndex].capital == (UnityEngine.Object)town;
        }

        private static int FollowedObjective(object faction)
        {
            if (_followedField == null || _followedField.DeclaringType != faction.GetType())
                _followedField = AccessTools.Field(faction.GetType(), "followedcampaignobjective");
            return _followedField != null ? (int)_followedField.GetValue(faction) : -1;
        }

        private static void AddGarrison(UnityEngine.Object at, float strength)
        {
            if (at == null) return;
            GarrisonAt.TryGetValue(at, out float sum);
            GarrisonAt[at] = sum + Math.Max(0f, strength);
        }

        private static float GarrisonOf(UnityEngine.Object at)
        {
            return GarrisonAt.TryGetValue(at, out float strength) ? strength : 0f;
        }

        // Rebuilds the railroad geometry when BattleUnits.railroad is a new list or changed size.
        // Line i of Rails is BattleUnits.railroad[i]; a null line or one without IIPs is empty.
        private static void EnsureRailGeometry()
        {
            var lines = BattleUnits.railroad;
            int count = lines != null ? lines.Count : 0;
            if (RailSource.Matches(lines, count)) return;
            Rails.Clear();
            RailSource.Clear();
            if (lines == null) return;
            for (int i = 0; i < count; i++)
            {
                Rails.BeginLine();
                var iips = lines[i]?.scriptref != null ? lines[i].scriptref.IIPsForTransportCapacityUpdate : null;
                if (iips != null)
                {
                    for (int k = 0; k < iips.Length; k++)
                    {
                        if ((UnityEngine.Object)iips[k] == null) continue;
                        Vector3 p = PositionOf(iips[k]);
                        Rails.AddIip(iips[k].GetInstanceID(), p.x, p.z);
                    }
                }
                Rails.EndLine();
            }
            RailSource.Set(lines, count);
        }

        // Towns (capitals included), IIPs and buildings never move once placed (CBuilding.Place
        // 96188 runs before the building joins cbuildings): one transform read per object per
        // campaign. Typed so a moving unit cannot be cached. A destroyed depot's entry stays until
        // the campaign reset: bounded by the depots built in one campaign, and Unity's hash is the
        // instance id, so a stale entry never matches a live object.
        private static Vector3 PositionOf(Town town) => CachedStaticPosition(town);

        private static Vector3 PositionOf(IIP iip) => CachedStaticPosition(iip);

        private static Vector3 PositionOf(CBuilding building) => CachedStaticPosition(building);

        private static Vector3 CachedStaticPosition(Component c)
        {
            if (PositionCache.TryGetValue(c, out Vector3 p)) return p;
            p = c.transform.position;
            PositionCache[c] = p;
            return p;
        }

        private static void ReadSupply(CampaignSnapshot s, int alliance)
        {
            var iips = BattleUnits.iips;
            if (iips != null)
            {
                for (int i = 0; i < iips.Count; i++)
                {
                    var iip = iips[i];
                    if ((UnityEngine.Object)iip == null) continue;
                    Vector3 p = PositionOf(iip);
                    s.Supply.Add(new SupplySourceRecord
                    {
                        Id = iip.GetInstanceID(),
                        Kind = SupplySourceKind.Iip,
                        Owner = iip.allianceowner,
                        X = p.x,
                        Z = p.z,
                        TransportCapacity = iip.GetTransportCapacity(),
                        Condition = iip.condition,
                        Built = true,
                        Invisible = iip.IIPType == IIP.id_invisible
                    });
                }
            }

            var buildings = BattleUnits.cbuildings;
            var alliances = GameVars.alliance;
            if (buildings == null || alliances == null || alliance < 0 || alliance >= alliances.Length) return;
            for (int i = 0; i < buildings.Count; i++)
            {
                var depot = buildings[i];
                if ((UnityEngine.Object)depot == null || depot.BuildingType != CBuilding.id_supplydepot) continue;
                if (depot.Owner != alliance && !alliances[alliance].IsInAllianceWith(depot.Owner)) continue;
                Vector3 p = PositionOf(depot);
                s.Supply.Add(new SupplySourceRecord
                {
                    Id = depot.GetInstanceID(),
                    Kind = SupplySourceKind.Depot,
                    Owner = depot.Owner,
                    X = p.x,
                    Z = p.z,
                    Condition = depot.Condition,
                    Built = depot.constructiontimer <= 0f
                });
            }
        }

        private static void ReadRails(CampaignSnapshot s, int alliance)
        {
            var lines = BattleUnits.railroad;
            if (lines == null) return;
            EnsureRailGeometry();
            for (int i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                if (line == null) continue;
                var record = new RailLineRecord
                {
                    Index = i,
                    AIipId = -1,
                    BIipId = -1,
                    Startable = line.StartConstruction(alliance, checkonly: true),
                    UnderConstruction = line.constructionprogress > 0f && line.constructionprogress < 1f,
                    Progress = line.constructionprogress
                };
                Rails.CopyEndpoints(i, ref record);
                s.Rails.Add(record);
            }
        }

        private static void ReadCrossings(CampaignSnapshot s)
        {
            var bfs = ResolveBattlefieldSetup();
            var cells = (UnityEngine.Object)bfs != null ? bfs.rivercrossingpositions : null;
            if (cells == null) return;
            if (!CrossingSource.Matches(cells, cells.Count))
            {
                CrossingCache.Clear();
                for (int i = 0; i < cells.Count; i++)
                    CrossingCache.Add(new CrossingRecord { X = cells[i].x, Z = cells[i].z });
                CrossingSource.Set(cells, cells.Count);
            }
            s.Crossings.AddRange(CrossingCache);
        }

        // AICampaign's own lookup (11143).
        private static BattlefieldSetup ResolveBattlefieldSetup()
        {
            if ((UnityEngine.Object)_bfs != null) return _bfs;
            var go = GameObject.Find("GameController");
            _bfs = (UnityEngine.Object)go != null ? go.GetComponent<BattlefieldSetup>() : null;
            return _bfs;
        }

        // The four live attribute getters cost a parent-radius test each (61043-61101); one read per
        // alliance per review day is enough (appointments execute after the snapshot).
        private static void ReadCommanders(CampaignSnapshot s, int alliance)
        {
            if (alliance < 0 || alliance >= CommanderCache.Length)
            {
                ReadCommandersLive(s, alliance);
                return;
            }
            var cache = CommanderCache[alliance];
            if (CommanderCacheDay[alliance] == s.DaySerial)
            {
                s.Commanders.AddRange(cache);
                return;
            }
            cache.Clear();
            CommanderCacheDay[alliance] = -1;
            ReadCommandersLive(s, alliance);
            cache.AddRange(s.Commanders);
            CommanderCacheDay[alliance] = s.DaySerial;
        }

        private static void ReadCommandersLive(CampaignSnapshot s, int alliance)
        {
            var commanders = GameVars.commander;
            if (commanders == null) return;
            bool career = DLC_WL.dlc_scenarioactive;
            int chosen = DLC_WL.dlc_chosencommander;
            for (int id = 0; id < commanders.Count; id++)
            {
                var c = commanders[id];
                if (c == null || c.alliance != alliance) continue;
                try
                {
                    var command = c.currentcommand;
                    bool hasCommand = (UnityEngine.Object)command != null;
                    s.Commanders.Add(new CommanderRecord
                    {
                        Id = id,
                        Rank = c.rank,
                        Initiative = c.GetCommanderInitiative(),
                        Leadership = c.GetCommanderLeadership(),
                        Administration = c.GetCommanderAdministration(),
                        Average = c.GetAverageAttributeValue(),
                        Available = c.IsAvailable(),
                        Navy = c.historicalbranch == 4,
                        Dummy = c.usedummyphoto,
                        RefreshCommand = c.refreshcommand,
                        ServingUnderPlayer = c.IsServingUnderPlayer(),
                        IsPlayer = career && id == chosen,
                        Pickable = c.CanBePickedForAIReplacement(alliance, includeofficerswithcommand: true),
                        FreeApartFromArmyGroup = CommanderAppointmentRuntime.FreeApartFromArmyGroup(c, alliance, id),
                        CommandInstanceId = hasCommand ? command.GetInstanceID() : -1,
                        CommandUnitTyp = hasCommand ? command.unittyp : -1,
                        Name = c.combinedname
                    });
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("campaign-planner:snapshot-commander-skipped", "[CampaignPlanner] a commander could not be read and was left out: " + ex.Message);
                }
            }
        }

        private static void ReadRankBands(CampaignSnapshot s, int alliance)
        {
            for (int unitTyp = 10; unitTyp <= 18; unitTyp++)
            {
                s.RankBands.Add(new RankBand
                {
                    UnitTyp = unitTyp,
                    MinRank = GameVars.GetMinRankForUnittyp(unitTyp, alliance),
                    MaxRank = GameVars.GetMaxRankForUnittyp(unitTyp, alliance, checkforpromotion: false)
                });
            }
        }

        // This alliance's vanilla army groups (BattleUnits.armygroups; GetAlliance 75528 reads the
        // first member's alliance, else the commander's): commander, members, mean position and
        // summed groupstrengthdirect of the members.
        private static void ReadArmyGroups(CampaignSnapshot s, int alliance)
        {
            var groups = BattleUnits.armygroups;
            if (groups == null) return;
            for (int i = 0; i < groups.Count; i++)
            {
                var group = groups[i];
                if ((UnityEngine.Object)group == null || group.GetAlliance() != alliance) continue;
                var members = group.attachedunits;
                int count = 0;
                float x = 0f, z = 0f, strength = 0f;
                var ids = new int[members != null ? members.Count : 0];
                for (int m = 0; m < ids.Length; m++)
                {
                    var unit = members[m];
                    if ((UnityEngine.Object)unit == null) continue;
                    Vector3 p = unit.GetPosition();
                    x += p.x;
                    z += p.z;
                    strength += unit.groupstrengthdirect;
                    ids[count++] = unit.GetInstanceID();
                }
                if (count == 0) continue;
                if (count < ids.Length) Array.Resize(ref ids, count);
                s.ArmyGroups.Add(new ArmyGroupRecord
                {
                    InstanceId = group.GetInstanceID(),
                    CommanderId = group.GetCommanderID(),
                    X = x / count,
                    Z = z / count,
                    Strength = strength,
                    MemberInstanceIds = ids
                });
            }
        }

        // FogPerkSightPatch.Enabled: the perk-id sight fix applies with the plugin and the flag on.
        internal static bool FixPerkIds()
        {
            try
            {
                return Plugin.Instance != null && Plugin.Instance.Enabled.Value
                    && Plugin.FixPerkIdMismatches != null && Plugin.FixPerkIdMismatches.Value;
            }
            catch { return false; }
        }

        internal static Weather ResolveWeather()
        {
            if ((UnityEngine.Object)_weather != null) return _weather;
            var go = GameObject.Find("WeatherObj");
            _weather = (UnityEngine.Object)go != null ? go.GetComponent<Weather>() : null;
            return _weather;
        }

        private static void Fill(HashSet<Regiment> set, IList list)
        {
            set.Clear();
            if (list == null) return;
            for (int i = 0; i < list.Count; i++)
                if (list[i] is Regiment reg && (UnityEngine.Object)reg != null) set.Add(reg);
        }

        private static float At(float[] values, int index)
        {
            return values != null && index >= 0 && index < values.Length ? values[index] : 0f;
        }
    }
}
