using System;
using System.Collections.Generic;
using UnityEngine;
using WhiskeyRealism.Patches;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Strategic.Planner
{
    // The only Whiskey code that reads raw enemy campaign groups (plan G1, spec component 2). Runs
    // once per AI alliance per daily review from CampaignPlannerRuntime.ReviewPlan, before the
    // stand-down and bookkeeping returns: detection is bookkeeping, so beliefs keep updating for a
    // W&L CIC player's alliance and while vanilla's job machine is paused (no executor writes).
    // Runtime adapter: not in the test csproj; every decision is DetectionRules (pure): mode,
    // relation, grid answer (fail closed), rule a-d, garrison gate.
    //
    // Enemy groups mirror AIFaction.SeparateUnits (10458-10515): campaignunitlist (79974-79994)
    // entries at war with the side, active, visibility state != 0, no garrison reference, not
    // fleets. Each is detected when (DetectionRules.TryDecide):
    //   (a) GameVars.usefow is off or a fog-honest flag is off -> NoFog;
    //   (d) it shares a battle with an own formation (unittriggeredbattle, set for every unit of a
    //       battle by MoveUnitsToBFLocation 94063) or lies within an own formation's or its own
    //       bugle range (CheckEnemyContactCampaign 115634-115637) -> Contact;
    //   (c) the side is the player alliance (a W&L career under ai_vs_ai): !iswithinfog, vanilla's
    //       own verdict on fows[0] (127211-127225) -> OwnSideWithinFog;
    //   (b) any other side: an own or allied viewer (campaign visibility != 0, active, not routed,
    //       101325) has it inside the viewer's own sight radius (CampaignSnapshotRuntime
    //       .SightRadiusOf, the radius that paints the grid) and fows[1].UnitIsHidden with the
    //       127225 arguments says visible -> FogGrid. Every non-player alliance paints fows[1]
    //       (CreateFOWObjects 116505-116517), so the viewer check keeps a European-painted cell
    //       honest (C14). W&L incidents 60/61/64 are masked for these probes (101183-101185).
    // Enemy objective-fort garrisons (CBuilding.garrisonunit, 81985-82093) at enemy-held objective
    // targets (the snapshot's ObjectiveRecord.HolderAlliance) go through the same rule and become
    // one ObjectiveGarrison detection at the target.
    // Position is GetPosition() (never GetLastTransmittedPathPos), strength groupstrengthdirect.
    // Traits: Embarked (the G0 predicate), CavalryMajority (direct
    // strength per unit type, the array 14213 sums), OnOwnSoil (GetStateOfField -> nation alliance,
    // the test 13601-13603 uses) and NearOwnCoast (an own harbour IIP within relocation range).
    internal static class EnemyDetectionRuntime
    {
        private const int Alliances = 2;
        private static readonly int[] LastMode = { -1, -1 };
        private static readonly string[] LastSignature = new string[Alliances];
        private static readonly int[] LastDaySerial = { -1, -1 };
        private static readonly int[] OmniscientCacheDay = { int.MinValue, int.MinValue };
        // The calendar day of the current strategic review (ReviewCadenceCatchUp.DaySerial),
        // stamped by the coordinator before any alliance is reviewed, also with the planner off.
        private static int _reviewDay = -1;

        // Pooled per-review state.
        private static readonly List<Regiment> Enemies = new List<Regiment>();
        private static readonly HashSet<Regiment> OwnBattles = new HashSet<Regiment>();
        private static readonly List<Vector3> OwnHarbours = new List<Vector3>();
        private static readonly HashSet<(float, float)> EnemyHeldTargets = new HashSet<(float, float)>();
        private static float[] _viewerX = new float[64], _viewerZ = new float[64], _viewerR = new float[64];
        private static int _viewerCount;
        private static float[] _ownX = new float[64], _ownZ = new float[64], _ownBugle = new float[64];
        private static int _ownCount;

        // The probe's inputs for the masked loop (RunMasked takes one cached delegate).
        private static CampaignSnapshot _probeSnapshot;
        private static FogOfWar _probeGrid;
        private static bool _probeNoFog, _probeSidePlayer, _gridBroken;
        private static readonly Action ProbeEnemies = ProbeAllEnemies;
        private static readonly Func<Regiment, bool> HiddenProbe = IsHiddenFromGrid;

        private static FogOfWar[] _fows;
        private static readonly FieldMask<DLC_WL.Incident, float> IncidentMask = new FieldMask<DLC_WL.Incident, float>(
            i => i.launchedtime, (i, v) => i.launchedtime = v, PlayerIntelIncidents.NotLaunched);
        private static readonly DLC_WL.Incident[] IntelIncidents = new DLC_WL.Incident[PlayerIntelIncidents.Ids.Length];

        // Drift table for the alliance being updated (P6), read by the cached delegate.
        private static readonly float[] DriftTable = new float[32];
        private static readonly Func<int, float> DriftDelegate = unitTyp => unitTyp >= 0 && unitTyp < DriftTable.Length ? DriftTable[unitTyp] : 0f;
        // Normal road march per day (identity plausibility radius, F2), same table shape.
        private static readonly float[] MarchTable = new float[32];
        private static readonly Func<int, float> MarchDelegate = unitTyp => unitTyp >= 0 && unitTyp < MarchTable.Length ? MarchTable[unitTyp] : 0f;

        // Rollback lists (flags off), rebuilt once per campaign day.
        private static readonly List<EnemyBelief>[] Omniscient = { new List<EnemyBelief>(), new List<EnemyBelief>() };
        private static readonly Func<int, IReadOnlyList<EnemyBelief>> OmniscientDelegate = OmniscientBeliefs;

        static EnemyDetectionRuntime()
        {
            CampaignScope.Register("campaign-planner-detection", Reset);
        }

        private static void Reset()
        {
            for (int a = 0; a < Alliances; a++)
            {
                LastMode[a] = -1;
                LastSignature[a] = null;
                LastDaySerial[a] = -1;
                OmniscientCacheDay[a] = int.MinValue;
                Omniscient[a].Clear();
            }
            _reviewDay = -1;
            _fows = null;
            _probeSnapshot = null;
            _probeGrid = null;
        }

        // Both planner flags: the detection runs fog-honest, and readers get the model.
        internal static bool FogHonestFlagsOn()
        {
            return FlagOn(Plugin.EnableCampaignPlanner) && FlagOn(Plugin.EnableCampaignFogHonestBeliefs);
        }

        internal static EnemyArmyModel ModelFor(int alliance)
        {
            return EnemyBeliefStore.ModelFor(alliance);
        }

        // The migrated ledgers' enemy picture (plan G1 fallback, decided by the flags only): with
        // both flags on, inside a review job the model as the last ReviewPlan left it (a job before
        // today's Plan job takes P1's one-day lag), outside a job the beliefs of the last committed
        // review day (EnemyBeliefStore's published copy); with either off, vanilla's omniscient
        // enemyunits at full confidence, the documented rollback.
        internal static IReadOnlyList<EnemyBelief> BeliefsFor(int alliance)
        {
            return EnemyBeliefStore.Select(alliance, FlagOn(Plugin.EnableCampaignPlanner), FlagOn(Plugin.EnableCampaignFogHonestBeliefs), OmniscientDelegate);
        }

        internal static void StampReviewDay(int daySerial)
        {
            _reviewDay = daySerial;
        }

        // The review day the alliance's beliefs were last updated (the recency clock of the
        // order-raising threats; the rollback list carries the same day, so it is always recent).
        internal static int ReviewDayFor(int alliance)
        {
            return alliance >= 0 && alliance < Alliances ? LastDaySerial[alliance] : -1;
        }

        internal static void Detect(CampaignSnapshot s, int playerAlliance, bool fogHonest)
        {
            using (TelemetryPerf.Scope("campaign.planner.detect", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
                s.Detections.Clear();
                int side = s.Alliance;
                if (side < 0 || side >= Alliances) return;
                var alliances = GameVars.alliance;
                if (alliances == null || side >= alliances.Length) return;

                bool sidePlayer = side == playerAlliance;
                DetectionMode mode = DetectionRules.Mode(fogHonest, GameVars.usefow, sidePlayer);
                LastMode[side] = (int)mode;
                LastDaySerial[side] = s.DaySerial;

                CollectUnits(s, side);
                CollectOwnFormations(s);
                CollectHarbours(side);

                _probeSnapshot = s;
                _probeNoFog = DetectionRules.IsOmniscient(mode);
                _probeSidePlayer = sidePlayer;
                _probeGrid = mode == DetectionMode.FogGrid ? Grid(1) : null;
                _gridBroken = DetectionRules.GridUnavailable(mode, (UnityEngine.Object)_probeGrid != null);
                if (_gridBroken)
                    OnceLog.Warning("campaign-planner:detect-no-grid", "[EnemyBeliefs] fows[1] not found; this review detects by contact only");

                bool mask = PlayerIntelIncidents.Masks(mode == DetectionMode.FogGrid, DLC_WL.dlc_scenarioactive);
                if (mask) FillIntelIncidents();
                try
                {
                    DetectionRules.RunMasked(IncidentMask, IntelIncidents, mask, i => i.launchedtime, i => i == null,
                        ProbeEnemies, out string restoreFailure);
                    if (restoreFailure != null)
                        OnceLog.Warning("campaign-planner:incident-restore", "[EnemyBeliefs] W&L incident restore failed: " + restoreFailure);
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("campaign-planner:detect-failed", "[EnemyBeliefs] detection failed, this review keeps what it found: " + ex.Message);
                }
                finally
                {
                    Array.Clear(IntelIncidents, 0, IntelIncidents.Length);
                    _probeSnapshot = null;
                    _probeGrid = null;
                }
            }
        }

        // The drift table for the side's enemies (P6, the planning uncertainty) and the road-march
        // table (identity plausibility), both filled before the model update.
        internal static Func<int, float> DriftFor(int alliance)
        {
            Array.Clear(DriftTable, 0, DriftTable.Length);
            Array.Clear(MarchTable, 0, MarchTable.Length);
            try
            {
                var prefs = SpeedPrefs(alliance);
                for (int t = 0; t < DriftTable.Length; t++)
                {
                    DriftTable[t] = CampaignSpeedBound.DriftPerDay(in prefs, t);
                    MarchTable[t] = CampaignSpeedBound.RoadMarchPerDay(in prefs, t);
                }
            }
            catch (Exception ex)
            {
                OnceLog.Warning("campaign-planner:drift", "[EnemyBeliefs] speed prefs not readable, beliefs do not widen: " + ex.Message);
            }
            return DriftDelegate;
        }

        // The road-march table DriftFor filled for this update.
        internal static Func<int, float> MarchFor()
        {
            return MarchDelegate;
        }

        // [EnemyBeliefs] on change (signature-deduped per alliance).
        internal static void Report(CampaignSnapshot s, EnemyArmyModel model)
        {
            int side = s.Alliance;
            if (side < 0 || side >= Alliances || model == null) return;
            int contact = 0;
            for (int i = 0; i < s.Detections.Count; i++)
                if (s.Detections[i].Source == DetectionSource.Contact) contact++;
            int seen = s.Detections.Count;
            int remembered = 0;
            for (int i = 0; i < model.Beliefs.Count; i++)
                if (model.Beliefs[i].LastSeenDay != s.DaySerial) remembered++;
            int mode = LastMode[side];
            string line = "[EnemyBeliefs] alliance=" + side +
                " mode=" + (mode >= 0 ? ((DetectionMode)mode).ToString() : "None") +
                " seen=" + seen +
                " remembered=" + remembered +
                " expired=" + model.ExpiredKeys.Count +
                " contact=" + contact;
            if (string.Equals(LastSignature[side], line, StringComparison.Ordinal)) return;
            LastSignature[side] = line;
            TelemetryRouter.LegacyInfo(line, TelemetryLayer.Campaign);
        }

        private static void CollectUnits(CampaignSnapshot s, int side)
        {
            Enemies.Clear();
            OwnBattles.Clear();
            _viewerCount = 0;
            var units = BattleUnits.campaignunitlist;
            if (units == null) return;
            var alliances = GameVars.alliance;
            var sideState = alliances[side];
            Weather weather = CampaignSnapshotRuntime.ResolveWeather();
            bool fixPerkIds = CampaignSnapshotRuntime.FixPerkIds();
            bool career = DLC_WL.dlc_scenarioactive;
            for (int i = 0; i < units.Count; i++)
            {
                var reg = units[i];
                if ((UnityEngine.Object)reg == null || !((Component)reg).gameObject.activeInHierarchy) continue;
                int other = reg.alliance;
                if (other < 0 || other >= alliances.Length) continue;
                if (BattleUnits.GetUnitVisibilityStateOnCampaignMap(reg) == 0) continue;
                try
                {
                    var relation = DetectionRules.RelationOf(side, other, sideState.IsInAllianceWith(other), sideState.IsAtWarWith(other), reg.unittyp == 17, (UnityEngine.Object)reg.garrisonreference != null);
                    if (relation == UnitRelation.Viewer)
                    {
                        if (other == side && reg.inbattle && (UnityEngine.Object)reg.unittriggeredbattle != null)
                            OwnBattles.Add(reg.unittriggeredbattle);
                        if (!reg.isrouted) AddViewer(reg, weather, fixPerkIds, career);
                    }
                    else if (relation == UnitRelation.Enemy)
                    {
                        Enemies.Add(reg);
                    }
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("campaign-planner:detect-unit-skipped", "[EnemyBeliefs] a campaign group could not be read and was left out: " + ex.Message);
                }
            }
        }

        private static void AddViewer(Regiment reg, Weather weather, bool fixPerkIds, bool career)
        {
            if (_viewerCount == _viewerX.Length)
            {
                Array.Resize(ref _viewerX, _viewerCount * 2);
                Array.Resize(ref _viewerZ, _viewerCount * 2);
                Array.Resize(ref _viewerR, _viewerCount * 2);
            }
            Vector3 p = reg.GetPosition();
            _viewerX[_viewerCount] = p.x;
            _viewerZ[_viewerCount] = p.z;
            _viewerR[_viewerCount] = CampaignSnapshotRuntime.SightRadiusOf(reg, weather, fixPerkIds, career);
            _viewerCount++;
        }

        // Contact is judged against the snapshot's own formations (FormationRecord.BugleRange).
        private static void CollectOwnFormations(CampaignSnapshot s)
        {
            _ownCount = 0;
            if (_ownX.Length < s.Own.Count)
            {
                int size = Math.Max(s.Own.Count, _ownX.Length * 2);
                _ownX = new float[size];
                _ownZ = new float[size];
                _ownBugle = new float[size];
            }
            for (int i = 0; i < s.Own.Count; i++)
            {
                _ownX[_ownCount] = s.Own[i].X;
                _ownZ[_ownCount] = s.Own[i].Z;
                _ownBugle[_ownCount] = s.Own[i].BugleRange;
                _ownCount++;
            }
        }

        private static void CollectHarbours(int side)
        {
            OwnHarbours.Clear();
            var iips = BattleUnits.iips;
            if (iips == null) return;
            for (int i = 0; i < iips.Count; i++)
            {
                var iip = iips[i];
                if ((UnityEngine.Object)iip == null || iip.allianceowner != side || iip.IIPType != IIP.id_harbor) continue;
                OwnHarbours.Add(((Component)iip).transform.position);
            }
        }

        private static void ProbeAllEnemies()
        {
            var s = _probeSnapshot;
            int side = s.Alliance;
            for (int i = 0; i < Enemies.Count; i++)
            {
                var e = Enemies[i];
                try
                {
                    if (!Sees(s, e, out DetectionSource source)) continue;
                    Vector3 p = e.GetPosition();
                    var quality = QualityOf(e);
                    s.Detections.Add(new EnemyDetection
                    {
                        InstanceId = e.GetInstanceID(),
                        UnitKey = SnapshotRules.UnitKey(((UnityEngine.Object)e).name, e.commander),
                        UnitTyp = e.unittyp,
                        X = p.x,
                        Z = p.z,
                        Strength = e.groupstrengthdirect,
                        Source = source,
                        Traits = TraitsOf(e, p, side, s.RelocationRange),
                        RifleQuality = quality.Rifle,
                        GunQuality = quality.Gun
                    });
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("campaign-planner:detect-unit-skipped", "[EnemyBeliefs] a campaign group could not be read and was left out: " + ex.Message);
                }
            }
            ProbeObjectiveGarrisons(s, side);
        }

        // Enemy forts at enemy-held objective targets (G0 landing F2): the snapshot's objective
        // records (ObjectiveRecord.HolderAlliance at war with the side; public ownership) name the
        // targets; the garrison passes the same rule, then one ObjectiveGarrison detection at the
        // target it guards (the fort's closest town, else its parent or closest IIP, the mapping
        // the snapshot uses for own garrisons).
        private static void ProbeObjectiveGarrisons(CampaignSnapshot s, int side)
        {
            var forts = BattleUnits.fort;
            if (forts == null) return;
            var sideState = GameVars.alliance[side];
            EnemyHeldTargets.Clear();
            for (int i = 0; i < s.Objectives.Count; i++)
            {
                int holder = s.Objectives[i].HolderAlliance;
                if (holder >= 0 && holder != side && sideState.IsAtWarWith(holder))
                    EnemyHeldTargets.Add((s.Objectives[i].X, s.Objectives[i].Z));
            }
            if (EnemyHeldTargets.Count == 0) return;
            for (int i = 0; i < forts.Count; i++)
            {
                var fort = forts[i];
                try
                {
                    if ((UnityEngine.Object)fort == null || !sideState.IsAtWarWith(fort.Owner)) continue;
                    if (!TryObjectiveTargetOf(fort, out Vector3 at)) continue;
                    var garrison = fort.garrisonunit;
                    if ((UnityEngine.Object)garrison == null || !((Component)garrison).gameObject.activeInHierarchy) continue;
                    if (DetectionRules.TryObjectiveGarrison(Sees(s, garrison, out _), garrison.GetInstanceID(),
                            SnapshotRules.UnitKey(((UnityEngine.Object)garrison).name, garrison.commander), garrison.unittyp,
                            at.x, at.z, garrison.groupstrengthdirect, out EnemyDetection detection))
                    {
                        var quality = QualityOf(garrison);
                        detection.RifleQuality = quality.Rifle;
                        detection.GunQuality = quality.Gun;
                        s.Detections.Add(detection);
                    }
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("campaign-planner:detect-garrison-skipped", "[EnemyBeliefs] a fort garrison could not be read and was left out: " + ex.Message);
                }
            }
        }

        private static bool TryObjectiveTargetOf(CBuilding fort, out Vector3 at)
        {
            if ((UnityEngine.Object)fort.closesttown != null)
            {
                at = ((Component)fort.closesttown).transform.position;
                if (EnemyHeldTargets.Contains((at.x, at.z))) return true;
            }
            var iip = (UnityEngine.Object)fort.parentiip != null ? fort.parentiip : fort.closestiip;
            if ((UnityEngine.Object)iip != null)
            {
                at = ((Component)iip).transform.position;
                if (EnemyHeldTargets.Contains((at.x, at.z))) return true;
            }
            at = default(Vector3);
            return false;
        }

        private static bool Sees(CampaignSnapshot s, Regiment e, out DetectionSource source)
        {
            Vector3 p = e.GetPosition();
            bool contact = InBattleWithSide(e) || DetectionRules.InBugleContact(p.x, p.z, e.buglerange, _ownX, _ownZ, _ownBugle, _ownCount);
            bool covers = !_probeNoFog && !contact && !_probeSidePlayer && ViewerCoverage.Covers(p.x, p.z, _viewerX, _viewerZ, _viewerR, _viewerCount);
            bool grid = DetectionRules.NeedsGridProbe(_probeNoFog, contact, _probeSidePlayer, covers) && GridVisible(e);
            bool ownSide = _probeSidePlayer && !_probeNoFog && !e.iswithinfog;
            return DetectionRules.TryDecide(_probeNoFog, contact, _probeSidePlayer, ownSide, covers, grid, out source);
        }

        private static bool InBattleWithSide(Regiment e)
        {
            return e.inbattle && (UnityEngine.Object)e.unittriggeredbattle != null && OwnBattles.Contains(e.unittriggeredbattle);
        }

        // Rule (b)'s grid answer through DetectionRules.GridVisible: usefow gated (101188 NRE), a
        // missing or broken grid answers not visible, a probe exception fails closed for the rest
        // of the review (contact only), never omniscient.
        private static bool GridVisible(Regiment e)
        {
            bool visible = DetectionRules.GridVisible(GameVars.usefow, !_gridBroken && (UnityEngine.Object)_probeGrid != null, HiddenProbe, e, out bool threw);
            if (threw)
            {
                _gridBroken = true;
                OnceLog.Warning("campaign-planner:detect-grid-failed", "[EnemyBeliefs] fog-grid probe threw, the rest of this review detects by contact only");
            }
            return visible;
        }

        // fows[1].UnitIsHidden with CheckRegimentVisibility's arguments (127225); commander and
        // unit type bound-checked (-1 skips the term, as vanilla's defaults do). Called only through
        // DetectionRules.GridVisible (HiddenProbe).
        private static bool IsHiddenFromGrid(Regiment e)
        {
            Vector3 p = e.GetPosition();
            var commanders = GameVars.commander;
            int c = e.commander;
            float cunning = commanders != null && c >= 0 && c < commanders.Count && commanders[c] != null ? commanders[c].GetCommanderCunning() : -1f;
            var sizes = GamePrefs.unittypesize;
            int t = e.unittyp;
            bool typeKnown = sizes != null && t >= 0 && t < sizes.Length && sizes[t] > 0;
            float relative = typeKnown ? (float)e.strength / (float)sizes[t] : -1f;
            return _probeGrid.UnitIsHidden(p, cunning, relative, e.liedown, e.coverobject, e.covervalue, typeKnown ? t : -1, e);
        }

        private static EnemyTraits TraitsOf(Regiment e, Vector3 p, int side, float relocationRange)
        {
            bool embarked = SnapshotRules.IsEmbarked(e.embarkmentstarttime, e.currentmodeltype);
            float cavalry = SnapshotRules.CavalryShare(e.groupstrengthperunittypcampaigndirect);
            int state = BattlefieldSetup.GetStateOfField(p);
            var nations = GameVars.nation;
            bool onOwnSoil = nations != null && state >= 0 && state < nations.Length && nations[state].alliance == side;
            bool nearCoast = false;
            float rangeSq = relocationRange * relocationRange;
            for (int i = 0; i < OwnHarbours.Count && !nearCoast; i++)
            {
                float dx = OwnHarbours[i].x - p.x;
                float dz = OwnHarbours[i].z - p.z;
                nearCoast = dx * dx + dz * dz <= rangeSq;
            }
            return DetectionRules.Traits(embarked, cavalry, onOwnSoil, nearCoast);
        }

        // What a sighting shows of a group's arms (G9): vanilla's per-model WSF (GetWeaponStrengthFactor 16214) of each armed unit of the
        // group (unittyp <= 13, weapon >= 0: the test of the group reading, 16195-16212), weighted by the unit's strength and kept apart
        // by the model's lane (isari). Read at the sighting only, from the group the side sees; a failed read records nothing for the
        // group (its sighting is kept).
        private static SightedQuality QualityOf(Regiment group)
        {
            var quality = default(SightedQuality);
            try
            {
                var units = group.allattachedunits;
                var weapons = GameVars.weapon;
                if (units == null || weapons == null) return quality;
                for (int i = 0; i < units.Length; i++)
                {
                    var unit = units[i];
                    if ((UnityEngine.Object)unit == null || unit.unittyp > 13 || unit.weapon < 0 || unit.weapon >= weapons.Length) continue;
                    quality.Add(weapons[unit.weapon].isari, AICampaign.GetWeaponStrengthFactor(unit.weapon), unit.strength);
                }
            }
            catch (Exception ex)
            {
                OnceLog.Warning("campaign-planner:detect-quality", "[EnemyBeliefs] a group's weapon quality could not be read and was left out: " + ex.Message);
                return default(SightedQuality);
            }
            return quality;
        }

        private static void FillIntelIncidents()
        {
            var incidents = DLC_WL.incidents;
            int[] ids = PlayerIntelIncidents.Ids;
            for (int i = 0; i < ids.Length; i++)
                IntelIncidents[i] = incidents != null && ids[i] >= 0 && ids[i] < incidents.Count ? incidents[ids[i]] : null;
        }

        // GameController's FogOfWar components (CreateFOWObjects 116507): [0] the player alliance's,
        // [1] every other alliance's.
        private static FogOfWar Grid(int index)
        {
            if (_fows == null || _fows.Length <= index || (UnityEngine.Object)_fows[index] == null)
            {
                var go = GameObject.Find("GameController");
                _fows = (UnityEngine.Object)go != null ? go.GetComponents<FogOfWar>() : null;
            }
            return _fows != null && _fows.Length > index ? _fows[index] : null;
        }

        // The rollback (either flag off): vanilla's own omniscient enemyunits (SeparateUnits) at
        // full confidence, with the same traits a sighting would carry. The only enemyunits read.
        // Rebuilt once per review day, keyed by the coordinator's calendar stamp (not by
        // currenttimefromstart x 365.25: 30171 makes months 1/12 year wide, so 31 Jan and 1 Feb
        // floor to the same day).
        private static IReadOnlyList<EnemyBelief> OmniscientBeliefs(int alliance)
        {
            var list = Omniscient[alliance];
            int campaignDay = _reviewDay;
            if (OmniscientCacheDay[alliance] == campaignDay) return list;
            OmniscientCacheDay[alliance] = campaignDay;
            list.Clear();
            try
            {
                object faction = null;
                for (int i = 0; ; i++)
                {
                    object candidate = AICampaignReflect.GetFaction(i);
                    if (candidate == null) break;
                    if (AICampaignReflect.GetAllianceId(i) != alliance) continue;
                    faction = candidate;
                    break;
                }
                var enemies = faction != null ? VanillaFieldCache.List(faction, "enemyunits") : null;
                if (enemies == null) return list;
                CollectHarbours(alliance);
                float relocation = GamePrefs.aimaximumdistancetosearchforunitrelocations;
                int day = campaignDay;
                for (int i = 0; i < enemies.Count; i++)
                {
                    if (!(enemies[i] is Regiment e) || (UnityEngine.Object)e == null) continue;
                    Vector3 p = e.GetPosition();
                    var traits = TraitsOf(e, p, alliance, relocation);
                    var quality = QualityOf(e);
                    list.Add(new EnemyBelief
                    {
                        InstanceId = e.GetInstanceID(),
                        UnitKey = SnapshotRules.UnitKey(((UnityEngine.Object)e).name, e.commander),
                        UnitTyp = e.unittyp,
                        X = p.x,
                        Z = p.z,
                        Strength = e.groupstrengthdirect,
                        LastSeenDay = day,
                        Confidence01 = 1f,
                        UncertaintyRadius = EnemyArmyModel.SeenRadius,
                        LastSource = DetectionSource.Seen,
                        Traits = traits,
                        LastEmbarkedDay = (traits & EnemyTraits.Embarked) != 0 ? day : -1,
                        RifleQuality = quality.Rifle,
                        GunQuality = quality.Gun
                    });
                }
            }
            catch (Exception ex)
            {
                OnceLog.Warning("campaign-planner:omniscient-rollback", "[EnemyBeliefs] rollback enemy list not readable: " + ex.Message);
            }
            return list;
        }

        // Vanilla's speed terms for P6's bound; railroadspeedadj is the fastest of the alliances at
        // war with the side (a scalar per alliance, 213217).
        private static CampaignSpeedPrefs SpeedPrefs(int alliance)
        {
            var basic = GamePrefs.unitbasicspeed;
            int types = basic != null ? basic.GetLength(0) : 0;
            var mounted = new float[types];
            for (int t = 0; t < types; t++)
                mounted[t] = basic.GetLength(1) > 1 ? basic[t, 1] : 0f;
            float rail = 1f;
            var alliances = GameVars.alliance;
            if (alliances != null && alliance >= 0 && alliance < alliances.Length)
                for (int a = 0; a < alliances.Length; a++)
                    if (a != alliance && alliances[alliance].IsAtWarWith(a) && alliances[a].railroadspeedadj > rail) rail = alliances[a].railroadspeedadj;
            var costs = GamePrefs.terrainspeccost;
            var cost = new float[costs != null ? costs.Length : 0];
            for (int k = 0; k < cost.Length; k++) cost[k] = costs[k];
            return new CampaignSpeedPrefs
            {
                UnitBasicSpeedMounted = mounted,
                GroupSpeedCeiling = types > 15 && basic.GetLength(1) > 1 ? basic[15, 1] : 0f,
                SkilledCartographers = GamePrefs.movementspeedskilledcartographers,
                FlyingColumn = GamePrefs.movementspeedflyingcolumn,
                FootCavalry = GamePrefs.forcedmarchspeedfootcavalry,
                RiverExpedition = GamePrefs.rivermovementspeedriverexpedition,
                DoubleQuick = GamePrefs.doublequickspeedfact,
                Running = GamePrefs.runningspeedfact,
                RailroadSpeedAdj = rail,
                BasicUnitSpeed = GamePrefs.basicunitspeed,
                TerrainSpecCost = cost,
                GeneralCampaignFactor = GamePrefs.generalcampaignunitspeedfactor,
                SpeedFactorRetreat = GamePrefs.speedfactorretreat,
                SpeedFactorFallback = GamePrefs.speedfactorfallback,
                FallbackCheat = GameVars.debug_aicheat_fasterfallbackspeed
            };
        }

        private static bool FlagOn(BepInEx.Configuration.ConfigEntry<bool> entry)
        {
            try { return entry != null && entry.Value; }
            catch { return false; }
        }
    }
}
