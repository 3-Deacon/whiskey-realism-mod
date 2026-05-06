# W&L Dispatch Objective Bridge Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the W&L campaign-map `"none"` dispatch bug first, then route Whiskey strategic orders through vanilla W&L current-order semantics for eligible player-chain commands without direct-moving player-controlled commands.

**Architecture:** Ship this in checkpoints. C0a adds a narrow, tested dispatch-text sanitizer around vanilla `Messages.Message.GenerateMessageContent()`. C0b adds a pure `WlStrategicOrderBridge` classifier plus a live adapter. C0c converts strategic movement call sites one at a time so non-W&L and opposing-AI behavior stays vanilla, while W&L player-chain units receive vanilla current orders or are skipped.

**Tech Stack:** BepInEx 5.4.x, HarmonyX Postfix patches, C# netstandard2.1 plugin, net8.0 console harness with explicit `<Compile Include>` entries, vanilla anchors from `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs`.

---

## Scope And Checkpoints

This plan implements the active spec `docs/superpowers/specs/2026-05-06-wl-dispatch-objective-bridge-design.md`.

Checkpoint C0a is independently shippable and fixes the user's observed symptom. Do not batch C0a with bridge conversion unless the user explicitly accepts the larger risk.

Checkpoint C0b adds pure bridge logic and tests but does not change runtime movement.

Checkpoint C0c converts runtime call sites after C0a has runtime smoke. Convert one caller per commit.

C0d richer popup copy is out of scope for this plan because vanilla opens `CareerInformationPanel.ShowNewOrder(...)` inside `AIBattle.CheckCurrentOrderUpdate(...)`; a Postfix would mutate `DLC_WL.givenorder` after the already-open popup is built.

## Vanilla Anchors To Recheck Before Coding

- `Messages.Message.GenerateMessageContent()` owner/signature: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:195447`.
- Type 56 `"to none"` sentence: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:196056-196075`.
- Type 57 same stance sentence: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:196100-196106`.
- W&L message filter: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:196752-196786`.
- `AIBattle.CheckCurrentOrderUpdate(...)` owner/signature and `calledfromcampaign` guard: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:8233-8368`.
- Accepted current-order write: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:8648-8664`.
- Player appointment marks new command `dlcw_isundercommander`: `/tmp/gt_src/asm/Assembly-CSharp.decompiled.cs:44261-44270`.
- Vanilla W&L campaign order call sites: fort `9674`, capital `11864`, engage `13715`, continuation `14052`, redeploy/offensive `14451/14455`, supply `14775`.

## File Structure

Create:

- `src/WhiskeyRealism/Strategic/WlDispatchSanitizer.cs` - pure string sanitizer for known W&L stance-0 message text.
- `src/WhiskeyRealism/Patches/DispatchStanceSanitizerPatch.cs` - Harmony Postfix on `Messages.Message.GenerateMessageContent()`.
- `src/WhiskeyRealism/Strategic/WlStrategicOrderBridge.cs` - pure role classifier plus live W&L adapter.

Modify:

- `tests/WhiskeyRealism.Tests/Program.cs` - add focused sanitizer and bridge-classifier tests.
- `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj` - add explicit compile entries for new pure strategic files.
- `src/WhiskeyRealism/Strategic/OperationalProbeRuntime.cs` - C0c bridge conversion for `Probe`, `Offensive`, and `OffensiveContinuation`.
- `src/WhiskeyRealism/Strategic/ArmyAreaRuntime.cs` - C0c bridge conversion for `Redeploy` before `theaterposition` writes.
- `src/WhiskeyRealism/Strategic/CoastalDefenseCustomOrderRunner.cs` - C0c bridge conversion for `EngageEnemy`.
- `src/WhiskeyRealism/Patches/CheckForDefensiveOperationsCandidateFilterPatch.cs` - audit revert movement for W&L player-chain units; do not fold into the first C0c conversion commit.
- `docs/patch-catalog.md` - add #36 for the sanitizer after implementation ships; add a helper/runtime row for the bridge only after a runtime caller uses it.
- `docs/handoff.md` - record active workstream at implementation start and smoke status after deploy.

## Task 1: C0a Pure Dispatch Sanitizer

**Files:**
- Create: `src/WhiskeyRealism/Strategic/WlDispatchSanitizer.cs`
- Modify: `tests/WhiskeyRealism.Tests/Program.cs`
- Modify: `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`

- [ ] **Step 1: Add failing sanitizer tests**

In `tests/WhiskeyRealism.Tests/Program.cs`, add these test registrations near the existing W&L tests:

```csharp
("wl dispatch sanitizer fixes type 56 stance none", WlDispatchSanitizerFixesType56StanceNone),
("wl dispatch sanitizer fixes type 57 stance none", WlDispatchSanitizerFixesType57StanceNone),
("wl dispatch sanitizer leaves normal content unchanged", WlDispatchSanitizerLeavesNormalContentUnchanged),
```

Add these test methods before `HistoricalHardDifficultyAddsCasualtyToleranceOnly()`:

