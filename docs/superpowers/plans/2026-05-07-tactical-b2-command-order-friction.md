# Tactical B2 Command Order Friction Implementation Plan

Status: implemented, console-tested, built, deployed, and hash-verified on 2026-05-07 in DLL `dc028bae2169ca4de00e5af6209f868ae1a3421f3cac6f9bba6cf12743edd8db` (501248 bytes). No B2 in-game smoke has been run. B2 remains read-only #35 telemetry; B3 tactical odds doctrine is next.

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add read-only command hierarchy and order-friction interpretation for Slice B2, using vanilla W&L order-delay surfaces without issuing or rewriting tactical movement orders.

**Architecture:** B2 extends the existing Slice B0 observer and pure tactical logic. Harmony patches remain Postfix observers on vanilla queue/courier methods; pure C# DTOs classify command tier, intended-vs-transmitted order state, stale order risk, and initiative-adjusted delay pressure. No B2 code calls `BattleUnits.SetWaypoint`, `Regiment.AddToOrderQueue`, `Regiment.SetOrderStatus`, or any direct movement API.

**Tech Stack:** C# netstandard2.1, BepInEx 5.4.x, HarmonyX Postfix observers, Grand Tactician vanilla `Regiment` / `AIBattle` runtime, console harness at `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`.

---

## Evidence And Scope

This plan has been executed. Fresh B1 in-game denial smoke was explicitly deferred by the user on 2026-05-07, and no B2 in-game smoke has been run, so keep those runtime boundaries visible in `docs/handoff.md`. Do not archive this plan until B2 runtime smoke is either completed or explicitly waived for archival.

Verified vanilla anchors in `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs`:

- `BattleUnits.SetWaypoint(GameObject, Vector3, ...)` at line 91225 delegates to `SetWaypoint(Regiment, Vector3, ...)` at line 91232; B2 must not patch or call this as behavior.
- `Regiment.AddToOrderQueue(...)` at line 124917 creates the order queue, records start path, processing time, source unit, and calls `AddOrderCourierline`.
- `Regiment.AddOrderCourierline(...)` at line 125009 chooses bugle vs courier, sets courier process time, records `CourierLine`, and can set `orderstate = 3` on courier creation failure.
- `Regiment.ProcessOrders()` at line 125173 moves queued orders through state 1, courier delivery, failed delivery, state 2 delivery, and queue deletion.
- `Regiment.SetOrderStatus(OrderStatus)` at line 125484 writes path status and clears `orderstate` when state 2 is delivered.
- `Regiment.GetLastTransmittedPathPos(bool ignoreorderdelay = false)` at line 127552 and `Regiment.GetLastTransmittedPath(bool ignoreorderdelay = false)` at line 127591 expose intended-vs-transmitted path lag when order delays are enabled.

Existing B0 runtime evidence:

- `[TacticalPlayerOrder] event=delivery relation=ai-to-player-subordinate ... targetUnderCommander=True delivery=bugle processHrs=0.00`
- `[TacticalPlayerOrder] event=queued relation=ai-to-player-subordinate ... queueProcessing=True delayHrs=0.02`
- `[TacticalPlayerOrder] event=delivery relation=ai-chain ... delivery=courier processHrs=9999999.00`
- `[TacticalOrder] ... orderSig=moving=...,waiting=...,interrupted=...`

These prove the control surfaces are readable from B0. B2 interprets them; it does not enforce behavior.

## File Structure

- Create `src/WhiskeyRealism/Tactical/TacticalOrderFriction.cs`
  - Pure friction model for `Immediate`, `Bugle`, `Courier`, `Pending`, `Delivered`, `Stale`, and `Failed`.
- Create `src/WhiskeyRealism/Tactical/TacticalCommanderProfile.cs`
  - Pure command-tier/profile DTO and classification helpers from vanilla-shaped inputs.
- Create `src/WhiskeyRealism/Tactical/TacticalCommandLedger.cs`
  - Pure command/order summary builder and signatures for bounded telemetry.
- Modify `src/WhiskeyRealism/Tactical/TacticalBattleContext.cs`
  - Add command telemetry fields and `TacticalObservedEvent.Command`.
- Modify `src/WhiskeyRealism/Tactical/TacticalTelemetry.cs`
  - Add `[TacticalCommand]` prefix and include command signature in summaries/signatures.
- Modify `src/WhiskeyRealism/Patches/TacticalObserverPatch.cs`
  - Reuse existing B0 `Regiment.AddToOrderQueue` and `Regiment.AddOrderCourierline` Postfixes to emit read-only `[TacticalCommand]` and richer `[TacticalOrder]` summaries.
- Modify `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`
  - Add explicit compile entries for new tactical source files.
- Modify `tests/WhiskeyRealism.Tests/Program.cs`
  - Add pure B2 tests before runtime patch wiring.
- Modify `docs/handoff.md`
  - Move active tactical workstream from B1 smoke gate to B2 plan/execution with B1 smoke marked user-deferred.
- Modify `docs/patch-catalog.md`
  - Keep #35 as the owner because B2 extends `TacticalObserverPatch`; do not reserve #43 unless a new Harmony patch file is added during implementation.

