using WhiskeyRealism.Tactical.Operations;

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

    // Per-chain inputs of AIBattle.UpdateMovingTargets (6870) that decide
    // whether vanilla reaches the movement branch for an objective chain.
    internal readonly struct ObjectiveChainMovementProbe
    {
        public ObjectiveChainMovementProbe(
            bool hasCenter,
            int objectiveCount,
            bool hasFirstObjective,
            float firstObjectiveTimeToMove,
            float nowBattleHours,
            int centerUnitTyp,
            int centerRegimentPaths,
            float groupSubordinatesMovingNotFar,
            int groupPositionAttachedUnits,
            bool pathInterrupted,
            bool blockedMovingOrderRunning,
            bool moveOrderActive,
            float combatEngagedNonArtillery,
            bool assaultMoveActive,
            bool ableToFight)
        {
            HasCenter = hasCenter;
            ObjectiveCount = objectiveCount;
            HasFirstObjective = hasFirstObjective;
            FirstObjectiveTimeToMove = firstObjectiveTimeToMove;
            NowBattleHours = nowBattleHours;
            CenterUnitTyp = centerUnitTyp;
            CenterRegimentPaths = centerRegimentPaths;
            GroupSubordinatesMovingNotFar = groupSubordinatesMovingNotFar;
            GroupPositionAttachedUnits = groupPositionAttachedUnits;
            PathInterrupted = pathInterrupted;
            BlockedMovingOrderRunning = blockedMovingOrderRunning;
            MoveOrderActive = moveOrderActive;
            CombatEngagedNonArtillery = combatEngagedNonArtillery;
            AssaultMoveActive = assaultMoveActive;
            AbleToFight = ableToFight;
        }

        public bool HasCenter { get; }
        public int ObjectiveCount { get; }
        public bool HasFirstObjective { get; }
        public float FirstObjectiveTimeToMove { get; }
        public float NowBattleHours { get; }
        public int CenterUnitTyp { get; }
        public int CenterRegimentPaths { get; }
        public float GroupSubordinatesMovingNotFar { get; }
        // -1 when the center has no groupposition.
        public int GroupPositionAttachedUnits { get; }
        public bool PathInterrupted { get; }
        public bool BlockedMovingOrderRunning { get; }
        public bool MoveOrderActive { get; }
        public float CombatEngagedNonArtillery { get; }
        public bool AssaultMoveActive { get; }
        public bool AbleToFight { get; }
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

        // XZ part of BattlefieldSetup.GetAngleTerrainPosStatic (26891). The mod
        // assembly does not reference UnityEngine.TerrainModule, so patches use
        // this and sample the height through BattlefieldSetup.GetTerrainHeight.
        // (Vanilla also clamps to the terrain bounds; callers move < 1 km.)
        public static void AnglePointXZ(float x, float z, float angleDegrees, float distance, out float outX, out float outZ)
        {
            double a = (angleDegrees - 90.0) * System.Math.PI / 180.0;
            outX = (float)(x + System.Math.Cos(a) * distance);
            outZ = (float)(z - System.Math.Sin(a) * distance);
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

        // BR-C2 artillery waypoint veto (BattleUnits.SetWaypoint Postfix, 91232).
        // showmovementoptions is the player-UI flag: it defaults true and every
        // vanilla AI call site passes false (3139, 3643, 4146, 4768, 4957, 6235,
        // 6959), so a true value means a player order and is never vetoed.
        // The distance is from the nearest visible enemy to the TARGET.
        public static bool ShouldVetoArtilleryWaypoint(
            int unitTyp,
            int guns,
            bool routedOrMarked,
            bool permanentlyDetached,
            bool showMovementOptions,
            bool canWrite,
            float nearestEnemyToTargetSq,
            float pointBlankMeters)
        {
            if (unitTyp != 2 || guns <= 0) return false;
            if (showMovementOptions) return false;
            if (routedOrMarked || permanentlyDetached) return false;
            if (!canWrite) return false;
            return nearestEnemyToTargetSq <= pointBlankMeters * pointBlankMeters;
        }

        // BR-H3 / B7 artillery micro writes (TacticalArtilleryApplier).
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

        // R14: gates/writers only intervene when Tactical Commander Mode is
        // Active and their own flag is on; otherwise vanilla runs untouched.
        public static bool GateMayIntervene(bool modeAllowsWrites, bool gateEnabled)
        {
            return modeAllowsWrites && gateEnabled;
        }

        // #46: AIBattle.UpdateMovingTargets returns at 6872 when
        // state < 4 || macroai == 3 || macroai == -1.
        public static bool ObjectiveChainCycleActive(int state, int macroAi)
        {
            return state >= 4 && macroAi != 3 && macroAi != -1;
        }

        // #46: UpdateMovingTargets 6951 skips the movement only for a center
        // that is itself under the player's command; ancestors of the player
        // advance, the player's command being shielded by its own
        // permanentlydetached subtree (deployment 85321-85336, group orders
        // drop detached children via GetAttachedUnitsReg 119857). The guard
        // removes a chain only for a center that is itself player-owned (the
        // player's current command or a battle take-over) but not flagged;
        // a flagged center is left to vanilla's own branch. Player-owned units
        // attached below the center do not count.
        public static bool ObjectiveChainGuardRemovesCenter(
            bool centerUnderCommander,
            bool centerIsPlayerCommand,
            bool centerTakenOver)
        {
            if (centerUnderCommander) return false;
            return centerIsPlayerCommand || centerTakenOver;
        }

        // #46: UpdateMovingTargets 6877-6936 - the per-chain "continue"
        // conditions before the movement branch. When this is true vanilla
        // would move the chain center (and then run CheckCurrentOrderUpdate);
        // for a center protected by the W&L guard Whiskey suppresses only the
        // movement and keeps the rest.
        public static bool ObjectiveChainWouldReachMovement(in ObjectiveChainMovementProbe chain)
        {
            if (!chain.HasCenter || chain.ObjectiveCount <= 0) return false;

            bool busy = false;
            if (chain.CenterUnitTyp <= 13 && chain.CenterRegimentPaths > 0) busy = true;
            if (chain.CenterUnitTyp > 13 && chain.GroupSubordinatesMovingNotFar > 0.3f) busy = true;
            if (chain.GroupPositionAttachedUnits == 0 && chain.CenterUnitTyp > 13) busy = true;
            if (chain.PathInterrupted || chain.BlockedMovingOrderRunning) busy = true;

            if (!chain.HasFirstObjective) return false;
            if (!(chain.FirstObjectiveTimeToMove <= chain.NowBattleHours)) return false;
            if (busy || chain.MoveOrderActive) return false;
            if (!(chain.CombatEngagedNonArtillery <= 0f) || chain.AssaultMoveActive) return false;
            return chain.AbleToFight;
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

        // #62: dedupe bookkeeping must not depend on diagnostics logging; the
        // patch records an unmatched bridge attempt whatever the log setting.
        public static bool RecordsPlayerOrderAttempt(bool bridgeMatched)
        {
            return !bridgeMatched;
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
