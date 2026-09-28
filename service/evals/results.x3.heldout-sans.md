| Modele | Ablation | Compilation | Forme | Exacte | Refus correct | Faux acceptes | Latence p50/p95 | Cout/instr. |
|---|---|---|---|---|---|---|---|---|
| `deepseek/deepseek-v4.1-flash` | sans glossaire, heldout.jsonl, 3 tirages | 76 % | 76 % | 65 % | 86 % | **0 %** | 471 / 633 ms | 0.00010 $ |

### `deepseek/deepseek-v4.1-flash` — sans glossaire, heldout.jsonl, 3 tirages

- (2/3) non traduite `ecart-03` — « Qu'aucun survivant ne passe le seuil d'un bâtiment les mains vides, sans arme. » → refus `not_expressible` (attendu : `require_before(armed, enter:building)`)
- (1/3) non traduite `ecart-03` — « Qu'aucun survivant ne passe le seuil d'un bâtiment les mains vides, sans arme. » → refus `conditional_not_supported` (attendu : `require_before(armed, enter:building)`)
- (1/3) non traduite `ecart-05` — « Faites du bruit loin de l'entrée avant de passer la porte d'un bâtiment. » → refus `conditional_not_supported` (attendu : `require_before(diversion_made, enter:building)`)
- (2/3) non traduite `ecart-05` — « Faites du bruit loin de l'entrée avant de passer la porte d'un bâtiment. » → refus `not_expressible` (attendu : `require_before(diversion_made, enter:building)`)
- (2/3) non traduite `ecart-06` — « Avant de fouiller, assurez-vous que personne n'est blessé. » → refus `conditional_not_supported` (attendu : `require_before(band_tended, SearchRoom)`)
- (1/3) non traduite `ecart-06` — « Avant de fouiller, assurez-vous que personne n'est blessé. » → refus `unknown_symbol` (attendu : `require_before(band_tended, SearchRoom)`)
- (1/3) traduction differente `ecart-07` — `require_before(known(objective_spot), ReachExtraction)` (attendu : `require_before(objective_found, ReachExtraction)`)
- (2/3) traduction differente `ecart-07` — `require_before(known(objective_zone), ReachExtraction)` (attendu : `require_before(objective_found, ReachExtraction)`)
- (3/3) traduction differente `ecart-08` — `require_before(searched(mission), enter:building)` (attendu : `require_before(searched(objective_zone), ReachExtraction)`)
- (3/3) non traduite `ecart-10` — « Ne quittez jamais vos camarades. » → refus `unknown_symbol` (attendu : `require_always(at(agent, group))`)
- (3/3) famille de refus `ecart-19` — `unknown_symbol` (attendu : `not_expressible`)
