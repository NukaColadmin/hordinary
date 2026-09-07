# Livrable 11 — Objets, armes et économie du camp

> Voir [00-INDEX.md](./00-INDEX.md) pour le contexte général du projet.

**Priorité : P1 — Haute.** Premier livrable post-MVP. Il transforme la boucle « une mission, une caisse
de médicaments » en une boucle de survie : ce que le groupe rapporte se consomme, et ce qu'il consomme
décide de la mission suivante. Les livrables 12 (le van coûte de l'essence) et 14 (une bouche de plus à
nourrir) en dépendent directement.

**Dépendances** : livrables 1 à 8 terminés. **Bloqué au départ** par le prérequis humain ci-dessous.

## Objectif

Donner une matière au monde : des objets et des armes que les survivants ramassent, portent et
utilisent ; des obstacles lourds qu'ils déplacent ; un camp qui a faim et soif entre deux missions.

## Prérequis (humain, bloquant)

Ce livrable ne démarre pas tant que les tuiles listées dans le **catalogue de tuiles** ci-dessous ne
sont pas livrées, au format des existantes (`Medikit.png` + `.import` dans `Assets/Lists/`, ou une
entrée de plus dans `Assets/atlas.png`) — mais **en 32 × 32**, y compris les huit tuiles déjà livrées,
qui sont à refaire.

Le registre graphique, la charte technique, la palette et **un prompt de génération par tuile** sont
dans [F-direction-artistique-et-prompts.md](./F-direction-artistique-et-prompts.md), section 4.

**Il n'y a rien à livrer au-delà de ces tuiles.** Les icônes d'inventaire du panneau latéral
(livrable 12) sont **les tuiles elles-mêmes**, affichées dans un emplacement à un facteur d'échelle
entier. Une seconde série de quinze dessins en trois quarts serait plus jolie ; elle ferait surtout
exister deux vérités pour le même objet, et la première fois qu'on repeindrait l'une sans l'autre, une
conserve n'aurait plus la même tête selon qu'on la regarde par terre ou dans un sac. Repeindre l'atlas
repeint l'inventaire — c'est la même image, adressée par le même `id`.