## Task 1: Pure Order Friction Model

**Files:**
- Create: `src/WhiskeyRealism/Tactical/TacticalOrderFriction.cs`
- Modify: `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`
- Modify: `tests/WhiskeyRealism.Tests/Program.cs`

- [ ] **Step 1: Add compile entry for the new pure model**

Add this line inside the tactical compile item group in `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`:

```xml
<Compile Include="..\..\src\WhiskeyRealism\Tactical\TacticalOrderFriction.cs" Link="TacticalOrderFriction.cs" />
```

- [ ] **Step 2: Add failing order-friction tests**

Add these entries to the `tests` array in `tests/WhiskeyRealism.Tests/Program.cs` near the existing tactical tests:

```csharp
("tactical order outside bugle range is delayed", TacticalOrderOutsideBugleRangeIsDelayed),
("tactical order delivered transmitted path differs while delayed", TacticalOrderDeliveredTransmittedPathDiffersWhileDelayed),
("tactical order stale delayed order downgrades on material contact change", TacticalOrderStaleDelayedOrderDowngradesOnContactChange),
("tactical order high initiative reduces delay pressure without instant delivery", TacticalOrderHighInitiativeReducesDelayPressureWithoutInstant),
```

Add these test methods in `Program.cs` near the existing tactical helper tests:

```csharp
static void TacticalOrderOutsideBugleRangeIsDelayed()
{
    var decision = TacticalOrderFriction.Evaluate(new TacticalOrderFrictionInput(
        orderDelayEnabled: true,
        queueProcessing: true,
        queueDelayHours: 0.20f,
        delivery: TacticalOrderDelivery.Courier,
        deliveryProcessHours: 9999999f,
        courierMissing: false,
        orderState: 1,
        intendedPathId: 4,
        transmittedPathId: 1,
        contactChangedMaterially: false,
        commanderInitiative01: 0.50f));

    AssertEqual(TacticalOrderFrictionState.Courier, decision.State, "courier state");
    AssertTrue(decision.IsDelayed, "courier is delayed");
    AssertTrue(decision.DelayPressure > 0.10f, "courier pressure positive");
}

static void TacticalOrderDeliveredTransmittedPathDiffersWhileDelayed()
{
    var decision = TacticalOrderFriction.Evaluate(new TacticalOrderFrictionInput(
        orderDelayEnabled: true,
        queueProcessing: true,
        queueDelayHours: 0.05f,
        delivery: TacticalOrderDelivery.Bugle,
        deliveryProcessHours: 0.02f,
        courierMissing: false,
        orderState: 1,
        intendedPathId: 5,
        transmittedPathId: 2,
        contactChangedMaterially: false,
        commanderInitiative01: 0.50f));

    AssertEqual(TacticalOrderFrictionState.Pending, decision.State, "pending state");
    AssertTrue(decision.TransmittedPathDiffers, "transmitted path differs");
    AssertFalse(decision.IsDelivered, "not delivered while path lag remains");
}

static void TacticalOrderStaleDelayedOrderDowngradesOnContactChange()
{
    var decision = TacticalOrderFriction.Evaluate(new TacticalOrderFrictionInput(
        orderDelayEnabled: true,
        queueProcessing: true,
        queueDelayHours: 0.10f,
        delivery: TacticalOrderDelivery.Bugle,
        deliveryProcessHours: 0.01f,
        courierMissing: false,
        orderState: 1,
        intendedPathId: 3,
        transmittedPathId: 1,
        contactChangedMaterially: true,
        commanderInitiative01: 0.50f));

    AssertEqual(TacticalOrderFrictionState.Stale, decision.State, "stale state");
    AssertTrue(decision.IsDelayed, "stale delayed");
}

static void TacticalOrderHighInitiativeReducesDelayPressureWithoutInstant()
{
    var low = TacticalOrderFriction.Evaluate(new TacticalOrderFrictionInput(
        orderDelayEnabled: true,
        queueProcessing: true,
        queueDelayHours: 0.25f,
        delivery: TacticalOrderDelivery.Courier,
        deliveryProcessHours: 9999999f,
        courierMissing: false,
        orderState: 1,
        intendedPathId: 6,
        transmittedPathId: 2,
        contactChangedMaterially: false,
        commanderInitiative01: 0.10f));
    var high = TacticalOrderFriction.Evaluate(new TacticalOrderFrictionInput(
        orderDelayEnabled: true,
        queueProcessing: true,
        queueDelayHours: 0.25f,
        delivery: TacticalOrderDelivery.Courier,
        deliveryProcessHours: 9999999f,
        courierMissing: false,
        orderState: 1,
        intendedPathId: 6,
        transmittedPathId: 2,
        contactChangedMaterially: false,
        commanderInitiative01: 0.90f));

    AssertEqual(TacticalOrderFrictionState.Courier, high.State, "high initiative still courier");
    AssertTrue(high.DelayPressure < low.DelayPressure, "high initiative lowers pressure");
    AssertFalse(high.IsDelivered, "initiative does not make courier instant");
}
```

