# Tactical B6b Local Reaction Scorer Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox syntax for tracking.

**Goal:** Add pure C# subordinate-reaction scoring on top of B6a intent/playbook outputs, plus a per-side reserve-policy aggregator that turns regiment-level `LineReliefRequest` signals into a single `RelieveBatteredLine` decision per side per cycle. No vanilla writes, no patches in this slice — runtime application is B6c's job.

**Status:** Complete for B6c consumption. Final review wired `TacticalLocalReactionInput.PlaybookPolicy` into the scorer: Conservative blocks `PermitCharge` and `LimitedCounterstroke`; Standard/Aggressive preserve the existing positive cases. Console harness and plugin build passed; B6b has no deploy or in-game smoke requirement.

**Implementation delta:** The task snippets below are historical execution scaffolding. The shipped B6b scorer is stricter after review: it does not emit `DenyCharge` or `LocalFallbackPressure`; it treats `PermitCharge` as the only positive charge permission; only `AttackWeakPoint` can permit charge; `Fix`/`EconomyOfForce` screen when path risk is absent; other support missions maintain line. B6c must consume the absence of `PermitCharge` as charge denial, and B8 must not assume B6b already emits fallback pressure.

**Architecture:** Two new pure types under `src/WhiskeyRealism/Tactical/`. `TacticalLocalReactionScorer` consumes intent + playbook + per-unit evidence (morale, ammo, casualties, target visibility, vanilla charge cooldown readiness, W&L ownership, B2 order friction) and produces one `TacticalLocalReactionDecision` per group. `TacticalReservePolicyLedger` aggregates reaction decisions plus reserve-availability evidence and produces one `TacticalReserveIntent` per side. Both ship covered by the console harness.

**Tech Stack:** C# netstandard2.1 (plugin) and net8.0 (test harness), no Harmony usage in this slice. Vanilla anchor cross-checks against `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs`.

---

## File Structure

**Create:**
- `src/WhiskeyRealism/Tactical/TacticalLocalReactionScorer.cs` - `LocalReaction` enum, `TacticalLocalReactionInput` struct, `TacticalLocalReactionDecision` struct, `TacticalLocalReactionScorer` static.
- `src/WhiskeyRealism/Tactical/TacticalReservePolicyLedger.cs` - `TacticalReserveIntent` enum, `TacticalReserveAvailability` struct, `TacticalReserveIntentInput` struct, `TacticalReserveIntentDecision` struct, `TacticalReservePolicyLedger` static.

**Modify:**
- `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj` - add Compile Includes for the two new files.
- `tests/WhiskeyRealism.Tests/Program.cs` - register tests + add bodies.

No `Plugin.cs` config flags in this slice — the runtime flags ship in B6c. No patches in this slice.

---

## Anchor Recheck

Before starting, verify the relevant vanilla anchors still match:

```bash
grep -n "private void MicroAICheckForCharges\|public float lastaichargetime\|public static float timetorenewaichargecheck\|public List<Regiment> reservegroups\|combatbehaviorordered\|public bool dlcw_isundercommander" /tmp/gt_src/asm/Assembly-CSharp.decompiled.cs | head -10
```

Expected:
- `MicroAICheckForCharges(Regiment, int)` at decompile 4905.
- `Regiment.lastaichargetime` declaration around 110798.
- `GamePrefs.timetorenewaichargecheck` around 51284.
- `ObjectiveChain.reservegroups` (List<Regiment>) declaration around 2972.
- `Regiment.combatbehaviorordered` and `dlcw_isundercommander` present (used as B6 input flags).

If any line drifts materially, update this plan inline before implementing.

---

## Task 1: Add `LocalReaction` enum and minimal scorer

**Files:**
- Create: `src/WhiskeyRealism/Tactical/TacticalLocalReactionScorer.cs`
- Modify: `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`, `tests/WhiskeyRealism.Tests/Program.cs`

- [x] **Step 1: Add Compile Include**

In `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`:

```xml
    <Compile Include="..\..\src\WhiskeyRealism\Tactical\TacticalLocalReactionScorer.cs" Link="TacticalLocalReactionScorer.cs" />
```

- [x] **Step 2: Write the failing test (ProbeIntent always denies charge)**

Add the dispatch entry:

```csharp
            ("tactical b6b probe intent denies charge", TacticalB6bProbeIntentDeniesCharge),
```

Add the test body:

