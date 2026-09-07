# Livrable 1 — Carte et déplacements

> Voir [00-INDEX.md](./00-INDEX.md) pour le contexte général du projet.

## Objectif

Poser la grille de jeu, une carte manuelle jouable, et permettre à un survivant de se déplacer d'un point à un autre en évitant les obstacles via A*.

## Carte du MVP

### Dimensions

Entre `20 x 20` et `40 x 40` cellules. Taille recommandée : `30 x 30`.

### Types de cellules

```text
UNKNOWN
FLOOR
WALL
DOOR_OPEN
DOOR_CLOSED
DOOR_LOCKED
WINDOW
OBSTACLE
PHARMACY
EXTRACTION
```

### Lieux principaux

La carte doit contenir au minimum :

* une zone de départ ;
* une pharmacie ;
* une zone d'extraction ;
* deux bâtiments secondaires ;
* une rue principale ;
* une ruelle ;
* au moins deux chemins possibles entre le départ et la pharmacie.

La carte peut être créée manuellement pour le MVP.

## Navigation A* (base)

### Coût des cellules

```text
Cellule libre connue : 1
Cellule inconnue : 4
Cellule avec danger suspecté : 8
Cellule proche d'un zombie : 15
Cellule bloquée : infini
```

> Note : à ce stade (avant le livrable 2 — Perception), A* peut naviguer sur la carte réelle. Le passage à la carte mentale de l'agent (`AgentKnowledge`) sera fait au livrable 2.

### Pathfinding interrompu

Un chemin doit être recalculé lorsque :

* un obstacle est découvert ;
* une porte est verrouillée ;
* un zombie bloque le passage ;
* une zone devient trop dangereuse ;
* l'objectif change.

(La détection de ces événements et le déclenchement effectif de la replanification seront complétés au livrable 6.)

## Étapes à créer

* [x] Créer la grille (`GridMap`).
* [x] Créer une carte manuelle respectant les lieux principaux ci-dessus.
* [x] Ajouter murs et obstacles.
* [x] Implémenter A* (`AI/Navigation/AStar`, `NavigationGrid`).
* [x] Déplacer un survivant le long du chemin trouvé (`PathFollower`).
* [x] Afficher son chemin (à minima en mode debug).

## Critère de validation

Un survivant peut atteindre une destination connue en évitant les obstacles.

## Tests associés

Voir [C-tests.md](./C-tests.md) section « Navigation » :

* A* trouve un chemin valide ;
* A* évite les cellules bloquées ;
* les cellules dangereuses augmentent le coût ;
* une cellule inconnue n'est pas traitée comme une cellule libre certaine.
