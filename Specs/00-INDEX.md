# Hordinary — Index des livrables

Ce dossier découpe [SPECS.md](./SPECS.md) (spécification complète du MVP) en livrables successifs, dans l'ordre où ils doivent être construits. Chaque fichier est autonome : il contient les extraits de spécification nécessaires pour réaliser l'étape, son critère de validation, et les tests associés.

---

## 1. Présentation du projet

**Hordinary** est un prototype de jeu de survie tactique avec des zombies. Le joueur ne contrôle pas directement les survivants : il leur attribue des objectifs généraux, et des agents autonomes décident quoi faire, construisent un plan et adaptent leur comportement selon les événements.

Objectif principal : démontrer une architecture d'IA combinant Utility AI, planification symbolique (GBFS), navigation A*, perception partielle, mémoire individuelle, partage d'informations et replanification dynamique.

## 2. Objectif du MVP

Partie jouable de 5 à 10 minutes : trois survivants récupèrent des médicaments dans une pharmacie puis rejoignent une zone d'extraction, sur une carte partiellement inconnue au départ, en évitant ou distrayant des zombies.

## 3. Expérience attendue

Le joueur observe les survivants analyser leur environnement, choisir une priorité, générer un plan, l'exécuter, découvrir une information inattendue, replanifier, communiquer et improviser face à une horde (voir l'exemple de séquence en section 3 de SPECS.md).

## 4. Périmètre fonctionnel (résumé)

**Obligatoire** : carte unique en grille, 3 survivants autonomes, 20-30 zombies, pharmacie, médicaments, zone d'extraction, bâtiments/portes/obstacles, vision, bruit, mémoire individuelle, Utility AI, planificateur GBFS, navigation A*, replanification, interface de décisions, victoire/défaite.

**Exclu** : monde ouvert, génération procédurale complexe, multijoueur, inventaire avancé, arbre de compétences, crafting, véhicules, cycle jour/nuit, météo, faim/soif, narration complexe, dialogues, sauvegarde persistante, combat tactique avancé, hordes de centaines de zombies.

Ces exclusions valent pour le **MVP**, c'est-à-dire les livrables 1 à 8. Les livrables 11 à 14 en lèvent une partie (inventaire, faim/soif, véhicule, génération procédurale, dialogues) et sont numérotés à la suite précisément pour que la frontière reste lisible : le MVP doit rester jouable et testable sans eux.

## 5. Boucle de jeu

1. Le joueur sélectionne un survivant ou le groupe.
2. Le joueur donne un objectif.
3. L'agent évalue ses priorités avec la Utility AI.
4. L'agent construit un problème de planification depuis ses connaissances.
5. GBFS cherche un plan valide.
6. L'agent utilise A* pour atteindre les lieux nécessaires.
7. L'agent exécute les actions du plan.
8. L'environnement évolue.
9. L'agent perçoit de nouvelles informations.
10. Ses croyances sont mises à jour.
11. Si le plan devient invalide, l'agent replanifie.
12. Le groupe tente d'atteindre l'extraction avec les médicaments.

## 6. Conditions de victoire et de défaite

**Victoire** : au moins un survivant atteint la zone d'extraction en transportant (ou avec un membre du groupe transportant) les médicaments.

**Défaite** : tous les survivants sont morts, ou tous incapables d'agir, ou les médicaments deviennent définitivement inaccessibles.

---

## Livrables (ordre de développement)

