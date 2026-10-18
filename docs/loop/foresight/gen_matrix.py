# Regenerates the coverage matrix of docs/loop/foresight/test-plan.md: spec lines derived from the requirement lines; the case mapping is maintained here.
import re, sys
root = sys.argv[1]
spec = 'docs/superpowers/specs/2026-10-14-campaign-planner-design.md'
tp = root + '/docs/loop/foresight/test-plan.md'
cases = {
 'requirement-enemy-total': 'TC-PIC-UNKNOWN, TC-PIC-CONSERVE, TC-NOTFROZEN',
 'requirement-picture-partition': 'TC-PIC-ONCE, TC-PIC-BUILD, TC-PIC-SHARES, TC-PIC-ELEMENTS, TC-PIC-FADE, TC-PIC-EXPIRE, TC-PIC-ANCHORS, TC-PIC-DISPLACE, TC-PIC-SUM, TC-PIC-CONSERVE',
 'requirement-hold-need': 'TC-HOLD-RATIO, TC-HOLD-ASSIGN, TC-HOLD-EASTDONOR, TC-HOLD-SECESSION, TC-HOLD-165, TC-HOLD-FLOOR, TC-HOLD-FACE, TC-HOLD-DEPT, TC-HOLD-D9',
 'requirement-hold-release': 'TC-REL-EVERY, TC-REL-NOTHING',
 'requirement-candidate-floor-capacity': 'TC-ELIG-MIN, TC-ELIG-ODDS',
 'requirement-marks-player-side': 'TC-MARK-NEWEST, TC-MARK-BAND, TC-MARK-READERS, TC-MARK-SMOKE',
 'requirement-marks-ai-side': 'TC-MARK-CHANCE, TC-MARK-50, TC-MARK-OFFSET, TC-MARK-SEED, TC-MARK-SEEDFIRST, TC-MARK-FIDELITY, TC-MARK-BOUND, TC-FIX-TIMING, TC-FIX-RATE, TC-FIX-SMOKE, TC-MARK-SMOKE',
 'requirement-mark-beliefs': 'TC-MARK-KEY, TC-MARK-NORETIRE, TC-MARK-SAMEDAY, TC-MARK-REVERSE, TC-MARK-NOSCOUT, TC-MARK-READERS, TC-MARK-ROW, TC-MARK-SIDECAR',
 'requirement-capital-window': 'TC-CAP-REACH, TC-CAP-WASH, TC-CAP-RAILS, TC-CAP-START, TC-CAP-SMOKE',
 'requirement-capital-floor': 'TC-CAP-BASE, TC-CAP-THREAT, TC-CAP-VSFOUR, TC-CAP-SMOKE',
 'requirement-covering-post': 'TC-COV-POINTS, TC-COV-POST, TC-COV-NONE, TC-COV-CENSUS',
 'requirement-covering-size': 'TC-COV-AXIS, TC-COV-NEED, TC-COV-MIN',
 'requirement-covering-members': 'TC-COV-INCUMBENT, TC-COV-NEAREST, TC-COV-AXIS, TC-COV-WL, TC-COV-BUSY, TC-COV-SECTOR',
 'requirement-covering-post-choice': 'TC-COV-VERDICT',
 'requirement-covering-hold': 'TC-COV-MASSFOR, TC-COV-POOLS, TC-COV-ROLE, TC-COV-DEFENCE, TC-COV-SMOKE',
 'requirement-covering-lifecycle': 'TC-COV-UNBIND, TC-COV-SIDECAR',
 'requirement-redeploy-role': 'TC-RED-PRIORITY, TC-RED-TEMPO, TC-RED-WITHHOLD, TC-RED-DELIST, TC-RED-GIVEUP, TC-RED-READERS',
 'requirement-claims': 'TC-CLAIM-ROWS, TC-CLAIM-COUNTED, TC-CLAIM-ONE, TC-CLAIM-SHIPPED, TC-COM-CLAIMS, TC-DEP-CLAIMS, TC-CLAIM-SMOKE',
 'requirement-department-reinforcement': 'TC-DEP-ONE, TC-DEP-HAVE, TC-DEP-LOCK, TC-DEP-DONOR, TC-DEP-LIFE, TC-DEP-CLAIMS, TC-DEP-SMOKE',
 'requirement-commitment': 'TC-COM-DIP, TC-COM-KEY, TC-COM-MASS, TC-COM-ENDS, TC-COM-RELIEF, TC-COM-CLAIMS, TC-COM-TRACE, TC-COM-SMOKE',
 'requirement-commitment-mover': 'TC-COM-NOMARCH, TC-COM-WAIT, TC-COM-DELIST, TC-RED-DELIST',
 'requirement-decision-end': 'TC-END-PLACED, TC-COM-DELIST, TC-END-SMOKE',
 'requirement-depot-builder': 'TC-DEPOT-READY, TC-DEPOT-SURPLUS, TC-DEPOT-AOV, TC-DEPOT-SMOKE',
 'requirement-live-min-strength': 'TC-MIN-PUBLISH, TC-MIN-ONESITE, TC-MIN-PICTUREOFF, TC-MIN-READERS, TC-MIN-SMOKE',
 'requirement-interception': 'TC-INT-MODE, TC-INT-NOREMOVE, TC-INT-SMOKE',
 'requirement-telemetry': 'TC-TEL-ISSUE, TC-TEL-TRACE, TC-TEL-RESERVES, TC-TEL-REASONS, TC-TEL-TAGS, TC-TEL-DATE, TC-TEL-PICK, TC-TEL-SHARED',
}
tps = open(tp).read()
scen = tps[tps.index('## Scenarios and cases'):tps.index('## Coverage matrix')]
absent = sorted({c.strip() for v in cases.values() for c in v.split(',')} - set(re.findall(r'TC-[A-Z0-9-]+', scen)))
assert not absent, ('case ids missing from the scenarios', absent)
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
