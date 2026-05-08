# Tactical B6c Runtime Application Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Apply B6a/B6b doctrine to vanilla battle state through three precise surfaces: (1) fix the existing silent vanilla-stance-4 demotion in `BattleGroupStancePatch` and add the explicit DenyCharge demotion path with `[TacticalChargeDeny]` telemetry; (2) extend `BattleChargeGatePatch` (#41) so DenyCharge becomes defense-in-depth at the per-unit charge initiation surface; (3) add a named, default-off, snapshot-protected reserve-list bias patch that consumes B6b's `TacticalReserveIntent`. Wire `[TacticalLocalReaction]` and `[TacticalReserveIntent]` telemetry. Five new per-reaction config flags. No new movement, no SetWaypoint, no AddToOrderQueue Prefix, no artillery, no fallback, no retreat.

**Architecture:** B6c is the only B6 slice that writes vanilla battle state. It uses a small in-memory per-tick cache (`TacticalReactionContext`) so the modified `BattleGroupStancePatch` and the extended `BattleChargeGatePatch` see the same B6b decision keyed by `Regiment.GetInstanceID()`. The reserve patch is its own file. Each runtime branch sits behind its own config flag for granular rollback.

**Tech Stack:** BepInEx 5.4.x + HarmonyX 2.10.2, C# netstandard2.1, console harness (net8.0) covers the cache and reaction-state plumbing, in-game smoke covers the actual writes.

---

## File Structure

**Create:**
- `src/WhiskeyRealism/Tactical/TacticalReactionContext.cs` - per-tick cache mapping `Regiment.GetInstanceID()` → `TacticalLocalReactionDecision`, plus per-side reserve intent. Cleared at the start of each B6c observer pass.
- `src/WhiskeyRealism/Patches/BattleReserveDoctrinePatch.cs` - default-off Postfix on `AIBattle.AssignReserves` (decompile 7017). Reads `TacticalReactionContext` reserve intent; mutates `objectivechain[i].reservegroups` only with snapshot/restore protection.

**Modify:**
- `src/WhiskeyRealism/Plugin.cs` - bind four new ConfigEntry flags (`EnableTacticalCommanderIntentDoctrine` already lives from B6a — do **not** add a duplicate).
- `src/WhiskeyRealism/Patches/BattleCommanderIntentObserverPatch.cs` (created in B6a) - extend to also compute `TacticalLocalReactionScorer` decisions for each `unitsused[i]` group, populate `TacticalReactionContext`, aggregate for the side via `TacticalReservePolicyLedger`, and emit `[TacticalLocalReaction]` + `[TacticalReserveIntent]` telemetry. The existing `[TacticalIntent]` and `[TacticalPlaybook]` emissions stay.
- `src/WhiskeyRealism/Patches/BattleGroupStancePatch.cs` - implement the stance-4 contract. Preserve vanilla `ai_stanceordered == 4` when `PermitCharge` (or no DenyCharge); demote to 3 with `[TacticalChargeDeny]` telemetry under `Enable Tactical Charge Denial`.
- `src/WhiskeyRealism/Patches/BattleChargeGatePatch.cs` - add a second deny condition (DenyCharge from `TacticalReactionContext`) under `Enable Tactical Charge Denial`. The existing W&L ownership branch is unchanged.
- `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj` - add Compile Include for `TacticalReactionContext.cs`.
- `tests/WhiskeyRealism.Tests/Program.cs` - tests for the cache and the reserve snapshot-restore helper.
- `docs/patch-catalog.md` - update #45 (B5 stance contract change), update #41 (charge gate DenyCharge branch), add new ordinal for `BattleReserveDoctrinePatch`.
- `docs/handoff.md` - "What just shipped" update plus current DLL hash plus smoke evidence.

---

## Anchor Recheck

```bash
grep -n "private void AdjustGroupAIStance\|private void MicroAICheckForCharges\|private void AssignReserves\|public List<Regiment> reservegroups\|public void ChangeStance\|private static float timetorenewaichargecheck\|public float lastaichargetime" /tmp/gt_src/asm/Assembly-CSharp.decompiled.cs | head -15
```

Expected: `AdjustGroupAIStance` 4221, `MicroAICheckForCharges` 4905, `AssignReserves` 7017, `reservegroups` declaration 2972, `ChangeStance` (BattleUnits public method) — verify exists. If any line drifts, update this plan inline before implementing.

Verify B5/B1 patch shape unchanged before extending:

```bash
grep -n "decision.GroupStance == 4\|EnableWlTacticalChargeGuard.Value\|tookOwnership = true" ~/Projects/whiskey-realism-mod/src/WhiskeyRealism/Patches/BattleGroupStancePatch.cs ~/Projects/whiskey-realism-mod/src/WhiskeyRealism/Patches/BattleChargeGatePatch.cs
```

Expected: B5 has the `decision.GroupStance == 4 return` guard at line ~75; B1 has `tookOwnership` rollback discipline.

---

## Task 1: Add `TacticalReactionContext` per-tick cache

**Files:**
- Create: `src/WhiskeyRealism/Tactical/TacticalReactionContext.cs`
- Modify: `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`, `tests/WhiskeyRealism.Tests/Program.cs`

- [ ] **Step 1: Add Compile Include**

In `tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj`:

```xml
    <Compile Include="..\..\src\WhiskeyRealism\Tactical\TacticalReactionContext.cs" Link="TacticalReactionContext.cs" />
```

- [ ] **Step 2: Write the failing test**

Add the dispatch entry:

```csharp
            ("tactical b6c reaction context returns last decision per group", TacticalB6cReactionContextReturnsLastDecisionPerGroup),
            ("tactical b6c reaction context clear discards all entries", TacticalB6cReactionContextClearDiscards),
            ("tactical b6c reaction context missing key returns default maintain", TacticalB6cReactionContextMissingKeyReturnsDefault),
```

Test bodies:

