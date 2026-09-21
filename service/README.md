# Service d'instructions tactiques

Traduit une phrase de joueur en **contrainte formelle** de la grammaire fermée du
[livrable 15](../Specs/15-instructions-tactiques.md).

> « Avant d'entrer dans un bâtiment, regroupez-vous. »
> → `require_before(at(agent, group), enter:building)`

**Le jeu tourne sans lui.** `instructions.enabled` est livré à `false` dans
`config/game_config.json`, et les instructions pré-écrites de `config/instruction_presets.json`
portent exactement les mêmes contraintes, en passant exactement les mêmes portillons. Ce que ce
service ajoute est la **traduction**, pas la mécanique.

La suite de tests .NET passe entièrement avec ce service arrêté. Aucun test ne dépend d'un modèle.

## Ce que le modèle a le droit de faire

**Il traduit. Il ne décide de rien.** Sa sortie est l'une de cinq formes, ou un refus motivé — jamais
du PDDL, jamais un patch de domaine, jamais du texte libre à afficher tel quel. La totalité du PDDL
donné à un planificateur sort du compilateur déterministe de `Hordinary.Core`.

`status: "constraint"` ne veut pas dire « appliquée » : les quatre portillons tournent ensuite, côté
jeu, et peuvent encore refuser.

## Installation

Python 3.11 ou plus — éprouvé sur **CPython 3.12.10**.

```bash
cd service
python -m venv .venv
. .venv/bin/activate            # Windows : .venv\Scripts\activate
pip install -r requirements.txt
```

La clé d'API se met dans l'environnement, **jamais dans un fichier** :

```bash
export OPENROUTER_API_KEY=sk-or-...        # Windows : $env:OPENROUTER_API_KEY = "sk-or-..."
```

`config/game_config.json` ne porte que le *nom* de cette variable (`instructions.openrouter.api_key_env`).
Un dépôt est publié ; une clé qui y entre est publiée avec lui.

## Lancer

```bash
uvicorn app.main:app --host 127.0.0.1 --port 8001
```

Puis, côté jeu, passer `instructions.enabled` à `true` dans `config/game_config.json`. Le champ de
saisie du panneau d'instructions accepte alors du français ; sans le service, il attend une contrainte
de la grammaire.

Le service **démarre sans clé** : `/health` répond, et `/instruction` refuse avec
`provider_unavailable`. C'est voulu — refuser de démarrer obligerait à configurer OpenRouter pour
seulement vérifier que le jeu se replie correctement, c'est-à-dire à payer pour tester le cas gratuit.

## Réglages

Tous par variable d'environnement, tous avec un défaut utilisable :

| Variable | Défaut | |
|---|---|---|
| `OPENROUTER_API_KEY` | — | sans elle, le service refuse tout appel |
| `HORDINARY_MODEL` | `qwen/qwen3.8-flash` | |
| `HORDINARY_BASE_URL` | `https://openrouter.ai/api/v1` | |
| `HORDINARY_MAX_COST_USD` | `0.50` | plafond dur, pour la durée du processus |
| `HORDINARY_TEMPERATURE` | `0.0` | |
| `HORDINARY_SELF_CHECK_RETRIES` | `1` | une reprise, puis abandon |
| `HORDINARY_TIMEOUT_S` | `20` | |

**Deux plafonds, pour deux pannes différentes.** Celui-ci protège d'un service oublié allumé ; celui
de `instructions.openrouter.max_cost_usd_per_session`, côté jeu, protège d'un jeu qui boucle. Atteint,
le client bascule sur les instructions pré-écrites **en le signalant** : un repli muet ferait croire au
joueur que le modèle a lu sa phrase.

## Le graphe

```
normalize → context → generate → self_check → project
                          ↑__________|
                        une reprise, puis abandon
```

| Nœud | |
|---|---|
| `normalize` | espaces réduits, casse effacée, coupe à `max_chars`, langue détectée |
| `context` | rend le digest du domaine en bloc de vocabulaire |
| `generate` | appel LangChain, sortie structurée demandée au fournisseur |
| `self_check` | revalide la sortie contre le digest ; une reprise **avec la raison**, puis abandon |
| `project` | une contrainte, ou un refus motivé. La seule sortie du graphe |

Le nœud 3 demande une sortie structurée, et le nœud 4 la revalide **de toute façon**. On ne fait
jamais confiance au drapeau `json_mode` : il réduit le taux d'échec, il ne le supprime pas.

### Le digest n'est pas écrit ici

`tests/domain.digest.json` est **généré par le jeu**
(`Tests/PddlArtifactsTests.Le_digest_du_service_est_a_jour`) et versionné. Le service ne fait que le
lire, et le jeu l'envoie à chaque requête.

C'est le point de toute l'architecture : le service et le portillon 1 lisent **la même source de
vérité** sur ce qui est nommable. Un symbole que le modèle a vu dans son contexte ne peut donc pas
être un symbole que la grammaire refuse ensuite. Un digest rédigé ici serait une seconde liste de
symboles, qui prendrait du retard sur `config/planner_domain.json` sans que rien ne le signale.

Changer le domaine du jeu sans régénérer casse la suite .NET :

```bash
HORDINARY_PDDL_REGEN=1 dotnet test Tests/Hordinary.Tests.csproj --filter "FullyQualifiedName~PddlArtifactsTests"
```

### Il n'y a pas de RAG, et c'est un choix

Ce qu'il faut donner au modèle — une trentaine d'actions, leurs prédicats, les classes de lieux —
pèse quelques kilo-octets et tient entièrement dans la fenêtre de contexte. Mettre une base
vectorielle devant huit kilo-octets de texte résout un problème qui n'existe pas.

