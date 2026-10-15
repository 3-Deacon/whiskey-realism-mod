namespace WhiskeyRealism.Tactical
{
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
