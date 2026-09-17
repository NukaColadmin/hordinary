# Hordinary — Index des livrables

Ce dossier découpe la spécification complète du MVP en livrables successifs, dans l'ordre où ils doivent être construits. Chaque fichier est autonome : il contient les extraits de spécification nécessaires pour réaliser l'étape, son critère de validation, et les tests associés. **Ce document est la seule spécification d'ensemble** : le `SPECS.md` d'origine a été entièrement absorbé par les fichiers de ce dossier et n'existe plus.

---

## 1. Présentation du projet

**Hordinary** est un prototype de jeu de survie tactique avec des zombies. Le joueur ne contrôle pas directement les survivants : il leur attribue des objectifs généraux, et des agents autonomes décident quoi faire, construisent un plan et adaptent leur comportement selon les événements.

Objectif principal : démontrer une architecture d'IA combinant Utility AI, planification symbolique (GBFS), navigation A*, perception partielle, mémoire individuelle, partage d'informations et replanification dynamique.

## 2. Objectif du MVP

Partie jouable de 5 à 10 minutes : trois survivants récupèrent des médicaments dans une pharmacie puis rejoignent une zone d'extraction, sur une carte partiellement inconnue au départ, en évitant ou distrayant des zombies.

## 3. Expérience attendue

Le joueur observe les survivants analyser leur environnement, choisir une priorité, générer un plan, l'exécuter, découvrir une information inattendue, replanifier, communiquer et improviser face à une horde. La séquence de démonstration complète est celle du [livrable 8](./08-interface-et-boucle-complete.md).

## 4. Périmètre fonctionnel (résumé)

**Obligatoire** : carte unique en grille, 3 survivants autonomes, 20-30 zombies, pharmacie, médicaments, zone d'extraction, bâtiments/portes/obstacles, vision, bruit, mémoire individuelle, Utility AI, planificateur GBFS, navigation A*, replanification, interface de décisions, victoire/défaite.

**Exclu** : monde ouvert, génération procédurale complexe, multijoueur, inventaire avancé, arbre de compétences, crafting, véhicules, cycle jour/nuit, météo, faim/soif, narration complexe, dialogues, sauvegarde persistante, combat tactique avancé, hordes de centaines de zombies.

Ces exclusions valent pour le **MVP**, c'est-à-dire les livrables 1 à 8. Les livrables 11 à 15 en lèvent une partie (inventaire, faim/soif, véhicule, génération procédurale, dialogues, instructions en langage naturel) et sont numérotés à la suite précisément pour que la frontière reste lisible : le MVP doit rester jouable et testable sans eux.

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

**Temps imparti** : une mission dure au plus `mission.time_limit` (cinq minutes dans le fichier livré, redéfinissable mission par mission dans `missions.json`). Le délai écoulé, le van annonce le départ et **tout le groupe est rappelé** : chaque survivant reçoit un ordre de retour, qui entre dans les scores de la Utility AI comme un ordre du joueur — plus lourd, mais négociable face à un danger immédiat.

**Victoire** : tous les survivants encore capables d'agir sont revenus au van, et l'un d'eux transporte les médicaments. Le retour d'un seul ne termine pas la mission : on ne laisse personne derrière.

**Défaite** : tous les survivants sont morts, ou tous incapables d'agir, ou les médicaments deviennent définitivement inaccessibles, ou le temps est écoulé et le groupe rentre les mains vides.

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

## Extension optionnelle (hors MVP) — livrables 9 et 10, absorbés

Ces deux extensions ont été annoncées, puis **fondues dans le livrable 14** plutôt que spécifiées à
part. Elles n'ont donc pas de fichier propre, et il ne faut pas en attendre un.

| # | Extension | Ce qu'elle est devenue |
|---|-----------|------------------------|
| 9 | Retour à la base et debrief | Le stock et la résolution de fin de journée sont passés au [livrable 11](./11-objets-armes-et-economie-du-camp.md) ; l'écran de camp au [livrable 12](./12-interface-survivants-et-van.md) ; l'habillage narratif (répliques de debrief) était prévu au livrable 14, retiré |
| 10 | Groupes de survivants indépendants | Devenu la partie « rencontres » du livrable 14, retiré |

Ce qui a été conservé de leur cahier des charges, et qui vaut toujours : la mécanique reste
**entièrement déterministe et sans service externe** ; seul l'habillage textuel dépend d'un modèle, il
est désactivable par `dialogue.enabled: false`, et il ne modifie ni `Hordinary.Core` ni les livrables 1
à 8. La création de nouvelles missions ou de nouvelles techniques par un modèle reste hors périmètre.

## Extensions post-MVP (livrables 11 à 15)

Chacun porte sa priorité en tête de fichier, et chacun reste retirable : le MVP (livrables 1 à 8) doit
continuer à se jouer et à passer ses tests si l'un d'eux est abandonné en cours de route.

**L'ordre de réalisation n'est pas l'ordre de numérotation.** Le numéro dit quand le livrable a été
spécifié ; la priorité dit quand il se fait :

```text
11  →  12  →  16  →  G  →  15  →  13
```

