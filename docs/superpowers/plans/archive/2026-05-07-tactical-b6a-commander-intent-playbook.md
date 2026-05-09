# Tactical B6a Commander Intent And Playbook Ledger Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add pure C# ledgers for tactical commander intent and tactical playbook plus a default-off telemetry-only Postfix observer, with full console-harness coverage. No vanilla writes, no behavior changes, no movement.

**Architecture:** B6a is a pure-logic addition under `src/WhiskeyRealism/Tactical/` plus one new Postfix patch under `src/WhiskeyRealism/Patches/` that reads vanilla state, calls the new ledgers, and emits bounded `[TacticalIntent]` and `[TacticalPlaybook]` telemetry. Strategic-to-tactical translation derives intent from `OperationPosture` (`Strategic/HistoricalOperationModels.cs:18-28`), in-battle commander initiative, and B3/B4/B5 evidence. The playbook ledger anchors positional decisions to `ObjectiveChain.linegroup_centerunit/leftunits/rightunits` (decompile 2992-2996) and `flankpositionobj[0/1]` evidence (5771-5777).

**Tech Stack:** C# netstandard2.1 (plugin) and net8.0 (test harness), HarmonyX 2.10.2, BepInEx 5.4.x, Grand Tactician decompile at `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs`.

---

## File Structure

**Create:**
- `src/WhiskeyRealism/Tactical/TacticalCommanderIntent.cs` - `CommanderIntent` enum, `TacticalIntentInput` struct, `TacticalIntentDecision` struct, `TacticalCommanderIntentResolver` static.
- `src/WhiskeyRealism/Tactical/TacticalPlaybookLedger.cs` - `TacticalPlaybook`, `TacticalRefusedFlank`, `TacticalSectorPosition`, `TacticalReservePolicy`, `TacticalLocalReactionPolicy` enums; `TacticalPlaybookSectorView`, `TacticalPlaybookInput`, `TacticalPlaybookDecision` structs; `TacticalPlaybookLedger` static.
- `src/WhiskeyRealism/Patches/BattleCommanderIntentObserverPatch.cs` - default-off Postfix on `AIBattle.AdjustGroupAIStance` that reads vanilla state, computes intent + playbook, and emits telemetry only.

**Modify:**
- `src/WhiskeyRealism/Plugin.cs` - add `EnableTacticalCommanderIntentDoctrine` ConfigEntry binding.
- `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj` - add `<Compile Include>` entries for the two new Tactical files.
- `tests/WhiskeyRealism.Tests/Program.cs` - register new test methods + add their bodies.
- `docs/patch-catalog.md` - add catalog entry for the new patch.
- `docs/handoff.md` - "What just shipped" update after smoke.

---

## Anchor Recheck

Before starting, verify decompile anchors still match:

```bash
grep -n "private void AdjustGroupAIStance\|public List<Regiment> linegroup_leftunits\|public Regiment linegroup_centerunit\|public List<Regiment> linegroup_rightunits\|public List<ObjectiveChain> objectivechain\|flankpositionobj\|anchoredflank\|public enum OperationPosture" /tmp/gt_src/asm/Assembly-CSharp.decompiled.cs ~/Projects/whiskey-realism-mod/src/WhiskeyRealism/Strategic/HistoricalOperationModels.cs
```

Expected: `AdjustGroupAIStance` at decompile line 4221, `linegroup_centerunit` at 2992, `linegroup_leftunits` at 2994, `linegroup_rightunits` at 2996, `objectivechain` at 3282, `OperationPosture` enum at `HistoricalOperationModels.cs:18-28`. If any line drifts, update this plan inline before proceeding.

---

## Task 1: Add `TacticalCommanderIntent` types and resolver

**Files:**
- Create: `src/WhiskeyRealism/Tactical/TacticalCommanderIntent.cs`
- Modify: `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`, `tests/WhiskeyRealism.Tests/Program.cs`

- [ ] **Step 1: Add Compile Include for the new file**

Edit `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`. In the existing tactical block (between `TacticalDoctrineScorer.cs` and the next file), insert:

```xml
    <Compile Include="..\..\src\WhiskeyRealism\Tactical\TacticalCommanderIntent.cs" Link="TacticalCommanderIntent.cs" />
```

- [ ] **Step 2: Write the failing test**

Edit `tests/WhiskeyRealism.Tests/Program.cs`. Find the dispatch tuple list (the `("name", Method),` lines starting near line 37) and add this line in the tactical block:

```csharp
            ("tactical b6a probe posture maps to probe intent", TacticalB6aProbePostureMapsToProbeIntent),
```

Then add the test method body (place near the other tactical helpers in the file):

```csharp
        private static void TacticalB6aProbePostureMapsToProbeIntent()
        {
            var input = new TacticalIntentInput(
                operationPosture: WhiskeyRealism.Strategic.OperationPosture.ProbeAndDevelop,
                hasPlan: true,
                vanillaMacro: 1,
                commanderInitiative01: 0.5f,
                oddsConfidence: 0.7f,
                weakPointConfirmed: false);

            var decision = TacticalCommanderIntentResolver.Resolve(input);

            Assert(decision.Intent == CommanderIntent.ProbeIntent, "Expected ProbeIntent, got " + decision.Intent);
            Assert(!decision.AllowsCharge, "ProbeIntent must not allow charge");
        }
```

- [ ] **Step 3: Run to verify failure**

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: build error about missing `TacticalIntentInput`, `TacticalCommanderIntentResolver`, `CommanderIntent`.

- [ ] **Step 4: Implement `TacticalCommanderIntent.cs`**

Create `src/WhiskeyRealism/Tactical/TacticalCommanderIntent.cs`:

