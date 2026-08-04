# C — Tests unitaires minimums

> Document transverse référencé par tous les livrables ([00-INDEX.md](./00-INDEX.md)).

## Utility AI

Voir [04-utility-ai.md](./04-utility-ai.md). Tester que :

* la fuite obtient un score élevé lorsque le danger est fort ;
* la mission reste prioritaire lorsque le danger est faible ;
* un survivant loyal privilégie davantage l'aide à un allié ;
* l'hystérésis évite les changements constants d'objectif.

## Planification

Voir [05-planification-gbfs.md](./05-planification-gbfs.md). Tester que :

* un plan valide est généré lorsque les préconditions sont réunies ;
* aucun plan impossible n'est retourné ;
* une porte verrouillée exige une action adaptée ;
* le plan final satisfait l'objectif ;
* la limite de profondeur est respectée.

## Navigation

Voir [01-carte-et-deplacements.md](./01-carte-et-deplacements.md). Tester que :

* A* trouve un chemin valide ;
* A* évite les cellules bloquées ;
* les cellules dangereuses augmentent le coût ;
* une cellule inconnue n'est pas traitée comme une cellule libre certaine.

## Connaissances

Voir [02-perception-et-memoire.md](./02-perception-et-memoire.md) et [07-survivants-et-partage.md](./07-survivants-et-partage.md). Tester que :

* un agent ne connaît pas une cellule non observée ;
* une observation directe met à jour une croyance ;
* la confiance d'une position de zombie diminue ;
* une information récente remplace une information ancienne ;
* le partage de connaissances n'accède pas au `WorldState`.

## Extensions post-MVP

Les livrables 11 à 15 portent chacun leur propre liste de tests, en fin de fichier. Ce document ne les
recopie pas ; il rappelle les **trois invariants** que ces listes vérifient toutes, sous une forme ou
une autre, et qui sont la raison d'être de la suite :

* **aucun système ne lit le `WorldState` à la place d'un agent** — résolveur d'expédition, résolveur de
  rencontre, contexte de dialogue ;
* **`Hordinary.Core` ne référence ni Godot, ni un client HTTP** — c'est ce qui rend tout le reste
  testable sans lancer le jeu ;
* **à graine égale, résultat égal** — roster généré, quartier généré, expédition, rencontre, mission
  rejouée avec ses contraintes figées.

Deux catégories de tests dérogent à la règle « aucune dépendance externe » et se marquent donc `Skip`
quand le binaire manque : ceux qui appellent **Fast Downward** ou **VAL**
([G-export-pddl.md](./G-export-pddl.md), portillon 3 du
[livrable 15](./15-instructions-tactiques.md)). Aucun test ne dépend d'un **modèle de langage** : les
livrables 14 et 15 passent leur suite entière, service arrêté.

Les tests côté service vivent dans `service/tests/` et ne font pas partie de la solution .NET.