```csharp
private static void WlDispatchSanitizerFixesType56StanceNone()
{
    string input = "\t\tMy division has reached the given objective.\n\nI will carry on according to your instructions that are to none. ";
    var result = WlDispatchSanitizer.Sanitize(56, input);

    AssertEqual(true, result.Changed);
    AssertContains(result.Content, "I will hold position and await further instructions.", "replacement text");
    AssertTrue(!result.Content.Contains("to none"), "sanitized content should not contain stance none");
}

private static void WlDispatchSanitizerFixesType57StanceNone()
{
    string input = "\t\tOur corps has withdrawn in face of the enemy.\n\nI will carry on according to your instructions that are to none. ";
    var result = WlDispatchSanitizer.Sanitize(57, input);

    AssertEqual(true, result.Changed);
    AssertContains(result.Content, "I will hold position and await further instructions.", "replacement text");
    AssertTrue(!result.Content.Contains("to none"), "sanitized content should not contain stance none");
}

private static void WlDispatchSanitizerLeavesNormalContentUnchanged()
{
    string input = "We are maintaining stations until we receive further orders.";
    var result = WlDispatchSanitizer.Sanitize(56, input);

    AssertEqual(false, result.Changed);
    AssertEqual(input, result.Content);
}
```

- [ ] **Step 2: Run the failing tests**

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: compile fails because `WlDispatchSanitizer` does not exist.

- [ ] **Step 3: Create the pure sanitizer**

Create `src/WhiskeyRealism/Strategic/WlDispatchSanitizer.cs`:

```csharp
namespace WhiskeyRealism.Strategic
{
    internal readonly struct WlDispatchSanitizerResult
    {
        internal WlDispatchSanitizerResult(string content, bool changed)
        {
            Content = content;
            Changed = changed;
        }

        internal string Content { get; }
        internal bool Changed { get; }
    }

    internal static class WlDispatchSanitizer
    {
        private const string BadInstructionSentence = "I will carry on according to your instructions that are to none.";
        private const string ReplacementInstructionSentence = "I will hold position and await further instructions.";
        private const string BadFallbackSentence = "I will none if no other orders are received";
        private const string ReplacementFallbackSentence = "I will hold position if no other orders are received";

        internal static WlDispatchSanitizerResult Sanitize(int messageType, string content)
        {
            if (!IsCandidateType(messageType) || string.IsNullOrEmpty(content))
                return new WlDispatchSanitizerResult(content, false);

            string sanitized = content;
            sanitized = sanitized.Replace(BadInstructionSentence, ReplacementInstructionSentence);
            sanitized = sanitized.Replace(BadFallbackSentence, ReplacementFallbackSentence);

            return new WlDispatchSanitizerResult(sanitized, sanitized != content);
        }

        internal static bool IsCandidateType(int messageType)
        {
            return messageType == 15 || messageType == 56 || messageType == 57;
        }
    }
}
```

- [ ] **Step 4: Add the test compile entry**

In `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`, add this line near the other W&L strategic entries:

```xml
<Compile Include="..\..\src\WhiskeyRealism\Strategic\WlDispatchSanitizer.cs" Link="WlDispatchSanitizer.cs" />
```

- [ ] **Step 5: Run the tests**

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: all existing tests plus the three new sanitizer tests pass.

- [ ] **Step 6: Commit C0a pure sanitizer**

Run:

```bash
git add src/WhiskeyRealism/Strategic/WlDispatchSanitizer.cs tests/WhiskeyRealism.Tests/Program.cs tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
git commit -m "test: add wl dispatch sanitizer rules"
```

## Task 2: C0a Dispatch Stance Sanitizer Patch

**Files:**
- Create: `src/WhiskeyRealism/Patches/DispatchStanceSanitizerPatch.cs`

- [ ] **Step 1: Re-read the vanilla patch surface**

Run:

```bash
nl -ba /tmp/gt_src/asm/Assembly-CSharp.decompiled.cs | sed -n '195360,195475p'
nl -ba /tmp/gt_src/asm/Assembly-CSharp.decompiled.cs | sed -n '196056,196107p'
```

Expected: `Messages.Message.GenerateMessageContent()` is still an instance `void` method, and message types 56/57 still write the stance text through `GameVars.groupstancename[...]`.

- [ ] **Step 2: Add the Harmony Postfix**

Create `src/WhiskeyRealism/Patches/DispatchStanceSanitizerPatch.cs`:

```csharp
using System;
using HarmonyLib;
using WhiskeyRealism.Strategic;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // Vanilla Messages.Message.GenerateMessageContent renders W&L campaign
    // status dispatches immediately. Type 56 and related stance messages can
    // render GameVars.groupstancename[0] as "none"; sanitize only the rendered
    // bad sentence for player-chain W&L messages.
    [HarmonyPatch(typeof(Messages.Message), "GenerateMessageContent")]
    internal static class DispatchStanceSanitizerPatch
    {
        [HarmonyPostfix]
        internal static void Postfix(Messages.Message __instance)
        {
            try
            {
                if (Plugin.Instance == null || !Plugin.Instance.Enabled.Value) return;
                if (__instance == null || !WlDispatchSanitizer.IsCandidateType(__instance.type)) return;
                if (!DLC_WL.dlc_scenarioactive) return;

                var unit = MessageUnit(__instance);
                if (!IsPlayerChainUnit(unit)) return;

                var result = WlDispatchSanitizer.Sanitize(__instance.type, __instance.content);
                if (!result.Changed) return;

                __instance.content = result.Content;
                OnceLog.Info(
                    "wl-dispatch-sanitizer:type:" + __instance.type.ToString(),
                    $"[W&LDispatch] sanitized stance-0 dispatch type={__instance.type} unit={SafeName(unit)}");
            }
            catch (Exception ex)
            {
                OnceLog.Warning("wl-dispatch-sanitizer:postfix", "[W&LDispatch] sanitizer failed: " + ex.Message);
            }
        }

        private static Regiment MessageUnit(Messages.Message message)
        {
            if (message == null) return null;
            if (message.type == 57 && message.sender != null) return message.sender;
            return message.regref ?? message.sender;
        }

        private static bool IsPlayerChainUnit(Regiment unit)
        {
            try
            {
                if (unit == null) return false;
                if (unit.alliance != GameVars.playeralliance) return false;
                if (unit.dlcw_isundercommander) return true;
                return DLC_WL.IsPlayerPartOfUnit(unit);
            }
            catch
            {
                return false;
            }
        }

        private static string SafeName(Regiment unit)
        {
            try { return unit != null ? ((UnityEngine.Object)unit).name : "<none>"; }
            catch { return "<unknown>"; }
        }
    }
}
```