```csharp
        private static void TacticalB6cReactionContextReturnsLastDecisionPerGroup()
        {
            var ctx = new TacticalReactionContext();
            var deny = new TacticalLocalReactionDecision(LocalReaction.DenyCharge, false, 0.7f, "deny");
            var permit = new TacticalLocalReactionDecision(LocalReaction.PermitCharge, false, 0.7f, "permit");

            ctx.SetReaction(groupInstanceId: 42, deny);
            ctx.SetReaction(groupInstanceId: 42, permit);
            ctx.SetReaction(groupInstanceId: 99, deny);

            Assert(ctx.GetReaction(42).Reaction == LocalReaction.PermitCharge, "Latest decision must win");
            Assert(ctx.GetReaction(99).Reaction == LocalReaction.DenyCharge, "Other key must persist");
        }

        private static void TacticalB6cReactionContextClearDiscards()
        {
            var ctx = new TacticalReactionContext();
            ctx.SetReaction(1, new TacticalLocalReactionDecision(LocalReaction.DenyCharge, false, 0.7f, "deny"));
            ctx.Clear();
            Assert(ctx.GetReaction(1).Reaction == LocalReaction.MaintainLine, "Clear must reset to default MaintainLine");
        }

        private static void TacticalB6cReactionContextMissingKeyReturnsDefault()
        {
            var ctx = new TacticalReactionContext();
            var d = ctx.GetReaction(123);
            Assert(d.Reaction == LocalReaction.MaintainLine, "Missing key must default to MaintainLine");
            Assert(d.Reason == "no-decision", "Missing key reason must be no-decision");
        }
```

- [ ] **Step 3: Run to verify failure**

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: missing `TacticalReactionContext`.

- [ ] **Step 4: Implement `TacticalReactionContext.cs`**

Create `src/WhiskeyRealism/Tactical/TacticalReactionContext.cs`:

```csharp
using System.Collections.Generic;

namespace WhiskeyRealism.Tactical
{
    public sealed class TacticalReactionContext
    {
        private readonly Dictionary<int, TacticalLocalReactionDecision> _reactions = new Dictionary<int, TacticalLocalReactionDecision>();
        private readonly Dictionary<int, TacticalReserveIntentDecision> _reserveIntents = new Dictionary<int, TacticalReserveIntentDecision>();

        public void SetReaction(int groupInstanceId, TacticalLocalReactionDecision decision)
        {
            _reactions[groupInstanceId] = decision;
        }

        public TacticalLocalReactionDecision GetReaction(int groupInstanceId)
        {
            if (_reactions.TryGetValue(groupInstanceId, out var decision)) return decision;
            return new TacticalLocalReactionDecision(LocalReaction.MaintainLine, false, 0f, "no-decision");
        }

        public void SetReserveIntent(int side, TacticalReserveIntentDecision decision)
        {
            _reserveIntents[side] = decision;
        }

        public TacticalReserveIntentDecision GetReserveIntent(int side)
        {
            if (_reserveIntents.TryGetValue(side, out var decision)) return decision;
            return new TacticalReserveIntentDecision(TacticalReserveIntent.None, false, 0f, "no-decision");
        }

        public void Clear()
        {
            _reactions.Clear();
            _reserveIntents.Clear();
        }

        public static readonly TacticalReactionContext Shared = new TacticalReactionContext();
    }
}
```

- [ ] **Step 5: Run, verify pass**

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/WhiskeyRealism/Tactical/TacticalReactionContext.cs tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj tests/WhiskeyRealism.Tests/Program.cs
git commit -m "$(cat <<'EOF'
feat(tactical): add B6c TacticalReactionContext shared cache

Per-tick map from group instance id to local reaction decision plus
per-side reserve intent. Lets the B5 stance patch and B1 charge gate
read the same B6b decision without recomputing.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 2: Bind the four new B6c config flags

**Files:**
- Modify: `src/WhiskeyRealism/Plugin.cs`

- [ ] **Step 1: Add field declarations**

In the existing tactical block of field declarations (after `EnableTacticalCommanderIntentDoctrine` from B6a), add:

```csharp
        internal ConfigEntry<bool> EnableTacticalLocalReactionDoctrine;
        internal ConfigEntry<bool> EnableTacticalChargeDenial;
        internal ConfigEntry<bool> EnableTacticalReserveIntentTelemetry;
        internal ConfigEntry<bool> EnableTacticalReserveListMutation;
```

- [ ] **Step 2: Bind the configs**

In the tactical config-bind block (after `EnableTacticalCommanderIntentDoctrine = Config.Bind(...)`), add:

```csharp
            EnableTacticalLocalReactionDoctrine = Config.Bind(
                "Tactical",
                "Enable Tactical Local Reaction Doctrine",
                false,
                "Default OFF for Slice B6c. Computes per-group local reactions from B6a intent + playbook + B3 evidence and emits read-only [TacticalLocalReaction] telemetry. Enables stance-4 preservation/demotion contract in BattleGroupStancePatch.");

            EnableTacticalChargeDenial = Config.Bind(
                "Tactical",
                "Enable Tactical Charge Denial",
                false,
                "Default OFF for Slice B6c. When the local reaction is DenyCharge, BattleGroupStancePatch demotes vanilla stance 4 to 3 with [TacticalChargeDeny] telemetry, and BattleChargeGatePatch denies SetMovementMode(3) at the per-unit charge initiation surface as defense in depth.");

            EnableTacticalReserveIntentTelemetry = Config.Bind(
                "Tactical",
                "Enable Tactical Reserve Intent Telemetry",
                false,
                "Default OFF for Slice B6c. Emits read-only [TacticalReserveIntent] lines aggregating LineReliefRequest signals + reserve availability per side. Does not mutate reserve lists.");

            EnableTacticalReserveListMutation = Config.Bind(
                "Tactical",
                "Enable Tactical Reserve List Mutation",
                false,
                "Default OFF for Slice B6c. Allows BattleReserveDoctrinePatch to bias objectivechain[i].reservegroups membership under snapshot/restore protection when reserve intent allows mutation. W&L ownership and stale-order gates apply.");
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
feat(tactical): bind four B6c per-reaction config flags

Default OFF: Local Reaction Doctrine, Charge Denial, Reserve Intent
Telemetry, Reserve List Mutation. Granular rollback per surface.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 3: Extend B6a observer to populate `TacticalReactionContext` and emit reaction telemetry

**Files:**
- Modify: `src/WhiskeyRealism/Patches/BattleCommanderIntentObserverPatch.cs`

- [ ] **Step 1: Add the per-group reaction loop and telemetry**

Open `src/WhiskeyRealism/Patches/BattleCommanderIntentObserverPatch.cs`. Locate the existing `Apply(AIBattle battle)` method. Replace the body with the extended version below (preserves `[TacticalIntent]`/`[TacticalPlaybook]` emission, adds reaction + reserve population and telemetry):

```csharp
        private static FieldInfo _unitsUsedField;

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

            if (!Plugin.Instance.EnableTacticalLocalReactionDoctrine.Value)
                return;

            TacticalReactionContext.Shared.Clear();

            var units = SafeList(battle, ref _unitsUsedField, "unitsused");
            var reactionList = new List<TacticalLocalReactionDecision>();
            if (units != null)
            {
                for (int i = 0; i < units.Count; i++)
                {
                    if (!(units[i] is Regiment group) || group.unittyp <= 13) continue;
                    var reactionInput = BuildReactionInput(group, intent, playbook);
                    var reaction = TacticalLocalReactionScorer.Score(reactionInput);
                    TacticalReactionContext.Shared.SetReaction(SafeInstanceId(group), reaction);
                    reactionList.Add(reaction);
                    EmitReaction(side, group, reaction);
                }
            }

            if (Plugin.Instance.EnableTacticalReserveIntentTelemetry.Value)
            {
                var availability = BuildReserveAvailability(battle);
                var reserveInput = new TacticalReserveIntentInput(playbook.ReservePolicy, reactionList.ToArray(), availability);
                var reserveDecision = TacticalReservePolicyLedger.Decide(reserveInput);
                TacticalReactionContext.Shared.SetReserveIntent(side, reserveDecision);
                EmitReserveIntent(side, reserveDecision);
            }
        }

        private static TacticalLocalReactionInput BuildReactionInput(Regiment group, TacticalIntentDecision intent, TacticalPlaybookDecision playbook)
        {
            float own = Math.Max(0f, group.groupowninrange);
            float enemy = Math.Max(0f, group.groupenemiesinrange);
            float odds = enemy <= 0f ? 0f : own / Math.Max(1f, enemy);
            bool flank = group.flanksthreated > 0f || group.outflanked > 0;
            bool strong = group.covervalue > 0.5f || group.fortinrange;
            bool targetVisible = group.unitrange != null && group.unitrange.closestenemyunitfarreg != null;
            bool targetBroken = targetVisible && (group.unitrange.closestenemyunitfarreg.morale < 0.45f || group.unitrange.closestenemyunitfarreg.markedforrout);
            bool targetStrongPoint = targetVisible && (group.unitrange.closestenemyunitfarreg.covervalue > 0.5f || group.unitrange.closestenemyunitfarreg.fortinrange);
            float morale = Mathf.Clamp01(group.morale);
            float ammo = group.maxammo > 0f ? Mathf.Clamp01(group.ammo / group.maxammo) : 0.5f;
            float casualties = Mathf.Clamp01(group.casualties / Mathf.Max(1f, group.startsize));
            bool wlSafe = WlOwnershipSafe(group);
            bool chargeReady = group.lastaichargetime + GamePrefs.timetorenewaichargecheck <= GameVars.currenttimefromstart;
            bool staleness = group.regimentpaths > 0 && group.pathinterrupted;
            bool pathRisk = false; // BUG-TAC-010 hookup is wired in a follow-up task; default false here.

            return new TacticalLocalReactionInput(
                intent: intent.Intent,
                playbookPolicy: playbook.LocalReactionPolicy,
                sectorMission: TacticalSectorMission.Hold,
                sectorOdds: odds,
                sectorConfidence: targetVisible ? 0.7f : 0.4f,
                targetVisible: targetVisible,
                targetBroken: targetBroken,
                targetStrongPoint: targetStrongPoint,
                morale01: morale,
                ammoRatio01: ammo,
                casualtyRatio01: casualties,
                flankRisk: flank,
                wlOwnershipSafe: wlSafe,
                chargeCooldownReady: chargeReady,
                stalenessActive: staleness,
                pathRiskActive: pathRisk);
        }

        private static bool WlOwnershipSafe(Regiment group)
        {
            if (group == null) return true;
            if (!DLC_WL.dlc_scenarioactive) return true;
            if (group.dlcw_isundercommander) return false;
            if (group.allattachedunits != null)
            {
                for (int i = 0; i < group.allattachedunits.Length; i++)
                {
                    var u = group.allattachedunits[i];
                    if (u != null && u.dlcw_isundercommander) return false;
                }
            }
            return true;
        }

        private static TacticalReserveAvailability BuildReserveAvailability(AIBattle battle)
        {
            int reserveCount = 0;
            bool flankRisk = false;
            bool lastIsFlankGuard = false;
            IList chain = ObjectiveChain(battle);
            if (chain != null)
            {
                for (int i = 0; i < chain.Count; i++)
                {
                    if (_reserveGroupsField == null) _reserveGroupsField = AccessTools.Field(chain[i].GetType(), "reservegroups");
                    if (_reserveGroupsField == null) continue;
                    if (_reserveGroupsField.GetValue(chain[i]) is IList reserves) reserveCount += reserves.Count;
                }
                if (chain.Count > 0)
                {
                    object first = chain[0];
                    if (_flankAnchoredField == null) _flankAnchoredField = AccessTools.Field(first.GetType(), "anchoredflank");
                    if (_flankAnchoredField != null && _flankAnchoredField.GetValue(first) is bool[] anchored)
                    {
                        flankRisk = anchored.Length >= 2 && (!anchored[0] || !anchored[1]);
                    }
                }
                lastIsFlankGuard = flankRisk && reserveCount <= 1;
            }
            return new TacticalReserveAvailability(reserveCount, flankRisk, lastIsFlankGuard, wlOwnershipSafe: true, stalenessActive: false);
        }

        private static void EmitReaction(int side, Regiment group, TacticalLocalReactionDecision reaction)
        {
            string signature = side + "|" + SafeInstanceId(group) + "|" + reaction.Reaction + "|" + reaction.Reason;
            if (!TacticalTelemetry.ShouldEmit(_lastEmittedAt, "b6c-reaction", signature, Time.realtimeSinceStartup, 30f, false))
                return;

            Plugin.Log.LogInfo("[TacticalLocalReaction] side=" + side +
                " group=" + SafeName(group) + "#" + SafeInstanceId(group) +
                " reaction=" + reaction.Reaction +
                " reliefRequested=" + (reaction.ReliefRequested ? "1" : "0") +
                " reason=" + reaction.Reason +
                " confidence=" + reaction.Confidence.ToString("0.00"));
        }

        private static void EmitReserveIntent(int side, TacticalReserveIntentDecision decision)
        {
            string signature = side + "|" + decision.Intent + "|" + decision.AllowsRuntimeMutation + "|" + decision.Reason;
            if (!TacticalTelemetry.ShouldEmit(_lastEmittedAt, "b6c-reserve-intent", signature, Time.realtimeSinceStartup, 30f, false))
                return;

            Plugin.Log.LogInfo("[TacticalReserveIntent] side=" + side +
                " intent=" + decision.Intent +
                " allowsMutation=" + (decision.AllowsRuntimeMutation ? "1" : "0") +
                " reason=" + decision.Reason +
                " confidence=" + decision.Confidence.ToString("0.00"));
        }

        private static IList SafeList(object instance, ref FieldInfo cache, string name)
        {
            try
            {
                if (instance == null) return null;
                if (cache == null) cache = AccessTools.Field(instance.GetType(), name);
                return cache != null ? cache.GetValue(instance) as IList : null;
            }
            catch
            {
                return null;
            }
        }

        private static int SafeInstanceId(UnityEngine.Object obj)
        {
            try { return obj != null ? obj.GetInstanceID() : 0; } catch { return 0; }
        }

        private static string SafeName(Regiment group)
        {
            try { return group != null ? ((Component)group).gameObject.name : "<null>"; } catch { return "<err>"; }
        }
