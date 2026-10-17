using WhiskeyRealism.Tactical.Battle;
using WhiskeyRealism.Tactical.Operations;
using WhiskeyRealism.Tactical.Orchestrator;

namespace WhiskeyRealism.Tactical
{
    internal enum ArtilleryMicroWrite
    {
        None = 0,
        Limber = 1,
        UnlimberAndFace = 2,
        Retreat = 3,
        Face = 4,
        CeaseBombard = 5
    }

    internal readonly struct MacroAiWriteInput
    {
        public MacroAiWriteInput(
            bool modeAllowsWrites,
            bool isFeudInstance,
            bool scriptedAiStrategy,
            bool scriptedSideMacro,
            bool endBattleActive,
            int currentMacro,
            int ledgerOpinion,
            int fallbackOpinion,
            bool wlPlayerSide,
            bool wlPlayerIsCic,
            int vanillaPendingMacro)
        {
            WlPlayerSide = wlPlayerSide;
            WlPlayerIsCic = wlPlayerIsCic;
            VanillaPendingMacro = vanillaPendingMacro;
            ModeAllowsWrites = modeAllowsWrites;
            IsFeudInstance = isFeudInstance;
            ScriptedAiStrategy = scriptedAiStrategy;
            ScriptedSideMacro = scriptedSideMacro;
            EndBattleActive = endBattleActive;
            CurrentMacro = currentMacro;
            LedgerOpinion = ledgerOpinion;
            FallbackOpinion = fallbackOpinion;
        }

        public bool ModeAllowsWrites { get; }
        public bool IsFeudInstance { get; }
        public bool ScriptedAiStrategy { get; }
        public bool ScriptedSideMacro { get; }
        public bool EndBattleActive { get; }
        public int CurrentMacro { get; }
        public int LedgerOpinion { get; }
        // Unplanned opinion: reinforcement-opportunity doctrine, or the legacy
        // scorer when the army orchestrator is off. Never written on the W&L
        // player side.
        public int FallbackOpinion { get; }
        // W&L player-side AIBattle (aibattle[1], isplayeraiorfeud == 1).
        public bool WlPlayerSide { get; }
        public bool WlPlayerIsCic { get; }
        // AIBattle.newmacroaitoset: where vanilla's own pipeline is heading.
        public int VanillaPendingMacro { get; }
    }

    internal readonly struct AddPathDecision
    {
        public AddPathDecision(bool overrideResult, int result, string reason)
        {
            OverrideResult = overrideResult;
            Result = result;
            Reason = reason;
        }

        public bool OverrideResult { get; }
        public int Result { get; }
        public string Reason { get; }
    }

    // How #42 replays one feud move (BattleWriterPolicy.DecideFeudReplayPath).
    internal enum FeudReplayPath
    {
        Executor = 0,      // FeudReplay intent through OrderExecutorRuntime.ExecuteInline
        DirectGuarded = 1, // no snapshot: vanilla's move, behind CanWrite(Regiment, feudReplay: true)
        Refused = 2,       // no snapshot and the guard refuses (player-owned / exposed descendant)
    }

    // Own-battery inputs the battle snapshot does not carry, read from the live
    // battery by ArtilleryIntentProducer (own side only; every enemy input comes
    // from the snapshot's observations).
    internal struct BattleArtilleryState
    {
        public int Mounted;                  // Regiment.mounted: 1 = limbered
        public int Guns;
        public float FireRange;
        public float AmmoRatio01;            // TacticalArtilleryInputAdapter.ArtilleryAmmoRatio01
        public float CanisterAmmo;           // ammo[2]
        public float Morale;
        public float BattleStartMorale;
        public float GroupMorale;            // groupmorale; the micro doctrine's morale bonus at >= 0.6
        public float FallbackMoraleThreshold; // moraletriggerforfallbackifenemyclose[parent ai_stance]
        public float CoverValue;
        public int CoverObject;
        public DoctrineArtilleryDecision Doctrine; // Observe without a doctrine order for the parent group
        public bool InHold;                  // Artillery Audit Min Hold Seconds

        // BC14W (requirement-focal-execution): the parent group's doctrine order carries a preparation point (the focal assault's focal point) while the
        // plan prepares its assault, and the battery's mean ammo as vanilla's CheckAIBombardment computes it (Sum(ammo) / 3, 3896).
        public bool HasPreparation;
        public float PreparationX, PreparationZ;
        public float AmmoMean;
    }

    // Config and vanilla prefs for the artillery producers, read once per tick.
    internal struct BattleArtilleryRules
    {
        public bool DoctrineOn;              // Enable Tactical Artillery Doctrine
        public bool AuditOn;                 // Enable Artillery All-Stance Audit
        public float FallbackDistance;       // GamePrefs.artilleryfallbackenemyclosedist
        public float TurnTriggerDegrees;     // GamePrefs.angletriggerforturningtowardsenemy
        public float MaxCoverValueForRotation;
        public float AdditionalAngleInCover;
    }

    // Pure decisions behind the WS T3 battle writer/gate patches. Runtime
    // patches gather vanilla state and ask these functions what to do, so the
    // harness can pin each decision to the vanilla rule it mirrors.
    internal static class BattleWriterPolicy
    {
        public const int NoMacroWrite = int.MinValue;

        // Single macroai owner (#44, AIBattle.CheckGlobalAIStrategy Postfix).
        // Vanilla anchors (decompile build 14703111):
        //   6355  startoffallback is stamped only on vanilla's own transition to 3;
        //   6362  macroai == 3 ends the battle once currenttimefromstart >=
        //         startoffallback + timetostartairetreattimer, so a foreign 3
        //         (startoffallback == 0) ends it on the next cycle -> never write 3;
        //   6374  macroai == 3 returns before everything else;
        //   6378  GameVars.aistrategy >= 0 and
        //   6383  sideinformation[side].macroai >= 0 are scripted overrides.
        // Opinions outside 0..2 mean "no opinion"; the operations-ledger opinion
        // wins over the fallback (doctrine or legacy scorer) opinion.
        public static int ResolveMacroAiWrite(in MacroAiWriteInput input)
        {
            if (!MacroAiMayHaveOpinion(
                    input.ModeAllowsWrites,
                    input.IsFeudInstance,
                    input.ScriptedAiStrategy,
                    input.ScriptedSideMacro,
                    input.EndBattleActive,
                    input.CurrentMacro))
                return NoMacroWrite;

            if (input.WlPlayerSide)
            {
                // W&L player side: its macroai drives dispatches to the player
                // (2 -> type-12 defend orders, CheckGlobalAIStrategy 6447-6466;
                // 3 -> type-15 retreat, CheckRemovalOfOrders 6790-6799). Only a
                // planned-operation opinion that genuinely differs from vanilla
                // is written; a CIC player owns every unit on the side.
                if (input.WlPlayerIsCic) return NoMacroWrite;
                if (!IsWritableMacro(input.LedgerOpinion)) return NoMacroWrite;
                if (input.LedgerOpinion == input.CurrentMacro || input.LedgerOpinion == input.VanillaPendingMacro)
                    return NoMacroWrite;
                return input.LedgerOpinion;
            }

            int opinion = IsWritableMacro(input.LedgerOpinion)
                ? input.LedgerOpinion
                : IsWritableMacro(input.FallbackOpinion) ? input.FallbackOpinion : NoMacroWrite;
            if (opinion == NoMacroWrite) return NoMacroWrite;
            return opinion == input.CurrentMacro ? NoMacroWrite : opinion;
        }