Tant que ces images manquent, le code peut être écrit et testé (les tests de ce livrable ne dépendent
d'aucun visuel), mais le livrable n'est **pas** considéré comme terminé : la Definition of Done exige
que la fonctionnalité soit visible en jeu (voir [D-qualite-et-definition-of-done.md](./D-qualite-et-definition-of-done.md)).

## Catalogue de tuiles

`tile_size` vaut encore `16` dans [tile_legend.json](../config/tile_legend.json) et **passe à 32 px**
avec ce livrable — c'est sa première étape, et c'est l'unique commutateur de géométrie du projet. Toute
tuile ajoutée doit être déclarée dans ce même fichier, sous la clé `source:x,y`, et rattachée soit à un
`CellType` ([CellType.cs](../Hordinary.Core/World/CellType.cs)), soit à un `id` du catalogue d'objets ou
d'armes.

**Toutes les tuiles de ce catalogue font 32 × 32 px**, y compris les calques de barricade — et c'est
la seule dimension du livrable : les emplacements d'inventaire montrent ces mêmes tuiles. Voir
[F-direction-artistique-et-prompts.md](./F-direction-artistique-et-prompts.md), sections 2 et 4.

Deux règles héritées de la légende existante, à ne pas enfreindre :

* **aucune tuile n'encode un état d'exécution.** Il n'existe pas de tuile « porte verrouillée » parce
  que le verrouillage est un état, pas un terrain ; pour la même raison, il n'existe pas de tuile
  « porte barricadée » : la barricade est un **calque** dessiné par-dessus la porte ;
* **une tuile de sol ne change rien au jeu.** Les variantes de sol ci-dessous sont toutes des
  `CellType.Floor` : elles servent à lire la carte, jamais à la modifier.

### Déjà livré — **à refaire en 32 × 32**

Ces huit images existent en 16 × 16 et sont à reprendre avec le reste : une carte qui mélange deux
échelles de tuile n'est pas jouable.

| Tuile | `CellType` | Source | Dimensions |
|-------|-----------|--------|-----------|
| `Wall` | `Wall` | `0:5,0` | **32 × 32** |
| `FloorIndoor` | `Floor` | `0:6,0` | **32 × 32** raccordable |
| `FloorOutdoor` | `Floor` | `0:7,0` | **32 × 32** raccordable |
| `Window` | `Window` | `0:4,0` | **32 × 32** |
| `WindowBroken` | `Floor` | `0:3,0` | **32 × 32** |
| `DoorClosed` | `DoorClosed` | `1:0,0` | **32 × 32** |
| `DoorOpen` | `DoorOpen` | `2:0,0` | **32 × 32** |
| `Medicine` | — (objet) | `0:2,0` | **32 × 32** |

Les clés `source:x,y` sont des **coordonnées de cellule** et ne changent pas. Le `tile_map_data` de
`Scenes/Main.tscn` non plus : la carte dessinée à la main survit intacte au changement d'échelle.

### À produire — décor bloquant

`CellType.Obstacle` est défini dans le code et bloque le déplacement **et** la vue, mais n'a aujourd'hui
aucune tuile : c'est le manque le plus criant du jeu de tuiles actuel. Cinq variantes, pour qu'un
obstacle ait toujours l'air d'être quelque chose.

| Tuile | Dimensions | Ce qu'on voit | Bloque déplacement | Bloque vue |
|-------|-----------|---------------|--------------------|------------|
| `ObstacleCarWreck` | **32 × 32** | Épave de voiture, portières ouvertes | oui | oui |
| `ObstacleRubble` | **32 × 32** | Tas de gravats, poutres | oui | oui |
| `ObstacleCounter` | **32 × 32** | Comptoir, banque d'accueil | oui | oui |
| `ObstacleShelfFull` | **32 × 32** | Rayonnage plein, encore garni | oui | oui |
| `ObstacleBin` | **32 × 32** | Benne, conteneur à ordures | oui | oui |

### À produire — variantes de sol (5)

Toutes `CellType.Floor`. Purement visuelles, et c'est ce qui permet de distinguer une rue d'une pièce
sans lire une légende.

Le raccord se fait sur 32 px : un motif conçu pour 16 puis agrandi produit une période deux fois plus
visible, et sur une chaussée entière ça se voit.

| Tuile | Dimensions | Usage |
|-------|-----------|-------|
| `FloorAsphalt` | **32 × 32** raccordable | Chaussée |
| `FloorSidewalk` | **32 × 32** raccordable | Trottoir |
| `FloorGrass` | **32 × 32** raccordable | Herbe, terrain vague, parc |
| `FloorTiled` | **32 × 32** raccordable | Carrelage : pharmacie, commerce |
| `FloorDirty` | **32 × 32** raccordable | Sol souillé, papiers, traces — signale une pièce déjà visitée par d'autres |

### À produire — sols de zone (2)

| Tuile | Dimensions | `CellType` | Usage |
|-------|-----------|-----------|-------|
| `FloorPharmacy` | **32 × 32** raccordable | `Pharmacy` | Marque la zone d'objectif à l'œil |
| `FloorCamp` | **32 × 32** raccordable | `Extraction` | Zone de dépose et de retour |

### À produire — objets au sol (14)

Une tuile par entrée des catalogues d'objets et d'armes, avec le même `id`. `medikit` réutilise
`Medikit.png`, déjà livré.

| Tuile | Dimensions | `id` | Catalogue | Icône d'inventaire |
|-------|-----------|------|-----------|--------------------|
| `ItemCannedFood` | **32 × 32** | `canned_food` | Objet 1 | la même tuile |
| `ItemWaterCan` | **32 × 32** | `water_can` | Objet 2 | la même tuile |
| `ItemBandage` | **32 × 32** | `bandage` | Objet 3 | la même tuile |
| *(à refaire)* | **32 × 32** | `medikit` | Objet 4 | la même tuile |
| `ItemFuelCan` | **32 × 32** | `fuel_can` | Objet 5 | la même tuile |
| `ItemAmmoBox` | **32 × 32** | `ammo_box` | Objet 6 | la même tuile |
| `ItemFlashlight` | **32 × 32** | `flashlight` | Objet 7 | la même tuile |
| `ItemRadio` | **32 × 32** | `radio` | Objet 8 | la même tuile |
| `ItemFirecracker` | **32 × 32** | `firecracker` | Objet 9 | la même tuile |
| `ItemToolbox` | **32 × 32** | `toolbox` | Objet 10 | la même tuile |
| `WeaponRevolver` | **32 × 32** | `revolver` | Arme à feu 1 | la même tuile |
| `WeaponShotgun` | **32 × 32** | `shotgun` | Arme à feu 2 | la même tuile |
| `WeaponBaseballBat` | **32 × 32** | `baseball_bat` | Corps à corps 1 | la même tuile |
| `WeaponCrowbar` | **32 × 32** | `crowbar` | Corps à corps 2 | la même tuile |
| `WeaponMachete` | **32 × 32** | `machete` | Corps à corps 3 | la même tuile |

Les cinq armes ont une **seconde** image, le sprite tenu en 32 × 16 vu de profil, qui appartient au
livrable 12 (section 5.1 de la spec F). Deux cadrages, deux tailles, le même objet — la tuile sert à la
fois au sol et à l'inventaire.

Contrainte de lisibilité : un objet posé au sol doit rester identifiable sur `FloorIndoor` comme sur
`FloorAsphalt`. Un liseré sombre d'un pixel autour de chaque objet suffit, et c'est la seule convention
imposée. **Il reste à 1 px malgré le passage à 32** : un contour deux fois plus fin à l'écran est
précisément ce qu'on est allé chercher.

### À produire — objets lourds (5 images)

| Tuile | Dimensions | `kind` | `mass` | Bloque vue | Note |
|-------|-----------|--------|--------|------------|------|
| `HeavyCrate` | **32 × 32** | `crate` | 2 | non | Déplaçable par un survivant seul |
| `HeavyShelf` | **32 × 32** | `shelf` | 3 | oui | La limite du survivant seul |
| `HeavyDumpster` | **32 × 32** | `dumpster` | 5 | oui | Exige deux survivants adjacents — c'est la tuile qui justifie la règle |
| `BarricadeDoor` | **32 × 32** calque | — | — | oui | **Calque** posé sur une porte, jamais une tuile de cellule |
| `BarricadeWindow` | **32 × 32** calque | — | — | oui | **Calque** posé sur une fenêtre |

### À produire — camp (12 images)

Le camp cesse d'être une zone vide dès qu'il a un stock. Trois réserves, **chacune en trois états lus
directement dans `config/camp.json`** : `plein` (> 60 % de la capacité), `bas` (10 à 60 %), `vide`
(< 10 %).

| Tuile | Dimensions | États | Ce qu'elle montre |
|-------|-----------|-------|-------------------|
| `CampFoodStore` | **32 × 32** × 3 | plein / bas / vide | Étagère de conserves |
| `CampWaterStore` | **32 × 32** × 3 | plein / bas / vide | Citerne, bidons |
| `CampFuelStore` | **32 × 32** × 3 | plein / bas / vide | Jerricans alignés |
| `CampFire` | **32 × 32** | — | Feu, point de regroupement |
| `CampTent` | **32 × 32** | — | Couchages |
| `CampWorkbench` | **32 × 32** | — | Établi, où l'on répare une arme usée |

Ces trois jauges à trois états sont ce qui fait exister la faim autrement que par un chiffre : le joueur
voit l'étagère se vider avant de lire le compteur. Les trois états d'une même réserve doivent être
**superposables au pixel près** — même cadrage, même empreinte, seul le contenu change.

### À produire — zones (6)

Les six tuiles réclamées par le catalogue de zones plus bas. Deux d'entre elles ont un effet mécanique,
et il est déclaré ici plutôt que déduit du dessin.

| Tuile | Dimensions | Zone | `CellType` | Effet mécanique |
|-------|-----------|------|-----------|-----------------|
| `FuelPump` | **32 × 32** | Station-service | `Obstacle` | Aucun. Bloque la vue, ce qui donne le seul couvert de l'esplanade |
| `FuelPumpEmpty` | **32 × 32** | Station-service | `Obstacle` | État visuel : pompe déjà vidée |
| `WindowBarred` | **32 × 32** | Poste de police | `Wall` | **`BreakWindow` indisponible** — c'est toute la difficulté de la zone |
| `ShutterHalf` | **32 × 32** | Quincaillerie | `DoorClosed` | Entrée unique et étroite ; ouvrable, jamais forçable |
| `Cot` | **32 × 32** | Centre d'accueil | `Obstacle` | Lit de camp. Fouillable : porte du butin dispersé |
| `Locker` | **32 × 32** | Centre d'accueil | `Obstacle` | Casiers. Fouillables, bloquent la vue |

### Récapitulatif

| Groupe | Dimensions | Tuiles | Images |
|--------|-----------|--------|--------|
| Décor bloquant | 32 × 32 | 5 | 5 |
| Variantes de sol | 32 × 32 raccordable | 5 | 5 |
| Sols de zone | 32 × 32 raccordable | 2 | 2 |
| Objets au sol | 32 × 32 | 14 | 14 |
| Objets lourds et barricades | 32 × 32 | 5 | 5 |
| Camp | 32 × 32 | 6 | 12 |
| Zones | 32 × 32 | 6 | 6 |
| Déjà livré, à refaire | 32 × 32 | 8 | 8 |
| **Total** | | **51** | **57** |

Et rien d'autre : les emplacements d'inventaire réutilisent les tuiles d'objets ci-dessus, il n'y a pas
de seconde série d'icônes à produire.

Cinquante-sept images de 32 × 32 tiennent dans un atlas **256 × 256** en grille 8 × 8, avec sept
emplacements de marge. Voir la section 4.10 de
[F-direction-artistique-et-prompts.md](./F-direction-artistique-et-prompts.md) pour la géométrie
d'atlas et le retrait de la séparation d'1 px.

Le livrable 13 ajoute son propre jeu de tuiles pour les bords de quartier (ceinture, terminaisons de
rue, décor) — voir [13-generation-de-cartes.md](./13-generation-de-cartes.md). Aucun recouvrement avec
la liste ci-dessus.

## Objets et armes sont des données

Comme les survivants ([survivors.json](../config/survivors.json)) et les actions
([planner_domain.json](../config/planner_domain.json)), aucun objet et aucune arme ne doit exister sous
forme de classe C# nommée. `Hordinary.Core` ne connaît que des *catégories* (`Consumable`, `Weapon`,
`Tool`, `Heavy`) et des *effets primitifs* ; le catalogue vit dans `config/items.json` et
`config/weapons.json`. Ajouter une conserve, retirer une arme, doubler les dégâts d'une batte doit
rester une modification de configuration, jamais une recompilation.

