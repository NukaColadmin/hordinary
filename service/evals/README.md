# Évaluation des modèles — résultats

Ce que quinze modèles font d'une phrase de joueur — onze ont répondu — mesuré sur le corpus de
[`utterances.jsonl`](utterances.jsonl). Ce fichier est écrit à la main : il synthétise des passages
facturés, qui ne sont pas reproductibles par la suite de tests seule. Les tableaux bruts de chaque
passage sont à côté, générés par `evals/run.py`.

**En bref**, pour le modèle livré, trois tirages par condition (« Le glossaire, mesuré ») : **94 %**
de compilation, **85 %** d'exactitude au mot près, **87 %** de refus corrects et **4 %** de faux
acceptés sur le corpus ; **86 %** d'exactitude et **0 %** de faux acceptés sur vingt-quatre phrases
tenues à l'écart. Le corpus a grandi avec le domaine, et chaque tableau dit sur combien de phrases il
a été pris : 66 pour la comparaison des modèles et les ablations, 74 pour le glossaire. Il en compte
76 aujourd'hui ; les deux dernières, les ordres de renvoi et de rappel, n'ont pas encore été mesurées.

## Ce qui est mesuré, et pourquoi le chiffre qui compte n'est pas celui qu'on croit

| Mesure | Ce qu'elle dit |
|---|---|
| Compilation | combien de phrases recevables deviennent une contrainte valide |
| Forme | la bonne des cinq formes a-t-elle été choisie |
| Exacte | la contrainte est-elle celle attendue, mot pour mot |
| Refus correct | combien d'irrecevables sont refusées, **et pour la bonne raison** |
| **Faux acceptés** | **combien d'irrecevables sont traduites quand même** |

Le corpus n'est pas un jeu de cas faciles : lors de la comparaison des modèles, **40 phrases
traduisibles et 26 qui doivent être refusées**, dont des préférences déguisées en règles, des
conditionnelles, une tentative d'injection de prompt, et des consignes parfaitement claires que la
grammaire fermée ne peut pas exprimer.

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

`--repeat 3` fusionne trois tirages en une ligne et écrit chaque échec avec sa fréquence (« 1/3 »,
« 3/3 »). **Il faut s'en servir pour conclure** : à température nulle, le modèle livré ne répond pas
deux fois pareil, et un passage unique varie de plusieurs points — voir « Le glossaire, mesuré ».

La clé passe **par l'environnement de la commande** et n'entre dans aucun fichier du dépôt :
`config/game_config.json` ne contient que le *nom* de la variable.

Le harnais pose `Budget(ceiling=inf)` — il n'a volontairement aucun plafond de dépense, contrairement
au jeu, parce qu'un plafond atteint en cours de passage fausserait la mesure. Compter avant de lancer :
avec le prompt actuel (~2 285 tokens), un passage coûte de 0,01 $ à 0,26 $ selon le modèle.

Ou en conteneur, par `compose.yaml` à la racine — la clé vient de l'environnement, et le montage de
`service/evals/` est ce qui fait que le rapport d'un passage facturé survit au conteneur :

```bash
docker compose --profile evals run --rm evals --out evals/results.baseline.md
docker compose --profile evals run --rm evals --no-digest --out evals/results.no-digest.md
docker compose --profile evals run --rm evals --few-shot 3 --out evals/results.few-shot.md
```

Ce sont les trois lignes qui ont produit le tableau ci-dessous. `results.md` en est le regroupement,
et il n'est **pas** versionné : c'est une mesure, pas une source.

## Les deux ablations, mesurées

Passage du 22 septembre 2026, `deepseek/deepseek-v4.1-flash`, raisonnement désactivé, les 66 phrases
du corpus par condition. Tableaux bruts : [`results.baseline.md`](./results.baseline.md),
[`results.no-digest.md`](./results.no-digest.md), [`results.few-shot.md`](./results.few-shot.md).