- [ ] **Step 3: Run tests and verify they fail before implementation**

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: build fails with missing `TacticalOrderFriction`, `TacticalOrderFrictionInput`, `TacticalOrderDelivery`, and `TacticalOrderFrictionState`.

- [ ] **Step 4: Add the pure model**

Create `src/WhiskeyRealism/Tactical/TacticalOrderFriction.cs`:

```csharp
using System;

namespace WhiskeyRealism.Tactical
{
    public enum TacticalOrderDelivery
    {
        Unknown = 0,
        Immediate = 1,
        Bugle = 2,
        Courier = 3
    }

    public enum TacticalOrderFrictionState
    {
        Immediate = 0,
        Bugle = 1,
        Courier = 2,
        Pending = 3,
        Delivered = 4,
        Stale = 5,
        Failed = 6
    }

    public readonly struct TacticalOrderFrictionInput
    {
        public TacticalOrderFrictionInput(
            bool orderDelayEnabled,
            bool queueProcessing,
            float queueDelayHours,
            TacticalOrderDelivery delivery,
            float deliveryProcessHours,
            bool courierMissing,
            int orderState,
            int intendedPathId,
            int transmittedPathId,
            bool contactChangedMaterially,
            float commanderInitiative01)
        {
            OrderDelayEnabled = orderDelayEnabled;
            QueueProcessing = queueProcessing;
            QueueDelayHours = Sanitize(queueDelayHours);
            Delivery = delivery;
            DeliveryProcessHours = Sanitize(deliveryProcessHours);
            CourierMissing = courierMissing;
            OrderState = orderState;
            IntendedPathId = intendedPathId;
            TransmittedPathId = transmittedPathId;
            ContactChangedMaterially = contactChangedMaterially;
            CommanderInitiative01 = Clamp01(commanderInitiative01);
        }

        public bool OrderDelayEnabled { get; }
        public bool QueueProcessing { get; }
        public float QueueDelayHours { get; }
        public TacticalOrderDelivery Delivery { get; }
        public float DeliveryProcessHours { get; }
        public bool CourierMissing { get; }
        public int OrderState { get; }
        public int IntendedPathId { get; }
        public int TransmittedPathId { get; }
        public bool ContactChangedMaterially { get; }
        public float CommanderInitiative01 { get; }

        private static float Sanitize(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? 0f : Math.Max(0f, value);
        }

        private static float Clamp01(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 0.5f;
            if (value < 0f) return 0f;
            return value > 1f ? 1f : value;
        }
    }

    public readonly struct TacticalOrderFrictionDecision
    {
        public TacticalOrderFrictionDecision(
            TacticalOrderFrictionState state,
            bool isDelayed,
            bool isDelivered,
            bool transmittedPathDiffers,
            float delayPressure,
            string reason)
        {
            State = state;
            IsDelayed = isDelayed;
            IsDelivered = isDelivered;
            TransmittedPathDiffers = transmittedPathDiffers;
            DelayPressure = delayPressure;
            Reason = string.IsNullOrEmpty(reason) ? "unknown" : reason;
        }

        public TacticalOrderFrictionState State { get; }
        public bool IsDelayed { get; }
        public bool IsDelivered { get; }
        public bool TransmittedPathDiffers { get; }
        public float DelayPressure { get; }
        public string Reason { get; }
    }

    public static class TacticalOrderFriction
    {
        public static TacticalOrderFrictionDecision Evaluate(TacticalOrderFrictionInput input)
        {
            bool pathDiffers = input.IntendedPathId >= 0 &&
                               input.TransmittedPathId >= 0 &&
                               input.TransmittedPathId < input.IntendedPathId;
            float pressure = DelayPressure(input.QueueDelayHours, input.DeliveryProcessHours, input.CommanderInitiative01);

            if (input.CourierMissing || input.OrderState == 3)
                return new TacticalOrderFrictionDecision(TacticalOrderFrictionState.Failed, true, false, pathDiffers, pressure, "courier-failed");

            if (!input.OrderDelayEnabled)
                return new TacticalOrderFrictionDecision(TacticalOrderFrictionState.Immediate, false, true, false, 0f, "order-delay-disabled");

            if (input.ContactChangedMaterially && (input.QueueProcessing || pathDiffers || input.OrderState == 1))
                return new TacticalOrderFrictionDecision(TacticalOrderFrictionState.Stale, true, false, pathDiffers, pressure, "contact-changed");

            if (input.OrderState == 2 && !pathDiffers)
                return new TacticalOrderFrictionDecision(TacticalOrderFrictionState.Delivered, false, true, false, 0f, "status-delivered");

            if (pathDiffers)
                return new TacticalOrderFrictionDecision(TacticalOrderFrictionState.Pending, true, false, true, pressure, "path-not-transmitted");

            if (input.Delivery == TacticalOrderDelivery.Courier)
                return new TacticalOrderFrictionDecision(TacticalOrderFrictionState.Courier, true, false, pathDiffers, pressure, "courier");

            if (input.Delivery == TacticalOrderDelivery.Bugle && (input.QueueProcessing || input.DeliveryProcessHours > 0f))
                return new TacticalOrderFrictionDecision(TacticalOrderFrictionState.Bugle, input.DeliveryProcessHours > 0f, false, pathDiffers, pressure, "bugle");

            return new TacticalOrderFrictionDecision(TacticalOrderFrictionState.Immediate, false, true, false, 0f, "no-delay");
        }

        private static float DelayPressure(float queueDelayHours, float deliveryProcessHours, float initiative01)
        {
            float raw = Math.Max(queueDelayHours, deliveryProcessHours >= 999999f ? queueDelayHours + 0.50f : deliveryProcessHours);
            float initiativeRelief = 0.50f + ((1f - initiative01) * 0.50f);
            return Math.Max(0f, raw * initiativeRelief);
        }
    }
}
```

