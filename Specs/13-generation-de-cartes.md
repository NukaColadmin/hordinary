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
`navigation.max_explored_nodes` passe de `4000` à `24000` dans
[game_config.json](../config/game_config.json). Un A* plat sur 25 600 cases reste très largement dans
le budget d'une frame. Tout le reste des réglages existants tient tel quel.

> **Mesuré à l'implémentation.** La spécification annonçait `12000`, et c'était trop peu : un quartier
> `grand` compte environ 21 800 cases praticables, et la traversée la plus coûteuse mesurée sur cinq
> graines y explore **17 051 nœuds**. À `12000`, A* abandonnait un trajet qui existe
> (`SearchLimitReached`). `24000` laisse la recherche balayer le plus grand quartier en entier — c'est
> le vrai pire cas, et au-delà il n'y a plus de chemin à trouver. Le test
> `A_star_traverse_le_plus_grand_quartier_sous_la_limite_de_noeuds` verrouille la valeur.

## Les bords

C'est la seule vraie question de conception du livrable. Une carte finie ne doit ni ressembler à une
carte finie, ni laisser croire qu'on peut continuer.

La réponse tient en **une seule couche** : le brouillard.

> **Simplification décidée à l'implémentation.** La version initiale de cette spécification décrivait
> quatre couches concentriques — six familles de tuiles de ceinture raccordables avec leurs
> terminaisons de rue, une bande de décor rendue seule, un fondu vers le noir. Elles ont toutes été
> remplacées par une famille unique, les tuiles de [Assets/Edges/](../Assets/Edges/). Le brouillard ne
> **cache** pas ce qui est au-delà : il le **remplace**. Il n'y a donc ni décor à rendre, ni fondu à
> calculer, ni terminaison de rue à raccorder sur deux voisins différents — une rue qui atteint le bord
> entre dans le brouillard, et c'est tout ce qu'il y a à dire. La section ci-dessous décrit ce qui est
> réellement implémenté ; le paragraphe « Ce qui a été écarté » garde la trace du reste.

```text
┌──────────────────────────────────────────────────────┐
│  ▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓  │  ← 2. ceinture de brouillard : visible,
│  ▓▓▓┌──────────────────────────────────────┐▓▓▓▓▓▓▓  │       infranchissable, et qui DIT pourquoi
│  ▓▓▓│                                      │▓▓▓▓▓▓▓  │
│  ▓▓▓│           QUARTIER JOUABLE           │▓▓▓▓▓▓▓  │  ← 1. la carte, au sens du livrable 1
│  ▓▓▓│                                      │▓▓▓▓▓▓▓  │
│  ▓▓▓└──────────────────────────────────────┘▓▓▓▓▓▓▓  │
│  ▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓[VAN]▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓  │  ← le van ferme la route d'entrée
│  ▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓  │
└──────────────────────────────────────────────────────┘
        caméra bornée : son cadre ne dépasse jamais cette limite
```

### 1. Le quartier jouable

Le rectangle du tableau ci-dessus. Tout s'y passe : bâtiments, zombies, objets, objectif, van.

### 2. La ceinture de brouillard (4 à 8 cases) — la seule couche

Des cases **visibles, dessinées, infranchissables**, dont le rôle est de donner une **raison** de ne pas
aller plus loin. Un mur invisible dit « le jeu s'arrête » ; un brouillard épais dit « pas par là », ce
qui n'est pas la même chose.

Techniquement c'est un `CellType.Border` : il bloque le mouvement **et** la vue, et il n'entre dans
aucune zone. Les deux ceintures livrées (`config/mapgen.json`) sont deux images d'une même règle :

| id | Ce qu'on voit | Ce que ça dit |
|----|---------------|---------------|
| `fog` | Brouillard épais | On ne voit rien par là, ce n'est pas la peine |
| `radioactive_fog` | Brouillard teinté | On ne va pas par là, et pour une bonne raison |

Changer l'une pour l'autre ne change rien au jeu, seulement ce que le joueur comprend de l'endroit où
il est. Un profil déclare l'ordre de ses `border_kinds`, et le générateur en tire une par quartier —
pas une par côté : un quartier a une ambiance, pas quatre.

