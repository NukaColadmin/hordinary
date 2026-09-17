# Livrable 5 — Planificateur GBFS

> Voir [00-INDEX.md](./00-INDEX.md) pour le contexte général du projet.

## Objectif

Transformer un objectif Utility AI en une suite d'actions exécutables, via une recherche Greedy Best-First Search sur un état symbolique.

## Rôle

Le planificateur transforme un objectif en suite d'actions.

```text
Objectif :
Posséder les médicaments

Plan :
1. Se rendre à la pharmacie
2. Ouvrir une entrée
3. Fouiller la pharmacie
4. Localiser les médicaments
5. Prendre les médicaments
```

## Représentation d'un état

```text
at(agent, location)
known(location)
accessible(location)
door_open(door)
door_locked(door)
has(agent, item)
objective_found
objective_collected
zombie_near(location)
extraction_known
```

## Actions symboliques

```text
MoveToLocation
OpenDoor
ForceDoor
BreakWindow
SearchRoom
PickUpObjective
EscapeLocation
ReachExtraction
```

Chaque action (symbolique comme exécutable) doit contenir :

```python
name: str
preconditions: list[Condition]
effects: list[Effect]
cost: float
noise_level: float
duration: float
```

Exemple :

```python
Action(
    name="BreakWindow",
    preconditions=[
        IsNearWindow(),
        WindowIsIntact(),
    ],
    effects=[
        WindowIsBroken(),
        PassageIsAccessible(),
        NoiseCreated(level=8),
    ],
    cost=4,
    noise_level=8,
    duration=2,
)
```

L'ensemble des actions exécutables (niveau jeu, pas seulement symbolique) à couvrir à terme :

```text
MoveTo
Explore
Observe
OpenDoor
ForceDoor
BreakWindow
SearchRoom
PickUpObjective
DropItem
Flee
Hide
Wait
ReachExtraction
ShareKnowledge
```

Toutes sont **écrites** dans [`config/planner_domain.json`](../config/planner_domain.json), et toutes
sont exécutables. Quatre ne sont pour autant jamais **jouées** : `Observe`, `Wait` — auxquelles le
livrable 11 a ajouté `UseItem` et `PushHeavyObject` — posent des faits (`observed`, `waited`, `used`,
`pushed`) qu'aucune précondition et aucun but ne lisent, si bien que `ActionRelevance` les écarte
pour tous les objectifs. `DropItem` passe le tri mais rien ne demande jamais de poser un objet, et
`ShareKnowledge` a un but symbolique qu'aucun objectif d'utilité ne réclame.

Ce n'est pas un manquement de ce livrable-ci : couvrir une action, c'est l'écrire et savoir
l'exécuter, ce qui est fait. Leur donner un demandeur revient au
[livrable 15](./15-instructions-tactiques.md), où c'est le joueur qui dit « observe », « attends » ou
« pousse ça » — aucune de ces trois n'est un réflexe.

`HelpAlly`, `HealAlly` et `CalmAlly` figuraient dans cette liste jusqu'au
[livrable 15](./15-instructions-tactiques.md), qui les a retirées du domaine : un survivant ne décide plus
seul d'aller au secours d'un autre. `ShareKnowledge` reste — dire ce qu'on a vu n'est pas porter secours,
et c'est ce qui alimente les croyances de tout le monde. Les prédicats `ally_in_danger`, `ally_wounded` et
`ally_panicking` disparaissent avec elles : aucun schéma ne les lisait plus, et un prédicat qu'aucune action
ne déclare n'a pas de traduction en PDDL (voir [G-export-pddl.md](./G-export-pddl.md)).

`CreateNoiseDistraction` est partie de la même façon, avec `ThrowFirecracker` du
[livrable 11](./11-objets-armes-et-economie-du-camp.md), le but `CreateDistraction` et le prédicat
`distraction_created`. Attirer la horde ailleurs pour dégager quelqu'un est un sacrifice calculé, pas un
réflexe : c'est au joueur de le demander. Le verbe `emitNoise` et `WorldPlanRuntime.ThrowNoise` restent
exécutables, sans aucune action qui les emploie — comme `BarricadeDoor`, ils attendent un demandeur.

## Data-driven : verbes primitifs, pas une classe par action

Cette liste ne doit **pas** devenir une classe C# par action (`MoveToAction`, `OpenDoorAction`, …).
Le planificateur (préconditions, effets, coût, bruit, durée) doit être **data-driven** : chaque action
du domaine PDDL/GBFS est une donnée décrite en JSON (voir [B-configuration.md](./B-configuration.md)),
construite à partir d'un petit socle de **verbes d'action primitifs et paramétrables** exécutés par
le moteur de planification — par exemple `toPos(location)`, `take(item)`, `put(item, location)`,
`setFlag(condition, value)`, `wait(duration)`.