- [ ] **Step 5: Run tests and verify this task passes**

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: all tests pass, including the four new tactical order-friction tests.

- [ ] **Step 6: Commit Task 1**

Run:

```bash
git add src/WhiskeyRealism/Tactical/TacticalOrderFriction.cs tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj tests/WhiskeyRealism.Tests/Program.cs
git commit -m "feat: add tactical order friction model"
```

## Task 2: Command Profiles And Command Ledger

**Files:**
- Create: `src/WhiskeyRealism/Tactical/TacticalCommanderProfile.cs`
- Create: `src/WhiskeyRealism/Tactical/TacticalCommandLedger.cs`
- Modify: `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`
- Modify: `tests/WhiskeyRealism.Tests/Program.cs`

- [ ] **Step 1: Add compile entries**

Add these lines to `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`:

```xml
<Compile Include="..\..\src\WhiskeyRealism\Tactical\TacticalCommanderProfile.cs" Link="TacticalCommanderProfile.cs" />
<Compile Include="..\..\src\WhiskeyRealism\Tactical\TacticalCommandLedger.cs" Link="TacticalCommandLedger.cs" />
```

- [ ] **Step 2: Add failing command hierarchy tests**

Add these entries to the `tests` array:

```csharp
("tactical command army and corps intent does not retask regiments directly", TacticalCommandArmyCorpsDoesNotRetaskRegimentsDirectly),
("tactical command division mission maps to brigade actions", TacticalCommandDivisionMissionMapsToBrigadeActions),
```

Add these test methods:

```csharp
static void TacticalCommandArmyCorpsDoesNotRetaskRegimentsDirectly()
{
    var army = TacticalCommanderProfile.FromVanillaShape(
        stableId: "army-1",
        displayName: "Army of Northern Virginia",
        unitType: 16,
        isTopUnit: true,
        underPlayerCommander: false,
        parentId: "",
        alliance: 1,
        side: 1,
        initiative01: 0.60f);
    var regiment = TacticalCommanderProfile.FromVanillaShape(
        stableId: "reg-1",
        displayName: "3rd South Carolina Infantry",
        unitType: 0,
        isTopUnit: false,
        underPlayerCommander: true,
        parentId: "brigade-1",
        alliance: 1,
        side: 1,
        initiative01: 0.50f);

    var decision = TacticalCommandLedger.DecideOrderScope(army, regiment);

    AssertEqual(TacticalOrderScope.BlockDirectRegimentRetask, decision.Scope, "army cannot retask regiment directly");
    AssertEqual("army-corps-intent-must-flow-through-subcommand", decision.Reason, "scope reason");
}

static void TacticalCommandDivisionMissionMapsToBrigadeActions()
{
    var division = TacticalCommanderProfile.FromVanillaShape(
        stableId: "division-1",
        displayName: "Hill's Division",
        unitType: 14,
        isTopUnit: false,
        underPlayerCommander: false,
        parentId: "corps-1",
        alliance: 1,
        side: 1,
        initiative01: 0.55f);
    var brigade = TacticalCommanderProfile.FromVanillaShape(
        stableId: "brigade-1",
        displayName: "First Brigade",
        unitType: 15,
        isTopUnit: false,
        underPlayerCommander: false,
        parentId: "division-1",
        alliance: 1,
        side: 1,
        initiative01: 0.50f);

    var decision = TacticalCommandLedger.DecideOrderScope(division, brigade);

    AssertEqual(TacticalOrderScope.SubcommandAction, decision.Scope, "division maps to brigade");
    AssertEqual("division-to-brigade", decision.Reason, "division reason");
}
```

- [ ] **Step 3: Run tests and verify they fail before implementation**

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: build fails with missing `TacticalCommanderProfile`, `TacticalCommandLedger`, and `TacticalOrderScope`.

- [ ] **Step 4: Add command profile model**

Create `src/WhiskeyRealism/Tactical/TacticalCommanderProfile.cs`:

```csharp
using System;

namespace WhiskeyRealism.Tactical
{
    public enum TacticalCommandTier
    {
        Unknown = 0,
        Regiment = 1,
        Brigade = 2,
        Division = 3,
        Corps = 4,
        Army = 5
    }

    public readonly struct TacticalCommanderProfile
    {
        public TacticalCommanderProfile(
            string stableId,
            string displayName,
            TacticalCommandTier tier,
            bool isTopUnit,
            bool underPlayerCommander,
            string parentId,
            int alliance,
            int side,
            float initiative01)
        {
            StableId = Safe(stableId, "unknown");
            DisplayName = Safe(displayName, "unknown");
            Tier = tier;
            IsTopUnit = isTopUnit;
            UnderPlayerCommander = underPlayerCommander;
            ParentId = Safe(parentId, "");
            Alliance = alliance;
            Side = side;
            Initiative01 = Clamp01(initiative01);
        }

        public string StableId { get; }
        public string DisplayName { get; }
        public TacticalCommandTier Tier { get; }
        public bool IsTopUnit { get; }
        public bool UnderPlayerCommander { get; }
        public string ParentId { get; }
        public int Alliance { get; }
        public int Side { get; }
        public float Initiative01 { get; }

        public static TacticalCommanderProfile FromVanillaShape(
            string stableId,
            string displayName,
            int unitType,
            bool isTopUnit,
            bool underPlayerCommander,
            string parentId,
            int alliance,
            int side,
            float initiative01)
        {
            return new TacticalCommanderProfile(
                stableId,
                displayName,
                TierFromUnitType(unitType, isTopUnit),
                isTopUnit,
                underPlayerCommander,
                parentId,
                alliance,
                side,
                initiative01);
        }

        public static TacticalCommandTier TierFromUnitType(int unitType, bool isTopUnit)
        {
            if (unitType <= 13) return TacticalCommandTier.Regiment;
            if (unitType == 14) return TacticalCommandTier.Division;
            if (unitType == 15) return TacticalCommandTier.Brigade;
            if (unitType == 16) return isTopUnit ? TacticalCommandTier.Army : TacticalCommandTier.Corps;
            return unitType > 16 ? TacticalCommandTier.Army : TacticalCommandTier.Unknown;
        }

        private static string Safe(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }

        private static float Clamp01(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 0.5f;
            if (value < 0f) return 0f;
            return value > 1f ? 1f : value;
        }
    }
}
```

- [ ] **Step 5: Add command ledger**

Create `src/WhiskeyRealism/Tactical/TacticalCommandLedger.cs`:

```csharp
using System;

namespace WhiskeyRealism.Tactical
{
    public enum TacticalOrderScope
    {
        Unknown = 0,
        DirectUnitAction = 1,
        SubcommandAction = 2,
        BlockDirectRegimentRetask = 3
    }

    public readonly struct TacticalCommandScopeDecision
    {
        public TacticalCommandScopeDecision(TacticalOrderScope scope, string reason)
        {
            Scope = scope;
            Reason = string.IsNullOrEmpty(reason) ? "unknown" : reason;
        }

        public TacticalOrderScope Scope { get; }
        public string Reason { get; }
    }

    public readonly struct TacticalCommandSummary
    {
        public TacticalCommandSummary(
            TacticalCommandTier sourceTier,
            TacticalCommandTier targetTier,
            TacticalOrderScope scope,
            TacticalOrderFrictionState friction,
            bool playerChain,
            bool localInitiativeAllowed,
            float delayPressure,
            string reason)
        {
            SourceTier = sourceTier;
            TargetTier = targetTier;
            Scope = scope;
            Friction = friction;
            PlayerChain = playerChain;
            LocalInitiativeAllowed = localInitiativeAllowed;
            DelayPressure = Sanitize(delayPressure);
            Reason = string.IsNullOrEmpty(reason) ? "unknown" : reason;
        }

        public TacticalCommandTier SourceTier { get; }
        public TacticalCommandTier TargetTier { get; }
        public TacticalOrderScope Scope { get; }
        public TacticalOrderFrictionState Friction { get; }
        public bool PlayerChain { get; }
        public bool LocalInitiativeAllowed { get; }
        public float DelayPressure { get; }
        public string Reason { get; }

        public string Signature()
        {
            return "src=" + SourceTier +
                   ",tgt=" + TargetTier +
                   ",scope=" + Scope +
                   ",friction=" + Friction +
                   ",playerChain=" + PlayerChain +
                   ",initiative=" + LocalInitiativeAllowed +
                   ",pressure=" + Bucket(DelayPressure);
        }

        private static float Sanitize(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? 0f : Math.Max(0f, value);
        }

        private static string Bucket(float value)
        {
            return (Math.Round(value * 10f) / 10f).ToString("0.0");
        }
    }

    public static class TacticalCommandLedger
    {
        public static TacticalCommandScopeDecision DecideOrderScope(TacticalCommanderProfile source, TacticalCommanderProfile target)
        {
            if ((source.Tier == TacticalCommandTier.Army || source.Tier == TacticalCommandTier.Corps) &&
                target.Tier == TacticalCommandTier.Regiment)
                return new TacticalCommandScopeDecision(TacticalOrderScope.BlockDirectRegimentRetask, "army-corps-intent-must-flow-through-subcommand");

            if (source.Tier == TacticalCommandTier.Division && target.Tier == TacticalCommandTier.Brigade)
                return new TacticalCommandScopeDecision(TacticalOrderScope.SubcommandAction, "division-to-brigade");

            if (source.Tier == TacticalCommandTier.Brigade && target.Tier == TacticalCommandTier.Regiment)
                return new TacticalCommandScopeDecision(TacticalOrderScope.DirectUnitAction, "brigade-to-regiment");

            return new TacticalCommandScopeDecision(TacticalOrderScope.DirectUnitAction, "local-or-unknown");
        }

        public static TacticalCommandSummary Summarize(
            TacticalCommanderProfile source,
            TacticalCommanderProfile target,
            TacticalOrderFrictionDecision friction)
        {
            var scope = DecideOrderScope(source, target);
            bool playerChain = source.UnderPlayerCommander || target.UnderPlayerCommander;
            bool initiative = target.Initiative01 >= 0.65f && friction.State != TacticalOrderFrictionState.Immediate;
            return new TacticalCommandSummary(
                source.Tier,
                target.Tier,
                scope.Scope,
                friction.State,
                playerChain,
                initiative,
                friction.DelayPressure,
                scope.Reason);
        }
    }
}
```