```csharp
using System;
using WhiskeyRealism.Strategic;

namespace WhiskeyRealism.Tactical
{
    public enum CommanderIntent
    {
        AllOutAttack = 0,
        Attack = 1,
        ProbeIntent = 2,
        Defend = 3,
        Hold = 4,
        HoldToLast = 5
    }

    public readonly struct TacticalIntentInput
    {
        public TacticalIntentInput(
            OperationPosture operationPosture,
            bool hasPlan,
            int vanillaMacro,
            float commanderInitiative01,
            float oddsConfidence,
            bool weakPointConfirmed)
        {
            OperationPosture = operationPosture;
            HasPlan = hasPlan;
            VanillaMacro = vanillaMacro;
            CommanderInitiative01 = Clamp01(commanderInitiative01);
            OddsConfidence = Clamp01(oddsConfidence);
            WeakPointConfirmed = weakPointConfirmed;
        }

        public OperationPosture OperationPosture { get; }
        public bool HasPlan { get; }
        public int VanillaMacro { get; }
        public float CommanderInitiative01 { get; }
        public float OddsConfidence { get; }
        public bool WeakPointConfirmed { get; }

        private static float Clamp01(float v)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) return 0.5f;
            if (v < 0f) return 0f;
            if (v > 1f) return 1f;
            return v;
        }
    }

    public readonly struct TacticalIntentDecision
    {
        public TacticalIntentDecision(CommanderIntent intent, bool allowsCharge, string reason)
        {
            Intent = intent;
            AllowsCharge = allowsCharge;
            Reason = string.IsNullOrEmpty(reason) ? "unknown" : reason;
        }

        public CommanderIntent Intent { get; }
        public bool AllowsCharge { get; }
        public string Reason { get; }
    }

    public static class TacticalCommanderIntentResolver
    {
        public static TacticalIntentDecision Resolve(TacticalIntentInput input)
        {
            if (!input.HasPlan)
                return ResolveFromMacro(input);

            switch (input.OperationPosture)
            {
                case OperationPosture.ConcentratedAttack:
                    if (input.WeakPointConfirmed && input.CommanderInitiative01 >= 0.6f)
                        return new TacticalIntentDecision(CommanderIntent.AllOutAttack, true, "concentrated-attack-weak-point");
                    return new TacticalIntentDecision(CommanderIntent.Attack, true, "concentrated-attack");
                case OperationPosture.ExploitBreakthrough:
                    if (input.OddsConfidence < 0.55f)
                        return new TacticalIntentDecision(CommanderIntent.Attack, true, "exploit-low-confidence");
                    return new TacticalIntentDecision(CommanderIntent.AllOutAttack, true, "exploit-breakthrough");
                case OperationPosture.Counterstroke:
                    return new TacticalIntentDecision(CommanderIntent.Defend, true, "counterstroke");
                case OperationPosture.ProbeAndDevelop:
                    return new TacticalIntentDecision(CommanderIntent.ProbeIntent, false, "probe-and-develop");
                case OperationPosture.ScreenAndDelay:
                    return new TacticalIntentDecision(CommanderIntent.Defend, false, "screen-and-delay");
                case OperationPosture.ReinforceAndHold:
                    return new TacticalIntentDecision(CommanderIntent.Hold, false, "reinforce-and-hold");
                case OperationPosture.Recover:
                    return new TacticalIntentDecision(CommanderIntent.HoldToLast, false, "recover");
                case OperationPosture.Inherit:
                default:
                    return ResolveFromMacro(input);
            }
        }

        private static TacticalIntentDecision ResolveFromMacro(TacticalIntentInput input)
        {
            switch (input.VanillaMacro)
            {
                case 0: return new TacticalIntentDecision(CommanderIntent.Attack, true, "macro-assault");
                case 1: return new TacticalIntentDecision(CommanderIntent.Attack, true, "macro-attack");
                case 2: return new TacticalIntentDecision(CommanderIntent.Defend, false, "macro-defend");
                case 3: return new TacticalIntentDecision(CommanderIntent.HoldToLast, false, "macro-retreat-vanilla-owns");
                default: return new TacticalIntentDecision(CommanderIntent.Hold, false, "macro-dynamic");
            }
        }
    }
}
```

- [ ] **Step 5: Run to verify pass**

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: PASS for `tactical b6a probe posture maps to probe intent`, no regressions.

- [ ] **Step 6: Commit**

```bash
git add src/WhiskeyRealism/Tactical/TacticalCommanderIntent.cs tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj tests/WhiskeyRealism.Tests/Program.cs
git commit -m "$(cat <<'EOF'
feat(tactical): add B6a TacticalCommanderIntent translation

Pure C# resolver mapping strategic OperationPosture, vanilla macro, and
commander initiative onto the six B6 intent bands. No vanilla writes.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 2: Cover the remaining intent translation branches

**Files:**
- Modify: `tests/WhiskeyRealism.Tests/Program.cs`

- [ ] **Step 1: Add nine dispatch entries**

In the dispatch tuple list, add after the existing B6a entry:

```csharp
            ("tactical b6a concentrated attack maps to attack", TacticalB6aConcentratedAttackMapsToAttack),
            ("tactical b6a concentrated attack with weak point and high init upgrades to all out", TacticalB6aConcentratedAttackUpgradesToAllOut),
            ("tactical b6a exploit breakthrough downgrades on low confidence", TacticalB6aExploitDowngradesOnLowConfidence),
            ("tactical b6a counterstroke maps to defend", TacticalB6aCounterstrokeMapsToDefend),
            ("tactical b6a screen and delay maps to defend", TacticalB6aScreenAndDelayMapsToDefend),
            ("tactical b6a reinforce and hold maps to hold", TacticalB6aReinforceAndHoldMapsToHold),
            ("tactical b6a recover maps to hold to last", TacticalB6aRecoverMapsToHoldToLast),
            ("tactical b6a no plan falls back to macro", TacticalB6aNoPlanFallsBackToMacro),
            ("tactical b6a macro retreat falls to hold to last", TacticalB6aMacroRetreatFallsToHoldToLast),
```

- [ ] **Step 2: Add the test method bodies**

```csharp
        private static void TacticalB6aConcentratedAttackMapsToAttack()
        {
            var input = new TacticalIntentInput(
                WhiskeyRealism.Strategic.OperationPosture.ConcentratedAttack,
                hasPlan: true, vanillaMacro: 1, commanderInitiative01: 0.5f,
                oddsConfidence: 0.7f, weakPointConfirmed: false);
            var d = TacticalCommanderIntentResolver.Resolve(input);
            Assert(d.Intent == CommanderIntent.Attack, "Expected Attack, got " + d.Intent);
            Assert(d.AllowsCharge, "Attack should allow charge");
        }

        private static void TacticalB6aConcentratedAttackUpgradesToAllOut()
        {
            var input = new TacticalIntentInput(
                WhiskeyRealism.Strategic.OperationPosture.ConcentratedAttack,
                hasPlan: true, vanillaMacro: 0, commanderInitiative01: 0.7f,
                oddsConfidence: 0.8f, weakPointConfirmed: true);
            var d = TacticalCommanderIntentResolver.Resolve(input);
            Assert(d.Intent == CommanderIntent.AllOutAttack, "Expected AllOutAttack, got " + d.Intent);
        }

        private static void TacticalB6aExploitDowngradesOnLowConfidence()
        {
            var input = new TacticalIntentInput(
                WhiskeyRealism.Strategic.OperationPosture.ExploitBreakthrough,
                hasPlan: true, vanillaMacro: 0, commanderInitiative01: 0.7f,
                oddsConfidence: 0.4f, weakPointConfirmed: true);
            var d = TacticalCommanderIntentResolver.Resolve(input);
            Assert(d.Intent == CommanderIntent.Attack, "Expected Attack on low confidence, got " + d.Intent);
        }

        private static void TacticalB6aCounterstrokeMapsToDefend()
        {
            var input = new TacticalIntentInput(
                WhiskeyRealism.Strategic.OperationPosture.Counterstroke,
                hasPlan: true, vanillaMacro: 2, commanderInitiative01: 0.5f,
                oddsConfidence: 0.6f, weakPointConfirmed: false);
            var d = TacticalCommanderIntentResolver.Resolve(input);
            Assert(d.Intent == CommanderIntent.Defend, "Expected Defend, got " + d.Intent);
            Assert(d.AllowsCharge, "Counterstroke must keep charge available for LimitedCounterstroke");
        }

        private static void TacticalB6aScreenAndDelayMapsToDefend()
        {
            var input = new TacticalIntentInput(
                WhiskeyRealism.Strategic.OperationPosture.ScreenAndDelay,
                hasPlan: true, vanillaMacro: 2, commanderInitiative01: 0.5f,
                oddsConfidence: 0.5f, weakPointConfirmed: false);
            var d = TacticalCommanderIntentResolver.Resolve(input);
            Assert(d.Intent == CommanderIntent.Defend, "Expected Defend, got " + d.Intent);
            Assert(!d.AllowsCharge, "ScreenAndDelay must not allow charge");
        }

        private static void TacticalB6aReinforceAndHoldMapsToHold()
        {
            var input = new TacticalIntentInput(
                WhiskeyRealism.Strategic.OperationPosture.ReinforceAndHold,
                hasPlan: true, vanillaMacro: 2, commanderInitiative01: 0.5f,
                oddsConfidence: 0.5f, weakPointConfirmed: false);
            var d = TacticalCommanderIntentResolver.Resolve(input);
            Assert(d.Intent == CommanderIntent.Hold, "Expected Hold, got " + d.Intent);
        }

        private static void TacticalB6aRecoverMapsToHoldToLast()
        {
            var input = new TacticalIntentInput(
                WhiskeyRealism.Strategic.OperationPosture.Recover,
                hasPlan: true, vanillaMacro: 2, commanderInitiative01: 0.5f,
                oddsConfidence: 0.5f, weakPointConfirmed: false);
            var d = TacticalCommanderIntentResolver.Resolve(input);
            Assert(d.Intent == CommanderIntent.HoldToLast, "Expected HoldToLast, got " + d.Intent);
        }

        private static void TacticalB6aNoPlanFallsBackToMacro()
        {
            var input = new TacticalIntentInput(
                WhiskeyRealism.Strategic.OperationPosture.Inherit,
                hasPlan: false, vanillaMacro: 2, commanderInitiative01: 0.5f,
                oddsConfidence: 0.5f, weakPointConfirmed: false);
            var d = TacticalCommanderIntentResolver.Resolve(input);
            Assert(d.Intent == CommanderIntent.Defend, "Expected Defend from macro 2, got " + d.Intent);
        }

        private static void TacticalB6aMacroRetreatFallsToHoldToLast()
        {
            var input = new TacticalIntentInput(
                WhiskeyRealism.Strategic.OperationPosture.Inherit,
                hasPlan: false, vanillaMacro: 3, commanderInitiative01: 0.5f,
                oddsConfidence: 0.0f, weakPointConfirmed: false);
            var d = TacticalCommanderIntentResolver.Resolve(input);
            Assert(d.Intent == CommanderIntent.HoldToLast, "Expected HoldToLast from macro 3, got " + d.Intent);
        }
