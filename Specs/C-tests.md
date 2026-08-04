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