### Modèle

```python
class Item:
    id: str
    name: str
    category: Category          # Consumable | Weapon | Tool | Heavy
    sprite: str
    weight: float
    stackable: bool
    max_stack: int
    effects: list[Effect]       # verbes d'effet primitifs, voir ci-dessous
    noise_level: float          # bruit de l'utilisation


class Effect:
    verb: str                   # heal | feed | drink | refuel | modify | consume | emit_noise
    target: str                 # self | ally | camp | agent_stat
    amount: float
    duration: float | None      # None = permanent tant que l'objet est équipé
```

Verbes d'effet primitifs, et rien de plus : `heal`, `feed`, `drink`, `refuel`, `modify` (multiplie ou
décale une caractéristique existante de `config/game_config.json`), `consume`, `emit_noise`.

### Inventaire

`Survivor.inventory` existe déjà ([A-architecture-et-donnees.md](./A-architecture-et-donnees.md)). Il
devient contraint :

```json
{
  "inventory": {
    "slots": 6,
    "max_weight": 20,
    "weapon_slots": 1,
    "overweight_speed_penalty": 0.5,
    "drop_on_death": true
  }
}
```

Un survivant surchargé ralentit. Un survivant mort laisse son inventaire au sol — c'est ce qui rend une
mort coûteuse autrement que par le nombre.