```

- [ ] **Step 3: Run all tests**

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: 9 new B6a intent tests PASS. No regressions in existing tests.

- [ ] **Step 4: Commit**

```bash
git add tests/WhiskeyRealism.Tests/Program.cs
git commit -m "$(cat <<'EOF'
test(tactical): cover all B6a OperationPosture branches and macro fallback

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 3: Add `TacticalPlaybookLedger` types and skeleton

**Files:**
- Create: `src/WhiskeyRealism/Tactical/TacticalPlaybookLedger.cs`
- Modify: `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`, `tests/WhiskeyRealism.Tests/Program.cs`

- [ ] **Step 1: Add the Compile Include**

In `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`:

```xml
    <Compile Include="..\..\src\WhiskeyRealism\Tactical\TacticalPlaybookLedger.cs" Link="TacticalPlaybookLedger.cs" />
```

- [ ] **Step 2: Write the failing test**

Add the dispatch entry:

```csharp
            ("tactical b6a probe intent yields probe and fix", TacticalB6aProbeIntentYieldsProbeAndFix),
```

Add the test body:

```csharp
        private static void TacticalB6aProbeIntentYieldsProbeAndFix()
        {
            var sectorL = new TacticalPlaybookSectorView(0, TacticalSectorMission.Hold, TacticalSectorPosition.Left,  ownStrength: 1000f, enemyStrength: 800f, confidence: 0.4f, strongPoint: false, flankRisk: false, ownerSubordinateShare01: 0f);
            var sectorC = new TacticalPlaybookSectorView(1, TacticalSectorMission.Hold, TacticalSectorPosition.Center, ownStrength: 1500f, enemyStrength: 1200f, confidence: 0.4f, strongPoint: false, flankRisk: false, ownerSubordinateShare01: 0f);
            var sectorR = new TacticalPlaybookSectorView(2, TacticalSectorMission.Hold, TacticalSectorPosition.Right, ownStrength: 1000f, enemyStrength: 800f, confidence: 0.4f, strongPoint: false, flankRisk: false, ownerSubordinateShare01: 0f);

            var input = new TacticalPlaybookInput(
                CommanderIntent.ProbeIntent,
                decisiveSectorId: -1,
                sectors: new[] { sectorL, sectorC, sectorR },
                hasReserveAvailable: true,
                anchoredFlankLeft: false, anchoredFlankRight: false,
                stalenessPressure: 0f);

            var decision = TacticalPlaybookLedger.Decide(input);

            Assert(decision.Playbook == TacticalPlaybook.ProbeAndFix, "Expected ProbeAndFix, got " + decision.Playbook);
            Assert(decision.RefusedFlank == TacticalRefusedFlank.None, "Probe with no flank risk must not refuse");
        }
```

- [ ] **Step 3: Run to verify failure**

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: build error - missing `TacticalPlaybookLedger`, `TacticalPlaybookSectorView`, etc.

- [ ] **Step 4: Implement `TacticalPlaybookLedger.cs`**

Create `src/WhiskeyRealism/Tactical/TacticalPlaybookLedger.cs`:

