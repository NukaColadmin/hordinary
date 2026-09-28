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

La comparaison se fait par nom de fichier, donc un instantané devenu orphelin — le but a changé de
nom, l'ancien fichier est resté — lui échapperait. Le dossier est pour cela **énuméré** : tout `.pddl`
que la capture ne produit plus fait échouer la suite, et il faut le supprimer à la main. Régénérer
écrit les dix noms attendus et n'efface rien d'autre.

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

**Les deux ont été lancés.** Les résultats sont dans [`results.md`](./results.md) — dont le premier
passage a trouvé quatre défauts d'export que rien d'autre n'aurait montrés, et où le support PDDL 3
de VAL, réserve de longue date de la spec, est confirmé.

Le jeu n'acquiert toujours aucune dépendance : aucun planificateur tiers ne tourne pendant une partie,
et la suite de tests passe sans eux. Mais elle **s'en sert quand ils sont là**, et une image Docker
suffit à les rendre disponibles :

```bash
docker pull aibasel/downward          # une fois. Les tests ne tirent jamais l'image eux-memes.

# Le domaine est-il bien formé, et un plan existe-t-il ?
docker run --rm -v "$PWD/tools/pddl:/benchmarks" -w /tmp aibasel/downward \
    /benchmarks/domain.pddl /benchmarks/snapshots/10-completeplayermission.pddl \
    --search "astar(lmcut())"

# VAL — le plan respecte-t-il la sémantique du domaine, contraintes PDDL 3 comprises ?
docker build -t hordinary-val tools/pddl/val     # pas d'image officielle : celle-ci compile VAL 4
docker run --rm -v "<dossier>:/data" hordinary-val -v \
    /data/domain.declared.pddl /data/problem.pddl /data/plan.txt
```

Le `-v` de VAL n'est pas décoratif : sans lui, un plan refusé pour violation de contrainte rend
« `Plan failed to execute` » et rien de plus.

Les mêmes, par [`compose.yaml`](../../compose.yaml) à la racine, qui porte les montages et le nom
d'image que les tests attendent :

```bash
docker compose --profile outils pull downward
docker compose --profile outils build val

docker compose --profile outils run --rm downward --plan-file /tmp/plan.txt \
    /benchmarks/domain.pddl /benchmarks/snapshots/10-completeplayermission.pddl \
    --search "astar(lmcut())"

docker compose --profile outils run --rm val -v \
    /data/missions/<id>/domain.declared.pddl /data/missions/<id>/problem.pddl \
    /data/missions/<id>/plan.txt
```

VAL y voit la racine du dépôt en lecture seule, parce que les trois fichiers qu'il réclame ne vivent
pas au même endroit : les artefacts versionnés sont ici, ceux d'une partie jouée dans
`missions/<id>/`. `HORDINARY_VAL_DATA` pointe ailleurs quand ils sont réunis dans un dossier à part.

**Sous Git Bash, préfixer par `MSYS_NO_PATHCONV=1`** — ou lancer depuis PowerShell. MSYS réécrit tout
argument qui ressemble à un chemin POSIX : `/benchmarks/domain.pddl` part au conteneur sous la forme
`C:/Program Files/Git/benchmarks/domain.pddl`, et Fast Downward s'arrête sur
« `Could not read file` », pour un montage pourtant correct. Le piège vaut pour toutes les lignes
ci-dessus, `docker run` comme `docker compose run`, et il ne se voit pas : le conteneur démarre, seul
le fichier manque.

`Tests/SatisfiabilityTests.cs` cherche le planificateur dans cet ordre : la variable
`HORDINARY_FAST_DOWNWARD`, le PATH, puis l'image Docker (`HORDINARY_FAST_DOWNWARD_IMAGE` pour en
désigner une autre). Les tests d'intégration ne sont pas marqués `Skip` : ils **changent de question**
selon ce qui est trouvé.

Les deux prouvent des choses différentes, et c'est pour ça qu'il en faut deux :

* **Fast Downward qui résout** prouve que le domaine est bien formé ;
* **VAL qui valide le plan de Hordinary** prouve que notre planificateur respecte la sémantique qu'il
  prétend implémenter.

Le tableau des dix est dans [`results.md`](./results.md). La colonne « plan GBFS » y manque encore :
la capture ne conserve pas le plan que GBFS avait produit au moment de l'instantané, donc on sait que
le domaine est bien formé, pas que les deux planificateurs sont d'accord.

### Livrable 15

Une mission sous instruction tactique écrit en plus, sous `missions/<id>/` (généré, non versionné) :

* `domain.constrained.pddl` — le domaine réécrit, **sans** bloc `(:constraints …)`. C'est celui qu'on
  donne à Fast Downward : il ne prend pas les contraintes de trajectoire PDDL 3 en entrée ;
* `domain.declared.pddl` — le même **avec** le bloc PDDL 3, pour VAL.

## Ce que le premier lancement a tranché

Trois points attendaient les binaires. Ils ont été posés à Fast Downward, et le détail est dans
[`results.md`](./results.md).

**1. `ally` est à la fois un type et un objet — accepté.** Le domaine déclare `(:types ally …)` et une
constante `ally`, le repère « l'allié le plus proche » que `ShareKnowledge` lit dans `(at agent ally)`.
Les deux espaces de noms sont bien distincts pour l'analyseur. Le renommage que
`SymbolicStateBuilder.LocationAliases` aurait permis n'est donc pas nécessaire, et n'a pas été fait :
la collision reste un piège de lecture pour un humain, pas pour l'outil.

**2. L'état initial place l'agent en plusieurs endroits à la fois — accepté, sans effet mesuré.**
`10-completeplayermission.pddl` porte `(at agent cover)` et `(at agent objective_zone)` ensemble. Le
domaine, lui, est correct : `MoveToLocation` efface toutes les positions avant d'en poser une
(`(forall (?l - location) (not (at agent ?l)))`), donc `at` redevient mono-valué dès la première
action. La multiplicité vient de ce que les repères se recouvrent — le survivant se tient sur une case
qui est en même temps la zone objectif et un abri.

La conséquence reste réelle : **le premier `MoveToLocation` détruit toutes ces positions et rien ne
peut en restaurer plus d'une.** GBFS ne s'en aperçoit pas, parce qu'il reconstruit l'état symbolique
depuis la perception à chaque replanification ; le PDDL exporté n'a que son `:init`. Aucun des dix
instantanés ne la fait apparaître, parce qu'aucun but n'exige d'être à deux endroits ensuite — donc le
point n'est pas clos, il est sans effet mesuré, ce qui n'est pas la même chose.

**3. Fast Downward peut déclarer morte une action qui ne l'est pas — non contredit.** Certains
prédicats sont posés par le moteur (`WorldPlanRuntime`, la perception, les catégories de lieux) et par
aucune action du fichier. Le domaine décrit ce que le planificateur peut faire, pas ce que le moteur
fait par ailleurs. Cette classe de faux positifs est documentée dans `Specs/G-export-pddl.md` et n'est
pas un défaut de l'export ; le passage n'en a caractérisé aucun cas, faute d'occasion.