| Ablation | Compilation | Forme | Exacte | Refus correct | Faux acceptés | p50/p95 | Coût/instr. |
|---|---|---|---|---|---|---|---|
| aucune | 82 % | 80 % | 72 % | 88 % | **4 %** | 1 759 / 3 896 ms | 0,00011 $ |
| sans digest | 0 % | 0 % | 0 % | 73 % | **0 %** | 1 554 / 8 025 ms | 0,00021 $ |
| few-shot 3 | 88 % | 85 % | 78 % | 85 % | **4 %** | 1 739 / 3 075 ms | 0,00013 $ |

**`--no-digest` ne dégrade pas, il supprime.** Zéro phrase compilée sur 66, et **38 des 40 refus
portent `unknown_symbol`** : privé du vocabulaire, le modèle ne reconnaît plus aucun symbole — ce qui
est le comportement correct. Le digest n'est donc pas une aide à la précision, c'est le substrat de
la tâche.

Son « 0 % de faux acceptés » est le même artefact que celui du `--dry-run` : on n'accepte rien à tort
quand on refuse tout. **Une colonne de faux acceptés se lit toujours avec le taux de compilation à
côté**, exactement comme une ligne à zéro se lit avec sa latence. Deux effets de bord confirment la
lecture : le coût par instruction **double** (0,00021 $ contre 0,00011 $), la reprise de `self_check`
partant sur chaque cas, et le p95 monte à 8 025 ms — au-dessus du `timeout_ms` de 8 000 du jeu.

**`--few-shot 3` gagne six points de compilation** (88 % contre 82 %) et six points d'exactitude
(78 % contre 72 %), pour 18 % de tokens en plus. Ce n'est pas un gain diffus : il tient presque
entièrement à **une convention de nommage que rien ne permet de deviner**. Le repère `group` est typé
`location`, donc « restez groupés » s'écrit `at(agent, group)` — « être à l'endroit nommé *groupe* ».
Le digest transmet le type, jamais le sens. Sans exemples, les quatre phrases de cette famille
échouent, avec trois motifs de refus différents ; avec trois voisins du corpus, il n'en reste qu'une.
**L'exemple canonique du livrable est dans le lot** : « Avant d'entrer dans un bâtiment,
regroupez-vous. » est refusée sans few-shot et traduite avec.

La contrepartie est réelle et se publie aussi : le refus correct **perd trois points** (85 % contre
88 %). Des exemples qui montrent comment traduire poussent à traduire.

## Le faux accepté qui survit aux deux conditions

`sym-arme` — « Équipez une arme avant d'attaquer. » — produit
`require_before(equipped(objective), AttackWithWeapon)` dans **chaque** passage où le digest est
présent. Le corpus en attend un refus `unknown_symbol` : aucun symbole d'arme n'existe au domaine.

**Le portillon 1 ne peut pas l'attraper, et c'est la limite exacte du typage.** `equipped` attend un
`item` ; `objective` *est* un `item`. La contrainte est donc bien formée, bien typée, et fausse. La
réparation trouvée par `nemotron` — comparer le type de chaque argument à celui que le prédicat
déclare — ferme les arguments du mauvais **type**, pas ceux du mauvais **référent**. Le portillon 3
ne la verrait pas davantage : elle est parfaitement satisfiable. C'est le joueur qui la voit, et
c'est pour cela que `InstructionPanel` affiche la **contrainte compilée** plutôt qu'un accusé de
réception : la seule défense contre une traduction bien typée mais fausse est de la montrer.

## Le corpus a vieilli avec le domaine (24 septembre 2026)

Trois gestes d'entraide sont revenus au domaine — `ThrowFirecracker`, `BorrowWeapon`, `TendAlly`, et
leurs faits `diversion_made`, `armed`, `band_tended`. Premier passage après eux, sur les 66 phrases
d'origine : **8 % de faux acceptés**, le double de la référence. Les deux « faux acceptés » étaient
`sym-diversion` → `require_before(diversion_made, enter:building)` et `sym-arme` →
`require_before(armed, AttackWithWeapon)` : **les bonnes traductions**, comptées fausses par un
attendu écrit quand ni la diversion ni aucun fait d'arme n'étaient nommables. Le faux accepté de la
section précédente a donc cessé de survivre, non parce que le modèle s'est corrigé mais parce que le
domaine lui a donné le mot juste — `equipped(objective)` était le plus proche voisin bien typé d'un
fait qui manquait.

