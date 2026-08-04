# Livrable 6 — Replanification

> Voir [00-INDEX.md](./00-INDEX.md) pour le contexte général du projet.

## Objectif

Faire réagir un agent lorsque son plan devient invalide : détecter l'événement, mettre à jour ses connaissances, réévaluer ses priorités et relancer GBFS.

## Événements déclencheurs

Un agent doit replanifier lorsque :

* une précondition devient fausse ;
* une action échoue ;
* une porte est verrouillée ;
* un obstacle est découvert ;
* un zombie apparaît ;
* un allié est attaqué ;
* la Utility AI change d'objectif ;
* un objet attendu est absent ;
* le chemin A* est impossible ;
* le niveau de danger dépasse un seuil.

## Flux de replanification

```text
Événement détecté
        ↓
Annulation de l'action actuelle
        ↓
Mise à jour des connaissances
        ↓
Réévaluation Utility AI
        ↓
Création du nouvel état symbolique
        ↓
Recherche GBFS
        ↓
Exécution du nouveau plan
```

## Journalisation

Chaque replanification doit produire un log :

```text
[10:42:13] Alice replans
Reason: Front door is locked
Previous goal: Collect medicine
New goal: Find alternate entrance
```

Ce log alimente le « journal des décisions de l'IA » attendu comme livrable final et le panneau debug (livrable 8).

## Dépendances

Ce livrable s'appuie sur :

* [02-perception-et-memoire.md](./02-perception-et-memoire.md) (mise à jour des connaissances) ;
* [04-utility-ai.md](./04-utility-ai.md) (réévaluation) ;
* [05-planification-gbfs.md](./05-planification-gbfs.md) (recherche GBFS) ;
* le pathfinding interrompu décrit dans [01-carte-et-deplacements.md](./01-carte-et-deplacements.md).

## Étapes à créer

* [x] Détecter l'échec d'une action.
* [x] Invalider un plan.
* [x] Mettre à jour les connaissances suite à l'événement.
* [x] Recalculer les priorités (Utility AI).
* [x] Générer un nouveau plan (GBFS).
* [x] Afficher/logger la raison de la replanification.

## Critère de validation

Un survivant change de plan lorsqu'il découvre qu'une porte est verrouillée.
