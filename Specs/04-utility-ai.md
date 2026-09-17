# Livrable 4 — Utility AI

> Voir [00-INDEX.md](./00-INDEX.md) pour le contexte général du projet.

## Objectif

Faire choisir à chaque survivant son objectif courant selon un score d'utilité, sans oscillation excessive.

## Rôle

La Utility AI choisit l'objectif actuel d'un survivant. Elle ne choisit pas directement chaque déplacement — elle sélectionne un objectif parmi plusieurs possibilités.

## Objectifs disponibles

```text
CompletePlayerMission
ExploreUnknownArea
FleeDanger
HealSelf
SearchForObjective
PickUpObjective
ReachExtraction
Hide
```

Le catalogue livré comptait aussi `HelpAlly`, `HealAlly`, `CalmAlly`, `Regroup` et `CreateDistraction`.
Ils ont été retirés au [livrable 15](./15-instructions-tactiques.md) : porter secours à quelqu'un n'est
pas un réflexe, c'est une **stratégie**, et les stratégies viennent désormais du joueur — par un ordre
direct (« Regroupez-vous », toujours là) ou par une instruction tactique. La diversion part avec eux, en
un peu plus tranché : faire du bruit exprès pour attirer la horde sur soi et dégager un allié est un
sacrifice calculé, décidé en sachant qui est où et ce qu'on protège. Personne ne fait cela sans qu'on le
lui demande. Ce qui reste ici est ce qu'un survivant décide seul quand personne ne lui dit rien : fuir,
se cacher, se soigner, se battre, fouiller, rapporter.

`FleeDanger` et `Hide` portent une précondition `at_extraction` : on ne fuit pas le van. C'est le seul
endroit de la carte où aucun coup ne porte (voir [livrable 8](./08-interface-et-boucle-complete.md)),
donc le seul d'où fuir revient à courir vers le danger. Tenir la position **ailleurs** reste une
décision tactique, et donc au joueur.

## Calcul des scores

Chaque objectif reçoit un score entre `0` et `1`.

```python
flee_score = (
    perceived_danger
    * low_health_factor
    * panic_factor
)
```

```python
distraction_score = (
    ally_danger
    * safety_factor
    * distance_factor
)
```

```python
mission_score = (
    player_priority
    * confidence_in_plan
    * safety_factor
)
```

## Changement d'objectif

Un objectif ne doit pas être remplacé à chaque frame. Utiliser un seuil minimum, un cooldown de décision, une hystérésis :

```python
if new_score > current_score + 0.15:
    switch_goal()
```

La Utility AI peut être réévaluée :

* toutes les 0,5 à 1 seconde ;
* lorsqu'un événement important survient ;
* lorsqu'un plan échoue ;
* lorsqu'un zombie est découvert ;
* lorsqu'un survivant est blessé ;
* lorsqu'un bruit important est entendu.

Fréquence recommandée en régime de croisière : 1 à 2 fois par seconde (voir la gestion des mises à jour dans [A-architecture-et-donnees.md](./A-architecture-et-donnees.md)).

## Data-driven : objectifs comme composition de considérations

Comme les actions du planificateur ([05-planification-gbfs.md](./05-planification-gbfs.md)), les
objectifs ci-dessus ne doivent **pas** devenir une méthode de score par objectif dans le code
(`ComputeFleeScore()`, `ComputeHideScore()`, …). Le code ne connaît qu'un petit socle de
**considérations primitives réutilisables** — `perceived_danger`, `health_ratio`, `panic`,
`distance_to(cible)`, `ally_danger`, … — chacune une
fonction pure d'entrée (une valeur du monde ou de la mémoire) vers `[0, 1]` via une courbe de réponse
(linéaire, quadratique, seuil, inversée).

Un objectif est alors une **donnée** : un nom, une liste de considérations pondérées à combiner (par
exemple par multiplication, pour qu'une seule considération à 0 annule le score), et éventuellement
des préconditions. `flee_score`, `distraction_score` et `mission_score` de ce document sont ainsi trois
*instances* d'un seul mécanisme de composition, pas trois formules différentes. Ajouter un objectif
(ou en retirer un pour un test) doit rester une modification de configuration — voir
[B-configuration.md](./B-configuration.md) — jamais une recompilation.

## Architecture concernée

`AI/Utility/UtilityGoal`, `UtilityScorer`, `UtilitySelector` — voir [A-architecture-et-donnees.md](./A-architecture-et-donnees.md).

## Étapes à créer

* [ ] Créer les objectifs (liste ci-dessus).
* [ ] Créer les considérations (danger perçu, santé, panique, distance, etc.).
* [ ] Calculer les scores par objectif.
* [ ] Afficher les scores (préparation du mode debug, livrable 8).
* [ ] Ajouter un cooldown de décision.
* [ ] Gérer les changements d'objectif avec hystérésis.

## Critère de validation

Un survivant peut abandonner une mission pour fuir un danger immédiat.

## Tests associés

Voir [C-tests.md](./C-tests.md) section « Utility AI » :

* la fuite obtient un score élevé lorsque le danger est fort ;
* la mission reste prioritaire lorsque le danger est faible ;
* un survivant loyal privilégie davantage l'aide à un allié ;
* l'hystérésis évite les changements constants d'objectif.
