# Livrable 13 — Génération de quartiers

> Voir [00-INDEX.md](./00-INDEX.md) pour le contexte général du projet.

**Priorité : P3 — Moyenne.** Ce livrable n'ajoute aucune mécanique : il remplace la carte de
démonstration unique par un quartier généré à chaque mission. Sa valeur est la rejouabilité. Placé
après les livrables 11 et 12, qui tiennent sur la carte actuelle, et avant le livrable 14, qui a besoin
de lieux variés où faire des rencontres.

**Dépendances** : livrables 1 à 8 ; le livrable 11 (la passe 4 pose les `profile` de `config/zones.json`,
la passe 5 applique leurs tables de butin et sème les objets lourds) ; et le van du livrable 12, c'est
lui qui dépose le groupe et dont l'empreinte de 2 × 3 cases entre dans le contrat de jouabilité.

## Objectif

Générer, pour chaque mission, un **quartier** de quelques dizaines de cases de côté : déterministe,
entièrement jouable, et indiscernable pour le reste du code d'une carte écrite à la main.

## Principe directeur : le van fait le cadrage

On ne simule pas une ville. Le van emmène les survivants à une **zone d'intérêt** et les y dépose ; le
terrain de jeu est ce quartier-là, et rien d'autre. Le reste de la ville existe dans la fiction — le
camp est ailleurs, la route qui y mène sort du champ — mais n'est jamais chargé, jamais navigué, jamais
peuplé.

Ce cadrage n'est pas une limitation qu'on subit, c'est ce qui rend le livrable réalisable :

* pas de navigation hiérarchique à écrire — `AStar.cs` reste le seul système de chemin ;
* pas de mémoire d'agent à repenser — `AgentKnowledge` garde son dictionnaire creux ;
* pas de rendu par tuiles à streamer — le quartier tient en mémoire et à l'écran ;
* pas de brouillard de guerre par région — celui du livrable 2 suffit ;
* une mission garde sa durée de 5 à 10 minutes sans que le trajet mange le temps de jeu.

## Taille

Trois formats, choisis par la mission via `map_profile` :

| Format | Côté | Cases | Traversée à `walk_speed 4` | Usage |
|--------|------|-------|-----------------------------|-------|
| `petit` | 80 | 6 400 | ~20 s | Une rue, deux bâtiments. Missions courtes, tutoriel |
| `moyen` | 120 | 14 400 | ~30 s | Le format par défaut |
| `grand` | 160 | 25 600 | ~40 s | Missions difficiles, plusieurs bâtiments d'intérêt |

À titre de comparaison, la carte de démonstration livrée fait environ 26 cases de côté. Le format
`moyen` est donc une vingtaine de fois plus grande, ce qui est confortable — et deux ordres de grandeur
en dessous des 250 000 cases d'une carte de ville, ce qui est le point.

### Budgets à relever

Seul réglage à revoir, et il tient en une ligne :
`navigation.max_explored_nodes` passe de `4000` à `12000` dans
[game_config.json](../config/game_config.json). Un A* plat sur 25 600 cases reste très largement dans
le budget d'une frame. Tout le reste des réglages existants tient tel quel.

## Les bords

C'est la seule vraie question de conception du livrable. Une carte finie ne doit ni ressembler à une
carte finie, ni laisser croire qu'on peut continuer.

La réponse est en **quatre couches concentriques**, dont trois sont du décor et une seule est du jeu.
Aucune n'est suffisante seule ; ensemble, la question disparaît.

