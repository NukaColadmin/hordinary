| Modele | Ablation | Compilation | Forme | Exacte | Refus correct | Faux acceptes | Latence p50/p95 | Cout/instr. |
|---|---|---|---|---|---|---|---|---|
| `deepseek/deepseek-v4.1-flash` | few-shot 3 | 88 % | 85 % | 78 % | 85 % | **4 %** | 1739 / 3075 ms | 0.00013 $ |

### `deepseek/deepseek-v4.1-flash` — few-shot 3

- non traduite `grp-toujours` — « Restez toujours groupés. » → refus `not_expressible` (attendu : `require_always(at(agent, group))`)
- traduction differente `rue-interdite` — `penalize(move:street, 3)` (attendu : `forbid(move:street)`)
- traduction differente `outil-barricade` — `require_before(has(agent, toolbox), BarricadeDoor)` (attendu : `require_before(equipped(toolbox), BarricadeDoor)`)
- traduction differente `caisse-avant-retour` — `require_before(objective_found, enter:building)` (attendu : `require_before(objective_found, ReachExtraction)`)
- non traduite `connu-avant-pharmacie` — « N'allez à la pharmacie que si vous savez où elle est. » → refus `not_expressible` (attendu : `require_before(known(objective_zone), move:pharmacy)`)
- non traduite `objet-interdit` — « Ne ramassez rien d'autre que la caisse. » → refus `not_expressible` (attendu : `forbid(PickUpItem)`)
- non traduite `lacher-interdit` — « Ne lâchez jamais ce que vous portez. » → refus `not_expressible` (attendu : `forbid(DropItem)`)
- non traduite `attente-interdite` — « Ne restez jamais sans rien faire. » → refus `preference_not_constraint` (attendu : `forbid(Wait)`)
- traduction differente `caisse-puis-van` — `require_after(at(agent, extraction), PickUpObjective)` (attendu : `require_after(extracted, PickUpObjective)`)
- famille de refus `pref-rentrer` — `unknown_symbol` (attendu : `preference_not_constraint`)
- famille de refus `pref-pharmacie` — `conditional_not_supported` (attendu : `preference_not_constraint`)
- **faux accepte** `sym-arme` — « Équipez une arme avant d'attaquer. » → `require_before(equipped(objective), AttackWithWeapon)` (attendu : refus `unknown_symbol`)
- famille de refus `exp-bruit` — `preference_not_constraint` (attendu : `not_expressible`)
