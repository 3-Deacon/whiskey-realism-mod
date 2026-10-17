using System;
using System.Collections;
using System.Collections.Generic;
using WhiskeyRealism.Patches;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Strategic.Planner
{
    // The vanilla side of the ordnance steering (plan G9), for RearmPriorityPatch and OrdnancePurchasePatch. The decisions are
    // RearmPriority's and OrdnancePicker's (pure, tested); this reads the alliance's units, the allocation and vanilla's weapon
    // facts, and logs. It writes only vanilla's unit-list order (restored after the call) and, through the patch, the weapon id of
    // one order vanilla is placing. Fog: the only averageweaponstrength read is the steering alliance's OWN lane average, the
    // candidate test vanilla itself applies (16298, 16315); the enemy's arms are the beliefs' lane qualities, recorded at sightings.
    internal static class OrdnanceRuntime
    {
        private const int Alliances = 2;
        private const int FactionSlots = 4;
        private static readonly string[] LastRearmRow = new string[Alliances];

        // Pooled per call.
        private static readonly RankClaims<Regiment> Claims = new RankClaims<Regiment>();
        private static readonly List<OrdnanceCandidate> Lane = new List<OrdnanceCandidate>();

        static OrdnanceRuntime()
        {
            CampaignScope.Register("campaign-planner-ordnance", Reset);
        }

        private static void Reset()
        {
            for (int a = 0; a < Alliances; a++) LastRearmRow[a] = null;
            Claims.Clear();
            Lane.Clear();
        }

        // ---- re-arming priority ----

        // The alliance's unit list reordered in place by allocation role for CheckWeaponUpgrades (16080); returns vanilla's order to be
        // given back by EndRearm, or null when vanilla's order stands (the stand-down rule, the new-campaign pass, no list).
        internal static Regiment[] BeginRearm(int alliance, bool isInitialization)
        {
            if (alliance < 0 || alliance >= Alliances) return null;
            bool enabled = FlagOn(Plugin.EnableRearmPriority) && CampaignPlannerRuntime.ExecutorsEnabled();
            bool fresh = CampaignWithholdView.TryFreshAllocation(alliance, out var allocation);
            if (!RearmPriority.ShouldReorder(enabled, alliance, GameVars.playeralliance, GameVars.ai_vs_ai,
                    StrategicCoordinator.IsPlayerCICOf(alliance, GameVars.playeralliance), isInitialization,
                    StrategicCoordinator.WlVanillaJobMachinePaused(), fresh)) return null;
            var alliances = GameVars.alliance;
            if (alliances == null || alliance >= alliances.Length) return null;
            var list = GameVars.alliance[alliance].unitlist;
            if (list == null || list.Count < 2) return null;
            var ownUnits = OwnUnitsOf(alliance);
            if (ownUnits == null) return null;

            var tracker = MoveTrackers.For(alliance);
            Claims.Clear();
            for (int i = 0; i < ownUnits.Count; i++)
            {
                var group = ownUnits[i] as Regiment;
                if ((UnityEngine.Object)group == null || !allocation.TryGet(group.GetInstanceID(), out var assignment)) continue;
                int rank = RearmPriority.RankOf(assignment.Role, tracker.InTransit(assignment.InstanceId) || group.transfertime > 0f);
                var members = group.allattachedunits;
                int size = members != null ? members.Length : 0;
                Claims.Claim(group, rank, size);
                for (int k = 0; k < size; k++) Claims.Claim(members[k], rank, size);
            }

            var ranks = new int[list.Count];
            var counts = new int[RearmPriority.LastRank + 1];
            for (int i = 0; i < ranks.Length; i++)
            {
                var unit = list[i];
                ranks[i] = (UnityEngine.Object)unit == null ? RearmPriority.UnassignedRank : Claims.RankOf(unit, RearmPriority.UnassignedRank);
                counts[ranks[i]]++;
            }
            Claims.Clear();
            LogRearm(alliance, counts);
            return RearmPriority.Reorder(list, ranks);   // the last act: once the list is reordered the caller holds its way back
        }

        // Vanilla's order back into the same list object, whatever the call did.
        internal static void EndRearm(int alliance, Regiment[] saved)
        {
            if (saved == null) return;
            var list = GameVars.alliance[alliance].unitlist;
            if (list == null || !RearmPriority.Restore(list, saved))
                OnceLog.Warning("ordnance-rearm:restore", "[Patch:OrdnanceRearm] the unit list changed length during CheckWeaponUpgrades; vanilla's order was not written back");
        }

        // [Ordnance] on change.
        private static void LogRearm(int alliance, int[] counts)
        {
            string row = RearmPriority.Row(alliance, counts);
            if (string.Equals(LastRearmRow[alliance], row, StringComparison.Ordinal)) return;
            LastRearmRow[alliance] = row;
            TelemetryRouter.LegacyInfo(row, TelemetryLayer.Campaign);
        }

        // The faction's own top groups (AIFaction.ownunits), the allocation's formations.
        private static IList OwnUnitsOf(int alliance)
        {
            for (int i = 0; i < FactionSlots; i++)
            {
                if (PlannerFactionMap.AllianceOf(i) != alliance) continue;
                var faction = AICampaignReflect.GetFaction(i);
                return faction == null ? null : VanillaFieldCache.List(faction, "ownunits");
            }
            return null;
        }

        // ---- the purchase pick ----

        // Whether the alliance's purchase pass is steered: the shared stand-down rule over a fresh published plan.
        internal static bool PurchaseScopeOpens(int alliance)
        {
            bool enabled = FlagOn(Plugin.EnableOrdnancePurchaseSteering) && CampaignPlannerRuntime.ExecutorsEnabled();
            return SteeringGate.ShouldSteer(enabled, alliance, GameVars.playeralliance, GameVars.ai_vs_ai,
                StrategicCoordinator.IsPlayerCICOf(alliance, GameVars.playeralliance), StrategicCoordinator.WlVanillaJobMachinePaused(),
                CampaignWithholdView.TryFreshAllocation(alliance, out _));
        }

        // The model for the order vanilla is placing (PlaceWeaponOrder 227136) for `alliance`: the lane is the one of the model vanilla
        // picked, the candidates are vanilla's own for it (researched or imported, no open order, WSF above the own lane average,
        // 16296-16317) that the order screen allows. True with `picked` when that is a different model; the amount, the open-order
        // rule, the capital check and the charge stay vanilla's. Reads prices through vanilla's figures with its own arguments, so
        // a blockade is already in an import's cost and delivery time (64120-64124).
        internal static bool TryPick(int alliance, int vanillaPick, int quantity, out int picked)
        {
            picked = vanillaPick;
            var weapons = GameVars.weapon;
            var alliances = GameVars.alliance;
            if (weapons == null || alliances == null || alliance < 0 || alliance >= alliances.Length) return false;
            if (vanillaPick < 0 || vanillaPick >= weapons.Length || weapons[vanillaPick] == null || quantity <= 0) return false;
            var vanilla = weapons[vanillaPick];
            bool guns = vanilla.isari;
            float ownAverage = guns ? GameVars.alliance[alliance].averageweaponstrengthguns : GameVars.alliance[alliance].averageweaponstrengthrifles;

            Lane.Clear();
            for (int j = 0; j < weapons.Length; j++)
            {
                if (weapons[j] == null || weapons[j].isari != guns) continue;
                var candidate = new OrdnanceCandidate
                {
                    Id = j,
                    Researched = weapons[j].IsResearched(alliance),
                    Imported = weapons[j].IsImported(alliance),
                    ProductionMultiplier = weapons[j].productionmultiplier,
                    OpenOrder = GameVars.alliance[alliance].weaponorders[j].quantity > 0,
                    Wsf = AICampaign.GetWeaponStrengthFactor(j),
                    BattleValue = OrdnancePicker.BattleValue(weapons[j].range_yd, weapons[j].roundsperminute_pract, weapons[j].firepower[0],
                        weapons[j].accuracy, guns, weapons[j].tubeweight_kg)
                };
                if (OrdnancePicker.NeedsPriceRead(candidate, ownAverage))
                {
                    var figures = weapons[j].GetProductionFigures(alliance, j, quantity);
                    candidate.UnitCost = Math.Abs(figures.cost) / quantity;
                    candidate.DeliveryYears = weapons[j].GetDeliveryTime(GameVars.playeralliance, quantity, j, figures);
                    var standardization = weapons[j].standardizationforalliance;
                    candidate.Standardization = standardization != null && alliance < standardization.Length ? standardization[alliance] : 0f;
                }
                Lane.Add(candidate);
            }

            float enemy = BelievedQuality.Of(EnemyDetectionRuntime.BeliefsFor(alliance), guns);
            var choice = OrdnancePicker.Pick(Lane, ownAverage, vanillaPick, enemy);
            Lane.Clear();
            if (choice.Reason == PickReason.VanillaKept)
                OnceLog.Info("ordnance-purchase:vanilla-kept", "[OrdnancePurchase] no candidate of vanilla's list is orderable; vanilla's pick stands (G9 cannot cancel an order)");
            TelemetryRouter.LegacyInfo(OrdnancePicker.Row(alliance, guns, vanillaPick, choice), TelemetryLayer.Campaign);
            picked = choice.Id;
            return choice.Id != vanillaPick;
        }

        private static bool FlagOn(BepInEx.Configuration.ConfigEntry<bool> entry)
        {
            try { return entry != null && entry.Value; }
            catch { return false; }
        }
    }
}