| # | Livrable | Fichier | Critère de validation |
|---|----------|---------|------------------------|
| 1 | Carte et déplacements | [01-carte-et-deplacements.md](./01-carte-et-deplacements.md) | Un survivant atteint une destination connue en évitant les obstacles. |
| 2 | Perception et mémoire | [02-perception-et-memoire.md](./02-perception-et-memoire.md) | Le survivant ne peut pas utiliser les informations d'une zone jamais observée. |
| 3 | Zombies et bruit | [03-zombies-et-bruit.md](./03-zombies-et-bruit.md) | Un zombie se dirige vers un bruit sans connaître automatiquement la position exacte du survivant. |
| 4 | Utility AI | [04-utility-ai.md](./04-utility-ai.md) | Un survivant peut abandonner une mission pour fuir un danger immédiat. |
| 5 | Planificateur GBFS | [05-planification-gbfs.md](./05-planification-gbfs.md) | L'objectif « récupérer les médicaments » produit une suite d'actions valide. |
| 6 | Replanification | [06-replanification.md](./06-replanification.md) | Un survivant change de plan lorsqu'il découvre qu'une porte est verrouillée. |
| 7 | Trois survivants et partage d'informations | [07-survivants-et-partage.md](./07-survivants-et-partage.md) | Une information découverte par Alice peut être utilisée par Bob après communication. |
| 8 | Interface et boucle complète | [08-interface-et-boucle-complete.md](./08-interface-et-boucle-complete.md) | Une partie complète peut être jouée du départ jusqu'à la victoire ou la défaite. |

## Extension optionnelle (hors MVP)

| # | Livrable | Fichier | Critère de validation |
|---|----------|---------|------------------------|
| 9 | Retour à la base et debrief | [09-narration-et-debrief.md](./09-narration-et-debrief.md) | Au retour à la base, les connaissances des survivants sont partagées, et un vétéran peut relancer la motivation du groupe, choisir la mission suivante, augmenter des caractéristiques et enseigner une technique. |
| 10 | Groupes de survivants indépendants | [10-groupes-de-survivants-independants.md](./10-groupes-de-survivants-independants.md) | Un groupe indépendant rencontré sur la carte peut être recruté, mis en confiance ou repoussé selon un calcul déterministe, sans service externe. |

Le livrable 9 est entièrement mécanique (partage de connaissances, motivation, montée de niveau,
enseignement de technique) et respecte donc la contrainte « aucun service externe » du MVP ; seul son
habillage narratif optionnel (répliques de debrief via FastAPI + LangChain/LangGraph, désactivable via
`enabled: false`) en dépend, sans jamais modifier `Hordinary.Core` ni les livrables 1 à 8. La création
de nouvelles missions ou techniques (via un LLM local) est hors périmètre du livrable 9 et fera l'objet
d'une spécification à part. Le livrable 10 est purement mécanique (même framework agent que les
survivants du joueur) et respecte cette contrainte ; le livrable 9 peut l'habiller narrativement, en
option, sans jamais changer son résultat.

## Extensions post-MVP (livrables 11 à 14)

Ces quatre livrables se construisent dans l'ordre du tableau. Chacun porte sa priorité en tête de
fichier, et chacun reste retirable : le MVP (livrables 1 à 8) doit continuer à se jouer et à passer ses
tests si l'un d'eux est abandonné en cours de route.

| # | Priorité | Livrable | Fichier | Critère de validation |
|---|----------|----------|---------|------------------------|
| 11 | **P1 — Haute** | Objets, armes et économie du camp | [11-objets-armes-et-economie-du-camp.md](./11-objets-armes-et-economie-du-camp.md) | Un survivant équipe un outil qui change le coût de ses actions, pousse une caisse, et rapporte de quoi nourrir le camp une journée de plus. |
| 12 | **P2 — Haute** | Interface, survivants générés et van | [12-interface-survivants-et-van.md](./12-interface-survivants-et-van.md) | Une partie se joue de bout en bout sans jamais activer le mode debug. |
| 13 | **P3 — Moyenne** | Génération de quartiers | [13-generation-de-cartes.md](./13-generation-de-cartes.md) | Trois missions d'affilée sur trois quartiers différents, reproductibles par leur graine, sans mur invisible ni limite non expliquée. |
| 14 | **P4 — Basse (optionnel)** | Recrutement et dialogues | [14-recrutement-et-dialogues.md](./14-recrutement-et-dialogues.md) | Une rencontre se résout à l'identique, service de dialogue arrêté ou démarré. |

### Justification des priorités

