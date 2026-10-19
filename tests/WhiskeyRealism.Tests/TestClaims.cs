using System.Collections.Generic;
using System.Runtime.CompilerServices;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Strategic.Planner;

// The ownership table of a plan the harness made by hand (foresight F6a, D11 requirement-claims). The allocator has no fallback: it reads
// TheaterPlan.Claims, which TheaterPlanner.Plan publishes. A plan a test builds with `new TheaterPlan` has none, so every harness call
// that allocates for one goes through Allocate here, which builds the table at call time with the very producer the planner uses
// (ClaimTable.Build over the plan's own HoldNeed and CapitalFloorStrength) and then takes the plan's mass. A plan the real
// TheaterPlanner.Plan produced already carries its table, and is allocated as it stands.
internal static class TestClaims
{
    // The plans this class gave a table: a second call rebuilds it (a test changes the held builders or the snapshot between two allocations
    // of one plan), where a table the planner published is never touched.
    private static readonly ConditionalWeakTable<TheaterPlan, object> Built = new ConditionalWeakTable<TheaterPlan, object>();

    // Gives a hand-built plan its table. held is the depot ledger's held builders (the list the planner's Plan receives), gaveUp and state the
    // inputs of the rows F7 and F8 fill; dispatched is the formations the mover has sent toward an objective (F7's row 4 reads it with the state;
    // with either missing the commitment claims nothing, which is what every hand-built plan wants). Returns the plan.
    internal static TheaterPlan For(TheaterPlan plan, CampaignSnapshot s, SectorModel sectors, IReadOnlyList<HeldBuilder> held = null,
                                    IReadOnlyList<GaveUpMove> gaveUp = null, PlannerState state = null, IReadOnlyList<DispatchedMove> dispatched = null)
    {
        var need = new float[sectors.Count];
        var believed = new float[sectors.Count];
        var nearX = new float[sectors.Count];
        var nearZ = new float[sectors.Count];
        ForceAllocator.SectorInputs(plan, s, sectors, need, believed, nearX, nearZ);
        var claims = ClaimTable.Build(s, sectors, need, nearX, nearZ, plan.CapitalFloorStrength, held, gaveUp, state, null, ClaimTable.RowSources, null, out float[] sectorHeld, dispatched);
        ClaimTable.TakeMass(claims, s, plan.MassInstanceIds);
        plan.Claims.Clear();
        plan.Claims.AddRange(claims);
        plan.SectorHeld = sectorHeld;
        Built.Remove(plan);
        Built.Add(plan, new object());
        return plan;
    }

    // ForceAllocator.Allocate for a plan that may be hand-built: the same arguments, the table made first when the plan has none or when this
    // class made the one it has.
    internal static Allocation Allocate(TheaterPlan plan, CampaignSnapshot s, SectorModel sectors, System.Func<string, Theater> homeDepartment,
                                        IReadOnlyList<ReconTask> reconTasks, ReconMissionLedger missions, IReadOnlyList<GaveUpMove> gaveUp = null,
                                        IReadOnlyList<HeldBuilder> heldBuilders = null)
    {
        if (plan != null && s != null && sectors != null && (plan.Claims.Count == 0 || Built.TryGetValue(plan, out _)))
            For(plan, s, sectors, heldBuilders, gaveUp);
        return ForceAllocator.Allocate(plan, s, sectors, homeDepartment, reconTasks, missions, gaveUp, heldBuilders);
    }
}

// A producer of one pre-candidate row (4 committed, 5 department move) that claims one formation by instance id, as F7's and F8's will claim
// theirs: the plan-input registry (OwnershipTableTests) and the garrison tests witness the rows at plan level before either slice exists.
// ClaimTable.Takes decides whether the formation's current claimant lets the row have it.
internal sealed class ClaimsOneFormation : IClaimSource
{
    private readonly int _row, _instanceId;
    private readonly Claimant _claimant;
    public ClaimsOneFormation(int row, Claimant claimant, int instanceId) { _row = row; _claimant = claimant; _instanceId = instanceId; }
    public int Row => _row;
    public void Produce(in ClaimContext ctx, Claim[] claims)
    {
        for (int i = 0; i < claims.Length; i++)
            if (claims[i].InstanceId == _instanceId && ClaimTable.Takes(_row, claims[i].Claimant)) claims[i].Claimant = _claimant;
    }
}