- [ ] **Step 3: Build**

Run:

```bash
./build.sh
```

Expected: build succeeds with `dist/WhiskeyRealism.dll` produced.

- [ ] **Step 4: Commit the patch**

Run:

```bash
git add src/WhiskeyRealism/Patches/DispatchStanceSanitizerPatch.cs
git commit -m "fix: sanitize wl stance-none dispatch text"
```

## Task 3: C0a Docs, Deploy, And Runtime Smoke

**Files:**
- Modify: `docs/patch-catalog.md`
- Modify: `docs/handoff.md`

- [ ] **Step 1: Add patch catalog row #36**

In `docs/patch-catalog.md`, add this row after #35:

```markdown
| 36 | `DispatchStanceSanitizerPatch` | Postfix | `Patches/DispatchStanceSanitizerPatch.cs` | `Messages.Message.GenerateMessageContent` (195447) | W&L campaign dispatch text sanitizer. After vanilla renders message content, replaces only known stance-0 sentences such as "instructions that are to none" for player-chain W&L messages of types 15/56/57. Does not edit `GameVars.groupstancename[0]`, does not suppress messages, and does not change current-order state. Logs bounded `[W&LDispatch]` first-sanitized markers. |
```

- [ ] **Step 2: Add handoff active-workstream note**

In `docs/handoff.md`, add a current-state note under the active workstream table or current "Next actions" section:

```markdown
- **W&L dispatch/objective bridge C0a active.** Current implementation target is #36 `DispatchStanceSanitizerPatch`, a narrow Postfix on `Messages.Message.GenerateMessageContent` that removes newly generated stance-0 `"none"` text from player-chain W&L dispatches. It does not yet claim current-order bridge behavior. C0b/C0c remain follow-up after C0a smoke.
```

- [ ] **Step 3: Run tests and build**

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
./build.sh
```

Expected: console harness passes and build succeeds.

- [ ] **Step 4: Deploy and verify hash**

Close GTCW first if it is running. Then run:

```bash
cp dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/"
stat -c '%y %s %n' dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/WhiskeyRealism.dll"
sha256sum dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/WhiskeyRealism.dll"
```

Expected: `stat` shows the deployed DLL updated, and both `sha256sum` lines match exactly.

- [ ] **Step 5: Runtime smoke C0a**

Start a fresh or live W&L career as a subordinate and let campaign time advance until a campaign status dispatch is generated.

Probe the log:

```bash
rg -n "W&LDispatch|msg_reachedfinalwaypointcamp|to none|I will none|Exception|Harmony" \
  "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/LogOutput.log"
```

Expected:

- If the bad message path fires, log contains one bounded `[W&LDispatch] sanitized stance-0 dispatch` line.
- Log does not contain repeated sanitizer warnings.
- Newly generated dispatch content shown to the user no longer contains `"to none"` or `"I will none"`.
- Old saved literal messages may still contain old text; do not treat old saved content as a C0a failure.

- [ ] **Step 6: Commit C0a docs**

Run:

```bash
git add docs/patch-catalog.md docs/handoff.md
git commit -m "docs: record wl dispatch sanitizer"
```

## Task 4: C0b Strategic Order Bridge Classifier

**Files:**
- Create: `src/WhiskeyRealism/Strategic/WlStrategicOrderBridge.cs`
- Modify: `tests/WhiskeyRealism.Tests/Program.cs`
- Modify: `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`

- [ ] **Step 1: Add failing classifier tests**

Add these test registrations near the W&L tests in `Program.cs`:

```csharp
("wl bridge inactive allows direct movement", WlBridgeInactiveAllowsDirectMovement),
("wl bridge player cic skips movement", WlBridgePlayerCicSkipsMovement),
("wl bridge moved by player skips movement", WlBridgeMovedByPlayerSkipsMovement),
("wl bridge eligible under commander issues current order", WlBridgeEligibleUnderCommanderIssuesCurrentOrder),
("wl bridge ineligible under commander blocks direct fallback", WlBridgeIneligibleUnderCommanderBlocksDirectFallback),
("wl bridge failed vanilla call blocks direct fallback", WlBridgeFailedVanillaCallBlocksDirectFallback),
("wl bridge part of player unit not under commander stays direct for c0c", WlBridgePartOfPlayerUnitNotUnderCommanderStaysDirectForC0c),
```

Add these test methods:

```csharp
private static WlStrategicRoleFacts BaseWlFacts()
{
    return new WlStrategicRoleFacts(
        wlActive: true,
        isPlayerAlliance: true,
        isPlayerCic: false,
        isMovedByPlayer: false,
        isUnderCommander: true,
        isPartOfPlayerUnit: true,
        currentCommandIsCampaignGroup: true,
        currentCommandParentIsUnderTargetUnit: true);
}

private static void WlBridgeInactiveAllowsDirectMovement()
{
    var facts = new WlStrategicRoleFacts(false, true, false, false, false, false, false, false);
    var decision = WlStrategicOrderBridge.Classify(WlStrategicIntent.Redeploy, facts);
    AssertEqual(WlStrategicOrderResult.NotWl, decision.Result);
    AssertEqual(true, decision.MayDirectMove);
}