```csharp
using System;
using System.Collections.Generic;

namespace WhiskeyRealism.Tactical
{
    public enum TacticalPlaybook
    {
        HighGroundDefense = 0,
        CombinedArmsDefense = 1,
        RefuseRight = 2,
        RefuseLeft = 3,
        ProbeAndFix = 4,
        WeakPointPressure = 5,
        ReserveHeldCenter = 6,
        LineRelief = 7
    }

    public enum TacticalRefusedFlank
    {
        None = 0,
        Left = 1,
        Right = 2
    }

    public enum TacticalSectorPosition
    {
        Unknown = 0,
        Left = 1,
        Center = 2,
        Right = 3
    }

    public enum TacticalReservePolicy
    {
        HoldReserve = 0,
        PrepareRelief = 1,
        RelieveBatteredLine = 2,
        FlankGuard = 3,
        ExploitWeakPoint = 4
    }

    public enum TacticalLocalReactionPolicy
    {
        Conservative = 0,
        Standard = 1,
        Aggressive = 2
    }

    public readonly struct TacticalPlaybookSectorView
    {
        public TacticalPlaybookSectorView(
            int sectorId,
            TacticalSectorMission mission,
            TacticalSectorPosition position,
            float ownStrength,
            float enemyStrength,
            float confidence,
            bool strongPoint,
            bool flankRisk,
            float ownerSubordinateShare01)
        {
            SectorId = sectorId;
            Mission = mission;
            Position = position;
            OwnStrength = Sanitize(ownStrength);
            EnemyStrength = Sanitize(enemyStrength);
            Confidence = Clamp01(confidence);
            StrongPoint = strongPoint;
            FlankRisk = flankRisk;
            OwnerSubordinateShare01 = Clamp01(ownerSubordinateShare01);
        }

        public int SectorId { get; }
        public TacticalSectorMission Mission { get; }
        public TacticalSectorPosition Position { get; }
        public float OwnStrength { get; }
        public float EnemyStrength { get; }
        public float Confidence { get; }
        public bool StrongPoint { get; }
        public bool FlankRisk { get; }
        public float OwnerSubordinateShare01 { get; }

        private static float Sanitize(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 0f;
            return Math.Max(0f, value);
        }

        private static float Clamp01(float v)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) return 0f;
            if (v < 0f) return 0f;
            if (v > 1f) return 1f;
            return v;
        }
    }

    public readonly struct TacticalPlaybookInput
    {
        public TacticalPlaybookInput(
            CommanderIntent intent,
            int decisiveSectorId,
            TacticalPlaybookSectorView[] sectors,
            bool hasReserveAvailable,
            bool anchoredFlankLeft,
            bool anchoredFlankRight,
            float stalenessPressure)
        {
            Intent = intent;
            DecisiveSectorId = decisiveSectorId;
            Sectors = sectors ?? Array.Empty<TacticalPlaybookSectorView>();
            HasReserveAvailable = hasReserveAvailable;
            AnchoredFlankLeft = anchoredFlankLeft;
            AnchoredFlankRight = anchoredFlankRight;
            StalenessPressure = Math.Max(0f, stalenessPressure);
        }

        public CommanderIntent Intent { get; }
        public int DecisiveSectorId { get; }
        public TacticalPlaybookSectorView[] Sectors { get; }
        public bool HasReserveAvailable { get; }
        public bool AnchoredFlankLeft { get; }
        public bool AnchoredFlankRight { get; }
        public float StalenessPressure { get; }
    }

    public readonly struct TacticalPlaybookDecision
    {
        public TacticalPlaybookDecision(
            TacticalPlaybook playbook,
            int mainEffortSectorId,
            TacticalRefusedFlank refusedFlank,
            int[] probeSectorIds,
            int[] fixSectorIds,
            int[] holdSectorIds,
            TacticalReservePolicy reservePolicy,
            TacticalLocalReactionPolicy localReactionPolicy,
            float confidence,
            string reason)
        {
            Playbook = playbook;
            MainEffortSectorId = mainEffortSectorId;
            RefusedFlank = refusedFlank;
            ProbeSectorIds = probeSectorIds ?? Array.Empty<int>();
            FixSectorIds = fixSectorIds ?? Array.Empty<int>();
            HoldSectorIds = holdSectorIds ?? Array.Empty<int>();
            ReservePolicy = reservePolicy;
            LocalReactionPolicy = localReactionPolicy;
            Confidence = Clamp01(confidence);
            Reason = string.IsNullOrEmpty(reason) ? "unknown" : reason;
        }

        public TacticalPlaybook Playbook { get; }
        public int MainEffortSectorId { get; }
        public TacticalRefusedFlank RefusedFlank { get; }
        public int[] ProbeSectorIds { get; }
        public int[] FixSectorIds { get; }
        public int[] HoldSectorIds { get; }
        public TacticalReservePolicy ReservePolicy { get; }
        public TacticalLocalReactionPolicy LocalReactionPolicy { get; }
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

    public static class TacticalPlaybookLedger
    {
        public static TacticalPlaybookDecision Decide(TacticalPlaybookInput input)
        {
            if (input.Sectors.Length == 0)
                return Empty(input.Intent, "no-sectors");

            var refused = ChooseRefusedFlank(input);
            int main = ChooseMainEffort(input, refused);

            switch (input.Intent)
            {
                case CommanderIntent.ProbeIntent:
                    return BuildProbeAndFix(input, refused);
                case CommanderIntent.HoldToLast:
                    return BuildLineHold(input, refused, TacticalReservePolicy.HoldReserve, "hold-to-last");
                case CommanderIntent.Hold:
                    return BuildLineHold(input, refused, TacticalReservePolicy.HoldReserve, "hold");
                case CommanderIntent.Defend:
                    return BuildDefense(input, refused, main);
                case CommanderIntent.Attack:
                case CommanderIntent.AllOutAttack:
                    return BuildAttack(input, refused, main);
                default:
                    return BuildLineHold(input, refused, TacticalReservePolicy.HoldReserve, "default-hold");
            }
        }

        private static TacticalRefusedFlank ChooseRefusedFlank(TacticalPlaybookInput input)
        {
            TacticalPlaybookSectorView left = default, right = default;
            bool foundL = false, foundR = false;
            for (int i = 0; i < input.Sectors.Length; i++)
            {
                if (input.Sectors[i].Position == TacticalSectorPosition.Left) { left = input.Sectors[i]; foundL = true; }
                if (input.Sectors[i].Position == TacticalSectorPosition.Right) { right = input.Sectors[i]; foundR = true; }
            }

            float leftRisk = foundL && left.FlankRisk && !input.AnchoredFlankLeft ? 1f : 0f;
            float rightRisk = foundR && right.FlankRisk && !input.AnchoredFlankRight ? 1f : 0f;

            if (leftRisk > 0f && leftRisk >= rightRisk) return TacticalRefusedFlank.Left;
            if (rightRisk > 0f) return TacticalRefusedFlank.Right;
            return TacticalRefusedFlank.None;
        }

        private static int ChooseMainEffort(TacticalPlaybookInput input, TacticalRefusedFlank refused)
        {
            if (input.DecisiveSectorId < 0) return -1;

            for (int i = 0; i < input.Sectors.Length; i++)
            {
                var s = input.Sectors[i];
                if (s.SectorId != input.DecisiveSectorId) continue;
                if (s.OwnerSubordinateShare01 > 0.5f) return -1;
                if (refused == TacticalRefusedFlank.Left && s.Position == TacticalSectorPosition.Left) return -1;
                if (refused == TacticalRefusedFlank.Right && s.Position == TacticalSectorPosition.Right) return -1;
                return s.SectorId;
            }
            return -1;
        }

        private static TacticalPlaybookDecision BuildProbeAndFix(TacticalPlaybookInput input, TacticalRefusedFlank refused)
        {
            var probe = new List<int>();
            var hold = new List<int>();
            for (int i = 0; i < input.Sectors.Length; i++)
            {
                var s = input.Sectors[i];
                if (s.Confidence < 0.55f) probe.Add(s.SectorId);
                else hold.Add(s.SectorId);
            }
            return new TacticalPlaybookDecision(
                TacticalPlaybook.ProbeAndFix,
                mainEffortSectorId: -1,
                refusedFlank: refused,
                probeSectorIds: probe.ToArray(),
                fixSectorIds: Array.Empty<int>(),
                holdSectorIds: hold.ToArray(),
                reservePolicy: TacticalReservePolicy.HoldReserve,
                localReactionPolicy: TacticalLocalReactionPolicy.Conservative,
                confidence: 0.6f,
                reason: "probe-intent");
        }

        private static TacticalPlaybookDecision BuildLineHold(TacticalPlaybookInput input, TacticalRefusedFlank refused, TacticalReservePolicy policy, string reason)
        {
            var hold = new List<int>();
            for (int i = 0; i < input.Sectors.Length; i++)
                hold.Add(input.Sectors[i].SectorId);
            return new TacticalPlaybookDecision(
                TacticalPlaybook.HighGroundDefense,
                mainEffortSectorId: -1,
                refusedFlank: refused,
                probeSectorIds: Array.Empty<int>(),
                fixSectorIds: Array.Empty<int>(),
                holdSectorIds: hold.ToArray(),
                reservePolicy: policy,
                localReactionPolicy: TacticalLocalReactionPolicy.Conservative,
                confidence: 0.65f,
                reason: reason);
        }

        private static TacticalPlaybookDecision BuildDefense(TacticalPlaybookInput input, TacticalRefusedFlank refused, int mainEffort)
        {
            var fix = new List<int>();
            var hold = new List<int>();
            for (int i = 0; i < input.Sectors.Length; i++)
            {
                var s = input.Sectors[i];
                if (s.SectorId == mainEffort) fix.Add(s.SectorId);
                else hold.Add(s.SectorId);
            }
            return new TacticalPlaybookDecision(
                refused == TacticalRefusedFlank.None ? TacticalPlaybook.CombinedArmsDefense
                    : (refused == TacticalRefusedFlank.Right ? TacticalPlaybook.RefuseRight : TacticalPlaybook.RefuseLeft),
                mainEffortSectorId: mainEffort,
                refusedFlank: refused,
                probeSectorIds: Array.Empty<int>(),
                fixSectorIds: fix.ToArray(),
                holdSectorIds: hold.ToArray(),
                reservePolicy: input.HasReserveAvailable ? TacticalReservePolicy.FlankGuard : TacticalReservePolicy.HoldReserve,
                localReactionPolicy: TacticalLocalReactionPolicy.Standard,
                confidence: 0.7f,
                reason: "defend");
        }

        private static TacticalPlaybookDecision BuildAttack(TacticalPlaybookInput input, TacticalRefusedFlank refused, int mainEffort)
        {
            if (mainEffort < 0)
                return BuildProbeAndFix(input, refused);

            var fix = new List<int>();
            var hold = new List<int>();
            int main = mainEffort;
            for (int i = 0; i < input.Sectors.Length; i++)
            {
                var s = input.Sectors[i];
                if (s.SectorId == main) continue;
                if (s.Confidence >= 0.55f && !s.StrongPoint) fix.Add(s.SectorId);
                else hold.Add(s.SectorId);
            }
            return new TacticalPlaybookDecision(
                TacticalPlaybook.WeakPointPressure,
                mainEffortSectorId: main,
                refusedFlank: refused,
                probeSectorIds: Array.Empty<int>(),
                fixSectorIds: fix.ToArray(),
                holdSectorIds: hold.ToArray(),
                reservePolicy: input.HasReserveAvailable ? TacticalReservePolicy.ExploitWeakPoint : TacticalReservePolicy.HoldReserve,
                localReactionPolicy: TacticalLocalReactionPolicy.Aggressive,
                confidence: 0.7f,
                reason: input.Intent == CommanderIntent.AllOutAttack ? "all-out-attack" : "attack");
        }

        private static TacticalPlaybookDecision Empty(CommanderIntent intent, string reason)
        {
            return new TacticalPlaybookDecision(
                TacticalPlaybook.HighGroundDefense,
                mainEffortSectorId: -1,
                refusedFlank: TacticalRefusedFlank.None,
                probeSectorIds: Array.Empty<int>(),
                fixSectorIds: Array.Empty<int>(),
                holdSectorIds: Array.Empty<int>(),
                reservePolicy: TacticalReservePolicy.HoldReserve,
                localReactionPolicy: TacticalLocalReactionPolicy.Conservative,
                confidence: 0f,
                reason: reason);
        }
    }
}
```

