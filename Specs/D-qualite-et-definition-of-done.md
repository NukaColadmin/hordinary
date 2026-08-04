# D — Qualité, critères d'acceptation & Definition of Done

> Document transverse référencé par tous les livrables ([00-INDEX.md](./00-INDEX.md)), à valider en particulier au terme du [livrable 8](./08-interface-et-boucle-complete.md).

Les **critères d'acceptation** ci-dessous sont ceux du MVP et ne bougent pas : un livrable post-MVP qui les casserait serait à retirer, pas à négocier. La **Definition of Done**, elle, vaut pour tout livrable, MVP ou non — c'est elle que les livrables 11 et 12 invoquent quand ils se déclarent bloqués tant que leurs images ne sont pas livrées : une fonctionnalité qu'on ne voit pas en jeu n'est pas terminée, même si son code passe ses tests.

## Contraintes de qualité

Le code doit :

* être modulaire ;
* être lisible ;
* utiliser des noms explicites ;
* éviter les dépendances inutiles ;
* séparer simulation, IA et interface ;
* permettre l'ajout de nouvelles actions ;
* permettre l'ajout de nouveaux objectifs ;
* permettre de modifier les coefficients de Utility AI ;
* permettre de modifier les coûts A* ;
* permettre de modifier les limites GBFS ;
* inclure des logs utiles ;
* inclure des tests automatisés pour les systèmes critiques.

Éviter :

* les classes géantes ;
* les dépendances circulaires ;
* l'accès direct des agents au monde complet ;
* les comportements codés entièrement avec des `if` imbriqués ;
* la planification à chaque frame ;
* les données de gameplay codées en dur dans plusieurs fichiers.

## Critères d'acceptation du MVP

Le MVP est terminé lorsque tous les critères suivants sont remplis.

### Gameplay

* Le joueur peut lancer une partie.
* Trois survivants sont présents.
* Des zombies se déplacent sur la carte.
* Le joueur peut demander au groupe de récupérer les médicaments.
* Les survivants peuvent atteindre la pharmacie.
* Les survivants peuvent récupérer les médicaments.
* Les survivants peuvent rejoindre l'extraction.
* La victoire et la défaite fonctionnent.

### Perception

* La carte commence partiellement inconnue.
* Chaque survivant possède ses propres connaissances.
* Les agents ne connaissent pas les zombies non observés.
* Les informations dynamiques peuvent devenir obsolètes.
* Les survivants peuvent partager des informations.

### Intelligence artificielle

* La Utility AI sélectionne les objectifs.
* GBFS génère les plans.
* A* génère les chemins.
* Les agents replanifient lorsqu'une action échoue.
* Les zombies réagissent à la vision et au bruit.
* Les décisions sont affichées à l'écran.

### Démonstration

* Le mode debug affiche la perception.
* Le mode debug affiche les scores Utility AI.
* Le mode debug affiche les chemins A*.
* Le mode debug affiche les plans GBFS.
* Le mode debug affiche les causes de replanification.
* Une partie produit au moins une situation émergente compréhensible.

## Priorité de développement

En cas de contrainte de temps, respecter cet ordre :

```text
1. Perception partielle
2. Utility AI
3. Planification GBFS
4. Replanification
5. Navigation A*
6. Système de bruit
7. Interface debug
8. Coopération
9. Combat
10. Polish visuel
```

La qualité de l'architecture IA est prioritaire sur la qualité graphique.

## Definition of Done

Une fonctionnalité est terminée uniquement lorsqu'elle :

* fonctionne dans le jeu ;
* possède un cas de test ou un scénario de validation ;
* produit des logs compréhensibles ;
* est visible dans le mode debug lorsque cela est pertinent ;
* ne donne pas accès aux agents à des informations omniscientes ;
* est documentée brièvement.

Le projet est terminé lorsqu'une partie complète peut être jouée et que l'on peut clairement observer :

```text
Perception
→ Mise à jour des connaissances
→ Choix Utility AI
→ Planification GBFS
→ Navigation A*
→ Exécution
→ Échec ou découverte
→ Replanification
```