- [ ] **Step 6: Run tests and verify this task passes**

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: all tests pass, including the two new command hierarchy tests.

- [ ] **Step 7: Commit Task 2**

Run:

```bash
git add src/WhiskeyRealism/Tactical/TacticalCommanderProfile.cs src/WhiskeyRealism/Tactical/TacticalCommandLedger.cs tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj tests/WhiskeyRealism.Tests/Program.cs
git commit -m "feat: add tactical command ledger"
```

## Task 3: Tactical Telemetry Shape

**Files:**
- Modify: `src/WhiskeyRealism/Tactical/TacticalBattleContext.cs`
- Modify: `src/WhiskeyRealism/Tactical/TacticalTelemetry.cs`
- Modify: `tests/WhiskeyRealism.Tests/Program.cs`

- [ ] **Step 1: Add failing telemetry tests**

Add these entries to the `tests` array:

```csharp
("tactical telemetry maps command prefix", TacticalTelemetryMapsCommandPrefix),
("tactical telemetry signature changes on command signature", TacticalTelemetrySignatureChangesOnCommandSignature),
```

Add these methods:

```csharp
static void TacticalTelemetryMapsCommandPrefix()
{
    var context = TacticalBattleContext.Empty();
    context.CommandSignature = "src=Division,tgt=Brigade,scope=SubcommandAction";

    string summary = TacticalTelemetry.Summary(TacticalObservedEvent.Command, context);

    AssertTrue(summary.StartsWith("[TacticalCommand]"), "command prefix");
    AssertTrue(summary.Contains("commandSig=src=Division,tgt=Brigade,scope=SubcommandAction"), "command signature");
}

static void TacticalTelemetrySignatureChangesOnCommandSignature()
{
    var first = TacticalBattleContext.Empty();
    first.CommandSignature = "src=Division,tgt=Brigade";
    var second = TacticalBattleContext.Empty();
    second.CommandSignature = "src=Army,tgt=Regiment";

    string a = TacticalTelemetry.Signature(TacticalObservedEvent.Command, first);
    string b = TacticalTelemetry.Signature(TacticalObservedEvent.Command, second);

    AssertTrue(a != b, "command signature should affect throttle key");
}
```

- [ ] **Step 2: Run tests and verify they fail before implementation**

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: build fails because `TacticalObservedEvent.Command` and `CommandSignature` do not exist.

- [ ] **Step 3: Extend tactical context**

Modify `src/WhiskeyRealism/Tactical/TacticalBattleContext.cs`:

```csharp
public enum TacticalObservedEvent
{
    Macro = 0,
    Group = 1,
    Charge = 2,
    Feud = 3,
    Sector = 4,
    Order = 5,
    Reserve = 6,
    Artillery = 7,
    Fallback = 8,
    PlayerOrder = 9,
    Command = 10
}
```

Add this property to `TacticalBattleContext`:

```csharp
public string CommandSignature { get; set; }
```

Update `Empty()`:

```csharp
CommandSignature = ""
```

- [ ] **Step 4: Extend telemetry summary/signature**

Modify `src/WhiskeyRealism/Tactical/TacticalTelemetry.cs`.

Append this fragment to `Summary(...)` after `orderSig=`:

```csharp
+ " commandSig=" + Safe(context.CommandSignature);
```

Append this fragment to `Signature(...)` after `Safe(context.OrderSignature)`:

```csharp
+ "|" + Safe(context.CommandSignature);
```

Add a prefix mapping:

```csharp
case TacticalObservedEvent.Command: return "[TacticalCommand]";
```

- [ ] **Step 5: Run tests and verify this task passes**

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: all tests pass, including the two command telemetry tests.

- [ ] **Step 6: Commit Task 3**

Run:

```bash
git add src/WhiskeyRealism/Tactical/TacticalBattleContext.cs src/WhiskeyRealism/Tactical/TacticalTelemetry.cs tests/WhiskeyRealism.Tests/Program.cs
git commit -m "feat: extend tactical command telemetry"
```

## Task 4: Observer Integration Without Behavior Changes

**Files:**
- Modify: `src/WhiskeyRealism/Patches/TacticalObserverPatch.cs`

