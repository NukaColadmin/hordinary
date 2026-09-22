# Évaluation des modèles — résultats

Ce que quinze modèles font d'une phrase de joueur — onze ont répondu — mesuré sur les **66 phrases** de
[`utterances.jsonl`](utterances.jsonl). Ce fichier est écrit à la main : il synthétise des passages
facturés, qui ne sont pas reproductibles par la suite de tests seule. Les tableaux bruts de chaque
passage sont à côté, générés par `evals/run.py`.

## Ce qui est mesuré, et pourquoi le chiffre qui compte n'est pas celui qu'on croit

| Mesure | Ce qu'elle dit |
|---|---|
| Compilation | combien de phrases recevables deviennent une contrainte valide |
| Forme | la bonne des cinq formes a-t-elle été choisie |
| Exacte | la contrainte est-elle celle attendue, mot pour mot |
| Refus correct | combien d'irrecevables sont refusées, **et pour la bonne raison** |
| **Faux acceptés** | **combien d'irrecevables sont traduites quand même** |

Le corpus n'est pas un jeu de cas faciles : **40 phrases traduisibles et 26 qui doivent être
refusées**, dont des préférences déguisées en règles, des conditionnelles, une tentative d'injection
de prompt, et des consignes parfaitement claires que la grammaire fermée ne peut pas exprimer.

**Le taux de faux acceptés est le chiffre à mettre en avant, y compris quand il est mauvais.** Un
tableau qui ne publierait que la colonne « compilation » désignerait le modèle le plus complaisant
comme le meilleur. Ce passage en fournit la démonstration, plus bas.

## Génération 2 — prompt réécrit, raisonnement désactivé

Septembre 2026. `HORDINARY_REASONING=off`, prompt système réécrit (voir plus bas).
Brut : [`results.recents.md`](results.recents.md).

| Modèle | Compil. | Forme | Exacte | Refus corr. | **Faux acc.** | p50 / p95 | $/instr. |
|---|---|---|---|---|---|---|---|
| `mistralai/mistral-medium-3-5` | **95 %** | **90 %** | **75 %** | 81 % | 8 % | 1 226 / 3 376 ms | 0,00393 $ |
| `inference-net/schematron-v2-turbo` | 90 % | 55 % | 2 % | 8 % | **85 %** | 3 299 / 6 201 ms | 0,00009 $ |
| **`deepseek/deepseek-v4.1-flash`** | 80 % | 78 % | 70 % | **85 %** | **0 %** | 1 125 / **2 771 ms** | 0,00010 $ |
| `inception/mercury-2.5` | 68 % | 65 % | 60 % | 81 % | **0 %** | 921 / 2 702 ms | 0,00011 $ |
| `mistralai/mistral-small-2603` | 28 % | 25 % | 20 % | 15 % | 8 % | 2 426 / 2 978 ms | 0,00004 $ |

### Quatre modèles n'ont pas pu concourir, et c'est un résultat

| Modèle | Réponse du fournisseur |
|---|---|
| `z-ai/glm-5.3-flash` | HTTP 400 — `Reasoning is mandatory for this endpoint and cannot be disabled.` |
| `z-ai/glm-5.3-flashx` | HTTP 400 — idem |
| `google/gemini-3.8-flash` | HTTP 400 — idem |
| `openai/gpt-luna-latest` | HTTP 400 — `is not a valid model ID` |

Ils comptent 0 % sur toutes les colonnes avec une latence de 30 à 65 ms : **un échec instantané n'est
pas un modèle nul**, et une ligne à zéro doit toujours se lire avec sa latence.

Deux enseignements. D'abord, `supported_parameters: ["reasoning"]` dans le catalogue OpenRouter ne
veut **pas** dire « débrayable » : trois des modèles les plus récents du catalogue imposent le
raisonnement. Une contrainte de latence dure exclut donc aujourd'hui une partie du frontier, par
construction. Ensuite, les entrées préfixées `~` dans le catalogue ne sont pas appelables sous ce nom.