- [ ] **Step 5: Run test, verify pass**

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/WhiskeyRealism/Tactical/TacticalPlaybookLedger.cs tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj tests/WhiskeyRealism.Tests/Program.cs
git commit -m "$(cat <<'EOF'
feat(tactical): add B6a TacticalPlaybookLedger skeleton

Pure ledger that picks one playbook per side from intent + sector views,
including refused-flank and main-effort selection. No vanilla writes.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 4: Cover playbook flank/decisive/ownership branches

**Files:**
- Modify: `tests/WhiskeyRealism.Tests/Program.cs`

- [ ] **Step 1: Add eight dispatch entries**

```csharp
            ("tactical b6a defend with right flank risk yields refuse right", TacticalB6aDefendRightFlankYieldsRefuseRight),
            ("tactical b6a defend with left flank risk yields refuse left", TacticalB6aDefendLeftFlankYieldsRefuseLeft),
            ("tactical b6a defend with anchored flank does not refuse", TacticalB6aDefendAnchoredFlankDoesNotRefuse),
            ("tactical b6a attack with decisive sector yields weak point pressure", TacticalB6aAttackDecisiveYieldsWeakPointPressure),
            ("tactical b6a attack without decisive sector falls back to probe and fix", TacticalB6aAttackNoDecisiveFallsBack),
            ("tactical b6a main effort rejected when subordinate share over half", TacticalB6aMainEffortRejectedOnPlayerOwnership),
            ("tactical b6a hold to last yields high ground defense", TacticalB6aHoldToLastYieldsHighGroundDefense),
            ("tactical b6a empty sectors yields no-sectors decision", TacticalB6aEmptySectorsYieldsEmpty),
```

- [ ] **Step 2: Add a test helper plus test bodies**

