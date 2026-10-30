# Hordinary

*[English version](README.en.md)*

**Un LLM traduit les consignes du joueur, des vérifications déterministes décident, des agents
autonomes planifient.** Dans ce jeu de survie tactique, le joueur ne pilote personne. Il écrit en
français « Soignez les blessés avant d'entrer » ; un modèle de langage en fait une contrainte
formelle ; quatre portillons la valident, la compilent en PDDL et vérifient chaque plan ; et trois
agents, qui ne voient que ce qu'ils perçoivent, changent leur façon de jouer.

![Démonstration du jeu](docs/demo.gif)

**Conçu, pensé et designé par un humain, exécuté avec Claude Code.**

## Architecture de la solution IA

```mermaid
flowchart LR
    Joueur[Consigne du joueur] --> Service[Service de langage]
    Service --> LLM[LLM : traduction en contrainte]
    LLM --> Validation[Validation déterministe : grammaire et types]
    Validation --> Compilation[Compilation vers le domaine PDDL]
    Compilation --> Plan[Planification individuelle des agents]
    Compilation -. Artefacts hors ligne .-> Verification[Fast Downward : satisfiabilité]
    Plan --> Trajectoire[Vérification de chaque plan]
    Trajectoire --> Execution[Exécution et replanification]
    Validation -. Refus .-> Retour[Contrainte ou refus affiché au joueur]
    Validation --> Retour
    Trajectoire -. Artefacts hors ligne .-> VAL[VAL : corroboration]
    Presets[Consignes pré-écrites] --> Validation
```

Le LLM interprète le langage naturel sans piloter les agents : les contrôles déterministes limitent
ce qui peut être exécuté, et les consignes pré-écrites offrent un fonctionnement sans service.
Les évaluations confrontent exactitude, faux acceptés, latence et coût ; elles ont notamment conduit
à renforcer le typage des arguments. Une contrainte valide peut néanmoins être sémantiquement fausse :
son affichage permet au joueur de la vérifier.

Sécurité : authentification locale, entrées bornées, quotas et dépendances vérifiées.
Voir les [frontières de confiance et limites](docs/securite.md), notamment le budget fournisseur
et le lancement avec `HORDINARY_SERVICE_TOKEN`. Les validateurs tiers interviennent hors ligne.

---

## Le LLM traduit, il ne décide pas

```
« Soignez les blessés avant d'entrer. »
      │  service de langage : FastAPI + LangGraph, via OpenRouter
      ▼
require_before(band_tended, enter:building)        une grammaire fermée de cinq formes
      │  portillon 1 : grammaire, symboles et types des arguments          (C#, déterministe)
      │  portillon 2 : compilation en préconditions du domaine PDDL
      │  portillon 3 : preuve de satisfiabilité par Fast Downward          (hors ligne)
      ▼
MoveToLocation(patient) -> TendAlly -> EnterBuilding(objective_zone) -> …   plan GBFS d'un agent
      │  portillon 4 : vérification de trajectoire sur chaque plan produit, corroborée par VAL
      ▼
exécution, replanification, bilan de mission
```

Le modèle n'a aucun pouvoir : il propose, le code tranche. Un modèle qui se trompe ne casse rien, il
est rattrapé ; un modèle absent non plus. Le jeu tourne sans le service, sur des consignes
pré-écrites qui passent **exactement les mêmes portillons**. Toutes les décisions d'ingénierie
autour de l'appel (cache, plafond de dépense, délai, reprise, repli) vivent dans le cœur C#, et le
transport est un simple délégué : le cœur ne peut pas référencer de client HTTP, il n'y a nulle part
où en mettre un.

Le joueur voit toujours **la contrainte compilée**, pas un accusé de réception, et un refus s'affiche
au même endroit qu'une acceptation, avec le portillon qui l'a produit.

## Évalué, pas supposé

Corpus de **76 phrases**, dont un tiers *doit* être refusé : préférences déguisées en règles
(« Soyez prudents »), conditionnelles (« Si un zombie vous repère, fuyez »), injection de prompt,
consignes claires mais inexprimables. Plus **24 phrases tenues à l'écart**, écrites pour vérifier
que le gain n'est pas un corpus appris par cœur. Dossier complet :
**[`service/evals/README.md`](service/evals/README.md)**.

Modèle livré (`deepseek/deepseek-v4.1-flash`, raisonnement désactivé), **trois tirages par
condition** :

Ces mesures précèdent le durcissement du service ; la nouvelle borne de sortie et les règles
d'arrêt demandent une réévaluation avant de leur attribuer les mêmes taux.