```csharp
        private static TacticalLocalReactionInput ReactionInput(
            CommanderIntent intent,
            TacticalLocalReactionPolicy policy = TacticalLocalReactionPolicy.Standard,
            float oddsConfidence = 0.7f,
            bool targetVisible = true,
            bool targetBroken = false,
            bool targetStrongPoint = false,
            float morale = 0.7f,
            float ammoRatio = 0.7f,
            float casualtyRatio = 0.1f,
            bool flankRisk = false,
            bool wlOwnershipSafe = true,
            bool chargeCooldownReady = true,
            bool stalenessActive = false,
            bool pathRiskActive = false)
        {
            return new TacticalLocalReactionInput(
                intent: intent,
                playbookPolicy: policy,
                sectorMission: TacticalSectorMission.Hold,
                sectorOdds: 1.2f,
                sectorConfidence: oddsConfidence,
                targetVisible: targetVisible,
                targetBroken: targetBroken,
                targetStrongPoint: targetStrongPoint,
                morale01: morale,
                ammoRatio01: ammoRatio,
                casualtyRatio01: casualtyRatio,
                flankRisk: flankRisk,
                wlOwnershipSafe: wlOwnershipSafe,
                chargeCooldownReady: chargeCooldownReady,
                stalenessActive: stalenessActive,
                pathRiskActive: pathRiskActive);
        }

        private static void TacticalB6bProbeIntentDeniesCharge()
        {
            var input = ReactionInput(CommanderIntent.ProbeIntent);
            var d = TacticalLocalReactionScorer.Score(input);
            AssertTrue(d.Reaction != LocalReaction.PermitCharge, "ProbeIntent must never PermitCharge, got " + d.Reaction);
            AssertTrue(d.Reaction != LocalReaction.LimitedCounterstroke, "ProbeIntent must not produce LimitedCounterstroke, got " + d.Reaction);
        }
```

- [x] **Step 3: Run to verify failure**

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: build error citing missing `TacticalLocalReactionScorer`, `TacticalLocalReactionInput`, `LocalReaction`.

- [x] **Step 4: Implement `TacticalLocalReactionScorer.cs`**

Create `src/WhiskeyRealism/Tactical/TacticalLocalReactionScorer.cs`:

