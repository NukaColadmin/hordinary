# Livrable 3 — Zombies et bruit

> Voir [00-INDEX.md](./00-INDEX.md) pour le contexte général du projet.

## Objectif

Ajouter les zombies (errance, vision, poursuite) et un système de bruit auquel ils réagissent, sans jamais connaître automatiquement la position exacte d'un survivant.

## Zombies

### États

Machine à états ou Utility AI simple. États minimums :

```text
Idle
Wander
InvestigateNoise
Chase
Attack
SearchLastKnownPosition
```

### Perception

Un zombie peut :

* voir un survivant ;
* entendre un bruit ;
* mémoriser la dernière position connue d'une cible.

Il ne doit **pas** connaître la position exacte d'un survivant caché.

### Comportement

Ordre de priorité recommandé :

```text
Survivant visible
Bruit récent
Dernière position connue
Errance
```

### Déplacement collectif

Règles simples : attraction vers une cible, séparation pour éviter les superpositions, légère cohésion avec les zombies proches. Aucune planification complexe n'est requise pour les zombies.

## Système de bruit

### Sources de bruit

| Action                | Niveau de bruit |
| ---------------------- | --------------: |
| Marcher                |               1 |
| Courir                 |               3 |
| Ouvrir une porte       |               2 |
| Forcer une porte       |               6 |
| Casser une fenêtre     |               8 |
| Créer une distraction  |              10 |

### Propagation

```python
position: GridPosition
intensity: float
created_at: float
```

L'intensité diminue avec la distance :

```python
perceived_intensity = intensity - distance
```

Un obstacle peut réduire l'intensité.

### Réaction des zombies

Lorsqu'un zombie entend un bruit suffisamment fort :

1. il mémorise la position approximative ;
2. il passe dans l'état `InvestigateNoise` ;
3. il se dirige vers la source ;
4. il cherche autour de la dernière position connue.

## Architecture concernée

`World/NoiseSystem`, `AI/Zombies/ZombieStateMachine`, `AI/Zombies/HordeBehaviour` — voir [A-architecture-et-donnees.md](./A-architecture-et-donnees.md).

## Étapes à créer

* [x] Ajouter les zombies (20 à 30 sur la carte).
* [x] Implémenter l'errance (`Wander`).
* [x] Implémenter la vision des zombies.
* [x] Implémenter les événements sonores (`NoiseSystem`, propagation, atténuation).
* [x] Faire réagir les zombies aux bruits (`InvestigateNoise`).
* [x] Ajouter la poursuite (`Chase`, `Attack`, `SearchLastKnownPosition`).
* [x] Ajouter les règles de déplacement collectif (attraction/séparation/cohésion).

## Critère de validation

Un zombie se dirige vers un bruit sans connaître automatiquement la position exacte du survivant.