```

- [ ] **Step 2: Build**

```bash
./build.sh
```

Expected: BUILD SUCCEEDED.

- [ ] **Step 3: Commit**

```bash
git add src/WhiskeyRealism/Patches/BattleCommanderIntentObserverPatch.cs
git commit -m "$(cat <<'EOF'
feat(tactical): wire B6c reaction + reserve telemetry into B6a observer

Per-group TacticalLocalReactionScorer evaluation populates the shared
TacticalReactionContext and emits [TacticalLocalReaction] lines.
Per-side TacticalReservePolicyLedger emits [TacticalReserveIntent].
Both gated by their own per-reaction config flags.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 4: Implement the B5 stance-4 preservation/demotion contract

**Files:**
- Modify: `src/WhiskeyRealism/Patches/BattleGroupStancePatch.cs`

- [ ] **Step 1: Replace the Apply path with the contract logic**

Open `src/WhiskeyRealism/Patches/BattleGroupStancePatch.cs`. Locate the existing `ApplyGroup(...)` method. Replace its body with the contract-aware version:

```csharp
        private static void ApplyGroup(BattleUnits bunits, int side, int macro, Regiment group, int index)
        {
            if (!WlAllowsControl(group)) return;
            if (!OrderFrictionAllowsChange(group)) return;

            int vanillaOrdered = SafeIntField(group, ref _orderedStanceField, "ai_" + "stanceordered", group.ai_stanceordered);

            // B6c stance-4 contract: vanilla just wrote stance 4 (charge).
            if (vanillaOrdered == 4)
            {
                if (!Plugin.Instance.EnableTacticalChargeDenial.Value)
                {
                    LogChargePreserved(side, group, "vanilla-charge-preserved");
                    return;
                }

                var reaction = TacticalReactionContext.Shared.GetReaction(SafeInstanceId(group));
                if (reaction.Reaction == LocalReaction.DenyCharge)
                {
                    DemoteCharge(bunits, group, side, reaction.Reason);
                    return;
                }

                LogChargePreserved(side, group, "vanilla-charge-preserved");
                return;
            }

            var sector = BuildGroupSector(group, index);
            var decision = TacticalDoctrineScorer.DecideGroupStance(new TacticalGroupStanceDecisionInput(
                vanillaOrdered,
                macro,
                sector,
                true,
                true));

            if (decision.Kind != TacticalDoctrineDecisionKind.Apply) return;
            if (decision.GroupStance == vanillaOrdered) return;
            if (decision.GroupStance == 4) return;
            if (decision.GroupStance < 0 || decision.GroupStance > 3) return;

            var gameObject = UnityObject(group);
            if (gameObject == null || !gameObject.activeInHierarchy) return;

            bunits.ChangeStance(gameObject, decision.GroupStance, immediate: false, overwriteaigroups: false);
            group.ai_stance = decision.GroupStance;
            group.ai_stanceordered = decision.GroupStance;
            group.lastaistancechangetime = GameVars.currenttimefromstart;
            LogDecision(side, group, sector, decision);
        }

        private static void DemoteCharge(BattleUnits bunits, Regiment group, int side, string reason)
        {
            var gameObject = UnityObject(group);
            if (gameObject == null || !gameObject.activeInHierarchy) return;

            bunits.ChangeStance(gameObject, 3, immediate: false, overwriteaigroups: false);
            group.ai_stance = 3;
            group.ai_stanceordered = 3;
            group.lastaistancechangetime = GameVars.currenttimefromstart;

            string signature = side + "|" + SafeInstanceId(group) + "|" + reason;
            if (!TacticalTelemetry.ShouldEmit(_lastLoggedAt, "b6c-charge-deny-stance", signature, Time.realtimeSinceStartup, 30f, false))
                return;

            Plugin.Log.LogInfo("[TacticalChargeDeny] surface=stance side=" + side +
                " group=" + SafeName(group) + "#" + SafeInstanceId(group) +
                " reason=" + reason);
        }

        private static void LogChargePreserved(int side, Regiment group, string reason)
        {
            string signature = side + "|" + SafeInstanceId(group) + "|" + reason;
            if (!TacticalTelemetry.ShouldEmit(_lastLoggedAt, "b6c-charge-preserved", signature, Time.realtimeSinceStartup, 60f, false))
                return;

            Plugin.Log.LogInfo("[TacticalGroupDecision] action=skip side=" + side +
                " group=" + SafeName(group) + "#" + SafeInstanceId(group) +
                " reason=" + reason);
        }

        private static string SafeName(Regiment group)
        {
            try { return group != null ? ((Component)group).gameObject.name : "<null>"; } catch { return "<err>"; }
        }
```

