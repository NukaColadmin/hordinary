# G — Export PDDL et validation par un planificateur tiers

> Voir [00-INDEX.md](./00-INDEX.md) pour le contexte général du projet.

**Nature : document transverse, pas un livrable de jeu.** Rien de ce qui suit ne change une règle, un
comportement ou une image. Le jeu se joue exactement pareil avant et après. C'est un outil de
vérification — et la preuve que la représentation du livrable 5 n'est pas une approximation privée
mais un vrai domaine de planification.

**Dépendances** : livrable 5 (le domaine), livrable 11 (qui est le dernier à faire bouger
`planner_domain.json`).

**Ce document est un prérequis dur du [livrable 15](./15-instructions-tactiques.md)**, qui compile ses
contraintes dans le domaine PDDL et non dans le JSON. Cela ne change pas son périmètre, mais cela impose
la forme de sa sortie : voir [Ce que l'export doit exposer](#ce-que-lexport-doit-exposer). Il doit être
terminé avant que le livrable 15 commence.

**Budget : un week-end.** Cette limite fait partie de la spécification, voir [Hors périmètre](#hors-périmètre).

---

## Objectif

Traduire [`config/planner_domain.json`](../config/planner_domain.json) en **PDDL valide**, et faire
résoudre le résultat par un planificateur de l'état de l'art extérieur au projet.

La spécification du MVP autorisait « une représentation inspirée de PDDL, sans moteur PDDL complet ».
Ce document referme cet écart par le seul bout qui coûte peu : on ne remplace pas GBFS, on **prouve
que le domaine aurait pu être donné à n'importe quel planificateur**.

Ce qu'on gagne, dans l'ordre d'importance :

1. **Une affirmation vérifiable.** « Domaine PDDL valide, résolu par Fast Downward » se contrôle en
   trente secondes par un lecteur. « Inspiré de PDDL » ne se contrôle pas.
2. **Une mesure de la sous-optimalité de GBFS.** Le livrable 5 assume `priorité = h`, donc des plans
   non optimaux. Personne ne sait de combien. Un planificateur optimal sur le même problème donne le
   chiffre.
3. **Un test de cohérence du domaine.** Un planificateur tiers ne pardonne pas ce que le nôtre
   tolère : préconditions inatteignables, effets qui s'annulent, actions mortes.

## Principe directeur : l'export est un instantané, pas un format d'échange

`SymbolicStateBuilder` construit l'état **d'un agent, à un instant**, fermé sur ce qu'il sait. Une
porte jamais vue n'existe pas pour lui.

L'export hérite de cette propriété, et c'est ce qui le rend simple : on ne cherche pas à décrire le
monde, on décrit **ce qu'Alice croit du monde à la seconde 47**. Un fichier `problem.pddl` par agent
et par instant capturé. Trois survivants qui divergent produisent trois problèmes différents à partir
du même domaine — ce qui est, accessoirement, la meilleure illustration possible de l'observabilité
partielle.

Le **domaine** (`domain.pddl`), lui, est unique et statique : il ne dépend d'aucune partie.

## Le domaine

### Types

Les six types de `parameters` se transposent directement :

```lisp
(:types location door window item ally threat - object)
```

### Constantes

Certaines préconditions nomment des objets en dur (`at(agent, objective_zone)`,
`has(agent, objective)`). Elles deviennent des constantes du domaine :

```lisp
(:constants agent - object  objective remedy - item)
```

`objective_zone` reste un objet du **problème** : c'est un lieu connu, pas une entité du langage — et
c'est la mission qui décide lequel, ce que le domaine n'a pas à savoir.

### Prédicats

Extraits mécaniquement des `preconditions` et `effects` de **toutes les actions du fichier**. L'arité et
les types se déduisent des `parameters`.

Le nombre d'actions n'est jamais écrit en dur, ni ici ni dans un test : le fichier en comptait 23, le
livrable 11 en a ajouté sept (`PickUpItem`, `UseItem`, `EquipWeapon`, `AttackWithWeapon`,
`ThrowFirecracker`, `PushHeavyObject`, `BarricadeDoor` — cette dernière n'a jamais été écrite), et le
livrable 15 en a retiré deux (`CreateNoiseDistraction`, `ThrowFirecracker`). Il en déclare vingt-quatre
au moment où ces lignes sont écrites, ce qui est exactement la raison de la règle : la seule formulation
qui ne périme pas est « autant d'actions que le fichier en déclare, moins celles à
`"enabled": false` ».

### Les six verbes ne se traduisent pas

`toPos`, `take`, `put`, `setFlag`, `wait`, `damage` sont la couche **exécution** : ce que le moteur
fait quand l'action est jouée. PDDL ne décrit que la couche **symbolique** — préconditions et effets.
Les champs `verb`, `params`, `duration` et `noise_action` ne sont donc simplement pas exportés.

C'est un point à écrire noir sur blanc dans le README : cette séparation entre schéma d'action et
implémentation du verbe est exactement ce que fait un exécuteur PDDL en robotique. Elle n'a pas été
faite pour ça, mais elle tient.

## Ce que l'export doit exposer

Trois exigences de forme, demandées par le [livrable 15](./15-instructions-tactiques.md), qui doit
**transformer** ce domaine à chaque instruction et pas seulement le lire. Aucune n'élargit le périmètre
ni le budget : elles disent où le code vit et sous quelle forme il garde ce qu'il a déjà construit.

1. **L'exportateur vit dans `Hordinary.Core`**, sous `AI/Planning/Pddl/`, et non dans un script de
   `tools/`. Le dossier `tools/pddl/` reste ce qu'il est : la **sortie**, versionnée.
2. **L'export passe par une représentation intermédiaire explicite** — `PddlDomain`, `PddlAction`,
   `PddlFormula` — plutôt que par une concaténation de chaînes. `PddlWriter` rend le texte à partir de
   cette structure. C'est le seul vrai ajout, de l'ordre de la demi-journée : l'exportateur construit
   déjà cette information, il la garde au lieu de l'aplatir tout de suite.
3. **L'export est appelable sur un domaine en mémoire**, pas seulement sur `config/planner_domain.json`
   lu depuis le disque.

**Ce n'est pas un import PDDL**, et l'exclusion de la section [Hors périmètre](#hors-périmètre) tient :
rien ne lit du texte PDDL. Le chemin est
`planner_domain.json → PddlDomain → { texte pour Fast Downward, PlanningDomain pour GBFS }`, et la
seconde flèche est un rendu en mémoire, pas une analyse syntaxique.

Le rendu `PddlDomain → PlanningDomain` n'est **pas** demandé par ce document — il appartient au livrable
15, qui en a besoin pour que le jeu change de comportement. Ce qui est demandé ici, c'est que la
structure existe et soit atteignable depuis `Hordinary.Core`. Sans elle, le livrable 15 devrait
maintenir deux compilateurs d'accord, et le portillon 3 finirait par vérifier autre chose que ce que le
jeu exécute.

---

## Le seul vrai obstacle : `!at(agent, *)`

`MoveToLocation` a cet effet :

```json
"effects": [ "!at(agent, *)", "at(agent, ?location)" ]
```

Le joker `*` dit « retire toute position mémorisée ». **PDDL n'a pas de suppression par joker.** C'est
le seul endroit où la traduction demande de réfléchir plutôt que de transcrire.

Trois réponses possibles :

| Réponse | Verdict |
|---|---|
| **Effet universellement quantifié** (`forall`) | **Retenue.** Fidèle, supportée par Fast Downward via ADL |
| Fluent d'objet PDDL 3.1 (`(at ?a) - location`) | Rejetée : sémantiquement la plus juste — `at` *est* une fonction — mais le support côté planificateurs est mauvais, Fast Downward compris |
| Précondition `at(agent, ?from)` et suppression explicite | Rejetée : demande d'ajouter un paramètre `?from` à `MoveToLocation`, donc de **modifier le domaine du jeu** pour satisfaire l'export. L'export s'adapte au jeu, jamais l'inverse |

La forme retenue :

```lisp
(:action MoveToLocation
  :parameters (?loc - location)
  :precondition (and (known ?loc) (not (at agent ?loc)))
  :effect (and (forall (?l - location) (not (at agent ?l)))
               (at agent ?loc)
               (increase (total-cost) 1)))
```

L'ordre du JSON — suppressions avant ajouts — est préservé par construction : en PDDL les effets
d'une action sont simultanés et le `forall` ne peut pas défaire l'ajout qui le suit, puisqu'il
s'applique à l'état de départ.

### Requirements

```lisp
(:requirements :strips :typing :negative-preconditions
               :conditional-effects :action-costs)
```

Les `!` du JSON imposent `:negative-preconditions` ; le `forall` en effet relève de
`:conditional-effects` (effets quantifiés) ; `:action-costs` porte les coûts. Les quatre premiers
sont couverts par le raccourci `:adl`.

---

## Le problème

Généré depuis un `SymbolicState` réel.

* **`:objects`** — les objets connus de l'agent, tels que `SymbolicStateBuilder` les dérive de sa
  mémoire. Une porte jamais vue n'y figure pas.
* **`:init`** — les faits de l'état, plus les coûts de distance (voir ci-dessous).
* **`:goal`** — les `conditions` du but courant, prises dans la section `goals` du JSON.
* **`:metric`** — `(:metric minimize (total-cost))`.

### Les coûts de distance

`distance_cost` ajoute un coût par cellule parcourue. Ce n'est pas exprimable dans le domaine : la
distance dépend de la carte et de la position. Elle se **fige dans le problème**, sous forme de
fonction numérique initialisée par A\* :

```lisp
(:functions (total-cost) (distance ?from ?to - location))
```

Chaque paire de lieux connus reçoit sa vraie distance A\*, calculée à l'instant de la capture. C'est
la traduction honnête de ce que fait le jeu — et c'est aussi ce que fait un système de planification
robotique : le planificateur de tâches raisonne sur des coûts que le planificateur de mouvement lui
fournit.

### Les prédicats non exportables

Cinq « objets » du domaine ne sont pas des objets du monde mais des **résolutions à l'exécution** :

| Symbole | Ce que c'est réellement |
|---|---|
| `away_from_danger` | Une case calculée au moment d'exécuter, depuis la carte de chaleur |
| `group` | La position supposée du barycentre des alliés, qui bouge |
| `mission` | Le lieu désigné par la consigne du joueur |
| `explored` / `hidden` | Des drapeaux posés par le moteur, sans structure symbolique |

Ils s'exportent comme des **objets ordinaires figés à leur valeur au moment de la capture**. C'est
correct pour un instantané et faux pour un usage général — un plan PDDL qui vise `away_from_danger`
suppose que le danger n'a pas bougé. Il faut l'écrire dans la limitation, pas le cacher.

---

## Critère de validation

> Sur dix instantanés pris dans des parties réelles, le domaine exporté est accepté par Fast Downward,
> qui produit un plan ; et **VAL valide contre ce même domaine le plan que GBFS avait produit** pour
> le même état.

Les deux moitiés comptent, et la seconde plus que la première :

* Fast Downward qui résout prouve que le domaine est **bien formé**.
* VAL qui valide le plan de Hordinary prouve que **notre planificateur respecte la sémantique qu'il
  prétend implémenter**. C'est la preuve forte, et elle va dans l'autre sens.

Un écart de longueur entre les deux plans n'est pas un échec : c'est le chiffre qu'on cherchait à
mesurer, celui de la sous-optimalité assumée de GBFS.

## Tests

| Test | Ce qu'il vérifie |
|---|---|
| `Le_domaine_exporte_declare_toutes_les_actions_activees` | Autant d'actions que `planner_domain.json` en déclare, moins celles à `"enabled": false` — le compte se lit dans le fichier, il n'est pas écrit dans le test |
| `Chaque_predicat_utilise_est_declare` | Aucun prédicat n'apparaît en précondition ou en effet sans figurer dans `:predicates` |
| `Le_deplacement_exporte_un_effet_universel` | Le joker `*` produit bien un `forall`, pour toute action qui en contient un |
| `Le_texte_rendu_correspond_a_la_representation` | `PddlWriter` ne perd ni n'invente rien par rapport au `PddlDomain` qu'on lui donne — le test qui rend la représentation intermédiaire utile plutôt que décorative |
| `Un_objet_jamais_percu_n_apparait_pas_dans_le_probleme` | L'observabilité partielle survit à l'export — c'est le test qui compte |
| `Deux_agents_produisent_deux_problemes_differents` | Trois croyances divergentes, trois `:init` distincts, un seul domaine |
| `Le_probleme_exporte_est_resolu_par_le_planificateur_tiers` | Test d'intégration, ignoré si l'exécutable est absent |

Les six premiers tournent sans rien installer : ils lisent la structure et la chaîne produites. Seul le
septième demande Fast Downward, et il se marque `Skip` s'il ne le trouve pas — la suite de tests ne doit
pas devenir dépendante d'un binaire externe.

## Hors périmètre

Ces exclusions ne sont pas des « pas encore », ce sont les bords du week-end. Elles existent parce que
ce document est un détour de crédibilité, pas un changement de cap.

* **Pas d'import.** On n'écrit pas de parseur PDDL. L'export est à sens unique — y compris avec la
  représentation intermédiaire, qui se construit depuis le JSON et jamais depuis du texte PDDL.
* **Pas de remplacement de GBFS.** Aucun planificateur externe ne tourne pendant une partie. Le jeu
  n'acquiert aucune dépendance.
* **Pas de ROS, pas de Gazebo, pas de nœud PlanSys2.** C'est la pente savonneuse à refuser
  explicitement : chacun de ces trois pas est raisonnable isolément et coûte un mois.
* **Pas de PDDL temporel ni de durées.** `duration` existe dans le JSON ; l'exporter demanderait
  PDDL2.1 durative-actions et n'apporte rien ici.
* **Pas de modélisation du bruit.** `noise_level` n'oriente la recherche qu'à travers l'heuristique,
  jamais comme contrainte. Un second `:metric` serait une réécriture du domaine.

## Livrable

Du code dans `Hordinary.Core/AI/Planning/Pddl/` (`PddlDomain`, `PddlAction`, `PddlFormula`,
`PddlWriter`, et l'exportateur de problème), et un dossier `tools/pddl/` qui n'est plus que la sortie :

```text
tools/pddl/
├── domain.pddl          genere, versionne (il est stable)
├── snapshots/           dix problemes captures, versionnes
├── README.md            comment regenerer, comment relancer Fast Downward et VAL
└── results.md           le tableau des dix : plan FD, plan GBFS, ecart
```

Et **un paragraphe** dans les deux README du projet, pas plus. Le fichier `results.md` porte le
détail ; le README porte la phrase qui se vérifie.

## Limitations connues

* Un instantané fige `away_from_danger`, `group` et `mission`. Le plan produit est correct pour l'état
  capturé et n'a pas de sens hors de lui.
* Les coûts de distance sont ceux d'A\* à l'instant de la capture, sur la carte de danger de l'agent :
  deux agents au même endroit peuvent produire deux `distance` différentes pour la même paire de
  lieux. C'est fidèle au jeu, et déroutant pour qui lit le problème sans le savoir.
* Le domaine exporté décrit ce que le planificateur peut faire, pas ce que l'Utility AI choisira de
  vouloir. La sélection d'objectif ne se modélise pas en PDDL et n'a pas à l'être.
* **Le domaine n'est pas le moteur, et Fast Downward ne voit que le domaine.** Certains prédicats sont
  posés par le runtime (`WorldPlanRuntime`, la perception, le moteur de bruit) et par aucune action du
  fichier ; d'autres actions portent en C# des conditions qui n'apparaissent nulle part dans les
  `preconditions`. Un planificateur tiers peut donc déclarer inatteignable un prédicat que le jeu établit
  très bien, et inversement produire un plan dont une action échouera systématiquement à l'exécution.
  C'est une classe de **faux positifs et de faux négatifs connue**, elle ne se corrige pas depuis
  l'export, et elle vaut aussi pour le portillon 3 du
  [livrable 15](./15-instructions-tactiques.md) — qui s'appuie sur ce document pour ne pas la
  redocumenter.