```csharp
using System;

namespace WhiskeyRealism.Tactical
{
    public enum LocalReaction
    {
        MaintainLine = 0,
        Screen = 1,
        ProbeRange = 2,
        RefuseFlank = 3,
        LimitedCounterstroke = 4,
        DenyCharge = 5,
        PermitCharge = 6,
        LineReliefRequest = 7,
        LocalFallbackPressure = 8
    }

    public readonly struct TacticalLocalReactionInput
    {
        public TacticalLocalReactionInput(
            CommanderIntent intent,
            TacticalLocalReactionPolicy playbookPolicy,
            TacticalSectorMission sectorMission,
            float sectorOdds,
            float sectorConfidence,
            bool targetVisible,
            bool targetBroken,
            bool targetStrongPoint,
            float morale01,
            float ammoRatio01,
            float casualtyRatio01,
            bool flankRisk,
            bool wlOwnershipSafe,
            bool chargeCooldownReady,
            bool stalenessActive,
            bool pathRiskActive)
        {
            Intent = intent;
            PlaybookPolicy = playbookPolicy;
            SectorMission = sectorMission;
            SectorOdds = Sanitize(sectorOdds);
            SectorConfidence = Clamp01(sectorConfidence);
            TargetVisible = targetVisible;
            TargetBroken = targetBroken;
            TargetStrongPoint = targetStrongPoint;
            Morale01 = Clamp01(morale01);
            AmmoRatio01 = Clamp01(ammoRatio01);
            CasualtyRatio01 = Clamp01(casualtyRatio01);
            FlankRisk = flankRisk;
            WlOwnershipSafe = wlOwnershipSafe;
            ChargeCooldownReady = chargeCooldownReady;
            StalenessActive = stalenessActive;
            PathRiskActive = pathRiskActive;
        }

        public CommanderIntent Intent { get; }
        public TacticalLocalReactionPolicy PlaybookPolicy { get; }
        public TacticalSectorMission SectorMission { get; }
        public float SectorOdds { get; }
        public float SectorConfidence { get; }
        public bool TargetVisible { get; }
        public bool TargetBroken { get; }
        public bool TargetStrongPoint { get; }
        public float Morale01 { get; }
        public float AmmoRatio01 { get; }
        public float CasualtyRatio01 { get; }
        public bool FlankRisk { get; }
        public bool WlOwnershipSafe { get; }
        public bool ChargeCooldownReady { get; }
        public bool StalenessActive { get; }
        public bool PathRiskActive { get; }

        private static float Sanitize(float v)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) return 0f;
            return Math.Max(0f, v);
        }

        private static float Clamp01(float v)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) return 0f;
            if (v < 0f) return 0f;
            if (v > 1f) return 1f;
            return v;
        }
    }

    public readonly struct TacticalLocalReactionDecision
    {
        public TacticalLocalReactionDecision(LocalReaction reaction, bool reliefRequested, float confidence, string reason)
        {
            Reaction = reaction;
            ReliefRequested = reliefRequested;
            Confidence = Clamp01(confidence);
            Reason = string.IsNullOrEmpty(reason) ? "unknown" : reason;
        }

        public LocalReaction Reaction { get; }
        public bool ReliefRequested { get; }
        public float Confidence { get; }
        public string Reason { get; }

        private static float Clamp01(float v)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) return 0f;
            if (v < 0f) return 0f;
            if (v > 1f) return 1f;
            return v;
        }
    }

    public static class TacticalLocalReactionScorer
    {
        public static TacticalLocalReactionDecision Score(TacticalLocalReactionInput input)
        {
            if (!input.WlOwnershipSafe)
                return new TacticalLocalReactionDecision(LocalReaction.MaintainLine, ReliefRequested(input), 0.4f, "wl-ownership-blocked");

            if (input.StalenessActive)
                return new TacticalLocalReactionDecision(LocalReaction.MaintainLine, ReliefRequested(input), 0.5f, "request-new-intent");

            if (input.FlankRisk)
                return new TacticalLocalReactionDecision(LocalReaction.RefuseFlank, ReliefRequested(input), 0.7f, "flank-risk");

            switch (input.Intent)
            {
                case CommanderIntent.HoldToLast:
                    return new TacticalLocalReactionDecision(LocalReaction.MaintainLine, ReliefRequested(input), 0.7f, "hold-to-last");

                case CommanderIntent.Hold:
                    return new TacticalLocalReactionDecision(
                        ReliefRequested(input) ? LocalReaction.LineReliefRequest : LocalReaction.MaintainLine,
                        ReliefRequested(input),
                        0.65f,
                        "hold");

                case CommanderIntent.Defend:
                    if (CanLimitedCounterstroke(input))
                        return new TacticalLocalReactionDecision(LocalReaction.LimitedCounterstroke, ReliefRequested(input), 0.65f, "limited-counterstroke");
                    return new TacticalLocalReactionDecision(
                        ReliefRequested(input) ? LocalReaction.LineReliefRequest : LocalReaction.MaintainLine,
                        ReliefRequested(input),
                        0.6f,
                        "defend");

                case CommanderIntent.ProbeIntent:
                    if (input.SectorConfidence < 0.55f)
                        return new TacticalLocalReactionDecision(LocalReaction.ProbeRange, ReliefRequested(input), 0.6f, "probe-range");
                    return new TacticalLocalReactionDecision(LocalReaction.Screen, ReliefRequested(input), 0.6f, "screen");

                case CommanderIntent.Attack:
                case CommanderIntent.AllOutAttack:
                    if (CanPermitCharge(input))
                        return new TacticalLocalReactionDecision(LocalReaction.PermitCharge, ReliefRequested(input), 0.7f, "permit-charge");
                    if (input.SectorMission == TacticalSectorMission.Fix || input.SectorMission == TacticalSectorMission.EconomyOfForce)
                        return new TacticalLocalReactionDecision(LocalReaction.Screen, ReliefRequested(input), 0.6f, "fix-screen");
                    return new TacticalLocalReactionDecision(LocalReaction.MaintainLine, ReliefRequested(input), 0.55f, "attack-maintain");

                default:
                    return new TacticalLocalReactionDecision(LocalReaction.MaintainLine, ReliefRequested(input), 0.5f, "default");
            }
        }

        private static bool CanLimitedCounterstroke(TacticalLocalReactionInput input)
        {
            return input.TargetVisible
                && (input.TargetBroken || !input.TargetStrongPoint)
                && input.SectorOdds >= 1.20f
                && input.SectorConfidence >= 0.55f
                && !input.PathRiskActive
                && !input.TargetStrongPoint;
        }

        private static bool CanPermitCharge(TacticalLocalReactionInput input)
        {
            return input.TargetVisible
                && !input.TargetStrongPoint
                && input.ChargeCooldownReady
                && input.WlOwnershipSafe
                && input.SectorConfidence >= 0.55f
                && !input.PathRiskActive;
        }

        private static bool ReliefRequested(TacticalLocalReactionInput input)
        {
            // Casualty / morale / ammo / flank thresholds: any one crossing triggers a request.
            if (input.CasualtyRatio01 >= 0.40f) return true;
            if (input.Morale01 <= 0.35f) return true;
            if (input.AmmoRatio01 <= 0.20f) return true;
            if (input.FlankRisk && input.Morale01 <= 0.55f) return true;
            return false;
        }
    }
}
```