The reflection helpers (`SafeIntField`, `SafeField`, `SafeList`, `UnityObject`, `SafeInstanceId`) at the bottom of the file are unchanged.

- [ ] **Step 2: Build**

```bash
./build.sh
```

Expected: BUILD SUCCEEDED.

- [ ] **Step 3: Commit**

```bash
git add src/WhiskeyRealism/Patches/BattleGroupStancePatch.cs
git commit -m "$(cat <<'EOF'
fix(tactical): B5 preserves vanilla stance 4 unless DenyCharge active

Pre-existing silent demotion: when vanilla AdjustGroupAIStance wrote
stance 4 (charge) and B5's scorer returned Apply 1/2/3, B5 silently
overwrote the vanilla charge.

B6c contract: vanilla stance 4 is preserved by default. Demotion to
stance 3 happens only when Enable Tactical Charge Denial is true and
the group's reaction in TacticalReactionContext is DenyCharge, with
explicit [TacticalChargeDeny] surface=stance telemetry.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 5: Extend `BattleChargeGatePatch` (#41) for DenyCharge defense in depth

**Files:**
- Modify: `src/WhiskeyRealism/Patches/BattleChargeGatePatch.cs`

- [ ] **Step 1: Add the second deny condition**

Open `src/WhiskeyRealism/Patches/BattleChargeGatePatch.cs`. Locate the inner per-unit charge initiation block (the `if (!unit.permanentlydetached && chargeStance && ...)` block where `TacticalWlActionGuard.Decide(...)` runs). Replace the deny branch's `LogDenied(unit, aigroup, decision.Reason);` call with the extended version that also consumes B6c DenyCharge:

```csharp
                        // Existing W&L ownership branch:
                        TacticalWlGuardDecision decision = TacticalWlActionGuard.Decide(
                            configEnabled: Plugin.Instance.EnableWlTacticalChargeGuard.Value,
                            dlcScenarioActive: DLC_WL.dlc_scenarioactive,
                            action: TacticalWlGuardAction.ChargeInitiation,
                            unitUnderCommander: unit.dlcw_isundercommander,
                            groupUnderCommander: aigroup.dlcw_isundercommander,
                            attachedUnitUnderCommander: false);

                        bool b6cDeny = false;
                        string b6cDenyReason = null;
                        if (decision.Allow && Plugin.Instance.EnableTacticalChargeDenial.Value)
                        {
                            var reaction = TacticalReactionContext.Shared.GetReaction(aigroup.GetInstanceID());
                            if (reaction.Reaction == LocalReaction.DenyCharge)
                            {
                                b6cDeny = true;
                                b6cDenyReason = reaction.Reason;
                            }
                        }

                        if (decision.Allow && !b6cDeny)
                        {
                            tookOwnership = true;
                            unit.SetMovementMode(3);
                            aigroup.lastfeudactiontime = CurrentBattleHour(bunits);
                        }
                        else
                        {
                            tookOwnership = true;
                            aigroup.lastfeudactiontime = CurrentBattleHour(bunits);
                            if (b6cDeny) LogDeniedB6c(unit, aigroup, b6cDenyReason);
                            else LogDenied(unit, aigroup, decision.Reason);
                        }
```

Add the new `LogDeniedB6c` helper near the existing `LogDenied`:

```csharp
        private static void LogDeniedB6c(Regiment unit, Regiment group, string reason)
        {
            OnceLog.Info("tactical-charge-guard:b6c", "BattleChargeGatePatch B6c DenyCharge wired");
            OnceLog.Info("tactical-charge-guard:b6c-deny:" + SafeName(unit), "[TacticalChargeDeny] surface=movement reason=" + reason +
                " unit=" + SafeName(unit) +
                " group=" + SafeName(group));
        }
```

The existing `using WhiskeyRealism.Tactical;` import at the top of the file is sufficient — `TacticalReactionContext` and `LocalReaction` are in that namespace.

- [ ] **Step 2: Build**

```bash
./build.sh
```

Expected: BUILD SUCCEEDED.

- [ ] **Step 3: Commit**

```bash
git add src/WhiskeyRealism/Patches/BattleChargeGatePatch.cs
git commit -m "$(cat <<'EOF'
feat(tactical): #41 charge gate consumes B6c DenyCharge defense in depth

Second deny condition under Enable Tactical Charge Denial: if the
group's TacticalReactionContext reaction is DenyCharge, the per-unit
SetMovementMode(3) is denied even if W&L ownership allows it.

The existing W&L ownership branch is unchanged.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 6: Add `BattleReserveDoctrinePatch` with snapshot/restore

**Files:**
- Create: `src/WhiskeyRealism/Patches/BattleReserveDoctrinePatch.cs`

- [ ] **Step 1: Create the patch**