```csharp
        private static TacticalPlaybookSectorView Sector(int id, TacticalSectorPosition pos, float own, float enemy, float conf, bool flankRisk = false, bool strongPoint = false, float share = 0f, TacticalSectorMission mission = TacticalSectorMission.Hold)
        {
            return new TacticalPlaybookSectorView(id, mission, pos, own, enemy, conf, strongPoint, flankRisk, share);
        }

        private static void TacticalB6aDefendRightFlankYieldsRefuseRight()
        {
            var sectors = new[]
            {
                Sector(0, TacticalSectorPosition.Left,   1000, 800, 0.7f),
                Sector(1, TacticalSectorPosition.Center, 1500, 1200, 0.7f),
                Sector(2, TacticalSectorPosition.Right,  900,  1500, 0.7f, flankRisk: true),
            };
            var input = new TacticalPlaybookInput(CommanderIntent.Defend, 1, sectors, true, false, false, 0f);
            var d = TacticalPlaybookLedger.Decide(input);
            Assert(d.Playbook == TacticalPlaybook.RefuseRight, "Expected RefuseRight, got " + d.Playbook);
            Assert(d.RefusedFlank == TacticalRefusedFlank.Right, "Refused flank mismatch");
        }

        private static void TacticalB6aDefendLeftFlankYieldsRefuseLeft()
        {
            var sectors = new[]
            {
                Sector(0, TacticalSectorPosition.Left,   900,  1500, 0.7f, flankRisk: true),
                Sector(1, TacticalSectorPosition.Center, 1500, 1200, 0.7f),
                Sector(2, TacticalSectorPosition.Right,  1000, 800,  0.7f),
            };
            var input = new TacticalPlaybookInput(CommanderIntent.Defend, 1, sectors, true, false, false, 0f);
            var d = TacticalPlaybookLedger.Decide(input);
            Assert(d.Playbook == TacticalPlaybook.RefuseLeft, "Expected RefuseLeft, got " + d.Playbook);
        }

        private static void TacticalB6aDefendAnchoredFlankDoesNotRefuse()
        {
            var sectors = new[]
            {
                Sector(0, TacticalSectorPosition.Left,   1000, 800, 0.7f),
                Sector(1, TacticalSectorPosition.Center, 1500, 1200, 0.7f),
                Sector(2, TacticalSectorPosition.Right,  900,  1500, 0.7f, flankRisk: true),
            };
            var input = new TacticalPlaybookInput(CommanderIntent.Defend, 1, sectors, true, false, true, 0f);
            var d = TacticalPlaybookLedger.Decide(input);
            Assert(d.RefusedFlank == TacticalRefusedFlank.None, "Anchored right flank must not be refused");
            Assert(d.Playbook == TacticalPlaybook.CombinedArmsDefense, "Expected CombinedArmsDefense, got " + d.Playbook);
        }

        private static void TacticalB6aAttackDecisiveYieldsWeakPointPressure()
        {
            var sectors = new[]
            {
                Sector(0, TacticalSectorPosition.Left,   1000, 800, 0.7f),
                Sector(1, TacticalSectorPosition.Center, 1500, 800, 0.8f, mission: TacticalSectorMission.AttackWeakPoint),
                Sector(2, TacticalSectorPosition.Right,  1000, 800, 0.7f),
            };
            var input = new TacticalPlaybookInput(CommanderIntent.Attack, 1, sectors, true, false, false, 0f);
            var d = TacticalPlaybookLedger.Decide(input);
            Assert(d.Playbook == TacticalPlaybook.WeakPointPressure, "Expected WeakPointPressure, got " + d.Playbook);
            Assert(d.MainEffortSectorId == 1, "Main effort must be sector 1");
        }

        private static void TacticalB6aAttackNoDecisiveFallsBack()
        {
            var sectors = new[]
            {
                Sector(0, TacticalSectorPosition.Left,   1000, 800, 0.4f),
                Sector(1, TacticalSectorPosition.Center, 1500, 1200, 0.4f),
                Sector(2, TacticalSectorPosition.Right,  1000, 800, 0.4f),
            };
            var input = new TacticalPlaybookInput(CommanderIntent.Attack, -1, sectors, true, false, false, 0f);
            var d = TacticalPlaybookLedger.Decide(input);
            Assert(d.Playbook == TacticalPlaybook.ProbeAndFix, "Expected ProbeAndFix fallback, got " + d.Playbook);
        }

        private static void TacticalB6aMainEffortRejectedOnPlayerOwnership()
        {
            var sectors = new[]
            {
                Sector(0, TacticalSectorPosition.Left,   1000, 800, 0.7f),
                Sector(1, TacticalSectorPosition.Center, 1500, 800, 0.8f, share: 0.6f, mission: TacticalSectorMission.AttackWeakPoint),
                Sector(2, TacticalSectorPosition.Right,  1000, 800, 0.7f),
            };
            var input = new TacticalPlaybookInput(CommanderIntent.Attack, 1, sectors, true, false, false, 0f);
            var d = TacticalPlaybookLedger.Decide(input);
            Assert(d.MainEffortSectorId == -1, "Main effort must be rejected when subordinate share > 0.5");
            Assert(d.Playbook == TacticalPlaybook.ProbeAndFix, "Expected ProbeAndFix fallback when main effort denied");
        }

        private static void TacticalB6aHoldToLastYieldsHighGroundDefense()
        {
            var sectors = new[]
            {
                Sector(0, TacticalSectorPosition.Left,   1000, 800, 0.7f),
                Sector(1, TacticalSectorPosition.Center, 1500, 1200, 0.7f),
                Sector(2, TacticalSectorPosition.Right,  1000, 800, 0.7f),
            };
            var input = new TacticalPlaybookInput(CommanderIntent.HoldToLast, -1, sectors, false, false, false, 0f);
            var d = TacticalPlaybookLedger.Decide(input);
            Assert(d.Playbook == TacticalPlaybook.HighGroundDefense, "Expected HighGroundDefense, got " + d.Playbook);
            Assert(d.ReservePolicy == TacticalReservePolicy.HoldReserve, "HoldToLast must keep reserve");
        }

        private static void TacticalB6aEmptySectorsYieldsEmpty()
        {
            var input = new TacticalPlaybookInput(CommanderIntent.Attack, -1, System.Array.Empty<TacticalPlaybookSectorView>(), false, false, false, 0f);
            var d = TacticalPlaybookLedger.Decide(input);
            Assert(d.Reason == "no-sectors", "Expected no-sectors reason, got " + d.Reason);
            Assert(d.Confidence == 0f, "Empty decision must have zero confidence");
        }
```

- [ ] **Step 3: Run tests**

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: 8 new B6a playbook tests PASS.

- [ ] **Step 4: Commit**

```bash
git add tests/WhiskeyRealism.Tests/Program.cs
git commit -m "$(cat <<'EOF'
test(tactical): cover B6a playbook flank/decisive/ownership branches

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 5: Bind `Enable Tactical Commander Intent Doctrine` config

**Files:**
- Modify: `src/WhiskeyRealism/Plugin.cs`

- [ ] **Step 1: Add the field declaration**

In the existing tactical block of field declarations (after `EnableTacticalGroupSectorStance` near line 38), add:

```csharp
        internal ConfigEntry<bool> EnableTacticalCommanderIntentDoctrine;
```

- [ ] **Step 2: Bind the config**

In the tactical config-bind block (after the `EnableTacticalGroupSectorStance = Config.Bind(...)` block), add:

```csharp
            EnableTacticalCommanderIntentDoctrine = Config.Bind(
                "Tactical",
                "Enable Tactical Commander Intent Doctrine",
                false,
                "Default OFF for Slice B6a. Computes tactical commander intent and playbook from B3-B5 evidence and the active OperationPosture, and emits read-only [TacticalIntent] and [TacticalPlaybook] telemetry. Does not change any vanilla battle state.");
