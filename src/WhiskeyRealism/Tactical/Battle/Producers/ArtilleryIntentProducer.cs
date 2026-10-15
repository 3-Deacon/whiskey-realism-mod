using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Tactical.Orchestrator;
using WhiskeyRealism.Telemetry;

namespace WhiskeyRealism.Tactical.Battle.Producers
{
    // Artillery on the order arbiter (plan BC5b). Replaces three deleted writers:
    //   - B7CheckAIBombardmentPatch, Postfix on AIBattle.CheckAIBombardment
    //     (3869-3911, micro task slot 2, groups at ai_stance 3/4 only): cancel a
    //     bombardment (9 -> 7 + bombardstarttime = 0, 3904), switch it to
    //     counter-battery (8), release a stale counter-battery (8 -> 7; vanilla's
    //     own release in CheckCounterBatteryFire is unreachable, 3834);
    //   - the #45 all-stance artillery audit (BattleGroupStancePatch): the micro
    //     doctrine for every AI battery whatever its group stance — limber,
    //     unlimber and face (UnlimberArtilleryAIMicro 3482-3495), turn toward the
    //     threat (vanilla micro facing 4599-4606), retreat (CheckArtyFallback
    //     3499-3539), cease fire below 10 % ammo;
    //   - ArtillerySetWaypointVetoPatch (SetWaypoint Postfix, 91232): a battery
    //     vanilla moves to within 60 m of a visible enemy is pulled back.
    // Two producers come from this class, matching BattleCoreTick's order:
    // "ArtilleryDoctrine" (doctrine + micro, Executor priority; retreats Safety)
    // and "ArtilleryPointBlank" (the watchdog, Safety). The decisions are pure
    // (BattleWriterPolicy.ArtilleryDoctrineIntents / TryArtilleryPointBlankIntent);
    // this adapter reads the live own battery for the fields the snapshot does
    // not carry (mounted, guns, fire range, ammo slots, cover, group morale) and
    // vanilla's GamePrefs. Every enemy input is the snapshot's observations.
    //
    // Cadence: the arbiter tick (once per macro cycle) instead of every micro
    // call. Per-battery hold: Artillery Audit Min Hold Seconds (real seconds at
    // 1x) on the doctrine clock against the battery's last executor outcome; the
    // point-blank watchdog is Safety and is not held.
    internal sealed class ArtilleryIntentProducer : IBattleIntentProducer
    {
        static ArtilleryIntentProducer()
        {
            BattleCoreTick.RegisterProducer(new ArtilleryIntentProducer("ArtilleryDoctrine", pointBlank: false));
            BattleCoreTick.RegisterProducer(new ArtilleryIntentProducer("ArtilleryPointBlank", pointBlank: true));
        }

        private static readonly OrderChannel[] HeldChannels =
        {
            OrderChannel.CombatBehavior, OrderChannel.Formation, OrderChannel.Facing, OrderChannel.Move,
        };

        private readonly bool _pointBlank;
        private readonly BattleOrderIntent[] _buffer = new BattleOrderIntent[3];

        private ArtilleryIntentProducer(string name, bool pointBlank)
        {
            Name = name;
            _pointBlank = pointBlank;
        }

        public string Name { get; }

        public void Produce(BattleSnapshot snapshot, IIntentSink sink)
        {
            using (TelemetryPerf.Scope(_pointBlank ? "tactical.producer.artillery-point-blank" : "tactical.producer.artillery",
                TelemetryLayer.Tactical, TelemetryCategory.Performance, 2.0))
            {
                if (snapshot == null || sink == null || !GroupStanceIntentProducer.PluginEnabled()) return;
                BattleArtilleryRules rules = ReadRules();
                if (!rules.DoctrineOn && !rules.AuditOn) return;

                double now = TacticalDoctrineTime.NowBattleHours;
                double holdHours = TacticalBattleClock.RealSecondsAtOneXToHours(
                    Plugin.ArtilleryAuditMinHoldSeconds != null ? Plugin.ArtilleryAuditMinHoldSeconds.Value : 3f);
                OrderExecutor executor = OrderExecutor.ForAlliance(snapshot.Alliance);
                UnitRecord[] own = snapshot.Own;
                for (int i = 0; i < snapshot.OwnCount; i++)
                {
                    if ((own[i].Flags & UnitFlags.IsArtillery) == 0 || !BattleWriterPolicy.ArtilleryBatteryEligible(snapshot, i)) continue;

                    if (_pointBlank)
                    {
                        if (BattleWriterPolicy.TryArtilleryPointBlankIntent(snapshot, i, rules.FallbackDistance, out BattleOrderIntent retreat))
                            sink.Submit(retreat);
                        continue;
                    }

                    Regiment unit = BattleSnapshotBuilder.UnitAt(snapshot, i, enemy: false);
                    if ((UnityEngine.Object)unit == null) continue;
                    BattleArtilleryState state = ReadState(snapshot, i, unit, rules);
                    state.InHold = InHold(executor, own[i].InstanceId, now, holdHours);
                    int n = BattleWriterPolicy.ArtilleryDoctrineIntents(snapshot, i, state, rules, _buffer);
                    for (int k = 0; k < n; k++) sink.Submit(_buffer[k]);
                }
            }
        }