- [x] **Step 5: Run tests, verify pass**

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: PASS.

- [x] **Step 6: Commit**

```bash
git add src/WhiskeyRealism/Tactical/TacticalLocalReactionScorer.cs tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj tests/WhiskeyRealism.Tests/Program.cs
git commit -m "$(cat <<'EOF'
feat(tactical): add B6b TacticalLocalReactionScorer

Pure scorer that maps intent + per-unit evidence onto bounded local
reactions. Charge permit/deny respects W&L ownership, vanilla cooldown,
target type, and path-risk evidence. No vanilla writes.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 2: Cover the remaining intent + safety branches

**Files:**
- Modify: `tests/WhiskeyRealism.Tests/Program.cs`

- [x] **Step 1: Add ten dispatch entries**

```csharp
            ("tactical b6b hold to last blocks fallback pressure", TacticalB6bHoldToLastBlocksFallbackPressure),
            ("tactical b6b defend with weak exposed target permits limited counterstroke", TacticalB6bDefendPermitsLimitedCounterstroke),
            ("tactical b6b defend against strongpoint denies counterstroke", TacticalB6bDefendStrongpointDeniesCounterstroke),
            ("tactical b6b attack permits charge against fresh target", TacticalB6bAttackPermitsChargeAgainstFreshTarget),
            ("tactical b6b attack with cooldown active denies charge", TacticalB6bAttackCooldownActiveDeniesCharge),
            ("tactical b6b attack with strongpoint target denies charge", TacticalB6bAttackStrongpointDeniesCharge),
            ("tactical b6b stale order downgrades to maintain line", TacticalB6bStaleOrderDowngradesToMaintainLine),
            ("tactical b6b wl ownership unsafe forces maintain line", TacticalB6bWlOwnershipUnsafeForcesMaintain),
            ("tactical b6b path risk blocks runtime application", TacticalB6bPathRiskBlocksRuntime),
            ("tactical b6b battered frontline emits line relief request under hold", TacticalB6bBatteredFrontlineEmitsLineReliefRequest),