private static void WlBridgePlayerCicSkipsMovement()
{
    var facts = new WlStrategicRoleFacts(true, true, true, false, true, true, true, true);
    var decision = WlStrategicOrderBridge.Classify(WlStrategicIntent.Offensive, facts);
    AssertEqual(WlStrategicOrderResult.SkippedPlayerCic, decision.Result);
    AssertEqual(false, decision.MayDirectMove);
}

private static void WlBridgeMovedByPlayerSkipsMovement()
{
    var facts = new WlStrategicRoleFacts(true, true, false, true, true, true, true, true);
    var decision = WlStrategicOrderBridge.Classify(WlStrategicIntent.EngageEnemy, facts);
    AssertEqual(WlStrategicOrderResult.SkippedPlayerControlled, decision.Result);
    AssertEqual(false, decision.MayDirectMove);
}

private static void WlBridgeEligibleUnderCommanderIssuesCurrentOrder()
{
    var decision = WlStrategicOrderBridge.Classify(WlStrategicIntent.Offensive, BaseWlFacts());
    AssertEqual(WlStrategicOrderResult.IssuedWlCurrentOrder, decision.Result);
    AssertEqual(16, decision.WlOrderType);
    AssertEqual(false, decision.MayDirectMove);
}

private static void WlBridgeIneligibleUnderCommanderBlocksDirectFallback()
{
    var facts = new WlStrategicRoleFacts(true, true, false, false, true, true, false, true);
    var decision = WlStrategicOrderBridge.Classify(WlStrategicIntent.Probe, facts);
    AssertEqual(WlStrategicOrderResult.WlCurrentOrderIneligible, decision.Result);
    AssertEqual(false, decision.MayDirectMove);
}

private static void WlBridgeFailedVanillaCallBlocksDirectFallback()
{
    var decision = WlStrategicOrderBridge.Classify(WlStrategicIntent.Probe, BaseWlFacts(), vanillaBridgeSucceeded: false);
    AssertEqual(WlStrategicOrderResult.FailedVanillaBridge, decision.Result);
    AssertEqual(false, decision.MayDirectMove);
}

private static void WlBridgePartOfPlayerUnitNotUnderCommanderStaysDirectForC0c()
{
    var facts = new WlStrategicRoleFacts(true, true, false, false, false, true, false, false);
    var decision = WlStrategicOrderBridge.Classify(WlStrategicIntent.Redeploy, facts);
    AssertEqual(WlStrategicOrderResult.DirectMovementAllowed, decision.Result);
    AssertEqual(true, decision.MayDirectMove);
    AssertContains(decision.Reason, "part-of-player-unit", "reason");
}
```

- [ ] **Step 2: Run tests and confirm compile failure**

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: compile fails because `WlStrategicOrderBridge` and related types do not exist.

- [ ] **Step 3: Add the pure classifier and live request DTO**

Create `src/WhiskeyRealism/Strategic/WlStrategicOrderBridge.cs` with this initial classifier surface:

```csharp
using UnityEngine;

namespace WhiskeyRealism.Strategic
{
    internal enum WlStrategicIntent
    {
        Redeploy,
        Probe,
        Offensive,
        OffensiveContinuation,
        EngageEnemy,
        DefendCapital,
        ConstructFort,
        ConstructSupplyDepot,
        ReportOnly
    }

    internal enum WlStrategicOrderResult
    {
        NotWl,
        DirectMovementAllowed,
        IssuedWlCurrentOrder,
        SkippedPlayerControlled,
        SkippedPlayerCic,
        FailedVanillaBridge,
        WlCurrentOrderIneligible,
        ReportOnly
    }

    internal readonly struct WlStrategicRoleFacts
    {
        internal WlStrategicRoleFacts(
            bool wlActive,
            bool isPlayerAlliance,
            bool isPlayerCic,
            bool isMovedByPlayer,
            bool isUnderCommander,
            bool isPartOfPlayerUnit,
            bool currentCommandIsCampaignGroup,
            bool currentCommandParentIsUnderTargetUnit)
        {
            WlActive = wlActive;
            IsPlayerAlliance = isPlayerAlliance;
            IsPlayerCic = isPlayerCic;
            IsMovedByPlayer = isMovedByPlayer;
            IsUnderCommander = isUnderCommander;
            IsPartOfPlayerUnit = isPartOfPlayerUnit;
            CurrentCommandIsCampaignGroup = currentCommandIsCampaignGroup;
            CurrentCommandParentIsUnderTargetUnit = currentCommandParentIsUnderTargetUnit;
        }

        internal bool WlActive { get; }
        internal bool IsPlayerAlliance { get; }
        internal bool IsPlayerCic { get; }
        internal bool IsMovedByPlayer { get; }
        internal bool IsUnderCommander { get; }
        internal bool IsPartOfPlayerUnit { get; }
        internal bool CurrentCommandIsCampaignGroup { get; }
        internal bool CurrentCommandParentIsUnderTargetUnit { get; }
    }

    internal readonly struct WlStrategicOrderDecision
    {
        internal WlStrategicOrderDecision(
            WlStrategicOrderResult result,
            int wlOrderType,
            bool mayDirectMove,
            bool mayMutateOperationList,
            string reason)
        {
            Result = result;
            WlOrderType = wlOrderType;
            MayDirectMove = mayDirectMove;
            MayMutateOperationList = mayMutateOperationList;
            Reason = reason ?? string.Empty;
        }

        internal WlStrategicOrderResult Result { get; }
        internal int WlOrderType { get; }
        internal bool MayDirectMove { get; }
        internal bool MayMutateOperationList { get; }
        internal string Reason { get; }
    }