`Chaque_contrainte_attendue_du_corpus_passe_la_grammaire` attache au domaine les attendus
**acceptés** ; rien n'y attache les refus `unknown_symbol`. Ajouter un symbole au jeu peut donc rendre
faux un attendu de refus sans rien casser, et c'est un passage payant qui l'a montré. Les deux phrases
attendent désormais leur contrainte, deux refus `unknown_symbol` les remplacent (`sym-pieges`,
`sym-voiture`), et six phrases de coopération entrent au corpus, qui passe à **74**. Tableau brut :
[`results.cooperation.md`](./results.cooperation.md).

| Corpus | Compilation | Forme | Exacte | Refus correct | Faux acceptés | p50/p95 | Coût/instr. |
|---|---|---|---|---|---|---|---|
| 66, attendus d'avant les gestes | 82 % | 80 % | 70 % | 81 % | **8 %** | 1 401 / 2 136 ms | 0,00019 $ |
| 74, attendus corrigés | 79 % | 77 % | 67 % | 88 % | **0 %** | 1 342 / 1 855 ms | 0,00011 $ |

Des six phrases de coopération, deux passent (`forbid(BorrowWeapon)`, `penalize(ThrowFirecracker, 3)`)
et quatre échouent, dont **les libellés de deux consignes pré-écrites** : « Soignez les blessés avant
d'entrer » est refusée comme un enchaînement d'actions, « Prévenez un camarade avant de rentrer »
comme un symbole inconnu. Deux causes, les mêmes que celles du reste du tableau :

* **le digest transmet le nom d'un fait, jamais son sens.** `band_tended()` ne dit pas « les blessés
  du groupe sont pansés » — c'est le cas de `group` dans l'ablation few-shot, étendu aux faits ;
* **« rentrer » n'est jamais lu comme `ReachExtraction`.** Cinq phrases du corpus y échouent :
  `enter:building`, `enter:extraction`, `MoveToLocation` ou un refus, selon la tournure.

« Personne n'entre désarmé » a été traduite dans une sonde et refusée dans le passage, à température
nulle : la tournure « personne n'… » (comme `grp-familier`) est à la frontière de ce que le modèle
tient pour une condition.

## Le glossaire, mesuré (24 septembre 2026)

La réponse à ces deux causes est un **glossaire** : chaque symbole nommable a une définition d'une
ligne dans `config/planner_domain.json` (section `glossary`), que le digest transporte et que le nœud
`context` écrit à côté du nom. Le contexte passe de 2 000 à 6 300 caractères. L'ablation
`--no-glossary` rend la mise en page exacte des passages précédents, donc mesure le seul apport du
sens des mots.

Une réserve devait être levée par la mesure et non par l'argument : les définitions ont été écrites
**après** avoir lu les échecs ci-dessus, et un gain limité au corpus serait de l'apprentissage
déguisé. D'où vingt-quatre phrases tenues à l'écart, absentes du corpus — dix-sept contraintes, sept
refus —, dans [`heldout.jsonl`](./heldout.jsonl) (`--corpus evals/heldout.jsonl`).

### Le tableau qui fait foi : trois tirages par condition

Modèle livré, prompt corrigé (paragraphe suivant), `--repeat 3`, quatre conditions lancées **l'une
après l'autre**. Tableaux bruts, avec la fréquence de chaque échec :
[`results.x3.glossaire.md`](./results.x3.glossaire.md),
[`results.x3.sans-glossaire.md`](./results.x3.sans-glossaire.md),
[`results.x3.heldout.md`](./results.x3.heldout.md),
[`results.x3.heldout-sans.md`](./results.x3.heldout-sans.md).

