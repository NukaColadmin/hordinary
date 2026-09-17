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

C'est ce qui a fait retirer `HelpAlly`, `HealAlly`, `CalmAlly`, le *but d'utilité* `Regroup` et toute
la diversion (`CreateDistraction`, `CreateNoiseDistraction`, `ThrowFirecracker`) — et garder
`ShareKnowledge` (dire ce qu'on a vu n'est pas porter secours), les réflexes personnels (fuir, se
cacher, se soigner, se battre) et l'*ordre joueur* `PlayerCommandKind.Regroup`, à ne pas confondre
avec le but d'utilité du même nom.

Retirer un demandeur ne veut pas dire retirer le geste : le moteur garde de quoi barricader une porte
et de quoi lancer un pétard, sans que rien ne le lui demande. C'est la forme normale d'un geste en
attente du livrable 15, pas du code mort.

## Spécifications

`Specs/00-INDEX.md` est la seule spécification d'ensemble. Les livrables 1 à 8 sont le MVP et leurs
critères d'acceptation (`Specs/D-qualite-et-definition-of-done.md`) **ne se négocient pas** : une
extension qui les casserait est à retirer. Les livrables 11 à 15 sont post-MVP et chacun doit rester
retirable sans casser le MVP.

Ordre de réalisation (≠ ordre de numérotation) : `11 → 12 → 16 → G → 15 → 13`. Le livrable 14 est
retiré, le 16 est fait.

## État actuel

Branche `epuration-spec-15`, travail en cours d'épuration du projet autour du livrable 15. Sur l'arbre
de travail actuel, `dotnet test` donne **513 passés sur 513**. **Toute ligne rouge est donc une
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
donc changer le domaine sans régénérer casse la suite :

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
passe de générateur : une mission rejouée n'est donc pas identique dès qu'un bruit est entendu de loin.
`SurvivorBand.Advance(delta, rng)` et `MissionRun(noiseSeed:)` permettent de la graine ; le jeu, lui,
n'en passe toujours pas — c'est un accroc connu à l'invariant « à graine égale, résultat égal ».

Ce qui reste à faire : la traduction d'une phrase libre en contrainte (service
FastAPI/LangGraph/OpenRouter et `InstructionClient.cs` ; `instructions.enabled` est livré à `false`) et
le portillon 3 — Fast Downward et VAL restent des outils **hors ligne** et ne sont pas installés ici,
aucun planificateur tiers ne tourne pendant une partie. `tools/pddl/results.md`, qui doit porter le
tableau des dix (plan FD, plan GBFS, écart), attend donc de les avoir lancés.

Le domaine compte **25 actions, 8 verbes** — mais toutes ne sont pas jouables. Une action n'est
atteignable que si une chaîne arrière la relie à un but de `planner_domain.json`, **et** qu'un
objectif de `utility_goals.json` réclame ce but. Six schémas n'ont ni l'un ni l'autre : `Observe`,
`Wait`, `UseItem`, `PushHeavyObject` et `BarricadeDoor` posent des faits (`observed`, `waited`,
`used`, `pushed`, `door_barricaded`) qu'aucune précondition ne lit — `ActionRelevance` les écarte pour
tous les objectifs — et `ShareKnowledge` a un but que personne ne demande. `DropItem` passe le tri
sans jamais servir.

`BarricadeDoor` vient d'entrer dans cette liste, et c'est un progrès : elle en était **absente**, ce
qui était pire. Spécifiée au livrable 11, exécutable (`WorldPlanRuntime.SetFlag`, branche
`door_barricaded`), mais sans entrée dans le domaine, elle n'existait pour personne — ni pour le
planificateur, ni pour l'export PDDL, ni pour la grammaire du livrable 15, qui valide les instructions
du joueur contre `planner_domain.json` et aurait rejeté « barricade cette porte » comme symbole
inconnu. Le schéma y figure désormais au contrat de la spec 11, avec `equipped(toolbox)` cité en
toutes lettres plutôt que par un modificateur de `config/items.json`.

La diversion est dans le même cas. `CreateNoiseDistraction`, `ThrowFirecracker`,
le but `CreateDistraction`, l'objectif d'utilité du même nom et le prédicat `distraction_created` ont
été retirés : faire du bruit exprès pour attirer la horde ailleurs est un sacrifice calculé, pas un
réflexe. Le verbe `emitNoise` et `WorldPlanRuntime.ThrowNoise` restent exécutables sans demandeur, et
l'entrée `ally_danger` reste au vocabulaire sans lecteur — c'est au livrable 15 de rendre le geste au
joueur.

Deux trous de couverture qui en découlent : `PickUpItem` n'est atteignable que pour s'armer (jamais
pour des vivres, et plus pour un pétard depuis le retrait de la diversion), et un blessé ne peut pas
planifier de ramasser un bandage qu'il voit — `remedy` n'est déclaré que s'il est **déjà** dans le
sac. Détail et tableau dans
`docs/architecture.md`, « Ce que le domaine sait faire, et ce qui y dort », et dans
`Specs/15-instructions-tactiques.md` § Prérequis 3, à qui revient de leur donner un demandeur.