    internal sealed class WlStrategicOrderRequest
    {
        internal int AllianceId;
        internal int AifactionIndex;
        internal Regiment Unit;
        internal Vector3 TargetPosition;
        internal string TargetName;
        internal int ObjectiveId;
        internal WlStrategicIntent Intent;
        internal float Width;
        internal float Depth;
        internal string SourceSystem;
    }

    internal static class WlStrategicOrderBridge
    {
        internal static WlStrategicOrderDecision Classify(
            WlStrategicIntent intent,
            WlStrategicRoleFacts facts,
            bool vanillaBridgeSucceeded = true)
        {
            int type = WlOrderTypeForIntent(intent);

            if (!facts.WlActive)
                return new WlStrategicOrderDecision(WlStrategicOrderResult.NotWl, type, true, true, "wl-inactive");

            if (!facts.IsPlayerAlliance)
                return new WlStrategicOrderDecision(WlStrategicOrderResult.DirectMovementAllowed, type, true, true, "non-player-alliance");

            if (facts.IsPlayerCic)
                return new WlStrategicOrderDecision(WlStrategicOrderResult.SkippedPlayerCic, type, false, false, "player-cic");

            if (facts.IsMovedByPlayer)
                return new WlStrategicOrderDecision(WlStrategicOrderResult.SkippedPlayerControlled, type, false, false, "moved-by-player");

            if (facts.IsUnderCommander)
            {
                if (!facts.CurrentCommandIsCampaignGroup || !facts.CurrentCommandParentIsUnderTargetUnit)
                    return new WlStrategicOrderDecision(WlStrategicOrderResult.WlCurrentOrderIneligible, type, false, false, "vanilla-chain-ineligible");

                if (!vanillaBridgeSucceeded)
                    return new WlStrategicOrderDecision(WlStrategicOrderResult.FailedVanillaBridge, type, false, false, "vanilla-bridge-no-session-change");

                return new WlStrategicOrderDecision(WlStrategicOrderResult.IssuedWlCurrentOrder, type, false, false, "issued-current-order");
            }

            if (facts.IsPartOfPlayerUnit)
                return new WlStrategicOrderDecision(WlStrategicOrderResult.DirectMovementAllowed, type, true, true, "part-of-player-unit-not-under-commander-c0c-direct");

            return new WlStrategicOrderDecision(WlStrategicOrderResult.DirectMovementAllowed, type, true, true, "not-player-chain");
        }

        internal static int WlOrderTypeForIntent(WlStrategicIntent intent)
        {
            switch (intent)
            {
                case WlStrategicIntent.Redeploy:
                case WlStrategicIntent.Probe:
                    return 5;
                case WlStrategicIntent.Offensive:
                    return 16;
                case WlStrategicIntent.OffensiveContinuation:
                    return 6;
                case WlStrategicIntent.EngageEnemy:
                    return 7;
                case WlStrategicIntent.DefendCapital:
                    return 8;
                case WlStrategicIntent.ConstructFort:
                    return 9;
                case WlStrategicIntent.ConstructSupplyDepot:
                    return 10;
                default:
                    return -1;
            }
        }

        internal static float DefaultWidth(WlStrategicIntent intent, float requestedWidth)
        {
            return requestedWidth > 0f ? requestedWidth : 20f;
        }

        internal static float DefaultDepth(WlStrategicIntent intent, float requestedDepth)
        {
            return requestedDepth > 0f ? requestedDepth : 20f;
        }
    }
}
```

- [ ] **Step 4: Add the test compile entry**

In `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`, add:

```xml
<Compile Include="..\..\src\WhiskeyRealism\Strategic\WlStrategicOrderBridge.cs" Link="WlStrategicOrderBridge.cs" />
```

- [ ] **Step 5: Run tests**

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: all tests pass, including seven bridge classifier tests.

- [ ] **Step 6: Commit C0b classifier**

Run:

```bash
git add src/WhiskeyRealism/Strategic/WlStrategicOrderBridge.cs tests/WhiskeyRealism.Tests/Program.cs tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
git commit -m "feat: add wl strategic order classifier"
```

## Task 5: C0b Live Adapter

**Files:**
- Modify: `src/WhiskeyRealism/Strategic/WlStrategicOrderBridge.cs`

- [ ] **Step 1: Add live adapter methods**

Append these methods inside `WlStrategicOrderBridge`:

```csharp
internal static WlStrategicOrderDecision TryIssue(WlStrategicOrderRequest request)
{
    if (request == null)
        return new WlStrategicOrderDecision(WlStrategicOrderResult.DirectMovementAllowed, -1, true, true, "null-request");

    WlStrategicRoleFacts facts = BuildFacts(request);
    var decision = Classify(request.Intent, facts);
    if (decision.Result != WlStrategicOrderResult.IssuedWlCurrentOrder)
        return decision;

    int beforeSession = ReadGivenOrdersSession();
    object beforeOrder = DLC_WL.givenorder;

    AIBattle.CheckCurrentOrderUpdate(
        request.Unit,
        decision.WlOrderType,
        request.TargetPosition,
        string.IsNullOrEmpty(request.TargetName) ? "Objective" : request.TargetName,
        -1f,
        DefaultWidth(request.Intent, request.Width),
        DefaultDepth(request.Intent, request.Depth),
        calledfromcampaign: true);

    bool changed = ReadGivenOrdersSession() != beforeSession || !object.ReferenceEquals(beforeOrder, DLC_WL.givenorder);
    if (!changed)
        return Classify(request.Intent, facts, vanillaBridgeSucceeded: false);

    return decision;
}