```text
┌──────────────────────────────────────────────────────┐  ← 4. fondu vers le noir
│  ░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░  │  ← 3. décor (toits, cimes) : rendu seul
│  ░░┌────────────────────────────────────────────┐░░  │
│  ░░│▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓│░░  │  ← 2. ceinture : visible, infranchissable,
│  ░░│▓▓┌──────────────────────────────────────┐▓▓│░░  │       et qui DIT pourquoi
│  ░░│▓▓│                                      │▓▓│░░  │
│  ░░│▓▓│           QUARTIER JOUABLE           │▓▓│░░  │  ← 1. la carte, au sens du livrable 1
│  ░░│▓▓│                                      │▓▓│░░  │
│  ░░│▓▓└──────────────────────────────────────┘▓▓│░░  │
│  ░░│▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓[VAN]▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓│░░  │  ← le van ferme la route d'entrée
│  ░░└────────────────────────────────────────────┘░░  │
│  ░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░  │
└──────────────────────────────────────────────────────┘
        caméra bornée : son cadre ne dépasse jamais cette limite
```

### 1. Le quartier jouable

Le rectangle du tableau ci-dessus. Tout s'y passe : bâtiments, zombies, objets, objectif, van.

### 2. La ceinture (4 à 8 cases) — la couche qui compte

Des cases **visibles, dessinées, infranchissables**, dont le rôle est de donner une **raison** de ne pas
aller plus loin. Un mur invisible dit « le jeu s'arrête » ; un grillage de chantier doublé de barbelés
dit « pas par là », ce qui n'est pas la même chose.

Types de ceinture, tirés par le générateur, **un par côté** (`config/mapgen.json`) :

| id | Ce qu'on voit | Où c'est crédible |
|----|---------------|-------------------|
| `grillage` | Grillage de chantier, barbelés, panneaux, sections affaissées mais toujours closes | Partout. Le plus polyvalent |
| `gravats` | Immeuble effondré, poutres, tas de béton | Centre-ville, zone bombardée |
| `remblai` | Talus d'autoroute surélevé, glissière, bouchon de voitures accidentées | Périphérie |
| `canal` | Canal, berges bétonnées, pont écroulé au milieu | Ville d'eau, très lisible |
| `foret` | Lisière dense, ronces, sous-bois impénétrable | Bordure de parc, campagne |
| `barrage` | Barrage de quarantaine : blocs de béton, sacs de sable, véhicule militaire à l'abandon | Un seul côté, celui de la route |

**Règle de composition** : tirer deux à trois types différents pour les quatre côtés. Quatre côtés
identiques se lisent immédiatement comme un cadre ; trois grillages et un talus d'autoroute se lisent
comme un endroit.

**Les rues qui sortent** : toute voie de la passe 2 qui atteint la ceinture doit être **terminée** par
elle, jamais coupée net. Une rue qui bute sur le `remblai` finit en bouchon de voitures ; sur le
`grillage`, en portail soudé ; sur les `gravats`, sous un immeuble effondré. C'est la règle qui fait la
différence entre un quartier et un rectangle découpé dans une ville.

**Le côté du van** : un côté porte la route d'arrivée, et le van y est garé. Derrière lui, la route est
fermée par le type de ceinture de ce côté. C'est le bord le plus regardé de la carte, parce que c'est
par là qu'on entre et qu'on repart — il mérite d'être celui qu'on soigne.

**La brèche (optionnelle)** : `spawn_breach` autorise une ouverture unique dans la ceinture — une
section de grillage arrachée, un trou dans les gravats. Elle n'est **pas** franchissable par les
survivants ; c'est par là que les renforts de horde entrent en cours de mission. Le bord cesse d'être
une limite pour devenir une menace, ce qui est la meilleure façon de faire oublier que c'en est une.

### 3. Le décor (8 à 16 cases) — rendu seul

Au-delà de la ceinture, des tuiles **dessinées et rien d'autre** : toits vus de dessus, cimes d'arbres,
silhouettes de bâtiments hors d'atteinte. Elles n'entrent ni dans la `GridMap`, ni dans la navigation,
ni dans la perception, ni dans la mémoire des agents. Aucune entité n'y existe.

Leur seul travail : que l'œil voie la ville continuer derrière la ceinture.

### 4. Le fondu

Sur les dernières cases du décor, assombrissement progressif jusqu'au noir. Il supprime la ligne nette
où le décor s'arrête et se raccorde à l'esthétique de brouillard de guerre déjà présente au livrable 2.