```csharp
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using WhiskeyRealism.Tactical;
using WhiskeyRealism.Util;

namespace WhiskeyRealism.Patches
{
    // B6c default-off reserve-list bias. Runs as Postfix on AIBattle.AssignReserves
    // (decompile 7017). Reads the per-side TacticalReserveIntentDecision from
    // TacticalReactionContext. Mutates objectivechain[i].reservegroups only when
    // Enable Tactical Reserve List Mutation is true and the decision allows it,
    // taking a structural snapshot of every touched list and restoring on any
    // exception. Never adds null or duplicate groups; never strips the last
    // flank-guard reserve.
    [HarmonyPatch(typeof(AIBattle), "AssignReserves")]
    internal static class BattleReserveDoctrinePatch
    {
        private static FieldInfo _objectiveChainField;
        private static FieldInfo _reserveGroupsField;
        private static FieldInfo _sideOfAiField;
        private static bool _missingAnchorLogged;

        [HarmonyPostfix]
        [HarmonyPriority(Priority.LowerThanNormal)]
        internal static void Postfix(AIBattle __instance)
        {
            if (!Enabled() || __instance == null) return;

            int side = SafeIntField(__instance, ref _sideOfAiField, "sideofai", -1);
            if (side < 0) return;

            var reserveDecision = TacticalReactionContext.Shared.GetReserveIntent(side);
            if (!reserveDecision.AllowsRuntimeMutation) return;
            if (reserveDecision.Intent == TacticalReserveIntent.None) return;

            IList chain = ObjectiveChain(__instance);
            if (chain == null || chain.Count == 0) return;

            var snapshot = SnapshotReserves(chain);
            try
            {
                ApplyBias(chain, reserveDecision);
                EnforcePostconditions(chain, snapshot);
                LogMutation(side, reserveDecision);
            }
            catch (Exception ex)
            {
                RestoreReserves(chain, snapshot);
                OnceLog.Warning("tactical-reserve-doctrine:failed",
                    "BattleReserveDoctrinePatch failed; restored snapshot: " + ex.Message);
            }
        }

        private struct ChainSnapshot
        {
            public int Index;
            public List<Regiment> Members;
        }

        private static List<ChainSnapshot> SnapshotReserves(IList chain)
        {
            var result = new List<ChainSnapshot>();
            for (int i = 0; i < chain.Count; i++)
            {
                if (_reserveGroupsField == null) _reserveGroupsField = AccessTools.Field(chain[i].GetType(), "reservegroups");
                if (_reserveGroupsField == null)
                {
                    LogMissingAnchor("reservegroups");
                    continue;
                }
                if (_reserveGroupsField.GetValue(chain[i]) is IList list)
                {
                    var copy = new List<Regiment>(list.Count);
                    for (int j = 0; j < list.Count; j++) copy.Add(list[j] as Regiment);
                    result.Add(new ChainSnapshot { Index = i, Members = copy });
                }
            }
            return result;
        }

        private static void RestoreReserves(IList chain, List<ChainSnapshot> snapshot)
        {
            try
            {
                for (int s = 0; s < snapshot.Count; s++)
                {
                    int idx = snapshot[s].Index;
                    if (idx < 0 || idx >= chain.Count) continue;
                    if (_reserveGroupsField == null) continue;
                    if (_reserveGroupsField.GetValue(chain[idx]) is IList live)
                    {
                        live.Clear();
                        for (int j = 0; j < snapshot[s].Members.Count; j++) live.Add(snapshot[s].Members[j]);
                    }
                }
            }
            catch (Exception ex)
            {
                OnceLog.Warning("tactical-reserve-doctrine:restore", "BattleReserveDoctrinePatch restore failed: " + ex.Message);
            }
        }

        private static void ApplyBias(IList chain, TacticalReserveIntentDecision decision)
        {
            // Conservative initial behavior: reorder reservegroups so the strongest
            // reserve sits first when intent is RelieveBatteredLine or
            // ExploitWeakPoint, leaving membership unchanged. This is the smallest
            // safe write that influences vanilla AI selection without adding or
            // removing reserves.
            if (decision.Intent != TacticalReserveIntent.RelieveBatteredLine &&
                decision.Intent != TacticalReserveIntent.ExploitWeakPoint)
                return;

            for (int i = 0; i < chain.Count; i++)
            {
                if (_reserveGroupsField == null) continue;
                if (!(_reserveGroupsField.GetValue(chain[i]) is IList list)) continue;
                if (list.Count <= 1) continue;

                int strongestIdx = -1;
                float bestStrength = -1f;
                for (int j = 0; j < list.Count; j++)
                {
                    if (!(list[j] is Regiment r)) continue;
                    float s = Math.Max(0f, r.groupowninrange) - Math.Max(0f, r.casualties);
                    if (s > bestStrength) { bestStrength = s; strongestIdx = j; }
                }
                if (strongestIdx > 0)
                {
                    var swap = list[0];
                    list[0] = list[strongestIdx];
                    list[strongestIdx] = swap;
                }
            }
        }

        private static void EnforcePostconditions(IList chain, List<ChainSnapshot> snapshot)
        {
            for (int i = 0; i < chain.Count; i++)
            {
                if (_reserveGroupsField == null) continue;
                if (!(_reserveGroupsField.GetValue(chain[i]) is IList list)) continue;

                int beforeCount = -1;
                for (int s = 0; s < snapshot.Count; s++)
                    if (snapshot[s].Index == i) { beforeCount = snapshot[s].Members.Count; break; }
                if (beforeCount >= 0 && list.Count != beforeCount)
                    throw new InvalidOperationException("reservegroups count changed at chain " + i);

                for (int j = 0; j < list.Count; j++)
                {
                    if (list[j] == null)
                        throw new InvalidOperationException("null reserve member at chain " + i + " index " + j);
                    if (list[j] is Regiment r && r.dlcw_isundercommander)
                        throw new InvalidOperationException("player-subordinate reserve at chain " + i + " index " + j);
                    for (int k = j + 1; k < list.Count; k++)
                        if (ReferenceEquals(list[j], list[k]))
                            throw new InvalidOperationException("duplicate reserve at chain " + i);
                }
            }
        }

        private static void LogMutation(int side, TacticalReserveIntentDecision decision)
        {
            OnceLog.Info("tactical-reserve-doctrine:wired", "BattleReserveDoctrinePatch wired");
            Plugin.Log.LogInfo("[TacticalReserveMutation] side=" + side +
                " intent=" + decision.Intent +
                " reason=" + decision.Reason);
        }

        private static IList ObjectiveChain(AIBattle battle)
        {
            if (_objectiveChainField == null) _objectiveChainField = AccessTools.Field(typeof(AIBattle), "objective" + "chain");
            if (_objectiveChainField == null) { LogMissingAnchor("objectivechain"); return null; }
            return _objectiveChainField.GetValue(battle) as IList;
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

        private static void LogMissingAnchor(string anchor)
        {
            if (_missingAnchorLogged) return;
            _missingAnchorLogged = true;
            OnceLog.Warning("tactical-reserve-doctrine:missing-anchor:" + anchor,
                "BattleReserveDoctrinePatch missing required anchor " + anchor + "; running inert");
        }

        private static bool Enabled()
        {
            return Plugin.Instance != null &&
                Plugin.Instance.Enabled.Value &&
                Plugin.Instance.EnableTacticalObserver.Value &&
                Plugin.Instance.EnableTacticalLocalReactionDoctrine.Value &&
                Plugin.Instance.EnableTacticalReserveListMutation.Value;
        }
    }
}
```