| Corpus | Compilation | Exacte | Refus correct | **Faux acceptés** | p95 |
|---|---|---|---|---|---|
| 74 phrases du corpus | 94 % | 85 % | 87 % | **4 %** | 4,5 s |
| 24 phrases tenues à l'écart | 94 % | 86 % | 76 % | **0 %** | 1,4 s |

La colonne qui décide est **faux acceptés** : une irrecevable traduite quand même. Sur les quinze
modèles soumis, `inference-net/schematron-v2-turbo` compile 90 % des phrases, et serait premier
d'un tableau qui ne publierait que cette colonne. Il traduit *tout*, injection de prompt comprise :
**85 % de faux acceptés**.

Ce que les mesures ont appris, et qu'aucune relecture n'aurait donné :

* **Le plafond était le prompt, pas le modèle.** 163 phrases traduisibles refusées contre 29
  traduites de travers. Trois défauts de *définition* dans le prompt système, et une réécriture qui
  fait passer un même modèle de 42 % à 78 % de compilation, mesurée sur un témoin où seul le prompt
  change.
* **Deux ablations isolent chaque composant.** Sans le vocabulaire du domaine envoyé au modèle,
  0 % de compilation : ce n'est pas une aide, c'est le substrat de la tâche. Avec trois exemples,
  +6 points de compilation, −3 de refus correct : montrer comment traduire pousse à traduire.
* **Un glossaire du domaine** (une ligne de sens par symbole) fait passer l'exactitude de 69 % à
  85 % sur le corpus, et de **65 % à 86 % sur les phrases tenues à l'écart**. Son prix est publié :
  de 0 % à 4 % de faux acceptés.
* **Un passage unique ment.** À température nulle, le modèle ne répond pas deux fois pareil.
  Le premier passage du glossaire a tiré trois conclusions fausses (des « régressions » qui étaient
  du bruit, un « 0 % » qui était de la chance) ; d'où `--repeat 3`.
* **La latence décide avant l'exactitude.** Les meilleurs taux de la première génération avaient un
  p95 de 22 s et 267 s, contre un délai de 8 s côté jeu. Trois des modèles les plus récents du
  catalogue refusent qu'on désactive leur raisonnement, ce qui les exclut d'un usage temps réel.
* **Le modèle le plus faible a trouvé un trou de sécurité.** `nemotron-3-super` a écrit une
  contrainte dont l'argument avait le mauvais type ; les portillons l'acceptaient, Fast Downward
  l'aurait refusée. Le typage des arguments est entré au portillon 1 : un portillon ne doit pas
  accepter ce qu'un validateur extérieur refuse.

Le faux accepté qui survit à toutes les conditions est documenté plutôt que caché : « Équipez une
arme avant d'attaquer » devient `equipped(objective)`. Bien formé, bien typé, satisfiable, et faux.
Le typage ferme le mauvais type, pas le mauvais référent ; c'est l'affichage de la contrainte qui
protège le joueur.

## Les agents qui exécutent

Écrits from scratch, **sans moteur de planification ni bibliothèque d'IA externe** :

| Couche | Ce qu'elle résout |
|---|---|
| **Utility AI** | Choix d'objectif : 9 buts notés sur des considérations passées par des courbes de réponse, avec hystérésis. Un but est une **donnée** : en ajouter un ne recompile rien |
| **Planification symbolique** | GBFS en espace d'états sur 30 actions, instances de 8 verbes primitifs décrits en JSON. Le domaine est exporté en **PDDL valide et versionné** ([`tools/pddl/`](tools/pddl/)) : un seul compilateur produit le texte lu par Fast Downward *et* le domaine que le jeu exécute |
| **Replanification** | Exécution surveillée à 2 Hz sur 10 déclencheurs ; réparation de plan plutôt que replanification complète ; journal des décisions |
| **Observabilité partielle** | Aucun système ne lit le monde à la place d'un agent : champ de vision, ligne de vue, perception sonore, et une carte mentale où une position vieillit et un souvenir se dégrade |
| **Coordination** | Partage de croyances à portée de voix (ou de radio) seulement, annonce d'intention, répartition de l'exploration par frontières, et cinq gestes d'entraide que seule une consigne du joueur déclenche |
| **Navigation A\*** | Coûts pondérés par le danger **mémorisé** : l'agent contourne ce qu'il croit dangereux, pas ce qui l'est |

La règle de conception qui relie les deux moitiés : **un réflexe reste au survivant, une stratégie
revient au joueur.** Fuir, se soigner, se battre sont des réflexes ; panser un camarade, faire
diversion, rappeler le groupe sont des décisions tactiques, et aucun agent ne les prend seul. C'est
la consigne du joueur qui leur donne une raison d'exister.

## Démonstration