### 5. La caméra bornée — ce qui fait tenir l'ensemble

[CameraController.cs](../Scripts/Game/CameraController.cs) borne son cadre pour qu'il ne dépasse jamais
la limite extérieure du décor, zoom compris. C'est la mesure la plus efficace des cinq : le joueur ne
voit tout simplement jamais le point où le monde s'arrête. Les couches 2 à 4 servent à ce que le
**bord du cadre** soit crédible ; la caméra sert à ce qu'il n'y en ait pas d'autre.

### Côté agents : la ceinture est connue d'avance

Les cases de ceinture sont marquées **connues et infranchissables dans l'`AgentKnowledge` de chaque
survivant dès le début de la mission**. C'est une exception délibérée à la perception partielle du
livrable 2, et elle est justifiée : le groupe a choisi ce quartier sur une carte et a été déposé à un
point précis. Il sait où sont l'autoroute et le canal ; il ne sait pas ce qu'il y a dans les bâtiments.

Sans cette exception, le `FrontierExplorer`
([FrontierExplorer.cs](../Hordinary.Core/AI/Utility/FrontierExplorer.cs)) verrait dans la ceinture une
frontière inconnue permanente et enverrait les survivants longer les bords pendant toute la mission.
C'est le bug que cette règle prévient, et le test associé le vérifie.

### Ce qui a été écarté

* **Mur invisible** — le joueur le trouve en cinq secondes et sait qu'il est dans un rectangle.
* **Carte bouclée** — absurde dans un quartier de ville.
* **Repousser le joueur par un message** — sort du monde pour dire une règle du monde.
* **Faire simplement plus grand** — c'est la carte 500×500 qu'on vient d'abandonner.

## Pipeline de génération

Six passes, chacune testable isolément, chacune consommant la sortie de la précédente.

```text
1. Trame       découpage du quartier en 3 à 8 îlots par la voirie. Le point de
               dépose du van est posé en premier, sur un bord : tout le reste
               est construit en fonction de lui.

2. Voirie      une rue principale traversante, des rues secondaires, des ruelles.
               La connectivité globale est garantie ici, avant que quoi que ce
               soit d'autre soit posé. Toute voie atteignant un bord est
               terminée par la ceinture.

3. Bâtiments   empreinte par îlot, murs, pièces par découpage binaire récursif,
               portes intérieures, fenêtres en façade, portes d'entrée (1 à 3
               par bâtiment).

4. Lieux       attribution des MapZone au format de config/map_locations.json :
               le point de dépose (kind Extraction), l'objectif, et les zones
               nommées posées sur des bâtiments et des rues réels.

5. Peuplement  zombies (selon mission.zombie_count), objets au sol et objets
               lourds (livrable 11), caisses d'objectif, obstacles de rue,
               rencontres (livrable 14).

6. Bordure     ceinture, terminaisons de rues, brèche optionnelle, bande de
               décor, puis validation : le générateur relit sa propre carte et
               rejette celle qui ne satisfait pas le contrat ci-dessous. Une
               carte rejetée est régénérée avec seed+1, au maximum N fois.
```

## Déterminisme

`seed` + `id de mission` → un quartier et un seul, au bit près : bâtiments, portes verrouillées,
positions de zombies, objets au sol, emplacement de l'objectif, types de ceinture. C'est la condition
pour qu'un bug de carte soit reproductible et pour que les tests de ce livrable existent.

La graine est affichée dans l'écran de préparation de mission et peut être saisie à la main.

## Contrat de jouabilité

La passe 6 vérifie, et refuse la carte si l'une de ces conditions manque :

* le point de dépose du van et l'objectif sont **connectés** en tenant compte des portes verrouillées :
  il existe au moins un chemin dont chaque porte fermée est ouvrable, forçable, ou contournable par une
  fenêtre ;
* la distance dépose → objectif est dans `objective_distance` : ni collée au van, ni au bord opposé ;
* tout bâtiment contenant l'objectif a au moins deux accès distincts — sinon un échec de `ForceDoor`
  clôt la mission au lieu de la compliquer ;
