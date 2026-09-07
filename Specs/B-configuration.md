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
…), chacune passée dans une courbe de réponse. Exemple :

```json
{
  "goals": [
    {
      "name": "FleeDanger",
      "considerations": [
        { "input": "perceived_danger", "curve": "linear" },
        { "input": "health_ratio", "curve": "inverse" },
        { "input": "panic", "curve": "linear" }
      ],
      "combine": "multiply"
    },
    {
      "name": "HelpAlly",
      "considerations": [
        { "input": "ally_danger", "curve": "linear" },
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
      "name": "PickUpObjective",
      "verb": "take",
      "params": { "item": "objective" },
      "preconditions": ["at(agent, objective_zone)", "objective_found"],
      "effects": ["has(agent, objective)", "objective_collected"],
      "cost": 1,
      "noise_level": 0,
      "duration": 1
    }
  ]
}
```

Ajouter, retirer ou ajuster une action doit rester une modification de configuration, jamais une
recompilation.

## Où vit quoi

Les livrables post-MVP ajoutent chacun leurs fichiers. La règle qui les gouverne tous est celle
ci-dessus : **un catalogue est une donnée, jamais une classe C# nommée**. Ce tableau existe pour qu'on
sache où chercher, et pour qu'aucun réglage ne soit ajouté deux fois à deux endroits.

| Fichier | Contenu | Introduit par |
|---------|---------|---------------|
| `game_config.json` | Réglages de simulation, et les sections ajoutées ensuite : `inventory` (11), `van` (12), `dialogue` (14), `instructions` (15) | MVP |
| `planner_domain.json` | Actions et objectifs du planificateur — 23 schémas, 30 après le livrable 11 | Livrable 5 |
| `utility_goals.json` | Objectifs et considérations de la Utility AI | Livrable 4 |
| `survivors.json` | Roster écrit à la main, et référence des tests | Livrable 7 |
| `map_locations.json` | Zones de la carte de démonstration | Livrable 1 |
| `missions.json` | Missions, leur `objective` et leur `map_profile` | Livrable 8 |
| `tile_legend.json` | `tile_size` et la table `source:x,y → tuile` | Livrable 1 |
| `items.json`, `weapons.json` | Catalogues d'objets et d'armes | Livrable 11 |
| `camp.json` | Stock du camp et résolution de fin de journée | Livrable 11 |
| `zones.json` | Profils de zone (`layout`, accès, densité, butin) | Livrable 11 |
| `survivor_generation.json` | Graine, noms, archétypes, traits, `appearance` | Livrable 12 |
| `mapgen.json` | Profils de quartier et réglages de bordure | Livrable 13 |
| `encounters.json` | Types de rencontres et seuils de recrutement | Livrable 14 |
| `instruction_presets.json` | Instructions pré-écrites du mode sans modèle | Livrable 15 |

Deux règles transverses qui valent pour tous :

* **`tile_size` est l'unique commutateur de géométrie.** Il vaut `16` aujourd'hui et passe à `32` au
  livrable 11 ; tout ce qui en dépend le lit, rien ne le duplique.
* **Aucune clé d'API dans un fichier versionné.** Les sections `dialogue` et `instructions` déclarent un
  `api_key_env` — le nom d'une variable d'environnement — et jamais la clé elle-même.
