# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

Le dépôt est **entièrement en français** — commentaires, documentation, noms de tests, journaux.
Écrire dans la même langue.

## Commandes

```bash
dotnet build Hordinary.sln
dotnet test Tests/Hordinary.Tests.csproj

# Un test seul, ou une classe
dotnet test Tests/Hordinary.Tests.csproj --filter "FullyQualifiedName~MemoryTests.Une_pharmacie_vide_finit_par_etre_abandonnee"
dotnet test Tests/Hordinary.Tests.csproj --filter "FullyQualifiedName~PlannerTests"
```

Le service de langage a sa propre suite, en Python, qui ne demande ni reseau ni cle (eprouvee sur
CPython 3.12.10). Elle ne fait pas partie de `dotnet test` et se lance a part :

```bash
cd service
python -m venv .venv && ./.venv/Scripts/python.exe -m pip install -r requirements.txt
./.venv/Scripts/python.exe -m pytest tests/ -q          # 89 tests
./.venv/Scripts/python.exe -m evals.run --dry-run       # le harnais d'evaluation, sans reseau
```

Le jeu s'ouvre dans **Godot 4.7** sur `Scenes/Main.tscn`. Sans éditeur, un smoke test headless :

```powershell
& "C:\Users\rapha\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe" `
  --headless --path "C:\Users\rapha\Personal Projects\hordinary" --quit-after 20000
