# Livrable 2 — Perception et mémoire

> Voir [00-INDEX.md](./00-INDEX.md) pour le contexte général du projet.

## Objectif

Introduire la séparation entre l'état réel du monde et ce que chaque agent croit savoir, avec un champ de vision et une mémoire individuelle qui expire pour les informations dynamiques.

## Séparation des états

Le système doit distinguer :

* `WorldState` — la vérité complète du jeu ;
* `AgentKnowledge` — uniquement ce que l'agent sait ou croit savoir.

Les systèmes de décision ne doivent **jamais** accéder directement aux informations inconnues du monde réel.

## Vision

Chaque survivant possède :

```python
vision_range: int
field_of_view: float
```

La vision est bloquée par les murs, les portes fermées, les obstacles opaques.

Lorsqu'une cellule est visible :

* son type est enregistré ;
* les entités visibles sont enregistrées ;
* l'heure de l'observation est enregistrée.

## Mémoire

Chaque connaissance doit contenir :

```python
value: Any
confidence: float
last_observed_at: float
source: KnowledgeSource
```

Sources possibles :

```text
DIRECT_OBSERVATION
ALLY_COMMUNICATION
INFERENCE
SOUND
```

## Informations statiques

Restent fiables : murs, bâtiments, fenêtres, emplacement de la pharmacie, emplacement de l'extraction.

## Informations dynamiques

Perdent en fiabilité : position d'un zombie, position d'un allié, niveau de danger d'une zone, état d'une porte pouvant être modifiée.

```python
confidence = max(
    0,
    initial_confidence - decay_rate * elapsed_time
)
```

## États inconnus

L'absence d'information ne signifie pas que l'information est fausse. Le système doit distinguer :

* « La pièce est vide » ;

de :

* « La pièce n'a pas été observée ».

## Modèle de données concerné

```python
class AgentKnowledge:
    known_cells: dict[GridPosition, CellBelief]
    known_entities: dict[str, EntityBelief]
    known_items: dict[str, ItemBelief]
    known_locations: dict[str, LocationBelief]
```

Voir [A-architecture-et-donnees.md](./A-architecture-et-donnees.md) pour le modèle complet.

## Étapes à créer

* [x] Ajouter le champ de vision.
* [x] Masquer les zones inconnues (cellules `UNKNOWN` tant que non observées).
* [x] Créer `AgentKnowledge`.
* [x] Enregistrer les cellules observées (avec confiance, source, timestamp).
* [x] Mémoriser les positions connues (entités, items, lieux).
* [x] Appliquer la décroissance de confiance aux informations dynamiques.
* [x] Afficher la carte mentale en mode debug.
* [x] Faire naviguer A* sur la carte mentale de l'agent plutôt que sur la carte réelle (bascule depuis le livrable 1).

## Critère de validation

Le survivant ne peut pas utiliser les informations d'une zone qu'il n'a jamais observée.

## Tests associés

Voir [C-tests.md](./C-tests.md) section « Connaissances » :

* un agent ne connaît pas une cellule non observée ;
* une observation directe met à jour une croyance ;
* la confiance d'une position de zombie diminue ;
* une information récente remplace une information ancienne ;
* le partage de connaissances n'accède pas au `WorldState` (préparation du livrable 7).
