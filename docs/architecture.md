# Hordinary

Prototype de survie tactique dont l'interet est l'architecture d'IA : Utility AI, planification
symbolique GBFS, navigation A*, perception partielle, memoire individuelle et replanification.
Le joueur ne pilote pas les survivants, il leur donne des objectifs.

La specification complete est dans [`Specs/`](Specs/00-INDEX.md), decoupee en huit livrables.

## Etat d'avancement

| Livrable | Etat |
|---|---|
| 1 — Carte et deplacements | fait |
| 2 — Perception et memoire | fait |
| 3 — Zombies et bruit | fait |
| 4 — Utility AI | fait |
| 5 — Planificateur GBFS | fait |
| 6 — Replanification | fait |
| 7 — Survivants et partage | fait |
| 8 — Interface et boucle complete | fait |

Le MVP est complet : une partie se joue du choix de l'equipe jusqu'a la victoire ou la defaite.

Au-dela des huit livrables, la boucle de jeu est passee de « une partie » a « une mission » :

* **Preparation** — le joueur choisit ses agents dans une grille de fiches en bas d'ecran, puis sa
  mission dans `config/missions.json`, puis demarre. Voir [Preparation de mission](#preparation-de-mission).
* **Le camp** — une mission commence et finit au meme endroit. La zone d'extraction est posee sur la
  zone de depart : on part du camp, on y revient.
* **Le rappel** — remplir la mission **n'arrete pas le jeu**. Celui qui rapporte la caisse repart
  chercher les autres pour leur annoncer « Mission terminee, rentrez au camp s'il vous plait », et
  chacun disparait de la carte en annoncant son retour. Le verdict ne tombe qu'une fois le terrain
  vide. Voir [Le rappel au camp](#le-rappel-au-camp).
* **La motivation** — une seconde jauge, `SANITY`, a cote de la sante. Voir [Sante et motivation](#sante-et-motivation).
* **Le mode debug s'eteint** — console, touches d'observation, panneaux de texte et overlays se
  coupent depuis `config/game_config.json`. Voir [Mode debug](#mode-debug).

## Technologies

* **Godot 4.7** (`Godot.NET.Sdk` 4.7.1), rendu 2D vue du dessus, carte en `TileMapLayer`.
* **C# / .NET 8** pour le projet de jeu, **.NET 10** pour les tests.
* Aucun service externe : le planificateur GBFS est implemente dans le projet (`Hordinary.Core/AI/Planning`),
  sans moteur PDDL, avec une representation inspiree de PDDL.

## Lancer le jeu

```bash
dotnet build Hordinary.sln
```

Puis ouvrir le dossier dans Godot 4.7 et lancer la scene `Scenes/Main.tscn` (scene principale par defaut).

Le jeu s'ouvre sur l'ecran de preparation : choisir ses agents dans la grille du bas, une mission
dans la liste, puis `DEMARRER LA MISSION`. Pour aller droit au but, `debug.skip_setup` a `true` dans
[`config/game_config.json`](config/game_config.json) lance directement la premiere mission avec toute
l'equipe.

> Le runtime .NET 8 n'est pas requis : `Hordinary.csproj` active `RollForward=LatestMajor`,
> l'assembly s'execute donc sur le runtime .NET majeur installe.

## Lancer les tests

```bash
dotnet test Tests/Hordinary.Tests.csproj
```

Les tests ne dependent pas de Godot : ils s'executent sur `Hordinary.Core`. Ceux de `DemoMapTests`,
`SurvivorJourneyTests`, `ZombieTests`, `UtilityTests`, `PlannedMissionTests` et `ReplanningTests`
decodent directement `Scenes/Main.tscn` pour verifier que la carte livree respecte toujours la
specification, qu'un survivant la traverse, que la horde y erre sans se coincer, qu'Alice y lache sa
mission devant un zombie et qu'elle y planifie puis execute la recuperation des medicaments — meme
sans editeur installe. `UtilityTests`, `PlannerTests`, `ReplanningTests`, `MemoryTests` et
`ConfigurationTests` lisent les vrais fichiers de `config/` : c'est la configuration livree qui est
verifiee, pas une copie.

`MemoryTests` couvre ce qu'un agent retient au-dela des cellules : les coordonnees d'un objet vu et
son invalidation differee, la fouille infructueuse, la carte de chaleur des menaces et son oubli, le
type d'un bruit entendu, et le coup d'oeil par-dessus l'epaule.

`RecallTests` couvre le rappel au camp : la mission remplie qui n'arrete pas la partie, le message
qui ne se transmet qu'a portee de voix, le messager qui part chercher celui qui ignore encore et qui
ne rentre pas avant, le survivant qui quitte le terrain sans mourir, et le verdict qui ne tombe qu'au
dernier retour. `SanityTests` couvre la jauge de motivation, `MissionCatalogueTests` le catalogue de
missions et sa validation.

`BandTests` couvre le livrable 7 : les trois profils et leurs competences, `merge_belief` ligne par
ligne, la portee de communication, la repartition des frontieres, l'entraide — et le critere de
validation, « une information decouverte par Alice peut etre utilisee ensuite par Bob apres
communication ».

`CommandTests`, `OutcomeTests`, `CombatTests` et `DebugModeTests` couvrent le livrable 8 : les
commandes du joueur et les deux refus qui comptent (un lieu inconnu, un groupe qu'on ne sait pas
situer), l'amputation reelle du domaine par une consigne de silence, une zone interdite devenue
infranchissable pour A*, les deux actions de combat et l'impossibilite d'aller chercher la bagarre,
l'interrupteur de panique — et le critere de validation du livrable, « une partie complete peut etre
jouee du depart jusqu'a la victoire ou la defaite », joue de bout en bout sur la carte livree.

`MissionRun` deroule la boucle complete sans le moteur — perception, connaissances, partage, choix
Utility AI, planification GBFS, navigation A*, execution, replanification — et sert aux tests des
livrables 5 a 7 (`team: true` enrole les trois survivants). Le cablage lui-meme vit dans
`SurvivorAgent` et `SurvivorBand`, donc dans le coeur : la couche Godot et ce harnais executent le
meme code, pas deux copies qui divergent.

## Controles

| Entree | Effet |
|---|---|
| `Tab` / clic sur un survivant | changer de point de vue : les panneaux et les overlays suivent le survivant choisi |
| `G` | selectionner le groupe : les ordres suivants s'adressent a tout le monde |
| Clic gauche | donner au survivant selectionne l'objectif d'aller sur la case visee |
| `Maj` + un ordre | adresser l'ordre au groupe entier plutot qu'au seul survivant selectionne |
| Clic droit | cycler le niveau de danger d'une case : aucun -> suspecte -> zombie proche |
| `1` / `2` | recuperer les medicaments / rentrer au camp (la sortie, qui est aussi le depart) |
| `3` | rappeler au camp : l'agent vise abandonne la mission et rentre, puis disparait en arrivant |
| Clic sur une fiche du bas | changer de point de vue, comme `Tab` |
| `4` | explorer la zone sous le curseur, ou la frontiere retenue si le curseur n'en survole aucune |
| `5` | se regrouper |
| `B` | interdire ou autoriser les actions bruyantes |
| `Z` | interdire ou autoriser la zone sous le curseur |
| `R` | marcher ou courir : plus vite, mais trois fois plus bruyant |
| `Espace` | jeter une distraction sur la case sous le curseur (bruit de niveau 10) |
| `C` | effacer les zones de danger du survivant selectionne |
| `F1` | afficher ou masquer l'overlay de navigation (chemin A*, frontieres, zones interdites) |
| `F2` | afficher ou masquer la carte mentale du survivant selectionne |
| `F3` | afficher ou masquer les bruits et l'etat des zombies |
| `F4` | detailler les scores Utility AI, consideration par consideration |
| `F5` | afficher ou masquer le panneau de planification |
| `F6` | derouler le journal des decisions de l'IA |
| `F7` | detailler le groupe : intentions connues et frontieres candidates notees |
| Molette / clic milieu | zoom / deplacement de la vue |
| `Echap` | quitter |

`Clic droit`, `Espace`, `C` et `F1` a `F7` sont les touches de l'**observateur**, pas du joueur :
elles obeissent a `debug.commands` et ne repondent plus quand le mode debug est eteint. Les autres
sont celles du joueur et repondent toujours.

Il y a **trois** survivants depuis le livrable 7, et l'interface le prend au serieux : elle n'affiche
jamais « ce que le groupe sait », parce que cela n'existe pas. `Tab` change de point de vue, et la
carte mentale, la carte de chaleur, les scores, le plan et le journal changent avec lui. Le
survivant selectionne est cercle de blanc ; la ligne « Groupe » du HUD indique qui est a portee de
voix de lui, donc qui pourra lui apprendre quelque chose a la prochaine seconde.

Depuis le livrable 4, un clic n'est plus un ordre de deplacement mais **un objectif** : le survivant
le met en concurrence avec ses autres priorites. Si une horde arrive, il ne s'y rend pas — et le
journal dit pourquoi. Depuis le livrable 5, cet objectif n'est plus resolu en une destination mais
en un **plan** : le HUD affiche la suite d'actions, l'action en cours et son avancement. Depuis le
livrable 6, ce plan est surveille : le HUD compte les replanifications et affiche la derniere cause,
et `F6` deroule le journal des decisions. Depuis le livrable 7, l'ordre s'adresse a **une** personne :
c'est `Maj` qui le donne au groupe, et chacun reste libre de le mettre de cote.

L'overlay `F1` affiche le chemin A*, les noeuds explores, les cellules dangereuses — celles posees a
la main comme celles dont Alice se souvient, en degrade selon ce qu'il lui en reste — et les lieux
nommes. L'overlay `F2` pose le brouillard de la carte mentale : noir la ou Alice n'a jamais regarde,
sombre la ou elle se souvient, clair dans son champ de vision. L'overlay `F3` trace la portee des
bruits en cours et relie chaque zombie a la case qu'il vise. `F4` deplie le detail des scores
Utility AI, consideration par consideration ; `F5` deplie le plan, action par action, l'action en
cours marquee `>` et les actions faites `x` ; `F6` deroule les dernieres lignes du journal des
decisions, replanifications comprises. Le HUD indique le temps de jeu, la sante et la panique
d'Alice, son objectif courant et ses meilleurs scores, son plan et son avancement, le compte des
replanifications par cause, la part du terrain exploree, le contenu de sa memoire et l'etat de la
horde.

La couleur d'un zombie suffit a lire son etat sans overlay : vert il erre, jaune il enquete sur un
bruit, orange il fouille, rouge il a quelqu'un en vue.

## Architecture

Le code est separe en trois projets pour que l'IA reste testable sans le moteur.

```
Hordinary.Core/          logique pure, aucune reference a Godot
├── World/               GridPosition, CellType, MapZone, GridMap, WorldState, EntityManager,
│                        PerceptionSystem, LineOfSight, PerceptionSettings,
│                        NoiseSystem, NoiseEvent, NoiseSettings
├── Agents/              Survivor, SurvivorAgent, SurvivorBand, Zombie, IPerceivingAgent,
│                        AgentKnowledge, KnowledgeSharing, BeliefMerge, DangerMemory,
│                        Belief (Cell/Entity/Item/Location/Noise/Search/Intent/Danger),
│                        RearGlance, Personality, MissionOrder, SurvivorSettings,
│                        SurvivorProfile, SurvivorSkill, SurvivorRoster
├── AI/Navigation/       NavigationGrid, AStar, PathFollower, NavigationSettings, NavigationProfile
├── AI/Utility/          UtilityGoal, Consideration, ResponseCurve, UtilityInputs, UtilityContext,
│                        UtilityScorer, UtilitySelector, UtilityGoalSet, UtilityAiSettings,
│                        FrontierExplorer, Frontier
├── AI/Planning/         Fact, Condition, Effect, SymbolicState, ActionVerb, ActionSchema,
│                        PlanningAction, PlanningDomain, PlanningProblem, SymbolicStateBuilder,
│                        ActionRelevance, Heuristic, Gbfs, Plan, Planner,
│                        PlanExecutor, ActionFailure, IPlanRuntime, WorldPlanRuntime
├── AI/Replanning/       ReplanCause, ReplanTrigger, PlanValidity, ReplanMonitor,
│                        Replanner, DecisionJournal, ReplanRecord, ReplanningSettings
├── AI/Zombies/          ZombieStateMachine, ZombieState, HordeBehaviour, ZombieSettings
├── Commands/            PlayerCommand, PlayerCommandKind, CommandManager, StandingOrders
├── Game/                MissionTracker, MissionOutcome, MissionVerdict,
│                        MissionDefinition, MissionCatalogue, MissionRecall
└── Config/              GameConfig, MapLocationsConfig, TileLegend, DebugSettings,
                         MissionSettings

Scripts/                 couche Godot (projet Hordinary.csproj)
├── World/MapLoader      convertit les TileMapLayer en GridMap
├── Agents/SurvivorNode  affichage et pas de simulation d'un survivant
├── Agents/ZombieNode    affichage d'un zombie, couleur selon l'etat
├── Game/GameRoot        assemblage, cadences, entrees joueur
├── Game/Trace           robinet de la console, commande par debug.enabled
├── Game/CameraController
├── UI/MissionSetupScreen  ecran de preparation : agents, mission, demarrage
├── UI/AgentCard         une fiche d'agent : nom, role, jauges HP et SANITY
├── UI/AgentRoster       la barre d'equipe du bas, en mission (interface du joueur)
├── UI/InfoPanel         socle commun des trois panneaux de texte (mode debug)
├── UI/Hud               etat de la partie : mission, rappel, groupe, horde, ordres
├── UI/AgentPanel        etat d'un survivant : objectif, action, plan, replanification
├── UI/PlanPanel         etat de la recherche : noeuds GBFS, plans rejetes, journal
├── UI/NavigationDebugOverlay
├── UI/KnowledgeDebugOverlay
└── UI/NoiseDebugOverlay

Tests/                   xUnit, sans Godot
config/                  parametres externes (aucune valeur de gameplay codee en dur)
├── game_config.json     navigation, perception, bruit, survivant, zombies, utility_ai, planner,
│                        replanning, knowledge, exploration, mission, debug
├── missions.json        les missions proposees : objectif, camp, caisses, taille de horde
├── survivors.json       les trois profils, leurs caracteristiques, competences et aptitudes
├── utility_goals.json   les objectifs et leurs considerations
├── planner_domain.json  les actions du planificateur et la traduction des objectifs en buts
├── map_locations.json   zones nommees de la carte
└── tile_legend.json     correspondance tuiles -> types de cellules
```

`MapLoader` est le seul point de contact entre le moteur et la simulation : tout le reste de l'IA
travaille sur `GridMap` et ignore l'existence de Godot.

## Navigation A*

Le modele de cout vit dans `NavigationGrid` ; `AStar` ne connait ni les murs ni le danger.

| Cellule | Cout d'entree |
|---|---|
| libre connue | 1 |
| inconnue | 4 |
| danger suspecte | 8 |
| proche d'un zombie | 15 |
| porte fermee | 1 + 3 (ouverture) |
| mur, obstacle, fenetre, porte verrouillee | infini |

Les deplacements sont autorises dans les huit directions (`allow_diagonal`, active par defaut) : un
pas diagonal coute racine de 2 fois le cout de la cellule, et la coupe de coin est interdite — les
deux cellules orthogonales adjacentes doivent etre libres. Une porte percee dans un mur ne peut
donc etre franchie que de face. Repasser `allow_diagonal` a `false` dans
[`config/game_config.json`](config/game_config.json) ramene une grille a 4 voisins sans autre
changement : `AStar` bascule seul de l'heuristique de Manhattan a celle de Chebyshev.

Le danger **remplace** le cout de base au lieu de s'y ajouter, conformement au bareme de la
specification. Une cellule inconnue est franchissable mais jamais consideree comme surement libre :
A* prefere un detour connu plus long a un raccourci non observe. `NavigationProfile.FromCaution`
module ces penalites selon la personnalite (affine au livrable 7).

L'heuristique est la distance de grille multipliee par le cout minimal d'une cellule : jamais
surestimee, donc A* reste optimal.

> Depuis le livrable 2, la `NavigationGrid` est construite sur `AgentKnowledge.MentalMap` et non
> plus sur la carte reelle. `AStar` n'a pas eu a changer d'une ligne.

## Perception et memoire

La verite du monde (`WorldState`) et ce qu'un agent croit savoir (`AgentKnowledge`) sont deux objets
distincts. `AgentKnowledge` n'a aucune reference vers `WorldState` — un test le verifie par
reflexion — et ne recoit que l'**etendue** du terrain, jamais son contenu. Seul `PerceptionSystem`
franchit la frontiere : il lit le monde et n'ecrit que dans la memoire de l'agent qui observe.

### Vision

`vision_range` et `field_of_view_degrees` viennent de la section `perception` de
[`config/game_config.json`](config/game_config.json). Une cellule est visible si elle est dans la
portee (distance euclidienne), dans le cone oriente selon le deplacement, et si la ligne de vue de
Bresenham n'est coupee ni par un mur, ni par une porte fermee, ni par un obstacle. La cellule
bloquante est visible — on voit le mur — mais rien derriere.

La perception tourne a `update_interval` (0,15 s, soit ~7 fois par seconde).

### Croyances

Chaque connaissance porte les quatre champs de la specification : `value`, `confidence`,
`last_observed_at`, `source` (`DirectObservation`, `AllyCommunication`, `Inference`, `Sound`).

| Information | Decroissance par seconde |
|---|---|
| mur, sol, fenetre, lieu, objet | aucune — information statique |
| etat d'une porte | 0,02 |
| position d'un allie | 0,05 |
| position d'un zombie | 0,08 |
| origine d'un bruit entendu | 0,10 |

`confidence = max(0, confiance_initiale - taux * temps_ecoule)`. Une information plus ancienne ne
remplace jamais une information plus recente, quelle que soit sa source.

### Une position vieillit, une trajectoire aussi

Une croyance d'entite ne porte pas qu'un endroit : elle porte aussi le **cap** qu'on prete a
l'entite, deduit de deux observations successives. C'est la difference entre « il etait la » et
« il me suivait ».

| Reglage (`knowledge`) | Defaut | Role |
|---|---:|---|
| `heading_memory` | 2 s | ecart maximal entre deux apercus pour en deduire un cap |
| `heading_smoothing` | 0,5 | part du cap precedent conservee — une trajectoire se confirme, elle ne saute pas |
| `maximum_tracked_speed` | 3 cases/s | plafond : un saut de position ne devient pas un zombie supersonique |
| `prediction_horizon` | 3 s | au-dela, l'extrapolation s'arrete |

`PredictedPositionOf` prolonge la derniere position vue le long de ce cap. C'est cette position
**supposee** — et non le souvenir — qui entre dans le calcul de danger : Alice fuit le zombie, pas
l'endroit ou il se tenait. La supposition s'arrete a ce qu'elle sait du terrain : elle ne prete a
personne le pouvoir de traverser un mur qu'elle a vu, mais une cellule jamais observee ne bloque
rien — elle ignore ce qu'elle contient, elle ne peut pas s'en servir pour ecarter une hypothese.
Passe `prediction_horizon`, la supposition se fige et seule la decroissance de confiance travaille :
deviner une trajectoire une minute apres avoir perdu quelqu'un de vue serait une invention.

### Absence d'information

L'absence d'une entree dans `known_cells` signifie « jamais observe », ce qui n'est pas « vide ».
Les deux etats sont distincts et testes :

* une cellule jamais vue reste `CellType.Unknown` sur la carte mentale et coute 4 a A* ;
* quand Alice regarde la case ou quelqu'un **devrait se trouver** et la trouve vide, la croyance
  tombe a une confiance de zero, cap compris — elle sait alors que la case est vide.

Le dementi porte bien sur la position supposee, pas sur celle d'origine. Sans cette nuance, un
survivant poursuivi qui se retourne verrait vide l'endroit ou le zombie se tenait deux secondes plus
tot, en conclurait qu'il n'y a plus de zombie, et repartirait tranquillement — alors qu'il vient
precisement de confirmer que le zombie avance. `PerceptionTests` couvre les deux cotes : constater
vide la case d'origine ne dement rien, constater vide la case supposee dement bien.

### Objets : l'invalidation differee

Un objet ne bouge pas tout seul. S'il n'est plus la ou Alice l'avait vu, c'est que quelqu'un l'a
pris — et **rien ne la previent**. Elle continue donc de planifier avec une boite de medicaments qui
a change de main, jusqu'a ce qu'elle revienne sur la case et constate le vide. C'est seulement la
que `ItemBelief.Confidence` tombe a zero et que `IsGone` devient vrai.

L'invalidation est differee, et c'est le point : c'est ce qui rend la carte mentale faillible
plutot que magique, et c'est exactement le declencheur « un objet attendu est absent » de la
replanification. Voir une case voisine ne suffit pas — il faut voir *la* case.

Meme regle pour les fouilles. Une pharmacie fouillee sans rien y trouver laisse une `SearchBelief`,
qui redevient le fait `searched_in_vain(pharmacy)` cote planificateur. Sans cette memoire, un
survivant dont la fouille echoue replanifie la meme fouille indefiniment : le plan est reconstruit
depuis une carte mentale qui a oublie la deception.

### Zones de danger : une carte de chaleur qui s'efface

Apercevoir un zombie ne marque pas une case mais un voisinage — on evite la rue, pas le pave.
`DangerMemory` propage la menace **de proche en proche sur les cases praticables de la carte
mentale**, pas en ligne droite : un zombie derriere un mur ne rend pas le couloir d'a cote
dangereux. Un bruit marque aussi, plus faiblement (`danger_noise_weight`) — on ne sait ni ce qui l'a
produit, ni exactement d'ou il venait.

Le souvenir s'efface a `danger_decay_per_second`. Sans oubli, Alice finirait par croire tout le
quartier infrequentable et ne bougerait plus.

Deux consommateurs :

* **A\*** — `NavigationGrid.Sensed` traduit la carte de chaleur en cout de trajet, via deux seuils
  (`danger_suspected_threshold`, `danger_confirmed_threshold`). Alice contourne alors l'endroit ou
  elle a vu passer quelque chose, meme si elle ne le voit plus. La navigation ne lit pas la memoire
  elle-meme : elle ne connait ni les agents ni l'horloge, c'est l'assemblage qui branche les deux.
* **La Utility AI** — l'entree `remembered_danger` dit « on a deja eu peur ici », la ou
  `perceived_danger` dit « quelque chose menace maintenant ».

L'overlay `F1` la dessine en degrade : une case qui palit sous les yeux du joueur, c'est un
survivant qui oublie.

### Memoire auditive

Un `NoiseBelief` retient **quoi** (`NoiseAction`) et **a quel point** (`PerceivedIntensity`), a une
position approximative — jamais **qui**. La signature de `RememberNoise` n'accepte aucun identifiant
de source : il est donc structurellement impossible de deduire d'un fracas de vitre que c'est un
zombie. `IsRemarkable` distingue le passage du fracas.

### Le coup d'oeil par-dessus l'epaule

Un survivant qui fuit regarde devant lui, donc ne voit plus ce qui le poursuit : la derniere
position connue de la menace vieillit sans jamais etre rafraichie, et il court sans savoir si elle
est encore derriere.

`RearGlance` lui fait tourner brievement son champ de vision vers l'arriere pendant qu'il fuit
(`rear_glance_interval`, `rear_glance_duration`), des que la pression de poursuite depasse
`rear_glance_threshold`. Le prix est reel — il ne voit plus devant pendant ce temps-la — et c'est ce
qui en fait un compromis plutot qu'un capteur gratuit. La classe ne decide pas s'il faut fuir : elle
recoit la pression deja calculee par la Utility AI et se contente de cadencer le geste.

### Consequence sur la navigation

Alice planifie a travers l'inconnu, puis se heurte a ce qu'elle decouvre. Quand la perception revele
un mur sur son itineraire, `PathFollower` emet `Blocked`, l'action de deplacement echoue, et la
replanification du livrable 6 s'en saisit. `SurvivorJourneyTests` rejoue cette boucle complete sur la
carte livree, sans Godot, et verifie qu'Alice atteint la pharmacie et l'extraction en ne se servant
que de ce qu'elle a vu.

Les lieux de la mission (depart, pharmacie, extraction) sont donnes a Alice au demarrage par le
joueur, avec la source `Inference` : elle sait ou aller, pas ce qu'il y a sur le chemin.

## Zombies et bruit

Vingt-quatre zombies sont repartis sur la carte au demarrage, a neuf cases au moins de la zone de
depart. Le placement suit la graine `zombies.seed` : la meme carte donne toujours la meme horde.

### Machine a etats

`ZombieStateMachine` applique l'ordre de priorite de la specification — survivant visible, bruit
recent, derniere position connue, errance — et **ne lit que la memoire du zombie**. Un test par
reflexion verifie qu'aucun champ, parametre ou retour de `ZombieStateMachine` ni de `Zombie` ne
mentionne `WorldState`, `NoiseSystem` ou `NoiseEvent`.

| Etat | Declencheur | Vitesse |
|---|---|---|
| `Chase` / `Attack` | croyance d'observation directe datant de moins de `sight_memory` | 2,4 |
| `InvestigateNoise` | souvenir sonore plus recent que ce qu'il sait de sa cible | 1,8 |
| `SearchLastKnownPosition` | derniere position connue, puis fouille du voisinage | 1,8 |
| `Wander` / `Idle` | rien d'autre a faire | 1,1 |

Un zombie n'erre que vers des cases **qu'il a deja observees** : il ne se donne pas rendez-vous dans
une piece dont il ignore l'existence. Quand il arrive a la derniere position connue et n'y trouve
personne, il ne renonce pas tout de suite : il fouille les environs pendant `search_duration`.

`HordeBehaviour` choisit ensuite la case adjacente qui maximise `attraction + separation + cohesion`.
Aucune planification — la specification l'exclut pour les zombies — et la meme regle anti-coupe-de-
coin que la navigation, pour qu'un zombie ne rase pas l'angle d'un mur.

C'est le seul endroit qui recoit la carte reelle, et c'est deliberé : decider **ou aller** passe par
la memoire, savoir **si le corps passe** passe par la geometrie. Un zombie qui ignore ou est sa proie
ne traverse pas les murs pour autant.

### Bruit

| Action | Niveau | Portee en terrain degage |
|---|---:|---:|
| marcher | 1 | 0,5 case |
| ouvrir une porte | 2 | 1,5 case |
| courir | 3 | 2,5 cases |
| forcer une porte | 6 | 5,5 cases |
| casser une fenetre | 8 | 7,5 cases |
| creer une distraction | 10 | 9,5 cases |

`perceived_intensity = intensity - distance`, conformement a la specification, moins
`obstacle_attenuation` par cellule opaque traversee. Un bruit est entendu si l'intensite qui parvient
a l'auditeur atteint `hearing_threshold`. La portee ci-dessus en decoule directement : marcher est
quasiment silencieux, et la distraction est le seul bruit qui porte a travers un quartier.

A ce stade, Alice emet un bruit de pas a chaque case franchie, un bruit d'ouverture en traversant une
porte fermee, et une distraction sur commande. Forcer une porte et casser une fenetre sont au bareme
et dans la configuration, mais restent a produire : ce sont des actions du livrable 5.

### Un bruit ne trahit pas une position

C'est le critere de validation du livrable, et il tient a une seule chose : ce qui entre dans la
memoire d'un zombie n'est pas la position de la source mais **une position approximative**.

```
rayon d'erreur = min(max_localisation_error, round((intensity - perceived) * localisation_error_factor))
```

L'erreur croit donc avec la distance et avec les murs traverses. Un bruit a bout portant est situe
exactement, un bruit venu du bout de la rue peut l'etre a quatre cases pres. Le zombie converge vers
un endroit, pas vers quelqu'un — et l'overlay `F3` encadre cet endroit pour le rendre visible.

Le souvenir d'un bruit porte la source `Sound` et perd `noise_confidence_decay` par seconde, soit dix
secondes de memoire. Il n'est jamais rafraichi tant que l'evenement dure : sinon un vacarme continu
resterait eternellement « recent » et figerait la horde dessus. Un zombie arrive sur place sans rien
trouver oublie le bruit sur le champ.

### Perception des zombies

Zombies et survivants passent par le meme `PerceptionSystem`, via `IPerceivingAgent` : un seul chemin
entre le monde et une memoire, donc la meme garantie pour les deux camps. Un zombie voit moins loin
(6 cases) et moins large (140 degres) qu'un survivant, et il reflechit trois fois par seconde quand
la perception d'Alice tourne a sept.

## Utility AI

Alice ne recoit plus une destination, elle recoit un **objectif**. Ce qu'elle en fait depend de ce
qu'elle sait : un ordre du joueur n'est qu'un score parmi d'autres.

### Un objectif est une donnee, pas une methode

Il n'existe ni `ComputeFleeScore()` ni classe `FleeDanger`. Le code connait un socle de
**considerations primitives** et quatre **courbes de reponse** ; les douze objectifs de la
specification sont douze entrees de [`config/utility_goals.json`](config/utility_goals.json) :

```json
{
  "name": "FleeDanger",
  "target": "away_from_danger",
  "considerations": [
    { "input": "threat_pressure",      "curve": "linear" },
    { "input": "health_ratio",         "curve": "inverse", "min": 0.7 },
    { "input": "panic",                "curve": "linear",  "min": 0.7 },
    { "input": "personality.caution",  "curve": "linear",  "min": 0.6 }
  ]
}
```

Les considerations sont multipliees : **une seule a 0 annule l'objectif**. C'est la plage de sortie
`min`/`max` qui decide de ce droit de veto. `threat_pressure` va de 0 a 1 : sans menace, pas de
fuite. `panic` va de 0,7 a 1 : la panique amplifie la fuite, elle ne la conditionne pas — un
survivant calme fuit quand meme un zombie qui lui saute dessus. C'est la lecture honnete de la
formule `flee = danger * sante * panique * prudence` de la specification, qui sinon rendrait toute
fuite impossible a froid.

| Consideration | Lue dans | Va de 0 a 1 quand |
|---|---|---|
| `perceived_danger` | zombies memorises + bruits entendus | rien en vue -> menace au contact |
| `threat_pressure` | le plus fort du danger percu et de la panique | tout va bien -> menace ou terreur |
| `personal_safety` | complement de `threat_pressure` | menace ou terreur -> tout va bien |
| `ally_danger` | menaces autour d'un allie croyable | allie tranquille -> allie assailli |
| `pursued` | cap memorise des menaces proches | personne ne la cherche -> on lui court apres |
| `health_ratio`, `panic` | etat du survivant | agonisant -> intact, calme -> panique |
| `has_medicine`, `has_ally`, `medicine_known` | inventaire et memoire | non -> oui |
| `unexplored_ratio` | carte mentale | terrain connu -> terrain vierge |
| `player_priority`, `plan_confidence` | ordre du joueur, plan courant | ignore -> imperatif |
| `mission_reached` | ordre du joueur et position | en route -> arrivee a destination |
| `heard_noise` | dernier bruit credible | silence -> vacarme |
| `personality.courage / loyalty / caution` | profil | — |
| `distance_to(cible)` | memoire | sur place -> a `distance_scale` cases |

`pursued` n'est pas un synonyme de `perceived_danger` : on peut etre cerne par des zombies qui ne
vous ont pas vu, ou poursuivi de loin par un seul. Il vaut 0 tant que les menaces connues sont
immobiles ou s'ecartent, et ne monte que pour une menace credible, proche, dont le cap pointe vers
le survivant. `Hide` s'en sert en `inverse` : se terrer marche contre ce qui ne vous a pas vu, pas
contre ce qui vous suit.

`distance_to(...)` et la cible d'un objectif partagent le meme vocabulaire : `ally`, `group`,
`medicine`, `pharmacy`, `extraction`, `start`, `cover`, `frontier`, `danger`, `away_from_danger`,
`mission`. Une cible inconnue vaut « infiniment loin », donc 1 : une consideration `inverse` la
ramene a 0 et l'objectif s'annule tout seul. Alice ne peut pas courir vers une pharmacie dont elle
ignore l'existence.

Une entree ou une cible mal orthographiee est refusee **au chargement**, avec la liste du
vocabulaire disponible — pas silencieusement traduite en objectif inerte.

### Les garde-fous contre l'oscillation

| Garde-fou | Reglage | Effet |
|---|---|---|
| Cooldown | `evaluation_interval` = 0,75 s | la decision est rejouee 1 a 2 fois par seconde, pas a chaque frame |
| Hysteresis | `switch_threshold` = 0,15 | un pretendant doit faire nettement mieux que le titulaire |
| Seuil minimum | `minimum_score` = 0,05 | en dessous, aucun objectif ne merite qu'on s'y mette |

L'hysteresis ne protege que ce qui tient encore debout : un objectif dont une **precondition** tombe
(les medicaments changent de mains) ou dont le score passe sous le seuil est lache immediatement,
sans marge. Sans cette exception, un survivant resterait accroche a un objectif devenu absurde.

C'est aussi le role de `mission_reached`, ajoute au livrable 5 : un ordre rempli cesse d'etre un
objectif. Sans cette precondition, un survivant arrive a destination continuerait a scorer haut
`CompletePlayerMission`, et le planificateur lui repondrait indefiniment « zero action » au lieu de le
laisser passer a ce que la consigne impliquait — fouiller la pharmacie, par exemple.

Un evenement important court-circuite le cooldown, jamais l'hysteresis : zombie repere, bruit fort
entendu, survivant blesse, itineraire bloque, nouvel ordre du joueur. La raison remonte telle quelle
dans le journal :

```
[Agent] Alice repere zombie-21.
[Utility] Alice : ExploreUnknownArea est retombe sous le seuil de 0.05 :
          FleeDanger prend la suite (0.52) — zombie-21 repere
[Utility]    FleeDanger  0.52  = threat_pressure 0.85->0.85 x health_ratio 1.00->0.70
                                 x panic 0.85->0.96 x personality.caution 0.80->0.92
```

### Sante et panique : l'inertie qui empeche l'oscillation

Un zombie au contact inflige desormais `zombies.attack_damage` points de degats — et se faire mordre
compte comme une observation directe : on sait ce qui vous touche, meme dans le dos, sinon un zombie
hors du champ de vision devorerait un survivant qui continue tranquillement d'explorer.

La panique suit le danger percu : elle monte vite (`panic_rise_per_second`), retombe lentement
(`panic_decay_per_second`) et fait un bond a chaque blessure. C'est elle qui donne son inertie a la
fuite. Sans elle, la perception partielle produit une oscillation caracteristique : Alice tourne les
talons, decouvre que la case ou elle croyait voir un zombie est vide, en conclut qu'il n'y a plus de
danger et repart tranquillement vers sa mission — pour se retourner une seconde plus tard. D'ou
`threat_pressure`, qui retient le plus fort du danger vu et de la panique residuelle : elle continue
de fuir quelques secondes apres avoir perdu le zombie de vue, puis se calme.

C'est la deuxieme ligne de defense contre cette oscillation, pas la premiere. La premiere est la
[memoire de trajectoire](#une-position-vieillit-une-trajectoire-aussi) : une menace perdue de vue ne
s'evanouit plus a l'endroit ou elle etait, elle continue d'avancer dans la carte mentale. La panique
est un scalaire — elle dit qu'Alice a peur, pas de quoi ni d'ou ; c'est la position supposee qui lui
permet de fuir *dans la bonne direction*.

Le danger percu s'accumule en probabilite plutot qu'en maximum : trois zombies autour de soi
inquietent plus qu'un seul. Un bruit compte pour moitie (`noise_danger_weight`) — entendre n'est pas
voir.

### Sante et motivation : deux jauges qui ne mesurent pas la meme duree

La fiche d'un agent porte deux barres, et elles repondent a deux questions differentes.

`HP` dit ce que le **corps** encaisse pendant une mission. `SANITY` dit ce que la **tete** encaisse
sur la duree — c'est la motivation, ce qui distingue un survivant frais d'un survivant qui revient de
trois missions.

La motivation suit la **panique**, pas le danger percu, et la nuance porte tout le sens de la jauge :
voir un zombie de loin sans s'affoler ne coute rien, c'est d'avoir eu peur qui coute.

```
sanity += (sanity_recovery_per_second * (1 - panique) - sanity_drain_per_second * panique) * dt
```

Elle se refait deux fois plus lentement qu'elle ne se vide (`3` contre `6`) : souffler ne rattrape
pas une poursuite, ce qui donne a la jauge une pente descendante sur la duree. Une blessure l'entame
en plus (`sanity_per_wound`), et ce **n'est pas** neutralise par `panic_enabled` — l'interrupteur
eteint la peur, pas ses causes. Rassurer un allie (`CalmAlly`, l'action de Claire) lui en rend :
c'est ce qui fait de Claire autre chose qu'une infirmiere, elle est la raison pour laquelle le groupe
ne rentre pas a mi-chemin.

A zero, `mission.abandon_at_zero_sanity` fait rentrer le survivant au camp de lui-meme. C'est ce qui
empeche la jauge d'etre decorative ; a `false`, elle redevient un simple indicateur, ce qui est
parfois ce qu'on veut pour observer autre chose.

Le maximum se redit par profil, comme la sante — Alice tient 180, Claire 200, Bob 240 — et la Utility
AI peut la lire par les entrees `sanity` et `weariness`.

### Ce que fait Alice de son objectif

Chaque objectif porte une cible, que `UtilityContext.Locate` traduit en cellule **a partir de la
seule memoire d'Alice**. `away_from_danger` choisit, parmi les cellules qu'elle a deja vues, la plus
sure au sens du danger cumule — pas seulement la plus eloignee du zombie le plus proche, sinon on
s'ecarte de l'un pour tomber sur trois autres — moins le prix du trajet. `frontier` prend la cellule
connue la plus proche voisine d'une cellule jamais observee.

Depuis le livrable 5, cette cible n'est plus la reponse finale mais le vocabulaire commun entre la
Utility AI et le planificateur : les memes noms — `pharmacy`, `cover`, `frontier`,
`away_from_danger` — deviennent les **lieux** du probleme symbolique. La Utility AI, elle, n'a pas
change : elle choisit un objectif, pas un chemin.

## Planification GBFS

Un objectif devient une suite d'actions. Le planificateur ne connait ni action ni objectif par son
nom : il lit un **domaine** et cherche.

### Un etat symbolique, ferme sur ce que l'agent sait

`SymbolicState` est un ensemble de faits vrais ; **tout ce qui n'y figure pas est repute faux**. Il
est construit par `SymbolicStateBuilder` a partir du seul `UtilityContext`, donc de la seule
`AgentKnowledge` : le planificateur n'a structurellement aucun moyen d'aller lire le `WorldState`.
Une porte jamais vue n'existe pas pour lui, et une pharmacie dont il ignore le verrou passe pour
ouverte — la desillusion est le sujet du livrable 6.

Ce qu'Alice sait au demarrage sur la carte livree, une fois briefee et apres un premier coup d'oeil :

```
at(agent, start)  known(start)  accessible(start)  known(pharmacy)  accessible(pharmacy)
known(extraction)  extraction_known  known(cover)  known(frontier)  at(agent, frontier)
door_closed(door_6_m2)  door_of(door_6_m2, pharmacy)
```

Les objets ne sont pas declares en configuration, ils sont derives de la memoire : un lieu par cible
resolvable, une porte ou une fenetre par cellule memorisee, un allie par croyance, une menace par
zombie memorise. Un lieu est `accessible` tant qu'une de ses ouvertures connues n'est pas verrouillee
— c'est cette precondition qui **oblige** le plan a intercaler une action adaptee.

`at(agent, pharmacy)` designe un **lieu**, pas une case : la pharmacie fait dix cases de long. C'est
pourquoi fouiller ou ramasser porte sur le local entier quand on est a l'interieur
(`survivor.search_radius` sinon) — la granularite du plan est la piece.

### Une action est une donnee, pas une classe

Il n'y a ni `MoveToAction` ni `OpenDoorAction`. Le code connait **six verbes primitifs** —
`toPos`, `take`, `put`, `setFlag`, `wait`, `damage` — et les vingt actions de la specification sont
vingt entrees de [`config/planner_domain.json`](config/planner_domain.json) :

```json
{
  "name": "ForceDoor",
  "verb": "setFlag",
  "parameters": { "door": "door", "location": "location" },
  "params": { "condition": "door_open(?door)", "value": true, "target": "?door" },
  "preconditions": [ "at(agent, ?location)", "door_of(?door, ?location)", "door_locked(?door)" ],
  "effects": [ "!door_locked(?door)", "door_open(?door)", "accessible(?location)" ],
  "cost": 3, "noise_level": 6, "duration": 3
}
```

Une valeur `?x` est une variable, declaree dans `parameters` avec son type d'objet ; tout autre jeton
est une constante. Le schema est ensuite **instancie** sur les objets que l'agent connait :
`ForceDoor(door_6_m2, pharmacy)`. `!` nie une precondition ou retire un fait, et `!at(agent, *)`
retire toute position memorisee avant que le deplacement n'en pose une nouvelle — les suppressions
sont appliquees avant les ajouts.

`OpenDoor`, `SearchRoom`, `HealSelf` et `ShareKnowledge` sont tous des `setFlag` avec un fait
different. `CreateNoiseDistraction` est un `wait` de 0,5 s avec `noise_level: 10` : toute action
executee emet son bruit, celle-la n'est faite que pour ca. `damage` n'est utilise par aucune action
livree — le combat est le livrable 8 — mais le verbe existe deja, comme la specification le demande,
pour qu'une action offensive n'ait pas a rouvrir le domaine.

Un verbe inconnu, un parametre manquant, une variable non declaree ou un type d'objet inexistant sont
refuses **au chargement**, avec le vocabulaire disponible.

### GBFS, et pourquoi ce n'est pas A*

```
priorite du noeud = heuristique(etat, but)
```

Uniquement l'heuristique : pas `g + h`. La recherche est donc rapide et **non optimale** ; le cout
accumule ne sert qu'a rendre compte du plan retenu, jamais a le choisir. Deux estimations egales sont
departagees par l'ordre de generation, si bien que deux planifications sur la meme memoire rendent le
meme plan — le journal et les tests sont reproductibles.

L'heuristique livree est la version enrichie de la specification, et ses poids sont dans
`planner` :

| Terme | Poids | Lit |
|---|---|---|
| conditions du but non satisfaites | `unsatisfied_weight` = 1 | l'etat |
| distance restante | `distance_weight` = 0,6 | les lieux cites par le but |
| danger | `danger_weight` = 1,5 | le danger memorise sur le lieu vise |
| bruit deja produit | `noise_weight` = 0,4 | le debut du plan |

Mettre les trois derniers a zero redonne exactement l'heuristique simple
(`number_of_unsatisfied_goal_conditions`), sans toucher au code. Un etat qui satisfait le but ne
coute rien, quel que soit le chemin : sinon la recherche pourrait preferer un noeud non final au
noeud final.

Trois garde-fous, tous configurables : `max_nodes` (1000), `max_depth` (20 actions),
`max_time_ms` (50). Chacun a sa propre cause d'echec dans le journal. La profondeur cesse de
developper une branche sans conclure a l'impossibilite — une branche plus courte reste peut-etre
ouverte.

**Tri de pertinence.** Avant de chercher, `ActionRelevance` ecarte les actions qui ne peuvent servir
au but : un parcours arriere depuis les faits reclames jusqu'au point fixe. Sans lui, GBFS
depenserait son budget de noeuds a envisager d'observer une rue ou de faire du bruit pendant qu'on
lui demande des medicaments — ces actions changent l'etat, donc creent des noeuds, mais aucune ne
rapproche du but. Une action ecartee n'aurait, par construction, pu apparaitre dans aucun plan
valide : le tri ne change aucun resultat, seulement le temps pour y arriver.

### Le plan produit, et son execution

Pour « recuperer les medicaments » depuis le depart, pharmacie fermee a clef :

```
MoveToLocation(pharmacy) -> ForceDoor(door_5_5, pharmacy) -> SearchRoom(pharmacy)
-> LocateMedicine -> PickUpMedicine
```

C'est exactement la suite donnee en exemple par la specification. Sur la carte livree, dont la porte
de pharmacie est simplement fermee, la meme demande rend le plan sans `ForceDoor` — et si Alice a deja
apercu les medicaments, elle va droit les prendre sans fouiller. Le plan retient l'empreinte de
l'etat qui l'a engendre (`created_from_state_hash`), de quoi reconnaitre d'un coup d'oeil qu'il a ete
concu dans un monde qui n'existe plus.

`PlanExecutor` deroule les actions une par une : verification des preconditions **au moment** de
commencer chaque action, declenchement du verbe, attente de sa duree, application des effets. Il n'y
a pas de branche par action, seulement six — une par verbe. `toPos` delegue le trajet a A* sur la
carte mentale ; `WorldPlanRuntime` realise le reste dans le monde et vit dans le coeur, sans
dependance au moteur, ce qui permet de derouler une mission complete en test.

Une action peut echouer, et c'est voulu : forcer une porte qui n'est plus verrouillee, fouiller une
pharmacie vide, ramasser un objet absent, un itineraire coupe en route. Chaque echec fait tomber
`plan_confidence` a 0, ce que la Utility AI lit a la passe suivante, et declenche une
replanification — voir plus bas.

### Quand aucun plan n'existe

La specification impose un repli : l'agent choisit un objectif d'exploration, cherche une zone
inconnue, collecte de l'information, relance la planification. Les deux premieres etapes sont dans
`Planner` (`exploration_fallback_goal`), les deux dernieres sont la boucle de jeu.

```
[Plan] Alice planifie (nouvel objectif) : FleeDanger sans solution (aucune suite d'actions ne mene
       au but) : repli sur ExploreUnknownArea — plan de 1 actions, cout 6, 2 noeuds explores en 0.4 ms
```

Fuir sans menace memorisee n'a pas de solution : `away_from_danger` n'est situable nulle part, donc
aucune action ne peut poser `at(agent, away_from_danger)`, et le tri de pertinence le constate
immediatement. Alice va voir ailleurs plutot que de rester plantee.

### Objectifs et buts symboliques

`planner_domain.json` porte aussi la traduction des douze objectifs du livrable 4 en conditions a
satisfaire. C'est le seul lien entre les deux fichiers, et c'est un nom :

```json
{ "name": "PickUpMedicine",  "conditions": [ "has(agent, medicine)" ] },
{ "name": "ReachExtraction", "conditions": [ "extracted" ] }
```

Un objectif absent du domaine est signale dans le journal et declenche le repli — jamais un plan
muet.

## Replanification

Un plan est une hypothese sur un monde qu'on ne connait qu'en partie. Le livrable 6 est ce qui se
passe quand l'hypothese tombe.

`Replanner` porte la boucle entiere, dans le coeur, et suit le flux de la specification :

```
Evenement detecte -> annulation de l'action actuelle -> mise a jour des connaissances
-> reevaluation Utility AI -> creation du nouvel etat symbolique -> recherche GBFS
-> execution du nouveau plan
```

`GameRoot` ne cable plus rien de tout cela : il percoit, passe la situation au cerveau, et affiche.
C'est ce qui permet aux tests de derouler exactement la meme boucle que le jeu.

### Les dix declencheurs, et qui les voit

`ReplanCause` a exactement dix valeurs — les dix evenements de la specification, ni plus ni moins,
et un test le verrouille. Ils arrivent par deux chemins :

| Declencheur | Detecte par |
|---|---|
| une precondition devient fausse | `PlanValidity`, en rejouant les actions restantes |
| une action echoue | `PlanExecutor` (`ActionFailureKind.Verb`) |
| une porte est verrouillee | `PlanValidity`, d'apres ce que l'etat dit de l'objet en cause |
| un obstacle est decouvert | `PlanExecutor` (`RouteBlocked`), via `PathFollower.Blocked` |
| un zombie apparait | `ReplanMonitor`, par difference sur les menaces crues |
| un allie est attaque | `ReplanMonitor`, par le danger pesant sur sa position supposee |
| la Utility AI change d'objectif | `Replanner`, en comparant l'objectif retenu au plan en cours |
| un objet attendu est absent | `PlanValidity`, ou l'echec du verbe `take` |
| le chemin A* est impossible | `PlanExecutor` (`NoRoute`) |
| le niveau de danger depasse un seuil | `ReplanMonitor`, au franchissement seulement |

Les echecs d'action sont **types** (`ActionFailure`) plutot que devines en relisant un message :
sans cela, « une action a echoue » et « un mur barre la rue » se ressembleraient dans le journal.

### Voir venir plutot que subir

`PlanExecutor` verifie les preconditions de l'action **au moment** de la commencer. `PlanValidity`
regarde plus loin : il rejoue toutes les actions restantes sur l'etat courant. Un survivant qui
apercoit de la rue que la porte de la pharmacie est verrouillee n'a aucune raison de finir son trajet
avant de s'en apercevoir.

Pour cela il faut un etat qui melange deux choses de nature differente : ce que le plan a deja
accompli, et ce que le monde vient de dire. `SymbolicState.Reconcile` les separe par predicat —
`SymbolicStateBuilder.ObservablePredicates` liste ce qu'un regard peut confirmer ou dementir
(`door_locked`, `accessible`, `at`, `has`, …). Le reste — `searched`, `explored`, `hidden` — est de
l'avancement : personne ne peut voir qu'une piece a deja ete fouillee, donc l'observation n'a rien a
en dire.

### Replanifier n'est pas tout recommencer

Un plan neuf repart d'un etat symbolique neuf, et c'est un probleme : la piece qu'Alice venait de
fouiller redevient a fouiller. `SymbolicStateBuilder.Carry` reconduit donc les acquis du plan
abandonne — avec deux exclusions, et les deux comptent.

Ce que l'observation sait produire est ecarte : le monde a le dernier mot sur les portes et les
objets, et reconduire une croyance perimee serait exactement l'erreur qu'on cherche a corriger. Ce
dont un objectif fait sa **reussite** est ecarte aussi (`PlanningDomain.GoalPredicates`) : reconduire
`explored` rendrait « explorer » satisfait des la premiere sortie, et le survivant ne bougerait plus.

Reste l'acquis durable — `searched(pharmacy)`, `window_broken`, `shared_with` — qui survit. La regle
est entierement derivee de `planner_domain.json` : ajouter un objectif y ajoute automatiquement son
marqueur.

### Un lieu ferme est sa porte

`ForceDoor` demande d'etre « a la pharmacie », mais la memoire retient la pharmacie par une case de
son interieur — que A* ne peut pas atteindre, puisque la porte est justement verrouillee. Le plan
etait correct et inexecutable : Alice replanifiait indefiniment le bon plan sans jamais pouvoir en
jouer la premiere action.

`SymbolicStateBuilder` ramene donc tout lieu devenu impenetrable a son **pas de porte** : la case
franchissable, voisine d'une de ses ouvertures, la plus proche du survivant — on force une porte
depuis la rue, pas depuis la piece. Une fois l'entree ouverte, le lieu redevient son interieur, et la
replanification qui suit fait entrer le survivant. Sur la carte livree, porte verrouillee au demarrage,
Alice force la porte et ressort avec les medicaments.

### Ne pas replanifier a chaque frame

La specification l'interdit, et un declencheur qui persiste — un zombie immobile en vue — relancerait
GBFS jusqu'a la fin de la partie. Trois garde-fous, tous configurables (`replanning` dans
`config/game_config.json`) :

* `check_interval` — on ne verifie la validite du plan que 2 fois par seconde ;
* `minimum_interval` — un delai apres chaque replanification ; un evenement deja **constate** par le
  moteur passe devant, parce qu'il n'est pas une hypothese a verifier ;
* des fronts sur les deductions — un zombie ne compte qu'a la premiere rencontre, un danger qu'au
  franchissement du seuil, avec `danger_hysteresis` pour qu'une pression qui oscille ne declenche pas
  en boucle.

Un test verrouille l'autre bout : une mission sans imprevu ne doit produire ni replanification de
danger, ni replanification de zombie.

### Le journal des decisions de l'IA

Chaque replanification produit le bloc impose par la specification, aux noms de champs traduits pres :

```
[00:00:31] Alice replanifie
Raison : SearchRoom(pharmacy) : « accessible(pharmacy) » n'est plus vrai (une porte est verrouillee)
Objectif precedent : SearchForMedicine
Nouvel objectif : SearchForMedicine
Nouveau plan : SearchForMedicine : MoveToLocation(pharmacy) -> ForceDoor(door_6_2, pharmacy)
               -> SearchRoom(pharmacy) -> LocateMedicine (4 actions, cout 8.5, bruit 8, 10 s)
```

La cause n'est pas devinee au nom de la precondition : c'est `accessible(pharmacy)` qui devient faux,
et c'est bien de verrouillage qu'il faut parler. `PlanValidity` remonte de l'argument en defaut vers
ce que l'etat affirme des objets qui s'y rattachent — `door_of(door_6_2, pharmacy)` et
`door_locked(door_6_2)`. La lecture ne suppose aucun nom de predicat, seulement la forme du fait.

`DecisionJournal` est une memoire bornee (`journal_capacity`) qui vit dans le coeur ; la console
Godot et le HUD n'en sont que des miroirs. Il conserve les replanifications sous forme structuree,
pour que le panneau debug puisse les compter par cause sans relire des chaines.

### Ce qu'on observe

Porte de la pharmacie verrouillee en cours de partie, alors qu'Alice avait deja bati son plan
dessus : elle voit la porte changer, constate que fouiller la pharmacie n'est plus possible, et
repart sur le plan que la specification donne en exemple — `MoveToLocation` puis `ForceDoor` puis
`SearchRoom`. C'est le critere de validation du livrable, joue dans `ReplanningTests`.

## Trois survivants et partage d'informations

Le livrable 7 tient dans une phrase : **la coordination est une consequence de la communication,
jamais un privilege de l'appartenance au groupe.** Deux survivants dans la meme equipe mais a vingt
cases l'un de l'autre en savent exactement autant que deux inconnus.

### Les trois profils

Alice, Bob et Claire ne sont pas trois classes du code — aucun fichier de `Hordinary.Core` ne les
nomme. Ce sont trois entrees de [`config/survivors.json`](config/survivors.json). Ajouter un
quatrieme survivant, ou echanger deux roles le temps d'un essai, ne demande pas de recompiler.

| | Alice — eclaireuse | Bob — bricoleur | Claire — secouriste |
|---|---|---|---|
| courage / loyaute / prudence | 0,5 / 0,7 / 0,8 | 0,8 / 0,5 / 0,4 | 0,4 / 0,9 / 0,7 |
| sante | 80 | 120 | 100 |
| vitesse (marche / course) | 5 / 8,5 | 4 / 7 | 4 / 6,5 |
| vision | 11 cases, 200° | 8 cases, 180° | 8 cases, 180° |
| discretion des pas | x0,6 | x1 | x0,9 |
| soin rendu | 45 | 25 | 90 |

Un profil ne redit que ce qui le distingue : toute valeur absente retombe sur la section `survivor`
de `game_config.json`.

### Les competences, ou comment on evite les `if (survivor is Bob)`

Une competence est une ponderation des trois grandeurs qu'une action du planificateur porte deja —
son cout, son bruit, sa duree — plus le droit de l'interdire. `PlanningDomain.Specialize` en derive
une copie du domaine par survivant ; le domaine commun n'est jamais modifie.

Cela suffit a produire des comportements distincts, **sans aucune branche par personnage** : GBFS
minimise le cout, et ce n'est pas le meme cout pour tout le monde. `ForceDoor` vaut 3 dans le domaine
commun, 1,5 pour Bob et 6 pour Alice — devant une porte verrouillee, Bob propose spontanement de la
forcer la ou Alice cherche la fenetre ou le detour. Claire, elle, a `BreakWindow` a `"enabled": false` :
l'action sort simplement de son repertoire.

Cote Utility AI, chaque profil declare des **aptitudes** (`exploration`, `support`, `breaching`,
`stealth`), lues par la consideration parametrable `aptitude(nom)`. Le vocabulaire est libre : il
n'appartient qu'a `survivors.json` et `utility_goals.json`, qui doivent s'accorder. Une aptitude
jamais declaree vaut 0,5, ni douee ni maladroite.

### Frontier exploration

Une frontiere est une cellule connue et praticable voisine d'une cellule jamais observee — le bord de
ce que le survivant sait. `FrontierExplorer` les note avec la formule de la specification :

```
score = gain d'information - cout de distance - danger percu - duplication entre allies
```

Les quatre termes sont conserves separement (`Frontier`), et pas seulement leur somme : c'est ce qui
permet a `F7` d'expliquer pourquoi Alice part au nord — « le gain etait le meme, mais Bob s'occupait
deja du sud ». Les poids sont dans la section `exploration` de `game_config.json` ; mettre
`duplication_weight` a zero ramene exactement au comportement d'avant ce livrable, ou les trois
survivants visaient la meme frontiere.

Le quatrieme terme est celui qui fait l'equipe, et **il ne peut se lire que dans la memoire du
survivant**. Chaque agent annonce ou il va (`AgentKnowledge.DeclareIntent`) ; cette annonce ne
devient une penalite chez les autres qu'apres avoir circule par la parole. Deux survivants hors de
portee de communication se marchent donc bel et bien dessus — et c'est le comportement voulu.

### Le partage

`KnowledgeSharing` appareille les survivants a moins de `communication_range` cellules (5 par defaut)
et les fait converser une fois par seconde. `radio_enabled: true` supprime la contrainte de distance,
comme la specification le propose en option.

La classe ne prend **aucun** `WorldState`, et un test le verifie par reflexion : un survivant ne peut
structurellement pas apprendre par la conversation ce que son interlocuteur ignorait. La seule chose
que le partage lit dans le monde physique est la distance entre deux corps, et c'est bien ce qu'il
doit lire — la portee de la voix n'est pas une croyance.

Se parler transmet aussi deux informations qui ne s'observent pas : la **sante** et la **panique** de
l'interlocuteur. C'est ce qui donne enfin a `HealAlly` une precondition verifiable (`ally_wounded`),
et ce qui fait qu'un survivant qui n'a parle a personne ne part jamais soigner un blesse qu'il aurait
devine.

### `merge_belief`

La fusion suit les trois lignes de la specification, dans cet ordre :

```csharp
if (current is null) return incoming;                                    // on ne savait rien
if (incoming.Source == KnowledgeSource.DirectObservation) return incoming; // vu de ses yeux
if (incoming.LastObservedAt > current.LastObservedAt) return incoming;     // plus recent
return current;
```

La troisieme ligne compte plus qu'il n'y parait. **A date egale, on ne change pas d'avis** : sans
cela, deux allies en conversation se renverraient indefiniment la meme information, chacun la
degradant d'un cran a chaque passage — un test verifie qu'apres cinq allers-retours la confiance n'a
baisse que d'une seule decote.

Circulent : les cellules, les entites (position, cap, sante et panique rapportees), les objets, les
lieux, les fouilles infructueuses (« j'ai deja cherche la ») et les intentions. Ne circulent pas :
les souvenirs sonores, dont l'erreur de localisation appartient a celui qui a entendu.

### Ce qu'on observe

Trois survivants partis de la meme case, avec la meme carte mentale vide, se separent au lieu de
partir ensemble ; ils se recroisent, se racontent le quartier, et une piece exploree par une seule
d'entre elles apparait dans la carte mentale des autres — en gris de temoignage, pas en clair
d'observation. Le critere de validation du livrable — « une information decouverte par Alice peut
etre utilisee ensuite par Bob apres communication » — est joue dans `BandTests` : Bob, qui n'a jamais
mis les pieds dans la pharmacie, sait ou sont les medicaments apres avoir parle a Alice, et le
planificateur lui construit un plan pour aller les chercher.

## Interface et boucle complete

Le livrable 8 ferme la boucle : le joueur commande, la partie se gagne ou se perd, et le mode debug
montre pourquoi.

### Commandes

Une entree clavier ne pose plus directement un objectif : elle fabrique un `PlayerCommand` que
`CommandManager` traduit. La distinction n'est pas cosmetique — elle separe deux choses qui n'ont pas
le meme statut :

* un **ordre** (`FetchMedicine`, `ReachExtraction`, `ExploreArea`, `Regroup`, `MoveTo`) pose un
  `MissionOrder` que la Utility AI met en concurrence avec ses propres objectifs. Il peut etre mis de
  cote au profit d'un danger immediat, et il peut aussi **echouer a etre donne** : on n'envoie
  personne a un endroit dont il ignore l'existence. Chaque ordre est resolu dans la memoire de celui
  qui le recoit, jamais sur la carte reelle ;
* une **consigne permanente** (`ForbidNoise`, `AllowNoise`, `ForbidZone`, `AllowZone`) ne se negocie
  pas. Elle ne penalise pas un score : elle ampute le repertoire. Interdire le bruit retire les
  actions au-dela de 2 du domaine **avant** la recherche, et GBFS trouve un autre plan — ou n'en
  trouve aucun, ce que le panneau de planification affiche au lieu de le masquer. Une zone interdite
  devient infranchissable pour A*, pas seulement couteuse : c'est le seul element du jeu qui ne se
  discute pas.

Les consignes sont **individuelles**. Interdire une zone a Alice ne l'interdit pas a Bob, exactement
comme sa carte mentale n'est pas la sienne.

### Mission remplie, partie finie : deux moments differents

`MissionTracker` observe le monde et ne modifie rien. Il distingue **deux** moments que le prototype
confondait, et cette distinction est ce qui fait exister le rappel :

1. **La mission est remplie** — la caisse est revenue au camp avec quelqu'un. C'est un evenement
   (`Accomplished`), pas une fin : le jeu continue, et c'est le rappel qui demarre.
2. **La partie est finie** — il ne reste plus personne sur le terrain. Soit tout le monde est rentre
   et la caisse est au camp, et c'est une **victoire** ; soit le groupe est rentre les mains vides,
   ou plus personne n'est en etat d'agir, et c'est une **defaite**.

La defaite se produit aussi quand il n'y a plus aucun medicament nulle part, tant que la mission
n'est pas remplie. Cette clause n'est pas decorative — `HealSelf` et `HealAlly` consomment la caisse
que la mission demandait de rapporter, et soigner un blesse avec le dernier stock perd donc la partie.

`OutcomeTests` joue une partie entiere sur la carte livree, du depart a la victoire, sans jamais
placer personne a la main : le joueur demande les medicaments, la Utility AI bascule seule sur
`ReachExtraction` une fois la caisse en poche, et la partie ne se conclut qu'au dernier retour.

### Le rappel au camp

La caisse est rentree, mais les autres ne le savent pas : ils sont a l'autre bout de la carte,
occupes a fouiller un entrepot pour rien. Quelqu'un doit aller le leur dire. C'est cela, `MissionRecall` :
**l'agent qui a fini la mission repart chercher les autres**, un par un, pour leur annoncer

> Mission terminee, rentrez au camp s'il vous plait.

Trois choses en decoulent, et les trois tiennent au modele de connaissance du livrable 7 :

* Le message se transmet **de vive voix**, a portee de communication — le meme canal que le partage
  de connaissances. Il n'existe pas d'annonce globale : un allie qu'on n'a pas rejoint continue sa
  mission, et c'est correct, il n'a aucun moyen de savoir.
* Le messager cherche ses allies **la ou il croit qu'ils sont**. Une position vieille de vingt
  secondes l'envoie a un endroit qu'ils ont quitte, et il recommence. Le journal le montre bien :
  `Claire part prevenir Alice vers (1,-7)` puis `vers (3,11)` puis `vers (5,0)`.
* Rentrer **n'est pas un pilotage** : le rappel pose un `MissionOrder` que la Utility AI met en
  concurrence avec le reste, avec le poids `mission.recall_priority`. Un survivant poursuivi par une
  horde ne rentre pas tout droit — il fuit d'abord, et rentre apres.

Un survivant qui atteint le camp **disparait** du terrain : il quitte `WorldState`, cesse de percevoir
et de decider, et sa fiche s'eteint en annoncant « rentre au camp ». Il n'est pas mort — c'est le
meme qui repartira, avec la sante et la motivation qu'il rapporte. Le sortir du monde n'est pas
cosmetique : sans cela, la horde continuerait de le poursuivre jusque dans le camp, et les allies
restes dehors le verraient encore la ou il n'est plus.

Deux autres facons de rentrer, qui passent par le meme chemin : le joueur peut rappeler un agent
(`3`), et un survivant a bout de motivation rentre de lui-meme.

### Preparation de mission

Une partie commence par un choix, en trois temps affiches en meme temps — c'est un briefing, pas un
assistant en trois pages : on choisit ses agents en regardant la mission, et on change d'avis en
lisant la difficulte.

1. **Les agents**, dans une grille horizontale de fiches en bas d'ecran. Chaque fiche porte le nom, le
   role, et les deux jauges : `HP 80/80` et `SANITY 180/180`. Un clic enrole ou retire.
2. **La mission**, choisie dans [`config/missions.json`](config/missions.json) : nom, difficulte,
   equipe conseillee, et le briefing en clair.
3. **Demarrer** — et rien n'existait avant : ni les survivants, ni la horde, ni les caisses.

La grille d'agents est en bas parce que c'est la qu'elle **reste** : la meme barre, aux memes
coordonnees, devient la fiche d'equipe pendant la mission. Le joueur n'a pas a chercher ou sont
passees les jauges qu'il vient de lire — elles n'ont pas bouge, elles se sont mises a vivre. C'est
aussi la seule interface qui survit a l'extinction du mode debug : les trois panneaux de texte disent
ce que la simulation pense, cette barre dit ce qu'un joueur a besoin de savoir.

Une mission est une **donnee**, comme un survivant ou une action du domaine. Aucun endroit de
`Hordinary.Core` ne nomme « Ravitaillement pharmacie » :

```json
{
  "id": "hangar",
  "name": "Traversee du hangar sud",
  "difficulty": "Difficile",
  "objective": "Building",
  "objective_zone": "Hangar sud",
  "camp": "Start",
  "supplies": 1,
  "zombie_count": 20,
  "recommended_team": 3
}
```

`objective_zone` tranche quand plusieurs zones partagent le meme type — la carte livree compte deux
batiments — et retombe sur le type si le nom ne correspond a rien. `zombie_count` est ce qui fait
reellement la difficulte ; `difficulty` ne fait que l'annoncer, et `MissionCatalogueTests` verifie
que les deux restent d'accord.

`debug.skip_setup` saute cet ecran et demarre la premiere mission avec toute l'equipe : de quoi
relancer une partie en boucle en observant une seule chose, sans recliquer trois fois.

### Le camp : partir et revenir au meme endroit

Une mission commence et finit au meme endroit. Dans
[`config/map_locations.json`](config/map_locations.json), la zone d'extraction est donc posee **sur**
la zone de depart, aux memes coordonnees :

```json
{ "name": "Camp",                     "kind": "Start",      "min": [-1, -8], "max": [3, -7] },
{ "name": "Camp (retour de mission)", "kind": "Extraction", "min": [-1, -8], "max": [3, -7] }
```

Les deux entrees restent distinctes parce que les deux vocabulaires du jeu existent deja : le domaine
de planification connait la sortie sous le nom `extraction` (`ReachExtraction`, le fait `extracted`)
et le briefing le depart sous le nom `start`. Les fondre en une seule ligne obligerait a renommer
l'un des deux, pour ne rien gagner. C'est la carte qui dit qu'ils designent le meme endroit, pas le
code — et `DemoMapTests` le verifie.

### Combat simple

« Repousser un zombie » et « effectuer une attaque simple » sont deux entrees de
[`config/planner_domain.json`](config/planner_domain.json), pas deux classes :

| Action | Verbe | Cout | Bruit | Effet |
|---|---|---|---|---|
| `PushBackZombie` | `setFlag` | 1 | 2 | recul de 3 cases, aucune blessure, le zombie perd sa piste |
| `AttackZombie` | `damage` | 3 | 6 | 15 degats sur les 30 points de vie d'un zombie |

Un seul objectif, `FightZombie`, couvre les deux : c'est **GBFS** qui arbitre entre la poussee, moins
chere et plus discrete, et l'attaque, qui regle le probleme mais s'entend de loin.

Il n'existe volontairement **aucune action pour rejoindre un zombie**. La precondition
`zombie_adjacent(?threat)` n'est produite par aucune action du domaine, seulement par la perception :
un survivant ne peut donc pas planifier une chasse, seulement repondre a ce qui est deja sur lui.
`CombatTests` verifie cette absence explicitement — c'est le genre de propriete qu'une action ajoutee
par distraction ferait disparaitre en silence.

### La barre d'equipe et les trois panneaux

L'ecran porte deux interfaces qui ne s'adressent pas au meme public.

La **barre d'equipe** en bas est celle du **joueur** : les memes fiches que l'ecran de preparation,
aux memes coordonnees, mais vivantes. Elle montre qui est encore dehors, dans quel etat (`HP`,
`SANITY`, objectif courant), et sur qui portent les ordres. Un agent rentre au camp n'en disparait
pas : sa fiche s'eteint et annonce `rentre au camp` — le faire disparaitre laisserait un trou, et le
joueur se demanderait qui manque. Elle reste allumee quand le mode debug est eteint.

Les **trois panneaux de texte** montrent ce que la **simulation** pense, et disparaissent avec le
mode debug (`debug.hud_text`). Leur partage suit celui de la specification, et il repose sur une
distinction que le livrable 2 rend obligatoire : **ce que le joueur sait n'est pas ce qu'un survivant
sait**.

* **HUD** (haut gauche) : l'etat de la partie — la mission, l'heure, le rappel, le groupe, la horde
  entiere, les ordres donnes, le verdict. C'est la vue du joueur.
* **AgentPanel** (haut droite) : l'etat d'**un** survivant, dans le format impose par la
  specification — nom, objectif, action actuelle, plan, derniere replanification — augmente de la
  sante, de la panique, du prochain point de deplacement, des scores et de ce qu'il connait. Tout y
  est lu dans sa memoire : changer de point de vue change les chiffres.
* **PlanPanel** (bas droite) : l'etat de la recherche — noeuds GBFS explores et generes, temps passe,
  limites atteintes, plans rejetes, causes de replanification, journal des decisions.

Melanger le premier et le deuxieme ferait mentir tout le livrable 2 : le joueur voit toute la horde,
Alice ne voit que les zombies qu'elle a croises.

### Mode debug

Aux overlays des livrables precedents s'ajoutent les **frontieres d'exploration** (la retenue en
plein, les candidates en contour) et les **zones interdites** (hachurees en croix, pour qu'on ne les
confonde pas avec une zone dangereuse : le danger est un cout, l'interdiction est un mur).

Le reste du mode debug se regle dans la section `debug` de
[`config/game_config.json`](config/game_config.json) — et **nulle part ailleurs** : aucune touche ne
le bascule en cours de route. Une partie se deroule donc entierement dans le regime que le fichier a
fixe, et deux parties lancees sur la meme configuration restent comparables.

```json
"debug": {
  "enabled": true,
  "console_output": true,
  "commands": true,
  "hud_text": true,
  "skip_setup": false,

  "panic_enabled": false,
  "show_navigation": false,
  "show_mental_map": false,
  "show_noise": false
}
```

`enabled` est l'**interrupteur general** : a `false`, tout le reste est neutralise — la console reste
muette, les touches d'observation ne repondent plus, les panneaux de texte disparaissent et les
overlays restent eteints. Reste a l'ecran ce qu'un joueur doit voir : la carte, les agents, leurs
jauges. Ce n'est pas un raccourci d'ecriture mais la difference entre deux publics : on livre le jeu
avec `enabled: false` et on le developpe avec `true`, sans avoir a se souvenir de la liste des
reglages a eteindre un par un. Les valeurs brutes sont conservees telles quelles, si bien que
rallumer l'interrupteur rend exactement le regime qu'on avait ecrit.

| Reglage | Ce qu'il coupe |
|---|---|
| `console_output` | le journal : plans, replanifications, bruits, ordres, partages, rappel |
| `commands` | `F1` a `F7`, `C`, `Espace`, clic droit — les touches de l'observateur, pas celles du joueur |
| `hud_text` | les trois panneaux de texte ; il reste la carte et la barre d'equipe |
| `skip_setup` | rien : il **saute** l'ecran de preparation et demarre la premiere mission |

Une erreur de configuration, elle, ne se coupe jamais : un fichier introuvable ou invalide est un
probleme que le joueur doit voir, meme sur une partie livree avec le mode debug eteint. C'est la
seule chose que `Trace` laisse passer.

Un seul endroit du programme applique l'interrupteur general — les proprietes derivees
`ShowsConsole`, `AcceptsCommands`, `ShowsText` et `ShowsOverlay` de `DebugSettings`. C'est donc le
seul endroit ou l'on puisse l'oublier, et `DebugModeTests` le verifie.

`panic_enabled` a `false` **neutralise la panique et le stress**. Ce n'est pas un filtre
d'affichage : la panique est mise a zero a la source et cesse d'entrer dans `threat_pressure`. La
difference compte — une panique cachee mais toujours calculee continuerait a faire fuir les
survivants, et le mode debug servirait a masquer le probleme au lieu de l'isoler. Les degats, eux,
restent bien reels : c'est la peur qu'on retire, pas les zombies. Le panneau d'agent affiche alors
`panique neutralisee (debug)` plutot qu'un `0 %` qu'on prendrait pour du sang-froid.

Les trois `show_*` fixent l'etat de depart des overlays `F1`, `F2` et `F3`, qui restent eux
basculables en jeu : allumer un calque ne change rien a ce que les agents decident, contrairement a
la panique.

## Carte de demonstration

Grille 26 x 29 (origine `(-10,-8)`), peinte a la main dans l'editeur Godot.

* trois batiments : entrepot ouest, pharmacie (est), hangar sud ;
* une rue principale, une ruelle centrale ;
* 105 murs, 3 portes fermees, 8 fenetres ;
* les medicaments en `(11,4)`, dans la pharmacie.

La geometrie vient des tuiles ; la semantique des lieux (depart, pharmacie, extraction, rue,
ruelle) est declaree dans [`config/map_locations.json`](config/map_locations.json) en coordonnees
de tuiles — les memes que celles affichees par l'editeur. Modifier une zone ne demande donc ni
nouvelle tuile ni recompilation.

La correspondance entre les tuiles et les types de cellules est dans
[`config/tile_legend.json`](config/tile_legend.json). Les cles ont la forme `source:x,y` : le
TileSet compte trois sources — `0` la planche `Assets/atlas.png`, `1` la porte fermee, `2` la porte
ouverte, ces deux dernieres partageant la coordonnee d'atlas `0,0`.

## Portes

Le TileSet ne connait que deux apparences : **fermee** (source 1) et **ouverte** (source 2).

Le **verrouillage n'est pas une donnee de la carte** mais un etat d'execution : le jeu tirera au
sort les portes verrouillees au demarrage, et le survivant devra crocheter ou passer par une
fenetre. `CellType.DoorLocked` existe donc dans le modele (il bloque le deplacement et la vue) mais
aucune tuile ne le peint — la carte livree contient trois portes fermees et zero porte verrouillee.

Pour le MVP, **la porte de la pharmacie en `(6,-2)` est simplement fermee** : franchissable avec un
surcout de 3, ce qui garde la pharmacie atteignable et le critere du livrable 1 valide.

Depuis le livrable 5, ouvrir une porte est une action du plan : `OpenDoor` et `ForceDoor` echangent
reellement la tuile et mettent a jour la carte mentale, avec le bruit du bareme (2 et 6).
`BreakWindow` transforme la fenetre en sol — une vitre brisee laisse un passage — pour 8 de bruit.

Depuis le livrable 6, decouvrir qu'une porte est verrouillee ne demande plus d'attendre de s'y
cogner : le plan est confronte a la carte mentale deux fois par seconde, et une pharmacie devenue
inaccessible fait tomber le plan qui comptait la fouiller. Le jour ou le tirage aleatoire verrouillera
une porte au demarrage, tout est en place — a la reserve pres notee dans les limites connues.

## Limites connues

* La pharmacie n'a qu'une seule entree, la porte en `(6,-2)`. `BreakWindow` existe, mais aucune
  fenetre n'est rattachee a la pharmacie sur la carte livree : porte verrouillee, `ForceDoor` est la
  seule reponse. Cela fonctionne — Alice force la porte et ressort avec les medicaments, c'est teste —
  mais le planificateur n'a aucun choix a faire, donc rien a arbitrer entre discretion et rapidite.
* Un ordre du joueur se resout en un simple deplacement, qu'aucune porte ne rend *invalide* — juste
  impossible. La decouverte d'un verrouillage se presente alors comme « obstacle decouvert » puis
  « chemin A* impossible », et non « porte verrouillee » : la cause nommee depend de ce que l'objectif
  courant exigeait, pas seulement du monde. Les deux chemins sont testes.
* La horde livree compte **cinq zombies**, la ou la specification en demande 20 a 30. C'est un ecart
  assume : a vingt-quatre zombies sur cette grille, les trois survivants passent la partie en fuite
  et l'on n'observe plus jamais un plan aller a son terme — or c'est precisement ce que ce prototype
  cherche a montrer. Le nombre reste une valeur de `config/game_config.json` (`zombies.count`) :
  le remonter a 24 rend la densite de la specification sans recompilation, et seul le test
  `La_horde_livree_compte_cinq_zombies` est a ajuster.
* Le combat est volontairement minimal : un zombie a 30 points de vie, une attaque en retire 15, et
  rien ne distingue un survivant d'un autre a ce jeu-la. Le prototype demontre une architecture de
  decision, pas un systeme de duel — et donner des competences offensives differenciees se ferait
  dans `config/survivors.json`, sans code.
* Repousser un zombie ne lui apprend rien : il perd sa piste, mais rien ne l'empeche de revenir dans
  la seconde s'il retrouve le survivant du regard. C'est voulu — la poussee achete du temps, pas la
  paix — mais cela rend l'action moins utile qu'elle en a l'air quand la fuite est deja coupee.
* « Un allie est attaque » se deduit du danger pesant sur sa position supposee, et non de sa sante.
  Celle-ci est desormais une croyance (livrable 7), mais elle n'arrive que par la parole : un allie
  hors de portee peut se faire devorer sans que personne ne le sache autrement qu'en voyant des
  zombies converger vers l'endroit ou on le croit.
* Le seuil qui declenche le coup d'oeil en arriere lit `pursued`, qui ne compte que les croyances
  d'entites : entendre quelqu'un approcher sans jamais l'avoir vu ne fait donc pas se retourner.
* La carte de chaleur se propage sur les cases praticables, ce qui la fait bien s'arreter aux murs,
  mais elle ignore les portes fermees — une piece close se marque comme un couloir ouvert.
* Un lieu ferme est ramene a son pas de porte, choisi comme la case franchissable la plus proche du
  survivant. C'est une heuristique geometrique : un batiment dont l'entree donne sur une cour
  interieure pourrait renvoyer le mauvais cote.
* Les trois survivants partagent la meme case de depart et le meme briefing. Les faire arriver par
  des cotes differents de la carte rendrait la repartition de l'exploration plus lisible, mais
  demanderait plusieurs zones de depart dans `map_locations.json`.
* `CalmAlly` est un treizieme objectif, au-dela des douze de la specification : « reduire la panique »
  est une action speciale de Claire (livrable 7) et n'avait pas d'objectif pour la declencher. Le
  catalogue etant une donnee, le test verifie que les douze sont toujours la, pas qu'il n'y en a que
  douze.
* L'action `ShareKnowledge` existe et fonctionne, mais aucun objectif Utility AI ne la declenche :
  le partage passif a portee de voix suffit au critere du livrable, et un objectif « aller raconter »
  ferait passer au groupe plus de temps a discuter qu'a survivre. Le but symbolique est declare dans
  `planner_domain.json` pour qui voudrait l'activer.
* Une intention annoncee est un point, pas une zone : « je vais en (12,3) » ne dit rien de
  l'itineraire. Deux survivants peuvent donc encore se suivre sur la moitie du trajet avant de
  diverger.
* Un survivant sait ramasser les medicaments et s'en servir, mais sa mort est toujours seulement
  journalisee, sans ecran de defaite — livrable 8. Un groupe entier hors de combat non plus.
* **Elle ne se bat jamais**, meme contre un zombie isole et meme calme. Il manque un objectif de
  combat et une entree decrivant « peu d'adversaires » (`perceived_danger` fond tout en un scalaire) ;
  `Zombie` n'a toujours ni points de vie ni moyen d'en perdre. La couche d'action, elle, ne bloque
  plus : le verbe `damage(cible, quantite)` fait partie du socle et les menaces memorisees sont deja
  des objets adressables du probleme — une action offensive sera une entree de
  `planner_domain.json`. C'est le livrable 8 ; le detail est note dans
  [`Specs/05-planification-gbfs.md`](Specs/05-planification-gbfs.md).
* GBFS n'est pas optimal, et c'est la specification qui le veut (`priorite = h`). Un plan un peu plus
  long ou un peu plus bruyant que le meilleur possible est donc normal ; les couts et le bruit
  n'orientent la recherche qu'a travers l'heuristique.
* Ramasser et fouiller portent sur le local entier des qu'on est a l'interieur : Alice peut prendre
  les medicaments a l'autre bout de la pharmacie sans traverser la piece. C'est la contrepartie
  assumee d'un plan qui raisonne sur des lieux et non sur des cases.
* Une porte est rattachee au lieu nomme le plus proche dans un rayon de `door_link_radius` : c'est
  une heuristique geometrique, pas une topologie. Un batiment aux entrees eloignees de son centre
  demanderait de declarer les ouvertures dans `map_locations.json`.
* Le gain d'information d'une frontiere est estime sur un carre autour d'elle, sans ligne de vue :
  une frontiere donnant sur un mur juste derriere parait aussi prometteuse qu'une rue ouverte.
  L'agent ne peut pas savoir mieux — il n'a justement pas regarde — mais la deception se paie en un
  trajet. Seules les `max_candidates` frontieres les plus proches sont notees, ce qui biaise
  discretement vers l'atteignable.
* La trajectoire supposee est une droite : un zombie qui tourne au coin d'une rue est cru tout droit
  pendant `prediction_horizon`. Un mur deja vu arrete la supposition, un virage non. Suivre le
  couloir plutot que la ligne demanderait de propager la croyance sur la carte mentale.
* `pursued` ne lit que les croyances d'entites : un bruit n'a pas de cap, donc entendre quelqu'un
  approcher sans le voir ne compte jamais comme une poursuite — ni ne declenche de coup d'oeil.
* Le danger **percu** ne depend que de la distance : un zombie derriere un mur inquiete autant qu'un
  zombie dans la rue. Le danger **memorise**, lui, contourne les murs — la carte de chaleur se
  propage de proche en proche. Les deux devraient s'accorder ; c'est `UtilityContext.DangerAt` qu'il
  resterait a ponderer par la ligne de vue.
* `CreateDistraction` a desormais son action (`CreateNoiseDistraction`, un `wait` a 10 de bruit), mais
  elle fait du bruit sur place : attirer la horde *ailleurs* demanderait de choisir ou le faire, ce que
  le domaine ne sait pas encore exprimer. La distraction manuelle (`Espace`) reste le seul moyen de
  viser une case.
* Les souvenirs sonores ne sont toujours pas partages par `LearnFrom` : l'erreur de localisation
  appartient a celui qui a entendu, et la transmettre reviendrait a la propager comme un fait. « J'ai
  entendu quelque chose par la » resterait pourtant utile ; il faudrait une croyance qui porte
  l'incertitude au lieu d'un point.
* La carte de chaleur des dangers n'est pas partagee non plus : chacun garde sa propre peur des
  lieux. Deux survivants peuvent donc avoir la meme carte mentale et des itineraires differents.
* Une action de plan nomme desormais son bruit (`noise_action` dans `planner_domain.json`) : forcer
  une porte s'entend comme tel, y compris dans la memoire auditive des agents. En revanche c'est une
  donnee de configuration libre — une action qui l'omet, ou qui declare un nom inconnu, retombe
  silencieusement sur le libelle generique.
* Les zombies s'evitent entre eux mais ne bloquent pas le survivant, et rien n'empeche Alice de
  traverser une case occupee par un zombie.