```

- [x] **Step 2: Add the test bodies**

```csharp
        private static void TacticalB6bHoldToLastBlocksFallbackPressure()
        {
            var input = ReactionInput(CommanderIntent.HoldToLast, morale: 0.3f, casualtyRatio: 0.5f);
            var d = TacticalLocalReactionScorer.Score(input);
            AssertTrue(d.Reaction != LocalReaction.LocalFallbackPressure, "HoldToLast must not emit LocalFallbackPressure, got " + d.Reaction);
            AssertTrue(d.Reaction == LocalReaction.MaintainLine, "Expected MaintainLine under HoldToLast");
        }

        private static void TacticalB6bDefendPermitsLimitedCounterstroke()
        {
            var input = ReactionInput(CommanderIntent.Defend, oddsConfidence: 0.7f, targetBroken: true, targetStrongPoint: false);
            var d = TacticalLocalReactionScorer.Score(input);
            AssertTrue(d.Reaction == LocalReaction.LimitedCounterstroke, "Expected LimitedCounterstroke, got " + d.Reaction);
        }

        private static void TacticalB6bDefendStrongpointDeniesCounterstroke()
        {
            var input = ReactionInput(CommanderIntent.Defend, oddsConfidence: 0.7f, targetStrongPoint: true);
            var d = TacticalLocalReactionScorer.Score(input);
            AssertTrue(d.Reaction != LocalReaction.LimitedCounterstroke, "Strongpoint target must deny counterstroke, got " + d.Reaction);
        }

        private static void TacticalB6bAttackPermitsChargeAgainstFreshTarget()
        {
            var input = ReactionInput(CommanderIntent.Attack, oddsConfidence: 0.7f, chargeCooldownReady: true, targetStrongPoint: false);
            var d = TacticalLocalReactionScorer.Score(input);
            AssertTrue(d.Reaction == LocalReaction.PermitCharge, "Expected PermitCharge, got " + d.Reaction);
        }

        private static void TacticalB6bAttackCooldownActiveDeniesCharge()
        {
            var input = ReactionInput(CommanderIntent.Attack, oddsConfidence: 0.7f, chargeCooldownReady: false);
            var d = TacticalLocalReactionScorer.Score(input);
            AssertTrue(d.Reaction != LocalReaction.PermitCharge, "Cooldown active must deny PermitCharge, got " + d.Reaction);
        }

        private static void TacticalB6bAttackStrongpointDeniesCharge()
        {
            var input = ReactionInput(CommanderIntent.Attack, oddsConfidence: 0.7f, targetStrongPoint: true);
            var d = TacticalLocalReactionScorer.Score(input);
            AssertTrue(d.Reaction != LocalReaction.PermitCharge, "Strongpoint target must deny PermitCharge, got " + d.Reaction);
        }

        private static void TacticalB6bStaleOrderDowngradesToMaintainLine()
        {
            var input = ReactionInput(CommanderIntent.Attack, stalenessActive: true);
            var d = TacticalLocalReactionScorer.Score(input);
            AssertTrue(d.Reaction == LocalReaction.MaintainLine, "Stale order must downgrade to MaintainLine, got " + d.Reaction);
            AssertTrue(d.Reason == "request-new-intent", "Expected request-new-intent reason, got " + d.Reason);
        }

        private static void TacticalB6bWlOwnershipUnsafeForcesMaintain()
        {
            var input = ReactionInput(CommanderIntent.Attack, wlOwnershipSafe: false);
            var d = TacticalLocalReactionScorer.Score(input);
            AssertTrue(d.Reaction == LocalReaction.MaintainLine, "WL ownership unsafe must force MaintainLine, got " + d.Reaction);
            AssertTrue(d.Reason == "wl-ownership-blocked", "Expected wl-ownership-blocked reason, got " + d.Reason);
        }

        private static void TacticalB6bPathRiskBlocksRuntime()
        {
            var input = ReactionInput(CommanderIntent.Attack, oddsConfidence: 0.7f, chargeCooldownReady: true, pathRiskActive: true);
            var d = TacticalLocalReactionScorer.Score(input);
            AssertTrue(d.Reaction != LocalReaction.PermitCharge, "Path risk must block PermitCharge, got " + d.Reaction);
            AssertTrue(d.Reaction != LocalReaction.LimitedCounterstroke, "Path risk must block LimitedCounterstroke, got " + d.Reaction);
        }

        private static void TacticalB6bBatteredFrontlineEmitsLineReliefRequest()
        {
            var input = ReactionInput(CommanderIntent.Hold, morale: 0.25f, casualtyRatio: 0.5f);
            var d = TacticalLocalReactionScorer.Score(input);
            AssertTrue(d.Reaction == LocalReaction.LineReliefRequest, "Expected LineReliefRequest, got " + d.Reaction);
            AssertTrue(d.ReliefRequested, "ReliefRequested flag must be true");
        }
```

- [x] **Step 3: Run tests**

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: 10 new B6b tests PASS.

- [x] **Step 4: Commit**

```bash
git add tests/WhiskeyRealism.Tests/Program.cs
git commit -m "$(cat <<'EOF'
test(tactical): cover B6b reaction safety, charge, and relief branches

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 3: Add `TacticalReservePolicyLedger` skeleton

**Files:**
- Create: `src/WhiskeyRealism/Tactical/TacticalReservePolicyLedger.cs`
- Modify: `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`, `tests/WhiskeyRealism.Tests/Program.cs`

- [x] **Step 1: Add Compile Include**

In `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`:

```xml
    <Compile Include="..\..\src\WhiskeyRealism\Tactical\TacticalReservePolicyLedger.cs" Link="TacticalReservePolicyLedger.cs" />
```

- [x] **Step 2: Write the failing test**

Add the dispatch entry:

```csharp
            ("tactical b6b reserve aggregator emits relieve battered line when reserve safe", TacticalB6bReserveEmitsRelieveBatteredLineWhenReserveSafe),
```

Add the test body:

```csharp
        private static void TacticalB6bReserveEmitsRelieveBatteredLineWhenReserveSafe()
        {
            var batteredA = new TacticalLocalReactionDecision(LocalReaction.LineReliefRequest, true, 0.7f, "battered");
            var batteredB = new TacticalLocalReactionDecision(LocalReaction.LineReliefRequest, true, 0.7f, "battered");
            var holdC = new TacticalLocalReactionDecision(LocalReaction.MaintainLine, false, 0.5f, "ok");

            var availability = new TacticalReserveAvailability(
                reserveCount: 2,
                hasFlankRisk: false,
                lastReserveIsFlankGuard: false,
                wlOwnershipSafe: true,
                stalenessActive: false);

            var input = new TacticalReserveIntentInput(
                playbookPolicy: TacticalReservePolicy.PrepareRelief,
                reactions: new[] { batteredA, batteredB, holdC },
                availability: availability);

            var decision = TacticalReservePolicyLedger.Decide(input);

            AssertTrue(decision.Intent == TacticalReserveIntent.RelieveBatteredLine, "Expected RelieveBatteredLine, got " + decision.Intent);
            AssertTrue(decision.AllowsRuntimeMutation, "AllowsRuntimeMutation must be true with safe reserve and ownership");
        }
```

