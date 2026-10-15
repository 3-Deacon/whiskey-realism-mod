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
            int doctrineOpinion)
        {
            ModeAllowsWrites = modeAllowsWrites;
            IsFeudInstance = isFeudInstance;
            ScriptedAiStrategy = scriptedAiStrategy;
            ScriptedSideMacro = scriptedSideMacro;
            EndBattleActive = endBattleActive;
            CurrentMacro = currentMacro;
            LedgerOpinion = ledgerOpinion;
            DoctrineOpinion = doctrineOpinion;
        }

        public bool ModeAllowsWrites { get; }
        public bool IsFeudInstance { get; }
        public bool ScriptedAiStrategy { get; }
        public bool ScriptedSideMacro { get; }
        public bool EndBattleActive { get; }
        public int CurrentMacro { get; }
        public int LedgerOpinion { get; }
        public int DoctrineOpinion { get; }
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
        // Opinions outside 0..2 mean "no opinion"; the ledger opinion wins over
        // the reinforcement-opportunity doctrine opinion.
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

            int opinion = IsWritableMacro(input.LedgerOpinion)
                ? input.LedgerOpinion
                : IsWritableMacro(input.DoctrineOpinion) ? input.DoctrineOpinion : NoMacroWrite;
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

        // AIBattle.PerformAIActionDLCWL (5101): outside W&L the AI may act;
        // a unit of a group whose ai_stance >= 1 (the player delegated the
        // group to the AI) may act even under the player's command; otherwise
        // dlcw_isundercommander blocks.
        public static bool WlAllowsUnitAction(
            bool dlcScenarioActive,
            int groupAiStance,
            bool sameGroupAiObject,
            bool unitUnderCommander)
        {
            if (!dlcScenarioActive) return true;
            if (groupAiStance >= 1 && sameGroupAiObject) return true;
            return !unitUnderCommander;
        }

        // #41: a charge into an enemy that is itself charging (movementmode 3,
        // or 6 for an infantry bayonet charge - SetMovementMode 124704), or by a
        // unit already in melee, is the counter-charge vanilla would make; no
        // role/doctrine denial applies to it. A routed enemy is a pursuit.
        public static bool IsCounterCharge(
            bool hasVisibleEnemy,
            int enemyMovementMode,
            bool enemyRoutedOrMarked,
            float ownInMelee)
        {
            if (ownInMelee > 0f) return true;
            if (!hasVisibleEnemy || enemyRoutedOrMarked) return false;
            return enemyMovementMode == 3 || enemyMovementMode == 6;
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