private static WlStrategicRoleFacts BuildFacts(WlStrategicOrderRequest request)
{
    bool wlActive = SafeWlActive();
    bool playerAlliance = request.Unit != null && request.Unit.alliance == GameVars.playeralliance && request.AllianceId == GameVars.playeralliance;
    bool playerCic = SafePlayerCic(request.AllianceId);
    bool movedByPlayer = SafeMovedByPlayer(request.Unit);
    bool underCommander = request.Unit != null && request.Unit.dlcw_isundercommander;
    bool partOfPlayerUnit = SafeIsPlayerPartOfUnit(request.Unit);
    bool currentCommandIsCampaignGroup = false;
    bool currentCommandParentIsUnderTarget = false;

    try
    {
        var current = GameVars.commander[DLC_WL.dlc_chosencommander].currentcommand;
        var campaignGroup = BattleUnits.GetCampaignGroup(current);
        var parent = BattleUnits.GetParentUnit(current);
        currentCommandIsCampaignGroup = current != null && campaignGroup == current;
        currentCommandParentIsUnderTarget = parent != null && request.Unit != null && parent.transform.IsChildOf(request.Unit.transform);
    }
    catch { }

    return new WlStrategicRoleFacts(
        wlActive,
        playerAlliance,
        playerCic,
        movedByPlayer,
        underCommander,
        partOfPlayerUnit,
        currentCommandIsCampaignGroup,
        currentCommandParentIsUnderTarget);
}

private static bool SafeWlActive()
{
    try { return DLC_WL.dlc_scenarioactive; }
    catch { return false; }
}

private static bool SafePlayerCic(int allianceId)
{
    try
    {
        return DLC_WL.IsCommanderInChief() ||
               StrategicCoordinator.IsPlayerCICOf(allianceId, GameVars.playeralliance);
    }
    catch { return false; }
}

private static bool SafeMovedByPlayer(Regiment unit)
{
    try { return unit != null && DLC_WL.IsMovedByPlayer(unit); }
    catch { return false; }
}

private static bool SafeIsPlayerPartOfUnit(Regiment unit)
{
    try { return unit != null && DLC_WL.IsPlayerPartOfUnit(unit); }
    catch { return false; }
}

private static int ReadGivenOrdersSession()
{
    try { return DLC_WL.GivenOrders.givenorderssession; }
    catch { return -1; }
}
```

- [ ] **Step 2: Build**

Run:

```bash
./build.sh
```

Expected: build succeeds.

- [ ] **Step 3: Commit live adapter**

Run:

```bash
git add src/WhiskeyRealism/Strategic/WlStrategicOrderBridge.cs
git commit -m "feat: add wl strategic order live adapter"
```

## Task 6: C0c Convert Operational Probe Runtime

**Files:**
- Modify: `src/WhiskeyRealism/Strategic/OperationalProbeRuntime.cs`

- [ ] **Step 1: Insert bridge decision before direct move**

In `OperationalProbeRuntime.Run(...)`, immediately before the existing `AICampaign.MoveUnitTo(unit, target.Value, true)` block, insert:

```csharp
var intent = output.Decision == OperationalProbeDecision.Escalate
    ? WlStrategicIntent.Offensive
    : WlStrategicIntent.Probe;
var bridgeDecision = WlStrategicOrderBridge.TryIssue(new WlStrategicOrderRequest
{
    AllianceId = allianceId,
    AifactionIndex = aifactionIndex,
    Unit = unit,
    TargetPosition = target.Value,
    TargetName = string.IsNullOrEmpty(output.TargetAreaKey) ? "Objective" : output.TargetAreaKey,
    ObjectiveId = -1,
    Intent = intent,
    Width = 20f,
    Depth = 20f,
    SourceSystem = "OperationalProbe"
});

if (bridgeDecision.Result == WlStrategicOrderResult.IssuedWlCurrentOrder)
{
    Plugin.Log.LogInfo(
        $"[OperationalProbe] alliance={allianceId} decision={output.Decision} " +
        $"unit={SafeName(unit)} action=wl-current-order type={bridgeDecision.WlOrderType} reason={output.Reason}");
    return;
}

if (!bridgeDecision.MayDirectMove)
{
    OnceLog.Info(
        $"operational-probe:wl-skip:{allianceId}:{UnitKey(unit)}:{bridgeDecision.Result}",
        $"[OperationalProbe] alliance={allianceId} unit={SafeName(unit)} action=skip-direct-move wlResult={bridgeDecision.Result} reason={bridgeDecision.Reason}");
    return;
}
```

Leave the existing direct `MoveUnitTo` and `offensive.Add(unit)` block unchanged after this insert. That preserves non-W&L and opposing-AI behavior.

- [ ] **Step 2: Build**

Run:

```bash
./build.sh
```

Expected: build succeeds.

- [ ] **Step 3: Commit operational probe conversion**

Run:

```bash
git add src/WhiskeyRealism/Strategic/OperationalProbeRuntime.cs
git commit -m "feat: bridge wl operational probe orders"
```

## Task 7: C0c Convert Army Area Runtime

**Files:**
- Modify: `src/WhiskeyRealism/Strategic/ArmyAreaRuntime.cs`

- [ ] **Step 1: Insert bridge decision before `SetTheaterPosition`**

In `ArmyAreaRuntime.ApplyHistoricalAreaOrders(...)`, after the `TryGetAnchor(...)` check and before `SetTheaterPosition(unit, anchor);`, insert:

```csharp
var regiment = unit as Regiment;
var bridgeDecision = WlStrategicOrderBridge.TryIssue(new WlStrategicOrderRequest
{
    AllianceId = allianceId,
    AifactionIndex = aifactionIndex,
    Unit = regiment,
    TargetPosition = anchor,
    TargetName = assignment.AssignedAreaKey,
    ObjectiveId = -1,
    Intent = WlStrategicIntent.Redeploy,
    Width = 20f,
    Depth = 20f,
    SourceSystem = "ArmyArea"
});

