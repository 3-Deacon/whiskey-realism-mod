# Regenerates the coverage matrix of docs/loop/foresight/test-plan.md from the spec's requirement lines (derived, never typed).
import re, sys
root = sys.argv[1]
spec = 'docs/superpowers/specs/2026-10-14-campaign-planner-design.md'
tp = root + '/docs/loop/foresight/test-plan.md'
cases = {
 'requirement-enemy-total': 'TC-PIC-UNKNOWN, TC-PIC-CONSERVE',
 'requirement-picture-partition': 'TC-PIC-ONCE, TC-PIC-FADE, TC-PIC-EXPIRE, TC-PIC-ANCHORS, TC-PIC-DISPLACE, TC-PIC-SUM, TC-PIC-CONSERVE',
 'requirement-hold-need': 'TC-HOLD-RATIO, TC-HOLD-165, TC-HOLD-FLOOR, TC-HOLD-FACE, TC-HOLD-DEPT, TC-HOLD-D9',
 'requirement-hold-release': 'TC-REL-EVERY, TC-REL-NOTHING',
 'requirement-candidate-floor-capacity': 'TC-ELIG-MIN, TC-ELIG-ODDS',
 'requirement-marks-player-side': 'TC-MARK-NEWEST, TC-MARK-BAND, TC-MARK-SMOKE',
 'requirement-marks-ai-side': 'TC-MARK-CHANCE, TC-MARK-50, TC-MARK-OFFSET, TC-MARK-SEED, TC-MARK-SMOKE',
 'requirement-mark-beliefs': 'TC-MARK-KEY, TC-MARK-NORETIRE, TC-MARK-SAMEDAY, TC-MARK-NOSCOUT, TC-MARK-SIDECAR',
 'requirement-capital-window': 'TC-CAP-REACH, TC-CAP-WASH, TC-CAP-RAILS, TC-CAP-SMOKE',
 'requirement-capital-floor': 'TC-CAP-BASE, TC-CAP-THREAT, TC-CAP-VSFOUR, TC-CAP-SMOKE',
 'requirement-covering-post': 'TC-COV-CORRIDOR, TC-COV-FWD, TC-COV-REAR, TC-COV-NONE, TC-COV-CENSUS',
 'requirement-covering-size': 'TC-COV-NEED, TC-COV-MIN',
 'requirement-covering-members': 'TC-COV-INCUMBENT, TC-COV-NEAREST, TC-COV-WL, TC-COV-BUSY, TC-COV-SECTOR',
 'requirement-covering-post-choice': 'TC-COV-VERDICT',
 'requirement-covering-hold': 'TC-COV-MASSFOR, TC-COV-POOLS, TC-COV-ROLE, TC-COV-DEFENCE, TC-COV-SMOKE',
 'requirement-covering-lifecycle': 'TC-COV-UNBIND, TC-COV-SIDECAR',
 'requirement-redeploy-role': 'TC-RED-PRIORITY, TC-RED-TEMPO, TC-RED-WITHHOLD',
 'requirement-department-reinforcement': 'TC-DEP-ONE, TC-DEP-DONOR, TC-DEP-LIFE, TC-DEP-SMOKE',
 'requirement-commitment': 'TC-COM-DIP, TC-COM-RELIEF, TC-COM-CHAIN, TC-COM-SMOKE',
 'requirement-commitment-mover': 'TC-COM-NOMARCH, TC-COM-WAIT',
 'requirement-return-home': 'TC-RET-SEND, TC-RET-LIFE, TC-RET-CAP, TC-RET-SMOKE',
 'requirement-depot-builder': 'TC-DEPOT-READY, TC-DEPOT-SURPLUS, TC-DEPOT-AOV, TC-DEPOT-SMOKE',
 'requirement-live-min-strength': 'TC-MIN-PUBLISH, TC-MIN-ONESITE, TC-MIN-SMOKE',
 'requirement-interception': 'TC-INT-MODE, TC-INT-NOREMOVE, TC-INT-SMOKE',
 'requirement-telemetry': 'TC-TEL-ISSUE, TC-TEL-TRACE, TC-TEL-RESERVES, TC-TEL-REASONS, TC-TEL-TAGS',
}
lines = open(root + '/' + spec).read().split('\n')
rows = []
for i, l in enumerate(lines, 1):
    m = re.search(r'\*\*(requirement-[a-z0-9-]+)\.\*\*', l)
    if m:
        r = m.group(1)
        rows.append(f"| {r} | {spec}:{i} | {cases[r]} |  |  |")
missing = set(cases) - {r.split(' | ')[0][2:] for r in rows}
assert not missing, missing
table = "| Requirement | Spec | Test cases | Result | Defect or concession |\n|---|---|---|---|---|\n" + "\n".join(rows)
s = open(tp).read()
a, b = '<!-- matrix:begin -->', '<!-- matrix:end -->'
s = s[:s.index(a) + len(a)] + "\n" + table + "\n" + s[s.index(b):]
open(tp, 'w').write(s)
print(len(rows), "rows")