## Catalogue d'objets (10)

`config/items.json`. Chaque objet a un effet mécanique distinct branché sur un réglage existant de
`config/game_config.json` : aucun objet n'est décoratif.

| # | id | Nom | Catégorie | Effet |
|---|----|-----|-----------|-------|
| 1 | `canned_food` | Conserve de nourriture | Consumable | `feed +1 ration` au camp |
| 2 | `water_can` | Bidon d'eau | Consumable | `drink +1 ration` au camp, ou `+20 stamina` en mission |
| 3 | `bandage` | Bandage | Consumable | `heal +20`, silencieux, `duration 1.5 s` |
| 4 | `medikit` | Trousse de soin | Consumable | `heal +survivor.heal_amount`, `duration 3 s` — c'est l'objet « médicaments » du MVP, désormais catalogué |
| 5 | `fuel_can` | Jerrican d'essence | Consumable | `refuel +20` au stock d'essence du camp (voir livrable 12 : le van) |
| 6 | `ammo_box` | Boîte de munitions | Consumable | `+12` munitions au stock du camp ; sans elle, les armes à feu sont des massues |
| 7 | `flashlight` | Lampe torche | Tool | équipée : `modify vision_range ×1.4` ; en contrepartie, `+1` au bruit perçu par les zombies dans la ligne de vue |
| 8 | `radio` | Radio portable | Tool | équipée : `modify knowledge.communication_range ×3` — le partage de connaissances du livrable 7 devient un objet à trouver, pas un réglage |
| 9 | `firecracker` | Pétard | Consumable | `emit_noise 10` à une case visée, sans que le survivant s'y trouve — `CreateNoiseDistraction` à distance |
| 10 | `toolbox` | Caisse à outils | Tool | équipée : `modify ForceDoor.cost ×0.5`, `BarricadeDoor` devient disponible |

Les objets 1, 2 et 5 sont les trois consommables de base que le camp consomme.

## Catalogue d'armes (5)

`config/weapons.json`. Une arme est un `Item` de catégorie `Weapon` avec les champs supplémentaires
`damage`, `range`, `noise_level`, `attack_duration`, `durability` et `ammo` (absent = corps à corps).
Le bruit reste sur le barème de la section 18.1 déjà utilisé par
[planner_domain.json](../config/planner_domain.json) : marche 1, porte 2, course 3, porte forcée 6,
vitre brisée 8, distraction 10.

### Armes à feu (2)

| id | Nom | Dégâts | Portée | Bruit | Munitions | Note |
|----|-----|--------|--------|-------|-----------|------|
| `revolver` | Revolver .38 | 40 | 6 | 9 | 6 par barillet | Tue un zombie en un coup sur deux, s'entend dans tout le quartier |
| `shotgun` | Fusil à pompe | 90 | 3 | 10 | 2 par tube | Nettoie un couloir et convoque la horde. Le bruit maximal du barème, comme la distraction |

Une arme à feu sans munitions reste utilisable au corps à corps (`damage 10`, `noise 3`). C'est ce qui
rend `ammo_box` intéressante sans la rendre indispensable.

### Armes de corps à corps (3)

| id | Nom | Dégâts | Portée | Bruit | Durabilité | Note |
|----|-----|--------|--------|-------|------------|------|
| `baseball_bat` | Batte de baseball | 25 | 1 | 3 | 40 coups | L'arme de départ, encombrante et honnête |
| `crowbar` | Pied-de-biche | 20 | 1 | 3 | 80 coups | Aussi un outil : équipé, `modify ForceDoor.cost ×0.6` et `ForceDoor.noise ×0.8` |
| `machete` | Machette | 35 | 1 | 2 | 25 coups | La plus discrète et la plus tranchante, la première à se ruiner |

À durabilité nulle, l'arme devient inutilisable et reste dans l'inventaire (« cassée ») : c'est une
information à ramener au camp, pas un objet qui disparaît sans prévenir.