        // #45 group stance as an arbiter intent (GroupStanceIntentProducer). The
        // answer is the coordinator's cached per-brigade decision
        // (PopulateResolvedDecisionsForSide: doctrine, then ledger task, then the
        // sector-odds scorer). Vanilla's AdjustGroupAIStance (4221-4278) writes
        // 1-4 per pool group; Whiskey writes only 1-3 and never over a vanilla
        // assault (4): an assault is either kept or demoted by the Safety
        // charge-demotion intent (TacticalChargeDenial.TryDemotionIntent). The
        // group must be settled to CHANGE stance — no path (regimentpaths) and no
        // courier order in flight — as the #45 order-friction gate required
        // (TacticalOrderSettlementGate: a marching or still-ordered group is not
        // changed). Priority Scorer: every other stance source outranks it.
        public static bool TryGroupStanceIntent(in UnitRecord group, in TacticalResolvedGroupDecision resolved, out BattleOrderIntent intent)
        {
            intent = default(BattleOrderIntent);
            if ((group.Flags & UnitFlags.IsGroup) == 0 || !TacticalDoctrineScorer.AllowsLocalGroupStanceWriter(group.UnitTyp)) return false;
            if ((group.Flags & UnitFlags.PlayerOwned) != 0) return false;
            if (group.AiStanceOrdered == TacticalCommandTaskToStanceMapper.StanceAssault) return false;
            if (resolved.Kind != TacticalDoctrineDecisionKind.Apply) return false;
            if (resolved.FinalStance < 1 || resolved.FinalStance > 3) return false;
            // Agreeing with the ordered stance is a hold refresh (#45 re-stamped it
            // before its friction gate); only a change waits for a settled group.
            if (group.AiStanceOrdered != resolved.FinalStance && (group.RegimentPaths > 0 || group.QueuedOrders > 0)) return false;

            intent = new BattleOrderIntent
            {
                GroupId = group.InstanceId,
                Channel = OrderChannel.Stance,
                Value = resolved.FinalStance,
                Heading = -1f,
                Priority = IntentPriority.Scorer,
                Source = IntentSource.GroupStanceScorer,
                Reason = resolved.FromDoctrine ? "group-stance-doctrine" : resolved.FromLedger ? "group-stance-ledger" : "group-stance-scorer",
            };
            return true;
        }

        // Cheap pre-check so the patch can skip building opinions when no write
        // is possible this cycle.
        public static bool MacroAiMayHaveOpinion(
            bool modeAllowsWrites,
            bool isFeudInstance,
            bool scriptedAiStrategy,
            bool scriptedSideMacro,
            bool endBattleActive,
            int currentMacro)
        {
            if (!modeAllowsWrites || isFeudInstance) return false;
            if (scriptedAiStrategy || scriptedSideMacro) return false;
            return !endBattleActive && currentMacro != 3;
        }

        // #44 legacy odds: a group's angular enemy pressure from the battle
        // snapshot (SlicePressureBuilder: visible in full, remembered weighted by
        // confidence, never-seen excluded) instead of vanilla's
        // unitrange.enemystrengthwithinangle, which bins true enemy positions
        // (UpdateUnitRangeFast 122719-122733). 0 for a unit outside the snapshot.
        public static float SnapshotPressure(BattleSnapshot s, int ownIndex)
        {
            if (s == null || ownIndex < 0 || ownIndex >= s.OwnCount) return 0f;
            float total = 0f;
            for (int k = 0; k < s.SliceCount; k++)
            {
                float p = s.SlicePressure(ownIndex, k);
                if (p > 0f) total += p;
            }
            return total;
        }

        public static bool IsWritableMacro(int value)
        {
            return value >= 0 && value <= 2;
        }

        // #44 per-instance shadow. Whiskey's value lives in AIBattle.macroai
        // only between its Postfix and the next CheckGlobalAIStrategy; before
        // vanilla runs, the Prefix puts back vanilla's own value captured when
        // Whiskey overwrote it, so vanilla's pipeline never sees Whiskey's value:
        //   6388  newmacroaitoset != macroai && timer expired reverts macroai
        //         (and logs through the ungated Debug.Log in DebugOwnLog 66964)
        //         -> without the shadow it reverted Whiskey's 1/2 every cycle;
        //   6438  newmacroaitoset >= 0 && macroai <= 0 re-stamps
        //         timertosetnewmacroai each cycle, so 6388 never fires again
        //         -> without the shadow a Whiskey 0 locked vanilla in assault.
        // When Whiskey has no opinion the Postfix writes nothing, so the value
        // left is vanilla's own (handed back once; the shadow is consumed).
        // A value someone else wrote in between (current != written, e.g. the
        // debug panel at 189257) is left alone.
        public static int MacroAiRestoreBeforeVanilla(
            bool shadowActive,
            int shadowWritten,
            int shadowVanillaMacro,
            int currentMacro)
        {
            if (!shadowActive || currentMacro != shadowWritten) return NoMacroWrite;
            return shadowVanillaMacro;
        }

        // XZ part of BattlefieldSetup.GetAngleTerrainPosXYStatic (26937).
        public static void AnglePointXYXZ(
            float x,
            float z,
            float angleDegrees,
            float distanceX,
            float distanceZ,
            out float outX,
            out float outZ)
        {
            double a = (angleDegrees - 90.0) * System.Math.PI / 180.0;
            double b = angleDegrees * System.Math.PI / 180.0;
            outX = (float)(x - System.Math.Cos(a) * distanceZ - System.Math.Cos(b) * distanceX);
            outZ = (float)(z + System.Math.Sin(a) * distanceZ + System.Math.Sin(b) * distanceX);
        }

        // #60: BattleUnits.SetGroupFormation (92044-92058) treats targetpos as
        // the line front and places the HQ at
        // GetAngleTerrainPosXY(targetpos, rot, 0, groupdepth + groupunitdistancetoback);
        // with a default targetpos it derives the front from the HQ with the
        // negated offset. A corrected HQ position must be converted the same way
        // or the whole group shifts forward by depth + back.
        public static void DeploymentLineFrontFromHq(
            float hqX,
            float hqZ,
            float facingDegrees,
            float groupDepth,
            float groupUnitDistanceToBack,
            out float frontX,
            out float frontZ)
        {
            AnglePointXYXZ(hqX, hqZ, facingDegrees, 0f, -(groupDepth + groupUnitDistanceToBack), out frontX, out frontZ);
        }

        // The inverse of DeploymentLineFrontFromHq: where SetGroupFormation puts the group's
        // headquarters for a line front (BC10: the executor judges an immediate placement by
        // the group's position, so it compares against this point).
        public static void DeploymentHqFromLineFront(
            float frontX,
            float frontZ,
            float facingDegrees,
            float groupDepth,
            float groupUnitDistanceToBack,
            out float hqX,
            out float hqZ)
        {
            AnglePointXYXZ(frontX, frontZ, facingDegrees, 0f, groupDepth + groupUnitDistanceToBack, out hqX, out hqZ);
        }

