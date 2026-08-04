# Livrable 14 — Recrutement de survivants et dialogues

> Voir [00-INDEX.md](./00-INDEX.md) pour le contexte général du projet.

**Priorité : P4 — Basse, et optionnelle.** Dernier livrable de la série. C'est le seul qui dépende d'un
service externe et d'un modèle de langage : il est donc le seul qui puisse être coupé sans que rien
d'autre cesse de fonctionner, et il doit le rester. Il vient en dernier parce qu'il a besoin du camp à
nourrir (livrable 11), de la fiche de personnage et du générateur de survivants (livrable 12) et de
lieux variés où faire des rencontres (livrable 13).

**Dépendances** : livrables 11, 12, 13. Il **absorbe** les livrables 9 (habillage narratif) et 10
(groupes de survivants indépendants) annoncés dans [00-INDEX.md](./00-INDEX.md), qui n'ont pas de
fichier propre.

Il ne dépend pas du [livrable 15](./15-instructions-tactiques.md), mais celui-ci passe avant dans
l'ordre de réalisation et aura donc **déjà créé le projet `service/`** : FastAPI, le commutateur de
fournisseur, le graphe LangGraph et le repli sur gabarit existent alors, et ce livrable n'ajoute qu'un
second endpoint. Si le 15 était abandonné, tout ce que la section « Architecture du service » décrit
resterait à écrire ici — c'est la seule conséquence, et elle est chiffrable.

## Objectif

Rencontrer d'autres survivants pendant une mission, les convaincre de rejoindre le camp, et faire parler
tout ce monde — entre eux et avec le joueur — au moyen d'un modèle de langage local.

## Règle non négociable

**Le modèle de langage ne décide de rien.**

L'issue d'une rencontre — recruté, méfiant, hostile, en fuite — est calculée par `Hordinary.Core` de
façon **déterministe**, à partir des mêmes ingrédients que le reste du jeu : personnalité, aptitudes,
état du camp, ce que chacun sait. Le service de dialogue reçoit ce résultat déjà décidé et n'a qu'une
tâche : l'écrire en français. Il ne peut pas transformer un refus en recrutement.

Trois conséquences, à respecter dans le code :

1. `Hordinary.Core` ne connaît ni HTTP, ni LangChain, ni FastAPI. Le service est appelé depuis
   `Scripts/`, au-dessus de la simulation ;
2. `dialogue.enabled: false` dans `config/game_config.json` remplace toute réplique générée par une
   réplique de gabarit, et le jeu reste **intégralement jouable**, y compris le recrutement ;
3. la suite de tests passe entièrement avec le service arrêté. Aucun test ne dépend d'un modèle.

C'est la contrainte « aucun service externe » du MVP, appliquée à la lettre : ce livrable ajoute une
couche de langage par-dessus un jeu complet, jamais un jeu qui a besoin de langage pour tourner.

## Perception partielle et dialogue

Le principe central du projet vaut aussi ici : **un survivant ne peut dire que ce qu'il sait.**