if (bridgeDecision.Result == WlStrategicOrderResult.IssuedWlCurrentOrder)
{
    issued++;
    OnceLog.Info(
        $"army-area:{allianceId}:{UnitKey(unit)}:{assignment.AssignedAreaKey}:wl-order",
        $"[Patch:ArmyArea] alliance={allianceId} unit={ObjectName(unit)} action=wl-current-order area={assignment.AssignedAreaKey} type={bridgeDecision.WlOrderType} reason={assignment.Reason}");
    continue;
}

if (!bridgeDecision.MayDirectMove)
{
    OnceLog.Info(
        $"army-area:{allianceId}:{UnitKey(unit)}:{assignment.AssignedAreaKey}:wl-skip:{bridgeDecision.Result}",
        $"[Patch:ArmyArea] alliance={allianceId} unit={ObjectName(unit)} action=skip-return-area wlResult={bridgeDecision.Result} reason={bridgeDecision.Reason}");
    continue;
}
```

This preserves the spec rule that `theaterposition` is not written when the bridge emits or rejects a W&L order.

- [ ] **Step 2: Build**

Run:

```bash
./build.sh
```

Expected: build succeeds.

- [ ] **Step 3: Commit army-area conversion**

Run:

```bash
git add src/WhiskeyRealism/Strategic/ArmyAreaRuntime.cs
git commit -m "feat: bridge wl army-area redeploy orders"
```

## Task 8: C0c Convert Coastal Defense Custom Orders

**Files:**
- Modify: `src/WhiskeyRealism/Strategic/CoastalDefenseCustomOrderRunner.cs`

- [ ] **Step 1: Insert bridge before custom defensive movement**

In `CoastalDefenseCustomOrderRunner.Run(...)`, inside the selected-package loop, replace:

```csharp
var anchor = new Vector3(response.Threat.X, 0f, response.Threat.Z);
SafeMoveUnitTo(unit, anchor);
defOps.Add(unit);
```

with:

```csharp
var anchor = new Vector3(response.Threat.X, 0f, response.Threat.Z);
var bridgeDecision = WlStrategicOrderBridge.TryIssue(new WlStrategicOrderRequest
{
    AllianceId = allianceId,
    AifactionIndex = aifactionIndex,
    Unit = unit,
    TargetPosition = anchor,
    TargetName = sig,
    ObjectiveId = -1,
    Intent = WlStrategicIntent.EngageEnemy,
    Width = 20f,
    Depth = 20f,
    SourceSystem = "CoastalDefense"
});

if (bridgeDecision.Result == WlStrategicOrderResult.IssuedWlCurrentOrder)
{
    defOps.Add(unit);
}
else if (bridgeDecision.MayDirectMove)
{
    SafeMoveUnitTo(unit, anchor);
    defOps.Add(unit);
}
else
{
    OnceLog.Info(
        $"defense-intent:custom-order:wl-skip:{allianceId}:{sig}:{candidate.UnitInstanceId}:{bridgeDecision.Result}",
        $"[DefenseIntent] skipped-wl-custom-order alliance={allianceId} threat={sig} unit={SafeName(unit, candidate.UnitInstanceId)} wlResult={bridgeDecision.Result} reason={bridgeDecision.Reason}");
    continue;
}
```

This intentionally allows `unitsindefensiveoperations` mutation after `IssuedWlCurrentOrder` for `EngageEnemy`, matching vanilla defensive response semantics.

- [ ] **Step 2: Build**

Run:

```bash
./build.sh
```

Expected: build succeeds.

- [ ] **Step 3: Commit coastal defense conversion**

Run:

```bash
git add src/WhiskeyRealism/Strategic/CoastalDefenseCustomOrderRunner.cs
git commit -m "feat: bridge wl coastal defense orders"
```

## Task 9: Audit Defensive Candidate Revert Movement

**Files:**
- Modify: `src/WhiskeyRealism/Patches/CheckForDefensiveOperationsCandidateFilterPatch.cs`

- [ ] **Step 1: Guard the revert move for player-chain W&L units**

In the Postfix block where `SafeMoveUnitTo(unit, priorPos);` is called, replace:

```csharp
SafeMoveUnitTo(unit, priorPos);
```

with:

```csharp
if (ShouldAvoidDirectWlRevert(unit, allianceId))
{
    OnceLog.Info(
        $"defense-intent:filter:wl-revert-skip:{allianceId}:{id}",
        $"[DefenseIntent] skipped direct W&L revert alliance={allianceId} candidate={SafeUnitName(unit, id)} reason=player-chain");
}
else
{
    SafeMoveUnitTo(unit, priorPos);
}
```

Add these helpers in the patch class:

```csharp
private static bool ShouldAvoidDirectWlRevert(Regiment unit, int allianceId)
{
    try
    {
        if (unit == null) return false;
        if (!DLC_WL.dlc_scenarioactive) return false;
        if (allianceId != GameVars.playeralliance || unit.alliance != GameVars.playeralliance) return false;
        if (DLC_WL.IsCommanderInChief()) return true;
        return unit.dlcw_isundercommander || DLC_WL.IsPlayerPartOfUnit(unit);
    }
    catch
    {
        return false;
    }
}