### Ce que les armes changent pour la Utility AI et GBFS

Rien n'est codé en dur. `AttackZombie` existe déjà avec `cost 3` et `noise_level 6` ; l'arme équipée
multiplie ces valeurs exactement comme les `skills` d'un survivant le font aujourd'hui
([survivors.json](../config/survivors.json)). Un survivant armé d'une machette voit donc GBFS lui
proposer le combat plus volontiers qu'un survivant à mains nues — sans qu'aucun code ne teste ce qu'il
porte.

## Objets lourds et déplacement

Une caisse, une étagère, une benne : bloque le passage, parfois la ligne de vue
([LineOfSight.cs](../Hordinary.Core/World/LineOfSight.cs)), et se déplace au prix d'un effort. La
barricade, elle, n'est pas un objet lourd mais un état posé sur une porte ou une fenêtre — voir la règle
des calques dans le catalogue de tuiles.

```python
class HeavyObject:
    id: str
    kind: str                   # crate | shelf | dumpster
    position: GridPosition
    mass: float                 # 1 à 5
    blocks_movement: bool
    blocks_sight: bool
    push_noise: float
    push_duration_per_cell: float
```

Règles :

* pousser demande d'être sur la case adjacente opposée à la direction voulue ;
* la case de destination doit être libre et connue ;
* la durée et le bruit sont proportionnels à `mass` ;
* un survivant seul ne déplace pas un objet de `mass > 3` ; il faut deux survivants adjacents, ce qui
  donne une raison mécanique de se regrouper ;
* déplacer un objet met à jour la carte mentale de ceux qui voient la scène, et **seulement** d'eux —
  la contrainte de perception partielle du livrable 2 vaut aussi pour le décor. Un survivant qui
  revient sur une case qu'il croit dégagée peut se heurter à une caisse qu'un allié a poussée.

### Nouvelles actions (`config/planner_domain.json`)

Aucun nouveau verbe primitif n'est nécessaire pour l'essentiel ; deux le sont pour les objets lourds.

| Action | Verbe | Préconditions | Effets |
|--------|-------|---------------|--------|
| `PickUpItem` | `take` | `at(agent, ?location)`, `item_at(?item, ?location)`, `!inventory_full` | `has(agent, ?item)` |
| `UseItem` | `setFlag` | `has(agent, ?item)`, `usable(?item)` | `used(?item)`, effets du catalogue |
| `EquipWeapon` | `setFlag` | `has(agent, ?weapon)`, `!equipped(?weapon)` | `equipped(?weapon)`, `!equipped(*)` d'abord |
| `AttackWithWeapon` | `damage` | `zombie_adjacent(?threat)` ou `zombie_in_range(?threat)` si `range > 1`, `equipped(?weapon)` | `threat_down(?threat)`, `threat_handled` |
| `ThrowFirecracker` | `emit_noise` (nouveau verbe) | `has(agent, firecracker)`, `known(?location)` | `distraction_created`, `!has(agent, firecracker)` |
| `PushHeavyObject` | `push` (nouveau verbe) | `at(agent, ?from)`, `heavy_at(?object, ?target)`, `free(?destination)`, `mass_ok(?object)` | `heavy_at(?object, ?destination)`, `free(?target)` |
| `BarricadeDoor` | `setFlag` | `at(agent, ?location)`, `door_of(?door, ?location)`, `door_closed(?door)`, `equipped(toolbox)` | `door_barricaded(?door)`, `!door_open(?door)` |

`AttackWithWeapon` ne remplace pas `AttackZombie` : les deux coexistent, et GBFS arbitre par le coût.
Un survivant sans arme garde exactement le comportement du MVP.

Ces sept actions portent le domaine de **23 à 30 schémas**, et c'est la dernière fois qu'il bouge :
[G-export-pddl.md](./G-export-pddl.md) est explicitement placé après ce livrable pour cette raison, et
le [livrable 15](./15-instructions-tactiques.md) compile ses contraintes sur le domaine ainsi obtenu.
Deux verbes primitifs sont ajoutés au passage (`push`, `emit_noise`) ; ils ne s'exportent pas en PDDL,
qui ne décrit que la couche symbolique.

## Faim et soif du camp

Entre deux missions, le camp consomme. C'est une ressource **collective** : il n'y a pas de faim
individuelle à suivre case par case, ce qui garderait la simulation lisible pour rien.

```json
{
  "camp": {
    "food": 12,
    "water": 10,
    "fuel": 40,
    "ammo": 18,

    "food_per_survivor_per_day": 1,
    "water_per_survivor_per_day": 1.5,

    "starvation_max_health_penalty": 15
  }
}
```

Résolution en fin de journée (une journée = une mission) :

1. le camp consomme `food_per_survivor_per_day × survivants vivants`, idem pour l'eau ;
2. si le stock est insuffisant, chaque survivant non ravitaillé (faim ou soif) voit sa santé maximale
   plafonnée de `starvation_max_health_penalty`.

Le stock du camp est visible dans l'écran de préparation de mission
([CampScreen.cs](../Scripts/UI/CampScreen.cs)) et détaillé par le livrable 12.