* aucune pièce n'est inaccessible depuis l'extérieur de son bâtiment ;
* le van dispose de son empreinte de 2×3 cases libres sur une rue touchant un bord (livrable 12) ;
* la proportion de cellules praticables dans le quartier jouable est dans `walkable_ratio` : ni
  labyrinthe, ni terrain vague ;
* aucune voie n'atteint un bord sans terminaison de ceinture ;
* aucun zombie et aucun objet ne sont posés dans la ceinture ou le décor ;
* aucun zombie n'apparaît à moins de `zombies.minimum_spawn_distance` du point de dépose — la règle
  existe déjà dans `config/game_config.json` et n'est pas réécrite ici.

## Compatibilité

Le générateur produit **exactement les données que `MapLoader` charge aujourd'hui**
([MapLoader.cs](../Scripts/World/MapLoader.cs)) : une `GridMap` de `CellType`, une liste de `MapZone`,
des portes, des fenêtres, des points d'apparition. Rien en aval ne doit savoir si la carte a été
dessinée ou générée, et un quartier généré peut être exporté au format de la carte de démonstration
pour être rejoué, inspecté ou corrigé à la main.

La carte de démonstration du livrable 8 reste livrée et jouable : elle demeure la carte de référence des
tests existants.

## Configuration

`config/mapgen.json`, une entrée de profil par ambiance, référencée par
[missions.json](../config/missions.json) via un champ `map_profile`.

```json
{
  "max_generation_attempts": 8,

  "border": {
    "belt_thickness": [4, 8],
    "backdrop_thickness": [8, 16],
    "fade_tiles": 6,
    "distinct_kinds_per_map": [2, 3],
    "spawn_breach_chance": 0.5
  },

  "profiles": [
    {
      "id": "banlieue",
      "description": "Pavillons, rues larges, peu de couvert. On voit venir, et on est vu.",
      "size": 120,

      "blocks": [4, 6],
      "roads": { "main_width": [4, 6], "street_width": [2, 3], "alley_chance": 0.35 },
      "buildings": { "density": [0.45, 0.65], "footprint": [[6, 8], [18, 22]], "rooms": [2, 6] },
      "doors": { "per_building": [1, 3], "locked_chance": 0.35, "window_chance": 0.6 },

      "border_kinds": ["grillage", "foret", "remblai"],
      "objective_distance": [35, 80],
      "walkable_ratio": [0.35, 0.6],

      "props": { "heavy_per_block": [2, 6], "ground_items_per_building": [0, 3] }
    }
  ]
}
```

Un profil ne contient que des intervalles et des probabilités : ajouter une ambiance (« centre-ville »,
« zone industrielle », « campagne ») est une entrée de plus, jamais du code.

## Prérequis (humain)

Non bloquant pour le code, bloquant pour la Definition of Done — la ceinture est ce que le joueur
regarde le plus souvent sans s'en rendre compte. Registre graphique et **prompts de génération** dans
[F-direction-artistique-et-prompts.md](./F-direction-artistique-et-prompts.md), section 6 : les bords
appartiennent à la famille de palette **froide**, pour que le quartier reste l'endroit où l'on a envie
de rester.

* [ ] tuiles de ceinture pour les six types (`grillage`, `gravats`, `remblai`, `canal`, `foret`,
      `barrage`), en **32 × 32 px raccordables dans les deux axes**, avec leurs **terminaisons de rue**
      en **32 × 32 px** : portail soudé, bouchon de voitures, pont écroulé, immeuble effondré sur la
      chaussée. Une terminaison se raccorde à la ceinture d'un côté et à `FloorAsphalt` de l'autre :
      c'est la seule tuile du projet contrainte sur deux voisins différents ;