```

`--quit-after` compte des frames (~60/s) : 4000 pour un smoke test, 20000 pour voir la horde arriver.
**Le headless n'est pas optionnel** après une modification de perception, mémoire ou décision : les
bugs de comportement (zombie hors champ, oscillation d'objectifs, refuge mal choisi) ne sont attrapés
par aucun test unitaire.

Le SDK installé est .NET 10 : `Hordinary.csproj` cible net8.0 avec `RollForward=LatestMajor`, les tests
ciblent net10.0 directement.

## Architecture

Trois projets, et une frontière qui est le cœur du projet :

| Projet | Rôle |
|---|---|
| `Hordinary.Core/` | Toute la simulation et l'IA. **Zéro référence à Godot.** |
| `Scripts/` (`Hordinary.csproj`) | Couche moteur : affichage, entrées, cadences. Ne décide de rien. |
| `Tests/` | xUnit, sans moteur, sur `Hordinary.Core`. |

`Scripts/World/MapLoader.cs` est le **seul** point de contact entre le moteur et la simulation : il
convertit les `TileMapLayer` en `GridMap`. Tout le reste de l'IA travaille sur `GridMap` et ignore
l'existence de Godot.

Le câblage d'un survivant — carte mentale, A*, Utility AI, GBFS, exécuteur, moniteur de
replanification — vit dans `Hordinary.Core/Agents/SurvivorAgent.cs` et `SurvivorBand.cs`, **pas** dans
`GameRoot`. C'est délibéré : `Scripts/Game/GameRoot.cs` et le harnais `Tests/MissionRun.cs` déroulent
le même code, pas deux copies qui divergent. Toute logique de décision ajoutée à `GameRoot` est au
mauvais endroit.

### La boucle

```
Perception → mise à jour des croyances → partage → choix Utility AI → planification GBFS
→ navigation A* → exécution → échec ou découverte → replanification
```

Chaque étape a son dossier dans `Hordinary.Core/` (`World/`, `Agents/`, `AI/Utility/`, `AI/Planning/`,
`AI/Navigation/`, `AI/Replanning/`, `AI/Zombies/`). `docs/architecture.md` détaille chaque système,
ses choix de conception et ses limites assumées — le lire avant de toucher à l'un d'eux.

### Invariants non négociables

Ces trois règles sont vérifiées par des tests (dont plusieurs par réflexion). Les casser casse le
propos du projet :

1. **`Hordinary.Core` ne référence ni Godot, ni un client HTTP.** C'est ce qui rend tout testable
   sans lancer le jeu.
2. **Aucun système ne lit le `WorldState` à la place d'un agent.** `AgentKnowledge` n'a aucune
   référence vers `WorldState` et ne reçoit que l'*étendue* du terrain, jamais son contenu. Seul
   `PerceptionSystem` franchit la frontière : il lit le monde et n'écrit que dans la mémoire de
   l'agent qui observe. Même règle pour les zombies (`ZombieStateMachine` ne lit que leur mémoire ;
   `HordeBehaviour` reçoit la carte réelle, uniquement pour savoir si un corps passe).
3. **À graine égale, résultat égal** — horde placée, quartier généré, mission rejouée.

### Rien n'est codé en dur

Les valeurs de gameplay vivent dans `config/` et nulle part ailleurs :

* `game_config.json` — navigation, perception, bruit, survivant, zombies, utility_ai, planner,
  replanning, knowledge, exploration, inventory, mission, mission_generation, instructions, debug ;
* `utility_goals.json`, `planner_domain.json`, `missions.json`, `survivors.json`, `items.json`,
  `weapons.json`, `map_locations.json`, `tile_legend.json`, `zones.json`, `camp.json`, `mapgen.json`,
  `instruction_presets.json`.

**Il n'existe pas de classe par action.** Les actions du planificateur sont des *instances* de huit
verbes primitifs (`ActionVerb` : `ToPos`, `Take`, `Put`, `SetFlag`, `Wait`, `Damage`, `Push`,
`EmitNoise`) décrites dans `planner_domain.json`. Ajouter une action au jeu se fait dans le JSON —
sauf si elle demande un nouveau prédicat, auquel cas il faut aussi une branche dans le `switch` de
`WorldPlanRuntime.SetFlag`. **Attention au défaut `_ => true` de ce switch** : un prédicat inconnu
fait *réussir l'action en silence sans rien changer au monde*.

De même, un fait symbolique qu'aucune action ne lit est un prédicat orphelin : le retirer de
`SymbolicStateBuilder` en même temps que l'action qui l'utilisait.

### Les tests lisent les vrais fichiers livrés

`Tests/DemoMap.cs` décode `Scenes/Main.tscn` sans Godot et charge les fichiers de `config/`
directement — **pas des copies**. Un objet retiré du catalogue, un objectif retiré de
`utility_goals.json` ou une tuile déplacée sur la carte doit faire échouer les tests qui s'en servent :
c'est l'intérêt. `Tests/MissionRun.cs` déroule la boucle complète (`team: true` enrôle les trois
survivants).

## Conventions

* Les commentaires XML expliquent **pourquoi** une conception a été retenue, souvent avec l'alternative
  rejetée et la raison. Les respecter : ils portent des décisions, pas de la paraphrase de code.
* Les noms de tests sont des phrases françaises décrivant le comportement attendu
  (`Un_zombie_memorise_rend_le_lieu_moins_attirant`).
* Les journaux passent par `Scripts/Game/Trace.cs`, jamais `GD.Print` directement. **Le crochet
  initial d'une ligne est son nom de canal** (`[Plan]`, `[Replan]`, `[Bruit]`…) et
  `debug.console_channels` dit lesquels sortent. `Trace.Error` ne se coupe jamais.
* `_work/` est de l'outillage jetable, exclu du build (un `.cs` qui y traîne casserait la compilation
  du projet Godot via le glob du SDK).

## Règle de conception : autonomie contre stratégie

**Un réflexe reste au survivant, une stratégie revient au joueur.** Le livrable 15 fait du joueur
celui qui définit les stratégies ; tout objectif ou action représentant une décision *tactique* prise
seule par l'IA n'a pas sa place. Avant d'ajouter ou de retirer un objectif d'utilité ou une action du
domaine, se demander : « est-ce que le survivant ferait ça sans qu'on le lui dise ? » Si non, ça
appartient au joueur, comme instruction tactique.

C'est ce qui a fait retirer `HelpAlly`, `HealAlly`, `CalmAlly`, le *but d'utilité* `Regroup` et
l'objectif `CreateDistraction` — et garder `ShareKnowledge` (dire ce qu'on a vu n'est pas porter
secours), les réflexes personnels (fuir, se cacher, se soigner, se battre) et l'*ordre joueur*
`PlayerCommandKind.Regroup`, à ne pas confondre avec le but d'utilité du même nom.

Retirer un demandeur ne veut pas dire retirer le geste : le moteur garde de quoi barricader une porte
sans que rien ne le lui demande. C'est la forme normale d'un geste en attente du livrable 15, pas du
code mort. **Cinq gestes d'entraide sont revenus sous cette forme** — `ThrowFirecracker`,
`BorrowWeapon`, `TendAlly`, `OrderAlly`, `OrderBand` : ils posent `diversion_made`, `armed`,
`band_tended`, `ordered(lieu)`, `band_ordered(lieu)`, qu'aucune précondition et aucun but ne réclament, donc aucun survivant livré à
lui-même ne les fait. C'est une
consigne du joueur (`require_before(diversion_made, enter:building)`…) qui leur donne un demandeur —
et `Tests/CooperationTests.cs` vérifie les deux moitiés de la règle sur les fichiers livrés.

## Spécifications

`Specs/00-INDEX.md` est la seule spécification d'ensemble. Les livrables 1 à 8 sont le MVP et leurs
critères d'acceptation (`Specs/D-qualite-et-definition-of-done.md`) **ne se négocient pas** : une
extension qui les casserait est à retirer. Les livrables 11 à 15 sont post-MVP et chacun doit rester
retirable sans casser le MVP.

Ordre de réalisation (≠ ordre de numérotation) : `11 → 12 → 16 → G → 15 → 13`. Le livrable 14 est
retiré, le 16 est fait.

## État actuel

Branche `main` : l'épuration du projet autour du livrable 15 y est fusionnée. Sur l'arbre
de travail actuel, `dotnet test` donne **685 passés sur 685** (sous `planner.search: gbfs`, le réglage livré). Deux d'entre eux exigent que `instructions.enabled`
reste à `false` dans le fichier livré : pour essayer le service de langage, lancer le jeu avec
`-- --ia` ou `HORDINARY_IA=1` (`InstructionSettings.LocallyEnabled`), jamais en écrivant `true` dans
un fichier versionné. **Toute ligne rouge est donc une
régression** — il n'y a plus de liste d'exceptions à consulter avant de conclure, et c'est le principal
acquis de cette passe : une suite qui se lit avec une table d'exceptions en prose ne détecte plus rien.

`planner.search` est revenu à `gbfs` dans `config/game_config.json`, qui est ce que le livrable 5 exige
du jeu livré ; `astar` reste disponible, c'est un réglage et non une branche de code.

Les huit échecs que l'épuration traînait sont fermés, et ils tenaient à deux causes, pas à huit :

* **le lieu de la mission ne s'appelle plus `pharmacy`.** Il porte un nom unique, `objective_zone`, et
  `pharmacy` n'en est plus que la catégorie (`SymbolicStateBuilder.IsMergedIntoObjective` :
  un endroit ne reçoit qu'un objet, sans quoi la porte se rattache à l'un des jumeaux et l'autre passe
  pour ouvert). `LocateObjective` et `PickUpObjective` citent d'ailleurs `objective_zone` en
  **constante** dans `planner_domain.json`, et non par un `?location`. Six tests interrogeaient encore
  l'ancien nom — dont `Une_porte_verrouillee_connue_rend_le_lieu_inaccessible`, qui vérifiait
  `!accessible(pharmacy)` sur un symbole disparu, donc **à vide** ;
* **les trois survivants n'exploraient plus séparément.** Voir le paragraphe suivant.

**La répartition du groupe ne tient plus à la personnalité.** `SurvivorBand.Think` fait décider les
survivants chacun à leur tour et intercale une annonce (`KnowledgeSharing.Announce`,
`AgentKnowledge.LearnIntentFrom`) : dire « je m'occupe du nord » à quelqu'un qui se tient à côté de soi
est instantané, alors que le reste de ce qu'on se raconte attend la passe d'une fois par seconde.
Sans cet intercalage, les trois décidaient depuis le même instantané d'intentions périmées et
choisissaient la même frontière — la pénalité de duplication de `FrontierExplorer` s'appliquant
identiquement aux trois, elle les repoussait **ensemble** d'un coin à l'autre au lieu de les séparer.
Mesuré sur 601 ticks : jamais sur trois cases distinctes.

Ce que l'épuration avait retiré sans le voir, c'est la dissymétrie : chaque survivant avait sa
personnalité, donc ses propres coûts A* (`NavigationProfile.FromCaution`) et ses propres scores
d'utilité. `config/utility_goals.json` attribue pourtant la répartition à la seule pénalité de
duplication (« c'est ce dernier terme, et lui seul ») — c'est désormais vrai. Rien n'est tiré au sort :
c'est l'ordre du groupe qui départage, donc à graine égale le résultat reste égal.

**La boucle de camp est fermée.** Une mission finie ne fige plus la partie : le verdict rendu, la
carte se laisse regarder `mission.debrief_seconds`, puis la journée se résout (`CampStock.ResolveDay`),
la scène se vide et l'écran de camp rouvre avec le verdict en tête et les offres du lendemain.
`GameRoot.AddMissionChild` tient le registre de ce qui appartient à la mission (et non à la partie) ;
les couches de tuiles sont recopiées avant toute peinture, sans quoi la « carte dessinée » de la
mission suivante serait le quartier généré de la précédente. Limite assumée : le plafond de santé de
la disette ne survit pas au retour, faute d'état de survivant persistant d'une mission à l'autre.

Le message du retour est un **bilan** (`Hordinary.Core/Game/MissionReport.cs`), pas trois lignes
assemblées dans `GameRoot` : qui est au van, dehors ou hors de combat (et sa santé), la durée contre
le temps imparti, ce qui a été rapporté, le **prêt non rendu**, ce que l'entrepôt plein a refusé, le
compte du portillon 4 (plans vérifiés sous consigne, écarts) et l'autonomie du camp en jours
(`CampStock.DaysOf`). Il se dresse en trois temps — au verdict, au règlement des sacs, à la journée —
et le prêt se compte **au groupe** : une arme ramassée sur un camarade tombé et rapportée est rendue.
`GameRoot` ne fait que remplir ce qu'il est seul à voir (ce que `Deposit` et `Recover` ont rangé) ;
toute soustraction est dans le cœur, sous `Tests/MissionReportTests.cs`. Le bilan part aussi sur le
canal `[Camp]`. Une mission du camp ne l'atteint pas en headless — elle s'arrête à l'écran de camp,
faute de clic —, une prise de démonstration si (paragraphe suivant).

**Le mode démonstration** sert à tourner des clips : `demo.enabled` (ou `-- --demo` en ligne de
commande) remplace l'écran de camp par un choix de **grille** et de **scénario d'essai**, avec une case
pour jouer la prise avec ou sans ses consignes. Quatre grilles et vingt-quatre scénarios — cinq de
consigne, sept d'action (effraction, combat armé, sans arme, fuite, soin, partage, rappel), sept de
**coopération** (paragraphe suivant), quatre de **stratégies combinées** (plus bas) et une **carte
blanche** (plus bas encore) — vivent dans
`config/demo_scenarios.json`, dont l'en-tête décrit le format. `DemoGrid.Build` fait d'une grille un
`GeneratedDistrict` : le jeu la joue par `GameRoot.UseDistrict`, et une prise partage
`GameRoot.Launch` avec une mission du camp. Une prise ne consomme rien — ni péage, ni entrepôt, ni
journée. `-- --demo=<scenario>` la lance sans clic, donc **en headless**, `-- --sans-consignes` la
joue sans ses consignes, `-- --consigne=<contrainte>` (répétable) donne les siennes à une carte
blanche, `-- --observateur` ouvre pour la session les touches `F1` à `F7` et les panneaux de texte que
le fichier livré ferme (`DebugSettings.LocallyObserved`, comme `--ia` pour le service), et
`-- --quitter` ferme le jeu après le bilan — de quoi dérouler les vingt-quatre à la suite
(`tools/demo/prises_headless.sh` le fait, contrastes compris) :

```powershell
& "C:\Users\rapha\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe" `
  --headless --path "C:\Users\rapha\Personal Projects\hordinary" --fixed-fps 60 --quit-after 40000 `
  -- --demo=aucune_effraction --quitter
