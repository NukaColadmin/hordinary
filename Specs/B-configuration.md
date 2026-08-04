# B — Configuration externe recommandée

> Document transverse référencé par tous les livrables ([00-INDEX.md](./00-INDEX.md)), notamment [04-utility-ai.md](./04-utility-ai.md), [05-planification-gbfs.md](./05-planification-gbfs.md), [01-carte-et-deplacements.md](./01-carte-et-deplacements.md) et [02-perception-et-memoire.md](./02-perception-et-memoire.md).

Placer les paramètres dans des ressources, fichiers JSON ou fichiers de configuration — pas codés en dur dans le code.

```json
{
  "utility_ai": {
    "evaluation_interval": 0.75,
    "switch_threshold": 0.15
  },
  "planner": {
    "max_nodes": 1000,
    "max_depth": 20,
    "max_time_ms": 50
  },
  "navigation": {
    "known_cell_cost": 1,
    "unknown_cell_cost": 4,
    "dangerous_cell_cost": 15
  },
  "knowledge": {
    "zombie_confidence_decay": 0.08,
    "communication_range": 5
  }
}
```

## Objectifs (Utility AI, data-driven)

Comme les actions ci-dessous, les objectifs (voir [04-utility-ai.md](./04-utility-ai.md)) ne sont pas
une méthode de score par objectif dans le code, mais une composition de **considérations
primitives** réutilisables (`perceived_danger`, `health_ratio`, `panic`, `distance_to`,
`personality.caution`, …), chacune passée dans une courbe de réponse. Exemple :

```json
{
  "goals": [
    {
      "name": "FleeDanger",
      "considerations": [
        { "input": "perceived_danger", "curve": "linear" },
        { "input": "health_ratio", "curve": "inverse" },
        { "input": "panic", "curve": "linear" },
        { "input": "personality.caution", "curve": "linear" }
      ],
      "combine": "multiply"
    },
    {
      "name": "HelpAlly",
      "considerations": [
        { "input": "ally_danger", "curve": "linear" },
        { "input": "personality.loyalty", "curve": "linear" },
        { "input": "distance_to(ally)", "curve": "inverse" },
        { "input": "personal_safety", "curve": "linear" }
      ],
      "combine": "multiply"
    }
  ]
}
```

Ajouter, retirer ou repondérer un objectif doit rester une modification de configuration, jamais une
recompilation — exactement comme pour les actions ci-dessous.

## Actions du planificateur (data-driven)

Les actions PDDL/GBFS (voir [05-planification-gbfs.md](./05-planification-gbfs.md)) ne sont pas des
classes une par action, mais des données décrivant préconditions, effets, coût, bruit et durée,
construites à partir d'un petit socle de **verbes d'action primitifs et paramétrables** (`toPos`,
`take`, `put`, `setFlag`, `wait`, …) exécutés par un moteur générique. Exemple :

```json
{
  "actions": [
    {
      "name": "OpenDoor",
      "verb": "setFlag",
      "params": { "condition": "door_open(door)", "value": true },
      "preconditions": ["at(agent, door)", "!door_locked(door)"],
      "effects": ["door_open(door)"],
      "cost": 1,
      "noise_level": 2,
      "duration": 0.5
    },
    {
      "name": "PickUpMedicine",
      "verb": "take",
      "params": { "item": "medicine" },
      "preconditions": ["at(agent, pharmacy)", "medicine_found"],
      "effects": ["has(agent, medicine)", "medicine_collected"],
      "cost": 1,
      "noise_level": 0,
      "duration": 1
    }
  ]
}
```

Ajouter, retirer ou ajuster une action doit rester une modification de configuration, jamais une
recompilation.
