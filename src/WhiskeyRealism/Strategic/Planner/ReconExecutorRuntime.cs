using UnityEngine;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Strategic.Planner
{
    // The reconnaissance executor's vanilla side (plan G5d): the daily review's step that sends the formations on a recon
    // mission to their posts and gives a cavalry screen the Scouting order. The decisions are ReconExecutor's (pure, tested);
    // this class implements the cavalry-order and live-chain half of its host over vanilla, the move half being the mover's
    // (VanillaMoveHost), and logs. Runs only inside ReviewExecute, after the pre-war gate (executors write vanilla state only
    // from the review), and reads the alliance's ReconMissionLedger as the review passes it in, so nothing outside the review
    // can name the ledger.
    //
    // Vanilla it rests on (decompile build 14703111): BattleUnits.ChangeCampaignOrders 90720-90770 (120 + n sets cavalryorders n,
    // 121 Scouting; it returns at once when cavalryorders already equals n, 90738; without order delays it applies at once to the
    // group and every descendant three levels down, PerformActionForGroupForAllUnitsOnLevel(..., -1, "SetCavalryOrders") at
    // 90758 and 92572-92620, else it queues on the group, applied at 125409-125412); Regiment.SetCavalryOrders 122074-122082;
    // the "Scouting" status 119923 and its intel term 120424-120426 test the group's own cavalryorders; a group in readiness
    // step 1 or less has them reset to 0 (113960), which is why the order is sent again each review rather than once; the
    // later arrival of an order-1 group as a battle reinforcement 67742-67744; AICampaign.MoveUnitTo 14479-14540 as the mover.
    internal static class ReconExecutorRuntime
    {
        private static readonly ReconExecutor[] Executors = { new ReconExecutor(), new ReconExecutor() };
        private static readonly VanillaReconHost Host = new VanillaReconHost();

        static ReconExecutorRuntime()
        {
            CampaignScope.Register("campaign-planner-recon-executors", Reset);
        }

        private static void Reset()
        {
            for (int a = 0; a < Executors.Length; a++) Executors[a] = new ReconExecutor();
        }

        internal static void Execute(int alliance, CampaignSnapshot snapshot, ReconMissionLedger ledger)
        {
            using (TelemetryPerf.Scope("campaign.planner.recon-execute", TelemetryLayer.Campaign, TelemetryCategory.Performance, 2.0))
            {
                if (snapshot == null || ledger == null || alliance < 0 || alliance >= Executors.Length) return;
                var tracker = MoveTrackers.For(alliance);
                if (tracker == null) return;

                Host.Bind(alliance, WlChainContext.Capture());
                var tally = Executors[alliance].Review(snapshot, ledger, Host, tracker, DLC_WL.dlc_scenarioactive && GameVars.playeralliance == alliance);
                Host.Unbind();

                for (int i = 0; i < tally.Rows.Count; i++) TelemetryRouter.LegacyInfo(tally.Rows[i], TelemetryLayer.Campaign);
                if (tally.Any) TelemetryRouter.LegacyInfo(tally.Summary(alliance), TelemetryLayer.Campaign);
            }
        }

        private sealed class VanillaReconHost : VanillaMoveHost, IReconHost
        {
            private WlChainContext _chain;
            private BattleUnits _battleUnits;
            private bool _battleUnitsResolved;

            internal void Bind(int alliance, WlChainContext chain)
            {
                Alliance = alliance;
                _chain = chain;
                _battleUnitsResolved = false;
            }

            internal void Unbind()
            {
                _battleUnits = null;
                _chain = default(WlChainContext);
            }

            // Stage F's classification, from a context captured once per pass.
            public WlChainClass LiveChain(int instanceId)
            {
                var unit = UnitOf(instanceId);
                if (unit == null) return WlChainClass.None;
                return WlStrategicOrderBridge.ChainClassOf(in _chain, Alliance, unit);
            }

            // ChangeCampaignOrders tests cavalryorders only (90738), so with order delays on an order still in the queue would be
            // queued again every review: compare what was ordered (cavalryordersordered, set by the call at 90748) first.
            public bool SendCavalryOrder(int instanceId, int orderCode)
            {
                var unit = UnitOf(instanceId);
                if (unit == null) return false;
                int mode = ReconOrders.ModeOf(orderCode);
                if (unit.cavalryordersordered == mode) return false;
                var battleUnits = ResolveBattleUnits();
                if ((UnityEngine.Object)battleUnits == null) return false;
                battleUnits.ChangeCampaignOrders(unit, orderCode);
                return true;
            }

            // The campaign map's own GameController (as AICampaign finds it, 11143-11144), null on a battle map; AICampaign's
            // bunits is private.
            private BattleUnits ResolveBattleUnits()
            {
                if (_battleUnitsResolved) return _battleUnits;
                _battleUnitsResolved = true;
                var go = GameObject.Find("GameController");
                var battleUnits = (UnityEngine.Object)go != null ? go.GetComponent<BattleUnits>() : null;
                if ((UnityEngine.Object)battleUnits == null || !battleUnits.IsCampaign) return null;
                _battleUnits = battleUnits;
                return _battleUnits;
            }
        }
    }
}