- [ ] **Step 2: Build**

```bash
./build.sh
```

Expected: BUILD SUCCEEDED.

- [ ] **Step 3: Commit**

```bash
git add src/WhiskeyRealism/Patches/BattleReserveDoctrinePatch.cs
git commit -m "$(cat <<'EOF'
feat(tactical): add B6c BattleReserveDoctrinePatch with snapshot restore

Default-off Postfix on AIBattle.AssignReserves. Reorders objectivechain
reservegroups so the strongest reserve sits first when intent is
RelieveBatteredLine or ExploitWeakPoint. Conservative bias: no add, no
remove, no null, no duplicate, no player-subordinate. Snapshot taken
before mutation; postcondition violation triggers restore.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 7: Build, deploy, hash verify

**Files:** none modified.

- [ ] **Step 1: Confirm GTCW is closed**

Windows holds an exclusive lock on the deployed DLL while the game is running. Close GTCW first.

- [ ] **Step 2: Build, deploy, hash**

```bash
./build.sh
cp dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/"
stat -c '%y %s %n' dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/WhiskeyRealism.dll"
sha256sum dist/WhiskeyRealism.dll "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/plugins/WhiskeyRealism.dll"
```

Expected: 0 errors, 0 warnings, both timestamps match, both SHA-256 hashes match. If `cp` fails with `Invalid argument`, GTCW is still running.

Record the hash for Task 8 catalog updates.

- [ ] **Step 3: Run console harness**

```bash
dotnet run --project tests/WhiskeyRealism.Tests/WhiskeyRealism.Tests.csproj
```

Expected: every test passes including the three B6c reaction-context tests.

---

## Task 8: Update patch catalog and handoff

**Files:**
- Modify: `docs/patch-catalog.md`, `docs/handoff.md`

- [ ] **Step 1: Update #45 row (B5)**

Find the existing #45 `BattleGroupStancePatch` row. Append a sentence to the description (do not replace the existing text):

> v0.3.x B6c contract: vanilla `ai_stanceordered == 4` is preserved by default and is demoted to stance 3 with `[TacticalChargeDeny] surface=stance` only when `Enable Tactical Charge Denial` is true and the group's `TacticalReactionContext` reaction is `DenyCharge`. The pre-existing silent-demotion path is removed.

- [ ] **Step 2: Update #41 row (B1 charge gate)**

Find the existing #41 `BattleChargeGatePatch` row. Append a sentence:

> v0.3.x B6c extension: a second deny condition consumes `TacticalReactionContext` reaction state under `Enable Tactical Charge Denial`. When that flag is true and the group's reaction is `DenyCharge`, per-unit `SetMovementMode(3)` is denied as defense-in-depth and `[TacticalChargeDeny] surface=movement` is logged. The existing W&L ownership branch is unchanged.

- [ ] **Step 3: Update #47 row (B6a observer)**

Append:

> Slice B6c extends this observer with per-group `TacticalLocalReactionScorer` evaluation populating `TacticalReactionContext`, plus per-side `TacticalReservePolicyLedger` aggregation, emitting `[TacticalLocalReaction]` and `[TacticalReserveIntent]` lines under `Enable Tactical Local Reaction Doctrine` and `Enable Tactical Reserve Intent Telemetry`.

- [ ] **Step 4: Add new ordinal for `BattleReserveDoctrinePatch`**

Pick the next free ordinal (likely `48`). Insert in numeric order; replace `<sha>` with the hash from Task 7:

```markdown
| 48 | `BattleReserveDoctrinePatch` | Postfix | `Patches/BattleReserveDoctrinePatch.cs` | `AIBattle.AssignReserves` (7017) | Slice B6c default-off reserve-list bias. Reads per-side `TacticalReserveIntentDecision` from `TacticalReactionContext` and reorders `objectivechain[i].reservegroups` so the strongest reserve sits first when intent is `RelieveBatteredLine` or `ExploitWeakPoint`. Membership is never added to or removed from; null, duplicate, and player-subordinate entries are postcondition-checked and trigger snapshot restore. Gated by `Enable Tactical Reserve List Mutation`. Build/deploy/hash verified in DLL `<sha>`. Runtime smoke pending. |
```

- [ ] **Step 5: Update handoff**

Append a "What just shipped" bullet (replace `<sha>` with the hash from Task 7):

```markdown
- **B6c tactical runtime application:** added `Tactical/TacticalReactionContext.cs` shared cache and `Patches/BattleReserveDoctrinePatch.cs` (#48). Extended `Patches/BattleCommanderIntentObserverPatch.cs` (#47) to populate the cache and emit `[TacticalLocalReaction]` + `[TacticalReserveIntent]` telemetry. Fixed pre-existing silent stance-4 demotion in `Patches/BattleGroupStancePatch.cs` (#45): vanilla charge stances are preserved by default and demoted only under `Enable Tactical Charge Denial` + `TacticalReactionContext` `DenyCharge`, with `[TacticalChargeDeny] surface=stance` telemetry. Extended `Patches/BattleChargeGatePatch.cs` (#41) with the same DenyCharge state as defense-in-depth at the per-unit charge initiation surface (`[TacticalChargeDeny] surface=movement`). Four new default-off configs: Local Reaction Doctrine, Charge Denial, Reserve Intent Telemetry, Reserve List Mutation. Build/deploy/hash verified in DLL `<sha>`; in-game smoke pending.
```

- [ ] **Step 6: Commit docs**

```bash
git add docs/patch-catalog.md docs/handoff.md
git commit -m "$(cat <<'EOF'
docs(tactical): catalog #48 B6c reserve doctrine and B5/#41 contract updates

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 9: Focused B6c smoke (charge contract first, no reserve mutation)

**Files:** none modified (smoke is in-game).

- [ ] **Step 1: Configure focused smoke**

User edits `<GTCW>/BepInEx/config/dev.kyle.whiskey-realism.cfg`. Set:

```ini
[Tactical]
Enable Tactical Observer = true
Enable Tactical Macro Stance Scorer = true
Enable Tactical Group Sector Stance = true
Enable Tactical Commander Intent Doctrine = true
Enable Tactical Local Reaction Doctrine = true
Enable Tactical Charge Denial = true
Enable Tactical Reserve Intent Telemetry = true
Enable Tactical Reserve List Mutation = false
Enable W&L Tactical Charge Guard = true
```

- [ ] **Step 2: Launch GTCW, start a W&L land battle (player-subordinate attached if possible)**

Tail the log:

```bash
tail -f "/mnt/c/Program Files (x86)/Steam/steamapps/common/Grand Tactician The Civil War (1861-1865)/BepInEx/LogOutput.log" | grep -E "TacticalIntent|TacticalPlaybook|TacticalLocalReaction|TacticalReserveIntent|TacticalChargeDeny|TacticalGroupDecision|tactical-b6a-observer|tactical-charge-guard|tactical-reserve-doctrine"
```

- [ ] **Step 3: Verify markers**

Within 2-3 minutes of land-battle play:

- `[TacticalIntent]`, `[TacticalPlaybook]`, `[TacticalLocalReaction]`, `[TacticalReserveIntent]` appear without exception spam.
- When the AI side reaches macro 0 or 1 and a group passes the vanilla strength threshold, expect either `[TacticalGroupDecision] action=skip ... reason=vanilla-charge-preserved` (PermitCharge default) or `[TacticalChargeDeny] surface=stance ...` followed by `[TacticalChargeDeny] surface=movement ...` if a follow-up charge initiation also denied.
- No `[once:tactical-reserve-doctrine:wired]` line (mutation disabled in this run).
- No `[once:tactical-b6a-observer:failed]`, `[once:tactical-charge-guard:b6c]` failure markers, or repeated exceptions.
- ProbeIntent never produces `PermitCharge`.
- W&L player-subordinate groups never receive a B6c stance-4 demotion (W&L safety still gates the charge surface).

If any unexpected marker fires, capture the line and disable the relevant flag before iterating.

- [ ] **Step 4: Update handoff with smoke evidence**

Replace "in-game smoke pending" in handoff with observed counts and timestamp:

```bash
git add docs/handoff.md docs/patch-catalog.md
git commit -m "$(cat <<'EOF'
chore(tactical): record B6c charge contract smoke evidence

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 10: Reserve mutation focused smoke

**Files:** none modified.

- [ ] **Step 1: Enable reserve mutation**

```ini
Enable Tactical Reserve List Mutation = true
```

Leave all other flags from Task 9 enabled.

- [ ] **Step 2: Launch and play through a battle that develops reserves**

Pick a battle where both sides actually accumulate reserves (typical W&L land scenarios). Tail the log for `[TacticalReserveMutation]`, `[once:tactical-reserve-doctrine:wired]`, and `[once:tactical-reserve-doctrine:failed]`.

- [ ] **Step 3: Verify postconditions**

Expected:

- `[once:tactical-reserve-doctrine:wired]` fires once.
- `[TacticalReserveMutation]` lines appear when intent is `RelieveBatteredLine` or `ExploitWeakPoint`.
- No `[once:tactical-reserve-doctrine:failed]` warning.
- No `[once:tactical-reserve-doctrine:restore]` warning unless paired with a meaningful exception trace.
- No vanilla AI behavior regression compared to a control run with `Enable Tactical Reserve List Mutation = false`.

If any postcondition violation fires, the snapshot restore handles it but you should still investigate before re-enabling.

- [ ] **Step 4: Update handoff**

```bash
git add docs/handoff.md docs/patch-catalog.md
git commit -m "$(cat <<'EOF'
chore(tactical): record B6c reserve mutation smoke evidence

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Rollback

Rollback is config-first and per-reaction:

- `Enable Tactical Reserve List Mutation = false` removes reserve-list writes (telemetry preserved).
- `Enable Tactical Charge Denial = false` removes the charge-deny path; vanilla charges resume.
- `Enable Tactical Reserve Intent Telemetry = false` removes `[TacticalReserveIntent]` lines.
- `Enable Tactical Local Reaction Doctrine = false` removes B6c per-group reaction evaluation; the B6a `[TacticalIntent]` and `[TacticalPlaybook]` telemetry continues.
- `Enable Tactical Commander Intent Doctrine = false` removes all B6 telemetry.

If config rollback is insufficient (e.g., a build-time regression in the patch files), revert in this order:

1. `src/WhiskeyRealism/Patches/BattleReserveDoctrinePatch.cs` (file-level revert).
2. `src/WhiskeyRealism/Patches/BattleChargeGatePatch.cs` B6c diff hunk.
3. `src/WhiskeyRealism/Patches/BattleGroupStancePatch.cs` B6c diff hunk (this restores the pre-existing silent demotion — accept that as a temporary regression).
4. `src/WhiskeyRealism/Patches/BattleCommanderIntentObserverPatch.cs` B6c diff hunk.

Pure types (`TacticalReactionContext`, `TacticalLocalReactionScorer`, `TacticalReservePolicyLedger`) stay; no consumer references them when the patches are reverted.

## Smoke Expectations Summary

| Marker | When |
|---|---|
| `[TacticalIntent]` | Always with B6a doctrine flag on |
| `[TacticalPlaybook]` | Always with B6a doctrine flag on |
| `[TacticalLocalReaction]` | With B6c local reaction flag on |
| `[TacticalReserveIntent]` | With B6c reserve telemetry flag on |
| `[TacticalChargeDeny] surface=stance` | When B6c charge denial flag on AND vanilla wrote stance 4 AND B6 reaction is DenyCharge |
| `[TacticalChargeDeny] surface=movement` | Same conditions, secondary defense-in-depth at per-unit charge initiation |
| `[TacticalGroupDecision] action=skip ... reason=vanilla-charge-preserved` | Vanilla wrote stance 4 AND B6 reaction is not DenyCharge AND charge denial flag on |
| `[TacticalReserveMutation]` | With B6c reserve mutation flag on AND intent is RelieveBatteredLine/ExploitWeakPoint |
| `[once:tactical-reserve-doctrine:failed]` | Postcondition violation triggered snapshot restore — investigate |