        // #53 pathfinder discipline (Regiment.AddPath Postfix, 130259).
        // Vanilla increments regimentpaths for a PathPartial/PathInvalid result
        // only once iterations >= 15 (130511) - a deliberate last resort - and
        // on an endpoint mismatch it keeps the segment and RegimentSetPath
        // (131167-131178) continues from its last corner. So an added path is
        // never removed. The only change: exact float endpoint equality
        // (130517) never matches a navmesh-snapped target, so vanilla burns all
        // 20 iterations adding slivers; an endpoint within tolerance ends it.
        public static AddPathDecision DecideAddPath(
            bool pathAdded,
            int cornerCount,
            int vanillaResult,
            float finalDistance,
            float tolerance)
        {
            if (!pathAdded) return new AddPathDecision(false, vanillaResult, "no-added-path");
            if (vanillaResult != 0) return new AddPathDecision(false, vanillaResult, "vanilla-accepted");
            if (cornerCount <= 0) return new AddPathDecision(false, vanillaResult, "no-corners");
            if (finalDistance >= 0f && finalDistance <= tolerance)
                return new AddPathDecision(true, 1, "endpoint-within-tolerance");
            return new AddPathDecision(false, vanillaResult, "vanilla-chain");
        }

        // BR-H3 / B7 artillery micro writes (ArtilleryIntentProducer intents).
        // Vanilla anchors:
        //   MountUnits 90860-90890: limber = formation 3 + mounted 1, unlimber =
        //     formation 0 + mounted 0 (ChangeRegimentFormation 135440-135452
        //     turns SetFormationParam(0, ..., 1) on artillery into an UNLIMBER);
        //   UnlimberArtilleryAIMicro 3482: only mounted, idle batteries with an
        //     enemy inside fire range, then RotateRegiment toward it;
        //   RotateRegiment (130159) and RegimentSetPath(newpath) (130836) both
        //     StopRegiment, so nothing is written to a moving or retreating
        //     battery (vanilla CheckArtyFallback 3513 likewise needs no paths);
        //   CheckAIBombardment 3904: cancelling bombardment = combat behavior 7.
        // No stance is ever written: batteries are attached sub-units and
        // ChangeStance (90772) would set permanentlydetached, after which
        // vanilla artillery AI skips them (3789, 3834, 3890, 3928).
        public static ArtilleryMicroWrite PlanArtilleryMicroWrite(
            TacticalArtilleryMicroAction action,
            int mounted,
            int regimentPaths,
            int movementMode,
            int combatBehaviorOrdered,
            bool enemyInFireRange)
        {
            if (action == TacticalArtilleryMicroAction.ConserveAmmo)
                return combatBehaviorOrdered == 9 ? ArtilleryMicroWrite.CeaseBombard : ArtilleryMicroWrite.None;

            bool idle = regimentPaths <= 0 && movementMode != 2 && movementMode != 4 && movementMode != 5;
            if (!idle) return ArtilleryMicroWrite.None;

            switch (action)
            {
                case TacticalArtilleryMicroAction.Limber:
                    return mounted != 1 ? ArtilleryMicroWrite.Limber : ArtilleryMicroWrite.None;
                case TacticalArtilleryMicroAction.Unlimber:
                    return mounted == 1 && enemyInFireRange ? ArtilleryMicroWrite.UnlimberAndFace : ArtilleryMicroWrite.None;
                case TacticalArtilleryMicroAction.Retreat:
                    return ArtilleryMicroWrite.Retreat;
                case TacticalArtilleryMicroAction.WheelToBestThreat:
                    return ArtilleryMicroWrite.Face;
                default:
                    return ArtilleryMicroWrite.None;
            }
        }

        // ---- Battle-core artillery (ArtilleryIntentProducer, plan BC5b) ----
        //
        // One pass per arbiter tick over the side's batteries replaces B7 (Postfix
        // on CheckAIBombardment 3869), the #45 all-stance audit and the
        // SetWaypoint point-blank veto. Enemy inputs come from the snapshot's
        // observations only (Visible; a Remembered enemy is not a target in fire
        // range); own-battery fields the snapshot does not carry (mounted, guns,
        // fire range, ammo slots, cover) come in BattleArtilleryState. Writes:
        //   CombatBehavior 7/8 (cease bombardment / counter-battery; vanilla
        //     CheckAIBombardment 3895-3906, CheckCounterBatteryFire 3851-3861).
        //     Bombardment (9) itself stays vanilla's: its ChangeCombatBehavior
        //     comes with bombardposition/bombardrange (3898-3901), which the
        //     CombatBehavior channel does not carry.
        //   Formation 3 + Limber / 0 + Unlimber (MountUnits 90860-90890;
        //     artillery formation 3 <=> mounted 1, ChangeRegimentFormation
        //     135440-135452).
        //   Facing: RotateRegiment toward the enemy (UnlimberArtilleryAIMicro
        //     3482-3495; a standing battery only past vanilla's turn trigger,
        //     4599-4606).
        //   Move (Safety) for a retreat: away from the visible threat by the
        //     fallback distance, validated by the executor (clamp, water/fence
        //     step-back, in-contact line check) instead of CheckArtyFallback's
        //     unchecked RegimentSetPath along retreatangle (3525), which vanilla
        //     derives from true enemy slice strength (122717, 122830).
        // Never a stance: ChangeStance on a battery sets permanentlydetached
        // (90788-90791) and vanilla artillery AI then skips it (3789, 3834, 3890,
        // 3928).

        // UnlimberArtilleryAIMicro / audit: unsupported batteries panic inside
        // musket range (Scourge's DistArtyPanic has no vanilla analog).
        public const float ArtilleryPanicMeters = 100f;
        public const int ArtilleryLimberedFormation = 3;
        public const int ArtilleryUnlimberedFormation = 0;
        public const int CombatBehaviorFireAtWill = 7;
        public const int CombatBehaviorCounterBattery = 8;
        public const int CombatBehaviorBombard = 9;
        /// <summary>Vanilla's start test needs a mean ammo above this (CheckAIBombardment 3894).</summary>
        public const float BombardMinAmmoMean = 0.2f;
        // Vanilla micro facing turns a unit only toward an enemy closer than
        // firerange * 1.5 (AIBattle 4606).
        public const float VanillaTurnReachFireRanges = 1.5f;
        // Old audit/B7 interposition geometry: a friend shields the battery only
        // within ~one regiment frontage of the battery-enemy line.
        private const float InterposedLateralMeters = 100f;
        private const float SupportFallbackRadiusMeters = 200f;