```

- [ ] **Step 3: Build**

```bash
./build.sh
```

Expected: BUILD SUCCEEDED with 0 warnings, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add src/WhiskeyRealism/Plugin.cs
git commit -m "$(cat <<'EOF'
feat(tactical): bind Enable Tactical Commander Intent Doctrine config

Default OFF. Gates the B6a observer's intent and playbook telemetry.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 6: Add `BattleCommanderIntentObserverPatch` (telemetry only)

**Files:**
- Create: `src/WhiskeyRealism/Patches/BattleCommanderIntentObserverPatch.cs`

- [ ] **Step 1: Create the patch file**

```csharp
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // B6a telemetry-only observer. Runs as Postfix on AIBattle.AdjustGroupAIStance
    // (decompile 4221), reads vanilla side/macro/objective-chain context, feeds
    // TacticalCommanderIntentResolver and TacticalPlaybookLedger, and emits
    // bounded [TacticalIntent] and [TacticalPlaybook] log lines. Never writes
    // vanilla battle state.
    [HarmonyPatch(typeof(AIBattle), "AdjustGroupAIStance")]
    internal static class BattleCommanderIntentObserverPatch
    {
        private static readonly Dictionary<string, float> _lastEmittedAt = new Dictionary<string, float>();
        private static FieldInfo _macroAiField;
        private static FieldInfo _sideOfAiField;
        private static FieldInfo _objectiveChainField;
        private static FieldInfo _chainCenterField;
        private static FieldInfo _flankAnchoredField;
        private static FieldInfo _reserveGroupsField;

        [HarmonyPostfix]
        [HarmonyPriority(Priority.LowerThanNormal)]
        internal static void Postfix(AIBattle __instance)
        {
            if (!Enabled() || __instance == null) return;

            try
            {
                Apply(__instance);
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-b6a-observer:failed", "BattleCommanderIntentObserverPatch failed: " + ex.Message);
            }
        }

        private static void Apply(AIBattle battle)
        {
            int side = SafeIntField(battle, ref _sideOfAiField, "sideofai", -1);
            int macro = SafeIntField(battle, ref _macroAiField, "macroai", -99);
            if (side < 0) return;

            var intentInput = BuildIntentInput(macro);
            var intent = TacticalCommanderIntentResolver.Resolve(intentInput);

            var sectors = BuildPlaybookSectors(battle);
            var playbookInput = new TacticalPlaybookInput(
                intent.Intent,
                decisiveSectorId: ChooseDecisiveSector(sectors),
                sectors: sectors,
                hasReserveAvailable: HasReserveAvailable(battle),
                anchoredFlankLeft: AnchoredFlank(battle, 0),
                anchoredFlankRight: AnchoredFlank(battle, 1),
                stalenessPressure: 0f);
            var playbook = TacticalPlaybookLedger.Decide(playbookInput);

            EmitIntent(side, macro, intentInput, intent);
            EmitPlaybook(side, playbook);
        }

        private static TacticalIntentInput BuildIntentInput(int macro)
        {
            // OperationPosture lookup is wired in B6c when strategic-side state
            // becomes available per-battle. B6a treats every battle as no-plan
            // and falls back to the vanilla macro mapping.
            return new TacticalIntentInput(
                operationPosture: OperationPosture.Inherit,
                hasPlan: false,
                vanillaMacro: macro,
                commanderInitiative01: 0.5f,
                oddsConfidence: 0.5f,
                weakPointConfirmed: false);
        }

        private static TacticalPlaybookSectorView[] BuildPlaybookSectors(AIBattle battle)
        {
            IList chain = ObjectiveChain(battle);
            if (chain == null || chain.Count == 0)
                return Array.Empty<TacticalPlaybookSectorView>();

            var list = new List<TacticalPlaybookSectorView>();
            for (int i = 0; i < chain.Count; i++)
            {
                object entry = chain[i];
                Regiment center = SafeRegimentField(entry, ref _chainCenterField, "linegroup_centerunit");
                if (center == null) continue;

                float own = Math.Max(0f, center.groupowninrange);
                float enemy = Math.Max(0f, center.groupenemiesinrange);
                bool flank = center.flanksthreated > 0f || center.outflanked > 0;
                bool strong = center.covervalue > 0.5f || center.fortinrange;
                float share = AttachedSubordinateShare(center);

                list.Add(new TacticalPlaybookSectorView(
                    sectorId: i,
                    mission: TacticalSectorMission.Hold,
                    position: i == 0 ? TacticalSectorPosition.Left :
                              i == chain.Count - 1 ? TacticalSectorPosition.Right :
                              TacticalSectorPosition.Center,
                    ownStrength: own,
                    enemyStrength: enemy,
                    confidence: enemy > 0f ? 0.6f : 0.3f,
                    strongPoint: strong,
                    flankRisk: flank,
                    ownerSubordinateShare01: share));
            }
            return list.ToArray();
        }

        private static int ChooseDecisiveSector(TacticalPlaybookSectorView[] sectors)
        {
            int best = -1;
            float bestScore = 0f;
            for (int i = 0; i < sectors.Length; i++)
            {
                if (sectors[i].EnemyStrength <= 0f) continue;
                float odds = sectors[i].OwnStrength / Math.Max(1f, sectors[i].EnemyStrength);
                float score = odds * sectors[i].Confidence;
                if (sectors[i].StrongPoint) score *= 0.65f;
                if (sectors[i].FlankRisk) score *= 0.55f;
                if (score > bestScore && sectors[i].Confidence >= 0.55f)
                {
                    bestScore = score;
                    best = sectors[i].SectorId;
                }
            }
            return best;
        }

        private static bool HasReserveAvailable(AIBattle battle)
        {
            IList chain = ObjectiveChain(battle);
            if (chain == null) return false;
            for (int i = 0; i < chain.Count; i++)
            {
                if (_reserveGroupsField == null) _reserveGroupsField = AccessTools.Field(chain[i].GetType(), "reservegroups");
                if (_reserveGroupsField == null) continue;
                if (_reserveGroupsField.GetValue(chain[i]) is IList reserves && reserves.Count > 0) return true;
            }
            return false;
        }

        private static bool AnchoredFlank(AIBattle battle, int index)
        {
            IList chain = ObjectiveChain(battle);
            if (chain == null || chain.Count == 0) return false;
            object entry = chain[0];
            if (_flankAnchoredField == null) _flankAnchoredField = AccessTools.Field(entry.GetType(), "anchoredflank");
            if (_flankAnchoredField == null) return false;
            if (_flankAnchoredField.GetValue(entry) is bool[] anchored && anchored.Length > index) return anchored[index];
            return false;
        }

        private static float AttachedSubordinateShare(Regiment center)
        {
            if (center == null || center.allattachedunits == null) return 0f;
            int total = 0, sub = 0;
            for (int i = 0; i < center.allattachedunits.Length; i++)
            {
                var u = center.allattachedunits[i];
                if (u == null) continue;
                total++;
                if (u.dlcw_isundercommander) sub++;
            }
            return total > 0 ? (float)sub / total : 0f;
        }

        private static void EmitIntent(int side, int macro, TacticalIntentInput input, TacticalIntentDecision intent)
        {
            string signature = side + "|" + macro + "|" + intent.Intent + "|" + intent.Reason;
            if (!TacticalTelemetry.ShouldEmit(_lastEmittedAt, "b6a-intent", signature, Time.realtimeSinceStartup, 30f, false))
                return;

            Plugin.Log.LogInfo("[TacticalIntent] side=" + side +
                " intent=" + intent.Intent +
                " posture=" + input.OperationPosture +
                " commanderInit=" + input.CommanderInitiative01.ToString("0.00") +
                " macro=" + macro +
                " reason=" + intent.Reason +
                " confidence=" + input.OddsConfidence.ToString("0.00"));
        }

        private static void EmitPlaybook(int side, TacticalPlaybookDecision decision)
        {
            string signature = side + "|" + decision.Playbook + "|" + decision.MainEffortSectorId + "|" + decision.RefusedFlank + "|" + decision.Reason;
            if (!TacticalTelemetry.ShouldEmit(_lastEmittedAt, "b6a-playbook", signature, Time.realtimeSinceStartup, 30f, false))
                return;

            Plugin.Log.LogInfo("[TacticalPlaybook] side=" + side +
                " playbook=" + decision.Playbook +
                " main=" + decision.MainEffortSectorId +
                " refuse=" + decision.RefusedFlank +
                " probe=" + Join(decision.ProbeSectorIds) +
                " fix=" + Join(decision.FixSectorIds) +
                " hold=" + Join(decision.HoldSectorIds) +
                " reserve=" + decision.ReservePolicy +
                " reason=" + decision.Reason);
        }

        private static string Join(int[] values)
        {
            if (values == null || values.Length == 0) return "-";
            return string.Join(",", values);
        }

        private static IList ObjectiveChain(AIBattle battle)
        {
            if (_objectiveChainField == null) _objectiveChainField = AccessTools.Field(typeof(AIBattle), "objective" + "chain");
            return _objectiveChainField?.GetValue(battle) as IList;
        }

        private static Regiment SafeRegimentField(object instance, ref FieldInfo cache, string name)
        {
            try
            {
                if (instance == null) return null;
                if (cache == null) cache = AccessTools.Field(instance.GetType(), name);
                return cache != null ? cache.GetValue(instance) as Regiment : null;
            }
            catch
            {
                return null;
            }
        }

        private static int SafeIntField(object instance, ref FieldInfo cache, string name, int fallback)
        {
            try
            {
                if (instance == null) return fallback;
                if (cache == null) cache = AccessTools.Field(instance.GetType(), name);
                if (cache == null) return fallback;
                return Convert.ToInt32(cache.GetValue(instance));
            }
            catch
            {
                return fallback;
            }
        }

        private static bool Enabled()
        {
            return Plugin.Instance != null &&
                Plugin.Instance.Enabled.Value &&
                Plugin.Instance.EnableTacticalObserver.Value &&
                Plugin.Instance.EnableTacticalCommanderIntentDoctrine.Value;
        }
    }
}
```

- [ ] **Step 2: Build**

```bash
./build.sh
```

Expected: BUILD SUCCEEDED.

- [ ] **Step 3: Deploy and verify hash**

```bash
cp dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/"
sha256sum dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/WhiskeyRealism.dll"
```

Expected: both SHA-256 hashes match. Record the hash for the catalog and handoff updates.

- [ ] **Step 4: Commit**

```bash
git add src/WhiskeyRealism/Patches/BattleCommanderIntentObserverPatch.cs
git commit -m "$(cat <<'EOF'
feat(tactical): add B6a BattleCommanderIntentObserverPatch