Un mode démonstration joue **24 scénarios** sur des terrains dessinés pour qu'un comportement s'y
produise : cinq consignes, sept actions, sept coopérations, quatre stratégies combinées, et une
**carte blanche** où l'on écrit sa propre stratégie en français. Chaque scénario déclare ce qu'il doit
montrer, avec et sans sa consigne, et le vérifie deux fois : par la suite de tests, et dans le moteur
en headless.

| Scénario | Avec la consigne | Sans |
|---|---|---|
| `rappeler_tout_le_monde` : « Rappelez tout le monde au van avant de rentrer » | victoire à 10 s | 97 s |
| `renvoyer_au_van` : « Renvoyez un camarade au van avant de rentrer » | victoire à 16 s | 97 s |
| `sans_effraction_mais_arme` : une interdiction et une exigence | vitre cassée, jamais la porte | porte forcée |

Guide de tournage, touches d'observation et phrases sûres :
[`docs/enregistrer-la-demo.md`](docs/enregistrer-la-demo.md).

## Qualité

* **774 tests** : 685 en C# (xUnit, sans moteur) et 89 en Python (sans réseau ni clé). Les tests
  lisent la carte et les fichiers de `config/` **réellement livrés**, pas des copies.
* **À graine égale, résultat égal** : horde, quartier généré, mission rejouée. Verrouillé par tests.
* **Deux validateurs tiers** dans la boucle, par Docker : Fast Downward (satisfiabilité) et VAL
  (conformité des plans à la forme déclarative PDDL 3 de la consigne).
* **Faire tourner les scénarios a révélé douze défauts du moteur.** Sept sont corrigés, les autres
  documentés avec leur contournement
  ([`docs/architecture.md`](docs/architecture.md), « Défauts révélés »).

## Démarrage

Prérequis : SDK .NET 10 pour la suite de tests, et Godot 4.7.1 avec prise en charge .NET pour
le jeu. Le cœur du jeu cible .NET 8.

```bash
dotnet build Hordinary.sln
dotnet test Tests/Hordinary.Tests.csproj                    # 685 tests, sans Godot
```

Le service de langage est optionnel, avec sa propre suite (Python 3.12) :

```bash
cd service
python -m venv .venv && ./.venv/Scripts/python.exe -m pip install -r requirements.lock
./.venv/Scripts/python.exe -m pytest tests/ -q              # 89 tests, sans réseau
./.venv/Scripts/python.exe -m evals.run --dry-run           # le harnais d'évaluation, sans réseau
```

Ou en conteneur, la clé OpenRouter restant dans l'environnement du shell. Avec une clé fournisseur,
générer le jeton local puis lancer Godot depuis ce même shell :

```powershell
$env:HORDINARY_SERVICE_TOKEN = python -c "import secrets; print(secrets.token_urlsafe(32))"
docker compose up -d service                                # 127.0.0.1:8001
```

Puis ouvrir le dossier dans **Godot 4.7** et lancer `Scenes/Main.tscn`. En ligne de commande :
`-- --demo=<scénario>` lance une prise, `-- --ia` allume le service de langage et
`-- --observateur` ouvre les touches `F1` à `F7` (plan, journal, scores, carte mentale), sans
toucher aux fichiers livrés.

## Limites assumées

* **Une consigne est collective, un plan est individuel.** Chaque agent planifie seul : sous trois
  consignes d'entraide, il ne se passe rien si aucun agent ne peut toutes les remplir seul.
  Répartir des rôles demanderait de la planification multi-agents. La prise qui empilait les trois
  a été retirée : le harnais la gagne, le moteur la perd, et l'écart n'est pas résolu.
* **Les évaluations demandent des appels facturés** et ne sont pas rejouées par la suite de tests ;
  le `--dry-run` valide le harnais, pas les modèles.
* **Le générateur de survivants** (livrable 12) n'est pas écrit : le groupe est fixe.

## Aller plus loin

* **[Évaluation des modèles](service/evals/README.md)** : la pièce maîtresse, mesures et méthode.
* **[Architecture](docs/architecture.md)** : chaque système, ses choix et ses limites.
* **[Service de langage](service/README.md)** : graphe, contrat d'API, conteneur.
* **[Spécifications](Specs/00-INDEX.md)** : les livrables et leurs critères de validation.

## Stack

Godot 4.7 · C# / .NET 8 · xUnit · Python 3.12 · FastAPI · LangGraph · OpenRouter · PDDL ·
Fast Downward · VAL · Docker

## Licence

Ce dépôt est sous **double licence** :

| Périmètre | Licence |
|---|---|
| Code source (`Hordinary.Core/`, `Scripts/`, `Tests/`, `config/`) | [MIT](LICENSE) |
| Ressources et contenu (`Assets/`, missions, textes, `Specs/F-*.md`) | [Tous droits réservés](Assets/LICENSE) |

© 2026 Raphaël JANSON