**Le côté du van** : un côté porte la route d'arrivée, et le van y est garé. Derrière lui, la route
entre dans le brouillard. C'est le bord le plus regardé de la carte, parce que c'est par là qu'on entre
et qu'on repart.

**La brèche (optionnelle)** : `breach_chance` autorise une ouverture unique dans la ceinture, jamais du
côté du van. Elle n'est **pas** franchissable par les survivants — les cases restent `Border` — et
n'est qu'un jeu de coordonnées enregistrées : c'est par là que les renforts de horde entreront en cours
de mission. Le bord cesse d'être une limite pour devenir une menace, ce qui est la meilleure façon de
faire oublier que c'en est une.

### 3. La caméra bornée — ce qui fait tenir l'ensemble

[CameraController.cs](../Scripts/Game/CameraController.cs) borne son cadre pour qu'il ne dépasse jamais
la limite extérieure de la ceinture, zoom compris. C'est la mesure la plus efficace des deux : le
joueur ne voit tout simplement jamais le point où le monde s'arrête. La ceinture sert à ce que le
**bord du cadre** soit crédible ; la caméra sert à ce qu'il n'y en ait pas d'autre.

Le zoom en fait partie, et c'est ce qui donne son épaisseur à la ceinture : déborder ne demande pas de
se déplacer, il suffit de reculer assez pour que l'écran devienne plus large que le monde. Le plancher
de zoom est donc celui qui **couvre** la fenêtre, et non celui qui fait tenir la carte dedans — un
grand quartier ne se voit pas d'un seul coup d'œil, et c'est voulu. La carte dessinée, elle, n'est pas
bornée : elle n'a pas de ceinture, et la borner reviendrait à interdire de la regarder en entier.

### Côté agents : la ceinture est connue d'avance

Les cases de ceinture sont marquées **connues et infranchissables dans l'`AgentKnowledge` de chaque
survivant dès le début de la mission**. C'est une exception délibérée à la perception partielle du
livrable 2, et elle est justifiée : le groupe a choisi ce quartier sur une carte et a été déposé à un
point précis. Il sait où le quartier s'arrête ; il ne sait pas ce qu'il y a dans les bâtiments.

Sans cette exception, le `FrontierExplorer`
([FrontierExplorer.cs](../Hordinary.Core/AI/Utility/FrontierExplorer.cs)) verrait dans la ceinture une
frontière inconnue permanente et enverrait les survivants longer les bords pendant toute la mission.
C'est le bug que cette règle prévient, et le test associé le vérifie.

### Ce qui a été écarté

* **Mur invisible** — le joueur le trouve en cinq secondes et sait qu'il est dans un rectangle.
* **Carte bouclée** — absurde dans un quartier de ville.
* **Repousser le joueur par un message** — sort du monde pour dire une règle du monde.
* **Faire simplement plus grand** — c'est la carte 500×500 qu'on vient d'abandonner.
* **Six familles de ceinture raccordables** (`grillage`, `gravats`, `remblai`, `canal`, `foret`,
  `barrage`), avec deux à trois types tirés par carte et une **terminaison de rue** par famille. C'était
  la partie la plus coûteuse du livrable, en dessin comme en code : une terminaison se raccorde à la
  ceinture d'un côté et à la chaussée de l'autre, la seule tuile du projet contrainte sur deux voisins
  différents. Le brouillard rend la question sans objet.
* **La bande de décor rendue seule et le fondu vers le noir** — deux couches dont le seul travail était
  de faire croire que la ville continue derrière la ceinture. Un brouillard qui remplace ce qui est
  au-delà n'a rien derrière lui à montrer.

## Pipeline de génération

Six passes, chacune testable isolément, chacune consommant la sortie de la précédente.