La seule ouverture honnête est le few-shot sur un corpus d'instructions réellement accumulées, et
elle est traitée comme une **ablation à publier**, pas comme un acquis (voir ci-dessous).

## Tests

```bash
cd service
python -m pytest tests/ -q          # 50 tests, ~0,5 s
```

Aucun n'ouvre de connexion et aucun ne demande de clé : le fournisseur est un `ScriptedProvider` à qui
chaque test donne la réponse qu'il veut éprouver. Ce n'est pas une commodité — le nœud `self_check` a
une reprise, et l'éprouver demande un fournisseur qui se trompe d'abord et se corrige ensuite, ce
qu'aucun vrai modèle ne fait sur commande.

Ce que ces tests ne vérifient pas : que le modèle traduit bien. Cela ne se teste pas, cela se mesure.

## Évaluation

Soixante-six phrases de joueur dans `evals/utterances.jsonl`, chacune avec son attendu — une
contrainte exacte, ou un refus avec sa famille. Le corpus contient des phrases ambiguës, des phrases
hors sujet, des préférences déguisées en règles, une injection, et des formulations que la grammaire
ne peut pas exprimer. Un corpus qui ne contient que des cas faciles ne mesure rien.

Il est attaché au domaine livré : `Tests/InstructionServiceTests` vérifie que **chaque contrainte
attendue passe le portillon 1**. Retirer une action du jeu casse le corpus bruyamment, plutôt que de
fausser un tableau quelques semaines plus tard.

```bash
python -m evals.run --dry-run                                   # vérifie le harnais, sans réseau
python -m evals.run --model qwen/qwen3.8-flash
python -m evals.run --model x --model y --model z               # la comparaison
python -m evals.run --model x --no-digest                       # ablation 1
python -m evals.run --model x --few-shot 4                      # ablation 2
```

La mesure à mettre en avant est le **taux de faux acceptés** — une contrainte plausible mais fausse —
y compris s'il est mauvais : c'est ce que les quatre portillons existent pour rattraper, et c'est ce
qui rend la démonstration honnête. Un tableau qui ne publierait que le taux de compilation dirait
qu'un modèle complaisant est le meilleur.

`evals/results.md` est la sortie de ces commandes. Le `--dry-run` en produit déjà une version, mais
elle ne mesure **que le harnais** : le fournisseur y est un bouchon qui refuse tout, si bien que son
« 0 % de faux acceptés » est un contrôle d'arithmétique et non un résultat. Le vrai tableau demande
une clé OpenRouter et des appels facturés ; le fichier est ignoré par git tant qu'il n'est pas réel.

Le rapport s'écrit sur disque **avant** d'être affiché. Ce n'est pas un détail : un passage complet
coûte des appels facturés, et la console Windows — en cp1252 — ne sait pas rendre les flèches du
tableau. Elle a déjà tué un passage entièrement réussi.

## Contrat d'API

```jsonc
// POST /instruction
{
  "utterance": "Avant d'entrer dans un batiment, regroupez-vous",
  "seed": 20260915,
  "domain_digest": { "domain_hash": "sha256:…", "predicates": [ … ], "objects": [ … ] },
  "max_chars": 200
}

// 200 OK — accepté
{
  "status": "constraint",
  "constraint": { "form": "require_before", "predicate": "at(agent, group)",
                  "action_class": "enter:building", "factor": null },
  "rationale": "regroupement exige avant tout deplacement vers un lieu de type batiment",
  "model": "qwen/qwen3.8-flash",
  "latency_ms": 1240,
  "tokens": { "prompt": 890, "completion": 64 },
  "cost_usd": 0.0012
}

// 200 OK — refusé par le service lui-même
{
  "status": "rejected",
  "reason": "preference_not_constraint",
  "message": "« priorite aux blesses » exprime une preference d'objectif, pas une regle de plan"
}
```

Deux écarts au contrat de la spécification, tous deux nécessaires et documentés :

* **`domain_digest.objects`** — le portillon 1 refuse tout objet qu'il ne connaît pas, et l'exemple
  canonique du livrable cite `group`, qui est un repère de `config/planner_domain.json` et non un type
  ni une catégorie de lieu. Sans cette liste, le modèle devait deviner un vocabulaire dont la
  grammaire exige pourtant l'exactitude ;
* **`constraint.factor`** — sans lui, `penalize(A, k)` était intraduisible : « évitez de forcer les
  portes » n'a pas de sens sans un `k`. Les bornes restent celles du jeu
  (`instructions.min_penalty` / `max_penalty`), donc le laisser libre ici ne libère rien.

Le service ne renvoie jamais autre chose que ces deux formes. Un test le vérifie grossièrement
(`test_le_contrat_de_sortie_ne_porte_jamais_de_pddl`), exprès : il attrape le jour où quelqu'un
voudra « juste renvoyer le domaine compilé pour déboguer ».

## Ce qui reste

* `evals/results.md` — demande une clé OpenRouter et des appels facturés ;
* **les versions ne sont pas figées.** `requirements.txt` ne pose que des planchers, et
  l'installation de référence a tiré `langgraph 1.2.11` et `langchain-core 1.6.3` — deux majeures
  au-dessus de `>=0.2.40` et `>=0.3.15`. Tout fonctionne, mais rien ne garantit que la prochaine
  installation tirera la même chose : il manque un jeu de versions consigné ;
* le portillon 3 (Fast Downward) reste **hors ligne** et indépendant de ce service : voir
  [`tools/pddl/README.md`](../tools/pddl/README.md).
