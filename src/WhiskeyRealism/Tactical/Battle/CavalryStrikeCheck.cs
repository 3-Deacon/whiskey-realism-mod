namespace WhiskeyRealism.Tactical.Battle
{
    // BC13 (spec § 7, requirement-fix-cavalry-strike): what a cavalry charge target must be for Whiskey to leave vanilla's pick alone.
    // Vanilla's AdjustCavalryBehavior (5049-5056) charges the closest enemy within maxdistancecavalryattack that is artillery or
    // has morale under maxenemymoraleforcavalrychargenonarty, seeing through fog and into squares and garrisons, ignoring
    // supporting infantry, the odds and its own outflanking. The patch overrides the result of vanilla's own
    // GetClosestEnemyUnitReg call: the pick stays only when this check passes. Pure; the runtime patch gathers the inputs.
    public struct CavalryStrikeInput
    {
        /// <summary>The target has a Visible observation in the side's latest snapshot.</summary>
        public bool TargetVisible;
        public int TargetUnitTyp;
        public bool TargetGarrisoned, TargetInSquare;
        public float TargetMorale;
        /// <summary>Artillery only: an infantry or cavalry sample of the target's side stands within <see cref="CavalryStrikeCheck.SupportMeters"/> of it.</summary>
        public bool TargetSupported;
        /// <summary>Vanilla's weighted charge odds (122229-122234, with the mounted weapon factor) and microaitriggerforcharge; a trigger of 0 or less gates nothing.</summary>
        public float Odds, OddsTrigger;
        /// <summary>The charging regiment is perceived outflanked and nomicroaichargeifunitisoutflanked is set.</summary>
        public bool ChargerBarredByOutflank;
        /// <summary>maxenemymoraleforcavalrychargenonarty: below it a non-artillery target counts as shaken.</summary>
        public float ShakenMorale;
    }

    public static class CavalryStrikeCheck
    {
        public const float SupportMeters = 250f;

        public const string Hidden = "hidden", Square = "square", Garrisoned = "garrisoned", Supported = "supported",
            Steady = "steady", Odds = "odds", Outflanked = "outflanked", ExploitTarget = "exploit-target";

        /// <summary>
        /// True when the charge may go ahead; otherwise the first refusal reason, in this order: hidden, square, garrisoned, then the
        /// target's own kind (supported artillery; a steady unit; odds under the trigger), then outflanked.
        /// </summary>
        public static bool Allows(in CavalryStrikeInput i, out string reason)
        {
            if (!i.TargetVisible) return Refuse(Hidden, out reason);
            if (i.TargetInSquare) return Refuse(Square, out reason);
            if (i.TargetGarrisoned) return Refuse(Garrisoned, out reason);
            if (i.TargetUnitTyp == TacticalUnitType.Artillery)
            {
                if (i.TargetSupported) return Refuse(Supported, out reason);
            }
            else
            {
                if (!(i.TargetMorale < i.ShakenMorale)) return Refuse(Steady, out reason);
                if (i.OddsTrigger > 0f && !(i.Odds > i.OddsTrigger)) return Refuse(Odds, out reason);
            }
            if (i.ChargerBarredByOutflank) return Refuse(Outflanked, out reason);
            reason = null;
            return true;
        }

        /// <summary>A live exploit's cavalry target replaces vanilla's pick only when it lies within vanilla's own cavalry reach (maxdistancecavalryattack).</summary>
        public static bool SubstituteInReach(float distanceMeters, float maxDistanceCavalryAttack)
        {
            return distanceMeters >= 0f && distanceMeters < maxDistanceCavalryAttack;
        }

        private static bool Refuse(string why, out string reason)
        {
            reason = why;
            return false;
        }
    }
}