        // A battery the artillery producers may act on: an AI battery, alive,
        // active, not routing, not detached (vanilla artillery AI skips detached
        // batteries for good), not player-owned, and neither it nor any command
        // ancestor feuding (vanilla's envelope is the group's ai_feudstance, 3890).
        public static bool ArtilleryBatteryEligible(BattleSnapshot s, int index)
        {
            if (s == null || index < 0 || index >= s.OwnCount) return false;
            UnitRecord[] own = s.Own;
            UnitFlags f = own[index].Flags;
            if ((f & UnitFlags.IsArtillery) == 0 || (f & UnitFlags.IsGroup) != 0) return false;
            if ((f & UnitFlags.Alive) == 0 || (f & UnitFlags.Active) == 0) return false;
            if ((f & (UnitFlags.Routed | UnitFlags.MarkedForRout | UnitFlags.OnRetreat | UnitFlags.PermanentlyDetached | UnitFlags.PlayerOwned | UnitFlags.Feud)) != 0)
                return false;
            int p = own[index].ParentIndex;
            for (int steps = 0; p >= 0 && p < s.OwnCount && steps < s.OwnCount; steps++)
            {
                if ((own[p].Flags & UnitFlags.Feud) != 0) return false;
                p = own[p].ParentIndex;
            }
            return true;
        }

        // Per-battery hold (Artillery Audit Min Hold Seconds) on the doctrine
        // clock: the battery's last executor outcome is younger than the hold. A
        // stamp from the future (reload, new battle) has expired.
        public static bool ArtilleryHoldActive(double nowHours, double lastOutcomeHours, double holdHours)
        {
            double age = nowHours - lastOutcomeHours;
            return holdHours > 0.0 && age >= 0.0 && age < holdHours;
        }

        // The executor resets bombardstarttime with this write (vanilla
        // CheckAIBombardment 3904 cancels a bombardment with ChangeCombatBehavior 7
        // and bombardstarttime = 0).
        public static bool ResetsBombardStart(in BattleOrderIntent intent)
        {
            return intent.Channel == OrderChannel.CombatBehavior &&
                intent.Value == CombatBehaviorFireAtWill &&
                intent.Source == IntentSource.ArtilleryDoctrine;
        }

        /// <summary>
        /// BC14W (spec § 7A requirement-focal-execution, D34): the intent that starts a bombardment of the preparation point, if vanilla's own per-battery start
        /// test (CheckAIBombardment 3890-3894) would admit this battery for it: artillery, not routed or marked for rout, guns, not permanently detached, not
        /// already on counter-battery (8) or bombardment (9), mean ammo above 0.2, no path, and the point within its fire range (in place of vanilla's closest
        /// enemy within it). The group conditions of vanilla's start (a group whose path was interrupted and is not engaged) are Whiskey's own plan here.
        /// </summary>
        public static bool TryBombardStartIntent(BattleSnapshot s, int index, in BattleArtilleryState b, out BattleOrderIntent intent)
        {
            intent = default(BattleOrderIntent);
            if (s == null || index < 0 || index >= s.OwnCount || !b.HasPreparation) return false;
            ref UnitRecord battery = ref s.Own[index];
            UnitFlags f = battery.Flags;
            if ((f & UnitFlags.IsArtillery) == 0 || (f & UnitFlags.IsGroup) != 0) return false;
            if ((f & (UnitFlags.Routed | UnitFlags.MarkedForRout | UnitFlags.PermanentlyDetached)) != 0) return false;
            if (b.Guns <= 0 || battery.RegimentPaths > 0) return false;
            if (battery.CombatBehaviorOrdered == CombatBehaviorCounterBattery || battery.CombatBehaviorOrdered == CombatBehaviorBombard) return false;
            if (!(b.AmmoMean > BombardMinAmmoMean)) return false;
            if (DistSq(battery.X, battery.Z, b.PreparationX, b.PreparationZ) > b.FireRange * b.FireRange) return false;
            intent = new BattleOrderIntent
            {
                GroupId = battery.InstanceId, Channel = OrderChannel.CombatBehavior, Value = CombatBehaviorBombard,
                TargetX = b.PreparationX, TargetZ = b.PreparationZ, Heading = -1f, Flags = IntentFlags.BombardStart,
                Priority = IntentPriority.Executor, Source = IntentSource.ArtilleryDoctrine, Reason = "artillery-bombard-start",
            };
            return true;
        }

        // BC14W: a battery whose group order carries the preparation point keeps bombarding through the doctrine's cancel and counter-battery rules, except
        // the safety ones: ammo below 0.10, an unsupported battery with the enemy inside 0.2 of its range, and friendly infantry or cavalry in danger close.
        private static bool PreparationKeepsBombarding(BattleSnapshot s, int index, in BattleArtilleryState b, TacticalSupportScreen.Result screen, bool enemyClose)
        {
            if (!b.HasPreparation) return false;
            if (b.AmmoRatio01 < 0.10f) return false;
            if (screen == TacticalSupportScreen.Result.Unsupported && enemyClose) return false;
            return !FriendlyDangerClose(s, index, b.FireRange);
        }

