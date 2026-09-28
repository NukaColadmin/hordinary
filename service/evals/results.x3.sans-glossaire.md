| Modele | Ablation | Compilation | Forme | Exacte | Refus correct | Faux acceptes | Latence p50/p95 | Cout/instr. |
|---|---|---|---|---|---|---|---|---|
| `deepseek/deepseek-v4.1-flash` | sans glossaire, 3 tirages | 81 % | 78 % | 69 % | 91 % | **0 %** | 484 / 974 ms | 0.00010 $ |

### `deepseek/deepseek-v4.1-flash` — sans glossaire, 3 tirages

- (3/3) non traduite `grp-rue` — « Regroupez-vous avant de traverser une rue. » → refus `unknown_symbol` (attendu : `require_before(at(agent, group), move:street)`)
- (3/3) non traduite `grp-toujours` — « Restez toujours groupés. » → refus `preference_not_constraint` (attendu : `require_always(at(agent, group))`)
- (3/3) traduction differente `grp-familier` — `forbid(enter:building)` (attendu : `require_before(at(agent, group), enter:building)`)
- (1/3) non traduite `rue-interdite` — « Ne traversez pas les rues à découvert. » → refus `preference_not_constraint` (attendu : `forbid(move:street)`)
- (2/3) non traduite `rue-interdite` — « Ne traversez pas les rues à découvert. » → refus `not_expressible` (attendu : `forbid(move:street)`)
- (3/3) traduction differente `outil-barricade` — `require_before(has(agent, toolbox), BarricadeDoor)` (attendu : `require_before(equipped(toolbox), BarricadeDoor)`)
- (3/3) non traduite `coop-soin` — « Soignez les blessés avant d'entrer. » → refus `conditional_not_supported` (attendu : `require_before(band_tended, enter:building)`)
- (2/3) traduction differente `coop-soin-van` — `require_before(band_tended, enter:extraction)` (attendu : `require_before(band_tended, ReachExtraction)`)
- (2/3) non traduite `coop-desarme` — « Personne n'entre désarmé dans un bâtiment. » → refus `conditional_not_supported` (attendu : `require_before(armed, enter:building)`)
- (1/3) traduction differente `coop-prevenir` — `require_before(knowledge_shared, enter:building)` (attendu : `require_before(knowledge_shared, ReachExtraction)`)
- (2/3) non traduite `coop-prevenir` — « Prévenez un camarade avant de rentrer. » → refus `unknown_symbol` (attendu : `require_before(knowledge_shared, ReachExtraction)`)
- (3/3) traduction differente `caisse-avant-retour` — `require_before(objective_found, enter:building)` (attendu : `require_before(objective_found, ReachExtraction)`)
- (3/3) traduction differente `fouille-avant-retour` — `require_before(searched(objective_zone), MoveToLocation)` (attendu : `require_before(searched(objective_zone), ReachExtraction)`)
- (3/3) non traduite `connu-avant-pharmacie` — « N'allez à la pharmacie que si vous savez où elle est. » → refus `not_expressible` (attendu : `require_before(known(objective_zone), move:pharmacy)`)
- (2/3) non traduite `objet-interdit` — « Ne ramassez rien d'autre que la caisse. » → refus `not_expressible` (attendu : `forbid(PickUpItem)`)
- (2/3) non traduite `lacher-interdit` — « Ne lâchez jamais ce que vous portez. » → refus `not_expressible` (attendu : `forbid(DropItem)`)
- (1/3) non traduite `lacher-interdit` — « Ne lâchez jamais ce que vous portez. » → refus `preference_not_constraint` (attendu : `forbid(DropItem)`)
- (3/3) non traduite `attente-interdite` — « Ne restez jamais sans rien faire. » → refus `preference_not_constraint` (attendu : `forbid(Wait)`)
- (2/3) traduction differente `caisse-puis-van` — `require_after(at(agent, extraction), PickUpObjective)` (attendu : `require_after(extracted, PickUpObjective)`)
- (1/3) non traduite `caisse-puis-van` — « Une fois la caisse ramassée, rentrez. » → refus `not_expressible` (attendu : `require_after(extracted, PickUpObjective)`)
- (3/3) famille de refus `pref-pharmacie` — `conditional_not_supported` (attendu : `preference_not_constraint`)
- (1/3) famille de refus `sym-porte-fond` — `not_expressible` (attendu : `unknown_symbol`)
- (3/3) famille de refus `exp-bruit` — `preference_not_constraint` (attendu : `not_expressible`)
