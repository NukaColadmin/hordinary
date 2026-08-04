# Hordinary

**Un jeu de survie tactique où le joueur ne pilote personne.** Il donne des objectifs ; trois
survivants autonomes perçoivent, se souviennent, planifient, se parlent — et changent d'avis.

<!-- TODO semaine 0 : remplacer par un GIF de 90 s montrant une replanification en direct -->
![Démo](docs/demo.gif)

---

## Ce que ce projet démontre

Une architecture d'IA d'agents complète, écrite from scratch, **sans moteur externe ni bibliothèque
d'IA** :

| Couche | Ce qu'elle résout |
|---|---|
| **Utility AI** | Choisir *quoi vouloir* : 13 objectifs pondérés par des courbes de réponse, avec garde-fous anti-oscillation |
| **Planification GBFS** | Choisir *comment y arriver* : recherche dans un espace d'états symbolique inspiré de PDDL, actions déclarées en JSON |
| **Replanification** | 10 déclencheurs surveillés 2×/s — un plan tombe quand le monde le contredit, et on ne repart pas de zéro |
| **Perception partielle** | Un agent ne peut pas utiliser ce qu'il n'a pas vu. Vision, ligne de vue, mémoire auditive |
| **Croyances individuelles** | Chaque survivant a *sa* carte mentale. Une position vieillit, une trajectoire se suppose, un souvenir s'efface |
| **Partage d'informations** | À portée de voix seulement. Ce que sait Alice, Bob l'ignore jusqu'à ce qu'elle le lui dise |
| **Navigation A\*** | Coûts pondérés par le danger *mémorisé*, pas par le danger réel |

Le point d'architecture central : **la logique d'IA ne référence jamais Godot**. `Hordinary.Core`
est un projet .NET pur, testé sans moteur — la couche Godot n'est qu'un afficheur.

**316 tests** (`xUnit`, ~0,5 s), qui lisent la vraie carte livrée et les vrais fichiers de `config/` — pas
des copies.

## Démarrage

```bash
dotnet build Hordinary.sln           # compiler
dotnet test Tests/Hordinary.Tests.csproj   # 316 tests, sans Godot
```

Puis ouvrir le dossier dans **Godot 4.7** et lancer `Scenes/Main.tscn`.

Choisir ses agents, choisir une mission, `DÉMARRER`. Les touches `F1`–`F7` ouvrent les overlays
d'observation : carte mentale, scores Utility AI, plan en cours, journal des décisions.

## Aucune valeur de gameplay n'est codée en dur

Les objectifs, les actions du planificateur, les profils de survivants et les missions sont des
fichiers JSON dans [`config/`](config/). Ajouter une action au planificateur ou un objectif à
l'Utility AI ne demande pas de recompiler.

## Aller plus loin

* **[Architecture détaillée](docs/architecture.md)** — le document complet : chaque système, ses
  choix de conception, et ses limites connues assumées.
* **[Spécifications](Specs/00-INDEX.md)** — les 8 livrables du MVP et les extensions, avec leur
  critère de validation.

## Stack

Godot 4.7 · C# / .NET 8 · xUnit · aucun service externe

## Licence

* **Code source** — [MIT](LICENSE)
* **Ressources graphiques et contenu** (`Assets/`) — [tous droits réservés](Assets/LICENSE)
