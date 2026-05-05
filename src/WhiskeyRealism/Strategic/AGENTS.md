# Strategic And Tactical Logic Instructions

These rules apply to non-patch logic under `src/WhiskeyRealism/Strategic/` and future pure tactical logic.

## Design Rules

- Keep scorer, ledger, doctrine, and catalog logic pure when possible.
- Do not reference Unity or vanilla game types from pure ledger/scorer classes.
- Put reflection-heavy extraction in runtime adapter classes, not pure logic.
- State writes belong in coordinator cadence/event handlers, not Harmony patches.
- Prefer deterministic inputs/outputs that can be covered by the console test harness.
- Use small DTOs for telemetry signatures so logs can be bounded and compared.

## Cadence Rules

- Daily ledgers must signature-skip unchanged input.
- Event-triggered dirty-plan work must stay idempotent.
- Runtime adapters should degrade to empty/no-op inputs on reflection failure and log a bounded warning.

## Tests

- Add pure tests for new scorer thresholds, ledger outputs, cooldown behavior, and edge cases.
- Include alliance bounds, empty input, stale input, and overmatch/undermatch cases when relevant.
- If the logic feeds a patch, test the pure decision before patch wiring.