## Génération 1 — prompt d'origine, réglage d'usine de chaque modèle

Brut : [`results.ouverts.md`](results.ouverts.md), [`results.qwen38.md`](results.qwen38.md),
[`results.deepseek.md`](results.deepseek.md), [`results.gemma4.md`](results.gemma4.md).

| Modèle | Compil. | Forme | Exacte | Refus corr. | **Faux acc.** | p50 / p95 | $/instr. |
|---|---|---|---|---|---|---|---|
| `deepseek/deepseek-v4-flash-0731` | 65 % | 52 % | 32 % | 73 % | **0 %** | 32 759 / **267 540 ms** | 0,00000 $ |
| `qwen/qwen3.7-flash` | 42 % | 32 % | 30 % | 81 % | **0 %** | 1 806 / 3 922 ms | 0,00003 $ |
| `qwen/qwen3.8-flash` | 40 % | 35 % | 30 % | **85 %** | **0 %** | 1 897 / **21 928 ms** | 0,00007 $ |
| `nvidia/nemotron-3-super-120b-a12b` | 32 % | 28 % | 22 % | 65 % | 4 % | 1 031 / 2 862 ms | 0,00010 $ |
| `google/gemma-4-26b-a4b-it` | 10 % | 10 % | 5 % | 58 % | **0 %** | 3 028 / 10 605 ms | 0,00000 $ |
| `nvidia/nemotron-3.5-lightning` | 2 % | 2 % | 0 % | 54 % | 4 % | 683 / 1 287 ms | 0,00005 $ |

Les deux meilleurs taux de compilation de cette génération appartiennent à des modèles à raisonnement
dont le **p95 est inutilisable** : 22 s et 267 s, pour un `timeout_ms` de 8 000 côté jeu. C'est le
compromis que la génération 2 casse.

## Ce que la réécriture du prompt a changé

Le plafond n'était pas le modèle. Dépouillement des six passages de la génération 1 :

| Genre d'échec | Occurrences |
|---|---|
| **Phrase traduisible refusée** | **163** |
| Traduite, mais de travers | 29 |

**Un rapport de 5,6 contre 1.** Le modèle ne se trompait pas de traduction : il refusait de traduire.
Les familles invoquées à tort désignaient trois défauts de *définition* dans le prompt, pas de
formulation :

| Famille invoquée à tort | Occ. | Le défaut |
|---|---|---|
| `preference_not_constraint` | 63 | Elle mangeait `penalize`. « Évitez de forcer les portes » était refusée comme une préférence — alors que la cinquième forme existe exactement pour ça. Le prompt ne disait nulle part qu'une préférence **portant sur une action nommée** est traduisible. |
| `not_expressible` | 61 | Aucun exemple, et une prudence non calibrée. « Dans le doute, refuse » est juste quand le doute porte sur *laquelle* des cinq formes écrire ; appliqué à *s'il faut traduire*, il produit exactement l'asymétrie mesurée. |
| `conditional_not_supported` | 22 | Elle mangeait `require_before`. Le prompt disait « conditionnelle ou chaînée » sans séparer « **si** vous êtes blessé » — un état qui peut survenir ou non, effectivement inexprimable — de « **avant d'**entrer », qui est la définition même de la forme. |

La réécriture donne un **test ordonné en quatre questions** — la phrase nomme-t-elle une action ?
quelle force (absolue → `forbid`, graduelle → `penalize`) ? y a-t-il une antériorité ? sinon un état
permanent ? — avec les marqueurs de langue qui tranchent à chaque étape, et une consigne de prudence
redressée dans les deux sens : *refuser une phrase qui nomme une action du domaine est une erreur au
même titre qu'inventer une contrainte.*

**Les exemples du prompt ne sont tirés du corpus d'aucune façon** — autres actions, autres tournures,
absence vérifiée dans `utterances.jsonl`. Y coller les 40 phrases traduisibles aurait fait monter tous
les taux en confondant l'apprentissage et l'examen, et aurait vidé de son sens l'ablation
`--few-shot`, dont le propos est précisément de mesurer ce qu'apportent des exemples tirés du corpus.
Le raisonnement complet est en commentaire au-dessus de `SYSTEM`, dans [`../app/graph.py`](../app/graph.py).

