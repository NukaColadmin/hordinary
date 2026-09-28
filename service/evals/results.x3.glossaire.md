| Modele | Ablation | Compilation | Forme | Exacte | Refus correct | Faux acceptes | Latence p50/p95 | Cout/instr. |
|---|---|---|---|---|---|---|---|---|
| `deepseek/deepseek-v4.1-flash` | aucune, 3 tirages | 94 % | 90 % | 85 % | 87 % | **4 %** | 1593 / 4503 ms | 0.00007 $ |

### `deepseek/deepseek-v4.1-flash` — aucune, 3 tirages

- (1/3) non traduite `grp-rue` — « Regroupez-vous avant de traverser une rue. » → refus `conditional_not_supported` (attendu : `require_before(at(agent, group), move:street)`)
- (2/3) traduction differente `rue-interdite` — `penalize(move:street, 3)` (attendu : `forbid(move:street)`)
- (1/3) traduction differente `rue-interdite` — `require_always(hidden)` (attendu : `forbid(move:street)`)
- (1/3) non traduite `cache-avant-ruelle` — « Cachez-vous avant de vous engager dans une ruelle. » → refus `out_of_scope` (attendu : `require_before(hidden, move:alley)`)
- (3/3) traduction differente `outil-barricade` — `require_before(has(agent, toolbox), BarricadeDoor)` (attendu : `require_before(equipped(toolbox), BarricadeDoor)`)
- (1/3) traduction differente `partage-avant-explo` — `require_before(at(agent, group), Explore)` (attendu : `require_before(knowledge_shared, Explore)`)
- (1/3) non traduite `coop-desarme` — « Personne n'entre désarmé dans un bâtiment. » → refus `preference_not_constraint` (attendu : `require_before(armed, enter:building)`)
- (1/3) non traduite `coop-prevenir` — « Prévenez un camarade avant de rentrer. » → refus `unknown_symbol` (attendu : `require_before(knowledge_shared, ReachExtraction)`)
- (1/3) non traduite `observe-avant-entree` — « Observez la zone objectif avant d'y entrer. » → refus `not_expressible` (attendu : `require_before(observed(objective_zone), enter:building)`)
- (1/3) traduction differente `connu-avant-pharmacie` — `require_before(known(mission), enter:pharmacy)` (attendu : `require_before(known(objective_zone), move:pharmacy)`)
- (2/3) non traduite `connu-avant-pharmacie` — « N'allez à la pharmacie que si vous savez où elle est. » → refus `not_expressible` (attendu : `require_before(known(objective_zone), move:pharmacy)`)
- (1/3) traduction differente `lacher-interdit` — `require_always(has(agent, objective))` (attendu : `forbid(DropItem)`)
- (1/3) non traduite `extraction-toujours` — « Ne quittez jamais la zone d'extraction. » → refus `unknown_symbol` (attendu : `require_always(at(agent, extraction))`)
- (1/3) traduction differente `ruelle-explo` — `penalize(move:alley, 3)` (attendu : `forbid(move:alley)`)
- (1/3) non traduite `pharmacie-evitee` — « Évitez la pharmacie tant que possible. » → refus `preference_not_constraint` (attendu : `penalize(move:pharmacy, k)`)
- (3/3) famille de refus `pref-rentrer` — `unknown_symbol` (attendu : `preference_not_constraint`)
- (2/3) famille de refus `pref-pharmacie` — `conditional_not_supported` (attendu : `preference_not_constraint`)
- (1/3) traduction differente `coop-diversion` — `require_before(diversion_made, ThrowFirecracker)` (attendu : `require_before(diversion_made, enter:building)`)
- (1/3) traduction differente `arme-avant-attaque` — `require_before(equipped(toolbox), AttackWithWeapon)` (attendu : `require_before(armed, AttackWithWeapon)`)
- (1/3) **faux accepte** `sym-porte-fond` — « Barricadez la porte du fond. » → `require_before(at(agent, cover), BarricadeDoor)` (attendu : refus `unknown_symbol`)
- (1/3) **faux accepte** `sym-porte-fond` — « Barricadez la porte du fond. » → `require_before(at(agent, mission), BarricadeDoor)` (attendu : refus `unknown_symbol`)
- (1/3) famille de refus `exp-individuel` — `unknown_symbol` (attendu : `not_expressible`)
- (1/3) famille de refus `exp-bruit` — `preference_not_constraint` (attendu : `not_expressible`)
- (1/3) **faux accepte** `exp-voix` — « Restez toujours à portée de voix les uns des autres. » → `require_always(at(agent, group))` (attendu : refus `not_expressible`)
