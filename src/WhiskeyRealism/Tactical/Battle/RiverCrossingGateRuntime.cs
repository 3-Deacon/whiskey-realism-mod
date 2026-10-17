using System;
using System.Collections.Generic;
using WhiskeyRealism.Tactical.Orchestrator;
using WhiskeyRealism.Tactical.Orchestrator.Plays;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Tactical.Battle
{
    // Runtime half of RiverCrossingRule (excluded from the test csproj): the side's BC14 crossing picture (FeasibilityRuntime.CrossingPicture:
    // the battle's RiverRegions, the side's own crossing watch and ledger), one answer per planned Move. A refused leg posts its look request into
    // the ledger. Rollback: Tactical / Enable Crossing Move Gate.
    //
    // No picture. With the feasibility off, the build failed, the terrain frozen or without a river, or no side state yet, there is no river map to
    // read and the leg passes as one bank (the flag's rollback). While the map is still being built on a terrain that has a river the move WAITS
    // (CrossingVerdict.Pending, CrossingPictureWait, at most CapHours of doctrine time) rather than passing unchecked: it is asked again next tick.
    // Vanilla gives no such check; it validates a waypoint's target for water only (WaypointValidator).
    //
    // Every verdict class writes one TacticalCrossingGate row per side and region pair, throttled (RowSeconds), so a leg that passed for want of a
    // bank (a picture not ready, an end on no land) can be told from a leg that stays on one bank.
    internal sealed class RiverCrossingGateRuntime : ICrossingGate
    {
        internal static readonly RiverCrossingGateRuntime Instance = new RiverCrossingGateRuntime();

        private const float RowSeconds = 60f;
        private static bool _firstBlockLogged;
        private static bool _firstViaLogged;
        private static bool _waitCapLogged;
        private static readonly CrossingPictureWait Wait = new CrossingPictureWait();
        private static readonly Dictionary<int, float> LastRowAt = new Dictionary<int, float>();

        static RiverCrossingGateRuntime()
        {
            BattleScope.Register("river-crossing-gate", () =>
            {
                _firstBlockLogged = false;
                _firstViaLogged = false;
                _waitCapLogged = false;
                Wait.Clear();
                LastRowAt.Clear();
            });
        }

        public CrossingVerdict Check(int alliance, float x0, float z0, float x1, float z1, bool attack, in RoadLeg road, out CrossingRoute route)
        {
            route = default;
            ICrossingPicture picture = FeasibilityRuntime.CrossingPicture(alliance);
            if (picture == null) return NoPicture(alliance, x0, z0, x1, z1, out route);
            CrossingVerdict verdict = RiverCrossingRule.Decide(picture, x0, z0, x1, z1, attack, road, out route);
            if (verdict == CrossingVerdict.NoKnownCrossing && !_firstBlockLogged)
            {
                _firstBlockLogged = true;
                OnceLog.Info("tactical-battle-core:crossing-gate-block",
                    "[CrossingGate] first move refused: its leg crosses a river and the side knows no crossing for it (side=" + alliance + ", look link=" + route.LookLink + ")");
            }
            else if (verdict == CrossingVerdict.ViaCrossing && !_firstViaLogged)
            {
                _firstViaLogged = true;
                OnceLog.Info("tactical-battle-core:crossing-gate-via",
                    "[CrossingGate] first move routed through known crossings (side=" + alliance + ", crossings=" + route.Crossings + ", vias=" + route.ViaCount + ")");
            }
            EmitRow(alliance, verdict, in route, x0, z0, x1, z1);
            return verdict;
        }

        // No river map: wait for it while it can still arrive on a terrain with a river, else the leg passes (the flag's rollback).
        private static CrossingVerdict NoPicture(int alliance, float x0, float z0, float x1, float z1, out CrossingRoute route)
        {
            route = new CrossingRoute { LookLink = -1, FromRegion = -1, ToRegion = -1, PictureNotReady = true };
            bool pending = false;
            try
            {
                BattleTerrainGrid grid = BattleTerrainGridRuntime.Get();
                pending = grid != null && !grid.Frozen && grid.CrossingCount > 0 && RiverRegionsRuntime.Pending();
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-battle-core:crossing-gate-pending", "[CrossingGate] pending check failed, the gate does not wait: " + ex.GetType().Name);
            }
            bool wait = Wait.Waits(pending, TacticalDoctrineTime.NowBattleHours);
            if (pending && !wait && !_waitCapLogged)
            {
                _waitCapLogged = true;
                OnceLog.Warning("tactical-battle-core:crossing-gate-wait-cap",
                    "[CrossingGate] the river map did not arrive within " + (CrossingPictureWait.CapHours * 3600.0).ToString("0") + " battle s; gated moves pass unchecked");
            }
            CrossingVerdict verdict = wait ? CrossingVerdict.Pending : CrossingVerdict.SameBank;
            EmitRow(alliance, verdict, in route, x0, z0, x1, z1);
            return verdict;
        }

        private static string BasisOf(in CrossingRoute route)
        {
            if (route.PictureNotReady) return "picture-not-ready";
            if (route.EndUnresolved) return "end-no-land";
            if (route.FromResolved || route.ToResolved) return route.FromRegion == route.ToRegion ? "resolved-same-region" : "resolved-regions";
            return route.FromRegion == route.ToRegion ? "same-region" : "regions";
        }

        // One row per (side, region pair, basis, verdict): the endpoints are those of the row's first leg after the throttle. The key is an int so the
        // steady state allocates nothing; the strings are built only for a row that is written.
        private static void EmitRow(int alliance, CrossingVerdict verdict, in CrossingRoute route, float x0, float z0, float x1, float z1)
        {
            try
            {
                int basisCode = route.PictureNotReady ? 1 : route.EndUnresolved ? 2 : (route.FromResolved || route.ToResolved) ? 3 : 4;
                int key = ((((alliance & 3) * 257 + (route.FromRegion + 1)) * 257 + (route.ToRegion + 1)) * 8 + basisCode) * 8 + (int)verdict;
                float now = Environment.TickCount * 0.001f;
                if (LastRowAt.TryGetValue(key, out float last) && now - last < RowSeconds) return;
                LastRowAt[key] = now;
                CrossingRoute copy = route;
                TelemetryRouter.Emit(TelemetryLayer.Tactical, TelemetryCategory.Decision, "TacticalCrossingGate", TelemetrySeverity.Info,
                    ev => ev
                        .WithSide(alliance)
                        .WithDecision("TacticalCrossingGate", verdict.ToString(), "side=" + alliance + "|basis=" + BasisOf(in copy))
                        .WithField("verdict", verdict.ToString())
                        .WithField("basis", BasisOf(in copy))
                        .WithField("fromRegion", copy.FromRegion)
                        .WithField("toRegion", copy.ToRegion)
                        .WithField("fromResolved", copy.FromResolved)
                        .WithField("toResolved", copy.ToResolved)
                        .WithField("fromX", x0)
                        .WithField("fromZ", z0)
                        .WithField("toX", x1)
                        .WithField("toZ", z1)
                        .WithField("crossings", copy.Crossings)
                        .WithField("lookLink", copy.LookLink));
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-battle-core:crossing-gate-row", "[CrossingGate] row failed: " + ex.GetType().Name);
            }
        }
    }
}