### Témoin : le même modèle, seul le prompt change

Les deux tableaux ci-dessus font varier **deux choses à la fois** — le prompt et la famille de
modèles. Ils ne permettent donc pas d'attribuer le gain. `qwen/qwen3.7-flash` est repassé avec le
nouveau prompt et ses réglages d'origine, ce qui isole la variable.
Brut : [`results.temoin-prompt.md`](results.temoin-prompt.md).

| Passage | Compil. | Forme | Exacte | Refus corr. | **Faux acc.** | p50 / p95 | $/instr. |
|---|---|---|---|---|---|---|---|
| Prompt d'origine | 42 % | 32 % | 30 % | 81 % | **0 %** | 1 806 / 3 922 ms | 0,00003 $ |
| Prompt réécrit | **78 %** | **72 %** | **65 %** | **85 %** | **4 %** | 20 597 / **71 166 ms** | 0,00027 $ |

**Le gain est bien celui du prompt, et il est large** : +36 points de compilation, +40 de forme, +35
d'exactitude au mot près, sur le même modèle, aux mêmes réglages. L'hypothèse « le plafond était le
prompt » est confirmée par mesure et non par raisonnement.

Il se paie de deux façons, et les deux doivent être publiées.

**Le taux de faux acceptés passe de 0 % à 4 %.** C'est le risque assumé d'assouplir un prompt, et il
s'est réalisé. Un seul cas, et il est instructif :

> `sym-arme` — « Équipez une arme avant d'attaquer. »
> → `require_before(equipped(objective), AttackWithWeapon)` — attendu : refus `unknown_symbol`

Le domaine n'a pas d'objet « arme » : ses items sont `objective`, `remedy`, `toolbox`. Poussé à
traduire plutôt qu'à refuser, le modèle a **substitué le symbole le plus proche**. Le résultat est
grammaticalement valide et **correctement typé** — `equipped` attend un `item`, `objective` en est
un — donc le portillon 1 l'accepte. C'est une contrainte qui passe toutes les vérifications et qui
dit autre chose que le joueur. Exactement la sortie que le taux de faux acceptés existe pour compter,
et la contrepartie exacte du gain ci-dessus.

Le correctif se voit d'ici — interdire explicitement la substitution de symbole approchant — mais il
n'est pas écrit : l'affirmer sans repasser le corpus serait précisément ce que ce dossier refuse de
faire.

**La latence explose : p95 de 3 922 ms à 71 166 ms, et le coût est multiplié par neuf.** Le prompt a
doublé (1 091 → 2 285 tokens), ce qui n'explique pas un facteur 18 sur le p95. L'explication est
ailleurs : `qwen3.7-flash` est un modèle **à raisonnement**, ici à son réglage d'usine, et un prompt
qui lui donne un test ordonné en quatre questions lui donne beaucoup plus à ruminer. Le coût par
instruction le confirme — ce sont des tokens de réflexion.

**Cet effet ne se transporte pas au modèle livré**, qui tourne avec `HORDINARY_REASONING=off` :
`deepseek/deepseek-v4.1-flash` compile 80 % avec le même prompt long, en 2 771 ms de p95. La leçon
utile n'est donc pas « le prompt long coûte cher » mais **« prompt riche et raisonnement actif se
cumulent mal »** — et c'est une raison de plus de livrer le raisonnement coupé.

## Le modèle le plus complaisant serait le meilleur d'un tableau malhonnête

`inference-net/schematron-v2-turbo` compile **90 %** des phrases — mieux que tout le reste sauf le
haut de gamme Mistral. Sur un tableau qui ne publierait que cette colonne, il serait premier, et pour
0,00009 $ l'instruction.

