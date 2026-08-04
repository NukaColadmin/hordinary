# A — Architecture logicielle et modèle de données

> Document transverse référencé par tous les livrables ([00-INDEX.md](./00-INDEX.md)).

## Architecture logicielle recommandée

```text
Game
├── World
│   ├── GridMap
│   ├── WorldState
│   ├── EntityManager
│   ├── NoiseSystem
│   └── PerceptionSystem
│
├── Agents
│   ├── Survivor
│   ├── Zombie
│   ├── AgentKnowledge
│   ├── Belief
│   └── Inventory
│
├── AI
│   ├── Utility
│   │   ├── UtilityGoal
│   │   ├── UtilityScorer
│   │   └── UtilitySelector
│   │
│   ├── Planning
│   │   ├── SymbolicState
│   │   ├── PlanningAction
│   │   ├── Planner
│   │   ├── GBFS
│   │   └── Heuristic
│   │
│   ├── Navigation
│   │   ├── AStar
│   │   ├── NavigationGrid
│   │   └── PathFollower
│   │
│   └── Zombies
│       ├── ZombieStateMachine
│       └── HordeBehaviour
│
├── Commands
│   ├── PlayerCommand
│   └── CommandManager
│
├── UI
│   ├── HUD
│   ├── AgentPanel
│   ├── PlanPanel
│   └── DebugOverlay
│
└── Tests
    ├── UtilityTests
    ├── PlannerTests
    ├── NavigationTests
    └── KnowledgeTests
```

## Modèle de données simplifié

```python
class WorldState:
    grid: GridMap
    survivors: dict[str, Survivor]
    zombies: dict[str, Zombie]
    items: dict[str, Item]
    noises: list[NoiseEvent]


class Survivor:
    id: str
    position: GridPosition
    health: float
    panic: float
    inventory: list[Item]
    knowledge: AgentKnowledge
    current_goal: Goal | None
    current_plan: Plan | None


class AgentKnowledge:
    known_cells: dict[GridPosition, CellBelief]
    known_entities: dict[str, EntityBelief]
    known_items: dict[str, ItemBelief]
    known_locations: dict[str, LocationBelief]


class Plan:
    goal: Goal
    actions: list[PlanningAction]
    current_action_index: int
    created_from_state_hash: str


class PlanningAction:
    name: str
    preconditions: list[Condition]
    effects: list[Effect]
    cost: float
```

## Gestion des mises à jour

Pour éviter d'exécuter tous les systèmes à chaque frame :

```text
Rendu : chaque frame
Déplacement : chaque frame
Perception : 5 à 10 fois par seconde
Utility AI : 1 à 2 fois par seconde
Planification : uniquement à la demande
Partage des connaissances : 1 fois par seconde
Zombie AI : 2 à 5 fois par seconde
```