## Catalogue de zones (5)

### Une zone n'est pas une table de butin

C'est le critère de sélection des cinq zones ci-dessous, et la raison de leur rendement. Une zone qui ne
fait que changer ce qu'on ramasse est une ligne de configuration déguisée en contenu : elle coûte des
tuiles et ne produit aucune décision. Les cinq retenues posent chacune un **problème tactique distinct**,
et surtout : **aucune n'ajoute de mécanique**. Chacune recompose des systèmes déjà livrés — lignes de
vue, verrouillage, bruit, fouille, objets lourds — dans une géométrie qui les fait enfin compter.

Chacune doit aussi **tenir seule**. Si le livrable 14 est abandonné, aucune des cinq ne perd sa raison
d'être.

### Zones et `ZoneKind`

`ZoneKind` ([MapZone.cs](../Hordinary.Core/World/MapZone.cs)) est une énumération : y ajouter cinq
valeurs serait cinq recompilations, contre toute la culture du projet. `ZoneKind` reste donc ce qu'il
est — la catégorie **structurelle** (`Building`, `Street`, `Alley`…), celle que la navigation et le
planificateur connaissent — et un champ **`profile`** est ajouté à `MapZone` : une chaîne pointant vers
une entrée de `config/zones.json`.

`Pharmacy` reste un `ZoneKind` parce que le domaine de planification le nomme (`at(agent, pharmacy)`) et
qu'on ne casse pas les livrables 5 à 8 pour du rangement ; il devient en plus la première entrée du
catalogue de profils. Toutes les zones suivantes sont de pures données.

### 1. Supermarché — `supermarche`

**Le problème.** La plus grande surface intérieure du jeu, et la seule où l'on ne voit rien. Les
rayonnages découpent l'espace en allées parallèles : la vue porte **le long** d'une allée, jamais **à
travers**. Une `vision_range` de 8 cases devient sans objet ; on croise un zombie à deux cases ou on ne
le croise pas. Le bruit redevient la seule perception qui porte loin, ce qui est exactement le
comportement que le livrable 3 a construit et que la carte actuelle ne met jamais à l'épreuve.

**Butin.** Nourriture et eau en volume — la meilleure source du jeu, et de loin.

**Accès.** Entrée libre : les portes vitrées sont déjà brisées. Mais le vrai stock est dans la
**réserve**, au fond, verrouillée. Le seul `ForceDoor` de la zone est donc à l'endroit le plus profond,
au bruit maximal, avec le plus long chemin de retour.

**Zombies.** Densité forte, répartie dans les allées.

**Ce qu'elle rend vrai.** Le partage de connaissances du livrable 7 — un allié dans l'allée voisine voit
ce qu'on ne voit pas — et `PushHeavyObject`, pour refermer une allée derrière soi.

**Tuiles nouvelles : aucune.** `ObstacleShelfFull`, `HeavyShelf` et `FloorTiled` sont déjà au catalogue.

### 2. Station-service — `station_service`

**Le problème.** L'esplanade est totalement à découvert : aucun couvert entre la route et la boutique.
C'est la seule zone du jeu où tirer un coup de feu revient à se signaler à tout le monde à la fois, et
donc la seule où l'arbitrage discrétion / puissance du livrable 11 se pose sans échappatoire.

**Butin.** `fuel_can`, source fiable et à peu près unique ; un peu de `canned_food` et de `water_can`
dans la boutique.

**Accès.** Les pompes sont dehors : gratuites, mais exposées. La boutique a une porte unique, souvent
verrouillée.

**Zombies.** Densité moyenne, mais **mobile** : l'esplanade est ouverte, ils convergent vite.

**Ce qu'elle rend vrai.** L'économie d'essence du van (livrable 12). Sans une source d'essence
identifiable, le coût du van est une contrainte sans réponse — c'est-à-dire une taxe. Avec elle, c'est
une boucle : le van coûte de l'essence, l'essence est ici, y aller coûte de l'essence.

**Tuiles nouvelles : 2** — `FuelPump`, `FuelPumpEmpty`.

### 3. Poste de police — `poste_police`

**Le problème.** La seule armurerie, et le seul bâtiment qu'on n'ouvre pas discrètement. Porte blindée
(`locked_chance: 1.0`), fenêtres à barreaux qui **désactivent** `BreakWindow`. Il ne reste qu'un chemin :
`ForceDoor`, bruit 6, au milieu d'un bâtiment plein. C'est le seul endroit du jeu où le joueur choisit
délibérément de faire du bruit, et où il le sait à l'avance.

**Butin.** `revolver`, `shotgun`, `ammo_box`, `crowbar`.

**Zombies.** Densité forte, concentrée dans le hall — c'est-à-dire juste derrière la porte qu'on force.

**Ce qu'elle rend vrai.** L'arbitrage GBFS entre le coût et le bruit d'une même intention.

**Tuiles nouvelles : 1** — `WindowBarred`, dont la seule propriété est de rendre `BreakWindow`
indisponible.

### 4. Quincaillerie — `quincaillerie`