Default-off Postfix on AIBattle.AdjustGroupAIStance that emits bounded
[TacticalIntent] and [TacticalPlaybook] telemetry derived from
TacticalCommanderIntentResolver and TacticalPlaybookLedger. Does not
change any vanilla battle state.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 7: Update patch catalog and handoff

**Files:**
- Modify: `docs/patch-catalog.md`, `docs/handoff.md`

- [ ] **Step 1: Pick the next free ordinal**

```bash
grep -E '^\| [0-9]+' docs/patch-catalog.md | tail -3
```

Use the next sequential ordinal not already in the table (likely `47` after `46`).

- [ ] **Step 2: Add catalog row**

Insert into `docs/patch-catalog.md` in numeric order (replace `<sha>` with the actual hash from Task 6):

```markdown
| 47 | `BattleCommanderIntentObserverPatch` | Postfix | `Patches/BattleCommanderIntentObserverPatch.cs` | `AIBattle.AdjustGroupAIStance` (4221) | Slice B6a default-off telemetry observer. Runs after vanilla group stance, derives commander intent (`TacticalCommanderIntentResolver`) and playbook (`TacticalPlaybookLedger`) from vanilla `macroai`, `objectivechain[i].linegroup_centerunit`, and per-side reserve/anchored-flank evidence, then emits bounded `[TacticalIntent]` and `[TacticalPlaybook]` lines under `Enable Tactical Commander Intent Doctrine`. Does not change `macroai`, `ai_stance`, movement orders, reserve lists, artillery behavior, fallback/retreat state, order queues, path status, or persistence. Build/deploy/hash verified in DLL `<sha>`; runtime telemetry smoke is pending. |
```

- [ ] **Step 3: Update handoff**

Append a "What just shipped" bullet (replace `<sha>` with the actual hash):

```markdown
- **B6a tactical commander intent doctrine (telemetry-only):** added `Tactical/TacticalCommanderIntent.cs`, `Tactical/TacticalPlaybookLedger.cs`, and `Patches/BattleCommanderIntentObserverPatch.cs` (#47). Default-off behind `Enable Tactical Commander Intent Doctrine`. No vanilla writes. Build/deploy/hash verified in DLL `<sha>`; in-game `[TacticalIntent]`/`[TacticalPlaybook]` smoke pending. Console harness covers nine intent translations and eight playbook branches.
```

- [ ] **Step 4: Commit docs**

```bash
git add docs/patch-catalog.md docs/handoff.md
git commit -m "$(cat <<'EOF'
docs(tactical): catalog #47 B6a commander intent observer

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 8: Smoke verify telemetry

**Files:** none modified (smoke is in-game).

- [ ] **Step 1: Enable observer + B6a in config**

User edits `<GTCW>/BepInEx/config/dev.kyle.whiskey-realism.cfg`, set:

```ini
[Tactical]
Enable Tactical Observer = true
Enable Tactical Commander Intent Doctrine = true
```

- [ ] **Step 2: Launch GTCW, start a W&L land battle**

Tail the log:

```bash
tail -f "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/LogOutput.log" | grep -E "TacticalIntent|TacticalPlaybook|tactical-b6a-observer"
```

- [ ] **Step 3: Verify smoke markers**

Expected within 1-2 minutes of land-battle play:
- At least one `[TacticalIntent] side=...` line.
- At least one `[TacticalPlaybook] side=...` line.
- No `[once:tactical-b6a-observer:failed]` warnings.
- No repeated exception spam.

If a `tactical-b6a-observer:failed` warning appears, capture the exception message and fix the patch file before declaring success.

- [ ] **Step 4: Update handoff with smoke result**

Replace "in-game `[TacticalIntent]`/`[TacticalPlaybook]` smoke pending" in handoff with observed counts and timestamp:

```bash
git add docs/handoff.md docs/patch-catalog.md
git commit -m "$(cat <<'EOF'
chore(tactical): record B6a runtime smoke evidence

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Rollback

If B6a regresses:

- Disable `Enable Tactical Commander Intent Doctrine` in config — observer becomes inert.
- If the observer Postfix itself misfires, revert `Patches/BattleCommanderIntentObserverPatch.cs` only; the pure `Tactical/TacticalCommanderIntent.cs` and `Tactical/TacticalPlaybookLedger.cs` types stay (they are consumed by B6b/B6c).
- If pure tests regress on a future change, revert the offending edit and re-run `dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj` until green.

## Smoke Expectations

`[TacticalIntent]` and `[TacticalPlaybook]` lines fire signature-gated. No `[TacticalLocalReaction]`, `[TacticalReserveIntent]`, or `[TacticalChargeDeny]` lines are required for B6a — those land in B6b/B6c.
