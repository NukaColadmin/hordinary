| Modele | Ablation | Compilation | Forme | Exacte | Refus correct | Faux acceptes | Latence p50/p95 | Cout/instr. |
|---|---|---|---|---|---|---|---|---|
| `deepseek/deepseek-v4.1-flash` | aucune, heldout.jsonl | 94 % | 94 % | 94 % | 86 % | **0 %** | 3458 / 4132 ms | 0.00013 $ |

### `deepseek/deepseek-v4.1-flash` — aucune, heldout.jsonl

- non traduite `ecart-05` — « Faites du bruit loin de l'entrée avant de passer la porte d'un bâtiment. » → refus `conditional_not_supported` (attendu : `require_before(diversion_made, enter:building)`)
- famille de refus `ecart-19` — `unknown_symbol` (attendu : `not_expressible`)