`OpenDoor`, `PickUpObjective`, `ReachExtraction`, etc. deviennent alors des **instances** de ces verbes
avec des paramètres différents, pas de nouveaux types de code. Ajouter une action au jeu (ou en
retirer une pour un test) doit être possible en éditant la configuration, sans recompiler.

## Chantier reporté : le combat (livrable 8)

> Note ajoutée pendant le livrable 4. Le combat n'est **pas** à implémenter ici — il appartient au
> livrable 8 ([08-interface-et-boucle-complete.md](./08-interface-et-boucle-complete.md), « Attaques »,
> priorité 9 sur 10 de [00-INDEX.md](./00-INDEX.md)). Elle est consignée dans ce document parce que
> c'est ce livrable-ci qui la débloque.

Constat à l'issue du livrable 4 : un survivant ne se bat jamais, même contre un zombie isolé et même
sans stress. Trois manques distincts, dans cet ordre de difficulté :

1. **Aucun objectif de combat.** Les douze objectifs sont ceux fixés par
   [04-utility-ai.md](./04-utility-ai.md). Ajouter `FightZombie` ou `PushBackZombie` est une simple
   entrée de `config/utility_goals.json` — aucune recompilation, c'est ce que garantit le mécanisme
   data-driven déjà livré.
2. **Aucune considération ne sait dire « peu d'adversaires ».** `perceived_danger` accumule toutes
   les menaces en un seul scalaire : un zombie à deux cases et trois zombies à six cases donnent la
   même valeur. Un objectif de combat crédible demande deux entrées primitives supplémentaires dans
   `UtilityInputs` — un décompte des menaces proches et la distance à la plus proche, non normalisée.
   Le volet « en fonction du stress » est déjà couvert : `panic` et `pursued` existent.
3. **Se battre n'est pas exécutable — c'est le vrai blocage, et c'est ici qu'il se lève.** Un
   objectif se résout aujourd'hui en une case de destination suivie d'un A*. Il n'y a pas de couche
   d'action. `Zombie` n'a ni points de vie ni moyen d'en perdre ; `zombies.attack_damage` ne va que
   du zombie vers le survivant.

Conséquence pour la conception du socle de verbes primitifs décrit ci-dessus : il doit pouvoir
porter une action qui **affecte une autre entité** (`damage(target, amount)`, ou un `setFlag` sur une
cible), pas seulement le monde ou l'inventaire de l'agent. `Flee`, `Hide` et `ShareKnowledge` figurent
déjà dans la liste des actions exécutables à couvrir ; une action offensive suit exactement le même
gabarit `préconditions / effets / coût / bruit / durée`. Prévoir cette forme dès maintenant évite
d'avoir à rouvrir le domaine au livrable 8.

## GBFS

Priorité d'un nœud = uniquement son heuristique :

```python
priority = heuristic(state, goal)
```

Heuristique simple :

```python
heuristic = number_of_unsatisfied_goal_conditions
```

Version enrichie :

```python
heuristic = (
    unsatisfied_conditions
    + estimated_distance
    + danger_penalty
    + noise_penalty
)
```

## Limites de recherche

```text
Maximum de nœuds explorés : 1 000
Profondeur maximale : 20 actions
Temps maximal par planification : 50 ms
```

Ces valeurs doivent être configurables (voir [B-configuration.md](./B-configuration.md)).

## Échec de planification

Si aucun plan n'est trouvé :

1. l'agent choisit un objectif d'exploration ;
2. il cherche une zone inconnue pertinente ;
3. il collecte de nouvelles informations ;
4. il relance la planification.

## Architecture et modèle de données concernés

`AI/Planning/SymbolicState`, `PlanningAction`, `Planner`, `GBFS`, `Heuristic`. Classes `Plan` et `PlanningAction` — voir [A-architecture-et-donnees.md](./A-architecture-et-donnees.md).

## Étapes à créer

* [x] Créer l'état symbolique.
* [x] Créer les actions (préconditions/effets/coût/bruit/durée).
* [x] Créer les préconditions.
* [x] Créer les effets.
* [x] Implémenter GBFS avec les limites de recherche configurables.
* [x] Produire un plan.
* [x] Exécuter les actions du plan.
* [x] Gérer l'échec de planification (repli sur exploration).

## Critère de validation

L'objectif « récupérer les médicaments » produit une suite d'actions valide.

## Tests associés

Voir [C-tests.md](./C-tests.md) section « Planification » :

* un plan valide est généré lorsque les préconditions sont réunies ;
* aucun plan impossible n'est retourné ;
* une porte verrouillée exige une action adaptée ;
* le plan final satisfait l'objectif ;
* la limite de profondeur est respectée.