        // Doctrine + micro intents for battery `index` (ArtilleryBatteryEligible
        // checked by the caller). Returns how many were written to buffer (at most
        // one per channel; up to 3: behavior, formation, facing). Limber is Safety:
        // the micro doctrine limbers only to escape (point-blank or unsupported
        // close), so it must pass the executor's recent-order cooldown.
        public static int ArtilleryDoctrineIntents(BattleSnapshot s, int index, in BattleArtilleryState b, in BattleArtilleryRules r, BattleOrderIntent[] buffer)
        {
            if (s == null || buffer == null || buffer.Length < 3 || b.InHold || b.Guns <= 0) return 0;
            ref UnitRecord battery = ref s.Own[index];
            int n = 0;

            int closest = s.ClosestVisibleEnemy(index, out float enemyDistance);
            bool hasEnemy = closest >= 0;
            float ex = hasEnemy ? s.Observations[closest].X : 0f;
            float ez = hasEnemy ? s.Observations[closest].Z : 0f;
            bool enemyArtilleryInRange = VisibleEnemyArtilleryWithin(s, battery.X, battery.Z, b.FireRange);
            int support = CountInterposedSupport(s, index, hasEnemy, ex, ez, enemyDistance);

            // 1. Counter-battery (8) has no reachable vanilla release (3834).
            bool behaviorWritten = false;
            if (r.DoctrineOn && ShouldReleaseCounterBattery(battery.CombatBehaviorOrdered, enemyArtilleryInRange))
            {
                buffer[n++] = BatteryIntent(battery, OrderChannel.CombatBehavior, CombatBehaviorFireAtWill, IntentPriority.Executor, "artillery-counterbattery-release");
                behaviorWritten = true;
            }

            // 1b. BC14W: the focal assault's preparation. A battery of the plan's children that vanilla's own start test admits for the preparation point
            // starts a bombardment of it (the executor writes the point and the range as vanilla does).
            if (r.DoctrineOn && !behaviorWritten && TryBombardStartIntent(s, index, in b, out BattleOrderIntent bombard))
            {
                buffer[n++] = bombard;
                behaviorWritten = true;
            }

            // 2. Doctrine over a vanilla bombardment (9): cancel (7) or counter-battery (8).
            if (r.DoctrineOn && !behaviorWritten && battery.CombatBehaviorOrdered == CombatBehaviorBombard)
            {
                var screen = TacticalSupportScreen.Score(new TacticalSupportScreen.Input
                {
                    ProtectedUnitMorale = b.Morale,
                    MoraleFallbackThreshold = b.FallbackMoraleThreshold,
                    BattleStartMorale = b.BattleStartMorale,
                    EnemyDistance = hasEnemy ? enemyDistance : 9999f,
                    DangerRadius = r.FallbackDistance,
                    ScreenUnitCount = support,
                    AiFeudStance = -1,
                    IsPlayerAiOrFeud = 0,
                });
                var decision = TacticalArtilleryDoctrine.Score(new TacticalArtilleryDoctrine.Input
                {
                    ScreenResult = screen,
                    AmmoTotalRatio = b.AmmoRatio01,
                    CanisterAmmo = b.CanisterAmmo,
                    ClosestEnemyDistance = hasEnemy ? enemyDistance : 9999f,
                    UnitFireRange = b.FireRange,
                    EnemyArtilleryVisible = enemyArtilleryInRange,
                    CombatBehaviorOrdered = battery.CombatBehaviorOrdered,
                    AiFeudStance = -1,
                    IsPlayerAiOrFeud = 0,
                    DoctrineDecision = b.Doctrine,
                });
                // BC14W: the doctrine does not stop the bombardment it started for the focal assault (the safety rules stay).
                if ((decision == TacticalArtilleryDoctrine.Decision.CancelBombard || decision == TacticalArtilleryDoctrine.Decision.CounterBattery) &&
                    PreparationKeepsBombarding(s, index, in b, screen, (hasEnemy ? enemyDistance : 9999f) <= b.FireRange * 0.20f))
                    decision = TacticalArtilleryDoctrine.Decision.PreserveFire;
                if (decision == TacticalArtilleryDoctrine.Decision.CancelBombard)
                {
                    buffer[n++] = BatteryIntent(battery, OrderChannel.CombatBehavior, CombatBehaviorFireAtWill, IntentPriority.Executor, "artillery-cancel-bombard");
                    behaviorWritten = true;
                }
                else if (decision == TacticalArtilleryDoctrine.Decision.CounterBattery)
                {
                    buffer[n++] = BatteryIntent(battery, OrderChannel.CombatBehavior, CombatBehaviorCounterBattery, IntentPriority.Executor, "artillery-counterbattery");
                    behaviorWritten = true;
                }
            }

            if (!r.AuditOn) return n;

            // 3. Micro doctrine (limber / unlimber / face / retreat / conserve).
            bool limbered = b.Mounted == 1;
            QuadrantThreats(s, battery.X, battery.Z, battery.RotationY, b.FireRange, out float currentThreat, out float bestThreat,
                out float threatX, out float threatZ);
            var micro = TacticalArtilleryMicroDoctrine.Decide(new TacticalArtilleryMicroInput(
                hasEnemy: hasEnemy,
                enemyCloseDistance: enemyDistance,
                panicDistance: ArtilleryPanicMeters,
                supportCount: support,
                moraleBonus: b.GroupMorale >= 0.6f,
                enemyRoutedOrRetreating: false, // routed enemies leave the observations (BC2a)
                limberState: limbered ? TacticalArtilleryLimberState.Limbered : TacticalArtilleryLimberState.Unlimbered,
                ammoRatio01: b.AmmoRatio01,
                targetDistance: enemyDistance,
                maxRange: b.FireRange,
                currentQuadrantThreat: currentThreat,
                bestQuadrantThreat: bestThreat,
                canWheel: battery.RegimentPaths <= 0 && !limbered,
                frontalTargetInRange: currentThreat > 0f));
            bool enemyInFireRange = hasEnemy && enemyDistance <= b.FireRange;
            ArtilleryMicroWrite write = PlanArtilleryMicroWrite(micro.Action, b.Mounted, battery.RegimentPaths, battery.MovementMode,
                battery.CombatBehaviorOrdered, enemyInFireRange);
            float towardEnemy = hasEnemy ? HeadingTo(battery.X, battery.Z, ex, ez) : -1f;
            switch (write)
            {
                case ArtilleryMicroWrite.CeaseBombard:
                    if (!behaviorWritten)
                        buffer[n++] = BatteryIntent(battery, OrderChannel.CombatBehavior, CombatBehaviorFireAtWill, IntentPriority.Executor, "artillery-conserve-ammo");
                    break;
                case ArtilleryMicroWrite.Limber:
                {
                    BattleOrderIntent limber = BatteryIntent(battery, OrderChannel.Formation, ArtilleryLimberedFormation, IntentPriority.Safety, "artillery-limber");
                    limber.Flags = IntentFlags.Limber;
                    buffer[n++] = limber;
                    break;
                }
                case ArtilleryMicroWrite.UnlimberAndFace:
                {
                    BattleOrderIntent unlimber = BatteryIntent(battery, OrderChannel.Formation, ArtilleryUnlimberedFormation, IntentPriority.Executor, "artillery-unlimber");
                    unlimber.Flags = IntentFlags.Unlimber;
                    buffer[n++] = unlimber;
                    if (towardEnemy >= 0f)
                    {
                        BattleOrderIntent face = BatteryIntent(battery, OrderChannel.Facing, 0, IntentPriority.Executor, "artillery-unlimber-face");
                        face.Heading = towardEnemy;
                        buffer[n++] = face;
                    }
                    break;
                }
                case ArtilleryMicroWrite.Face:
                {
                    // The wheel is chosen by quadrant strength, so it faces the
                    // winning quadrant's strongest visible enemy in fire range
                    // (vanilla micro facing turns toward one enemy unit's
                    // position, GetAngleTerrain(own, enemy) + 180, 4603); with no
                    // enemy in fire range, the closest visible one, but only inside
                    // vanilla's turn reach closestDistance < firerange * 1.5 (4606).
                    float wheelTo = bestThreat > 0f ? HeadingTo(battery.X, battery.Z, threatX, threatZ)
                        : hasEnemy && enemyDistance < b.FireRange * VanillaTurnReachFireRanges ? towardEnemy : -1f;
                    if (wheelTo >= 0f &&
                        ShouldRotateTowardTarget(battery.RotationY, wheelTo, r.TurnTriggerDegrees, b.CoverValue, b.CoverObject,
                            r.MaxCoverValueForRotation, r.AdditionalAngleInCover))
                    {
                        BattleOrderIntent face = BatteryIntent(battery, OrderChannel.Facing, 0, IntentPriority.Executor, "artillery-face");
                        face.Heading = wheelTo;
                        buffer[n++] = face;
                    }
                    break;
                }
                case ArtilleryMicroWrite.Retreat:
                    if (hasEnemy)
                        buffer[n++] = ArtilleryRetreatIntent(battery, ex, ez, r.FallbackDistance, IntentSource.ArtilleryDoctrine, "artillery-retreat");
                    break;
            }
            return n;
        }

