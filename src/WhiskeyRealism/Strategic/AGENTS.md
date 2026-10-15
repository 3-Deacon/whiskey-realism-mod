# Strategic Logic Instructions

These rules apply to non-patch logic under `src/WhiskeyRealism/Strategic/`. Identical rules apply to tactical-orchestrator code under `src/WhiskeyRealism/Tactical/` — a sibling [`src/WhiskeyRealism/Tactical/AGENTS.md`](../Tactical/AGENTS.md) mirrors this file so path-based `AGENTS.md` discovery picks it up regardless of which subtree a session opens in.

## Design Rules

- Keep scorer, ledger, doctrine, and catalog logic pure when possible.
- Do not reference Unity or vanilla game types from pure ledger/scorer classes.
- Put reflection-heavy extraction in runtime adapter classes, not pure logic.
- State writes belong in coordinator cadence/event handlers, not Harmony patches.
- Prefer deterministic inputs/outputs that can be covered by the console test harness.
- Use focused DTOs for telemetry signatures so logs can be bounded and compared.

## Layout Notes

- `Policies/` holds the stage E need-driven policy selection: pure `PolicyEffectCatalog`, `PolicyEligibility`, `PolicyNeedScorer`, `PolicyPlanLedger`, `PolicyListOverride`, and the runtime adapter `PolicyRuntime`. The needs come from `CampaignNeedBuilder` (pure) / `CampaignNeedRuntime` (runtime) in this folder and are shared with project selection. Inside `WhiskeyRealism.Strategic.*` the vanilla `Policies` class must be written `global::Policies` (the namespace shadows it).

## Cadence Rules

- Daily ledgers must signature-skip unchanged input.
- Event-triggered dirty-plan work must stay idempotent.
- Runtime adapters should degrade to empty/no-op inputs on reflection failure and log a bounded warning.
- Per-campaign static state registers its own reset with `CampaignScope.Register(name, reset)` from the owner's static constructor (`Util/ScopedState.cs`); `StrategicCoordinator.ResetAll()` runs `CampaignScope.ResetAll()` on campaign change and before a sidecar load. Never hand-wire a reset into a teardown path. State that must survive a save/load goes into the sidecar DTO as well.
- Weekly/alternating gates must catch up when fast-forward skips days; monthly work runs on the real month boundary.

## Static Checks

- `python3 -I tools/orphan-sweep.py .` — no member without a production reference (`--include-test-only` also lists members only tests reach).
- `python3 -I tools/config-audit.py .` — every config entry read, bound once, default on, description matching code.

## Tests

- Add pure tests for new scorer thresholds, ledger outputs, cooldown behavior, and edge cases.
- Include alliance bounds, empty input, stale input, and overmatch/undermatch cases when relevant.
- If the logic feeds a patch, test the pure decision before patch wiring.