        private static BattleArtilleryRules ReadRules()
        {
            return new BattleArtilleryRules
            {
                DoctrineOn = Plugin.EnableTacticalArtilleryDoctrine != null && Plugin.EnableTacticalArtilleryDoctrine.Value,
                AuditOn = Plugin.EnableArtilleryAllStanceAudit != null && Plugin.EnableArtilleryAllStanceAudit.Value,
                FallbackDistance = GamePrefs.artilleryfallbackenemyclosedist,
                TurnTriggerDegrees = GamePrefs.angletriggerforturningtowardsenemy,
                MaxCoverValueForRotation = GamePrefs.maxcovervalueforgrouprotation,
                AdditionalAngleInCover = GamePrefs.additionalanglewhenincovermicro,
            };
        }

        private static BattleArtilleryState ReadState(BattleSnapshot snapshot, int index, Regiment unit, in BattleArtilleryRules rules)
        {
            ref UnitRecord battery = ref snapshot.Own[index];
            int parent = battery.ParentIndex;
            int parentStance = parent >= 0 && parent < snapshot.OwnCount ? snapshot.Own[parent].AiStance : -1;
            float[] fallback = GamePrefs.moraletriggerforfallbackifenemyclose;
            float[] ammo = unit.ammo;
            var state = new BattleArtilleryState
            {
                Mounted = unit.mounted,
                Guns = unit.guns,
                FireRange = unit.firerange,
                AmmoRatio01 = TacticalArtilleryInputAdapter.ArtilleryAmmoRatio01(ammo),
                CanisterAmmo = ammo != null && ammo.Length > 2 ? ammo[2] : 0f,
                Morale = battery.Morale,
                BattleStartMorale = battery.BattleStartMorale,
                GroupMorale = unit.groupmorale,
                FallbackMoraleThreshold = fallback != null && parentStance >= 0 && parentStance < fallback.Length ? fallback[parentStance] : 0.4f,
                CoverValue = unit.covervalue,
                CoverObject = unit.coverobject,
            };
            // Doctrine only speaks to a running bombardment (TacticalArtilleryDoctrine
            // acts on 9 alone); skip the order lookup otherwise.
            if (rules.DoctrineOn && battery.CombatBehaviorOrdered == BattleWriterPolicy.CombatBehaviorBombard)
                state.Doctrine = DoctrineFor(snapshot, index, parent, state.FireRange, state.AmmoRatio01);
            return state;
        }

        // The parent group's doctrine order, as B7 resolved it.
        private static DoctrineArtilleryDecision DoctrineFor(BattleSnapshot snapshot, int index, int parent, float fireRange, float ammoRatio01)
        {
            Regiment group = BattleSnapshotBuilder.UnitAt(snapshot, parent, enemy: false);
            if ((UnityEngine.Object)group == null ||
                !TacticalDoctrineOrderResolver.TryResolve(group, out CommandDoctrineOrder order, out BattlefieldPictureSnapshot picture))
                return default(DoctrineArtilleryDecision);
            ref UnitRecord battery = ref snapshot.Own[index];
            int closest = snapshot.ClosestVisibleEnemy(index, out float distance);
            if (closest < 0) distance = 9999f;
            return DoctrineConsumerDecisions.DecideArtillery(
                order,
                new TacticalArtilleryMissionInput(
                    requestedSupport: DoctrineConsumerDecisions.EnemyMainLineExposed(order, picture),
                    enemyArtilleryVisible: BattleWriterPolicy.VisibleEnemyArtilleryWithin(snapshot, battery.X, battery.Z, fireRange),
                    ammoRatio01: ammoRatio01,
                    targetDistance: distance,
                    optimalRange: fireRange * 0.75f,
                    maxRange: fireRange,
                    friendlyDangerClose: BattleWriterPolicy.FriendlyDangerClose(snapshot, index, fireRange),
                    threatenedByCloseEnemy: distance <= GamePrefs.artilleryfallbackenemyclosedist,
                    canDisplace: true));
        }

        private static bool InHold(OrderExecutor executor, int batteryId, double now, double holdHours)
        {
            if (executor == null) return false;
            for (int c = 0; c < HeldChannels.Length; c++)
            {
                if (!executor.TryGetLastOutcome(batteryId, HeldChannels[c], out OrderOutcome outcome)) continue;
                if (outcome.Kind != OrderOutcomeKind.Issued && outcome.Kind != OrderOutcomeKind.Moved) continue;
                if (BattleWriterPolicy.ArtilleryHoldActive(now, outcome.AtHours, holdHours)) return true;
            }
            return false;
        }
    }
}
