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
| 11 — Objets, armes et economie du camp | fait (code, tests et tuiles ; rendu a verifier dans l'editeur) |
| 15 — Instructions tactiques | fait, sauf le service de langage (portillon 3 hors ligne) |
| 16 — Missions aleatoires et recompenses | fait |
| G — Export PDDL | fait |

Le MVP est complet : une partie se joue du choix de l'equipe jusqu'a la victoire ou la defaite.

Au-dela des huit livrables, la boucle de jeu est passee de « une partie » a « une mission » :

* **Preparation** — le joueur choisit ses agents dans une grille de fiches en bas d'ecran, puis sa
  mission parmi trois offres generees a la graine de la partie, puis demarre. Voir
  [Preparation de mission](#preparation-de-mission) et
  [Missions aleatoires et recompenses](#missions-aleatoires-et-recompenses).
* **Le camp** — une mission commence et finit au meme endroit. La zone d'extraction est posee sur la
  zone de depart : on part du camp, on y revient.
* **La ligne d'arrivee** — la partie est gagnee des que la caisse revient au camp avec quelqu'un.
  Depart et arrivee au meme endroit : c'est ce qui donne un sens au mot « mission ». Voir
  [Fin de partie](#fin-de-partie).
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
dans la liste, composer leur paquetage, puis `DEMARRER LA MISSION`. C'est le seul chemin d'entree
d'une partie : il n'existe plus de raccourci de debug qui le saute, parce qu'une partie qui commence
sans que le joueur ait rien confie ne joue pas le meme jeu que celle qu'on livre.

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

`MissionCatalogueTests` couvre le catalogue de missions et sa validation.

`BandTests` couvre le livrable 7 : les trois profils — identiques, et c'est le propos —, le prix
d'une action que seul l'equipement change, `merge_belief` ligne par ligne, la portee de
communication, la repartition des frontieres, l'entraide — et le critere de validation, « une
information decouverte par Alice peut etre utilisee ensuite par Bob apres communication ».

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
| `Maj` + un ordre | adresser l'ordre au groupe entier plutot qu'au seul survivant selectionne |
| Clic droit | cycler le niveau de danger d'une case : aucun -> suspecte -> zombie proche |
| `1` / `2` | recuperer les medicaments / rentrer au camp (la sortie, qui est aussi le depart) |
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
│                        RearGlance, MissionOrder, SurvivorSettings,
│                        SurvivorProfile, SurvivorRoster
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
│                        MissionDefinition, MissionCatalogue, CampStock
└── Config/              GameConfig, MapLocationsConfig, TileLegend, DebugSettings,
                         InventorySettings

Scripts/                 couche Godot (projet Hordinary.csproj)
├── World/MapLoader      convertit les TileMapLayer en GridMap
├── World/VanNode        skin visuel de la zone d'extraction, aucune logique de jeu
├── Agents/SurvivorNode  affichage et pas de simulation d'un survivant
├── Agents/ZombieNode    affichage d'un zombie, couleur selon l'etat
├── Game/GameRoot        assemblage, cadences, entrees joueur
├── Game/Trace           robinet de la console, commande par debug.enabled
├── Game/CameraController
├── UI/CampScreen          ecran de camp : reserves, equipe, mission
├── UI/SurvivorPanel       panneau lateral : identite, jauges, inventaire, decision
├── UI/UiSkin              cadres 9-slice, boutons et icones d'etat d'Assets/UI
├── UI/AgentCard         une fiche d'agent : nom, jauges HP et panique
├── UI/AgentRoster       la barre d'equipe du bas, en mission (interface du joueur)
├── UI/InfoPanel         socle commun des trois panneaux de texte (mode debug)
├── UI/Hud               etat de la partie : mission, groupe, horde, ordres
├── UI/AgentPanel        etat d'un survivant : objectif, action, plan, replanification
├── UI/PlanPanel         etat de la recherche : noeuds GBFS, plans rejetes, journal
├── UI/NavigationDebugOverlay
├── UI/KnowledgeDebugOverlay
└── UI/NoiseDebugOverlay

Tests/                   xUnit, sans Godot
config/                  parametres externes (aucune valeur de gameplay codee en dur)
├── game_config.json     navigation, perception, bruit, survivant, zombies, utility_ai, planner,
│                        replanning, knowledge, exploration, inventory, mission,
│                        mission_generation, debug
├── missions.json        les missions ecrites a la main : objectif, camp, caisses, taille de horde
├── survivors.json       les trois profils : un identifiant, un nom, une image
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
A* prefere un detour connu plus long a un raccourci non observe.

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
qui redevient le fait `searched_in_vain(objective_zone)` cote planificateur. Sans cette memoire, un
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
**considerations primitives** et quatre **courbes de reponse** ; les neuf objectifs livres sont neuf
entrees de [`config/utility_goals.json`](config/utility_goals.json) — la specification en decrivait
douze, cinq ont ete retires au livrable 15 et `FightZombie` ajoute au livrable 8 :

```json
{
  "name": "FleeDanger",
  "target": "away_from_danger",
  "preconditions": [ { "input": "at_extraction", "max": 0.5 } ],
  "considerations": [
    { "input": "threat_pressure",      "curve": "linear" },
    { "input": "health_ratio",         "curve": "inverse", "min": 0.7 },
    { "input": "panic",                "curve": "linear",  "min": 0.7 }
  ]
}
```

Les considerations sont multipliees : **une seule a 0 annule l'objectif**. C'est la plage de sortie
`min`/`max` qui decide de ce droit de veto. `threat_pressure` va de 0 a 1 : sans menace, pas de
fuite. `panic` va de 0,7 a 1 : la panique amplifie la fuite, elle ne la conditionne pas — un
survivant calme fuit quand meme un zombie qui lui saute dessus. C'est la lecture honnete de la
formule `flee = danger * sante * panique` de la specification, qui sinon rendrait toute
fuite impossible a froid.

| Consideration | Lue dans | Va de 0 a 1 quand |
|---|---|---|
| `perceived_danger` | zombies memorises + bruits entendus | rien en vue -> menace au contact |
| `threat_pressure` | le plus fort du danger percu et de la panique | tout va bien -> menace ou terreur |
| `personal_safety` | complement de `threat_pressure` | menace ou terreur -> tout va bien |
| `ally_danger` | menaces autour d'un allie croyable | allie tranquille -> allie assailli (plus aucun objectif ne la lit, voir « Limites connues ») |
| `pursued` | cap memorise des menaces proches | personne ne la cherche -> on lui court apres |
| `health_ratio`, `panic` | etat du survivant | agonisant -> intact, calme -> panique |
| `has_objective`, `has_ally`, `objective_known` | inventaire et memoire | non -> oui |
| `unexplored_ratio` | carte mentale | terrain connu -> terrain vierge |
| `player_priority`, `plan_confidence` | ordre du joueur, plan courant | ignore -> imperatif |
| `mission_reached` | ordre du joueur et position | en route -> arrivee a destination |
| `heard_noise` | dernier bruit credible | silence -> vacarme |
| `distance_to(cible)` | memoire | sur place -> a `distance_scale` cases |

`pursued` n'est pas un synonyme de `perceived_danger` : on peut etre cerne par des zombies qui ne
vous ont pas vu, ou poursuivi de loin par un seul. Il vaut 0 tant que les menaces connues sont
immobiles ou s'ecartent, et ne monte que pour une menace credible, proche, dont le cap pointe vers
le survivant. `Hide` s'en sert en `inverse` : se terrer marche contre ce qui ne vous a pas vu, pas
contre ce qui vous suit.

`distance_to(...)` et la cible d'un objectif partagent le meme vocabulaire : `ally`, `group`,
`objective`, `objective_zone`, `pharmacy`, `extraction`, `start`, `cover`, `frontier`, `danger`, `away_from_danger`,
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

Un objectif qui se ferme en arrivant a destination laisse la place a un autre, et c'est la que se
loge le piege : si celui qui prend la suite fait ressortir le survivant, la precondition redevient
fausse et le premier objectif revient. `ReachExtraction` se fermait sur `at_extraction`,
`ExploreUnknownArea` prenait la suite, le porteur quittait le van, cessait d'etre a l'extraction, y
retournait — un aller-retour de deux secondes, tenu jusqu'a la fin de la partie. La reponse n'est pas
d'empecher la sortie du van mais de fermer ce qui n'a plus lieu d'etre : `has_objective` retire
l'exploration a qui rapporte la caisse. Cela reste une precondition dans un fichier, pas une regle
dans le code, et c'est la ligne de partage du livrable 15 — repartir en reconnaissance avec la caisse
sur le dos serait une strategie, elle revient au joueur.

Le piege a deux autres pretendants, `FleeDanger` et `Hide`, et la reponse est la meme sans etre la
meme raison. Tant que la horde pouvait mordre au van, fuir en etait un reflexe legitime : celui qui se
fait entamer en attendant les autres a raison de partir, et tenir la position sous les coups aurait ete
une decision tactique — donc au joueur. Le van protege maintenant (voir « Fin de partie »), et la
question change de sens : le seul endroit du quartier ou l'on ne peut pas etre frappe est aussi le seul
d'ou fuir revient a courir vers le danger. `FleeDanger` et `Hide` recoivent donc la precondition
`at_extraction`, et la seule chose qu'on abandonne en la posant est une erreur. Dehors, elles scorent
exactement comme avant.

Un evenement important court-circuite le cooldown, jamais l'hysteresis : zombie repere, bruit fort
entendu, survivant blesse, itineraire bloque, nouvel ordre du joueur. Une seule chose la franchit, le
rappel du van (`Reconsider`, voir « Fin de partie ») : il ne se discute plus, et l'hysteresis figeait
un survivant terre pour le reste de la partie. La raison remonte telle quelle dans le journal :

```
[Agent] Alice repere zombie-21.
[Utility] Alice : ExploreUnknownArea est retombe sous le seuil de 0.05 :
          FleeDanger prend la suite (0.52) — zombie-21 repere
[Utility]    FleeDanger  0.52  = threat_pressure 0.85->0.85 x health_ratio 1.00->0.70
                                 x panic 0.85->0.96
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

### Ce que fait Alice de son objectif

Chaque objectif porte une cible, que `UtilityContext.Locate` traduit en cellule **a partir de la
seule memoire d'Alice**. `away_from_danger` choisit, parmi les cellules qu'elle a deja vues, la plus
sure au sens du danger cumule — pas seulement la plus eloignee du zombie le plus proche, sinon on
s'ecarte de l'un pour tomber sur trois autres — moins le prix du trajet. `frontier` prend la cellule
connue la plus proche voisine d'une cellule jamais observee.

Depuis le livrable 5, cette cible n'est plus la reponse finale mais le vocabulaire commun entre la
Utility AI et le planificateur : les memes noms — `objective_zone`, `cover`, `frontier`,
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
at(agent, start)  known(start)  accessible(start)  known(objective_zone)  accessible(objective_zone)
known(extraction)  extraction_known  known(cover)  known(frontier)  at(agent, frontier)
door_closed(door_6_m2)  door_of(door_6_m2, objective_zone)
```

Les objets ne sont pas declares en configuration, ils sont derives de la memoire : un lieu par cible
resolvable, une porte ou une fenetre par cellule memorisee, un allie par croyance, une menace par
zombie memorise. Un lieu est `accessible` tant qu'une de ses ouvertures connues n'est pas verrouillee
— c'est cette precondition qui **oblige** le plan a intercaler une action adaptee.

`at(agent, objective_zone)` designe un **lieu**, pas une case : la pharmacie fait dix cases de long. C'est
pourquoi fouiller ou ramasser porte sur le local entier quand on est a l'interieur
(`survivor.search_radius` sinon) — la granularite du plan est la piece.

### Une action est une donnee, pas une classe

Il n'y a ni `MoveToAction` ni `OpenDoorAction`. Le code connait **huit verbes primitifs** — `toPos`,
`take`, `put`, `setFlag`, `wait`, `damage`, `push`, `emitNoise` — et les vingt-cinq actions livrees
sont vingt-cinq entrees de [`config/planner_domain.json`](config/planner_domain.json) :

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
`ForceDoor(door_6_m2, objective_zone)`. `!` nie une precondition ou retire un fait, et `!at(agent, *)`
retire toute position memorisee avant que le deplacement n'en pose une nouvelle — les suppressions
sont appliquees avant les ajouts.

`OpenDoor`, `SearchRoom`, `HealSelf` et `ShareKnowledge` sont tous des `setFlag` avec un fait
different. `AttackZombie` est un `damage` a mains nues, `AttackWithWeapon` le meme verbe avec
`amount: 0`, qui veut dire « ce que je tiens » et fait lire les degats dans `config/weapons.json`.

Le verbe `emitNoise` n'a plus d'action qui l'emploie : `ThrowFirecracker` a ete retiree du domaine en
meme temps que `CreateNoiseDistraction`, l'objectif `CreateDistraction` et le fait
`distraction_created`. Faire du bruit expres pour attirer la horde ailleurs n'est pas un reflexe,
c'est un sacrifice calcule — voir « Ce qui a ete retire, et pourquoi ». Le moteur sait toujours
l'executer (`WorldPlanRuntime.ThrowNoise`, et l'effet `emit_noise` du petard de `config/items.json`) :
c'est le demandeur qui manque, et le livrable 15 doit le fournir.

Un verbe inconnu, un parametre manquant, une variable non declaree ou un type d'objet inexistant sont
refuses **au chargement**, avec le vocabulaire disponible.

### Ce que le domaine sait faire, et ce qui y dort

Une action n'est jouable que si une chaine arriere la relie a un but symbolique, et si un objectif de
la Utility AI demande ce but. Les vingt-cinq schemas ne se valent donc pas, et c'est mesurable : le
tri de `ActionRelevance` part des faits que le but reclame et remonte jusqu'au point fixe, si bien
qu'une action dont **aucun** effet n'entre dans cette cloture ne peut apparaitre dans aucun plan, pour
aucun objectif.

| Etat | Actions | Ce qui les tient |
|---|---|---|
| **Jouees** — vues dans un plan sur la carte livree | `MoveToLocation`, `Explore`, `SearchRoom`, `LocateObjective`, `PickUpObjective`, `PickUpSpottedObjective`, `ForceDoor` | la mission elle-meme |
| **Atteignables** — chaine complete, selon la partie | `OpenDoor`, `BreakWindow`, `ReachExtraction`, `EscapeLocation`, `Hide`, `PushBackZombie`, `AttackZombie`, `HealSelf`, `EquipWeapon`, `AttackWithWeapon`, `PickUpItem` | une porte fermee, un zombie au contact, une blessure, une arme au sol |
| **Degeneree** | `DropItem` | elle survit au tri par `item_at` et `!has`, mais aucun but ne demande de poser quoi que ce soit : elle ne peut apparaitre que dans un cycle que GBFS ne prefere jamais |
| **Inertes** — jamais pertinentes, quel que soit le but | `Observe`, `Wait`, `UseItem`, `PushHeavyObject` | leurs effets — `observed`, `waited`, `used`, `pushed` — ne sont lus par aucune precondition et par aucun but |
| **Sans demandeur** | `ShareKnowledge` | le but symbolique existe, aucun objectif de `utility_goals.json` ne le reclame |

Les quatre inertes ne sont pas des oublis. `Observe`, `Wait` et `DropItem` sont listees par
[`Specs/05`](../Specs/05-planification-gbfs.md) parmi les actions a couvrir : elles sont ecrites, et il
leur manque un but qui les reclame. `UseItem` et `PushHeavyObject` viennent du livrable 11 et
attendent la meme chose — c'est au livrable 15 de le leur donner, sous la forme d'une instruction
tactique du joueur plutot que d'un objectif d'utilite de plus.

Deux consequences a connaitre avant d'ecrire une instruction tactique :

* **`PickUpItem` ne sert plus qu'a s'armer.** Le seul chemin qui y mene passe par `EquipWeapon` →
  `AttackWithWeapon` → `FightZombie`. Il y en avait un second, `ThrowFirecracker` →
  `CreateDistraction`, qui est parti avec la diversion. Aucun but ne dit « ramasse ce qui traine », et
  ramasser des vivres n'est donc pas planifiable.
* **Un blesse ne peut pas planifier de ramasser un bandage qu'il voit.** `HealSelf` demande
  `has(agent, remedy)`, et `remedy` n'est declare par `SymbolicStateBuilder` que si le sac en contient
  **deja** un : ce qui soigne au sol reste un `item_at(bandage, spot_x_y)` que rien ne relie au soin.
  L'objectif d'utilite `HealSelf` a la meme precondition (`has_remedy`) et ne score meme pas.

`BarricadeDoor` etait un cas a part, et ne l'est plus. [`Specs/11`](../Specs/11-objets-armes-et-economie-du-camp.md)
la decrivait, `WorldPlanRuntime.SetFlag` avait sa branche `door_barricaded` et `config/items.json` la
citait dans le commentaire de la caisse a outils — mais elle n'avait **aucune entree** dans
`planner_domain.json`. Le moteur savait barricader une porte ; personne ne savait le lui demander, et
surtout personne ne *pouvait* : la grammaire du livrable 15 valide les instructions du joueur contre ce
fichier, et aurait rejete « barricade cette porte » comme symbole inconnu.

Le schema y figure desormais, au contrat exact du tableau de la spec 11 : `at(agent, ?location)`,
`door_of(?door, ?location)`, `door_closed(?door)` et `equipped(toolbox)` en preconditions,
`door_barricaded(?door)` en effet. Deux ajouts au tableau, et tous deux pour dire la verite sur le
monde plutot que de la taire — `!door_closed(?door)` et `door_locked(?door)`, parce que c'est ce que
`WorldPlanRuntime.Barricade` fait reellement a la grille. La caisse a outils est citee **en toutes
lettres** et non par un modificateur de `config/items.json` : un seul mecanisme de disponibilite vaut
mieux que deux, ce que le commentaire de la caisse disait deja.

Ce que cet ajout ne fait **pas** : rendre l'action jouable. Aucun but ne lit `door_barricaded`, donc
`ActionRelevance` l'ecarte pour tous les objectifs, exactement comme `Observe` ou `Wait`. Elle passe
de « inexistante » a « en sommeil », ce qui est le bon etat d'attente — et c'est tout ce qu'un
livrable anterieur au 15 peut lui offrir sans violer la regle de coupe : condamner une porte est une
strategie, pas un reflexe.

### GBFS, et pourquoi ce n'est pas A* par defaut

```
priorite du noeud = (heuristique(etat, but), cout accumule, ordre de generation)
```

L'heuristique d'abord, et elle seule : la priorite n'est **pas** `g + h`, le cout ne s'ajoute jamais
a l'estimation. La recherche reste donc gloutonne et **non optimale**.

Le cout n'intervient qu'a estimation egale, comme second critere. Ce n'est pas un detail : `Heuristic`
rend exactement `0` pour tout etat qui satisfait le but, si bien que **toutes** les actions terminales
sont a egalite. Sans le cout, c'est l'ordre de declaration dans `planner_domain.json` qui tranchait —
et les couts d'action, les `transition_cost` et les coefficients d'arme de `config/weapons.json`
n'avaient aucun effet sur le plan retenu, alors que tout le reste du projet raisonne comme s'ils en
avaient un. C'est ce qui faisait repousser un zombie plutot que de l'abattre, machette en main.

L'ordre de generation ferme la marche : a estimation et cout egaux, deux planifications sur la meme
memoire rendent le meme plan — le journal et les tests restent reproductibles.

#### L'autre strategie : `planner.search`

```json
"planner": { "search": "gbfs" }
```

`astar` change **une seule ligne** du chercheur, la fonction de priorite :

```
gbfs  : (h,     g, ordre de generation)
astar : (g + h, h, ordre de generation)
```

Tout le reste — la frontiere, la table des etats fermes, les trois garde-fous, la reconstruction du
plan — est le meme code. C'est la raison de ne pas avoir ecrit deux chercheurs : deux boucles, c'est
deux fois la meme correction a faire, et une seule des deux qui la recevrait.

Le second terme change lui aussi de role. Sous `gbfs` il departage des estimations egales par le
cout ; sous `astar` le cout est deja dans la cle, et c'est `h` qui departage — a somme egale, on
developpe le noeud le plus proche du but plutot que le plus avance dans un detour.

**`gbfs` reste le defaut, et c'est ce que le jeu joue.** Deux raisons. Le livrable 5 nomme GBFS, et
[`Specs/D`](../Specs/D-qualite-et-definition-of-done.md) en fait un critere d'acceptation. Et
l'equilibrage du domaine — les couts de `planner_domain.json`, les coefficients de `weapons.json` —
a ete regle en lisant les plans que GBFS produit ; basculer la strategie par defaut reviendrait a
rejouer cet equilibrage.

`astar` est donc la pour etre **compare**, pas pour remplacer : un meme objectif, deux fichiers de
configuration, et l'on lit dans le journal ce que le plan a coute et combien de noeuds il a fallu
ouvrir pour le trouver. Une mise en garde honnete tant qu'a faire : ce n'est A* qu'au sens de la
fonction de priorite. `Heuristic` ajoute au nombre de conditions non satisfaites des termes de
distance, de danger et de bruit ponderes par la configuration ; rien ne borne leur somme par le cout
reel du reste du plan, l'heuristique n'est donc pas admissible et l'optimalite n'est pas garantie
pour autant. C'est un A* pondere, ce qui est la situation ordinaire d'un A* de planification.

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

Pour « recuperer ce qu'on est venu chercher » depuis le depart, lieu de mission ferme a clef :

```
MoveToLocation(objective_zone) -> ForceDoor(door_5_5, objective_zone) -> SearchRoom(objective_zone)
-> LocateObjective -> PickUpObjective
```

Aucune pharmacie n'est nommee, et c'est le point : `objective_zone` est le lieu que la mission a
designe. La meme suite d'actions joue un ravitaillement de pharmacie, une fouille d'entrepot ou une
traversee de hangar — c'est `config/missions.json` qui en decide, pas le domaine.

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
{ "name": "PickUpObjective",  "conditions": [ "has(agent, objective)" ] },
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

Reste l'acquis durable — `searched(objective_zone)`, `window_broken`, `shared_with` — qui survit. La regle
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
Raison : SearchRoom(objective_zone) : « accessible(objective_zone) » n'est plus vrai (une porte est verrouillee)
Objectif precedent : SearchForObjective
Nouvel objectif : SearchForObjective
Nouveau plan : SearchForObjective : MoveToLocation(objective_zone) -> ForceDoor(door_6_2, objective_zone)
               -> SearchRoom(objective_zone) -> LocateObjective (4 actions, cout 8.5, bruit 8, 10 s)
```

La cause n'est pas devinee au nom de la precondition : c'est `accessible(objective_zone)` qui devient
faux, et c'est bien de verrouillage qu'il faut parler. `PlanValidity` remonte de l'argument en defaut
vers ce que l'etat affirme des objets qui s'y rattachent — `door_of(door_6_2, objective_zone)` et
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
nomme. Ce sont trois entrees de [`config/survivors.json`](../config/survivors.json), et une entree
ne declare que trois choses : un identifiant, un nom, une image.

Il n'y a **ni role, ni caracteristiques, ni aptitudes, ni competences par action**, et c'est le
choix qui structure le reste. Les trois partent avec la meme sante, la meme vue, la meme vitesse et
le meme repertoire d'actions au meme prix — tout cela se regle une fois pour tous dans la section
`survivor` de `game_config.json`.

Ce qui les distingue tient en deux choses, et ce sont exactement celles que le projet demontre :

1. **chacun ne connait que ce qu'il a vu** (livrable 2) — deux survivants cote a cote n'ont pas la
   meme carte mentale, donc pas les memes plans ;
2. **le joueur leur parle** (livrable 15) — une contrainte en langage naturel change ce qu'ils
   peuvent planifier.

Une fiche de personnage aurait mis une troisieme explication a cote des deux autres. Devant un
survivant qui contourne une porte au lieu de la forcer, « il est maladroit » et « le joueur a
interdit le bruit » se seraient disputes la meme observation — et la premiere aurait toujours ete la
plus facile a croire.

### Ce qui change le prix d'une action

Le prix d'une action ne depend pas de qui la fait, mais de **ce qu'on a en main**. Un
`ActionModifier` pondere les trois grandeurs qu'une action porte deja — son cout, son bruit, sa
duree — plus le droit de l'interdire. Ils viennent tous de l'equipement, via les `effects` de
`config/items.json` et `config/weapons.json`.

`PlanningDomain.Specialize` en derive une copie du domaine ; le domaine commun n'est jamais modifie,
et le domaine d'un survivant se recalcule quand son sac change
(`Inventory.EquipmentChanged` -> `SurvivorAgent.RefreshDomain`).

Cela suffit a produire des comportements distincts, **sans aucune branche par personnage** : GBFS
minimise le cout. `ForceDoor` vaut 3 dans le domaine commun et 1,5 pour qui porte la caisse a outils
— devant une porte verrouillee, celui qui l'a ramassee propose spontanement de la forcer la ou les
autres cherchent la fenetre ou le detour. La difference sort de la recherche, et elle tient a une
situation, pas a une identite : elle s'inverse des qu'on se passe la caisse.

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
l'interlocuteur. Elles conditionnaient `HealAlly` et `CalmAlly` jusqu'au livrable 15, qui a retire
l'entraide autonome ; elles restent les entrees `ally_wounded` et `ally_panicking` de la Utility AI —
le vocabulaire ou puiseront les strategies que le joueur, lui, a le droit de composer.

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

* un **ordre** (`FetchObjective`, `ReachExtraction`, `ExploreArea`, `Regroup`) pose un
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

### Fin de partie

`MissionTracker` observe le monde, tient l'horloge de la mission, et ne modifie rien d'autre. La
ligne d'arrivee est double : **la caisse revient au van, et le groupe avec elle**. Depart et arrivee
au meme endroit — la zone d'extraction est posee sur la zone de depart — et c'est ce qui donne un
sens au mot « mission ». Il faut la caisse **et** tout le monde au van : c'est elle qui doit franchir
la ligne, mais on ne laisse personne derriere.

Le retour d'un seul ne conclut donc rien. C'etait le cas avant, et cela vidait la fin de partie de
tout enjeu : le plus rapide franchissait le seuil, et le sort des deux autres n'avait aucune
consequence. « Tout le monde » s'entend des survivants **encore capables d'agir** — on n'attend pas
indefiniment celui qui est reste sur le pave, sans quoi une seule perte empecherait a jamais la
mission de se conclure.

Un survivant compte comme rentre s'il est au van **maintenant**, ou s'il y est rentre plus tot et ne
s'en est pas ecarte de plus de `mission.home_radius` cases. Les deux, parce que ni l'un ni l'autre ne
suffit : celui qui n'a jamais quitte le vehicule n'a aucun retour a effectuer, et celui qui est
arrive a rempli son ordre — la Utility AI lui rend la main, et il peut tourner autour du van en
attendant les autres.

Le retour est **revocable**, et c'est ce qui compte. Tant qu'il ne l'etait pas, un survivant qui
traversait le camp dans les premieres secondes de l'expedition comptait comme rentre jusqu'a la fin
de la partie. Sur la carte livree, ou le camp jouxte la ruelle, les trois y passaient : la victoire
se declarait a la seconde ou la caisse etait ramassee, tout le groupe etant encore dans la pharmacie.
Le rayon est ce qui separe les deux erreurs symetriques — declarer « ressorti » quiconque fait un pas
de cote repousse la fin de la mission a un instant ou les trois sont sur la meme poignee de cases ;
retenir le retour une fois pour toutes la declare n'importe ou.

**Le temps imparti** (`mission.time_limit`, cinq minutes dans le fichier livre) : une expedition ne
dure pas une soiree. Le delai ecoule, le van annonce le depart et tout le monde rentre. Le rappel
passe par `SurvivorBand.Recall` et emprunte le chemin d'un ordre du joueur — un `MissionOrder` de
plus dans les scores d'utilite, avec le poids `mission.recall_priority`, resolu dans la memoire de
chacun. Ce n'est donc pas un pilotage : celui qui a une horde aux trousses commence par lui echapper,
puis rentre. Une mission de `config/missions.json` peut redire `time_limit` pour elle seule, et zero
desamorce le chronometre.

Le rappel **reconsidere** au lieu de simplement demander a reflechir (`UtilitySelector.Reconsider`
contre `Request`). La nuance decide de la fin de partie : `Request` rend la main tout de suite mais
laisse au titulaire le benefice de l'hysteresis, et un objectif installe qui reste sous le pretendant
d'un ecart inferieur au seuil ne lache jamais. Deux survivants terres dans la pharmacie scoraient
`Hide` a 0,25 contre le rappel a 0,37 — meilleur score, ecart de 0,12 sous une hysteresis de 0,15 —
et se terrer entretient exactement le danger qui fait se terrer : le van partait, ils restaient, la
mission ne se concluait pas. Reconsiderer refait la decision sans l'imposer ; celui qu'une horde
poursuit rescore la fuite en tete et fuit d'abord.

**Le van protege.** La zone `Extraction` est le seul endroit du quartier ou l'on ne peut pas etre
frappe : `EntityManager.Sheltered` regarde si la cible d'un zombie s'y tient, et le coup n'est pas
porte. La horde se presse contre la tole, elle n'atteint pas ce qui est dedans. Sans cette regle, le
premier rentre passait le reste de la mission a se faire entamer en attendant les autres — le groupe
etait puni de la seule chose que la victoire lui demande, s'attendre.

La regle est posee sur le monde et non dans la decision du zombie : sa machine a etats ne lit que sa
propre memoire (invariant 2), et lui apprendre ou est le van reviendrait a lui donner la carte. Il
decide donc d'attaquer comme avant. Une carte qui ne declare pas de zone d'extraction n'abrite
personne, et c'est le bon defaut : un abri se declare, il ne s'invente pas.

Sa contrepartie est cote decision, dans « Les garde-fous contre l'oscillation » : `FleeDanger` et
`Hide` se ferment au van. Un abri que l'IA quitte a la premiere silhouette n'est pas un abri.

Le rappel appartient au `MissionTracker` et non a l'appelant parce que c'est lui qui tient l'horloge :
le confier a la boucle en ferait deux copies — une dans `GameRoot`, une dans le harnais de test — qui
divergeraient le jour ou l'une des deux changerait.

Trois facons de perdre :

* plus aucun survivant en etat d'agir ;
* plus aucun medicament nulle part. Cette clause n'est pas decorative — `HealSelf` consomme ce qui
  soigne, et se soigner avec le dernier stock perd donc la partie ;
* le temps ecoule et le groupe rentre les mains vides. Avant le rappel, un groupe au complet au van
  ne perd rien : rien ne dit qu'il a renonce, et il peut repartir.

**Une mission finie ramene au camp.** Le verdict rendu, la carte se laisse regarder
`mission.debrief_seconds`, puis `GameRoot` resout la journee (`CampStock.ResolveDay` : le camp
mange), vide la scene de tout ce que la mission y avait pose, rend aux couches de tuiles la carte
dessinee, et rouvre l'ecran de camp avec le verdict en tete et les offres du lendemain. Le prototype
s'arretait sur un verdict fige : tout ce que le camp sait faire — consommer, entreposer, reproposer —
n'arrivait jamais, et le jour de campagne ne bougeant pas, les trois memes offres seraient revenues.

Le nettoyage passe par un registre (`AddMissionChild`) plutot que par une recherche de noeuds par
leur nom : ce qui appartient a la mission y est inscrit en y entrant, et un noeud ajoute demain sera
retire sans qu'on y revienne. Ce qui appartient a la **partie** — la camera, les couches, l'ecran de
camp — n'y est pas et reste en place. Les couches de tuiles sont recopiees telles que l'editeur les a
laissees avant toute peinture, une fois pour toutes : `MapPainter.Paint` efface les couches pour
poser un quartier genere, et sans cette copie la « carte dessinee » de la mission suivante serait le
quartier de la precedente.

Une limite assumee : `ResolveDay` plafonne la sante de ceux que le camp n'a pas pu nourrir, et ce
plafond ne survit pas au retour — un survivant est recree a partir de son profil a chaque depart, et
rien ne porte encore son etat d'une mission a l'autre. La disette se voit dans les reserves et au
journal, pas dans les corps.

`OutcomeTests` joue une partie entiere sur la carte livree, du depart a la victoire, sans jamais
placer personne a la main : le joueur demande les medicaments, la Utility AI bascule seule sur
`ReachExtraction` une fois la caisse en poche, et le camp est la sortie. Un autre test laisse le
chronometre aller a son terme et verifie que la boucle ramene le groupe au van sans que personne ait
a conclure la mission a sa place.

### Preparation de mission

Une partie commence par un choix, en trois temps affiches en meme temps — c'est un briefing, pas un
assistant en trois pages : on choisit ses agents en regardant la mission, et on change d'avis en
lisant la difficulte.

1. **Les agents**, dans une grille horizontale de fiches en bas d'ecran. Chaque fiche porte le nom, le
   role, et la jauge `HP 80/80`. Un clic enrole ou retire.
2. **La mission**, choisie parmi trois offres generees (livrable 16) : titre, difficulte, zone,
   objectif chiffre, equipe conseillee, temps, cout d'entree en essence et recompenses exactes. Une
   offre generee n'a pas de briefing — l'ecran affiche des donnees comparables, et rien qu'elles. Les
   missions ecrites dans [`config/missions.json`](config/missions.json) restent proposees telles
   quelles, avec leur briefing, quand `mission_generation.enabled` est a `false`.
3. **Le paquetage** — ce que chacun emporte, pris dans l'entrepot du camp. Une ligne par survivant
   enrole : ce qu'il porte deja, ce que l'etagere offre encore, ses emplacements et son poids.
4. **Demarrer** — le cout d'entree est debite une seule fois, et rien n'existait avant : ni les
   survivants, ni la horde, ni les caisses.

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

#### Le paquetage : ce que le joueur confie, et ce qui doit revenir

Distribuer le materiel du camp est une **strategie**, pas un reflexe — la regle de coupe du livrable
15, appliquee telle quelle. Un survivant ramasse ce qu'il trouve sur place parce qu'il en a besoin
la tout de suite ; decider la veille lequel des trois emporte l'unique fusil est une decision de
joueur, et elle se prend au camp.

`CampLoadout` (`Hordinary.Core/Game/`) tient ces promesses, et rien de plus :

* **composer ne coute rien.** L'entrepot n'est debite qu'au depart (`HandOver`), comme le cout
  d'entree d'une offre : consulter, defaire, changer d'avis est gratuit. `Available` est ce que
  l'entrepot contient moins ce qui est deja promis, de sorte qu'un camp qui n'a qu'une trousse ne
  peut pas l'avoir confiee a deux survivants ;
* **les limites du sac ne sont pas redites.** Un paquetage **est** un `Inventory`, construit avec les
  reglages de `inventory` : emplacements, piles et poids sont ceux du jeu. `Inventory.Accepts` est la
  regle du ramassage posee une fois — `Add` la consulte, l'ecran aussi pour griser un bouton, et il
  n'existe pas de second comptage a faire diverger ;
* **une arme confiee part en main.** Le joueur a decide qui porte le fusil ; la lui laisser au fond du
  sac reviendrait a ne pas ecouter la decision ;
* **preter n'est pas donner.** Ce que le camp a confie revient a l'entrepot avec celui qui rentre,
  mission gagnee ou perdue (`Recover`). Seul ce que le groupe a ramasse en chemin depend du verdict.
  Ce qui ne rentre pas — un sac reste dehors, un objet consomme — est perdu, et c'est la que le pret
  coute.

Le materiel de depart de l'entrepot se declare dans [`config/camp.json`](config/camp.json)
(`storage`). Sans lui, l'entrepot n'existerait qu'apres une premiere victoire et le premier jour
n'aurait rien a repartir ; une liste vide rend un camp demuni. Symetriquement, ce qu'un survivant
rapporte et qui n'alimente aucune reserve va desormais a l'entrepot (`CampStock.Deposit`) : le
survivant est recree a chaque mission, et une arme y disparaissait avec l'objet qui la portait.

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

Dans l'autre sens, il existe un endroit ou aucun coup ne porte : la zone d'extraction. Voir « Fin de
partie » — c'est la seule exception a « un zombie au contact mord », et elle est posee sur le monde,
pas sur la decision du zombie.

### La barre d'equipe et les trois panneaux

L'ecran porte deux interfaces qui ne s'adressent pas au meme public.

La **barre d'equipe** en bas est celle du **joueur** : les memes fiches que l'ecran de preparation,
aux memes coordonnees, mais vivantes. Elle montre qui est encore dehors, dans quel etat (`HP`,
objectif courant), et sur qui portent les ordres. Un agent rentre au camp n'en disparait
pas : sa fiche s'eteint et annonce `rentre au camp` — le faire disparaitre laisserait un trou, et le
joueur se demanderait qui manque. Elle reste allumee quand le mode debug est eteint.

Les **trois panneaux de texte** montrent ce que la **simulation** pense, et disparaissent avec le
mode debug (`debug.hud_text`). Leur partage suit celui de la specification, et il repose sur une
distinction que le livrable 2 rend obligatoire : **ce que le joueur sait n'est pas ce qu'un survivant
sait**.

* **HUD** (haut gauche) : l'etat de la partie — la mission, l'heure, le groupe, la horde
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
| `console_output` | le journal : plans, replanifications, bruits, ordres, partages |
| `commands` | `F1` a `F7`, `C`, `Espace`, clic droit — les touches de l'observateur, pas celles du joueur |
| `hud_text` | les trois panneaux de texte ; il reste la carte et la barre d'equipe |

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

## Objets, armes et economie du camp

Le livrable 11 ajoute quatre choses au monde — un sac, des armes, des meubles qu'on pousse, et un
camp qui mange — sans ajouter une seule classe par objet de jeu.

### Aucun objet n'existe dans le code

`config/items.json` et `config/weapons.json` decrivent quinze entrees ; `Hordinary.Core` n'en nomme
aucune. Un objet est un identifiant, un poids, une categorie, et une liste d'**effets** ecrits dans un
vocabulaire de verbes primitifs : `heal`, `feed`, `drink`, `refuel`, `modify`, `emit_noise`. Les deux
fichiers partagent un espace d'identifiants unique (`ItemCatalogue.Merge`) : un inventaire ne connait
qu'un `id`, jamais le fichier dont il vient.

`Inventory` porte les quatre limites de `game_config.json` (`inventory`) : emplacements, poids,
emplacements d'arme, penalite de surcharge. Un sac plein **refuse** un ramassage — et c'est cet echec
d'action qui declenche une replanification. Le poids, lui, n'interdit rien : il ralentit.

### Une arme equipee ne fait que changer un prix

C'est le point de conception du livrable. Un pied-de-biche en main ne declenche aucun code
particulier : ses effets sont traduits en `ActionModifier`, et `SurvivorAgent.RefreshDomain`
recompose le domaine en multipliant ceux de tout ce qui est equipe. GBFS decouvre le nouveau plan
tout seul, et **aucune ligne de code ne teste ce qu'un survivant porte**.

C'est aussi le seul mecanisme qui differencie deux survivants devant la meme porte — voir
[Ce qui change le prix d'une action](#ce-qui-change-le-prix-dune-action).

Une arme a feu sans munitions ne disparait pas : elle retombe sur `unloaded_damage` / `unloaded_noise`
et sur une portee de 1. Une arme a durabilite nulle reste dans le sac, cassee — c'est une information
a ramener au camp, pas un objet qui s'evapore.

### Objets lourds

`HeavyObject` est un meuble, `HeavyObjects` la regle. La regle vit a part parce qu'elle porte sur la
scene entiere : la case d'ou l'on pousse, celle ou l'on pousse, et le nombre de bras. Au-dela de
`SoloMassLimit` (3), il faut un allie adjacent — c'est la seule regle du jeu qui exige d'etre deux au
meme endroit, et elle donne au regroupement une raison mecanique plutot qu'une consigne.

`WhyCannotPush` renvoie une **raison**, pas un booleen : c'est elle qui part au journal quand l'action
echoue, et une replanification qui sait pourquoi vaut mieux qu'une qui reessaie a l'identique.

Un meuble deplace n'est appris que par les temoins (`WorldPlanRuntime.TellWitnesses`). La perception
partielle du livrable 2 vaut aussi pour le decor : un survivant qui revient sur une case qu'il croit
degagee peut s'y heurter.

### Ce que les tuiles rendent impossible

Deux indisponibilites du livrable ne sont ecrites nulle part dans le planificateur. `WindowBarred` est
un `CellType.Wall` pour la carte : `BreakWindow` n'a donc **aucun objet** sur lequel s'appliquer.
`ShutterHalf` est une `DoorClosed` qu'aucune donnee ne verrouille, et `GridMap` marque sa case
infranchissable pour qu'une passe ulterieure ne la verrouille pas par accident. Une action devient
inapplicable parce que son objet n'existe pas — c'est plus solide qu'une condition ecrite quelque part.

### Une seule image par objet

Il n'existe pas de serie d'icones d'inventaire. La vignette d'un objet **est** sa tuile de 32 px :
`TileLegend.TileOf(id)` donne son adresse dans l'atlas (`TileAddress`, la cle `source:x,y` relue en
trois nombres), et `ItemIcons` (`Scripts/UI/`) y decoupe un `AtlasTexture`, affiche a un facteur
d'echelle entier. Quand un objet apparait sur plusieurs tuiles — `medikit` en a deux, dont une heritee
du MVP qui peint aussi un sol — la vignette prend celle qui ne pose que l'objet.

La direction artistique commandait quinze dessins de plus, vus de trois quarts, en 48 x 48. Les
abandonner n'est pas qu'une economie d'images : deux dessins pour un objet, ce sont deux verites a
tenir accordees, et la premiere fois qu'on repeint l'une sans l'autre, une conserve n'a plus la meme
tete selon qu'on la regarde par terre ou dans un sac. Ici, repeindre l'atlas repeint l'inventaire.

L'ecran de preparation s'en sert deja : chaque reserve du camp porte la vignette de l'objet qui la
remplit. Aucun code ne nomme cet objet — `CampStock.StockerOf` cherche dans le catalogue celui dont un
effet alimente la reserve, et changer d'objet nourricier reste une modification de
`config/items.json`.

### Le camp

`CampStock` resout une journee — une mission — hors simulation : il ne connait ni
carte ni `WorldState`. La faim est **collective**, il n'y a pas de faim individuelle a suivre case par
case. Un survivant non ravitaille voit sa sante maximale plafonnee.

Les trois etats de chaque reserve (`empty_threshold`, `full_threshold` de `config/camp.json`) sont lus
la et nulle part ailleurs : l'ecran de preparation les affiche sans redecider a partir de quel chiffre
une reserve est basse.

### Zones et agencements

`ZoneKind` reste la categorie **structurelle**, celle que la navigation et le planificateur
connaissent. Un champ `profile` s'y ajoute : une chaine pointant vers `config/zones.json`, et une
chaine ne se recompile pas. Cinq zones, **quatre** agencements primitifs (`aisles`, `open`, `cells`,
`forecourt`) : c'est la que se trouve le rendement, et pas dans les tables de butin. Aucune zone
n'ajoute de mecanique — chacune recompose des systemes deja livres dans une geometrie qui les fait
enfin compter.

Le champ `objective` de `config/missions.json` accepte desormais les deux vocabulaires dans la meme
chaine : `"Pharmacy"` designe un `ZoneKind`, `"station_service"` un profil. La carte de demonstration
ne declare aucun `profile` et reste jouable.

Une zone peut aussi declarer les types de mission qu'elle accepte (`mission_types`, livrable 16). Le
champ existe pour les exceptions, pas pour la norme : declarer les trois types sur les cinq zones
livrees ne dirait rien de plus que le silence, et ferait mentir la premiere zone qu'on ajouterait en
oubliant la ligne. A defaut, les regles generales s'appliquent — une zone avec des zombies peut
recevoir une elimination, une zone avec un butin une recuperation, une zone contenant une ressource
vitale un ravitaillement.

## Missions aleatoires et recompenses

Le catalogue ecrit a l'avance est devenu un **generateur d'offres** (livrable 16). A l'ouverture de
l'ecran de camp, `MissionGenerator` tire `mission_generation.offer_count` missions : une zone cible,
un objectif mesurable, une recompense annoncee, un cout d'entree en essence. Le fichier
`config/missions.json` reste charge et jouable — c'est `mission_generation.enabled` qui tranche, et
c'est aussi ce qui rend le livrable retirable sans toucher au MVP.

### Une offre est une `MissionDefinition`

Il n'existe pas de classe `GeneratedMission`. Une offre est une `MissionDefinition` avec cinq champs
de plus (`type`, `requirements`, `rewards`, `seed`, `entry_fuel_cost`), et leur **absence** est ce qui
distingue une mission ecrite a la main. Le choix vaut la peine d'etre dit : tout ce qui est en aval —
`MissionTracker`, `ZonePass`, `GameRoot.PlaceSupplies`, `CampScreen`, le harnais `Tests/MissionRun.cs`
— travaille sur une `MissionDefinition` et ne sait pas d'ou elle vient. Une seconde classe aurait
demande une seconde branche a chacun de ces endroits, et c'est la seconde branche qui aurait diverge.

`MissionDefinition.SuppliesFor(catalogue)` est la seule reponse a la question « qu'est-ce qu'on pose
dans la zone ? ». Elle est unique pour que le moteur et le harnais de test posent la meme chose.

### Trois objectifs, trois compteurs

`MissionObjectiveTracker` porte les trois mesures, et rien d'autre : il ne rend aucun verdict, c'est
toujours `MissionTracker` qui le fait. La separation tient a la duree de vie des deux questions — un
verdict est definitif, une progression fait des aller-retours. Un objet ramasse puis perdu redescend
le compteur, et c'est voulu : la mission se conclut au retour, pas au ramassage.

| Type | Ce qui est compte | Ce qui le rend acquis |
|---|---|---|
| `KillZombies` | morts enregistrees **dans la zone cible** | le compteur, plus le retour du groupe |
| `CollectObjects` | objets ramasses dans la zone **et encore portes** | le retour du groupe au camp |
| `CollectPriorityResources` | idem, pour `food`, `water`, `fuel` | le retour du groupe au camp |

Un zombie qui tombe hors de la zone est enregistre pour ne pas etre recompte, mais ne fait rien
progresser : c'est ce qui donne son sens a la zone cible.

**Pourquoi observer plutot que s'abonner.** `EntityManager.Remove` emet bien un evenement, mais
`IHorde` — l'interface que le groupe connait — ne l'expose pas, et l'y ajouter obligerait chaque
horde de test a le porter. Le tracker compare donc la horde d'une passe a l'autre : un identifiant
qui disparait est un zombie qui est tombe, et sa derniere position connue dit ou. Rien de nouveau
n'a besoin d'etre cable pour qu'une mission se deroule en test.

Un compteur ne conclut jamais a l'impossible avant d'avoir constate l'existence de ce qu'il cherche.
Sans ce garde-fou, une mission serait perdue au premier tick — avant que la horde ou les caisses ne
soient posees.

### Ce que le domaine ne recoit pas

Aucun nom de zone generee, d'objet requis ou de recompense n'entre dans le domaine de planification.
Une mission generee lui parle exactement comme une mission ecrite : par `objective_zone` et par
`item`.

D'ou une consequence assumee, et documentee ici plutot que cachee : **une elimination n'a pas de
demandeur dans le domaine**. « Tuer huit zombies au supermarche » n'est un but symbolique de personne,
alors que « il y a quelque chose a chercher la-bas » en est un depuis le livrable 5. Une offre
`KillZombies` pose donc un objet d'appel dans sa zone — c'est ce qui donne au groupe une raison d'y
aller —, et le combat reste ce qu'il a toujours ete : un reflexe (`FightZombie`) qui se declenche une
fois sur place. Faire de l'elimination un but planifie serait une decision **tactique** prise seule
par l'IA, donc du ressort du livrable 15 : c'est au joueur de dire « nettoyez cet endroit ».

### La graine d'une offre

Elle est derivee de quatre valeurs — graine de partie, jour de campagne, rang de l'offre, et
`generator_version` — melangees par le meme FNV + xorshift que `Deterministic`. Une somme donnerait
la meme graine a (jour 2, offre 0) et (jour 1, offre 1), et les offres du lendemain seraient celles
de la veille decalees d'un cran.

`generator_version` merite un mot : une graine ne promet un contenu identique **que pour un jeu de
regles donne**. Le jour ou une borne ou un poids change, l'ancienne graine ne reproduit plus rien —
et pretendre le contraire serait le pire des deux mondes. La version entre dans le calcul, et
l'incrementer dit honnetement « ce sont d'autres missions ».

Deux precautions rendent la generation independante de l'ordre d'ecriture de la configuration : les
types sont parcourus dans l'ordre de l'enumeration, jamais celui du fichier, et toute table lue dans
un dictionnaire — butin d'une zone, vivier de recompenses — est triee avant d'etre parcourue.
Deplacer une ligne dans un fichier ne doit pas changer les missions du jour.

### La difficulte n'est pas une etiquette

Elle se **calcule**, a partir de ce que la mission demande (la charge) et de la densite de zombies de
la zone, ponderees par `difficulty.zone_weight`. Tout le reste se lit par elle : temps imparti,
equipe conseillee, ambiance de quartier, table de recompenses, cout d'entree. C'est ce qui empeche
l'etiquette et le contenu de diverger — defaut que `config/missions.json` avait avant ce livrable, et
qu'un test devait surveiller a la main.

Pour une elimination, la taille de horde est calculee et non tiree : la part de la horde qui echoit a
la zone est sa densite, et on la fait croitre jusqu'a ce que le compte y soit. C'est exactement
« augmenter la densite de la zone » du livrable — et c'est ce qui garantit qu'on ne demande jamais
plus de morts que la zone n'en recevra.

### Le grand livre : `MissionRewardService`

Ce qu'une mission coute a l'aller et ce qu'elle rapporte au retour tiennent dans la meme classe,
parce que ce sont deux fois la meme garantie : un mouvement de stock ne doit se produire **qu'une
fois**. L'essence est debitee au lancement et pas a chaque clic sur l'offre ; les recompenses sont
versees au verdict de victoire et pas a chaque passe qui le relit — or `MissionTracker.Evaluate` est
appele a chaque frame, et un verdict deja rendu y repasse indefiniment.

Le service est cree **par mission jouee**, jamais une fois pour la partie : rejouer la meme mission un
autre jour la repaie, ce qui est le comportement voulu. Une defaite, un abandon ou la mort du groupe
ne versent rien, et l'essence de l'aller n'est pas rendue — le van a roule.

Une recompense que l'entrepot du camp ne peut pas prendre (`camp.storage_slots`) passe en file
d'attente et reste visible a l'ecran de camp. Elle n'est jamais supprimee en silence : le joueur a
choisi son expedition sur ce qu'elle promettait.

### Le repli

Apres `max_attempts_per_offer` tentatives infructueuses, le generateur emploie le profil de secours
ecrit dans `mission_generation.fallback` et ecrit la raison dans son journal. Ce profil est **ecrit**
et non calcule : une offre de secours calculee echouerait comme celles qu'elle remplace. Il passe par
la meme validation que les autres, et un profil de secours fautif leve — c'est la seule facon qu'un
« ne jamais afficher une mission impossible » ait un sens.

## Export PDDL et instructions tactiques

### Une representation, deux rendus

`Hordinary.Core/AI/Planning/Pddl/` n'est pas un ecrivain de fichiers `.pddl` : c'est une
**representation intermediaire** (`PddlDomain`, `PddlAction`, `PddlFormula`, `PddlConstraint`)
construite depuis `config/planner_domain.json` par `PddlDomainBuilder`, et rendue de deux facons.

```
planner_domain.json → PddlDomain → PddlWriter          → texte, pour Fast Downward et VAL (hors ligne)
                                 → PlanningDomainRenderer → PlanningDomain, pour GBFS (en jeu)
```

C'est la seule propriete qui compte ici : il n'y a pas deux compilateurs a maintenir d'accord, donc
pas de derive possible entre ce qu'un planificateur tiers valide et ce que le jeu execute. C'est
aussi ce qui fait que les contraintes du joueur changent reellement le comportement du groupe — le
domaine reecrit repasse par le second rendu, et le survivant planifie dessus.

Rien n'y est ecrit en dur. Les predicats, leur arite et leurs types sont deduits mecaniquement des
preconditions et des effets de toutes les actions activees ; les constantes sont les symboles nommes
en dur par ces memes actions. La ligne de partage entre domaine et probleme suit PDDL : un lieu
depend de la carte et de la mission, donc il appartient au probleme (`Landmarks`) ; `agent`,
`objective` ou `remedy` sont des symboles du langage, donc au domaine (`Constants`).

Un seul point demandait une decision : le joker `*`. `"!at(agent, *)"` n'a pas d'equivalent STRIPS
et se rend en effet universellement quantifie, `(forall (?l - location) (not (at agent ?l)))` —
fidele, et supporte par Fast Downward via ADL. Le rendu inverse refait le chemin.

`PddlAction.Origin` porte la couche execution (verbe, parametres de verbe, duree, bruit) que PDDL ne
decrit pas et que `PddlWriter` n'ecrit jamais. Sans elle, un domaine reecrit par une contrainte serait
un joli PDDL que le jeu ne saurait pas jouer.

### Le probleme : un instantane, pas le monde

Le domaine ne change pas d'une partie a l'autre ; un **probleme** est un instantane. `PddlProblemBuilder`
traduit le `PlanningProblem` que GBFS resout reellement — celui que `SymbolicStateBuilder` a derive de
la memoire d'**un** survivant.

Rien n'y est ajoute, et c'est tout l'interet. Un lieu jamais percu n'a pas d'objet, donc pas de fait,
donc aucun plan ne peut s'appuyer dessus — ni sous GBFS, ni sous Fast Downward. C'est ce qui rend la
corroboration honnete : le planificateur tiers ne voit pas plus que le survivant
(`Un_objet_jamais_percu_n_apparait_pas_dans_le_probleme`), et trois croyances divergentes donnent trois
`:init` distincts pour un seul domaine (`Deux_agents_produisent_deux_problemes_differents`).

**Un instantane tient en 45 a 72 lignes**, et c'est venu d'une coupe. Le probleme exportait une table
des distances entre lieux — un `(= (distance a b) n)` par paire, les deux tiers du fichier — que rien
dans le domaine ne lisait, et que rien ne pouvait lire : ecrire
`(increase (total-cost) (distance ?from ?to))` demande un parametre d'origine que
`MoveToLocation(?location)` n'a pas. La fonction et sa table sont parties, et avec elles l'illusion
que le cout exporte tenait compte du trajet.

C'est donc une **limite assumee** : le `distance_cost` de `planner_domain.json` — le cout par cellule
parcourue, que quatre schemas portent — n'est pas exprimable dans cette forme de domaine, et le
`total-cost` exporte est la part fixe seule. Comparer le cout d'un plan de Fast Downward a celui de
GBFS demande de le savoir. `Le_probleme_ne_fixe_aucune_valeur_que_personne_ne_lit` interdit a ce poids
mort de revenir, sous ce nom ou un autre : toute fonction declaree doit etre incrementee quelque part.

Les sorties versionnees vivent dans [`tools/pddl/`](../tools/pddl/README.md) : `domain.pddl` et dix
`snapshots/` preleves dans une partie jouee du depart au verdict. Des fichiers generes **et**
versionnes, ce qui est normalement une mauvaise idee — ils le sont parce que ce sont eux qu'on donne
aux outils hors ligne, et le risque de derive est couvert par `PddlArtifactsTests`, qui rend a nouveau
et compare.

Comparer le premier instantane et le dernier donne la mesure de ce qu'une mission apprend : 12 objets
et 1 action instanciee au depart, 25 objets et 105 actions a l'arrivee — avec `entrepot_ouest`,
`hangar_sud`, `rue_principale` et `ruelle`, c'est-a-dire le prerequis des batiments visible jusque dans
le PDDL.

Deux ecueils que cette capture a mis au jour, et qui depassent l'export :

* **`planner.max_time_ms` est une horloge.** Une recherche qui aboutit seule peut expirer quand la
  machine est chargee, donc la suite des plans d'une mission n'est pas reproductible sous charge. Un
  test qui compare deux deroules desserre ce plafond (`MissionRun(planner:)`), et un test de budget
  porte sur les **noeuds** — le facteur de branchement dit quelque chose du domaine, l'horloge dit
  surtout que la machine etait occupee.
* **`NoiseSystem.Localize` retombe sur `Random.Shared`** quand personne ne lui passe de generateur.
  Une mission rejouee n'est donc pas identique des qu'un bruit est entendu de loin.
  `SurvivorBand.Advance(delta, rng)` et `MissionRun(noiseSeed:)` permettent d'en passer un ; le jeu
  n'en passe toujours pas. C'est un accroc connu a l'invariant « a graine egale, resultat egal », et il
  est anterieur au livrable 15.

### Les cinq formes, et ce qu'elles deviennent

Une instruction du joueur se lit dans une grammaire fermee de cinq formes, et rien d'autre :
`require_before(P, A)`, `require_after(P, A)`, `forbid(A)`, `require_always(P)`, `penalize(A, k)`.
`A` designe un schema (`ForceDoor`) ou une **classe** de schemas par la categorie du lieu vise
(`enter:building`) — PDDL ne connait pas la notion d'« entrer », seulement celle d'emmener l'agent
quelque part.

Trois des cinq sont des modalites de trajectoire PDDL 3 (`always`, `sometime-before`,
`sometime-after`). Les deux autres n'en sont pas — `forbid` est un retrait de schema, `penalize` une
preference de cout — et le rendu declaratif le dit en commentaire plutot que de faire semblant.

Ce rendu declaratif n'est donne a **aucun** planificateur : ni GBFS ni Fast Downward ne prennent un
bloc `(:constraints …)` en entree. Ce qui leur est donne est la forme **compilee**, methode de
Gerevini & Long : la modalite temporelle devient des preconditions supplementaires, quitte a scinder
une action en deux. Une precondition PDDL est conjonctive — on ne peut pas y ecrire « si batiment
alors regroupe » — alors on ecrit « batiment **et** regroupe » d'un cote, « pas batiment » de
l'autre. L'union des deux moities couvre exactement les instances du schema d'origine, leur
intersection est vide, et le domaine reste dans le meme fragment que celui de la spec G.

La compilation est **plus stricte que la contrainte, et c'est voulu** : `sometime-before` demande le
fait a un moment quelconque avant, la forme compilee l'exige dans l'etat immediatement precedent.
Elle est donc correcte et incomplete, et l'approximation va dans le sens de l'intention.

### Quatre portillons

| | Ou | Quoi |
|---|---|---|
| 1 — grammaire | `ConstraintGrammar` | cinq formes, predicats et schemas valides **contre le domaine PDDL** |
| 2 — compilation | `DomainCompiler` | `PddlDomain` → `PddlDomain`, fonction pure |
| 3 — satisfiabilite | Fast Downward | **hors ligne** : aucun planificateur tiers ne tourne pendant une partie |
| 4 — conformite | `TrajectoryChecker` | le plan produit satisfait-il la forme *declarative* ? Il tourne **en jeu**, sur chaque plan |

Le portillon 1 ne tient aucune liste de symboles a la main : le vocabulaire est le domaine exporte.
Il n'y a donc nulle part ou oublier de le mettre a jour quand le domaine bouge — un modele qui
proposerait `require_before(barricaded(?door), enter:building)` est refuse parce que `barricaded`
n'existe pas, et il le reste sans que personne y pense.

Le portillon 4 va dans l'autre sens que le 3 : le 3 verifie le domaine compile, le 4 verifie **la
compilation elle-meme**. Une compilation fausse produit un plan valide au regard du domaine reecrit
et faux au regard de la phrase du joueur ; rien d'autre dans la chaine ne le voit. Il lit la
structure, jamais du texte PDDL, et interprete les modalites dans leur lecture **large** — un plan
qui rejoint le groupe, traverse une rue puis entre dans le batiment est accepte, alors que la forme
compilee ne l'aurait pas produit. Sans cela il ne verifierait plus la contrainte mais sa traduction,
et l'incompletude assumee deviendrait invisible.

Un refus n'est pas un echec du systeme, c'est son contenu : `InstructionLog` garde la raison et le
portillon qui l'a produite. Un systeme qui n'affiche que ses succes ne prouve rien.

### `penalize` depend de la recherche active

`penalize(A, k)` multiplie le cout de `A`. Sous GBFS la file est ordonnee sur `h` seul et le cout ne
departage que les ex aequo : la contrainte est **exacte dans le PDDL et sans effet sur le plan joue**.
Sous A*, la priorite est `g + h` et elle agit. Le compilateur accepte dans les deux cas et n'avertit
que dans le premier ; la question se lit dans `planner.search` (`PlannerSearches.UsesCost`) et nulle
part ailleurs. L'avertissement est conditionne a la recherche active, il n'est pas une propriete de
la forme — c'est ce que verifient `Un_penalize_sous_gbfs_avertit_sans_refuser` et
`Un_penalize_sous_astar_agit_et_n_avertit_pas`.

### Sans modele de langage

`instructions.enabled` est livre a `false` : aucune requete n'est emise et les huit instructions
pre-ecrites de [`config/instruction_presets.json`](config/instruction_presets.json) restent
proposees. Ce qui est desactive est la **traduction** d'une phrase libre en contrainte, pas les
contraintes : les portillons, le compilateur et le verificateur tournent exactement pareil dans les
deux cas, et `ConstraintGrammar` ne fait aucune difference entre les deux sources.

Retirer toutes les instructions rend le jeu bit a bit identique a ce qu'il etait : le domaine source
n'est jamais modifie et le domaine compile repart de lui a chaque changement.

### Un vocabulaire de lieux commun au joueur et au planificateur

C'etait le prerequis bloquant du livrable, et il vaut **meme sans lui**. Jusqu'ici, seul le batiment
vise par la mission existait pour le planificateur, sous le nom `objective_zone` ; l'autre n'etait
rien. « Avant d'entrer dans un batiment » n'avait donc rien sur quoi se poser.

Trois changements, bornes :

* **La perception nomme les lieux.** `PerceptionSystem.RememberZones` : apercevoir un bout
  d'entrepot, c'est savoir qu'il y a un entrepot la. Seules les categories dont la carte compte
  plusieurs exemplaires passent par la (`ZoneKinds.IsCommon` — Building, Street, Alley) ; la
  pharmacie, le camp et l'extraction sont annonces au briefing et se reconnaissent au sol, et leur
  souvenir ne doit pas se deplacer a chaque coup d'oeil. Le premier regard fait foi, sauf quand il
  n'avait vu qu'une facade : une case praticable du meme lieu la remplace, parce que c'est le moment
  ou l'on apprend par ou entrer.
* **Un nom de zone devient un symbole.** `SymbolicStateBuilder.ObjectNameOf` : « Entrepot ouest » →
  `entrepot_ouest`. Les accents tombent et les espaces deviennent des soulignes, parce que ce nom
  traverse le PDDL ou un symbole est un mot sans ponctuation.
* **Chaque lieu porte sa categorie.** `building(entrepot_ouest)`, `street(rue_principale)`,
  `alley(ruelle)` — et `building(objective_zone)`, sans quoi l'instruction vaudrait pour tous les
  batiments sauf celui qu'on envoie fouiller. Ce sont les entrees de `places.kinds`, declarees dans
  `planner_domain.json` et posees ici : **aucune action ne les produit**, c'est le moteur qui les
  emet, comme il emet `known`.

Pourquoi un predicat et pas un sous-type PDDL : le typage est une hierarchie de subsomption **sans
negation**, et il n'existe aucun type « location qui n'est pas un building ». La moitie residuelle du
schema scinde a besoin de `(not (building ?loc))` de toute facon, donc du predicat — autant n'avoir
qu'un seul mecanisme a tester.

Deux effets de bord, tous deux traites :

* **Une rue n'a pas de porte.** Rattacher une ouverture a une rue ferait passer un quartier entier
  pour une piece verrouillee des qu'un survivant apercevrait une porte close depuis le trottoir, et
  `Relocate` ramenerait la rue a ce pas de porte. Les lieux `IsOpenAir()` sont donc exclus du
  rattachement des ouvertures — `Une_rue_n_est_jamais_fermee_par_une_ouverture`.
* **Le facteur de branchement augmente.** Plus d'objets `location`, donc plus d'instances de
  `MoveToLocation`. La specification demande de verifier que `planner.max_nodes` et
  `planner.max_time_ms` tiennent encore ; la reponse vient d'une mesure sur un **quartier genere**,
  le cas defavorable, et non sur la carte dessinee :
  `Nommer_tous_les_lieux_ne_fait_pas_deborder_les_budgets_du_planificateur`. Aucune recherche ne
  sature son budget. Si un jour l'une le faisait, ce serait un chiffre a relever dans
  `config/game_config.json`, jamais en dur.

### Ce qu'une contrainte fait a une mission

Une instruction ne donne **aucun ordre**. Elle change le repertoire, et le survivant decide comme
avant — sauf que le plan qu'on ne voulait pas n'existe plus.

```
config/planner_domain.json   (jamais modifie)
        │
        ▼
   PddlDomain  ──► TacticalInstructions.Add(texte)  ──► portillons 1 et 2
        │                     │
        │                     ▼
        │              PddlDomain compile ──► PlanningDomain ──► SurvivorBand.Instruct
        │                     │                                        │
        │                     ▼                                        ▼
        │        missions/<id>/domain.constrained.pddl        SurvivorAgent.Rebase
        │        missions/<id>/domain.declared.pddl           (puis replanification)
        ▼
   tools/pddl/domain.pddl     (jamais modifie)
```

* **Le groupe porte les contraintes, pas l'agent.** `SurvivorBand.Instructions` : une instruction
  s'applique aux trois, et a ceux qui s'enrolent apres elle. Des instructions individuelles
  demanderaient un domaine compile par agent — possible, hors perimetre.
* **Poser une contrainte fait replanifier.** Sans cela le groupe continuerait de jouer le plan que la
  consigne vient d'invalider, et le joueur verrait sa regle ignoree pendant une action entiere.
* **Tout retirer rend le repertoire d'origine**, et non le dernier domaine reecrit : `Instruct(null)`
  repart de la source que le groupe a retenue au premier enrolement.
* **Le portillon 4 tourne en jeu**, sur chaque plan produit (`Brain.Replanned` →
  `SurvivorAgent.VerifyTrajectory`). Une violation est un defaut du **compilateur**, pas du
  survivant : elle sort sur un canal qui ne se coupe jamais (`Trace.Error`) plutot que de faire
  tomber le plan — un plan refuse la laisserait le groupe sans plan du tout.

`GameRoot` ne decide de rien ici non plus : il compile **apres la carte et avant le groupe**, parce
que « aucun batiment ici » ne se sait qu'une fois le quartier tire. Les contraintes ont deja ete
acceptees au camp, par ces memes portillons ; on recommence sur le terrain reel parce que le
portillon 2 n'y dit pas la meme chose. Un refus a ce stade ne casse pas la mission : la contrainte
tombe, la raison part au journal, le groupe part avec ce qui reste.

Le but courant n'est pas donne au compilateur. Un survivant en choisit un par seconde et par tete, et
figer celui du depart ferait refuser une contrainte au nom d'un objectif que personne ne poursuit
encore ; c'est le portillon 3, hors ligne, qui pose cette question-la.

### Le domaine compile d'une mission, sur disque

`config/planner_domain.json` et le `tools/pddl/domain.pddl` versionne ne bougent **jamais**. La
compilation ecrit, pour la duree de la mission, `missions/<id>/` — genere, non versionne, relu par
personne :

| Fichier | Pour qui |
|---|---|
| `domain.constrained.pddl` | le portillon 3. Le domaine reecrit, **sans** bloc `(:constraints …)`, dans le meme fragment que la spec G — Fast Downward ne prend pas les contraintes de trajectoire PDDL 3 en entree. |
| `domain.declared.pddl` | VAL, et le lecteur. Le meme domaine **avec** le bloc PDDL 3 : ce que le joueur voulait, plutot que ce que la compilation en a fait. |

Les deux portent la phrase du joueur en commentaire de tete, parce qu'un fichier doit pouvoir
s'ouvrir seul. Sans contrainte active, rien n'est ecrit : un domaine que personne n'a reecrit vit
deja dans `tools/pddl/`.

### L'interface

`Scripts/UI/InstructionPanel.cs`, a l'ecran de camp. Il ne compile rien lui-meme : il appelle
`TacticalInstructions` et affiche ce qui en revient — la contrainte acceptee, ses avertissements, ou
le refus **avec le portillon qui l'a produit et sa raison**. Les refus s'affichent au meme endroit
que les acceptations : un systeme qui ne montre que ses succes ne prouve rien.

Les instructions pre-ecrites sont des cases a cocher, chacune montrant a cote la contrainte qu'elle
vaut — c'est elle qui est appliquee, il n'y a rien a cacher. La saisie libre attend une contrainte de
la **grammaire fermee**, pas du francais : traduire une phrase est le travail du service de langage,
et lui seul. Pendant la mission, le HUD rappelle les consignes actives : une contrainte n'est visible
nulle part ailleurs sur la carte — elle n'ajoute aucun ordre, elle retire des plans — et un
comportement qu'on ne sait plus avoir demande passe pour un defaut.

### Limites assumees

* Le portillon 3 n'est pas cable : Fast Downward et VAL restent des outils **hors ligne**, et ne sont
  installes sur aucune machine du projet. Aucun planificateur tiers ne tourne pendant une partie, et
  le refus par preuve d'insatisfiabilite — le meilleur moment de demonstration du livrable — n'existe
  donc pas encore en jeu. `tools/pddl/results.md`, qui doit porter le tableau des dix instantanes
  (plan FD, plan GBFS, ecart), attend de les avoir lances.
* **`ally` est a la fois un type et un objet** dans les instantanes : le domaine declare
  `(:types ally …)` et le repere « l'allie le plus proche » s'appelle `ally`. Les deux vivent dans des
  espaces de noms distincts au sens de la grammaire PDDL, et la plupart des analyseurs l'acceptent ;
  certains s'en plaignent. Le mecanisme pour le corriger existe deja —
  `SymbolicStateBuilder.LocationAliases` renomme `objective` en `objective_spot` pour exactement cette
  raison — mais le faire change le vocabulaire du domaine livre, alors on attend de savoir si c'est
  necessaire plutot que de le changer a l'aveugle.
* La traduction d'une phrase libre en contrainte (service FastAPI/LangGraph/OpenRouter, et le
  `InstructionClient.cs` qui l'appellerait) n'est pas faite. `instructions.enabled` est livre a
  `false`, et tout le reste du livrable fonctionne sans.
* Les contraintes sont **collectives** et ne sont pas figees dans une sauvegarde de mission : rejouer
  une mission demande de reposer les memes instructions.
* La compilation reste **correcte et non complete** : la lecture « immediatement avant » est plus
  stricte que le `sometime-before` de PDDL 3. Le verificateur de trajectoire, lui, lit la modalite au
  sens large — sans quoi il ne verifierait plus la contrainte mais sa traduction.

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

Le decor suit, et il ne l'a pas toujours fait : le terrain changeait dans `GridMap` sans que rien ne
repeigne la case, et le joueur voyait un survivant franchir une porte **dessinee fermee**. C'etait le
seul endroit ou l'ecran contredisait la simulation. `GridMap.CellChanged` annonce maintenant les cases
dont le terrain change — et seulement celles-la, sans quoi une generation de quartier en emettrait des
milliers — et `MapPainter.ShowCell` repeint la case annoncee. Le sol, lui, ne bouge pas : c'est
justement celui qu'une porte ouverte doit laisser voir.

## Sols

Le quartier declarait quatre sols exterieurs et n'en peignait qu'un. `TileLegend.TileFor` rend
toujours la **meme** tuile pour un terrain donne — la plus petite cle — et une carte entiere se
rendait donc en bitume : ni trottoir, ni herbe, ni les deux carrelages d'interieur.

Le choix se fait maintenant dans `MapPainter.FloorAt`, et il se lit sur la carte elle-meme plutot que
sur un tirage :

| Case | Sol |
|---|---|
| dans une zone `Street` ou `Alley` | `FloorRoad` — la chaussee |
| exterieure, touchant une voie | `FloorSidewalk` — le trottoir |
| exterieure, ailleurs | `FloorGrass` — la friche |
| interieure | une variante de `FloorIndoor` par **batiment**, choisie sur le nom de la zone |

Les voies n'ont demande aucune donnee nouvelle : la passe 5 du generateur enregistre deja chaque rue
et chaque ruelle comme une `MapZone`, et une carte relue depuis un export se repeint donc a
l'identique.

Trois `TileKind` s'ajoutent (`FloorRoad`, `FloorSidewalk`, `FloorGrass`) et ne changent rien au jeu :
comme `FloorOutdoor`, ils donnent un `CellType.Floor` exterieur, et la navigation ne les distingue
pas. Ils n'existent que pour que le peintre puisse **demander** une image au lieu de prendre la
premiere venue.

Le sol d'un interieur est choisi par zone et non par case : un carrelage tire case par case ferait un
damier, pas une piece. Le nom de la zone est stable, donc deux lancements peignent le meme quartier de
la meme facon — et la somme des caracteres remplace `string.GetHashCode`, randomise par processus
depuis .NET Core.

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
* Cinq objectifs de la specification ne sont plus livres : `HelpAlly`, `HealAlly`, `CalmAlly`,
  `Regroup` et `CreateDistraction`, retires au livrable 15. Porter secours n'est pas un reflexe, c'est
  une strategie, et les strategies viennent du joueur — « Regroupez-vous » reste un ordre direct,
  touche 5. La diversion est partie pour la meme raison, en un peu plus tranche : faire du bruit
  expres pour attirer la horde sur soi et degager un allie est un sacrifice calcule, decide en sachant
  qui est ou et ce qu'on protege. Personne ne fait cela sans qu'on le lui demande. Les deux actions
  qui la servaient — `CreateNoiseDistraction` et `ThrowFirecracker` — et le fait `distraction_created`
  ont ete retires avec elle ; `ally_danger` reste au vocabulaire, sans lecteur. Le catalogue etant une
  donnee, le test verifie que ceux qui restent sont toujours la, pas qu'il n'y en a pas d'autres.
* L'action `ShareKnowledge` existe et fonctionne, mais aucun objectif Utility AI ne la declenche :
  le partage passif a portee de voix suffit au critere du livrable, et un objectif « aller raconter »
  ferait passer au groupe plus de temps a discuter qu'a survivre. Le but symbolique est declare dans
  `planner_domain.json` pour qui voudrait l'activer.
* Cinq actions du domaine sont inertes — `Observe`, `Wait`, `UseItem`, `PushHeavyObject`,
  `BarricadeDoor` — parce qu'aucun but ne lit leurs effets ; `DropItem` n'est guere mieux lotie. Elles
  restent livrees, mais tant que rien ne les reclame, `objective_collected`, `observed`, `waited`,
  `used`, `pushed`, `door_barricaded`, `heavy_at` et `mass_ok` sont des predicats orphelins, et
  l'export PDDL de la spec G n'aura rien a en faire. Voir « Ce que le domaine sait faire, et ce qui y
  dort ».
* Une intention annoncee est un point, pas une zone : « je vais en (12,3) » ne dit rien de
  l'itineraire. Deux survivants peuvent donc encore se suivre sur la moitie du trajet avant de
  diverger.
* Un survivant ne se bat que sur ce qui est deja sur lui : `zombie_adjacent` et `zombie_in_range` sont
  les seules preconditions de combat, et **aucune action ne permet de rejoindre une menace**. On ne
  planifie pas une chasse. La contrepartie est qu'un zombie isole et lent, a quatre cases, n'est
  jamais traite — il est evite, ou il suit.
* Le score de combat lit `pursued`, pas le nombre d'adversaires : `perceived_danger` fond tout en un
  scalaire, et « un seul zombie » ne se distingue pas de « trois zombies deux fois plus loin ».
* GBFS n'est pas optimal, et c'est la specification qui le veut : le cout ne s'ajoute pas a
  l'estimation, il ne fait que departager les ex aequo. Un plan un peu plus long ou un peu plus
  bruyant que le meilleur possible est donc normal. La ou le cout decide vraiment, c'est entre deux
  facons d'atteindre le meme but — frapper ou repousser — parce que l'heuristique les note pareil.
  `planner.search: "astar"` met le cout dans la cle, mais n'achete pas l'optimalite pour autant :
  l'heuristique enrichie n'est pas admissible.
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
* La diversion n'est plus dans les mains de personne. `CreateDistraction`, `CreateNoiseDistraction` et
  `ThrowFirecracker` ont ete retires ; le verbe `emitNoise` et `WorldPlanRuntime.ThrowNoise` restent
  executables, et la distraction manuelle (`Espace`, sous `debug.commands`) reste le seul moyen de
  viser une case. C'est un manque assume jusqu'au livrable 15, pas une regression : ce qui manque est
  un demandeur, et il doit etre le joueur.
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
