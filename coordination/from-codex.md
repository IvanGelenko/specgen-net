# Codex findings: sheet boundaries

Control: 050-NG001-LS016C-NN, snapshot a6de03ad87ba40e7a1e49e118136094d, symbolScale=10, quarterTurns=0, flange hardware included, breakpoints respected.

Independent stage instrumentation (temporary source only):
- Root has 302 eligible cuts.
- Initial recursive partition: 45 / 46 / 46 / 41 / 4 source objects.
- Packing result: 49 / 87 / 46 = 3 sheets, 182 objects total.
- No continuation or final-centering capacity failure was observed; transitions did not cause additional splits in this run.
- Earlier expansion from 6 to 12 balanced cuts still produced 3 sheets.
- A separate bounded probe checked six other root cuts (ordered indexes 12, 18, 24, 30, 36, 42). None passed both fragment fitting and paired continuation fitting. Probe intentionally stopped after six candidates; exit code 1 with PROBE COMPLETE is its stop marker, not a new production failure.

Conclusion: 3 sheets is the best verified result so far, not a proof of the global minimum. Blindly removing the recursive break or increasing cut counts has no demonstrated benefit. No additional production changes applied.

Artifacts: C:\Users\Gelenko\AppData\Local\Temp\specgen-audit-7c43229182fc480490a89ded5c0a732c\stages.log and alternative.log; stage scene JSONs in the same directory. Current source in this temporary directory contains diagnostic instrumentation and must not be copied to production.

Antigravity remains idle; no further runs requested. A future improvement must target a specific failed fragment layout and show a complete 2-sheet result without weakening collision or integrity checks. DWG/PDF validation remains pending.