| Corpus | Glossaire | Compilation | Forme | Exacte | Refus correct | Faux acceptés | p50/p95 | Coût/instr. |
|---|---|---|---|---|---|---|---|---|
| 74 phrases | sans | 81 % | 78 % | 69 % | 91 % | 0 % | 484 / 974 ms | 0,00010 $ |
| 74 phrases | **avec** | **94 %** | 90 % | **85 %** | 87 % | **4 %** | 1 593 / 4 503 ms | 0,00007 $ |
| 24 tenues à l'écart | sans | 76 % | 76 % | 65 % | 86 % | 0 % | 471 / 633 ms | 0,00010 $ |
| 24 tenues à l'écart | **avec** | **94 %** | 94 % | **86 %** | 76 % | 0 % | 785 / 1 402 ms | 0,00007 $ |

**Le gain tient hors du corpus, et il y est au moins aussi grand** : +16 points d'exactitude sur le
corpus, +21 sur les phrases tenues à l'écart. Ce n'est donc pas un corpus appris par cœur. Sur le
corpus, huit contraintes ratées à chaque tirage sans glossaire passent deux ou trois fois sur trois
avec, et ce sont celles que la section précédente attribuait au sens manquant : trois phrases de
regroupement (`grp-rue`, `grp-toujours`, `grp-familier` — `group` se lit enfin « être regroupé » ;
la quatrième, l'exemple canonique, passait déjà), trois de retour au van (`caisse-avant-retour`,
`fouille-avant-retour`, `coop-prevenir` — `ReachExtraction` se lit « le retour à la base »),
`coop-soin` et `attente-interdite`.

Le prix, dit avec le chiffre :

* **des faux acceptés reviennent : 4 % sur le corpus, contre 0 % sans.** Trois sur 78 :
  « Barricadez la porte du fond » deux fois, en `require_before(at(agent, cover), BarricadeDoor)` et
  `require_before(at(agent, mission), BarricadeDoor)`, et « Restez toujours à portée de voix les uns
  des autres » en `require_always(at(agent, group))`. Une sonde de quatre tirages sur les seuls refus
  en trouve 5 sur 104, dont deux **contresens** : `forbid(BarricadeDoor)` pour « Barricadez la porte
  du fond », `penalize(ReachExtraction, 3)` pour « Rentrez au camp dès que possible ». Donner le sens
  des symboles pousse aussi à en trouver un là où il n'y en a pas. Aucun sur les phrases tenues à
  l'écart. Ce qui protège le joueur reste l'affichage de la contrainte compilée ;
* **les refus corrects baissent** : de 91 % à 87 % sur le corpus, de 86 % à 76 % à l'écart. Ce sont
  des erreurs de famille, pas des traductions : `unknown_symbol` là où le corpus attend
  `preference_not_constraint` (`pref-rentrer`, trois fois sur trois) ou `not_expressible`
  (`ecart-24`, « Rapportez trois caisses », deux fois sur trois). `ecart-19` échoue dans les deux
  conditions ;
* **la latence monte, mais pas du double qu'annonçait le premier passage** : le p50 est 1,7 à 3,3
  fois plus lent, et le pire p95 mesuré est de 4,5 s, sous le `timeout_ms` de 8 000 du jeu. Le
  chiffre se lit avec prudence : lancée seule, une sonde a trouvé les appels avec glossaire *plus*
  rapides (0,9 s contre 1,3 s). Sur OpenRouter, le moment et l'hébergeur pèsent autant que la longueur
  du contexte ;
* **le coût ne monte pas** (0,00007 $ contre 0,00010 $), et la cause n'est pas établie : le cache du
  fournisseur en décide sans doute plus que les 4 000 caractères de plus.

Trois phrases échouent à chaque tirage, avec ou sans glossaire, et sont le chantier suivant :
`outil-barricade` (`has(agent, toolbox)` au lieu de `equipped(toolbox)`), `rue-interdite`
(« Ne traversez pas les rues à découvert » : refusée sans glossaire, `penalize` au lieu de `forbid`
avec) et `connu-avant-pharmacie`.

### Un quatrième défaut du prompt

