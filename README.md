# Hordinary

*[English version](README.en.md)*

**Un jeu de survie tactique où le joueur ne pilote personne.** Il donne des objectifs ; trois
agents autonomes perçoivent, se souviennent, planifient, se coordonnent — et changent d'avis.

<!-- TODO : remplacer par un GIF de 90 s montrant une replanification en direct -->
![Démo](docs/demo.gif)

---

## Ce que ce projet démontre

Une architecture d'agents autonomes complète, écrite from scratch, **sans moteur de planification
ni bibliothèque d'IA externe** :

| Couche | Ce qu'elle résout |
|---|---|
| **Utility AI** | Sélection d'objectif : 9 buts composés à partir de 12 considérations primitives réutilisables, chacune passée par une courbe de réponse ; hystérésis anti-oscillation. Un but est une **donnée** — ajouter un objectif ne recompile rien |
| **Planification symbolique** | Task planning en espace d'états. Le domaine est exporté en **PDDL** valide et versionné ([`tools/pddl/`](tools/pddl/)) — un seul compilateur produit le texte lu par un planificateur tiers *et* le domaine que le jeu exécute. Recherche **GBFS** avec filtrage des actions par pertinence |
| **Replanification** | Exécution surveillée à 2 Hz sur 10 déclencheurs ; réparation de plan plutôt que replanification complète ; journal de décisions traçable |
| **Observabilité partielle** | Un agent ne peut pas raisonner sur ce qu'il n'a pas observé. Champ de vision, ligne de vue, perception sonore |
| **États de croyance** | Carte mentale individuelle : une position vieillit, une trajectoire s'extrapole, un souvenir se dégrade, une hypothèse s'invalide |
| **Coordination multi-agents** | Partage de croyances à portée de communication uniquement ; répartition de l'exploration par frontières ; annonce d'intention |
| **Navigation A\*** | Coûts pondérés par le danger **mémorisé** — l'agent contourne ce qu'il croit dangereux, pas ce qui l'est |

Le point d'architecture central : **la logique de décision ne référence jamais le moteur de jeu.**
`Hordinary.Core` est un projet .NET pur, testable sans Godot ; la couche moteur n'est qu'un
afficheur et un fournisseur de perception.

**609 tests** : 551 en C# (`xUnit`, ~44 s) exécutés sans moteur, sur la carte et les fichiers de
`config/` réellement livrés — pas des copies — et 58 en Python sur le service de langage, sans
réseau ni clé d'API.

## Le LLM traduit, il ne décide pas

Le joueur donne ses consignes tactiques en français — « Évitez de forcer les portes », « Avant
d'entrer dans un bâtiment, regroupez-vous ». Un modèle les traduit dans une **grammaire fermée de
cinq formes**, puis **quatre portillons déterministes** décident, et eux seuls : validation
grammaticale et typée, compilation en préconditions PDDL, preuve de satisfiabilité par Fast Downward,
vérification de trajectoire sur chaque plan produit. Un modèle qui se trompe ne casse rien — il est
rattrapé.

Quinze modèles ont été soumis à un corpus de **66 phrases**, dont 26 qui *doivent* être refusées :
préférences déguisées en règles, conditionnelles, injection de prompt, consignes inexprimables.
Dossier complet : **[`service/evals/README.md`](service/evals/README.md)**.

| Modèle | Compilation | Refus corrects | **Faux acceptés** | p95 |
|---|---|---|---|---|
| `deepseek/deepseek-v4.1-flash` | 80 % | 85 % | **0 %** | 2 771 ms |
| `mistralai/mistral-medium-3-5` | 95 % | 81 % | 8 % | 3 376 ms |
| `inference-net/schematron-v2-turbo` | 90 % | 8 % | **85 %** | 6 201 ms |

La colonne qui décide est **faux acceptés**, pas compilation. `schematron-v2-turbo` compile 90 % des
phrases et serait premier d'un tableau malhonnête : il traduit *tout*, y compris l'injection de prompt
du corpus. C'est la justification de cette colonne, et elle est arrivée toute seule dans l'échantillon.

Trois résultats que seule l'exécution a donnés : le plafond de qualité était **le prompt et non le
modèle** — 163 phrases traduisibles refusées contre 29 traduites de travers, et une réécriture qui
fait passer un même modèle de 42 % à 78 % de compilation, au prix mesuré de 4 points de faux
acceptés ; la
**latence décidait avant l'exactitude** (les meilleurs taux de la première génération avaient des p95
de 22 s et 267 s) ; et trois des modèles les plus récents du catalogue **refusent qu'on désactive leur
raisonnement**, ce qui les exclut d'un usage temps réel.

## Démarrage

```bash
dotnet build Hordinary.sln
dotnet test Tests/Hordinary.Tests.csproj   # 551 tests, sans Godot
```

Le service de langage est optionnel et a sa propre suite (Python 3.12) :

```bash
cd service
python -m venv .venv && ./.venv/Scripts/python.exe -m pip install -r requirements.txt
./.venv/Scripts/python.exe -m pytest tests/ -q          # 58 tests, sans réseau
```

Puis ouvrir le dossier dans **Godot 4.7** et lancer `Scenes/Main.tscn`.

Choisir ses agents, choisir une mission, `DÉMARRER`. Les touches `F1`–`F7` ouvrent les overlays
d'observation : carte mentale, scores Utility AI, plan en cours, journal des décisions.

## Aucune valeur de gameplay n'est codée en dur

Le domaine de planification, les objectifs, les profils d'agents et les missions sont des fichiers
JSON dans [`config/`](config/). Ajouter une action au planificateur ou un objectif à l'Utility AI
ne demande pas de recompiler.

## Aller plus loin

* **[Architecture détaillée](docs/architecture.md)** — chaque système, ses choix de conception, et
  ses limites connues assumées.
* **[Spécifications](Specs/00-INDEX.md)** — les 8 livrables du MVP et les extensions, avec leur
  critère de validation.

## Stack

Godot 4.7 · C# / .NET 8 · xUnit — **le jeu n'appelle aucun service externe.**

Un service de traduction optionnel vit à part, dans [`service/`](service/README.md) : FastAPI,
LangGraph, OpenRouter. Il transforme une phrase de joueur en contrainte de planification. Le jeu
tourne intégralement sans lui — `instructions.enabled` est livré à `false`, les instructions
pré-écrites portent les mêmes contraintes, et la suite de tests passe avec le service arrêté.

## Licence

Ce dépôt est sous **double licence** :

| Périmètre | Licence |
|---|---|
| Code source (`Hordinary.Core/`, `Scripts/`, `Tests/`, `config/`) | [MIT](LICENSE) |
| Ressources et contenu (`Assets/`, missions, textes, `Specs/F-*.md`) | [Tous droits réservés](Assets/LICENSE) |

© 2026 Raphaël JANSON
