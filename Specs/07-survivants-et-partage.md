# Livrable 7 — Trois survivants et partage d'informations

> Voir [00-INDEX.md](./00-INDEX.md) pour le contexte général du projet.

## Objectif

Passer d'un seul survivant testable à trois profils distincts, avec exploration répartie et partage de connaissances entre alliés.

## Survivants

### Alice — Éclaireuse

Caractéristiques : rapide, bonne vision, peu bruyante, faible résistance, exploration efficace.

Actions spéciales : observer une zone, regarder par une fenêtre, se déplacer silencieusement, détecter les zombies à plus longue distance.

### Bob — Bricoleur

Caractéristiques : vitesse moyenne, bruit moyen, bonne capacité à ouvrir les accès, résistant.

Actions spéciales : forcer une porte, ouvrir une porte verrouillée, barricader une porte, créer une distraction sonore.

### Claire — Secouriste

Caractéristiques : vitesse moyenne, bonne capacité de soutien, faible efficacité au combat.

Actions spéciales : soigner un survivant, stabiliser un survivant, réduire la panique, transporter les médicaments plus efficacement.

## Caractéristiques des survivants (modèle)

```python
id: str
name: str
position: GridPosition
health: float
stamina: float
panic: float
alive: bool
inventory: list[Item]
current_goal: Goal | None
current_plan: Plan | None
current_action: Action | None
knowledge: AgentKnowledge
```

## Exploration répartie

### Frontier exploration

Une frontière est une cellule connue et accessible adjacente à une cellule inconnue.

```python
score = (
    expected_information_gain
    - distance_cost
    - perceived_danger
    - ally_duplication_penalty
)
```

L'agent choisit la meilleure frontière accessible.

### Répartition entre survivants

Deux survivants ne doivent pas automatiquement choisir la même frontière. Ajouter une pénalité lorsqu'un autre survivant explore déjà cette zone.

## Partage des connaissances

### Communication

Les survivants peuvent partager leurs connaissances lorsqu'ils sont à moins de cinq cellules les uns des autres. Optionnellement, une radio globale peut simplifier le gameplay.

### Fusion des croyances

Lorsqu'une information est transmise :

* conserver sa source ;
* diminuer légèrement sa confiance ;
* privilégier les informations les plus récentes ;
* privilégier les observations directes.

```python
def merge_belief(current, incoming):
    if incoming.source == DIRECT_OBSERVATION:
        return incoming

    if incoming.last_observed_at > current.last_observed_at:
        return incoming

    return current
```

Fréquence recommandée : partage des connaissances 1 fois par seconde.

## Étapes à créer

* [x] Ajouter les trois profils (Alice, Bob, Claire) avec leurs caractéristiques.
* [x] Ajouter les actions/compétences spéciales de chaque survivant.
* [x] Implémenter la frontier exploration et son score.
* [x] Ajouter la pénalité de duplication d'exploration entre survivants.
* [x] Implémenter le partage de croyances à portée de communication.
* [x] Implémenter `merge_belief` (fusion des croyances).
* [x] Permettre l'aide entre alliés (`HelpAlly`, `HealAlly`).

## Critère de validation

Une information découverte par Alice peut être utilisée ensuite par Bob après communication.

## Tests associés

Réutilise les tests « Connaissances » de [C-tests.md](./C-tests.md), notamment : le partage de connaissances n'accède pas au `WorldState`.