Il a **85 % de faux acceptés et 8 % de refus corrects**. Il traduit tout : les préférences vagues, les
conditionnelles, « Raconte-moi une blague », et la tentative d'injection de prompt. C'est un modèle
spécialisé dans l'extraction structurée, et il fait exactement ce pour quoi il est fait — produire du
JSON bien formé quoi qu'on lui donne.

Il n'a pas été cherché pour illustrer ce point : il est arrivé dans l'échantillon parce qu'il était le
candidat non-raisonnant le plus récent du catalogue. C'est la meilleure justification possible de la
colonne « faux acceptés », et elle est venue toute seule.

## Le modèle livré

**`deepseek/deepseek-v4.1-flash`** depuis le 21 septembre 2026, contre `qwen/qwen3.8-flash`
auparavant. La bascule a été faite **dans les trois endroits à la fois** —
`config/game_config.json`, `InstructionSettings.Model`, `Settings.model` — plus les exemples de la
spécification et des tests : trois fichiers qui nomment trois modèles différents est la pire forme de
défaut, celle où lire le code ne dit pas ce que le jeu fait.

| | Avant | Livré |
|---|---|---|
| Compilation | 40 % | **80 %** |
| Refus correct | 85 % | 85 % |
| Faux acceptés | 0 % | **0 %** |
| p95 | **21 928 ms** | **2 771 ms** |
| Coût / instruction | 0,00007 $ | 0,00010 $ |

Le p95 est le point décisif, plus que la compilation. Le jeu coupe à `timeout_ms` = 8 000 et le
service à `timeout_s` = 20 : à 21,9 s, la queue lente du modèle livré tombait en repli sur les
instructions pré-écrites — proprement, et en le signalant, mais elle tombait. À 2,8 s, elle passe.

**`HORDINARY_REASONING` passe donc à `off` par défaut, et c'est indissociable du changement de
modèle.** `deepseek-v4.1-flash` sait raisonner, et les 2 771 ms ont été mesurés **sans réflexion** :
livrer le modèle en laissant le fournisseur à son réglage d'usine donnerait un défaut qui ne
reproduit pas le chiffre publié ci-dessus. Le défaut précédent — vide, « on ne bride personne » — se
défendait tant que ce champ ne servait qu'à comparer ; pour mesurer, passer la variable **vide** reste
la bonne façon de faire.

`mistralai/mistral-medium-3-5` a la meilleure qualité brute du tableau (95 % / 90 % / 75 %, personne
n'approche), mais il laisse passer 8 % de faux acceptés et coûte **39 fois** plus cher. Le compromis
ne se défend pas pour un usage où quatre portillons déterministes rattrapent déjà les erreurs de
traduction, et où la latence se voit à l'écran.

## Reproduire

```bash
cd service
OPENROUTER_API_KEY=<clé> HORDINARY_REASONING=off ./.venv/Scripts/python.exe -u -m evals.run \
    --model deepseek/deepseek-v4.1-flash \
    --model mistralai/mistral-medium-3-5 \
    --out evals/results.recents.md
```

La clé passe **par l'environnement de la commande** et n'entre dans aucun fichier du dépôt :
`config/game_config.json` ne contient que le *nom* de la variable.

Le harnais pose `Budget(ceiling=inf)` — il n'a volontairement aucun plafond de dépense, contrairement
au jeu, parce qu'un plafond atteint en cours de passage fausserait la mesure. Compter avant de lancer :
avec le prompt actuel (~2 285 tokens), un passage coûte de 0,01 $ à 0,26 $ selon le modèle.

## Ce qui manque encore

**Les deux ablations que la spécification exige — `--no-digest` et `--few-shot` — n'ont aucun
chiffre.** Le harnais les implémente et les a déroulées à blanc ; aucun passage facturé ne les a
encore mesurées. Tant que c'est le cas, ce que le digest de domaine apporte réellement au taux de
compilation est une hypothèse, pas un résultat — et c'est précisément le genre d'affirmation que ce
dossier existe pour ne pas faire.
