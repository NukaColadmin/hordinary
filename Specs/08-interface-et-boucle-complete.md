# Livrable 8 — Interface et boucle complète

> Voir [00-INDEX.md](./00-INDEX.md) pour le contexte général du projet.

## Objectif

Fermer la boucle de jeu : commandes joueur, pharmacie/médicaments/extraction, victoire/défaite, panneau d'inspection et mode debug complet.

## Santé, dégâts et panique

### Santé

```python
health: float  # 0 à 100
```

À `0`, le survivant meurt ou devient incapable d'agir.

### Attaques

Combats simples pour le MVP. Un zombie au contact inflige des dégâts à intervalles réguliers. Les survivants peuvent : fuir, repousser un zombie, effectuer une attaque simple, aider un allié.

**Une exception, une seule : la zone d'extraction.** Un survivant qui se tient dans le van ne peut pas
être frappé — la horde se presse contre la tôle, elle n'atteint pas ce qui est dedans. Sans cette règle,
le premier rentré passe le reste de la mission à se faire entamer en attendant les autres, et le groupe
est puni de la seule chose que la victoire lui demande : s'attendre. La règle est posée sur le monde
(`EntityManager.Sheltered`) et non sur la décision du zombie, qui ne connaît pas la carte ; il décide
d'attaquer comme avant. Une carte qui ne déclare pas de zone `Extraction` n'abrite personne. Côté
décision, `FleeDanger` et `Hide` se ferment au van : un abri que l'IA quitte à la première silhouette
n'est pas un abri (voir [livrable 4](./04-utility-ai.md)).

### Panique

Augmente : plusieurs zombies proches, allié attaqué, survivant blessé, horde arrivant, survivant isolé.

Diminue : loin du danger, près d'un allié, après un soin, en zone sûre.

Influence : priorité de fuite, prudence, budget de planification, stabilité des décisions.

## Commandes disponibles

Le joueur peut :

* sélectionner un survivant ;
* sélectionner le groupe ;
* demander d'explorer une zone ;
* demander de récupérer les médicaments ;
* demander de rejoindre l'extraction ;
* demander de se regrouper ;
* interdire les actions bruyantes ;
* autoriser les actions bruyantes ;
* définir une zone interdite.

## Informations visibles

L'interface doit afficher : santé, panique, objectif actuel, action actuelle, plan courant, prochain point de déplacement, score des principaux objectifs Utility AI, informations connues par l'agent, dernière cause de replanification.

```text
Alice

Objectif :
Explorer la pharmacie

Action actuelle :
Observer la porte principale

Plan :
1. Atteindre la pharmacie
2. Observer l'entrée
3. Ouvrir la porte
4. Fouiller la pièce

Dernière replanification :
Porte principale verrouillée
```

## Mode debug

Doit afficher :

* le champ de vision ;
* la carte mentale de chaque agent ;
* les cellules inconnues ;
* les dernières positions connues des zombies ;
* les chemins A* ;
* les frontières d'exploration ;
* les scores Utility AI ;
* les nœuds explorés par GBFS ;
* les plans rejetés ;
* la raison d'un échec de planification.

Ce mode est essentiel pour la démonstration du portfolio.

### Contrôles de debug

En plus de l'affichage ci-dessus, le mode debug doit permettre de désactiver la panique et le stress
(le champ `panic` reste à 0 et n'influence plus la Utility AI). Cela permet d'observer le comportement
de planification et de navigation isolément, sans l'influence de la panique.

## Scénario de démonstration

Le joueur donne l'objectif « Récupérer une caisse de médicaments » au groupe. Sans script imposé, la
boucle suivante doit se dérouler et rester observable depuis le mode debug :

1. Les survivants quittent la zone de départ.
2. Ils explorent la carte pour localiser la pharmacie.
3. Ils coopèrent face aux zombies rencontrés (évitement, partage d'informations, distraction, entraide).
4. Ils atteignent la pharmacie et récupèrent la caisse de médicaments.
5. Ils rejoignent la zone d'extraction avec la caisse. La mission se termine quand le groupe entier
   y est rentré — le retour d'un seul ne conclut rien.
6. À défaut, le temps imparti (`mission.time_limit`) s'écoule : le van annonce le départ, tout le
   monde est rappelé, et la partie se conclut quand chacun est rentré — avec la caisse ou sans elle.
7. Le verdict rendu, la carte se laisse regarder `mission.debrief_seconds`, puis **l'écran de camp
   reprend la main** : la journée est résolue, le verdict s'affiche en tête, et les missions du
   lendemain sont proposées. Une mission finie ramène au camp, elle ne fige pas la partie.

Le comportement émergent (replanification suite à un imprévu, communication d'une menace, entraide)
doit apparaître naturellement grâce à la Utility AI, au planificateur et au partage de connaissances
déjà spécifiés dans les livrables précédents (04 à 07) — aucune séquence scriptée n'est requise.

## Architecture concernée

`Commands/PlayerCommand`, `CommandManager`, `UI/HUD`, `AgentPanel`, `PlanPanel`, `DebugOverlay` — voir [A-architecture-et-donnees.md](./A-architecture-et-donnees.md).

## Étapes à créer

* [x] Ajouter les commandes joueur (sélection, objectifs, autorisations de bruit, zone interdite).
* [x] Ajouter la pharmacie et l'objet médicaments.
* [x] Ajouter la zone d'extraction.
* [x] Ajouter la santé, les dégâts et la panique.
* [x] Implémenter la condition de victoire et de défaite.
* [x] Ajouter le temps imparti par mission et le rappel automatique au van.
* [x] Créer le panneau d'inspection (HUD/AgentPanel/PlanPanel).
* [x] Finaliser le mode debug (tous les éléments listés ci-dessus).
* [x] Construire la carte permettant de jouer le scénario de démonstration de bout en bout.
* [x] Ajouter l'interrupteur debug pour désactiver la panique et le stress.

## Critère de validation

Une partie complète peut être jouée du départ jusqu'à la victoire ou la défaite.

## À vérifier avant de considérer le MVP terminé

Voir [D-qualite-et-definition-of-done.md](./D-qualite-et-definition-of-done.md) pour les critères d'acceptation complets et la Definition of Done.
