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
| **Utility AI** | Sélection d'objectif : 13 buts pondérés par des courbes de réponse, avec hystérésis anti-oscillation |
| **Planification symbolique** | Task planning en espace d'états, représentation inspirée de **PDDL** (prédicats, préconditions, effets), recherche **GBFS** avec filtrage des actions par pertinence |
| **Replanification** | Exécution surveillée à 2 Hz sur 10 déclencheurs ; réparation de plan plutôt que replanification complète ; journal de décisions traçable |
| **Observabilité partielle** | Un agent ne peut pas raisonner sur ce qu'il n'a pas observé. Champ de vision, ligne de vue, perception sonore |
| **États de croyance** | Carte mentale individuelle : une position vieillit, une trajectoire s'extrapole, un souvenir se dégrade, une hypothèse s'invalide |
| **Coordination multi-agents** | Partage de croyances à portée de communication uniquement ; répartition de l'exploration par frontières ; annonce d'intention |
| **Navigation A\*** | Coûts pondérés par le danger **mémorisé** — l'agent contourne ce qu'il croit dangereux, pas ce qui l'est |

Le point d'architecture central : **la logique de décision ne référence jamais le moteur de jeu.**
`Hordinary.Core` est un projet .NET pur, testable sans Godot ; la couche moteur n'est qu'un
afficheur et un fournisseur de perception.

**441 tests** (`xUnit`, ~20 s), exécutés sans moteur, sur la carte et les fichiers de `config/`
réellement livrés — pas des copies.

## Démarrage

```bash
dotnet build Hordinary.sln
dotnet test Tests/Hordinary.Tests.csproj   # 441 tests, sans Godot
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

Godot 4.7 · C# / .NET 8 · xUnit · aucun service externe

## Licence

Ce dépôt est sous **double licence** :

| Périmètre | Licence |
|---|---|
| Code source (`Hordinary.Core/`, `Scripts/`, `Tests/`, `config/`) | [MIT](LICENSE) |
| Ressources et contenu (`Assets/`, missions, textes, `Specs/F-*.md`) | [Tous droits réservés](Assets/LICENSE) |

© 2026 Raphaël JANSON