- [x] **Step 3: Run to verify failure**

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: missing `TacticalReservePolicyLedger`, etc.

- [x] **Step 4: Implement `TacticalReservePolicyLedger.cs`**

Create `src/WhiskeyRealism/Tactical/TacticalReservePolicyLedger.cs`:

```csharp
using System;

namespace WhiskeyRealism.Tactical
{
    public enum TacticalReserveIntent
    {
        None = 0,
        HoldReserve = 1,
        PrepareRelief = 2,
        RelieveBatteredLine = 3,
        FlankGuard = 4,
        ExploitWeakPoint = 5
    }

    public readonly struct TacticalReserveAvailability
    {
        public TacticalReserveAvailability(
            int reserveCount,
            bool hasFlankRisk,
            bool lastReserveIsFlankGuard,
            bool wlOwnershipSafe,
            bool stalenessActive)
        {
            ReserveCount = Math.Max(0, reserveCount);
            HasFlankRisk = hasFlankRisk;
            LastReserveIsFlankGuard = lastReserveIsFlankGuard;
            WlOwnershipSafe = wlOwnershipSafe;
            StalenessActive = stalenessActive;
        }

        public int ReserveCount { get; }
        public bool HasFlankRisk { get; }
        public bool LastReserveIsFlankGuard { get; }
        public bool WlOwnershipSafe { get; }
        public bool StalenessActive { get; }
    }

    public readonly struct TacticalReserveIntentInput
    {
        public TacticalReserveIntentInput(
            TacticalReservePolicy playbookPolicy,
            TacticalLocalReactionDecision[] reactions,
            TacticalReserveAvailability availability)
        {
            PlaybookPolicy = playbookPolicy;
            Reactions = reactions ?? Array.Empty<TacticalLocalReactionDecision>();
            Availability = availability;
        }

        public TacticalReservePolicy PlaybookPolicy { get; }
        public TacticalLocalReactionDecision[] Reactions { get; }
        public TacticalReserveAvailability Availability { get; }
    }

    public readonly struct TacticalReserveIntentDecision
    {
        public TacticalReserveIntentDecision(
            TacticalReserveIntent intent,
            bool allowsRuntimeMutation,
            float confidence,
            string reason)
        {
            Intent = intent;
            AllowsRuntimeMutation = allowsRuntimeMutation;
            Confidence = Clamp01(confidence);
            Reason = string.IsNullOrEmpty(reason) ? "unknown" : reason;
        }

        public TacticalReserveIntent Intent { get; }
        public bool AllowsRuntimeMutation { get; }
        public float Confidence { get; }
        public string Reason { get; }

        private static float Clamp01(float v)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) return 0f;
            if (v < 0f) return 0f;
            if (v > 1f) return 1f;
            return v;
        }
    }

    public static class TacticalReservePolicyLedger
    {
        public static TacticalReserveIntentDecision Decide(TacticalReserveIntentInput input)
        {
            int reliefRequests = CountRelief(input.Reactions);
            var avail = input.Availability;

            if (!avail.WlOwnershipSafe)
                return new TacticalReserveIntentDecision(TacticalReserveIntent.HoldReserve, false, 0.4f, "wl-ownership-blocked");

            if (avail.StalenessActive)
                return new TacticalReserveIntentDecision(TacticalReserveIntent.PrepareRelief, false, 0.4f, "stale-order");

            if (avail.ReserveCount <= 0)
                return new TacticalReserveIntentDecision(TacticalReserveIntent.None, false, 0.5f, "no-reserve");

            if (avail.HasFlankRisk && avail.LastReserveIsFlankGuard)
                return new TacticalReserveIntentDecision(TacticalReserveIntent.FlankGuard, false, 0.65f, "last-reserve-is-flank-guard");

            if (avail.HasFlankRisk && avail.ReserveCount >= 2)
                return new TacticalReserveIntentDecision(TacticalReserveIntent.FlankGuard, true, 0.6f, "flank-guard");

            if (reliefRequests >= 2 && (input.PlaybookPolicy == TacticalReservePolicy.PrepareRelief
                                        || input.PlaybookPolicy == TacticalReservePolicy.RelieveBatteredLine))
                return new TacticalReserveIntentDecision(TacticalReserveIntent.RelieveBatteredLine, true, 0.7f, "battered-line");

            if (reliefRequests >= 1)
                return new TacticalReserveIntentDecision(TacticalReserveIntent.PrepareRelief, false, 0.55f, "prepare-relief");

            if (input.PlaybookPolicy == TacticalReservePolicy.ExploitWeakPoint)
                return new TacticalReserveIntentDecision(TacticalReserveIntent.ExploitWeakPoint, true, 0.65f, "exploit-weak-point");

            return new TacticalReserveIntentDecision(TacticalReserveIntent.HoldReserve, false, 0.6f, "hold-reserve");
        }

        private static int CountRelief(TacticalLocalReactionDecision[] reactions)
        {
            if (reactions == null) return 0;
            int n = 0;
            for (int i = 0; i < reactions.Length; i++)
                if (reactions[i].ReliefRequested || reactions[i].Reaction == LocalReaction.LineReliefRequest) n++;
            return n;
        }
    }
}
```

