using System;

namespace WhiskeyRealism.Strategic.Construction
{
    // Alliance spending fields CBuilding.Place(pay: true) touches (96215-96246).
    public readonly struct AllianceSpendSnapshot
    {
        public AllianceSpendSnapshot(
            float treasury,
            float[] subsidyFunding,
            float cumulatedSpendingConstruction,
            float constructionCosts)
        {
            Treasury = treasury;
            SubsidyFunding = subsidyFunding != null ? (float[])subsidyFunding.Clone() : new float[0];
            CumulatedSpendingConstruction = cumulatedSpendingConstruction;
            ConstructionCosts = constructionCosts;
        }

        public float Treasury { get; }
        public float[] SubsidyFunding { get; }
        public float CumulatedSpendingConstruction { get; }
        public float ConstructionCosts { get; }
    }

    // What a telegraph construction wish cost the alliance.
    //
    // CBuilding.AddConstructionWish (97479) charges at wish time: it calls
    // Place(placeholder, newlycreated: true) with the default pay: true, which
    // moves subsidy funding (AdjustSubsidyFunding, clamped at 0, 63189) and
    // cumulatedspending_construction when the building type has a subsidy type,
    // the treasury (AdjustTreasury, which shrinks an AI alliance's negative
    // adjustments by its campaign bonus, 62571) and constructioncosts. The
    // final placement in WorkDownConstructionWishes is pay: false (97562).
    // The charge is therefore measured as the before/after difference around
    // AddConstructionWish rather than recomputed from the cost formulas.
    // companyfoundings++ (97534) is never undone by vanilla, not even on its own
    // cancellation path, and is left alone.
    public readonly struct TelegraphWishCharge
    {
        private TelegraphWishCharge(
            float treasuryDelta,
            float[] subsidyFundingDelta,
            float cumulatedSpendingConstructionDelta,
            float constructionCostsDelta)
        {
            TreasuryDelta = treasuryDelta;
            SubsidyFundingDelta = subsidyFundingDelta ?? new float[0];
            CumulatedSpendingConstructionDelta = cumulatedSpendingConstructionDelta;
            ConstructionCostsDelta = constructionCostsDelta;
        }

        // after - before; a charge is negative.
        public float TreasuryDelta { get; }
        public float[] SubsidyFundingDelta { get; }
        public float CumulatedSpendingConstructionDelta { get; }
        public float ConstructionCostsDelta { get; }

        public bool HasCharge
        {
            get
            {
                if (TreasuryDelta != 0f || CumulatedSpendingConstructionDelta != 0f || ConstructionCostsDelta != 0f)
                    return true;
                float[] subsidy = SubsidyFundingDelta ?? new float[0];
                for (int i = 0; i < subsidy.Length; i++)
                    if (subsidy[i] != 0f) return true;
                return false;
            }
        }

        public static TelegraphWishCharge Measure(in AllianceSpendSnapshot before, in AllianceSpendSnapshot after)
        {
            float[] b = before.SubsidyFunding ?? new float[0];
            float[] a = after.SubsidyFunding ?? new float[0];
            var subsidy = new float[Math.Min(a.Length, b.Length)];
            for (int i = 0; i < subsidy.Length; i++)
                subsidy[i] = Finite(a[i] - b[i]);

            return new TelegraphWishCharge(
                Finite(after.Treasury - before.Treasury),
                subsidy,
                Finite(after.CumulatedSpendingConstruction - before.CumulatedSpendingConstruction),
                Finite(after.ConstructionCosts - before.ConstructionCosts));
        }

        private static float Finite(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? 0f : value;
        }
    }
}
