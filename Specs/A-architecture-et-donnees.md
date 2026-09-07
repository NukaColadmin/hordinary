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

### Ce que les livrables 11 à 15 ajoutent

L'arborescence ci-dessus est celle du MVP. Les extensions post-MVP s'y greffent sans la réorganiser —
c'est une contrainte, pas une observation : un livrable qui demanderait de déplacer `AI/Planning/` ne
serait plus retirable.

```text
Hordinary.Core
├── Agents/Inventory                                    livrable 11
├── World/ItemCatalogue, WeaponCatalogue, HeavyObject    livrable 11
├── World/MapGeneration/  (6 passes + validation)        livrable 13
├── Game/CampStock                                      livrable 11
├── Game/SurvivorGenerator                               livrable 12
├── Game/Encounter, EncounterResolver, CampReputation    livrable 14
├── AI/Planning/ActionVerbs   (push, emit_noise)         livrable 11
├── AI/Planning/Pddl/  (PddlDomain, PddlAction,
│                       PddlFormula, PddlWriter)         document G
└── AI/Instructions/  (TacticalConstraint, Grammar,
                       DomainCompiler, TrajectoryChecker,
                       InstructionLog)                   livrable 15

Scripts/                     (Godot, jamais de logique de regles)
├── UI/*                     refonte complete            livrable 12
├── Dialogue/DialogueClient                              livrable 14
└── Instructions/InstructionClient                       livrable 15

service/                     projet Python separe, hors solution .NET
                             endpoints /instruction (15) et /dialogue (14)

tools/pddl/                  sorties versionnees du document G
```

**La ligne de partage ne bouge jamais** : tout ce qui décide vit dans `Hordinary.Core` et se teste sans
Godot ; `Scripts/` affiche et appelle ; `service/` écrit du texte et ne décide de rien. Les trois
livrables qui touchent à un modèle de langage (14, 15, et l'habillage narratif) sont précisément ceux
qui doivent rester coupables de zéro décision.

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
