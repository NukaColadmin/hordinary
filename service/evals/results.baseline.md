| Modele | Ablation | Compilation | Forme | Exacte | Refus correct | Faux acceptes | Latence p50/p95 | Cout/instr. |
|---|---|---|---|---|---|---|---|---|
| `deepseek/deepseek-v4.1-flash` | aucune | 82 % | 80 % | 72 % | 88 % | **4 %** | 1759 / 3896 ms | 0.00011 $ |

### `deepseek/deepseek-v4.1-flash` — aucune

- non traduite `grp-batiment` — « Avant d'entrer dans un bâtiment, regroupez-vous. » → refus `unknown_symbol` (attendu : `require_before(at(agent, group), enter:building)`)
- non traduite `grp-rue` — « Regroupez-vous avant de traverser une rue. » → refus `conditional_not_supported` (attendu : `require_before(at(agent, group), move:street)`)
- non traduite `grp-toujours` — « Restez toujours groupés. » → refus `not_expressible` (attendu : `require_always(at(agent, group))`)
- non traduite `grp-familier` — « Personne n'entre seul dans un bâtiment. » → refus `not_expressible` (attendu : `require_before(at(agent, group), enter:building)`)
- traduction differente `rue-interdite` — `penalize(move:street, 3)` (attendu : `forbid(move:street)`)
- traduction differente `outil-barricade` — `require_before(has(agent, toolbox), BarricadeDoor)` (attendu : `require_before(equipped(toolbox), BarricadeDoor)`)
- non traduite `partage-avant-van` — « Dites ce que vous avez vu avant de rejoindre le van. » → refus `conditional_not_supported` (attendu : `require_before(knowledge_shared, ReachExtraction)`)
- traduction differente `caisse-avant-retour` — `require_before(objective_found, enter:building)` (attendu : `require_before(objective_found, ReachExtraction)`)
- non traduite `connu-avant-pharmacie` — « N'allez à la pharmacie que si vous savez où elle est. » → refus `not_expressible` (attendu : `require_before(known(objective_zone), move:pharmacy)`)
- non traduite `lacher-interdit` — « Ne lâchez jamais ce que vous portez. » → refus `preference_not_constraint` (attendu : `forbid(DropItem)`)
- traduction differente `caisse-puis-van` — `require_after(at(agent, extraction), PickUpObjective)` (attendu : `require_after(extracted, PickUpObjective)`)
- famille de refus `pref-pharmacie` — `conditional_not_supported` (attendu : `preference_not_constraint`)
- **faux accepte** `sym-arme` — « Équipez une arme avant d'attaquer. » → `require_before(equipped(objective), AttackWithWeapon)` (attendu : refus `unknown_symbol`)
- famille de refus `exp-bruit` — `preference_not_constraint` (attendu : `not_expressible`)
