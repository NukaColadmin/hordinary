| Modele | Ablation | Compilation | Forme | Exacte | Refus correct | Faux acceptes | Latence p50/p95 | Cout/instr. |
|---|---|---|---|---|---|---|---|---|
| `qwen/qwen3.7-flash` | aucune | 78 % | 72 % | 65 % | 85 % | **4 %** | 20597 / 71166 ms | 0.00027 $ |

### `qwen/qwen3.7-flash` — aucune

- traduction differente `grp-rue` — `require_before(at(agent, group), MoveToLocation)` (attendu : `require_before(at(agent, group), move:street)`)
- non traduite `grp-toujours` — « Restez toujours groupés. » → refus `not_expressible` (attendu : `require_always(at(agent, group))`)
- non traduite `rue-interdite` — « Ne traversez pas les rues à découvert. » → refus `not_expressible` (attendu : `forbid(move:street)`)
- non traduite `outil-barricade` — « Il faut une boîte à outils pour barricader une porte. » → refus `conditional_not_supported` (attendu : `require_before(equipped(toolbox), BarricadeDoor)`)
- non traduite `partage-avant-van` — « Dites ce que vous avez vu avant de rejoindre le van. » → refus `conditional_not_supported` (attendu : `require_before(knowledge_shared, ReachExtraction)`)
- traduction differente `caisse-avant-retour` — `require_before(objective_found, enter:building)` (attendu : `require_before(objective_found, ReachExtraction)`)
- traduction differente `fouille-avant-retour` — `require_before(searched(objective_zone), MoveToLocation)` (attendu : `require_before(searched(objective_zone), ReachExtraction)`)
- non traduite `observe-avant-entree` — « Observez la zone objectif avant d'y entrer. » → refus `not_expressible` (attendu : `require_before(observed(objective_zone), enter:building)`)
- non traduite `connu-avant-pharmacie` — « N'allez à la pharmacie que si vous savez où elle est. » → refus `not_expressible` (attendu : `require_before(known(objective_zone), move:pharmacy)`)
- non traduite `objet-interdit` — « Ne ramassez rien d'autre que la caisse. » → refus `not_expressible` (attendu : `forbid(PickUpItem)`)
- non traduite `lacher-interdit` — « Ne lâchez jamais ce que vous portez. » → refus `not_expressible` (attendu : `forbid(DropItem)`)
- non traduite `attente-interdite` — « Ne restez jamais sans rien faire. » → refus `preference_not_constraint` (attendu : `forbid(Wait)`)
- traduction differente `caisse-puis-van` — `require_before(has(agent, objective), ReachExtraction)` (attendu : `require_after(extracted, PickUpObjective)`)
- traduction differente `ruelle-explo` — `penalize(move:alley, 3)` (attendu : `forbid(move:alley)`)
- famille de refus `pref-rentrer` — `conditional_not_supported` (attendu : `preference_not_constraint`)
- famille de refus `hs-charabia` — `preference_not_constraint` (attendu : `out_of_scope`)
- **faux accepte** `sym-arme` — « Équipez une arme avant d'attaquer. » → `require_before(equipped(objective), AttackWithWeapon)` (attendu : refus `unknown_symbol`)
- famille de refus `exp-individuel` — `preference_not_constraint` (attendu : `not_expressible`)