        // Point-blank watchdog (replaces ArtillerySetWaypointVetoPatch): a battery
        // vanilla sent (regimentpaths > 0; e.g. the delayed group move 4146) to a
        // waypoint within ArtilleryPointBlankMeters of a visible enemy is pulled
        // back with a Safety retreat. Whiskey's own targets never get there
        // (WaypointValidator rejects them, PointBlank).
        public static bool TryArtilleryPointBlankIntent(BattleSnapshot s, int index, float fallbackDistance, out BattleOrderIntent intent)
        {
            intent = default(BattleOrderIntent);
            if (s == null || index < 0 || index >= s.OwnCount) return false;
            ref UnitRecord battery = ref s.Own[index];
            if (battery.RegimentPaths <= 0) return false;
            if (battery.LastWaypointX == 0f && battery.LastWaypointZ == 0f) return false;
            EnemyObservation[] obs = s.Observations;
            float r2 = WaypointValidator.ArtilleryPointBlankMeters * WaypointValidator.ArtilleryPointBlankMeters;
            int threat = -1;
            float best = float.MaxValue;
            for (int k = 0; k < s.ObservationCount; k++)
            {
                if (obs[k].State != ObservationState.Visible) continue;
                float d2 = DistSq(obs[k].X, obs[k].Z, battery.LastWaypointX, battery.LastWaypointZ);
                if (d2 <= r2 && d2 < best)
                {
                    best = d2;
                    threat = k;
                }
            }
            if (threat < 0) return false;
            intent = ArtilleryRetreatIntent(battery, obs[threat].X, obs[threat].Z, fallbackDistance, IntentSource.ArtilleryPointBlank, "artillery-point-blank");
            return true;
        }

        // Safety retreat away from (enemyX, enemyZ) by the fallback distance, ending
        // facing the enemy (vanilla faces retreatangle + 180, 3525). Immediate and
        // line-checked (InContactWithdrawal), limbered first (Limber), allowed onto
        // the supporting line (ReliefOrSupport). The executor validates the target.
        public static BattleOrderIntent ArtilleryRetreatIntent(in UnitRecord battery, float enemyX, float enemyZ, float fallbackDistance, IntentSource source, string reason)
        {
            float distance = fallbackDistance > 0f ? fallbackDistance : 200f;
            float dx = battery.X - enemyX;
            float dz = battery.Z - enemyZ;
            float d = (float)System.Math.Sqrt(dx * dx + dz * dz);
            float ux, uz;
            if (d > 0.01f)
            {
                ux = dx / d;
                uz = dz / d;
            }
            else
            {
                double rear = (battery.RotationY + 180f) * System.Math.PI / 180.0;
                ux = (float)System.Math.Sin(rear);
                uz = (float)System.Math.Cos(rear);
            }
            return new BattleOrderIntent
            {
                GroupId = battery.InstanceId,
                Channel = OrderChannel.Move,
                TargetX = battery.X + ux * distance,
                TargetZ = battery.Z + uz * distance,
                Heading = HeadingTo(battery.X, battery.Z, enemyX, enemyZ),
                Priority = IntentPriority.Safety,
                Source = source,
                Flags = IntentFlags.InContactWithdrawal | IntentFlags.ReliefOrSupport | IntentFlags.Limber,
                Task = CommandTaskType.FallBackToLine,
                Reason = reason,
            };
        }

        // Unity yaw from (x, z) toward (tx, tz), [0, 360): vanilla
        // Tools.GetAngle(own, target) + 180 (151304: atan2(dx, dz) - 180).
        public static float HeadingTo(float x, float z, float tx, float tz)
        {
            float h = (float)(System.Math.Atan2(tx - x, tz - z) * 180.0 / System.Math.PI);
            if (h < 0f) h += 360f;
            return h >= 360f ? h - 360f : h;
        }

        // Friendly infantry/cavalry (unittyp 0, 1, 4) between the battery and the
        // enemy: on the enemy side, closer than the enemy and within one frontage
        // of the line. Without an enemy, friends within 200 m. Port of the #45
        // audit's CountFriendlyInfantryCavInterposed over the snapshot.
        public static int CountInterposedSupport(BattleSnapshot s, int index, bool hasEnemy, float enemyX, float enemyZ, float enemyDistance)
        {
            UnitRecord[] own = s.Own;
            float ax = own[index].X;
            float az = own[index].Z;
            int count = 0;
            float tx = enemyX - ax;
            float tz = enemyZ - az;
            float len2 = tx * tx + tz * tz;
            bool geometry = hasEnemy && enemyDistance > 1f && len2 >= 1f;
            float len = geometry ? (float)System.Math.Sqrt(len2) : 0f;
            for (int j = 0; j < s.OwnCount; j++)
            {
                if (j == index) continue;
                ref UnitRecord f = ref own[j];
                if (f.UnitTyp != TacticalUnitType.Infantry && f.UnitTyp != TacticalUnitType.Cavalry && f.UnitTyp != TacticalUnitType.Scout) continue;
                if ((f.Flags & (UnitFlags.Routed | UnitFlags.MarkedForRout | UnitFlags.PermanentlyDetached)) != 0) continue;
                if ((f.Flags & UnitFlags.Alive) == 0) continue;
                float fx = f.X - ax;
                float fz = f.Z - az;
                if (!geometry)
                {
                    if (fx * fx + fz * fz <= SupportFallbackRadiusMeters * SupportFallbackRadiusMeters) count++;
                    continue;
                }
                float dot = fx * tx + fz * tz;
                if (dot <= 0f) continue;
                float proj = dot / len;
                if (proj >= len) continue;
                float lateral2 = fx * fx + fz * fz - proj * proj;
                if (lateral2 > InterposedLateralMeters * InterposedLateralMeters) continue;
                count++;
            }
            return count;
        }

        // CheckCounterBatteryFire's enemy-battery test (3843) over what is seen:
        // a visible enemy battery within fire range.
        public static bool VisibleEnemyArtilleryWithin(BattleSnapshot s, float x, float z, float range)
        {
            EnemyObservation[] obs = s.Observations;
            float r2 = range * range;
            for (int k = 0; k < s.ObservationCount; k++)
            {
                if (obs[k].State != ObservationState.Visible || obs[k].UnitTyp != TacticalUnitType.Artillery) continue;
                if (DistSq(obs[k].X, obs[k].Z, x, z) <= r2) return true;
            }
            return false;
        }

        // Visible enemy strength in fire range per 90-degree quadrant around the
        // battery's facing: current = the front quadrant, best = the strongest.
        // Feeds the micro doctrine's wheel test (best > current + 1000);
        // (bestX, bestZ) is the strongest single enemy of the winning quadrant, the
        // wheel's facing target. A remembered enemy is never in fire range.
        public static void QuadrantThreats(BattleSnapshot s, float x, float z, float facing, float range, out float current, out float best,
            out float bestX, out float bestZ)
        {
            float q0 = 0f, q1 = 0f, q2 = 0f, q3 = 0f;
            // Per quadrant: strongest enemy index and its strength (stack, no alloc).
            int t0 = -1, t1 = -1, t2 = -1, t3 = -1;
            float s0 = 0f, s1 = 0f, s2 = 0f, s3 = 0f;
            EnemyObservation[] obs = s.Observations;
            float r2 = range * range;
            for (int k = 0; k < s.ObservationCount; k++)
            {
                if (obs[k].State != ObservationState.Visible) continue;
                if (DistSq(obs[k].X, obs[k].Z, x, z) > r2) continue;
                float rel = HeadingTo(x, z, obs[k].X, obs[k].Z) - facing + 45f;
                rel %= 360f;
                if (rel < 0f) rel += 360f;
                float strength = obs[k].Strength > 0f ? obs[k].Strength : 1f;
                switch ((int)(rel / 90f))
                {
                    case 0: q0 += strength; if (strength > s0) { s0 = strength; t0 = k; } break;
                    case 1: q1 += strength; if (strength > s1) { s1 = strength; t1 = k; } break;
                    case 2: q2 += strength; if (strength > s2) { s2 = strength; t2 = k; } break;
                    default: q3 += strength; if (strength > s3) { s3 = strength; t3 = k; } break;
                }
            }
            current = q0;
            best = q0;
            int target = t0;
            if (q1 > best) { best = q1; target = t1; }
            if (q2 > best) { best = q2; target = t2; }
            if (q3 > best) { best = q3; target = t3; }
            bestX = target >= 0 ? obs[target].X : x;
            bestZ = target >= 0 ? obs[target].Z : z;
        }