* [ ] tuile de brèche pour les types qui l'autorisent, en **32 × 32 px** ;
* [ ] tuiles de décor vues de dessus : toits, cimes d'arbres, silhouettes de bâtiments — trois planches
      de **128 × 128 px**, soit 16 tuiles raccordables de 32 × 32 chacune. Le fondu vers le noir de la
      couche 4 est produit par le code, jamais dessiné dans ces tuiles.

## Architecture concernée

`World/MapGeneration/` (`LayoutPass`, `RoadPass`, `BuildingPass`, `ZonePass`, `PopulatePass`,
`BorderPass`, `ValidationPass`, `DistrictGenerator`) — huit classes pour six passes : la passe 6 se
partage en deux, `BorderPass` qui pose, `ValidationPass` qui relit et rejette, parce qu'on ne teste pas
un validateur qui est aussi son propre producteur. Voir
[A-architecture-et-donnees.md](./A-architecture-et-donnees.md). Le générateur vit dans
`Hordinary.Core` : il ne dépend d'aucun type Godot et se teste sans lancer le jeu. Seuls le décor, le
fondu et le bornage de caméra vivent dans `Scripts/`.

## Étapes à créer

* [ ] Poser le squelette du pipeline (6 passes, une graine, un quartier en sortie).
* [ ] Passe 1 : point de dépose du van, puis découpage en îlots.
* [ ] Passe 2 : voirie et garantie de connectivité globale.
* [ ] Passe 3 : bâtiments, pièces, portes, fenêtres.
* [ ] Passe 4 : attribution des `MapZone` au format `config/map_locations.json`.
* [ ] Passe 5 : peuplement (zombies, objets, objets lourds, caisses d'objectif).
* [ ] Passe 6 : ceinture, terminaisons de rues, brèche, décor.
* [ ] Implémenter la validation du contrat de jouabilité et la régénération à `seed+1`.
* [ ] Marquer la ceinture comme connue et infranchissable dans l'`AgentKnowledge` au départ.
* [ ] Vérifier que le `FrontierExplorer` ne produit aucune frontière sur la ceinture.
* [ ] Ajouter `config/mapgen.json` et le champ `map_profile` aux missions.
* [ ] Relever `navigation.max_explored_nodes` à `12000` et mesurer un trajet traversant.
* [ ] Rendre la bande de décor et le fondu (hors `GridMap`, hors navigation, hors perception).
* [ ] Borner la caméra à la limite extérieure du décor, zoom compris.
* [ ] Afficher la graine dans l'écran de préparation et permettre sa saisie.
* [ ] Exporter un quartier généré au format de la carte de démonstration.

## Critère de validation

Trois missions lancées d'affilée se jouent sur trois quartiers différents, chacun reproductible à
l'identique par sa graine. Le scénario de démonstration du livrable 8 s'y déroule sans qu'aucune ligne
des livrables 4 à 8 ait été modifiée. Et en longeant les bords, le joueur ne trouve jamais de mur
invisible ni de limite non expliquée : la carte s'arrête sur un talus d'autoroute, un grillage ou un
canal, et la caméra ne montre jamais ce qu'il y a derrière.

## Tests associés

À ajouter dans `Tests/` (voir [C-tests.md](./C-tests.md)) :

* deux générations avec la même graine produisent des grilles, zones, ceintures et peuplements
  identiques ;
* tout quartier accepté satisfait les neuf clauses du contrat de jouabilité ;
* un quartier volontairement invalide (objectif enclavé) est rejeté par la passe 6 et régénéré ;
* le point de dépose et l'objectif sont connectés en tenant compte des portes verrouillées ;
* aucune voie n'atteint un bord sans terminaison de ceinture ;
* aucune case de ceinture ni de décor n'est praticable, et aucune entité n'y est posée ;
* la ceinture est connue de chaque survivant au départ, et le `FrontierExplorer` n'y produit aucune
  frontière ;
* un A* entre deux coins opposés d'un quartier `grand` reste sous `max_explored_nodes` ;
* un quartier exporté puis rechargé par `MapLoader` est identique au quartier généré ;
* le générateur ne référence aucun type Godot.
