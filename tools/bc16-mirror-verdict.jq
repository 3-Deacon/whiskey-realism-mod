# BC16 decision 9: reads TacticalLayoutMirror rows; prints the per-depth totals and the verdict.
[.[] | select(.event=="TacticalLayoutMirror") | .fields] as $r
| def tot(f): (map(select(f)) | {rows: length, brigades: (map(.brigades) | add // 0), agree: (map(.agree) | add // 0), reversed: (map(.reversed) | add // 0)});
  ($r | tot(.depth==1)) as $d1 | ($r | tot(.depth==2)) as $d2 | ($r | tot(.depth==3)) as $d3 | ($r | tot(.depth==1 or .depth==2)) as $d12
| def share(a; b): if b == 0 then 0 else a / b end;
  { depth1: $d1, depth2: $d2, depth3: $d3,
    agree12: share($d12.agree; $d12.brigades), reversed12: share($d12.reversed; $d12.brigades),
    agree3: share($d3.agree; $d3.brigades), reversed3: share($d3.reversed; $d3.brigades) } as $s
| ($d1.rows >= 10 and $d2.rows >= 10 and $d3.rows >= 10) as $enough
| $s + { verdict:
    (if ($enough | not) then "inconclusive: fewer than 10 rows at a depth"
     elif (($s.agree12 >= 0.8 and $s.reversed3 >= 0.8) or ($s.reversed12 >= 0.8 and $s.agree3 >= 0.8)) then "mirror confirmed: build A4"
     elif (($s.agree12 >= 0.8 and $s.agree3 >= 0.8) or ($s.reversed12 >= 0.8 and $s.reversed3 >= 0.8)) then "not reproduced: close BUG-TAC-026"
     else "inconclusive: neither pattern at 80 %" end) }