- [x] **Step 5: Run, verify pass**

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: PASS.

- [x] **Step 6: Commit**

```bash
git add src/WhiskeyRealism/Tactical/TacticalReservePolicyLedger.cs tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj tests/WhiskeyRealism.Tests/Program.cs
git commit -m "$(cat <<'EOF'
feat(tactical): add B6b TacticalReservePolicyLedger aggregator

Pure per-side aggregator turning regiment LineReliefRequest signals plus
reserve availability and W&L ownership into a single TacticalReserveIntent
decision. No vanilla writes.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 4: Cover the reserve-aggregator branches

**Files:**
- Modify: `tests/WhiskeyRealism.Tests/Program.cs`

- [x] **Step 1: Add seven dispatch entries**

```csharp
            ("tactical b6b reserve no reserve yields none", TacticalB6bReserveNoReserveYieldsNone),
            ("tactical b6b reserve flank risk with last reserve guards", TacticalB6bReserveLastReserveGuardsFlank),
            ("tactical b6b reserve flank risk with multiple reserves picks flank guard", TacticalB6bReserveMultipleFlankGuard),
            ("tactical b6b reserve single relief request prepares relief", TacticalB6bReserveSingleReliefPrepares),
            ("tactical b6b reserve exploit weak point picks exploit", TacticalB6bReserveExploitWeakPoint),
            ("tactical b6b reserve wl ownership unsafe holds reserve", TacticalB6bReserveWlUnsafeHolds),
            ("tactical b6b reserve stale order prepares without mutation", TacticalB6bReserveStaleOrderNoMutation),