```text
1. Trame       découpage du quartier en 3 à 8 îlots par la voirie. Le point de
               dépose du van est posé en premier, sur un bord : tout le reste
               est construit en fonction de lui.

2. Voirie      une rue principale traversante, des rues secondaires, des ruelles.
               La connectivité globale est garantie ici, avant que quoi que ce
               soit d'autre soit posé : le quartier jouable est rempli de sol,
               et les bâtiments y sont creusés ensuite. Une voie qui atteint un
               bord entre dans le brouillard.

3. Bâtiments   empreinte par îlot, murs, pièces par découpage binaire récursif,
               portes intérieures, fenêtres en façade, portes d'entrée (1 à 3
               par bâtiment).

4. Lieux       attribution des MapZone au format de config/map_locations.json :
               le point de dépose (kind Extraction), l'objectif, et les zones
               nommées posées sur des bâtiments et des rues réels.

5. Peuplement  objets lourds (livrable 11), objets au sol, puis zombies (selon
               mission.zombie_count). L'ordre est le seul possible : les meubles
               ferment des cases, les objets se posent sur celles qui restent,
               et les zombies apparaissent sur ce qui est encore praticable une
               fois tout posé. Les caisses de la mission sont posées par
               l'assemblage, comme pour la carte dessinée.

6. Bordure     ceinture de brouillard et brèche optionnelle, puis validation :
               le générateur relit sa propre carte et rejette celle qui ne
               satisfait pas le contrat ci-dessous. Une carte rejetée est
               régénérée avec seed+1, au maximum N fois.
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
* **rien de praticable ne sort du quartier jouable** — c'est la clause que le brouillard simplifie le
  plus. Avec six familles raccordables, elle demandait de vérifier que chaque rue butant sur le bord
  portait bien son portail soudé ou son bouchon de voitures ; avec une ceinture qui remplace tout ce
  qui est au-delà, elle se dit une fois pour toutes ;
* aucun zombie, aucun objet au sol et aucun meuble ne sont posés dans la ceinture ;
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
    "thickness": [4, 8],
    "breach_chance": 0.5,
    "breach_width": [2, 4],

    "kinds": [
      { "id": "fog", "name": "Brouillard", "tile": "3:0,0", "allows_breach": true },
      { "id": "radioactive_fog", "name": "Brouillard radioactif", "tile": "4:0,0", "allows_breach": true }
    ]
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

      "border_kinds": ["fog", "radioactive_fog"],
      "objective_distance": [30, 90],
      "walkable_ratio": [0.70, 0.90],

      "props": { "heavy_per_block": [2, 6], "ground_items_per_building": [0, 3] }
    }
  ]
}
```

Un profil ne contient que des intervalles et des probabilités : ajouter une ambiance (« centre-ville »,
« zone industrielle », « campagne ») est une entrée de plus, jamais du code.

> **`walkable_ratio` recalibré.** La spécification annonçait `[0.35, 0.6]`. C'est un chiffre qui suppose
> des bâtiments qui bloquent ; ici on y entre, et l'intérieur d'un bâtiment est praticable — un pavillon
> de 12 × 13 cases n'a que 40 % de murs. Un quartier jouable tourne donc autour de **85 %**, mesuré sur
> 600 générations, et l'intervalle est calé là-dessus. La clause garde tout son sens : en dessous de
> 70 % le quartier est un labyrinthe, au-dessus de 90 % c'est un terrain vague avec deux maisons dessus
> — et c'est ce second cas, le plus fréquent, que la relecture rejette.

## Prérequis (humain)

C'est le poste que la simplification a le plus allégé, et c'était sa raison d'être : la ceinture est ce
que le joueur regarde le plus souvent sans s'en rendre compte, et sept familles de tuiles à dessiner
avant de pouvoir jouer auraient bloqué le livrable. Registre graphique et **prompts de génération** dans
[F-direction-artistique-et-prompts.md](./F-direction-artistique-et-prompts.md), section 6 : les bords
appartiennent à la famille de palette **froide**, pour que le quartier reste l'endroit où l'on a envie
de rester.

* [x] tuiles de brouillard, une par ambiance, dans [Assets/Edges/](../Assets/Edges/) —
      `EdgesFog.png` et `EdgesRadioactiveFog.png`, déclarées comme sources 3 et 4 de
      [tile_legend.json](../config/tile_legend.json). Une seule tuile suffit par famille : le brouillard
      n'a pas de raccord à respecter, ce qui est exactement pourquoi il a été retenu ;
* [ ] tuile de brèche, en **32 × 32 px**, le jour où les renforts de horde entreront réellement par là.
      En attendant, la brèche est un jeu de coordonnées enregistrées et se dessine comme le reste de la
      ceinture.

## Architecture concernée