```

**`--fixed-fps 60` n'est pas optionnel en headless** : sans lui, Godot tourne sans synchronisation,
une frame dure environ une milliseconde et `--quit-after` ne dit plus rien du temps de jeu —
30 000 frames ont couvert 32 s de partie, et trois prises « sans bilan » n'étaient qu'un budget de
frames épuisé. Avec, le jeu joue exactement la frame du harnais. **`dotnet test` ne reconstruit pas
le jeu** : après une retouche du cœur, `dotnet build Hordinary.sln` avant tout headless, sans quoi
Godot joue l'ancien `Hordinary.Core` avec le `config/` neuf — et un prédicat que l'ancien
`WorldPlanRuntime.SetFlag` ne connaît pas y réussit en silence (`_ => true`). C'est arrivé à
`band_ordered` : « PRISE CONFORME », et personne n'avait été rappelé. Le 25 septembre 2026, les vingt
prises et les onze contrastes sont sortis « PRISE CONFORME » du moteur, sans une erreur, et la carte
blanche « PRISE LIBRE » sous deux consignes du joueur ; le 28, les combinaisons et leurs contrastes
aussi, sous `astar`. Rejouée le même jour sous `gbfs`, le réglage livré, la série entière — vingt-quatre
prises, quinze contrastes, deux cartes blanches — sort conforme et sans erreur ; seul un chiffre bouge,
`fouiller_puis_rappeler` gagnant à 94 s et non plus à 32. Le tournage des clips a son guide : `docs/enregistrer-la-demo.md`.

**Les stratégies combinées** (`DemoFocus.Combination`, « [combinaison] » à l'écran) empilent deux
consignes — `Validate` refuse une combinaison à une seule : `armer_puis_diversion`,
`soigner_et_armer`, `fouiller_puis_rappeler` et `sans_effraction_mais_arme`
(une interdiction et une exigence). Une cinquième, `grande_manoeuvre` (soin, arme et diversion), a
été **retirée** : elle a montré la limite du moteur telle quelle — **une consigne est collective, un
plan est individuel**. Un survivant n'a de plan que s'il remplit seul les exigences qu'il ne trouve
pas déjà vraies ; avec le paquetage de la carte blanche, sous les trois consignes, personne ne
bougeait, et aucun ne lance un pétard qui ne servirait qu'au plan d'un autre. Donner tout à Claire la
faisait démarrer, et elle est sortie conforme du moteur sous `astar` ; sous `gbfs`, le réglage livré,
le moteur la perd (Bob mordu à mort pendant le soin, Claire acculée loin du van) pendant que le
harnais la gagne — un écart qui reste ouvert. Coordonner des rôles serait de la planification
multi-agents. Détail dans `docs/architecture.md`, « Stratégies combinées ».

**Une prise ne joue pas le plafond de temps d'une partie.** `planner.max_time_ms` (50 ms, livrable 5)
est une horloge ; sous trois consignes, une recherche le dépassait déjà, et le plan dépendait de la
vitesse de la machine. `Tests/DemoRun.cs` le desserrait à 60 s pour lui seul, en supposant que le jeu
ne le toucherait jamais — il donnait donc raison à des prises que le moteur ratait.
`demo.planner_max_time_ms` (2 000 ms) est désormais lu par les deux (`DemoSettings.PlannerFor`) ; les
missions du camp gardent leurs 50 ms, et le plafond de nœuds reste la borne.

**La carte blanche** (`carte_blanche`, `DemoFocus.Strategy`) est la prise où le joueur donne ses
propres stratégies : l'écran de démonstration y remplace la case « consignes » par le même
`InstructionPanel` qu'au camp — du français traduit par le service de langage si
`instructions.enabled` (ou `-- --ia`), la grammaire fermée et les consignes pré-écrites sinon. Le fichier n'écrit que
l'attente du groupe **livré à lui-même** ; une prise jouée sous consignes du joueur n'en a pas, et son
bilan dit « PRISE LIBRE » avec le déroulé et chaque consigne (phrase et contrainte). Bob y part blessé
pour que les six consignes d'entraide aient chacune de quoi agir, et
`La_carte_blanche_joue_la_strategie_que_le_joueur_lui_donne` le vérifie. `--consigne=` ne passe pas par
le service : une prise headless se rejoue sans réseau.

Chaque scénario écrit ce qu'il montre (`expect` : `shows` et `never`, avec et sans consignes). Le jeu
l'affiche au bilan (« PRISE CONFORME » ou l'écart), et `Tests/DemoScenarioTests.cs` l'exige des
vingt-quatre — plus le contraste des quinze scénarios qui portent une consigne — par `Tests/DemoRun.cs`, qui
déroule un scénario
**horde comprise**, ce que `MissionRun` ne fait pas, **jusqu'au bilan** et **à la cadence du jeu** :
frame de 1/60 s, perception et zombies à leurs propres intervalles. C'est la leçon du mode. Un premier
harnais au pas de 0,1 s donnait raison aux douze pendant que le moteur en ratait six ; une horde fait
d'un combat une affaire de dixièmes de seconde, et les scénarios à zombies ont été retenus parmi les
placements qui tiennent de 30 à 75 images par seconde. Les grilles sont sensibles aussi : un couloir
ajouté a suffi à changer l'issue de deux scénarios. Toute retouche se valide en relançant cette
classe, puis la série headless.

**La coopération entre survivants** a son propre jeu de scénarios, sur une grille à elle (« La
supérette »). Six consignes pré-écrites de plus (`config/instruction_presets.json`) en sont le cœur,
et chacune compose une stratégie que le joueur peut désormais donner :

| Scénario | Consigne | Ce qui se passe |
|---|---|---|
| `diversion` | `require_before(diversion_made, enter:building)` | Bob lance son pétard du côté des zombies et loin de la porte ; Alice, qui n'a rien à lancer, attend le fracas — c'est lui, et non un message, qui lui dit que la voie est libre |
| `pret_d_arme` | `require_before(armed, enter:building)` | Alice apprend en parlant que Bob a une batte de trop, va la lui emprunter, puis entre |
| `soin_d_un_camarade` | `require_before(band_tended, enter:building)` | Claire apprend que Bob est blessé, le panse ; Bob, blessé, n'entre pas le premier |
| `prevenir_avant_de_rentrer` | `require_before(knowledge_shared, ReachExtraction)` | `ShareKnowledge`, qui dormait depuis le livrable 7, se réveille sans une ligne de code |
| `renvoyer_au_van` | `require_before(ordered(extraction), ReachExtraction)` | même placement : Alice ordonne à Bob de rentrer (`OrderAlly`), Bob laisse sa fouille — victoire à 20 s contre 96 sans consigne. Prévenir dit ce qu'on a vu, renvoyer dit quoi faire |
| `rappeler_tout_le_monde` | `require_before(band_ordered(extraction), ReachExtraction)` | le même placement, Claire en plus : Alice rappelle **tout le groupe** (`OrderBand`), Bob et Claire laissent leur fouille ensemble — victoire à 10 s contre 97 |
| `radio` | — | deux radios portent la voix à quinze cases ; le journal dit « par radio » |

Le moteur a gagné pour cela trois actions, trois faits et trois repères — `lender` (l'allié qui a
**dit** porter une arme de rechange : on voit ce qu'un camarade tient, pas ce qu'il a dans son sac),
`patient` (le blessé qu'on **sait** blessé), `decoy` (où lancer : entendu du zombie le plus proche du
but, et le plus loin possible de ce but). Ce qu'un allié a de trop voyage avec la santé dans
`KnowledgeSharing.Talk` ; une diversion se retient sous son propre bruit, `NoiseAction.Lure`, parce
que le moteur étiquette `Distraction` tout bruit d'action sans libellé et que `diversion_made` ne
doit pas se lire dans un pas de départ ; et elle **se raconte** (`AgentKnowledge.LearnFrom`), parce
que le lanceur ne s'entend pas lui-même. La radio, enfin, était déclarée dans le catalogue depuis le
livrable 11 et personne ne la lisait — `KnowledgeSharing.RangeBetween` prend désormais le plus faible
des deux postes : une radio seule ne relie personne. Les scénarios de coopération partent avec **le
plan du quartier** (`scenario.mapped`, `SurvivorBand.KnowTerrain`) : sans lui, la première minute est
une reconnaissance pendant laquelle la horde repère le groupe, et le pétard part sur des zombies déjà
lancés à sa poursuite.

Faire tourner ces situations a **révélé douze défauts du moteur**. Détail dans
`docs/architecture.md`, « Défauts révélés ». **Sept sont corrigés**, parce qu'ils empêchaient une
partie ordinaire de se conclure ou de jouer le bon plan :

* **le rappel du van perdait contre un objectif sans plan.** `plan_confidence` est la confiance dans le
  plan *courant* : bloquée sur une fouille que le planificateur ne sait pas résoudre, une survivante
  l'avait à zéro, et le rappel qui la lit se scorait 0,3 contre 0,33 pour la fouille impossible — elle
  ne rentrait jamais. Un ordre neuf remet la confiance à 1 (`Survivor.Mission`), rappel comme ordre du
  joueur ;
* **le seuil d'un lieu fermé tombait parfois à l'intérieur.** `SymbolicStateBuilder.Doorstep` prenait
  la case voisine d'une ouverture la plus proche à vol d'oiseau ; devant une vitre, la rue et la pièce
  sont souvent à égale distance, et « aller à la pharmacie, puis forcer sa porte » commençait derrière
  la porte à forcer. Le seuil est pris parmi les cases **atteignables**. Un test du planificateur
  vérifiait l'ancien comportement sans le savoir — son runtime ne cherche pas de chemin ;
* **une cachette hors d'atteinte était choisie.** Le repère `cover` prenait la pièce connue la plus
  proche, fût-elle derrière une porte verrouillée : `Hide` échouait sans itinéraire et le plan se
  suspendait indéfiniment. Il est désormais filtré par l'atteignabilité, comme `objective` ;
* **un zombie assiégeait le van.** L'abri arrêtait le coup mais pas le regard : le zombie voyait sa
  proie dans le van et cognait contre la tôle indéfiniment, en barrant l'entrée au survivant qui
  rentrait après les autres — la mission ne se concluait jamais. Un survivant au van ne se voit plus
  du dehors (`EntityManager.InVan`, par le paramètre `concealed` de `PerceptionSystem.Observe`) ; le
  zombie perd la piste, fouille les abords et repart. Le test du van a changé de promesse en
  conséquence : « pas de coup, et pas de siège ».

Un sixième l'est en partie, et les quatre autres ne le sont pas — les grilles sont dessinées pour les éviter : un objet de
mission vu à travers la vitre d'un bâtiment fermé à clef bloque le survivant ; `door_link_radius` se
mesure depuis le coin haut-gauche d'un lieu, donc la porte au bout d'un grand bâtiment n'existe pas
pour le planificateur ; `BreakWindow` est dominée par `ForceDoor`, si bien que `forbid(BreakWindow)` —
la consigne pré-écrite « Ne cassez aucune vitre » — n'a aucun effet observable seule ; la trousse de
la mission soigne celui qui la porte (`remedy`), et la mission est perdue ; **désarmée, une survivante
ne se bat jamais** (`FightZombie` plafonne à 0,165 sans arme — `PushBackZombie` et `AttackZombie` sont
au domaine sans qu'aucune prise ne les montre, et le scénario « Sans arme, pas de combat » le filme tel
quel). Corrigé en partie : **une consigne interdisait un schéma, pas un geste**. Un repère
(`objective_spot`, `cover`…) prend désormais la catégorie du lieu où il tombe, tel que l'agent l'a vu
(`AgentKnowledge.ZoneKindAt`), donc « avant d'entrer dans un bâtiment » couvre aussi « aller ramasser
la trousse aperçue dans le kiosque » — `group` excepté, sans quoi la consigne de regroupement serait
insatisfiable ; mais `MoveToLocation(away_from_danger)` contourne toujours
`require_after(threat_handled, EscapeLocation)`, qui vise un autre schéma.

Le cinquième corrigé : **les pas d'un camarade comptaient comme une menace.** `UtilityContext` faisait
une menace de tout bruit connu, et à trois sur le square, sans un zombie, le groupe fuyait plus de cent
fois par prise, chaque fuite relançant les autres. `PerceptionSystem.Listen` écarte désormais, pour un
survivant, les bruits de déplacement (`Walk`, `Run`, `OpenDoor` — les zombies n'en émettent aucun, un
pas entendu est toujours celui du groupe) et les fracas dont il **voit** l'auteur, le leurre excepté.
Les zombies entendent toujours tous les pas. Le défaut en cachait un autre : **`MissionRun` n'émettait
aucun pas**, et `GameRoot` et `DemoRun` en avaient chacun leur copie. L'émission vit dans le cœur
(`SurvivorBand.EmitFootsteps`, appelée par `Advance`), et les trois boucles la déroulent.

Les deux derniers, trouvés par les stratégies combinées : **deux consignes sur la même classe se
rescindaient** — la seconde `enter:building` scindait la moitié « dedans » de la première, d'où
`EnterBuilding_EnterBuilding` et un schéma mort exigeant `building(?l)` et sa négation ; un schéma déjà
d'un seul côté n'est plus scindé (`DomainCompiler.SideOf`). Et **une vitre brisée refermait le
lieu** : devenue du sol, elle n'était plus une ouverture, et un bâtiment dont on connaissait la porte
verrouillée redevenait « scellé » juste après qu'on y eut fait un passage ;
`AgentKnowledge.Breaches` retient la vitre vue intacte puis en sol, et `CollectOpenings` la compte
pour praticable.

**Le paquetage** (`Hordinary.Core/Game/CampLoadout.cs`, `Scripts/UI/LoadoutPanel.cs`) : le joueur
confie à chaque survivant enrôlé ce qu'il emporte, pris dans l'entrepôt du camp — dont le contenu de
départ se déclare dans `camp.storage`. Composer ne débite rien (seul `HandOver` débite, au départ),
les limites du sac sont celles de `inventory` parce qu'un paquetage **est** un `Inventory`, une arme
confiée part en main, et prêter n'est pas donner : `Recover` rend à l'entrepôt ce qui revient avec
celui qui rentre, verdict ou pas. C'est une décision de joueur au sens du livrable 15, pas un réflexe.

**Le livrable 16 est fait** (missions aléatoires et récompenses). `MissionGenerator` tire trois offres
au début d'une partie ; une offre est une `MissionDefinition` avec cinq champs de plus (`type`,
`requirements`, `rewards`, `seed`, `entry_fuel_cost`) et **pas** une seconde classe — tout ce qui est
en aval travaille sur une `MissionDefinition` et ignore d'où elle vient. `MissionObjectiveTracker`
porte les trois compteurs (zombies tués *dans la zone cible*, objets et ressources ramassés là-bas
puis rapportés) sans jamais rendre de verdict ; `MissionRewardService` est le grand livre d'une
mission — l'essence débitée une seule fois au départ, les récompenses versées une seule fois à la
victoire. `mission_generation.enabled` à `false` rend l'écran de camp aux missions de
`config/missions.json` : le livrable reste retirable.

Une conséquence assumée, documentée dans `docs/architecture.md` : **une élimination n'a pas de
demandeur dans le domaine de planification**. Une offre `KillZombies` pose donc un objet d'appel dans
sa zone pour donner au groupe une raison d'y aller, et le combat reste le réflexe `FightZombie`. En
faire un but planifié serait une décision tactique prise seule par l'IA — donc du ressort du
livrable 15.

Une mission dure au plus `mission.time_limit` (cinq minutes) : le délai écoulé, `MissionTracker`
rappelle tout le monde au van par `SurvivorBand.Recall`, et la partie se conclut quand chaque
survivant encore debout est rentré — avec la caisse (victoire) ou sans elle (défaite). Le retour d'un
seul ne termine plus la mission.

**Le van protège** : un survivant dans la zone `Extraction` ne peut pas être frappé
(`EntityManager.Sheltered` — la règle est sur le monde, pas sur la décision du zombie, qui ne connaît
pas la carte). En contrepartie `FleeDanger` et `Hide` portent la précondition `at_extraction` : le seul
endroit où l'on ne peut pas être touché est aussi le seul d'où fuir revient à courir vers le danger.
Une carte sans zone `Extraction` n'abrite personne.

Quatre défauts de fin de partie ont été levés depuis, tous de la même famille — « un survivant qui a
fini devrait rester au van » :

* le porteur de la caisse faisait l'aller-retour entre `ReachExtraction` et `ExploreUnknownArea`.
  Arriver ferme `ReachExtraction` (`at_extraction`), l'exploration prenait la suite et le faisait
  ressortir. `ExploreUnknownArea` gagne la précondition `has_objective` : qui rapporte la caisse
  n'explore plus ;
* « être rentré » était acquis pour de bon. Sur la carte livrée, où le camp jouxte la ruelle, les
  trois y passaient dans les premières secondes et la victoire se déclarait au ramassage de la
  caisse, tout le groupe étant encore dans la pharmacie. Le retour est maintenant révocable au-delà
  de `mission.home_radius` cases ;
* le rappel du van ne délogeait pas un objectif installé. `Hide` à 0,25 contre le rappel à 0,37,
  écart de 0,12 sous une hystérésis de 0,15 — et se terrer entretient le danger qui fait se terrer.
  `SurvivorBand.Recall` appelle désormais `UtilitySelector.Reconsider` et non `Request` ;
* le quatrième était `ReachExtraction` ↔ `FleeDanger` sur le van, longtemps tenu pour légitime : fuir
  ce qui vous mord est un réflexe. Il a cessé de l'être le jour où le van a protégé — plus rien ne mord
  là-bas, et fuir revient à sortir se faire mordre. D'où la précondition `at_extraction` sur
  `FleeDanger` et `Hide`. Tenir la position **ailleurs** reste une décision tactique, donc au joueur
  (livrable 15).

**La spec G est faite, et la partie PDDL 3 du livrable 15 avec elle.**
`Hordinary.Core/AI/Planning/Pddl/` expose la représentation intermédiaire
(`PddlDomain`/`PddlAction`/`PddlFormula`/`PddlConstraint`) et ses **deux** rendus — texte pour Fast
Downward, `PlanningDomain` pour GBFS — un seul compilateur, donc aucune dérive possible entre ce
qu'un planificateur tiers valide et ce que le jeu exécute. `Hordinary.Core/AI/Instructions/` porte la
grammaire fermée des cinq formes (portillon 1), la compilation des modalités de trajectoire en
préconditions (portillon 2) et le vérificateur de trajectoire (portillon 4). Détail dans
`docs/architecture.md`, « Export PDDL et instructions tactiques ».

**Le livrable 15 est branché sur le jeu**, service de langage excepté. Le prérequis des bâtiments est
levé : la perception nomme les zones `Building`/`Street`/`Alley` qu'elle aperçoit
(`PerceptionSystem.RememberZones`), `SymbolicStateBuilder.ObjectNameOf` en fait des symboles
(« Entrepôt ouest » → `entrepot_ouest`) et chaque lieu porte sa catégorie — `building(entrepot_ouest)`,
et `building(objective_zone)` pour celui de la mission. Une rue ne reçoit jamais d'ouverture
(`ZoneKinds.IsOpenAir`), sans quoi une porte close vue du trottoir fermerait un quartier entier. Le
surcoût de branchement est mesuré sur un quartier généré, pas supposé
(`Nommer_tous_les_lieux_ne_fait_pas_deborder_les_budgets_du_planificateur`).

Le reste du câblage : `SurvivorBand.Instruct` porte les contraintes — elles sont **collectives** — et
`SurvivorAgent.Rebase` remplace le répertoire puis fait replanifier ; le portillon 4 tourne en jeu sur
chaque plan produit et sort sur `Trace.Error`, parce qu'une violation est un défaut du compilateur et
non du survivant. `GameRoot.Instruct` recompile **après la carte et avant le groupe** (« aucun bâtiment
ici » ne se sait qu'une fois le quartier tiré) et écrit `missions/<id>/domain.constrained.pddl` (pour
Fast Downward, sans bloc `(:constraints`) et `domain.declared.pddl` (pour VAL, avec). `InstructionPanel`
montre les refus avec leur portillon au même endroit que les acceptations, et le HUD rappelle les
consignes actives.

**L'export PDDL est complet, domaine et problème.** `PddlProblemBuilder` traduit le `PlanningProblem`
que GBFS résout — donc un instantané de ce qu'**un** survivant croyait, jamais l'état du monde. Les
sorties versionnées vivent dans `tools/pddl/` : `domain.pddl` et dix `snapshots/` prélevés dans une
partie jouée jusqu'au verdict. Ce sont des fichiers générés **et** versionnés, parce que ce sont eux
qu'on donne à Fast Downward hors ligne ; `Tests/PddlArtifactsTests.cs` les rend à nouveau et compare,
donc changer le domaine sans régénérer casse la suite. Il **énumère aussi le dossier** : un instantané
qu'aucune capture ne produit plus — renommé parce qu'un but a changé de nom, l'ancien survivant — ne
se verrait pas autrement, puisque les comparaisons se font par nom de fichier. Le régénérer ne l'efface
pas non plus ; c'est une suppression à la main :

```bash
HORDINARY_PDDL_REGEN=1 dotnet test Tests/Hordinary.Tests.csproj --filter "FullyQualifiedName~PddlArtifactsTests"
```

Un instantané tient en 45 à 76 lignes et se lit d'un bout à l'autre. Il en faisait 130 à 204 : les
deux tiers étaient une table des distances entre lieux, un `(= (distance a b) n)` par paire, qu'aucune
action ne lisait et qu'aucune ne pouvait lire — `(increase (total-cost) (distance ?from ?to))`
demanderait un paramètre d'origine que `MoveToLocation(?location)` n'a pas. Elle est partie, avec la
fonction `distance` du domaine. **Le `total-cost` exporté est donc la part fixe du coût seule** : le
`distance_cost` de `planner_domain.json` n'est pas exprimable sous cette forme, et comparer le coût
d'un plan de Fast Downward à celui de GBFS demande de le savoir.
`Le_probleme_ne_fixe_aucune_valeur_que_personne_ne_lit` interdit le retour du poids mort en général —
toute fonction déclarée doit être incrémentée par une action — et pose la question sur le **texte
rendu**, parce que le coût ne redevient un `(increase …)` qu'au rendu.

Deux pièges que cette capture a révélés, et qui valent au-delà d'elle. **`planner.max_time_ms` est une
horloge** : la suite des plans d'une mission dépend de la charge machine, donc un test qui compare deux
déroulés doit desserrer ce plafond (`MissionRun(planner:)`) — et un test de budget doit porter sur les
nœuds, pas sur le temps. **`NoiseSystem.Localize` retombe sur `Random.Shared`** quand personne ne lui
passe de générateur : une mission rejouée ne serait alors pas identique dès qu'un bruit est entendu de
loin. `SurvivorBand.Advance(delta, rng)`, `SurvivorBand.Perceive(elapsed, rng)` et
`MissionRun(noiseSeed:)` permettent d'en passer un, et **le jeu en passe un** — `GameRoot` sème
`_noiseRng` sur `zombies.seed` et le donne à chaque passe de perception. L'accroc à l'invariant « à
graine égale, résultat égal » est donc fermé, et **deux tests le verrouillent**
(`Tests/NoiseTests.cs`) : l'un fait entendre un bruit lointain à un groupe et vérifie que douze
graines donnent douze localisations, donc que le générateur traverse bien les quatre maillons qui le
déclarent optionnel ; l'autre **lit le texte de `Scripts/Game/GameRoot.cs`** et exige qu'aucune passe
de perception ne se fasse sans générateur. Le second est un pis-aller assumé — le câblage vit dans
`Scripts/`, hors de portée d'une suite qui ne lance pas le moteur, et le choix était entre lire le
fichier et ne rien verrouiller du tout. Il attrape le retrait du paramètre, pas un câblage qui compile
et se trompe.

**Le service de langage est écrit** (`service/` — FastAPI, graphe LangGraph à cinq nœuds, OpenRouter),
avec son corpus d'évaluation de 76 phrases (74 lors des mesures publiées ; les deux dernières sont les ordres de renvoi et de rappel) et son harnais. Le découpage est le point : *toutes* les
décisions — cache, plafond de dépense, délai, reprise, portillon 1 sur la réponse, repli sur les
instructions pré-écrites — sont dans `Hordinary.Core/AI/Instructions/InstructionBroker`, et
`Scripts/Instructions/InstructionClient.cs` ne fait que poster du JSON. Le transport est un **délégué**,
ce qui rend l'invariant « le cœur ne référence aucun client HTTP » structurel plutôt que disciplinaire :
il n'y a pas d'endroit où en mettre un. En retour, un service muet, bavard ou hors grammaire se teste
sans réseau.

Le **digest** de domaine (`DomainDigest`) est dérivé du `PddlDomain` et envoyé à chaque requête : le
service et le portillon 1 lisent la même source de vérité sur ce qui est nommable.
`service/tests/domain.digest.json` est généré par `PddlArtifactsTests` et versionné — sans quoi les
tests Python se seraient réécrit un vocabulaire, c'est-à-dire exactement ce que le digest existe pour
supprimer.

Trois champs ont dû être ajoutés au contrat d'API de la spec, et les trois sont documentés là où ils
vivent : `domain_digest.objects` (sans lui l'exemple canonique du livrable, qui cite le repère `group`,
demandait au modèle de deviner un vocabulaire dont la grammaire exige l'exactitude),
`constraint.factor` (sans lui `penalize(A, k)` était intraduisible) et `domain_digest.glossary`.

**Le glossaire** est la section `glossary` de `config/planner_domain.json` : une ligne de français
courant par symbole nommable — ce qu'il désigne, jamais quand l'employer. Ce n'est pas le champ
`description` des actions, qui s'adresse au concepteur. Il voyage par `PddlDomain.Glossary` et
`DomainDigest.Glossary` jusqu'au nœud `context`, qui l'écrit en face de chaque nom. Il est né d'un
passage réel : le digest donnait le nom et le type, jamais le sens, et « Soignez les blessés avant
d'entrer » — le libellé d'une consigne pré-écrite — était refusée faute de rien qui la relie à
`band_tended`. Il vit dans le domaine et non dans le prompt parce qu'une liste de correspondances
côté service serait un second vocabulaire ; `Chaque_symbole_nommable_a_sa_definition` exige une
définition par symbole **et** un symbole par définition. Il n'entre pas dans l'empreinte du domaine
(il n'est pas rendu en PDDL). **Son apport est mesuré** (`--no-glossary`, trois tirages par
condition, 25 septembre 2026) : exactitude de 69 % à 85 % sur le corpus, et de 65 % à 86 % sur
vingt-quatre phrases tenues à l'écart (`service/evals/heldout.jsonl`, `--corpus`) — le glossaire a
été écrit en connaissant les échecs du corpus, et c'est ce second chiffre qui dit que le gain n'est
pas un corpus appris par cœur. Le prix : **4 % de faux acceptés** sur le corpus contre 0 % sans, dont
des contresens (`forbid(BarricadeDoor)` pour « Barricadez la porte du fond »), des refus corrects en
baisse (erreurs de famille), et une latence qui monte sans doubler — p95 au pire de 4,5 s, sous le
`timeout_ms` de 8 000. Détail dans `service/evals/README.md`, « Le glossaire, mesuré ».

**Une mesure se prend sur trois tirages** (`evals.run --repeat 3`). À température nulle, le modèle
livré ne répond pas deux fois pareil — OpenRouter répartit les appels entre hébergeurs — et un passage
unique varie de plusieurs points. Le premier passage du glossaire en a tiré trois conclusions fausses :
trois « régressions » qui étaient du bruit, un « 0 % de faux acceptés » qui était de la chance, et une
latence « doublée » qui mesurait quatre passages lancés en même temps. La répétition a aussi fait
trouver **un quatrième défaut de définition du prompt** : `conditional_not_supported` visait
« un enchaînement imposé de plusieurs actions », et « Soignez les blessés avant d'entrer », libellé
d'une consigne pré-écrite, était refusée comme « enchaînement de deux actions ». La famille couvre
désormais une suite de trois gestes ou plus, et le test de l'antériorité dit qu'un geste mis en
premier s'écrit par le fait qu'il laisse vrai ; la phrase passe (3/3 sur la mesure de référence).

**Les deux portillons ne vérifiaient pas le type des arguments**, et c'est une évaluation facturée qui
l'a trouvé, pas une relecture : `nvidia/nemotron-3-super-120b-a12b` a produit
`require_before(equipped(agent), AttackWithWeapon)`, que les deux acceptaient — `agent` est déclaré,
`equipped` prend bien un argument, mais il est de type `object` là où `equipped` attend un `item`.
Accepté, le fait partait en précondition dans le `domain.constrained.pddl` donné à Fast Downward, qui
l'aurait refusé pour type invalide : **un portillon ne doit pas accepter ce que le validateur
extérieur refuse.** La réparation a trois pièces, et les trois comptent — `PddlDomain.Vocabulary`
porte le type et devient le seul endroit où cette liste existe (le portillon 1 et le digest la lisent
au lieu de la recopier, ce que le commentaire affirmait déjà à tort) ; `ConstraintGrammar` compare le
type de chaque argument à celui que le prédicat déclare ; et `domain_digest.objects` transporte le
type jusqu'au modèle, sans quoi on aurait durci le refus sans donner de quoi l'éviter. Détail dans
`docs/architecture.md`, « Quatre portillons ».

C'est **le modèle le plus faible du tableau** qui a trouvé le trou : un modèle fort n'aurait jamais
écrit cette contrainte, donc ne l'aurait jamais révélée.

Deux artefacts de mesure ont été fermés en même temps, et ils sont de nature différente. Le service
rendait `knowledge_shared()` là où le jeu écrit `knowledge_shared` (`PddlAtom.AsFact`) : c'était le
**produit** qui divergeait entre les deux langages, `grammar.to_text` canonise désormais le fait. Le
mot de classe de schémas — `enter:alley` contre `move:alley` — n'a en revanche aucune conséquence
mécanique (`ActionFamilies.Members` rend le même ensemble, le compilateur pose la même précondition) :
c'est la **mesure** qui était fausse, donc `evals/run.py` normalise pour comparer et le rendu montré
au joueur garde le mot qu'il a employé.

**Ce qui reste, et une réserve à dire franchement :**

* **le livrable 12 est le seul antérieur qui soit resté partiel**, et c'est sa moitié testable qui
  manque. L'interface est refondue et jouable — HUD en trois zones, panneau latéral, grille,
  écran de camp — mais le **générateur de survivants** n'est pas écrit, ni
  `config/survivor_generation.json`, ni l'export d'un roster. Le groupe est donc toujours celui, fixe,
  de `config/survivors.json`. Manquent aussi la sélection multiple dans la grille, l'écran titre, et
  9 des 22 images de la planche d'interface. L'écran de fin de partie, lui, a été **écarté** et non
  oublié : le verdict s'affiche dans le HUD avec ce que la victoire a rapporté. Détail case par case
  dans `Specs/12-interface-survivants-et-van.md`. C'est le seul endroit du projet où une spec promet
  des tests (« deux générations avec la même graine produisent des rosters identiques ») qui n'ont pas
  de sujet ;
* **le service Python tourne, et ses tests passent** : **89 sur 89** (`service/`, pytest), à
  côté des 685 du .NET — soit **774 au total**. Le harnais d'évaluation se déroule de bout en bout
  (`python -m evals.run --dry-run`) et `uvicorn` sert `/health` et `/instruction`, qui refuse
  proprement avec `provider_unavailable` tant qu'aucune clé n'est configurée. L'exécution a levé
  **trois défauts qu'aucune relecture n'aurait donnés**, et c'est l'argument pour lancer plutôt que
  relire : un témoin négatif qui visait `barricaded`, symbole pourtant *présent* au domaine depuis
  l'entrée de `BarricadeDoor` ; un `UnicodeEncodeError` en toute fin de harnais — console Windows en
  cp1252, flèches dans le rapport — qui tuait un passage entièrement réussi ; et un tableau markdown
  sans barres encadrantes, que GitHub aurait rendu en paragraphe. Le rapport s'écrit désormais
  **avant** d'être affiché : un passage réel coûte des appels facturés et ne doit pas se perdre sur
  un caractère que le terminal ignore ;
* **les versions testées sont figées dans `service/requirements.txt`**, utilisé pour les
  installations, Docker et la CI. `requirements.in` déclare les dépendances directes ; les mises
  à jour du verrou doivent passer les tests et l'audit de vulnérabilités avant publication ;
* `service/evals/results.md` — le tableau des modèles, des deux ablations et du **taux de faux
  acceptés** — demande une clé OpenRouter et des appels facturés. Le `--dry-run` valide **le harnais,
  pas les modèles** : son « 0 % de faux acceptés » vient d'un bouchon qui refuse tout.

  **Quinze modèles soumis, onze mesurés, en deux générations de prompt.** La synthèse est
  `service/evals/README.md` — c'est le dossier à lire, et c'est la pièce portfolio du livrable ; les
  tableaux bruts sont les `results.*.md` à côté.

  **Les deux ablations que la spec exige sont mesurées** (22 septembre 2026, modèle livré,
  66 phrases par condition — `results.baseline.md`, `results.no-digest.md`, `results.few-shot.md`).
  Référence : 82 % de compilation, 88 % de refus correct, 4 % de faux acceptés, p95 3 896 ms.

  * **`--no-digest` ne dégrade pas, il supprime** : 0 % de compilation, et **38 des 40 refus portent
    `unknown_symbol`**. Privé du vocabulaire, le modèle ne reconnaît plus aucun symbole — c'est le
    comportement correct, et cela dit ce que le digest est : le substrat de la tâche, pas une aide à
    la précision. Son « 0 % de faux acceptés » est l'artefact du `--dry-run` reproduit par un vrai
    modèle : **une colonne de faux acceptés se lit toujours avec le taux de compilation à côté.** Le
    coût par instruction double (la reprise de `self_check` part sur chaque cas) et le p95 monte à
    8 025 ms, au-dessus du `timeout_ms` du jeu ;
  * **`--few-shot 3` gagne six points de compilation** (88 %) et six d'exactitude (78 %), pour 18 %
    de tokens en plus — et perd trois points de refus correct (85 %) : des exemples qui montrent
    comment traduire poussent à traduire. Le gain n'est pas diffus, il tient à **une convention de
    nommage indevinable** : `group` est typé `location`, donc « restez groupés » s'écrit
    `at(agent, group)`. Le digest transmet le type, jamais le sens. Sans exemples les quatre phrases
    de cette famille échouent, avec trois motifs de refus différents ; avec trois voisins il n'en
    reste qu'une — et **l'exemple canonique du livrable est dans le lot**.

  **Un faux accepté survit à toutes les conditions**, et il marque la limite exacte du typage :
  « Équipez une arme avant d'attaquer » produit `require_before(equipped(objective), AttackWithWeapon)`.
  `equipped` attend un `item`, `objective` *est* un `item` — la contrainte est bien formée, bien
  typée, et fausse. Le portillon 1 ferme les arguments du mauvais **type**, pas ceux du mauvais
  **référent**, et le portillon 3 ne la verrait pas non plus puisqu'elle est satisfiable. C'est le
  joueur qui la voit, d'où l'affichage de la contrainte compilée plutôt qu'un accusé de réception.

  Trois résultats, qu'aucune relecture n'aurait donnés :

  * **le plafond était le prompt, pas le modèle.** Dépouillement des six premiers passages : **163
    phrases traduisibles refusées contre 29 traduites de travers**, un rapport de 5,6 contre 1. Trois
    défauts de *définition* dans le prompt système l'expliquaient — `preference_not_constraint`
    mangeait `penalize` (« évitez de forcer les portes » était refusée alors que la cinquième forme
    existe pour elle), `conditional_not_supported` mangeait `require_before` (« avant de » lu comme
    « si »), et « dans le doute, refuse » n'était pas calibré. Le prompt réécrit donne un test ordonné
    et des exemples **pris hors du corpus** — y coller les phrases évaluées aurait confondu
    l'apprentissage et l'examen, et vidé l'ablation `--few-shot` de son sens ;
  * **la latence décide avant l'exactitude.** Les deux meilleurs taux de compilation de la première
    génération appartenaient à des modèles dont le p95 était de 22 s et 267 s, hors de portée du
    `timeout_ms` de 8 000 du jeu ;
  * **un modèle à 90 % de compilation peut être le pire du tableau.**
    `inference-net/schematron-v2-turbo` traduit tout, y compris l'injection de prompt du corpus :
    **85 % de faux acceptés, 8 % de refus corrects**. Il est arrivé seul dans l'échantillon, et c'est
    la meilleure justification possible de cette colonne.

  **Le défaut livré est `deepseek/deepseek-v4.1-flash`** depuis le 21 septembre 2026, et il l'est
  partout : `config/game_config.json`, `InstructionSettings.Model`, `Settings.model`, plus les
  exemples de la spec et des tests. Ces quatre endroits ont déjà dit trois choses différentes par le
  passé, ce qui est la pire forme de défaut — celle où lire le code ne dit pas ce que le jeu fait ;
  **les changer ensemble, ou pas du tout.** Aucun modèle Anthropic n'est plus cité nulle part.

  Il remplace `qwen/qwen3.8-flash` : compilation doublée (80 % contre 40 %), refus correct égal
  (85 %), faux acceptés égaux (0 %), pour 0,00010 $ contre 0,00007 $. Surtout, **p95 de 21 928 ms à
  2 771 ms** — sous le `timeout_ms` de 8 000 du jeu pour la première fois. La queue lente de l'ancien
  tombait en repli sur les instructions pré-écrites, proprement et en le signalant, mais elle tombait.

  **`HORDINARY_REASONING` passe à `off` par défaut, et c'est indissociable.** Le nouveau modèle sait
  raisonner et les 2 771 ms ont été mesurés sans réflexion : le livrer au réglage d'usine du
  fournisseur donnerait un défaut qui ne reproduit pas le chiffre publié. Le défaut précédent — vide,
  « on ne bride personne » — se défendait tant que ce champ ne servait qu'à *comparer* ; pour comparer,
  passer la variable **vide** reste la bonne façon de faire, et le harnais lit le même champ.

  Une note de catalogue, apprise à l'usage : `supported_parameters: ["reasoning"]` sur OpenRouter ne
  veut **pas** dire « débrayable ». Trois des modèles les plus récents (`z-ai/glm-5.3-flash`,
  `z-ai/glm-5.3-flashx`, `google/gemini-3.8-flash`) répondent `400 — Reasoning is mandatory for this
  endpoint and cannot be disabled`. Ils ont compté 0 % avec 40 ms de latence : **une ligne à zéro se
  lit toujours avec sa latence**, sans quoi on prend un échec de transport pour un modèle nul ;
* **le portillon 3 tourne pour de vrai**, par Docker (`aibasel/downward`). `Tests/SatisfiabilityTests`
  cherche le planificateur dans `HORDINARY_FAST_DOWNWARD`, puis le PATH, puis l'image ; les tests
  d'intégration ne sont pas marqués `Skip`, ils **changent de question** selon ce qu'ils trouvent. La
  preuve d'insatisfiabilité marche de bout en bout : trois consignes actives, code de retour 11,
  `forbid(MoveToLocation)` nommée seule en cinq appels. Le verdict se lit dans le **code de retour** et
  non dans le texte — `11` est une preuve, `12` est une recherche qui a renoncé, et accuser un joueur
  sur un `12` serait l'erreur judiciaire que `SatisfiabilityGate` existe pour éviter.

  Ce qui reste : la preuve ne s'affiche pas *pendant une partie*, ce qui est conforme au portillon
  (« hors ligne ») mais pas au critère de validation 3 de la spec 15 — les deux exigences sont en
  tension dans ce document ;
* **VAL corrobore le portillon 4**, et son support PDDL 3 — réserve de longue date de la spec — est
  confirmé : il applique `sometime-before` sur notre domaine et le dit en toutes lettres. Pas d'image
  officielle, donc `tools/pddl/val/Dockerfile` compile VAL 4 ; à construire une fois, à la main. Le
  test qui compte est `Val_confirme_que_la_compilation_dit_ce_que_le_joueur_a_dit` : le plan que Fast
  Downward produit contre le domaine **compilé** satisfait la forme **déclarative** de la consigne.
  `TrajectoryChecker` répond déjà à cette question, mais il est écrit par la main qui a écrit le
  compilateur — si les deux se trompaient de la même façon, rien ne le verrait.

  Deux pièges. **Sans `-v`, VAL ne dit pas pourquoi** il refuse : « `Plan failed to execute` » et rien
  d'autre. Et **le témoin négatif ne peut pas s'adosser au domaine compilé** — le plan libre n'y est
  même pas exécutable, puisque la compilation a scindé `MoveToLocation` ; VAL le refuserait pour
  inapplicabilité, ce qui ne dirait rien de la contrainte. Le bloc PDDL 3 s'adosse donc au domaine
  d'origine pour ce cas ;
* **le premier appel au binaire a trouvé quatre défauts d'export d'un coup**, et c'est l'argument
  entier pour installer un outil plutôt que relire un fichier. Le domaine livré **n'était pas du PDDL
  valide** : `LocateObjective` nommait `objective_zone` sans que le domaine le déclare, `PushBackZombie`
  coûtait 2,5 là où PDDL n'admet que des entiers, le problème redéclarait ensuite ce que le domaine
  déclarait, et le schéma scindé par une consigne du joueur nommait `group`, que personne ne nommait
  avant. La règle retenue est structurelle plutôt que ponctuelle : **le vocabulaire nommable est
  exactement l'ensemble des constantes du domaine** — tout ce qu'un joueur peut citer peut finir en
  précondition, donc tout doit être déclaré d'avance. Les coûts sont **multipliés** par un facteur
  commun (2, écrit en tête du domaine) et non arrondis : arrondir 2,5 à 3 changerait l'ordre des plans
  en silence. Détail et tableau dans `tools/pddl/results.md` ;
* **le service se lance aussi en conteneur** (`service/Dockerfile`), ce qui le rend démarrable sans
  Python ni environnement virtuel. La clé passe par `-e OPENROUTER_API_KEY` sans valeur, donc n'entre
  ni dans l'image ni dans une ligne de commande. L'image n'emporte pas le dépôt : les trois tests qui
  comparent le digest à `tools/pddl/domain.pddl` s'y annoncent ignorés avec leur raison, et c'est le
  passage depuis le dépôt qui fait foi. Le conteneur ignore ces trois comparaisons seulement.

  `compose.yaml`, à la racine, réunit les quatre conteneurs du projet : le service, son harnais
  d'évaluation, Fast Downward et VAL. **Le jeu n'en dépend toujours d'aucun** — un `up` sans profil ne
  démarre que le service, les trois autres sont des outils qu'on appelle et qui rendent la main, d'où
  les profils `outils` et `evals`. Trois points ne sont pas cosmétiques : le port est publié sur
  `127.0.0.1` et non `0.0.0.0`, parce qu'un service qui porte une clé facturée à l'appel n'a rien à
  offrir au réseau local ; `image: hordinary-val` est écrit en toutes lettres parce que
  `SatisfiabilityTests` cherche ce nom-là par `docker image inspect`, et le nom que compose déduirait
  du projet et du service tombe juste aujourd'hui et faux au premier renommage — les tests
  changeraient alors de question en silence ; et le harnais d'évaluation monte `service/evals/`, que
  le `.dockerignore` tient hors de l'image, sans quoi le rapport d'un passage payant s'écrirait dans
  le conteneur et disparaîtrait avec lui.

Le domaine compte **30 actions, 8 verbes** — mais toutes ne sont pas jouables. Une action n'est
atteignable que si une chaîne arrière la relie à un but de `planner_domain.json`, **et** qu'un
objectif de `utility_goals.json` réclame ce but. Onze schémas n'ont ni l'un ni l'autre sans consigne :
`Observe`, `Wait`, `UseItem`, `PushHeavyObject`, `BarricadeDoor`, et les cinq gestes d'entraide
`ThrowFirecracker`, `BorrowWeapon`, `TendAlly`, `OrderAlly`, `OrderBand` posent des faits qu'aucune précondition
ne lit — `ActionRelevance` les écarte pour tous les objectifs — et `ShareKnowledge` a un but que
personne ne demande. `DropItem` passe le tri sans jamais servir. Les cinq gestes d'entraide et
`ShareKnowledge` ont désormais chacun une consigne pré-écrite qui les réveille.

**L'ordre entre survivants** (`OrderAlly(?location)`) est le seul geste qui porte sur la décision
d'un autre. Il pose chez le camarade à portée de voix (ou de radio) un `MissionOrder` marqué
`Issuer`, au poids `utility_ai.cooperation.order_priority` — au-dessus d'un ordre du joueur, qu'il
met de côté (`Superseded`) et rend une fois résolu, au-dessous du rappel du van, qu'il ne supplante
jamais (`Survivor.Obey`). L'entrée d'utilité `obeying` ferme les quatre objectifs propres (explorer,
chercher, ramasser, rentrer) et **pas les réflexes** : on obéit à un camarade, on ne se laisse pas
mordre pour lui. `SurvivorAgent.SettleOrder` clôt l'ordre rempli (à une case,
`MissionOrder.ReachedFrom`) ou impossible (aucun plan, ou plan suspendu — `Replanner.Suspended`) ;
sans cette clôture, un ordre impossible laisserait le camarade planté pour la mission. `ordered(lieu)`
se retient **aux deux bouts** (`AgentKnowledge.Orders`), sans quoi le camarade envoyé devrait, sous
une consigne collective, renvoyer quelqu'un à son tour. Consigne pré-écrite et scénario :
`renvoyer_au_van` (`require_before(ordered(extraction), ReachExtraction)`), sur le placement de
`prevenir_avant_de_rentrer` pour que les deux se comparent. Les ordres se journalisent sur le canal
`[Action]`, comme tous les gestes, et ce canal ne sort pas en console par défaut.

**Rappeler tout le monde** (`OrderBand(?location)`, fait `band_ordered(lieu)`) est le même ordre donné
à **chacun** de ceux qui l'entendent, et non au premier — un geste distinct, parce que renvoyer un
camarade ou rappeler le groupe est un choix du joueur ; son fait a son propre registre
(`AgentKnowledge.BandOrders`), et aucune des deux consignes ne se contente de l'autre. Deux écarts avec
`OrderAlly`, trouvés **dans le moteur** alors que le harnais gagnait la prise dès le premier essai :
il est instantané (à une seconde, les camarades faisaient un pas, `at(agent, ally)` tombait, et le plan
courait après eux jusqu'au rappel), et **vers le van, il se tient** (`MissionOrder.Holds`) — rempli, il
ne s'efface plus, sans quoi le premier arrivé repartait fouiller avant le retour des autres. L'ordre
simple garde son effacement à l'arrivée. Consigne pré-écrite et scénario : `rappeler_tout_le_monde`.

`BarricadeDoor` vient d'entrer dans cette liste, et c'est un progrès : elle en était **absente**, ce
qui était pire. Spécifiée au livrable 11, exécutable (`WorldPlanRuntime.SetFlag`, branche
`door_barricaded`), mais sans entrée dans le domaine, elle n'existait pour personne — ni pour le
planificateur, ni pour l'export PDDL, ni pour la grammaire du livrable 15, qui valide les instructions
du joueur contre `planner_domain.json` et aurait rejeté « barricade cette porte » comme symbole
inconnu. Le schéma y figure désormais au contrat de la spec 11, avec `equipped(toolbox)` cité en
toutes lettres plutôt que par un modificateur de `config/items.json`.

La diversion a suivi ce chemin jusqu'au bout. `CreateNoiseDistraction`, le but `CreateDistraction`,
l'objectif d'utilité du même nom et le prédicat `distraction_created` ont été retirés : faire du bruit
exprès pour attirer la horde ailleurs est un sacrifice calculé, pas un réflexe. `ThrowFirecracker` est
revenue au domaine sans eux, et c'est une consigne du joueur qui la déclenche.

Deux trous de couverture subsistent : `PickUpItem` n'est atteignable que pour s'armer (jamais pour
des vivres, ni pour un pétard au sol — on lance celui qu'on porte), et un blessé ne peut pas
planifier de ramasser un bandage qu'il voit — `remedy` n'est déclaré que s'il est **déjà** dans le
sac. Détail et tableau dans
`docs/architecture.md`, « Ce que le domaine sait faire, et ce qui y dort », et dans
`Specs/15-instructions-tactiques.md` § Prérequis 3, à qui revient de leur donner un demandeur.