« Soignez les blessés avant d'entrer », libellé d'une consigne pré-écrite, restait refusée avec le
glossaire. Une sonde de trois tirages par condition l'a trouvée refusée cinq fois sur six, et
**toujours avec le même motif** : « un enchaînement de deux actions ». Le prompt définissait
`conditional_not_supported` comme « un enchaînement imposé de plusieurs actions » — ce qui visait
« A, puis B, puis C » (`cond-chaine`) — et ne disait nulle part qu'un geste placé avant un autre
s'écrit par le fait qu'il laisse vrai. Le glossaire donnait `band_tended` ; il manquait le pont entre
le geste et son fait. C'est un défaut de *définition*, de la même famille que les trois de « Ce que la
réécriture du prompt a changé », et la correction est du même ordre : la famille couvre « une suite
imposée de trois gestes ou plus », et le test de l'antériorité dit qu'un geste mis en premier s'écrit
par son fait. Aucune phrase du corpus n'est entrée dans le prompt.

L'ancien et le nouveau prompt ont été comparés par une série à part — trois tirages chacun, avec
glossaire, lancés en parallèle : l'exactitude passe de 81 % à 88 % sur le corpus et de 86 % à 92 % à
l'écart. Le tableau ci-dessus, pris plus tard et l'un après l'autre, donne 85 % et 86 % au nouveau :
cohérent au bruit près, et c'est l'ordre de grandeur qu'il faut retenir. Les deux témoins de refus —
la suite de trois gestes, « Si Bob est blessé, soignez-le » — restent refusés à chaque tirage.

La phrase visée passe de 3 fois sur 6 à 7 fois sur 9, sondes et séries confondues : une tendance, pas
une preuve. Le gain de la série ne vient d'ailleurs pas d'elle — elle y passe deux fois sur trois
avec les deux prompts —, mais de phrases voisines que le même malentendu faisait refuser :
`attente-interdite` et `grp-familier` de 0 à 3 sur 3, `grp-rue` et `coop-prevenir` de 1 à 3.

### Le premier passage, et ce qu'il a fait dire à tort

La première mesure, avec l'ancien prompt, était **un passage unique par condition, les quatre
lancés en même temps** : [`results.glossaire.md`](./results.glossaire.md),
[`results.sans-glossaire.md`](./results.sans-glossaire.md), [`results.heldout.md`](./results.heldout.md),
[`results.heldout-sans.md`](./results.heldout-sans.md). Elle donnait 67 % → 88 % sur le corpus et
65 % → 94 % à l'écart, et le sens du gain était juste. Trois de ses conclusions étaient fausses, et
c'est la répétition qui l'a montré :

* **les « trois régressions » étaient du bruit.** Dans une sonde de trois tirages,
  `observe-avant-entree`, `attente-interdite` et `extraction-toujours`, perdues avec le glossaire,
  passent une ou deux fois sur trois avec, trois sur trois sans : des phrases instables, pas perdues ;
* **le « 0 % de faux acceptés » avec glossaire était de la chance** : c'est 4 % sur trois tirages ;
* **« la latence double » mesurait la charge** des quatre passages simultanés, pas le glossaire.

La cause est la même pour les trois : **à température nulle, le modèle livré ne répond pas deux fois
pareil** — OpenRouter répartit les appels entre plusieurs hébergeurs. Un passage unique varie de
plusieurs points, et une phrase ratée une fois ne dit rien. D'où `--repeat`.

Vingt-quatre phrases, c'est peu : une contrainte vaut six points sur un tirage, deux sur trois. Et
elles ont été écrites par la même main que le glossaire, sans le relire mais en le connaissant.

## Ce qui manque encore

Les ablations n'ont été mesurées que sur le **modèle livré**. Savoir si le gain du few-shot tient
pour un modèle plus faible — qui en aurait le plus besoin — ou s'il aggrave au contraire sa
complaisance demanderait de refaire les deux conditions sur deux ou trois lignes du tableau
ci-dessus. Le harnais ne demande qu'un `--model` de plus.
