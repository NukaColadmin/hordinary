| Modele | Ablation | Compilation | Forme | Exacte | Refus correct | Faux acceptes | Latence p50/p95 | Cout/instr. |
|---|---|---|---|---|---|---|---|---|
| `deepseek/deepseek-v4.1-flash` | sans glossaire, heldout.jsonl | 76 % | 76 % | 65 % | 86 % | **0 %** | 1273 / 1570 ms | 0.00014 $ |

### `deepseek/deepseek-v4.1-flash` — sans glossaire, heldout.jsonl

- non traduite `ecart-03` — « Qu'aucun survivant ne passe le seuil d'un bâtiment les mains vides, sans arme. » → refus `not_expressible` (attendu : `require_before(armed, enter:building)`)
- non traduite `ecart-05` — « Faites du bruit loin de l'entrée avant de passer la porte d'un bâtiment. » → refus `not_expressible` (attendu : `require_before(diversion_made, enter:building)`)
- non traduite `ecart-06` — « Avant de fouiller, assurez-vous que personne n'est blessé. » → refus `conditional_not_supported` (attendu : `require_before(band_tended, SearchRoom)`)
- traduction differente `ecart-07` — `require_before(known(objective_spot), ReachExtraction)` (attendu : `require_before(objective_found, ReachExtraction)`)
- traduction differente `ecart-08` — `require_before(searched(mission), enter:building)` (attendu : `require_before(searched(objective_zone), ReachExtraction)`)
- non traduite `ecart-10` — « Ne quittez jamais vos camarades. » → refus `preference_not_constraint` (attendu : `require_always(at(agent, group))`)
- famille de refus `ecart-19` — `unknown_symbol` (attendu : `not_expressible`)