private static string SafeUnitName(Regiment unit, int fallbackId)
{
    try { return unit != null ? ((UnityEngine.Object)unit).name : fallbackId.ToString(); }
    catch { return fallbackId.ToString(); }
}
```

- [ ] **Step 2: Build**

Run:

```bash
./build.sh
```

Expected: build succeeds.

- [ ] **Step 3: Commit revert audit**

Run:

```bash
git add src/WhiskeyRealism/Patches/CheckForDefensiveOperationsCandidateFilterPatch.cs
git commit -m "fix: avoid direct wl defensive revert movement"
```

## Task 10: Full Verification And Closeout Docs

**Files:**
- Modify: `docs/patch-catalog.md`
- Modify: `docs/handoff.md`

- [ ] **Step 1: Run console tests and build**

Run:

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
./build.sh
```

Expected: console harness passes and build succeeds.

- [ ] **Step 2: Deploy and verify hash**

Close GTCW first if it is running. Then run:

```bash
cp dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/"
stat -c '%y %s %n' dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/WhiskeyRealism.dll"
sha256sum dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/WhiskeyRealism.dll"
```

Expected: deployed and dist DLL hashes match exactly.

- [ ] **Step 3: Runtime smoke C0c**

Use a W&L career and exercise these states:

1. Start as subordinate: current-order UI may show no specific order until real AI commander order appears; generic dispatches do not show `"none"`.
2. Let campaign AI issue movement/defense/offensive order near the player command chain.
3. Promote or appoint to an independent division/corps/army command and confirm Whiskey does not directly drag the player command.
4. Promote to CIC or force CIC state and confirm player-alliance Whiskey steering is skipped.

Probe logs:

```bash
rg -n "W&LDispatch|WlStrategicOrderBridge|checking new player AI order|adding new player AI order|wl-current-order|skip-direct-move|skipped-wl-custom-order|to none|I will none|Exception|Harmony" \
  "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/LogOutput.log"
```

Expected:

- `checking new player AI order` / `adding new player AI order` appears only when vanilla accepts the bridge call.
- Bridge skip logs are bounded and show `WlCurrentOrderIneligible`, `FailedVanillaBridge`, `SkippedPlayerControlled`, or `SkippedPlayerCic` instead of direct movement for player-chain W&L units.
- No new repeated exceptions or Harmony patch failures.

- [ ] **Step 4: Update docs after smoke**

In `docs/patch-catalog.md`, add a helper/runtime row after the numbered table:

```markdown
| - | `WlStrategicOrderBridge` | Coordinator/helper/runtime | `Strategic/WlStrategicOrderBridge.cs` | called by operational probe, army-area, and coastal-defense runtime conversions | Central W&L strategic order bridge. Classifies player role state without Unity dependencies for tests, then live adapters call vanilla `AIBattle.CheckCurrentOrderUpdate(..., calledfromcampaign:true)` only when the W&L chain gate should pass. Failed or ineligible player-chain bridge calls log and skip instead of direct-moving. |
```

In `docs/handoff.md`, replace the active-workstream note with a concrete shipped-status bullet after the smoke run. The bullet must include the exact smoke date, scenario, deployed DLL SHA-256, and whether the log probe found bridge-order lines, sanitizer lines, `"to none"` text, or exceptions. Do not commit this docs step until those actual values are written.

```markdown
- **W&L dispatch/objective bridge C0a-C0c shipped locally.** #36 sanitizes newly generated player-chain W&L dispatches so stance-0 messages do not render `"none"`. `WlStrategicOrderBridge` now routes eligible operational probe, army-area redeploy, and coastal-defense engage intents through vanilla W&L current orders, while failed/ineligible player-chain calls log and skip instead of direct-moving.
```

- [ ] **Step 5: Commit closeout docs**

Run:

```bash
git add docs/patch-catalog.md docs/handoff.md
git commit -m "docs: close out wl dispatch bridge"
```

## Rollback And Defer Boundaries

- If C0a sanitizer throws in live logs, disable by reverting only `DispatchStanceSanitizerPatch.cs`; do not alter `GameVars.groupstancename[0]`.
- If C0b classifier tests pass but live adapter compile fails on nested `DLC_WL.GivenOrders`, switch only session-read logic to reflection.
- If any C0c conversion causes missing movement for non-W&L or opposing AI, revert that caller conversion commit only; C0a and C0b can remain.
- Do not add a behavior-changing global `MoveUnitTo` patch.
- Do not add a `CheckCurrentOrderUpdate` Transpiler without explicit user approval.
- Do not add report-only generic dispatches until C0a runtime smoke proves the sanitizer path.

## Self-Review Checklist

- Spec coverage: C0a covers dispatch `"none"`; C0b covers pure classifier and failed-bridge semantics; C0c covers three direct strategic movement callers; C0d rich popup decoration remains out of scope with a stated reason.
- Placeholder scan: no `TBD`, `TODO`, or angle-bracket evidence slots are present; Task 10 requires actual smoke evidence before closeout docs are committed.
- Type consistency: `WlStrategicIntent`, `WlStrategicRoleFacts`, `WlStrategicOrderDecision`, and `WlStrategicOrderResult` are introduced before any caller conversion references them.
- Verification gates: C0a and full closeout both require console tests, build, deploy, `stat`, and matching `sha256sum` before game smoke.