`World/MapGeneration/` (`LayoutPass`, `RoadPass`, `BuildingPass`, `ZonePass`, `PopulatePass`,
`BorderPass`, `ValidationPass`, `DistrictGenerator`) — huit classes pour six passes : la passe 6 se
partage en deux, `BorderPass` qui pose, `ValidationPass` qui relit et rejette, parce qu'on ne teste pas
un validateur qui est aussi son propre producteur. Voir
[A-architecture-et-donnees.md](./A-architecture-et-donnees.md). Le générateur vit dans
`Hordinary.Core` : il ne dépend d'aucun type Godot et se teste sans lancer le jeu. Seuls le peintre de
tuiles (`MapPainter`) et le bornage de caméra vivent dans `Scripts/`.

## Étapes à créer

* [x] Poser le squelette du pipeline (6 passes, une graine, un quartier en sortie).
* [x] Passe 1 : point de dépose du van, puis découpage en îlots.
* [x] Passe 2 : voirie et garantie de connectivité globale.
* [x] Passe 3 : bâtiments, pièces, portes, fenêtres.
* [x] Passe 4 : attribution des `MapZone` au format `config/map_locations.json`.
* [x] Passe 5 : peuplement (objets lourds, objets au sol, zombies).
* [x] Passe 6 : ceinture de brouillard et brèche.
* [x] Implémenter la validation du contrat de jouabilité et la régénération à `seed+1`.
* [x] Marquer la ceinture comme connue et infranchissable dans l'`AgentKnowledge` au départ
      (`SurvivorBand.KnowBorder`).
* [x] Vérifier que le `FrontierExplorer` ne produit aucune frontière sur la ceinture.
* [x] Ajouter `config/mapgen.json` et le champ `map_profile` aux missions.
* [x] Relever `navigation.max_explored_nodes` et mesurer un trajet traversant — `24000`, voir plus haut.
* [x] Borner la caméra à la limite extérieure de la ceinture, zoom compris.
* [x] Afficher la graine dans l'écran de camp et permettre sa saisie.
* [x] Exporter un quartier généré au format de la carte de démonstration.

## Critère de validation

Trois missions lancées d'affilée se jouent sur trois quartiers différents, chacun reproductible à
l'identique par sa graine. Le scénario de démonstration du livrable 8 s'y déroule sans qu'aucune ligne
des livrables 4 à 8 ait été modifiée. Et en longeant les bords, le joueur ne trouve jamais de mur
invisible ni de limite non expliquée : la carte s'arrête dans le brouillard, et la caméra ne montre
jamais ce qu'il y a derrière.

## Tests associés

Dans [Tests/MapGenerationTests.cs](../Tests/MapGenerationTests.cs) (voir [C-tests.md](./C-tests.md)).
Aucun profil n'y est nommé : les tests parcourent `config/mapgen.json`, et une ambiance ajoutée au
fichier entre dans la suite sans qu'une ligne de test change.

* [x] deux générations avec la même graine produisent des grilles, zones, ceintures et peuplements
      identiques — et deux graines différentes, deux quartiers différents ;
* [x] tout quartier accepté satisfait les neuf clauses du contrat de jouabilité ;
* [x] un profil qu'aucune carte ne peut satisfaire épuise ses essais sur des graines consécutives et
      dit pourquoi ; une graine refusée du premier coup rend un quartier valide à la graine suivante ;
* [x] le point de dépose et l'objectif sont connectés en tenant compte des portes verrouillées — et
      le quartier en pose bien, sans quoi la clause ne prouverait rien ;
* [x] rien de praticable ne sort du quartier jouable, et tout ce qui en sort est de la ceinture ;
* [x] aucun zombie, objet au sol ni meuble n'est posé dans la ceinture ;
* [x] la ceinture est connue de chaque survivant au départ, et le `FrontierExplorer` n'y produit aucune
      frontière — vérifié dans les deux sens, avec et sans la mise au courant ;
* [x] un A* traversant le plus grand profil reste sous `max_explored_nodes` ;
* [x] un quartier exporté puis relu est identique au quartier généré, case par case ;
* [x] le générateur ne référence aucun type Godot ;
* [x] chaque mission livrée qui demande un quartier nomme un profil qui existe, et une mission sans
      `map_profile` continue de se jouer sur la carte dessinée.