        // Fog-honest replacement for vanilla groupenemiesinrange (#44 sector
        // input). Vanilla's UpdateGroupStrength sets it from the group's OWN
        // unitrange.enemiesinrange (120558/120563: the HQ-local reach), the
        // strength of every enemy within max(maximumlineofsight,
        // distancetoenemytocancelinterruption) of the unit (122608,
        // 122650-122655), not fog-filtered for the AI side. Here: the enemies this
        // side perceives within rangeMeters of the unit's own position — visible in
        // full, remembered by confidence, off-map reinforcements never. The one
        // implementation of this rule (BC5a's posture sense calls it too).
        public static float SnapshotEnemyStrengthInRange(IBattleUnitView s, int index, float rangeMeters)
        {
            if (s == null || index < 0 || index >= s.OwnCount || rangeMeters <= 0f) return 0f;
            float x = s.Own[index].X;
            float z = s.Own[index].Z;
            EnemyObservation[] obs = s.Observations;
            float r2 = rangeMeters * rangeMeters;
            float total = 0f;
            for (int k = 0; k < s.ObservationCount; k++)
            {
                if (obs[k].IsReinforcement) continue;
                if (DistSq(obs[k].X, obs[k].Z, x, z) > r2) continue;
                total += obs[k].State == ObservationState.Visible ? obs[k].Strength : obs[k].Strength * obs[k].Confidence01;
            }
            return total;
        }

        // #44 sector flank risk from what the side perceives, replacing vanilla
        // flanksthreated / outflanked (UpdateGroupStrength 120566-120580 over
        // UpdateUnitRangeFast's unfiltered range lists, 122650, 122976+): the
        // group's own outflanked state over the snapshot's flank pressure, or a
        // positive mean vanilla outflanked weight of its regiments
        // (ArmyEvidenceBuilder.MeanFlankThreatByGroup, filled by the caller).
        public static bool PerceivedFlankRisk(BattleSnapshot s, int index, float[] meanFlankThreat)
        {
            if (s == null || index < 0 || index >= s.OwnCount) return false;
            if (ArmyEvidenceBuilder.OutflankedState(s.FlankPressure(index, 0), s.FlankPressure(index, 1), s.FlankPressure(index, 2)) != 0)
                return true;
            return meanFlankThreat != null && index < meanFlankThreat.Length && meanFlankThreat[index] > 0f;
        }

        // Within max(80 m, 20 % of fire range) of a friendly infantry/cavalry
        // regiment: the doctrine's danger-close input (B7 HasFriendlyCloseRangeRisk
        // over the snapshot instead of vanilla's range list).
        public static bool FriendlyDangerClose(BattleSnapshot s, int index, float fireRange)
        {
            UnitRecord[] own = s.Own;
            float close = System.Math.Max(80f, fireRange * 0.20f);
            for (int j = 0; j < s.OwnCount; j++)
            {
                if (j == index) continue;
                if (own[j].UnitTyp != TacticalUnitType.Infantry && own[j].UnitTyp != TacticalUnitType.Cavalry) continue;
                if ((own[j].Flags & (UnitFlags.Routed | UnitFlags.MarkedForRout)) != 0) continue;
                if (DistSq(own[j].X, own[j].Z, own[index].X, own[index].Z) <= close * close) return true;
            }
            return false;
        }

        private static BattleOrderIntent BatteryIntent(in UnitRecord battery, OrderChannel channel, int value, IntentPriority priority, string reason)
        {
            return new BattleOrderIntent
            {
                GroupId = battery.InstanceId,
                Channel = channel,
                Value = value,
                Heading = -1f,
                Priority = priority,
                Source = IntentSource.ArtilleryDoctrine,
                Reason = reason,
            };
        }

        private static float DistSq(float ax, float az, float bx, float bz)
        {
            float dx = ax - bx;
            float dz = az - bz;
            return dx * dx + dz * dz;
        }

        // Face writes (B7 + all-stance audit) follow vanilla's micro facing rule
        // (AIBattle 4599-4606): RotateRegiment only when
        // Tools.GetAbsAngleDifference(transform yaw, target) >=
        // angletriggerforturningtowardsenemy, plus additionalanglewhenincovermicro
        // when the unit sits in cover (covervalue > maxcovervalueforgrouprotation
        // and coverobject != 3, 4600-4603). RotateRegiment (130159) stops the
        // regiment and clears its target, so re-issuing it to an already-facing
        // battery every hold window is not harmless.
        public static bool ShouldRotateTowardTarget(
            float currentYaw,
            float targetYaw,
            float turnTriggerDegrees,
            float coverValue,
            int coverObject,
            float maxCoverValueForRotation,
            float additionalAngleInCover)
        {
            if (!IsFinite(currentYaw) || !IsFinite(targetYaw)) return false;
            float trigger = IsFinite(turnTriggerDegrees) ? turnTriggerDegrees : 0f;
            if (coverValue > maxCoverValueForRotation && coverObject != 3 && IsFinite(additionalAngleInCover))
                trigger += additionalAngleInCover;
            return AbsAngleDifference(currentYaw, targetYaw) >= trigger;
        }

