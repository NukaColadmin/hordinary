| Modele | Ablation | Compilation | Forme | Exacte | Refus correct | Faux acceptes | Latence p50/p95 | Cout/instr. |
|---|---|---|---|---|---|---|---|---|
| `deepseek/deepseek-v4.1-flash` | aucune, heldout.jsonl, 3 tirages | 94 % | 94 % | 86 % | 76 % | **0 %** | 785 / 1402 ms | 0.00007 $ |

### `deepseek/deepseek-v4.1-flash` — aucune, heldout.jsonl, 3 tirages

- (1/3) traduction differente `ecart-05` — `require_before(diversion_made, OpenDoor)` (attendu : `require_before(diversion_made, enter:building)`)
- (1/3) non traduite `ecart-05` — « Faites du bruit loin de l'entrée avant de passer la porte d'un bâtiment. » → refus `not_expressible` (attendu : `require_before(diversion_made, enter:building)`)
- (1/3) traduction differente `ecart-07` — `require_before(known(objective_spot), ReachExtraction)` (attendu : `require_before(objective_found, ReachExtraction)`)
- (1/3) traduction differente `ecart-07` — `require_before(known(extraction), ReachExtraction)` (attendu : `require_before(objective_found, ReachExtraction)`)
- (1/3) traduction differente `ecart-07` — `require_before(known(cover), ReachExtraction)` (attendu : `require_before(objective_found, ReachExtraction)`)
- (1/3) non traduite `ecart-10` — « Ne quittez jamais vos camarades. » → refus `unknown_symbol` (attendu : `require_always(at(agent, group))`)
- (1/3) non traduite `ecart-11` — « Interdiction d'emprunter l'équipement des autres. » → refus `preference_not_constraint` (attendu : `forbid(BorrowWeapon)`)
- (3/3) famille de refus `ecart-19` — `unknown_symbol` (attendu : `not_expressible`)
- (2/3) famille de refus `ecart-24` — `unknown_symbol` (attendu : `not_expressible`)