Le document transverse [G-export-pddl.md](./G-export-pddl.md) s'intercale parce qu'il est un
**prérequis dur du livrable 15**. Le livrable 15 passe devant le 13 parce qu'il n'en dépend pas et
qu'il porte davantage la démonstration. Le livrable 16 passe avant les deux parce qu'il ne coûte
rien au reste : il produit des `MissionDefinition` comme le fichier en produisait, et tout ce qui
est en aval — planificateur, suivi de partie, écran de camp — ne sait pas d'où elles viennent. Le
livrable 13 reste spécifié et reste en fin de file ; le livrable 14 est retiré.

| # | Priorité | Livrable | Fichier | Critère de validation |
|---|----------|----------|---------|------------------------|
| 11 | **P1 — Haute** | Objets, armes et économie du camp | [11-objets-armes-et-economie-du-camp.md](./11-objets-armes-et-economie-du-camp.md) | Un survivant équipe un outil qui change le coût de ses actions, pousse une caisse, et rapporte de quoi nourrir le camp une journée de plus. |
| 12 | **P2 — Haute** | Interface, survivants générés et van | [12-interface-survivants-et-van.md](./12-interface-survivants-et-van.md) | Une partie se joue de bout en bout sans jamais activer le mode debug. |
| 13 | **P3 — Moyenne** | Génération de quartiers | [13-generation-de-cartes.md](./13-generation-de-cartes.md) | Trois missions d'affilée sur trois quartiers différents, reproductibles par leur graine, sans mur invisible ni limite non expliquée. |
| 14 | *retiré* | Recrutement et dialogues | — | Spécification retirée ; la personnalité des survivants n'est plus un concept du projet. |
| 15 | **P2 — Haute** | Instructions tactiques en langage naturel | [15-instructions-tactiques.md](./15-instructions-tactiques.md) | « Avant d'entrer dans un bâtiment, regroupez-vous » change le domaine de planification ; et là où elle rend la mission impossible, elle est refusée avant application, avec sa raison. |
| 16 | **P2 — Haute** | Missions aléatoires et récompenses | [16-missions-aleatoires-et-recompenses.md](./16-missions-aleatoires-et-recompenses.md) | Trois offres déterministes proposent des objectifs de zombies, de récupération ou de ressources, avec leurs récompenses exactes affichées avant le départ. |

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

**P4 — Livrable 14.** Il vient en dernier parce qu'il a besoin des trois autres, et parce qu'il est le
seul dont l'abandon ne coûterait rien au reste. Sa règle centrale — le modèle écrit, il ne décide pas —
est la même contrainte que celle déjà posée pour le livrable 9.

**P2 — Livrable 15.** Numéroté après le 14, mais à faire avant lui : il ne dépend que du livrable 11 et
du document transverse [G-export-pddl.md](./G-export-pddl.md) — **celui-ci en prérequis dur, et non
plus en simple appui** : les instructions se compilent dans le domaine **PDDL**, pas dans le JSON, donc
la spec G doit être terminée avant que le livrable 15 commence, et elle doit exposer sa représentation
intermédiaire plutôt que seulement des fichiers. Il partage avec le livrable 14 le projet
`service/` et la règle « le modèle ne décide de rien », mais il l'applique à un objet différent — une
contrainte de plan plutôt qu'une réplique — et il est le seul des deux dont la sortie se **vérifie
objectivement** : une contrainte compile ou ne compile pas, laisse la mission solvable ou la rend
impossible, et Fast Downward tranche. Il est en P2 parce que c'est le livrable qui rend la
planification symbolique visible pour un lecteur non spécialiste.

## Documents transverses

| Document | Fichier | Contenu |
|----------|---------|---------|
| Architecture & modèle de données | [A-architecture-et-donnees.md](./A-architecture-et-donnees.md) | Arborescence logicielle recommandée, classes principales |
| Configuration externe | [B-configuration.md](./B-configuration.md) | Paramètres à externaliser (utility_ai, planner, navigation, knowledge) |
| Tests unitaires minimums | [C-tests.md](./C-tests.md) | Tests requis par système (Utility AI, Planification, Navigation, Connaissances) |
| Qualité, critères d'acceptation & Definition of Done | [D-qualite-et-definition-of-done.md](./D-qualite-et-definition-of-done.md) | Contraintes de code, critères d'acceptation du MVP, priorités, DoD |
| README attendu | [E-readme-attendu.md](./E-readme-attendu.md) | Contenu obligatoire du README du projet |
| Direction artistique & prompts | [F-direction-artistique-et-prompts.md](./F-direction-artistique-et-prompts.md) | Registre graphique, charte technique, palette, et un prompt par sprite des livrables 11 à 13 |
| Export PDDL & validation tierce | [G-export-pddl.md](./G-export-pddl.md) | Traduction de `planner_domain.json` en PDDL valide, résolu par Fast Downward et validé par VAL. Ne change rien au jeu : outil de vérification, à faire après le livrable 11 — et **prérequis dur du livrable 15**, qui compile ses contraintes dans le domaine PDDL |

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

Cette contrainte tient toujours après [G-export-pddl.md](./G-export-pddl.md) : aucun planificateur tiers ne tourne pendant une partie, et le jeu n'acquiert aucune dépendance. Fast Downward et VAL sont des outils **hors ligne**, de vérification, et les tests qui les emploient se marquent `Skip` quand le binaire est absent.

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
