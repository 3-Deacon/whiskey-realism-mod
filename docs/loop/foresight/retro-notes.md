# Strategic foresight: lessons for the retro

Recorded 2026-10-18, at the end of the phase-2 dry runs, for `methodology-retro` to fold into permanent structure.

## Phase-2 dry-run counts by pass

| Pass | Blockers | Majors | Minors | Gap file |
|---|---|---|---|---|
| 5b | 2 | 15 | 3 | `gaps-pass5b.md` |
| 6b | 1 | 13 | 5 | `gaps-pass6b.md` |
| 7b | 0 | 8 | 10 | `gaps-pass7b.md` |
| 8b | 0 | 9 | 9 | `gaps-pass8b.md` |
| 9b | 0 | 7 | 10 | `gaps-pass9b.md` |
| 10b | 0 | 8 | 12 | `gaps-pass10b.md` |
| 11b | 0 | 8 | 10 | `gaps-pass11b.md` |
| 12b | 0 | 7 | 10 | `gaps-pass12b.md` |

Pass 5b's header records only its total; its row is the `Severity:` lines of `gaps-pass5b.md` tallied by value (read: `grep -oiE 'Severity: *(blocker|major|minor)'` over the file, 2026-10-18).

## Lessons

1. **The dry-run task was too large.** Each pass was briefed as "all of phase 2": five slices, the ownership table and three lifecycles, in one seat. In every pass the seat hit its 20-gap cap or came close. Each fold then opened new edges in a neighbouring slice, which the next pass found. Candidate rule: brief one dry run per slice, or per element (ownership table, covering force, commitment, department reinforcement), with the elements already crit-passed named as fixed inputs.
2. **The method sets no pass budget.** The precondition "a pass with no blocker and no major" has no stop clause. Majors held between 7 and 9 for six passes (7b-12b) while the design changed under each fold. The user ended the loop by review, not by rule. Candidate rule: after two passes with no fall in majors, stop folding gap by gap. Escalate a simplification to the integrator (as passes 9b-11b did late), or send the artifact to crit with the open gaps routed as questions and a build-time rule.
3. **Majors at a plateau were mostly fold side-effects, not residue.** From 9b on, most majors were new edges that the previous round's decisions opened: an end rule that contradicted an old test, a lock that could not be read at plan time, a load that dropped state. Few were gaps left over from an earlier pass. A pass that re-checks the folds would have caught them for less than a full re-walk. Candidate rule: after a fold, run a targeted check of the folded requirements and their readers, and save the full walk for every other round.