- [ ] **Step 1: Add no-new-Harmony-target guardrail comment**

At the `Regiment.AddToOrderQueue` and `Regiment.AddOrderCourierline` observers in `src/WhiskeyRealism/Patches/TacticalObserverPatch.cs`, add this comment above the first Postfix:

```csharp
// B2 command/order friction stays read-only: these Postfixes interpret vanilla queue/courier state.
// They must not call SetWaypoint, AddToOrderQueue, SetOrderStatus, or mutate Regiment order fields.
```

- [ ] **Step 2: Add direct command telemetry from queued orders**

Inside `ObserveQueuedOrder(...)`, after the existing `[TacticalPlayerOrder] event=queued` `EmitDirect(...)` call, add:

```csharp
var friction = TacticalOrderFriction.Evaluate(new TacticalOrderFrictionInput(
    orderDelayEnabled: GameVars.useorderdelays,
    queueProcessing: queueProcessingTime,
    queueDelayHours: delay,
    delivery: TacticalOrderDelivery.Unknown,
    deliveryProcessHours: delay,
    courierMissing: false,
    orderState: target != null ? target.orderstate : 0,
    intendedPathId: SafePathId(target, ignoreOrderDelay: true),
    transmittedPathId: SafePathId(target, ignoreOrderDelay: false),
    contactChangedMaterially: false,
    commanderInitiative01: 0.50f));
var command = TacticalCommandLedger.Summarize(
    BuildCommanderProfile(issuer),
    BuildCommanderProfile(target),
    friction);

EmitDirect(
    "TacticalCommandQueued",
    "command-queued|" + SafeInstanceId(issuer) + "|" + SafeInstanceId(target) + "|" + command.Signature(),
    "[TacticalCommand] event=queued relation=" + relation +
    " source=" + SafeUnitName(issuer) +
    " target=" + SafeUnitName(target) +
    " summary=" + command.Signature() +
    " reason=" + command.Reason +
    " dlcWl=" + SafeDlcWlActive());
```

- [ ] **Step 3: Add delivery-friction telemetry from courier lines**

Inside `ObserveCourierLine(...)`, after the existing `[TacticalPlayerOrder] event=delivery` `EmitDirect(...)` call, add:

```csharp
var deliveryKind = delivery == "bugle"
    ? TacticalOrderDelivery.Bugle
    : delivery == "courier"
        ? TacticalOrderDelivery.Courier
        : TacticalOrderDelivery.Unknown;
bool courierMissing = line != null && line.type == 1 && line.lineactive && line.courierref == null;
var friction = TacticalOrderFriction.Evaluate(new TacticalOrderFrictionInput(
    orderDelayEnabled: GameVars.useorderdelays,
    queueProcessing: processTime > 0f,
    queueDelayHours: processTime,
    delivery: deliveryKind,
    deliveryProcessHours: processTime,
    courierMissing: courierMissing,
    orderState: lineTarget != null ? lineTarget.orderstate : 0,
    intendedPathId: SafePathId(lineTarget, ignoreOrderDelay: true),
    transmittedPathId: SafePathId(lineTarget, ignoreOrderDelay: false),
    contactChangedMaterially: false,
    commanderInitiative01: 0.50f));
var command = TacticalCommandLedger.Summarize(
    BuildCommanderProfile(lineSource),
    BuildCommanderProfile(lineTarget),
    friction);

EmitDirect(
    "TacticalOrderDelivery",
    "order-delivery|" + SafeInstanceId(lineSource) + "|" + SafeInstanceId(lineTarget) + "|" + command.Signature(),
    "[TacticalOrder] event=delivery relation=" + relation +
    " source=" + SafeUnitName(lineSource) +
    " target=" + SafeUnitName(lineTarget) +
    " delivery=" + delivery +
    " friction=" + friction.State +
    " delivered=" + friction.IsDelivered +
    " delayed=" + friction.IsDelayed +
    " pathLag=" + friction.TransmittedPathDiffers +
    " pressure=" + FormatHours(friction.DelayPressure) +
    " command=" + command.Signature() +
    " dlcWl=" + SafeDlcWlActive());
```

- [ ] **Step 4: Add safe profile/path helpers**

Add these helpers near existing `Safe*` helpers in `TacticalObserverPatch.cs`:

