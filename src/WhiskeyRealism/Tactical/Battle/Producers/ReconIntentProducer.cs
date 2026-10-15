using System;
using System.Collections.Generic;
using UnityEngine;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Tactical.Orchestrator;
using WhiskeyRealism.Telemetry;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Tactical.Battle.Producers
{
    // BC9 reconnaissance and probing, runtime half (pure half: Battle/BattleRecon.cs).
    // Built by BattleCoreTick's producer registry after DivisionPlay (it reads which
    // groups the posture producer ordered this tick, PostureTick.Current) and before
    // ReserveRelease; runs inside the arbiter tick (#68, AdjustGroupFormations 5870
    // Postfix). Support priority: every doctrine move of the same unit outranks it.
    //
    // Inputs, all read-only: the side's battle snapshot; the contact ledger's
    // confidence, stalest band and entry-point watch stamps
    // (ArmyEvidenceBuilder.ContactLedgerFor, fed by the evidence build); the army plan's
    // phase; the battle stance (ArmyOrchestrator.CurrentBattleStance, GetBattleType via
    // ArmyEvidenceBuilder.ReadBattleStance, the stance the director and the posture
    // guard read);
    // the AI pool (unitsused, captured on the coordinator tick); the operation's
    // objective; vanilla's GamePrefs distances (maximumlineofsight 51308,
    // distancetoenemytocancelinterruption / distancetoenemyforinterruption 52444-52446,
    // aiminmoraletodeployskirmishers 52606).
    //
    // The posture producer consumes the probe nominations (task rewrite), the probe
    // holds (moves back over the ground gained are dropped) and the look points
    // (Scout/Probe targets); the facing pulse the entry bearing.
    // Rollback: Enable Battle Reconnaissance = false.
    internal sealed class ReconIntentProducer : IBattleIntentProducer
    {
        private const float TelemetrySeconds = 30f;

        private static readonly ReconMemory[] _memory = { new ReconMemory(), new ReconMemory() };
        private static readonly ReconPicture[] _picture = { new ReconPicture(), new ReconPicture() };
        private static readonly Dictionary<string, float> _lastRowAt = new Dictionary<string, float>();

        static ReconIntentProducer()
        {
            BattleScope.Register("recon-producer", Reset);
        }

        public string Name => "Recon";

        public void Produce(BattleSnapshot snapshot, IIntentSink sink)
        {
            using (TelemetryPerf.Scope("tactical.producer.recon", TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
            {
                if (snapshot == null || sink == null) return;
                int alliance = snapshot.Alliance;
                if (alliance < 0 || alliance >= _memory.Length) return;

                ReconSettings settings = CurrentSettings();
                ReconPicture picture = _picture[alliance];
                FillPicture(snapshot, picture);
                ReconTickResult result = BattleRecon.Plan(snapshot, settings, picture, _memory[alliance],
                    OrderExecutor.ForAlliance(alliance), sink);
                if (result.Events != ReconEvents.None) Report(snapshot, alliance, result);
            }
        }

        /// <summary>The probe nomination of a group this battle (the posture pipeline's rewrite).</summary>
        internal static bool TryProbe(int alliance, int groupId, out float x, out float z)
        {
            x = 0f;
            z = 0f;
            return alliance >= 0 && alliance < _memory.Length && _memory[alliance].TryGetProbe(groupId, out x, out z);
        }

        /// <summary>
        /// The ground a group holds after its probe while <paramref name="postureTask"/> is
        /// still the ledger task it probed under (the posture pipeline drops moves back).
        /// </summary>
        internal static bool TryHold(int alliance, int groupId, CommandTaskType postureTask,
            out float x, out float z, out float dirX, out float dirZ)
        {
            x = z = dirX = dirZ = 0f;
            return alliance >= 0 && alliance < _memory.Length && CurrentSettings().Enabled &&
                _memory[alliance].TryGetHold(groupId, postureTask, out x, out z, out dirX, out dirZ);
        }

        /// <summary>Where a Scout or Probe task looks: the probe nomination, else BattleRecon.LookPoint.</summary>
        internal static bool TryLookTarget(BattleSnapshot s, int index, out float x, out float z)
        {
            x = 0f;
            z = 0f;
            if (s == null || index < 0 || index >= s.OwnCount) return false;
            int alliance = s.Alliance;
            if (alliance < 0 || alliance >= _memory.Length) return false;
            ReconSettings settings = CurrentSettings();
            if (!settings.Enabled) return false;
            if (_memory[alliance].TryGetProbe(s.Own[index].InstanceId, out x, out z)) return true;
            return BattleRecon.LookPoint(s, index, settings, _picture[alliance], out x, out z);
        }

        /// <summary>The bearing to the stalest unwatched enemy-side entry point (facing pulse, Entry source).</summary>
        internal static bool TryEntryBearing(BattleSnapshot s, int index, out float heading)
        {
            heading = -1f;
            if (s == null) return false;
            int alliance = s.Alliance;
            if (alliance < 0 || alliance >= _picture.Length || !CurrentSettings().Enabled) return false;
            return BattleRecon.TryEntryBearing(s, index, _picture[alliance], out heading);
        }

        private static void FillPicture(BattleSnapshot s, ReconPicture p)
        {
            int alliance = s.Alliance;
            p.BeginTick(s.OwnCount);
            float now = (float)s.DoctrineHours;

            TacticalEnemyContactLedger ledger = ArmyEvidenceBuilder.ContactLedgerFor(alliance);
            p.IntelConfidence = 0f;
            p.HadContact = false;
            if (ledger != null)
            {
                p.IntelConfidence = ledger.Confidence(now);
                p.HadContact = ledger.HadContact;
                if (ledger.TryStalestBand(now, out float lateral, out float recency))
                {
                    p.HasStaleBand = true;
                    p.StaleBandLateral = lateral;
                    p.StaleBandRecency = recency;
                    p.FrameHeading = ledger.FrameHeading;
                }
                for (int k = 0; k < ledger.EntryPointCount; k++)
                    if (ledger.TryGetEntryPoint(k, out float x, out float z, out float watched)) p.AddEntry(x, z, watched);
            }

            TacticalBattleOrchestrator side = TacticalBattleCoordinator.GetSideOrchestrator(alliance);
            ArmyOrchestrator army = side?.Army;
            p.Phase = army != null && army.HasPlan ? army.CurrentPlan.Phase : (BattlePhase?)null;
            p.SideDefending = army != null && army.CurrentBattleStance == BattleStance.Defender;
            p.HasObjective = PostureTargets.TryOperationObjective(side, out p.ObjectiveX, out p.ObjectiveZ, out _);

            PostureTick.State posture = PostureTick.Current(s);
            UnitRecord[] own = s.Own;
            for (int i = 0; i < s.OwnCount; i++)
            {
                if ((own[i].Flags & UnitFlags.IsGroup) == 0) continue;
                p.AiPool[i] = ArmyPostureInputsRuntime.InAiPool(alliance, own[i].InstanceId);
                p.PostureMoved[i] = posture != null && posture.Groups[i].Submitted;
                p.PostureTask[i] = posture != null && posture.Groups[i].Has ? posture.Groups[i].State.Task : CommandTaskType.None;
            }
        }

        // GamePrefs read per call (static fields, no allocation). An unreadable pref
        // idles the producer (fail closed).
        internal static ReconSettings CurrentSettings()
        {
            try
            {
                bool enabled = Plugin.Instance != null && Plugin.Instance.Enabled != null && Plugin.Instance.Enabled.Value &&
                    Plugin.EnableBattleReconnaissance != null && Plugin.EnableBattleReconnaissance.Value;
                float[] sight = GamePrefs.maximumlineofsight;
                if (sight == null || sight.Length <= TacticalUnitType.Cavalry) return default(ReconSettings);
                return new ReconSettings(enabled,
                    cavalrySight: sight[TacticalUnitType.Cavalry],
                    infantrySight: sight[TacticalUnitType.Infantry],
                    standOff: GamePrefs.distancetoenemytocancelinterruption,
                    interruptMeters: GamePrefs.distancetoenemyforinterruption,
                    minMorale: GamePrefs.aiminmoraletodeployskirmishers);
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-battle-core:recon-settings",
                    "[BattleRecon] prefs unreadable; reconnaissance idle: " + ex.GetType().Name + " " + ex.Message);
                return default(ReconSettings);
            }
        }

        // Bounded: missions start at most 4 times per side per battle hour and probes
        // once per band per hour; rows are also throttled per side and event.
        private static void Report(BattleSnapshot s, int alliance, in ReconTickResult r)
        {
            try
            {
                ReconMemory memory = _memory[alliance];
                if ((r.Events & ReconEvents.MissionStarted) != 0)
                {
                    OnceLog.Info("tactical-battle-core:recon-first-fire",
                        "[BattleRecon] first reconnaissance: a cavalry regiment rides to look (target=" + r.StartedKind + ")");
                    Row(s, alliance, "MissionStarted", r.StartedKind.ToString(), memory.ScoutId, memory.TargetX, memory.TargetZ);
                }
                if ((r.Events & ReconEvents.MissionEnded) != 0)
                    Row(s, alliance, "MissionEnded", r.EndReason ?? "ended", 0, 0f, 0f);
                if ((r.Events & ReconEvents.ProbeNominated) != 0)
                {
                    OnceLog.Info("tactical-battle-core:probe-first-fire",
                        "[BattleRecon] first reconnaissance in force: a group probes its front band in the Probe phase");
                    Row(s, alliance, "ProbeNominated", "probe-phase", 0, 0f, 0f);
                }
                if ((r.Events & ReconEvents.ProbeHeld) != 0)
                {
                    OnceLog.Info("tactical-battle-core:probe-hold-first-fire",
                        "[BattleRecon] first probe hold: a probe ended short of a superior enemy and holds the ground it gained");
                    Row(s, alliance, "ProbeHeld", BattleRecon.HoldReason, 0, 0f, 0f);
                }
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-battle-core:recon-telemetry", "[BattleRecon] telemetry threw: " + ex.GetType().Name + " " + ex.Message);
            }
        }

        private static void Row(BattleSnapshot s, int alliance, string decision, string reason, int unitId, float x, float z)
        {
            string signature = alliance + "|" + decision + "|" + reason + "|" + unitId;
            if (!TacticalTelemetry.ShouldEmit(_lastRowAt, "tactical-recon:" + alliance + ":" + decision, signature,
                    Time.realtimeSinceStartup, TelemetrySeconds, false))
                return;
            string unit = "";
            if (unitId != 0)
            {
                IReadOnlyDictionary<int, string> names = BattleSnapshotBuilder.NamesFor(alliance);
                unit = names != null && names.TryGetValue(unitId, out string name) ? TacticalCurrentOrderSignature.Safe(name) : "#" + unitId;
            }
            TelemetryRouter.Emit(TelemetryLayer.Tactical, TelemetryCategory.Decision, "TacticalRecon", TelemetrySeverity.Info,
                ev => ev
                    .WithSide(alliance)
                    .WithUnit(unit)
                    .WithDecision(decision, reason, signature)
                    .WithField("targetX", x)
                    .WithField("targetZ", z)
                    .WithField("doctrineHours", s.DoctrineHours));
        }

        private static void Reset()
        {
            for (int a = 0; a < _memory.Length; a++) _memory[a].Clear();
            _lastRowAt.Clear();
        }
    }
}
