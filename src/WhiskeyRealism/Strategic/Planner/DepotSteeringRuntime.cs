using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Strategic.Construction;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Strategic.Planner
{
    // The construction steering executor's vanilla side (plan G5c, spec C10-C12). Runs only inside ReviewExecute, after the pre-war
    // gate, for the alliance the planner stands in for, and does two things:
    //
    //   Depots   DepotTaskLedger (pure) decides; this class places the wish. A DepotBuilder the mover has marched to a task's site
    //            gets CBuilding.AddConstructionWish(CBuilding.id_supplydepot, its position, nearestOwnIip, alliance) once, on
    //            arrival (97479-97536). Place charges the treasury with no funds check (AdjustTreasury 96211-96257; depots have
    //            subsidytype -1, so never a subsidy lane); affordability is the construction ledger's verdict on the task, taken
    //            before the task starts. The returned CBuilding is the placeholder at -20000: WorkDownConstructionWishes replaces it
    //            with a new building on suitable ground (97538-97591), so "built" is read from the snapshot's depots, not from it.
    //   Board    the rail line scores and the fort site preferences the two construction patches read (ConstructionSteeringBoard),
    //            built from today's snapshot and plan and swapped in whole.
    //
    // Nothing here is a Harmony patch. Vanilla's own depot job (19, CheckSupplyDepotConstruction 14659-14779) is unchanged; it keeps
    // wishing for starving units, and its two bad branches belong to the bug-fix slice.
    internal static class DepotSteeringRuntime
    {
        // AICampaign.fortconstructionsites is built once at scenario setup and never changed (81898-81942): its positions are
        // copied to arrays once per list instance.
        private static List<Vector3> _fortSource;
        private static int _fortSourceCount = -1;
        private static float[] _fortX = new float[0];
        private static float[] _fortZ = new float[0];

        static DepotSteeringRuntime()
        {
            CampaignScope.Register("campaign-planner-fort-site-copy", ResetSites);
        }

        private static void ResetSites()
        {
            _fortSource = null;
            _fortSourceCount = -1;
            _fortX = new float[0];
            _fortZ = new float[0];
        }

        // Its own try/catch: this runs ahead of G5a's steering and the mover inside ReviewExecute's single try, so a fault here
        // (a ledger bug, a vanilla read) would otherwise idle the mover for the review. The next review starts afresh.
        internal static void Execute(int alliance, CampaignSnapshot snapshot)
        {
            using (TelemetryPerf.Scope("campaign.planner.construction", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
                try
                {
                    Run(alliance, snapshot);
                }
                catch (Exception ex)
                {
                    OnceLog.Warning("campaign-planner:construction-executor", "[CampaignPlanner] the construction steering executor failed for this review (the mover and the other executors still run): " + ex.Message);
                }
            }
        }

        private static void Run(int alliance, CampaignSnapshot snapshot)
        {
            if (snapshot == null || alliance < 0 || alliance > 1) return;
            if (!CampaignPlanStore.TryGetPlan(alliance, out var plan) || !CampaignPlanStore.TryGetAllocation(alliance, out var allocation)) return;
            PublishBoard(alliance, snapshot, plan);
            var ledger = DepotTaskLedgers.For(alliance);
            if (!FlagOn(Plugin.EnableSupplyDepotSteering))
            {
                ledger.AbandonActive("flag-off", snapshot.DaySerial);
                EmitTransitions(alliance, ledger);
                return;
            }
            ledger.Sync(plan, allocation, snapshot, EvaluateDepots(alliance));
            PlaceWishesAndWatchBuilds(alliance, snapshot, ledger);
            EmitTransitions(alliance, ledger);
        }

        // The construction ledger's verdict on today's depot tasks (null when it cannot be taken: nothing is suppressed).
        private static ConstructionOutput EvaluateDepots(int alliance)
        {
            try
            {
                var coordinator = StrategicCoordinator.Instance;
                if (coordinator == null) return null;
                var input = ConstructionRuntime.BuildDepotInput(alliance, At(coordinator.FiscalIntents, alliance),
                    At(coordinator.Fronts, alliance), At(coordinator.FormationDirectives, alliance));
                return ConstructionIntentLedger.Compute(input, new ConstructionOptions());
            }
            catch (Exception ex)
            {
                OnceLog.Warning("campaign-planner:depot-ledger", "[DepotTask] the construction ledger's verdict could not be taken (nothing suppressed): " + ex.Message);
                return null;
            }
        }

        private static T At<T>(T[] array, int index) where T : class
        {
            return array != null && index >= 0 && index < array.Length ? array[index] : null;
        }

        private static void PlaceWishesAndWatchBuilds(int alliance, CampaignSnapshot snapshot, DepotTaskLedger ledger)
        {
            // The wish is placed on the nearest suitable ground to the builder, up to buildingsitemaxradius away (97557-97585), and the
            // builder may stand up to the mover's arrival radius from the site.
            float builtRadius = GamePrefs.buildingsitemaxradius + CampaignMover.ArrivalRadius;
            bool haveSpeed = false;
            MarchSpeedInputs speed = default(MarchSpeedInputs);
            for (int t = 0; t < ledger.Tasks.Count; t++)
            {
                var task = ledger.Tasks[t];
                if (task.State == DepotTaskState.Wished)
                {
                    ledger.WatchWish(t, snapshot, builtRadius);
                    continue;
                }
                if (task.State != DepotTaskState.Marching) continue;
                int i = snapshot.IndexOfOwn(task.BuilderInstanceId);
                if (i < 0) continue;
                var builder = snapshot.Own[i];
                // Arrived: within the builder's bugle range of the site, or where the mover considers a formation there
                // (CampaignMover.ArrivalRadius: a site that moved a little with the frontier is not re-marched to).
                float reach = Math.Max(builder.BugleRange, CampaignMover.ArrivalRadius);
                if (!haveSpeed)
                {
                    speed = TerrainLineRuntime.ReadMarchSpeed(alliance);
                    haveSpeed = true;
                }
                float days = MarchTime.EstimateDays(speed, builder, task.X, task.Z, Distance(builder.X, builder.Z, task.X, task.Z), snapshot, null);
                var facts = new DepotWishFacts
                {
                    DaySerial = snapshot.DaySerial,
                    MarchIssuedEarlier = MoveTrackers.For(alliance).MarchingToward(builder.InstanceId, task.X, task.Z, DepotTaskLedger.SiteMatchRadius, snapshot.DaySerial)
                };
                facts.OwnUnbuiltDepotAtSite = DepotTaskLedger.NearestOwnDepot(snapshot, task, builtRadius, true, builder.X, builder.Z, builder.BugleRange, out facts.AdoptX, out facts.AdoptZ);
                switch (ledger.WishMoment(t, builder, reach, days, facts))
                {
                    case DepotWishMoment.Here: Wish(alliance, snapshot, ledger, t, task, builder.X, builder.Z, builder.InstanceId); break;
                    case DepotWishMoment.Ahead: Wish(alliance, snapshot, ledger, t, task, task.X, task.Z, builder.InstanceId); break;
                    case DepotWishMoment.Adopt: ledger.RecordWished(t, snapshot.DaySerial, facts.AdoptX, facts.AdoptZ, "vanilla-depot-adopted"); break;
                }
            }
        }

        private static float Distance(float ax, float az, float bx, float bz)
        {
            float dx = ax - bx, dz = az - bz;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }

        // One wish per task (P8). The IIP is the nearest own one without a building under construction: a wish at an IIP that has
        // one would return that building (97481-97484) and place nothing, so the wish waits for the next review; AddConstructionWish
        // returns null when the IIP is null or companyfoundings has reached debug_maxcompanyfoundings (97489-97497), which abandons
        // the task rather than recording a wish that does not exist.
        private static void Wish(int alliance, CampaignSnapshot snapshot, DepotTaskLedger ledger, int taskIndex, DepotTaskRecord task, float x, float z, int builderInstanceId)
        {
            IIP iip = ConstructionRuntime.NearestOwnIip(alliance, task.X, task.Z);
            if ((UnityEngine.Object)iip == null) return;
            var unit = CampaignSnapshotRuntime.OwnUnit(builderInstanceId);
            float y = unit != null ? unit.GetPosition().y : 0f;
            // The wish position is the builder's when it is there, as vanilla's own depot wish passes the unit's (14766-14770): the depot
            // rises on the nearest suitable ground to the formation that has to stay within its bugle range of it. A builder still on
            // the march (WishMoment.Ahead) wishes at the site it is marching to.
            CBuilding building = CBuilding.AddConstructionWish(CBuilding.id_supplydepot, new Vector3(x, y, z), iip, alliance);
            if ((UnityEngine.Object)building == null)
            {
                ledger.Abandon(taskIndex, "wish-refused", snapshot.DaySerial);
                return;
            }
            ledger.RecordWished(taskIndex, snapshot.DaySerial, x, z);
        }

        private static void EmitTransitions(int alliance, DepotTaskLedger ledger)
        {
            var transitions = ledger.Transitions;
            for (int k = 0; k < transitions.Count; k++)
            {
                var t = transitions[k];
                TelemetryRouter.LegacyInfo("[DepotTask] alliance=" + alliance + " site=" + Mathf.RoundToInt(t.X) + "," + Mathf.RoundToInt(t.Z)
                    + " builder=" + TelemetryLegacyParser.Quote(t.BuilderUnitKey ?? "none") + " state=" + t.State + " reason=" + t.Reason, TelemetryLayer.Campaign);
            }
            ledger.ClearTransitions();
        }

        // The rail line scores and fort site preferences for today (ConstructionSteeringBoard). Built every review, flags
        // regardless: the patches test their own flag.
        private static void PublishBoard(int alliance, CampaignSnapshot snapshot, TheaterPlan plan)
        {
            try
            {
                CampaignPlanStore.TryGetSectors(alliance, out var sectors);
                var coordinator = StrategicCoordinator.Instance;
                var construction = coordinator != null ? At(coordinator.ConstructionIntents, alliance) : null;
                ReadFortSites();
                ConstructionSteeringBoard.Publish(alliance, ConstructionSteeringBoard.Build(
                    snapshot, plan, sectors, construction, _fortX, _fortZ, GamePrefs.rangefortconstructionsite));
            }
            catch (Exception ex)
            {
                OnceLog.Warning("campaign-planner:construction-board", "[CampaignPlanner] the construction steering board could not be built (patches read none): " + ex.Message);
            }
        }

        private static void ReadFortSites()
        {
            var sites = VanillaFieldCache.Field(typeof(AICampaign), "fortconstructionsites")?.GetValue(null) as List<Vector3>;
            if (sites == null || (ReferenceEquals(sites, _fortSource) && sites.Count == _fortSourceCount)) return;
            _fortX = new float[sites.Count];
            _fortZ = new float[sites.Count];
            for (int i = 0; i < sites.Count; i++)
            {
                _fortX[i] = sites[i].x;
                _fortZ[i] = sites[i].z;
            }
            _fortSource = sites;
            _fortSourceCount = sites.Count;
        }

        private static bool FlagOn(BepInEx.Configuration.ConfigEntry<bool> entry)
        {
            try { return entry != null && entry.Value; }
            catch { return false; }
        }
    }
}