```

- [x] **Step 2: Add the test bodies**

```csharp
        private static TacticalLocalReactionDecision Battered() => new TacticalLocalReactionDecision(LocalReaction.LineReliefRequest, true, 0.7f, "battered");
        private static TacticalLocalReactionDecision OkLine() => new TacticalLocalReactionDecision(LocalReaction.MaintainLine, false, 0.5f, "ok");

        private static void TacticalB6bReserveNoReserveYieldsNone()
        {
            var avail = new TacticalReserveAvailability(0, false, false, true, false);
            var input = new TacticalReserveIntentInput(TacticalReservePolicy.HoldReserve, new[] { Battered(), Battered() }, avail);
            var d = TacticalReservePolicyLedger.Decide(input);
            AssertTrue(d.Intent == TacticalReserveIntent.None, "Expected None, got " + d.Intent);
            AssertTrue(!d.AllowsRuntimeMutation, "No-reserve must not mutate");
        }

        private static void TacticalB6bReserveLastReserveGuardsFlank()
        {
            var avail = new TacticalReserveAvailability(1, true, true, true, false);
            var input = new TacticalReserveIntentInput(TacticalReservePolicy.PrepareRelief, new[] { Battered(), Battered() }, avail);
            var d = TacticalReservePolicyLedger.Decide(input);
            AssertTrue(d.Intent == TacticalReserveIntent.FlankGuard, "Expected FlankGuard, got " + d.Intent);
            AssertTrue(!d.AllowsRuntimeMutation, "Last-reserve flank guard must not mutate");
        }

        private static void TacticalB6bReserveMultipleFlankGuard()
        {
            var avail = new TacticalReserveAvailability(3, true, false, true, false);
            var input = new TacticalReserveIntentInput(TacticalReservePolicy.PrepareRelief, new[] { OkLine() }, avail);
            var d = TacticalReservePolicyLedger.Decide(input);
            AssertTrue(d.Intent == TacticalReserveIntent.FlankGuard, "Expected FlankGuard, got " + d.Intent);
            AssertTrue(d.AllowsRuntimeMutation, "Multi-reserve flank guard may mutate");
        }

        private static void TacticalB6bReserveSingleReliefPrepares()
        {
            var avail = new TacticalReserveAvailability(2, false, false, true, false);
            var input = new TacticalReserveIntentInput(TacticalReservePolicy.HoldReserve, new[] { Battered(), OkLine(), OkLine() }, avail);
            var d = TacticalReservePolicyLedger.Decide(input);
            AssertTrue(d.Intent == TacticalReserveIntent.PrepareRelief, "Expected PrepareRelief, got " + d.Intent);
            AssertTrue(!d.AllowsRuntimeMutation, "Single relief must not mutate yet");
        }

        private static void TacticalB6bReserveExploitWeakPoint()
        {
            var avail = new TacticalReserveAvailability(2, false, false, true, false);
            var input = new TacticalReserveIntentInput(TacticalReservePolicy.ExploitWeakPoint, new[] { OkLine(), OkLine() }, avail);
            var d = TacticalReservePolicyLedger.Decide(input);
            AssertTrue(d.Intent == TacticalReserveIntent.ExploitWeakPoint, "Expected ExploitWeakPoint, got " + d.Intent);
            AssertTrue(d.AllowsRuntimeMutation, "ExploitWeakPoint allows mutation when conditions met");
        }

        private static void TacticalB6bReserveWlUnsafeHolds()
        {
            var avail = new TacticalReserveAvailability(2, false, false, false, false);
            var input = new TacticalReserveIntentInput(TacticalReservePolicy.PrepareRelief, new[] { Battered(), Battered() }, avail);
            var d = TacticalReservePolicyLedger.Decide(input);
            AssertTrue(d.Intent == TacticalReserveIntent.HoldReserve, "WL unsafe must HoldReserve, got " + d.Intent);
            AssertTrue(!d.AllowsRuntimeMutation, "WL unsafe must not mutate");
        }

        private static void TacticalB6bReserveStaleOrderNoMutation()
        {
            var avail = new TacticalReserveAvailability(2, false, false, true, true);
            var input = new TacticalReserveIntentInput(TacticalReservePolicy.PrepareRelief, new[] { Battered(), Battered() }, avail);
            var d = TacticalReservePolicyLedger.Decide(input);
            AssertTrue(d.Intent == TacticalReserveIntent.PrepareRelief, "Stale order must PrepareRelief, got " + d.Intent);
            AssertTrue(!d.AllowsRuntimeMutation, "Stale order must not mutate");
        }
```

- [x] **Step 3: Run tests**

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: 7 new B6b reserve tests PASS.

- [x] **Step 4: Commit**

```bash
git add tests/WhiskeyRealism.Tests/Program.cs
git commit -m "$(cat <<'EOF'
test(tactical): cover B6b reserve aggregator branches

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 5: Self-review and final harness sweep

**Files:** none modified.

- [x] **Step 1: Run the full harness**

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: every prior B-tactical test still passes plus all B6a + B6b tests pass.

- [x] **Step 2: Build the plugin to confirm netstandard2.1 still compiles**

```bash
./build.sh
```

Expected: BUILD SUCCEEDED with 0 warnings, 0 errors. (Plugin compiles even though no patches are added in B6b — the Tactical types are referenced only by the B6a observer Postfix and the future B6c patches.)

- [x] **Step 3: Sanity check no patch was modified**

```bash
git status
git diff --stat src/WhiskeyRealism/Patches/
```

Expected: empty diff under `Patches/` (B6b is pure logic). If anything appears there, revert it — that work belongs to B6c.

- [x] **Step 4: Final commit if needed**

```bash
git add docs/handoff.md
git commit --allow-empty -m "$(cat <<'EOF'
chore(tactical): mark B6b ledgers/scorer ready for B6c consumption

Console harness covers reaction safety, charge, and reserve aggregator
branches. No vanilla writes. B6c will consume these decisions.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

(`--allow-empty` only if there's nothing else to commit; otherwise stage changes that handoff requires.)

---

## Rollback

B6b ships no patches and no DLL changes that affect runtime behavior. Rollback options:

- Remove the two new pure files plus their Compile Includes plus their tests; the plugin still compiles.
- If a future B6c patch consumes a stale field, fix B6b in place rather than rolling back — the consumers expect immutable decisions, and any change is additive.

## Smoke Expectations

There is no in-game smoke for B6b. All verification is the console harness. Telemetry for `[TacticalLocalReaction]` and `[TacticalReserveIntent]` is wired in B6c.
