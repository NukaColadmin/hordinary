| Modele | Ablation | Compilation | Forme | Exacte | Refus correct | Faux acceptes | Latence p50/p95 | Cout/instr. |
|---|---|---|---|---|---|---|---|---|
| `deepseek/deepseek-v4.1-flash` | aucune | 90 % | 90 % | 88 % | 85 % | **0 %** | 2890 / 4969 ms | 0.00011 $ |

### `deepseek/deepseek-v4.1-flash` — aucune

- traduction differente `outil-barricade` — `require_before(has(agent, toolbox), BarricadeDoor)` (attendu : `require_before(equipped(toolbox), BarricadeDoor)`)
- non traduite `coop-soin` — « Soignez les blessés avant d'entrer. » → refus `unknown_symbol` (attendu : `require_before(band_tended, enter:building)`)
- non traduite `observe-avant-entree` — « Observez la zone objectif avant d'y entrer. » → refus `not_expressible` (attendu : `require_before(observed(objective_zone), enter:building)`)
- non traduite `connu-avant-pharmacie` — « N'allez à la pharmacie que si vous savez où elle est. » → refus `not_expressible` (attendu : `require_before(known(objective_zone), move:pharmacy)`)
- non traduite `attente-interdite` — « Ne restez jamais sans rien faire. » → refus `preference_not_constraint` (attendu : `forbid(Wait)`)
- non traduite `extraction-toujours` — « Ne quittez jamais la zone d'extraction. » → refus `conditional_not_supported` (attendu : `require_always(at(agent, extraction))`)
- famille de refus `pref-rentrer` — `unknown_symbol` (attendu : `preference_not_constraint`)
- famille de refus `pref-pharmacie` — `not_expressible` (attendu : `preference_not_constraint`)
- famille de refus `sym-porte-fond` — `preference_not_constraint` (attendu : `unknown_symbol`)
- famille de refus `exp-individuel` — `conditional_not_supported` (attendu : `not_expressible`)
