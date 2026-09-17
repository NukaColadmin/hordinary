# Export PDDL

Sortie **générée** de [`Specs/G-export-pddl.md`](../../Specs/G-export-pddl.md). Rien n'est écrit à la
main ici : `domain.pddl` et les instantanés de `snapshots/` sont rendus par
`Hordinary.Core/AI/Planning/Pddl/`, depuis `config/planner_domain.json` et depuis des parties
réellement jouées.

Ces fichiers sont versionnés bien qu'ils soient générés, et c'est délibéré : ce sont eux qu'on donne à
Fast Downward et à VAL, hors ligne. Un dépôt qui ne les contient pas oblige à lancer le jeu avant de
pouvoir vérifier quoi que ce soit.

Le risque habituel d'un fichier généré versionné — qu'il dérive de sa source sans qu'on le voie — est
couvert par `Tests/PddlArtifactsTests.cs`, qui rend à nouveau et compare. **Changer une action du
domaine sans régénérer fait échouer la suite de tests.**

## Ce qu'il y a ici

| | |
|---|---|
| `domain.pddl` | le domaine, stable d'une partie à l'autre |
| `snapshots/NN-<objectif>.pddl` | dix problèmes prélevés dans une partie à trois, du départ au verdict |

Un instantané est **ce qu'un survivant croyait à un instant donné**, pas l'état du monde. Ce qu'il n'a
jamais perçu n'y figure pas : c'est la thèse du projet, et elle survit à l'export
(`Un_objet_jamais_percu_n_apparait_pas_dans_le_probleme`).

Un instantané tient en 45 à 76 lignes : il se lit d'un coup d'œil. Comparer `01` et `10` donne la
mesure de ce qu'une mission apprend — 12 objets et 1 action instanciée au départ, 25 objets et 105
actions à l'arrivée.

Le coût exporté est la **part fixe** du coût d'une action. Le `distance_cost` de
`config/planner_domain.json` — le coût par cellule parcourue — n'est pas exprimable ici :
`(increase (total-cost) (distance ?from ?to))` demanderait un paramètre d'origine que
`MoveToLocation(?location)` n'a pas. Comparer le coût d'un plan de Fast Downward à celui de GBFS
demande de le savoir.

## Régénérer

```bash
HORDINARY_PDDL_REGEN=1 dotnet test Tests/Hordinary.Tests.csproj --filter "FullyQualifiedName~PddlArtifactsTests"
```

```powershell
$env:HORDINARY_PDDL_REGEN = "1"
dotnet test Tests\Hordinary.Tests.csproj --filter "FullyQualifiedName~PddlArtifactsTests"
```

La capture passe une graine de bruit explicite (`noiseSeed`). Sans elle, l'erreur de localisation d'un
bruit tire dans `Random.Shared` et deux régénérations de la même partie diffèrent — le test de
non-dérive signalerait alors du bruit plutôt qu'un changement.

## Relancer Fast Downward et VAL

**Aucun des deux n'est installé ni requis.** Le jeu n'acquiert aucune dépendance : aucun planificateur
tiers ne tourne pendant une partie, et la suite de tests passe sans eux. Ce qui suit est la procédure
hors ligne, le jour où on la fait.

```bash
# Fast Downward — le domaine est-il bien formé, et un plan existe-t-il ?
./fast-downward.py tools/pddl/domain.pddl tools/pddl/snapshots/10-completeplayermission.pddl \
    --search "astar(lmcut())"

# VAL — le plan que GBFS avait produit respecte-t-il la sémantique du domaine ?
validate tools/pddl/domain.pddl tools/pddl/snapshots/10-completeplayermission.pddl plan.txt
```

Les deux prouvent des choses différentes, et c'est pour ça qu'il en faut deux :

* **Fast Downward qui résout** prouve que le domaine est bien formé ;
* **VAL qui valide le plan de Hordinary** prouve que notre planificateur respecte la sémantique qu'il
  prétend implémenter.

Le tableau des dix — plan FD, plan GBFS, écart — vivra dans `results.md`, qui n'existe pas encore
faute d'avoir lancé les binaires.

### Livrable 15

Une mission sous instruction tactique écrit en plus, sous `missions/<id>/` (généré, non versionné) :

* `domain.constrained.pddl` — le domaine réécrit, **sans** bloc `(:constraints …)`. C'est celui qu'on
  donne à Fast Downward : il ne prend pas les contraintes de trajectoire PDDL 3 en entrée ;
* `domain.declared.pddl` — le même **avec** le bloc PDDL 3, pour VAL.

## À regarder au premier lancement

Trois points connus, qu'aucun test local ne peut trancher sans les binaires.

**1. `ally` est à la fois un type et un objet.** Le domaine déclare `(:types ally … )` et les
instantanés déclarent un lieu nommé `ally` — le repère « l'allié le plus proche », que
`ShareKnowledge` lit dans `(at agent ally)`. Les deux vivent dans des espaces de noms distincts au
sens de la grammaire PDDL, et la plupart des analyseurs l'acceptent ; certains s'en plaignent. Le jeu
a déjà le mécanisme pour le corriger si nécessaire — `SymbolicStateBuilder.LocationAliases` renomme
déjà `objective` en `objective_spot` pour exactement cette raison — mais le faire change le
vocabulaire du domaine livré, donc on attend de savoir si c'est nécessaire.

**2. L'état initial place l'agent en plusieurs endroits à la fois.** `10-completeplayermission.pddl`
porte `(at agent cover)` et `(at agent objective_zone)` ensemble. Le domaine, lui, est correct :
`MoveToLocation` efface toutes les positions avant d'en poser une
(`(forall (?l - location) (not (at agent ?l)))`), donc `at` redevient mono-valué dès la première
action. La multiplicité vient de ce que les repères se recouvrent — le survivant se tient sur une case
qui est en même temps la zone objectif et un abri.

Conséquence concrète, et c'est elle qui vaut la note : **le premier `MoveToLocation` détruit toutes ces
positions et rien ne peut en restaurer plus d'une.** GBFS ne s'en aperçoit pas, parce qu'il reconstruit
l'état symbolique depuis la perception à chaque replanification ; le PDDL exporté n'a que son `:init`.
C'est une divergence réelle entre les deux rendus, pas un détail de syntaxe — et exactement ce que le
portillon 3 existe pour attraper. La corriger demanderait de distinguer *être à un endroit* de *être
auprès d'un repère*, soit un second prédicat : on attend de savoir si Fast Downward s'en plaint.

**3. Fast Downward peut déclarer morte une action qui ne l'est pas.** Certains prédicats sont posés
par le moteur (`WorldPlanRuntime`, la perception, les catégories de lieux) et par aucune action du
fichier. Le domaine décrit ce que le planificateur peut faire, pas ce que le moteur fait par ailleurs.
Cette classe de faux positifs est documentée dans `Specs/G-export-pddl.md` et n'est pas un défaut de
l'export.