**Le problème.** Des allées étroites saturées d'objets lourds. On ne traverse pas la zone, on s'y
dégage un passage. C'est la vitrine de `PushHeavyObject`, et l'inverse exact du supermarché : la
difficulté vient de la géométrie, pas des occupants.

**Butin.** `toolbox`, `crowbar`, `machete`, `bandage`, et le matériel de barricade.

**Accès.** Rideau métallique à moitié baissé : une entrée unique et étroite — donc facile à tenir au
retour, ce qui en fait un bon point de repli.

**Zombies.** Densité faible, délibérément. Une zone difficile n'a pas besoin d'être une zone dangereuse,
et alterner les deux est ce qui empêche toutes les missions de se ressembler.

**Ce qu'elle rend vrai.** `toolbox` (`ForceDoor ×0.5`), `BarricadeDoor`, et les objets lourds — trois
ajouts du livrable 11 qui, sans elle, resteraient des trouvailles de hasard.

**Tuiles nouvelles : 1** — `ShutterHalf`.

### 5. Centre d'accueil — `centre_accueil`

**Le problème.** Un gymnase où les gens se sont regroupés, donc où ils sont morts : grand volume
ouvert, longues lignes de vue, aucun couvert. Accolé à un réseau de couloirs et de salles étroites.
Deux régimes de perception opposés dans un seul bâtiment, et un passage brutal de l'un à l'autre.

**Butin.** Dispersé et individuel : beaucoup de `bandage`, `flashlight`, `radio`, `canned_food`, en
petites quantités, dans des lits de camp et des casiers. Le total est excellent, mais il faut fouiller
partout — c'est la seule zone qui exige des `SearchRoom` répétés plutôt qu'un seul bon.

**Zombies.** Très forte densité dans le gymnase, faible dans les couloirs.

**Ce qu'elle rend vrai.** La fouille et la frontier exploration du livrable 7, qui récompensent enfin
une exploration méthodique plutôt qu'un trajet direct. Et, **si** le livrable 14 est réalisé, c'est la
zone de rencontre par excellence : c'est là qu'on cherchait des survivants. Sans lui, elle reste la
meilleure source d'objets utilitaires du jeu.

**Tuiles nouvelles : 2** — `Cot` (lit de camp), `Locker` (casiers, obstacle).

### Récapitulatif — le rendement

| Zone | Problème posé | Ce qu'elle rend vrai | Tuiles nouvelles |
|------|---------------|----------------------|------------------|
| Supermarché | On ne voit rien ; le bruit remplace la vue | Partage de connaissances, objets lourds | 0 |
| Station-service | Aucun couvert ; tirer, c'est être vu | L'économie d'essence du van | 2 |
| Poste de police | Une seule entrée, et elle est bruyante | `breaching`, arbitrage coût/bruit | 1 |
| Quincaillerie | On se dégage un passage | `toolbox`, `BarricadeDoor`, objets lourds | 1 |
| Centre d'accueil | Deux régimes de vue dans un bâtiment | Fouille, exploration méthodique, rencontres | 2 |
| | | **Total** | **6** |

Six tuiles pour cinq zones : c'est le rapport visé, et il tient parce qu'aucune zone n'introduit de
mécanique. Les tuiles correspondantes sont ajoutées au catalogue de tuiles ci-dessus.

### Configuration

`config/zones.json`. Une zone est une donnée, comme une mission ou un survivant.

```json
{
  "zones": [
    {
      "id": "supermarche",
      "name": "Supermarche",
      "kind": "Building",
      "description": "Les allees sont longues et on n'y voit qu'en enfilade. La reserve du fond n'a jamais ete ouverte.",

      "footprint": [[18, 14], [30, 22]],
      "layout": "aisles",
      "aisle_spacing": 3,

      "access": { "entry_doors": [1, 2], "locked_chance": 0.0, "inner_room": { "locked_chance": 1.0 } },
      "zombie_density": 0.85,

      "loot": {
        "canned_food": [4, 9],
        "water_can": [3, 7],
        "bandage": [0, 2],
        "baseball_bat": [0, 1]
      },
      "search_rooms": [1, 2]
    }
  ]
}
```

`layout` est le seul champ qui demande du code : quatre agencements primitifs — `aisles` (allées
parallèles), `open` (volume libre), `cells` (couloirs et petites salles), `forecourt` (esplanade et
cabane) — que la passe 3 du livrable 13 applique à l'empreinte. Cinq zones, quatre agencements : c'est
là que se trouve le rendement, et pas dans les tables de butin.

### Branchements

* **[missions.json](../config/missions.json)** : le champ `objective` accepte désormais un `id` de
  profil de zone en plus d'un `ZoneKind`. « Rapporter de l'essence » devient une mission en une ligne.
* **[map_locations.json](../config/map_locations.json)** : une zone écrite à la main peut déclarer un
  `profile`. La carte de démonstration reste valide sans en déclarer aucun.
* **Livrable 13** : la passe 4 pose les profils sur des bâtiments réels et la passe 5 applique leur
  table de butin.

## Architecture concernée