**P1 — Livrable 11.** Il transforme la boucle « une mission, une caisse » en boucle de survie : ce qui
est rapporté se consomme, et ce qui est consommé décide de la mission suivante. Les livrables 12 (le van
brûle l'essence du camp) et 14 (un recruté est une ration de plus) en dépendent mécaniquement. C'est
aussi le livrable qui donne le plus de jeu pour le moins de risque technique.

**P2 — Livrable 12.** Le livrable 11 ajoute une matière que l'interface de démonstration du livrable 8
ne sait pas montrer — un inventaire, une arme équipée, un stock, des survivants sans nom écrit d'avance.
Sans lui, le 11 est jouable mais illisible. Il est second, et pas premier, parce qu'une interface se
dessine autour d'un contenu existant, pas l'inverse.

**P3 — Livrable 13.** Il n'ajoute aucune mécanique : sa valeur est la rejouabilité. Le van dépose le
groupe sur une **zone d'intérêt**, et le terrain de jeu est ce quartier-là — quelques dizaines de cases
de côté, pas une ville. Ce cadrage supprime le seul risque technique de la série : rien à réécrire dans
la navigation, la mémoire des agents ou le rendu, un unique budget à relever
(`navigation.max_explored_nodes`). Il reste en P3 parce que les livrables 11 et 12 apportent davantage
de jeu, pas parce qu'il est risqué. Sa vraie difficulté est ailleurs, et elle est visuelle : faire que
les bords du quartier se lisent comme un endroit et non comme un rectangle.

**P4 — Livrable 14.** Seul livrable dépendant d'un service externe et d'un modèle de langage, donc seul
livrable devant rester désactivable sans conséquence. Il vient en dernier parce qu'il a besoin des trois
autres, et parce qu'il est le seul dont l'abandon ne coûterait rien au reste. Sa règle centrale — le
modèle écrit, il ne décide pas — est la même contrainte que celle déjà posée pour le livrable 9.

## Documents transverses

| Document | Fichier | Contenu |
|----------|---------|---------|
| Architecture & modèle de données | [A-architecture-et-donnees.md](./A-architecture-et-donnees.md) | Arborescence logicielle recommandée, classes principales |
| Configuration externe | [B-configuration.md](./B-configuration.md) | Paramètres à externaliser (utility_ai, planner, navigation, knowledge) |
| Tests unitaires minimums | [C-tests.md](./C-tests.md) | Tests requis par système (Utility AI, Planification, Navigation, Connaissances) |
| Qualité, critères d'acceptation & Definition of Done | [D-qualite-et-definition-of-done.md](./D-qualite-et-definition-of-done.md) | Contraintes de code, critères d'acceptation du MVP, priorités, DoD |
| README attendu | [E-readme-attendu.md](./E-readme-attendu.md) | Contenu obligatoire du README du projet |
| Direction artistique & prompts | [F-direction-artistique-et-prompts.md](./F-direction-artistique-et-prompts.md) | Registre graphique, charte technique, palette, et un prompt par sprite des livrables 11 à 13 |
| Export PDDL & validation tierce | [G-export-pddl.md](./G-export-pddl.md) | Traduction de `planner_domain.json` en PDDL valide, résolu par Fast Downward et validé par VAL. Ne change rien au jeu : outil de vérification, à faire après le livrable 11 |

## Priorité en cas de contrainte de temps

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

## Technologies

Moteur préféré : **Godot 4.7**. Langage recommandé : **C#**. Le moteur et le langage retenus doivent être documentés dans le README. Le planificateur doit être implémenté dans le projet, sans dépendance à un service externe (une représentation inspirée de PDDL est acceptable, sans moteur PDDL complet).

## Livrables finaux attendus

```text
1. Le projet jouable
2. Le code source
3. Le README
4. Les tests
5. Une carte de démonstration
6. Un mode debug
7. Les fichiers de configuration
8. Un journal des décisions de l'IA
```

Optionnel : vidéo/GIF de démonstration, diagramme d'architecture de l'IA.