```csharp
private static TacticalCommanderProfile BuildCommanderProfile(Regiment unit)
{
    if (unit == null)
    {
        return TacticalCommanderProfile.FromVanillaShape(
            "unknown",
            "unknown",
            -1,
            false,
            false,
            "",
            -1,
            -1,
            0.5f);
    }

    return TacticalCommanderProfile.FromVanillaShape(
        SafeInstanceId(unit),
        SafeUnitName(unit),
        unit.unittyp,
        unit.istopunit,
        unit.dlcw_isundercommander,
        SafeParentId(unit),
        SafeAlliance(unit),
        SafeSide(unit),
        0.5f);
}

private static int SafePathId(Regiment unit, bool ignoreOrderDelay)
{
    if (unit == null) return -1;

    try
    {
        return unit.GetLastTransmittedPath(ignoreOrderDelay);
    }
    catch (Exception ex)
    {
        OnceLog.Warning("tactical-observer:pathid", "Tactical path read failed: " + ex.Message);
        return -1;
    }
}

private static string SafeParentId(Regiment unit)
{
    try
    {
        if (unit == null || unit.groupposition == null || unit.groupposition.parentunit == null) return "";
        return SafeInstanceId(unit.groupposition.parentunit.GetComponent<Regiment>());
    }
    catch
    {
        return "";
    }
}

private static int SafeAlliance(Regiment unit)
{
    try
    {
        return unit != null ? unit.alliance : -1;
    }
    catch
    {
        return -1;
    }
}

private static int SafeSide(Regiment unit)
{
    try
    {
        return unit != null ? unit.side : -1;
    }
    catch
    {
        return -1;
    }
}
```

If `Regiment.side`, `Regiment.alliance`, `groupposition.parentunit`, or `Regiment.GetLastTransmittedPath(bool)` do not compile against the refs, replace only that helper with cached reflection. Keep the public pure DTO interfaces unchanged.

- [ ] **Step 5: Run focused verification**

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
./build.sh
```

Expected:

- Console harness passes.
- Build passes with `0 Warning(s)` and `0 Error(s)`.
- No new Harmony Prefix/Transpiler target appears for B2.

- [ ] **Step 6: Commit Task 4**

Run:

```bash
git add src/WhiskeyRealism/Patches/TacticalObserverPatch.cs
git commit -m "feat: observe tactical command order friction"
```

## Task 5: Docs And Closeout

**Files:**
- Modify: `docs/handoff.md`
- Modify: `docs/patch-catalog.md`

- [ ] **Step 1: Update handoff active workstream**

In `docs/handoff.md`, update the Slice B active line to this state:

```markdown
| **Active workstream** | **Slice B3 tactical odds doctrine is next.** B0 observer smoke closed on 2026-05-07. B1 W&L charge/feud guard is implemented and B1 runtime denial smoke was deferred by user direction on 2026-05-07. B2 command hierarchy/order friction is implemented, console-tested, built, deployed, and hash-matched as read-only #35 telemetry; runtime `[TacticalCommand]` / B2 `[TacticalOrder]` smoke remains useful but is not blocking B3 planning. |
```

Update the Slice B roadmap notes to include this B2 plan:

```markdown
| **B - Tactical brain** | active prep ([design](superpowers/specs/2026-05-05-tactical-brain-design.md) + [vanilla verification](superpowers/specs/2026-05-05-tactical-brain-vanilla-verification.md) + [weapons/ammunition adjunct](superpowers/specs/2026-05-05-tactical-weapons-ammunition-design.md)) | active sequencing ([master](superpowers/plans/2026-05-05-tactical-brain-master-sequencing.md), [B0 observer](superpowers/plans/2026-05-05-tactical-b0-observer.md), [B1 guard](superpowers/plans/2026-05-07-tactical-b1-wl-feud-charge-guard.md), [B2 command/order friction](superpowers/plans/2026-05-07-tactical-b2-command-order-friction.md)) | B0 observer closed; B1 built/deployed; B2 built/deployed | v0.3.0 | ~8+ | Next slice is B3 tactical odds doctrine. B2 is read-only command/order-friction interpretation from vanilla order-delay surfaces. |
```

- [ ] **Step 2: Update patch catalog row #35**

Append this sentence to the #35 `TacticalObserverPatch` description in `docs/patch-catalog.md`:

```markdown
B2 extends this same observer with read-only `[TacticalCommand]` and order-friction summaries from vanilla queue/courier/path-transmission state; it does not add a new Harmony target or mutate order state.
```

Update `Pending`:

```markdown
Next unreserved patch ordinal is #43. B2 command/order-friction extends #35 observer telemetry; no new patch ordinal is reserved unless implementation adds a new Harmony patch file.
```

- [ ] **Step 3: Run final verification**

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
./build.sh
git diff --check
```

Expected:

- Console harness passes.
- Build passes with `0 Warning(s)` and `0 Error(s)`.
- `git diff --check` prints no whitespace errors.

- [ ] **Step 4: Deploy and hash-verify if Task 4 changed DLL behavior**

Run:

```bash
cp dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/"
stat -c '%y %s %n' dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/WhiskeyRealism.dll"
sha256sum dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/WhiskeyRealism.dll"
```

Expected: the two SHA-256 hashes match. If the game is open and Windows locks the DLL, close the game and rerun the deploy/hash commands. Do not run a smoke test unless the user explicitly asks for it.

- [ ] **Step 5: Commit Task 5**

Run:

```bash
git add docs/handoff.md docs/patch-catalog.md
git commit -m "docs: close tactical b2 command order plan"
```

## Execution Options

Plan complete and saved to `docs/superpowers/plans/2026-05-07-tactical-b2-command-order-friction.md`.

1. **Subagent-Driven (recommended)** - dispatch a fresh worker per task, review between tasks, fast iteration.
2. **Inline Execution** - execute tasks in this session using `superpowers:executing-plans`, batching with checkpoints.