`Agents/Inventory`, `World/ItemCatalogue`, `World/WeaponCatalogue`, `World/HeavyObject`,
`Game/CampStock`, `AI/Planning/ActionVerbs` (verbes `push` et `emit_noise`) — voir
[A-architecture-et-donnees.md](./A-architecture-et-donnees.md).

Nouveaux fichiers de configuration : `config/items.json`, `config/weapons.json`, `config/camp.json`.
Section `inventory` ajoutée à `config/game_config.json`.

## Étapes à créer

* [ ] **Passage à 32 px, avant tout le reste** : `"tile_size": 32` dans `config/tile_legend.json`,
      `tile_size = Vector2i(32, 32)` déclaré explicitement dans `Assets/poc.tres` (il tombe aujourd'hui
      sur le défaut 16 de Godot), `separation` remise à `Vector2i(0, 0)`, et `MinZoom` de
      [CameraController.cs](../Scripts/Game/CameraController.cs) abaissé de `0.4f` à `0.2f`.
* [ ] Doubler les constantes en **pixels monde** des dessins par code, qui ne passent pas par
      `tile_size` : rayons de halo et largeurs d'arc de [SurvivorNode.cs](../Scripts/Agents/SurvivorNode.cs)
      et [ZombieNode.cs](../Scripts/Agents/ZombieNode.cs), tailles de police (8 → 16, 7 → 14) et
      largeurs de trait des trois overlays de `Scripts/UI/`. Les rayons proportionnels
      (`_tileSize * 0.34f`, `* 0.30f`, `* 0.18f`) suivent seuls.
* [ ] **(humain)** Livrer les 57 images du catalogue de tuiles — dont les 8 déjà livrées, refaites en
      32 × 32. Rien de plus : les emplacements d'inventaire montrent ces mêmes tuiles.
* [ ] Déclarer chaque tuile produite dans `config/tile_legend.json`.
* [ ] Ajouter le modèle `Item`, les catégories et les verbes d'effet primitifs.
* [ ] Ajouter `config/items.json` et son chargeur, avec les 10 objets.
* [ ] Ajouter `config/weapons.json` et son chargeur, avec les 5 armes.
* [ ] Contraindre l'inventaire (slots, poids, pénalité de surcharge, chute à la mort).
* [ ] Ajouter les actions `PickUpItem`, `UseItem`, `EquipWeapon`, `AttackWithWeapon`, `ThrowFirecracker`.
* [ ] Appliquer les modificateurs d'arme et d'outil aux coûts/bruits des actions du domaine.
* [ ] Ajouter les objets lourds, le blocage de vue et le verbe `push`.
* [ ] Ajouter `PushHeavyObject` et `BarricadeDoor`, avec la règle des deux survivants.
* [ ] Propager le déplacement d'un objet lourd aux seules carte mentales des témoins.
* [ ] Ajouter le stock du camp et la résolution de fin de journée (faim, soif, essence, munitions).
* [ ] Ajouter le champ `profile` à `MapZone` et le fichier `config/zones.json`.
* [ ] Implémenter les quatre agencements primitifs (`aisles`, `open`, `cells`, `forecourt`).
* [ ] Décrire les cinq zones du catalogue et leurs tables de butin.
* [ ] Rendre `BreakWindow` indisponible sur `WindowBarred` et `ForceDoor` indisponible sur `ShutterHalf`.
* [ ] Accepter un `id` de profil de zone comme `objective` dans `config/missions.json`.
* [ ] Afficher le stock du camp dans l'écran de préparation de mission.

## Critère de validation

Un survivant ramasse un pied-de-biche, force une porte pour un coût moindre qu'à mains nues, pousse une
caisse pour se dégager un passage, et rapporte au camp de quoi tenir une journée de plus.

## Tests associés

À ajouter dans `Tests/` (voir [C-tests.md](./C-tests.md)) :

* chaque `id` des catalogues d'objets et d'armes possède une tuile déclarée dans
  `config/tile_legend.json`, et réciproquement aucune tuile d'objet ne pointe vers un `id` inexistant ;
* `CellType.Obstacle` a au moins une tuile déclarée ;
* chaque profil de `config/zones.json` référence un `layout` implémenté et des `id` d'objets existants ;
* `BreakWindow` n'est pas proposé par le planificateur sur une `WindowBarred`, et `ForceDoor` ne l'est
  pas sur un `ShutterHalf` ;
* une mission dont l'`objective` est un profil de zone se résout comme une mission dont l'`objective`
  est un `ZoneKind` ;
* la carte de démonstration, qui ne déclare aucun `profile`, reste chargeable et jouable ;
* un objet équipé modifie le coût de l'action attendue, et lui seule ;
* un inventaire plein refuse un ramassage supplémentaire ;
* une arme à durabilité nulle ne peut plus attaquer ;
* une arme à feu sans munitions retombe sur ses valeurs de corps à corps ;
* un objet lourd de `mass > 3` n'est pas déplaçable par un survivant seul ;
* un objet lourd déplacé n'est connu que des survivants qui ont vu la scène ;
* le stock du camp décroît du bon montant, et la pénalité de soif est le double de celle de la faim ;