Le contexte envoyé au modèle est construit à partir de l'`AgentKnowledge` du locuteur, jamais du
`WorldState`. Un survivant qui n'a pas vu la horde ne peut pas la mentionner ; un survivant qui a reçu
l'information d'un allié la mentionne avec la source et la confiance que le livrable 7 lui a données
(« Bob dit qu'il y en a une dizaine près du hangar »).

C'est aussi ce qui rend ce livrable intéressant plutôt que décoratif : le dialogue devient une fenêtre
sur la carte mentale, au même titre que les overlays du mode debug.

## Rencontres

### Génération d'une rencontre

Pendant une mission, une rencontre peut être posée par la passe de peuplement du livrable 13, ou
déclenchée par la découverte d'un lieu. Le survivant rencontré est produit par le générateur du
livrable 12 — il a donc une personnalité, des aptitudes, des compétences et une apparence, comme
n'importe quel membre du camp.

Types de rencontres (`config/encounters.json`, données) :

| id | Situation | Issue mécanique possible |
|----|-----------|--------------------------|
| `piege` | Bloqué dans une pièce, entouré | Sauvé → recrutement très probable ; abandonné → mort |
| `blesse` | À terre, saigne | Soigné (coûte un `medikit`) → recrutement probable |
| `mefiant` | Armé, retranché, ne veut pas d'ennuis | Négociation ; peut demander un don de nourriture |
| `pillard` | Veut ce que le groupe porte | Hostile, combat ou renoncement |
| `groupe` | Deux à quatre survivants ensemble | Recrutement en bloc, ou refus en bloc |

### Résolution déterministe

Un score, calculé exactement comme un score de Utility AI — considérations primitives, courbes de
réponse, combinaison — et une graine. Les seuils vivent dans `config/encounters.json`.

```python
confiance = combine(
    curve(rencontre.detresse,              "linear"),
    curve(aide_apportee,                   "linear"),      # soigné, sauvé, nourri
    curve(negociateur.personality.loyalty, "linear"),
    curve(negociateur.aptitude("support"), "linear"),
    curve(camp.reputation,                 "linear"),
    curve(camp.rations_par_survivant,      "linear"),      # un camp affamé ne recrute pas
    curve(menace_percue,                   "inverse"),
)
```

Seuils : `confiance >= seuil_recrutement` → rejoint le groupe ; entre les deux seuils → reste méfiant,
peut être retenté plus tard ; en dessous de `seuil_hostilite` → fuit ou attaque.

Un survivant recruté suit le groupe jusqu'à l'extraction — il occupe une place dans le van (livrable 12)
et une ration au camp (livrable 11). Recruter a un coût : c'est ce qui en fait une décision.

### Réputation du camp

Une valeur unique, persistante entre missions, dans la sauvegarde du camp. Elle monte quand le groupe
sauve, soigne ou donne ; elle descend quand il abandonne un blessé ou dépouille un survivant. Elle entre
dans le calcul ci-dessus, et dans rien d'autre.

## Dialogues

### Où ils apparaissent

* **Rencontre** : la négociation, entre le survivant qui parle et celui d'en face ;
* **Entre survivants** : de courts échanges pendant une mission, déclenchés par un événement déjà
  existant — une replanification, un partage de connaissances, un allié blessé, une horde repérée. Le
  `DecisionJournal` ([DecisionJournal.cs](../Hordinary.Core/AI/Replanning/DecisionJournal.cs)) fournit
  déjà les événements : le dialogue les habille, il n'en invente pas ;
* **Avec le joueur** : au camp, un survivant peut être interrogé — ce qu'il a vu, ce qu'il pense de la
  prochaine mission, de quoi il a besoin. Réponses contraintes à ce qu'il sait.

### Architecture du service

```text
Godot (Scripts/Dialogue/DialogueClient.cs)
   │  HTTP POST /dialogue, JSON, asynchrone, timeout court
   ▼
FastAPI  (service/  — projet Python séparé, hors solution .NET)
   │
   ▼
LangGraph — graphe à cinq nœuds
   │
   ├── 1. context      normalise la requête ; ne garde que ce que le locuteur SAIT
   ├── 2. persona      compose le prompt système depuis le profil (rôle, personnalité,
   │                   description, traits) — les mêmes données que config/survivors.json
   ├── 3. generate     appel LangChain au fournisseur de modèle
   ├── 4. guardrail    valide la sortie ; en cas d'échec, une reprise, puis gabarit
   └── 5. project      renvoie la réplique ET l'issue mécanique reçue en entrée, inchangée
   │
   ▼
Fournisseur de modele : OpenRouter OU Ollama (voir ci-dessous)
```

Le projet `service/` est **le même** que celui du livrable 15 : deux endpoints, `/dialogue` et
`/instruction`, un seul déploiement, un seul commutateur de fournisseur.

### Choix du fournisseur

Un commutateur, `dialogue.provider`, deux valeurs. LangChain rend les deux interchangeables derrière la
même interface de modèle de conversation.

| | `ollama` | `openrouter` |
|---|---|---|
| Modèles | local (`phi4-mini`, `qwen2.5:7b-instruct`…) | catalogue distant, plusieurs familles |
| Coût | nul | facturé au jeton, et **un dialogue en appelle beaucoup** |
| Réseau | aucun | requis |
| Déterminisme | `seed` honoré | selon le fournisseur amont, **non garanti** |
| Usage visé | jouer — c'est le défaut livré | comparer des modèles sur la qualité des répliques |

Ollama est le mode normal de ce livrable, et pas par principe : le bavardage entre survivants est le
seul appel **récurrent** de tout le projet. Six survivants sur une mission de dix minutes en produisent
des dizaines, sans aucune valeur mécanique. Le facturer au jeton contredirait l'objectif « jeu
vendable » — un joueur n'a pas à payer pour que ses personnages se parlent.

OpenRouter existe donc ici pour une seule chose : **comparer des modèles hors ligne**, sur un corpus de
scènes figées, afin de choisir lequel installer en local. C'est l'inverse du livrable 15, où la
comparaison distante est le mode de travail normal.

**Le déterminisme de la partie ne vient jamais du modèle.** Il vient du cache
`(kind, seed, situation, locuteur)`, et surtout du fait que l'issue mécanique est décidée par
`Hordinary.Core` **avant** tout appel. Un fournisseur distant non déterministe ne rend donc aucune
partie irreproductible : il ne change que la formulation, et seulement à la première exécution d'une
scène.

### Contrat d'API

```jsonc
// POST /dialogue
{
  "kind": "encounter",              // encounter | banter | player_question
  "seed": 20260803,                 // même graine, même réplique de repli
  "speaker": {
    "name": "Claire", "role": "Secouriste",
    "personality": { "courage": 0.4, "loyalty": 0.9, "caution": 0.7 },
    "traits": ["insomniaque"],
    "state": { "health_ratio": 0.8, "panic": 0.2, "sanity_ratio": 0.6 }
  },
  "listener": { "name": "Inconnu", "situation": "blesse" },
  "known_facts": [                  // issu de l'AgentKnowledge, JAMAIS du WorldState
    { "fact": "zombies_pres_du_hangar", "count": 4, "confidence": 0.6, "source": "ally:bob" },
    { "fact": "camp_rations", "days": 2, "confidence": 1.0, "source": "direct" }
  ],
  "outcome": "recruited",           // DÉJÀ décidé par Hordinary.Core
  "max_chars": 220
}

// 200 OK
{ "line": "...", "outcome": "recruited", "fallback_used": false, "latency_ms": 380 }
```

`outcome` est renvoyé tel qu'il a été reçu. Le client **vérifie** cette égalité et ignore la réponse si
elle diffère : le service n'a aucun moyen, même en cas de bug ou de sortie aberrante, d'influer sur la
partie.

### Garde-fous (nœud 4)

Rejettent la génération et déclenchent une reprise, puis le gabarit :

* longueur au-delà de `max_chars` ;
* mention d'un fait absent de `known_facts` — c'est le garde-fou le plus important, celui qui protège la
  perception partielle ;
* rupture de personnage (le modèle parle de lui-même, d'un jeu, d'un modèle) ;
* langue autre que le français ;
* réponse vide, ou identique à la précédente réplique du même locuteur.

### Performance et intégration

* appel **asynchrone**, jamais dans la boucle de jeu ; une réplique en retard s'affiche en retard ou pas
  du tout, elle ne fait jamais attendre la simulation ;
* `timeout_ms` court (800 par défaut) ; au-delà, gabarit ;
* budget d'appels : `max_concurrent` et `min_interval_between_banter` pour que six survivants ne
  bavardent pas en même temps ;
* les répliques générées sont mises en cache par `(kind, seed, situation, locuteur)` : rejouer la même
  scène ne recoûte pas un appel.

### Configuration

```json
{
  "dialogue": {
    "enabled": false,
    "provider": "ollama",
    "endpoint": "http://127.0.0.1:8001/dialogue",
    "ollama": { "model": "phi4-mini", "base_url": "http://127.0.0.1:11434" },
    "openrouter": {
      "model": "anthropic/claude-haiku-4.5",
      "api_key_env": "OPENROUTER_API_KEY",
      "base_url": "https://openrouter.ai/api/v1",
      "max_cost_usd_per_session": 0.25
    },
    "timeout_ms": 800,
    "max_concurrent": 2,
    "min_interval_between_banter": 20,
    "max_chars": 220,
    "retries": 1,
    "cache_enabled": true
  }
}
```

Livré à `false`. C'est un supplément, pas une dépendance.

La clé d'API se lit dans une variable d'environnement, **jamais** dans un fichier de configuration
versionné. `max_cost_usd_per_session` est un plafond dur côté client : dépassé, le dialogue bascule sur
les gabarits et le signale dans le journal — le même repli que le timeout, déclenché par une autre
cause.

Le `timeout_ms` de 800 reste valable quel que soit le fournisseur, et c'est une contrainte réelle sur le
choix d'un modèle distant : au-delà, le gabarit s'affiche et l'appel est perdu. Un modèle qui répond en
1,5 s est inutilisable **ici**, alors qu'il conviendrait très bien au livrable 15, dont le budget est
de huit secondes.

## Architecture concernée

`Game/Encounter`, `Game/EncounterResolver`, `Game/CampReputation` (dans `Hordinary.Core`, déterministes,
testables sans réseau) ; `Scripts/Dialogue/DialogueClient.cs` (Godot, appels HTTP) ; `service/` (projet
Python : FastAPI, LangChain, LangGraph, `requirements.txt`, `README`, `docker-compose` optionnel). Voir
[A-architecture-et-donnees.md](./A-architecture-et-donnees.md).

Nouveaux fichiers de configuration : `config/encounters.json`, section `dialogue` dans
`config/game_config.json`.

## Étapes à créer

* [ ] Ajouter le modèle de rencontre et `config/encounters.json` (cinq types).
* [ ] Poser les rencontres depuis la passe de peuplement du livrable 13.
* [ ] Générer le survivant rencontré via le générateur du livrable 12.
* [ ] Implémenter le résolveur déterministe (considérations, courbes, seuils, graine).
* [ ] Ajouter la réputation du camp, persistante entre missions.
* [ ] Intégrer un survivant recruté : place dans le van, ration au camp, entrée au roster.
* [ ] Écrire les gabarits de repli pour chaque type de rencontre et d'échange.
* [ ] **Vérifier ici que le jeu est complet et jouable, dialogue coupé.** Tout ce qui suit est du
      supplément.
* [ ] Créer le service `service/` : FastAPI, endpoint `/dialogue`, `/health`.
* [ ] Construire le graphe LangGraph à cinq nœuds.
* [ ] Implémenter le commutateur de fournisseur `ollama` / `openrouter` via LangChain, et le plafond de
      coût par session.
* [ ] Implémenter les garde-fous, en particulier le contrôle des faits hors `known_facts`.
* [ ] Écrire `DialogueClient.cs` : appels asynchrones, timeout, cache, budget, repli sur gabarit.
* [ ] Vérifier côté client que l'`outcome` renvoyé est identique à celui envoyé.
* [ ] Construire le contexte de dialogue depuis `AgentKnowledge`, jamais depuis `WorldState`.
* [ ] Ajouter l'interface de dialogue (bulle en jeu, panneau de rencontre, interrogation au camp).
* [ ] Documenter l'installation du service dans le README.

## Critère de validation

Deux validations, dans cet ordre :

1. **Service arrêté, `dialogue.enabled: false`** : une rencontre se joue entièrement, le survivant est
   recruté ou refuse selon un calcul reproductible à graine donnée, les répliques sont des gabarits, et
   la totalité des tests passe.
2. **Service démarré** : la même rencontre, avec la même graine, produit la même issue mécanique et des
   répliques écrites — dans lesquelles aucun survivant ne mentionne un fait qu'il n'a pas observé ou
   reçu d'un allié.

## Tests associés

À ajouter dans `Tests/` (voir [C-tests.md](./C-tests.md)) — tous exécutables sans réseau :

* deux résolutions de rencontre avec la même graine donnent la même issue ;
* un camp aux rations basses recrute moins qu'un camp approvisionné, toutes choses égales par ailleurs ;
* un survivant abandonné fait baisser la réputation, un survivant soigné la fait monter ;
* le résolveur de rencontre n'accède jamais au `WorldState` ;
* le contexte de dialogue construit ne contient que des faits présents dans l'`AgentKnowledge` du
  locuteur ;
* avec `dialogue.enabled: false`, aucune requête réseau n'est émise et chaque réplique a un gabarit ;
* un service qui renvoie un `outcome` différent de celui envoyé voit sa réponse ignorée ;
* un service qui ne répond pas dans le `timeout_ms` n'empêche pas la mission de se poursuivre ;
* `Hordinary.Core` ne référence aucun client HTTP.

Côté service, dans `service/tests/` :

* le garde-fou rejette une réplique mentionnant un fait absent de `known_facts` ;
* le garde-fou rejette une réplique hors longueur, hors personnage ou dans une autre langue ;
* le nœud `project` renvoie toujours l'`outcome` reçu, quelle que soit la génération ;
* le commutateur de fournisseur produit le même contrat de sortie sur `ollama` et sur `openrouter` ;
* le plafond de coût coupe les appels et bascule sur les gabarits.

## Sur le RAG

**Il n'y en a pas ici, et c'est délibéré.** Ce qu'un survivant sait vit dans son `AgentKnowledge` : un
état structuré, typé, de quelques dizaines de faits. Le transmettre est un filtre — « ne garde que ce
que le locuteur sait » — pas une recherche par similarité, et l'appeler récupération documentaire
serait un abus de vocabulaire. Le profil du locuteur tient en une centaine d'octets. Il n'existe aucun
corpus dans ce projet.

Le raisonnement complet, avec la seule ouverture honnête vers de la vraie récupération, est écrit une
fois pour toutes au livrable 15, section « Sur le RAG ».