        // Port of Tools.GetAbsAngleDifference (151779) with its defaults.
        public static float AbsAngleDifference(float angle1, float angle2)
        {
            float diff = angle1 - angle2;
            while (diff < -180f) diff += 360f;
            while (diff > 180f) diff -= 360f;
            return System.Math.Abs(diff);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        // Counter-battery (combat behavior 8) has no reachable vanilla release:
        // CheckCounterBatteryFire skips units already at 8 (3834), so its own
        // 8 -> 7 branch is dead, and CheckAIBombardment skips 8 too (3890).
        // Release to fire-at-will (7) once no enemy battery is in fire range.
        public static bool ShouldReleaseCounterBattery(int combatBehaviorOrdered, bool enemyArtilleryInFireRange)
        {
            return combatBehaviorOrdered == 8 && !enemyArtilleryInFireRange;
        }

        // #42: CheckForFeudGroupActions (4931) targets
        // GetClosestEnemyUnit(GamePrefs.neededdistancefeudgroupmovement); the
        // FOW-visible replacement must keep that distance cap.
        public static bool FeudMoveTargetAllowed(bool hasVisibleEnemy, float distance, float neededDistance)
        {
            return hasVisibleEnemy && distance <= neededDistance;
        }

        // #42 fails closed: vanilla's feud move (4931-4960) has no ownership
        // check, so the body is never handed back to it. With the side's battle
        // snapshot the executor decides (guard, chain fan-out around player-owned
        // descendants); without one the replay is a direct move only where
        // TacticalWriteGuard.CanWrite(Regiment, feudReplay: true) allows it.
        public static FeudReplayPath DecideFeudReplayPath(bool hasSnapshot, bool canWriteFeudReplay)
        {
            if (hasSnapshot) return FeudReplayPath.Executor;
            return canWriteFeudReplay ? FeudReplayPath.DirectGuarded : FeudReplayPath.Refused;
        }

        // #42's replay of vanilla's feud move (4955-4957) for own record `index`:
        // toward the closest VISIBLE enemy (the snapshot's, never a remembered one)
        // inside the feud cap, delayed like vanilla (useorderdelay: true). Support
        // priority: an arbiter claim above it on the group's Move refuses it
        // (OrderExecutor.InlineClaimCheck). Executed only through
        // OrderExecutorRuntime.ExecuteInline (the arbiter drops FeudReplay intents).
        public static bool TryFeudReplayIntent(BattleSnapshot s, int index, float neededDistance, out BattleOrderIntent intent)
        {
            intent = default(BattleOrderIntent);
            if (s == null || index < 0 || index >= s.OwnCount) return false;
            int closest = s.ClosestVisibleEnemy(index, out float distance);
            if (!FeudMoveTargetAllowed(closest >= 0, distance, neededDistance)) return false;
            intent = new BattleOrderIntent
            {
                GroupId = s.Own[index].InstanceId,
                Channel = OrderChannel.Move,
                TargetX = s.Observations[closest].X,
                TargetZ = s.Observations[closest].Z,
                Heading = -1f,
                FireTargetId = s.Observations[closest].InstanceId,
                Priority = IntentPriority.Support,
                Source = IntentSource.FeudReplay,
                Flags = IntentFlags.UseOrderDelay,
                Reason = "feud-replay",
            };
            return true;
        }

        // #60: BattleUnits.DoPlacementAIUnitsWithinDeploymentzoneNew sets
        // num3 = 1 for a defender (GetBattleType == 2) or an attacker (== 1)
        // whose groupmorale <= minimummoraleforaistances[1]; fewer than 3
        // eligible sub-units force num3 = 2. num3 == 1 places the group on
        // fortification lines with ignoredeplyomentzone: true and
        // PlaceSingleBrigades - outside-zone placement there is deliberate.
        public static bool WouldTakeFortifiedDeploymentPath(
            int battleType,
            float groupMorale,
            float minimumMoraleForStance1,
            int eligibleSubUnits)
        {
            if (eligibleSubUnits < 3) return false;
            if (battleType == 2) return true;
            return battleType == 1 && groupMorale <= minimumMoraleForStance1;
        }

        // #60 eligibility: never fleets (unittyp 17); no correction when the
        // deployment zone has fewer than 2 frontline vectors (every point would
        // read as out of zone); never undo vanilla's fortified placement.
        public static bool DeploymentCorrectionAllowed(int unitTyp, int frontlineVectorCount, bool fortifiedPath)
        {
            return unitTyp != 17 && frontlineVectorCount >= 2 && !fortifiedPath;
        }

        // #55: "same command" for Regiment.MoveNonAIUnits' auto-link (129026):
        // either unit in the other's allattachedunits (GetComponentsInChildren,
        // 129574), or both under the same real parent per
        // BattleUnits.GetParentUnit (93883 - a root-level unit is its own parent).
        public static bool HqLinkSameCommand(
            bool targetInSourceAttached,
            bool sourceInTargetAttached,
            int sourceId,
            int targetId,
            int sourceParentId,
            int targetParentId)
        {
            if (targetInSourceAttached || sourceInTargetAttached) return true;
            if (sourceParentId == sourceId || targetParentId == targetId) return false;
            return sourceParentId == targetParentId;
        }

        // #34: artillery guns are derived, not stored: Regiment.Initialize sets
        // guns = GetSollGuns(forinitialization: true) (112681), which on the
        // campaign map counts sprites from strength (GetSpriteAmounts(-1,
        // calculateonly: true), 135231/127049). After CombineUnits moves men,
        // the durable value is that same derivation; in battle the sprite count
        // is stale, so the guns are left to vanilla.
        public static int GunsAfterCombine(bool isArtillery, bool calledFromCampaign, int sollGuns, int currentGuns)
        {
            if (!isArtillery || !calledFromCampaign || sollGuns < 0) return currentGuns;
            return sollGuns;
        }

        // #59 pre-check — AIBattle.CheckUseOfReserves 6093-6113: vanilla only
        // commits a reserve when an attached unit (unittyp <= 2, no paths, not
        // routed) is outflanked (1|5 left, 2|6 right) on a side its own flank
        // unit (ownonflank) does not cover. Otherwise it returns before writing,
        // so there is nothing to snapshot or deny.
        public static bool IsEndangeredForReserveSupport(
            int unitTyp,
            int regimentPaths,
            bool routed,
            int outflanked,
            int ownOnFlank)
        {
            if (unitTyp > 2 || regimentPaths > 0 || routed || outflanked <= 0) return false;
            if ((outflanked == 1 || outflanked == 5) && ownOnFlank != 1) return true;
            return (outflanked == 2 || outflanked == 6) && ownOnFlank != 2;
        }

        // CheckUseOfReserves 6121: the support unit vanilla may send is
        // unittyp <= 1, idle, not routed, not outflanked and not engaged (the
        // distance/cover/W&L terms only narrow this set further).
        public static bool IsReserveSupportCandidate(
            int unitTyp,
            int regimentPaths,
            bool routed,
            int outflanked,
            bool inEngagement)
        {
            return unitTyp <= 1 && regimentPaths <= 0 && !routed && outflanked <= 0 && !inEngagement;
        }

        // #56: BattleUnits.SetWaypoint only queues a courier order for a unit
        // with a groupposition (91449 forces useorderdelay = false otherwise;
        // groupposition is built only by SetGroupFormation 92006-92030, for the
        // formed group and the units attached to it), that is not a unittyp 5
        // and has no parentregiment (91463). For any other unit the "delayed"
        // re-issue is just another immediate path, so it is skipped.
        public static bool ReserveDelayConversionApplies(bool hasGroupPosition, int unitTyp, bool hasParentRegiment)
        {
            return hasGroupPosition && unitTyp != 5 && !hasParentRegiment;
        }

        // Vanilla's RegimentSetPath(newpath: true) (6170 -> 130836) runs
        // StopRegiment(manualstop: true), which removes the unit from pontoon /
        // obstacle construction and re-forms it out of march column through
        // ChangeRegimentFormation. A field snapshot cannot undo those, so the
        // gate only rolls back units where they could not have happened.
        public static bool ReserveRollbackIsSafe(
            bool hasPontoonConstruction,
            bool hasObstacleConstruction,
            int formation,
            int lastFormation)
        {
            return !hasPontoonConstruction && !hasObstacleConstruction && formation == lastFormation;
        }
    }
}
