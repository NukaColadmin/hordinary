# F — Direction artistique et prompts de génération

> Document transverse référencé par les livrables [11](./11-objets-armes-et-economie-du-camp.md),
> [12](./12-interface-survivants-et-van.md) et [13](./13-generation-de-cartes.md), qui portent tous
> trois un prérequis graphique. Voir [00-INDEX.md](./00-INDEX.md) pour le contexte général.

## 1. Avis sur la direction artistique

Avis demandé, avis donné franchement : **le pixel art mignon est le bon choix, et le « décalé » est
celui des trois mots qu'il faut manier avec précaution.**

### Ce qui plaide pour

**La lisibilité est une exigence fonctionnelle, pas esthétique.** Sur une grille de 120 cases de côté à
32 px par tuile, le joueur doit distinguer d'un coup d'œil une conserve d'un bidon d'eau, un rayonnage
d'un mur, un survivant blessé d'un survivant paniqué. Le registre mignon — formes rondes, palette
chaude et saturée, contours francs, silhouettes simplifiées — est précisément celui qui reste lisible en
petit.

Le passage de 16 à 32 px desserre la contrainte sans la supprimer, et il faut le dire dans ce sens :
32 px donne quatre fois plus de surface qu'une tuile de 16, ce qui rend enfin dessinables la cinquantaine
d'objets du livrable 11 — mais la carte se joue **dézoomée**, et une tuile de 32 px vue à un zoom de 0,6
occupe moins de place à l'écran qu'une tuile de 16 px vue de près. Le registre réaliste et sombre
resterait donc de la bouillie brune ; c'est la silhouette qui porte la lecture, pas le nombre de pixels.
La règle pratique : **la surface gagnée va dans le détail et le contour, jamais dans le nombre de
couleurs.**

**C'est le seul registre tenable pour une personne seule.** Un sprite mignon pardonne l'approximation ;
un sprite réaliste la souligne.

**Le contraste renforce le propos du jeu au lieu de l'affaiblir.** Ce projet produit des moments dont
l'intérêt est moral : un survivant peu loyal qui abandonne un allié blessé, un camp qui affame ses
occupants, un blessé qu'on laisse parce que le van est plein. Ces moments frappent **plus fort** avec
une image chaleureuse, pas moins. Une scène dure dans un décor dur est redondante ; la même dans un
décor accueillant est troublante.

### Ce contre quoi je mets en garde

**Le vrai risque n'est pas le mignon, c'est l'incohérence de ton.** Les textes déjà écrits dans ce
projet sont sobres et littéraires — « Elle ne force rien et ne brise rien : ce qu'elle apporte, c'est
que les autres tiennent » ([survivors.json](../config/survivors.json)), « Personne n'en est revenu sans
courir » ([missions.json](../config/missions.json)). Si les images font des blagues pendant que le texte
reste grave, le joueur ne lira pas un parti pris, il lira un accident.

**Ma recommandation : garder le texte exactement tel qu'il est.** Le ton du jeu naît du **contraste**
entre une image chaleureuse et une écriture sobre. Rendre les deux mignons supprime le contraste et
donne un jeu qui ne dit plus rien.

**Le « décalé » ne doit jamais toucher la menace.** Un zombie drôle n'est pas un zombie menaçant, et si
la menace ne pèse pas, l'arbitrage fuir / combattre / aider de la Utility AI perd tout enjeu — c'est
exactement le système que ce projet existe pour démontrer. Le décalage vit **dans les objets et les
détails** : l'étiquette de la conserve, l'autocollant sur le fusil, le nain de jardin devant le
pavillon. Jamais dans les zombies, jamais dans l'état d'un survivant.

### La règle, en une ligne

> **Formes mignonnes, palette chaude, détails décalés — sauf sur la menace et sur l'état des
> survivants, qui restent froids et lisibles.**

Traduction concrète pour la palette : les survivants, les objets et le décor en **tons chauds et
saturés** ; les zombies, le danger et les jauges critiques en **verts désaturés et bleus froids**. C'est
le même registre graphique, et pourtant on sait toujours ce qui est dangereux — y compris en périphérie
de l'écran.

### Avertissement pratique sur la génération

**Les modèles d'image ne produisent pas de vrai pixel art à 32×32.** Ils produisent une image qui
*ressemble* à du pixel art : grille irrégulière, anticrénelage résiduel, palette dérivante, taille non
respectée. Ces prompts ne sont donc pas des prompts de production, et il serait faux de les présenter
comme tels.

La cible de 32 px améliore quand même nettement le rendement de la deuxième méthode ci-dessous :
réduire une génération de 512 px vers 32 conserve une silhouette exploitable, là où la même réduction
vers 16 détruisait à peu près tout. C'est le bénéfice pratique le plus concret du changement d'échelle,
et il porte sur le décor et les objets — jamais sur les personnages.

Trois usages honnêtes, par ordre de rendement :

1. **Planche de références** — générer à 512 ou 1024 px pour fixer la silhouette, la palette et le ton,
   puis pixeliser à la main. C'est l'usage recommandé, et de loin celui qui rend le plus.
2. **Génération large + réduction contrôlée** — générer grand, réduire au plus proche voisin, recaler
   sur la palette imposée, nettoyer. Fonctionne pour les objets et le décor, jamais pour les
   personnages animés.
3. **Outil dédié au pixel art** — un modèle spécialisé ou un éditeur (Aseprite) donne un meilleur
   résultat que n'importe quel prompt sur les portraits, le seul art de personnage restant (section 5).

Les prompts ci-dessous sont donc écrits comme des **spécifications visuelles** : ils décrivent ce qu'il
faut obtenir, que le chemin passe par une génération, une planche de référence ou un pixel posé à la
main.

## 2. Charte technique

Contraintes non négociables, valables pour toute image du projet.

| Contrainte | Valeur | Source |
|------------|--------|--------|
| **Frames d'animation** | **Aucune** | Tout mouvement des survivants et des zombies est produit par le code (`SurvivorNode.cs`, `ZombieNode.cs`), pas par un sprite |
| Taille de tuile | **32 × 32 px** | `tile_size` de [tile_legend.json](../config/tile_legend.json) |
| Portrait / buste | **64 × 64 px** | Grille du bas et panneau latéral (livrable 12) |
| Icône d'inventaire | **la tuile 32 × 32 de l'objet** | Aucune image à part : un emplacement affiche la tuile à un facteur d'échelle entier |
| Vue | **Dessus, légèrement inclinée (~60°)** | Assez de face pour qu'on reconnaisse les objets |
| Contour | **1 px, teinte foncée de la couleur de base** | Jamais noir pur. **Reste à 1 px** : le contour deux fois plus fin à l'écran *est* le gain du passage à 32 |
| Ombre portée | **2 px, sous l'objet, couleur unique** | Fixe, aucune ombre dirigée. Elle porte un poids visuel, pas un détail : à 1 px elle disparaîtrait |
| Anticrénelage | **Aucun** | — |
| Fond | **Transparent** | — |
| Palette | **4 à 6 couleurs par sprite**, issues de la palette globale | Section 3. **Inchangé malgré le passage à 32** : la discipline de palette fait la cohésion de l'ensemble, la surface gagnée va dans la silhouette |

### Une seule densité de pixel pour tout le jeu

Toutes les tailles ci-dessus ont doublé en même temps, y compris celles de l'interface (section 5.4),
qui n'y était pourtant pas obligée : une jauge est en espace écran, pas en espace monde. Elles doublent
quand même, parce qu'une interface dont les pixels sont deux fois plus gros que ceux de la carte se
repère immédiatement et donne un jeu qui a l'air assemblé à partir de deux jeux.

**Toute image de ce projet est dessinée à la même densité de pixel.** C'est la règle qui rend les
tableaux de dimensions ci-dessous prévisibles : en cas de doute sur un élément non listé, il fait le
double de ce qu'il aurait fait à l'échelle 16.

### Prompt de base

Tout prompt du document est la composition de trois parties :

```text
PRÉAMBULE + SUJET (colonnes ci-dessous) + SUFFIXE
```

**Préambule** (invariant, en anglais — les modèles y répondent mieux) :

```text
cute chibi pixel art game asset, 32x32 pixels, top-down view slightly tilted,
limited palette of 4 to 6 colors, flat shading with two tones per material,
1px dark outline in a darker shade of the base hue (never pure black),
warm saturated colors, rounded friendly shapes, clearly readable silhouette,
single object centered, transparent background, no anti-aliasing,
crisp hard pixel edges, 16-bit game asset sheet style —
```

La mention `32x32 pixels` du préambule est à **remplacer par la dimension de la colonne « Dimensions »**
de chaque table ci-dessous dès qu'elle diffère. Le reste du préambule ne change jamais.

**Suffixe** (invariant, à ajouter en négatif) :

```text
NEGATIVE: anti-aliasing, gradients, blur, soft shadows, drop shadow,
text, letters, watermark, signature, 3D render, photorealism, gore, blood,
grim desaturated palette, multiple objects, frame, border, background scenery
```

Le seul prompt de personnage du projet est celui des portraits (section 5.1), déjà complet et autonome.

## 3. Palette globale

Deux familles, et c'est la règle de ton de la section 1 rendue opérationnelle.

**Chaude — survivants, objets, décor, camp**

```text
#f4e4c1  crème (sols clairs, papier)
#e8a765  ocre (bois, carton, conserves)
#d4703f  terre cuite (briques, toits)
#a8452e  rouge brique (accents, danger non menaçant)
#6b4226  brun foncé (contours chauds, structure)
```

**Froide — zombies, menace, jauges critiques, ceintures de quartier**

```text
#8fa87c  vert olive désaturé (teinte de zombie appliquée par code dans ZombieNode.cs)
#5a7a6b  vert-gris (végétation morte, métal oxydé)
#3d5a6c  bleu ardoise (béton, grillage, ombre)
#2b3a45  bleu nuit (contours froids)
#c94f4f  rouge signal (uniquement : santé critique, sang de jauge — jamais en décor)
```

Un sprite ne mélange **pas** les deux familles, sauf pour son contour. C'est ce qui garantit qu'un
zombie reste identifiable en périphérie de l'écran, à toute vitesse de jeu.

## 4. Prompts — Livrable 11 : 57 tuiles

Prompt complet = préambule + colonne « Sujet » + suffixe. Le **détail décalé** est l'avant-dernière
colonne : c'est là, et seulement là, que vit le « décalé » de la direction artistique. La colonne
**Dimensions** donne la taille exacte à produire, et remplace la mention de taille du préambule.

**Toutes les tuiles de cette section font 32 × 32 px**, sans exception : c'est la définition d'une
tuile. Elle est répétée ligne à ligne pour que la table serve de bon de commande sans qu'on ait à
remonter au texte. Toutes les tables sont faites de tuiles, y compris pour l'inventaire : 49 tuiles à
produire, plus 8 à refaire (4.9), et rien d'autre.

### 4.1 Décor bloquant (5)

| Tuile | Dimensions | Sujet | Détail décalé |
|-------|-----------|-------|---------------|
| `ObstacleCarWreck` | **32 × 32** | `a small abandoned hatchback car seen from above, doors ajar, one wheel missing, dusty pastel blue paint` | Un sapin désodorisant encore accroché au rétroviseur |
| `ObstacleRubble` | **32 × 32** | `a pile of concrete rubble and broken wooden beams, chunky rounded blocks, warm ochre and brown` | Un lavabo intact posé en équilibre au sommet |
| `ObstacleCounter` | **32 × 32** | `a shop checkout counter seen from above, warm wood top, small cash register` | Un pot à stylos renversé, stylos éparpillés en éventail |
| `ObstacleShelfFull` | **32 × 32** | `a supermarket shelving unit seen from above, still stocked with tiny colorful boxes and cans` | Une rangée entière du même produit invendable, couleur criarde |
| `ObstacleBin` | **32 × 32** | `a green metal dumpster seen from above, lid half open, warm rust patches` | Une patte de chat qui dépasse du couvercle entrouvert |

### 4.2 Variantes de sol (5)

Pour les sols, ajouter au préambule : `seamless tileable texture, no outline, no centered object`.

**Le raccord doit se faire sur 32 px, pas sur 16.** Un motif dessiné pour une tuile de 16 et
simplement agrandi produit une répétition deux fois plus visible : à cette taille, la période du motif
saute aux yeux sur une chaussée. Les cinq sols sont à redessiner, pas à mettre à l'échelle.

| Tuile | Dimensions | Sujet | Détail décalé |
|-------|-----------|-------|---------------|
| `FloorAsphalt` | **32 × 32** raccordable | `seamless asphalt road texture, warm dark grey, subtle cracks and patches` | Un marquage au sol repeint de travers |
| `FloorSidewalk` | **32 × 32** raccordable | `seamless pavement texture, pale warm grey slabs with visible joints` | Une dalle avec une empreinte de main séchée |
| `FloorGrass` | **32 × 32** raccordable | `seamless overgrown grass texture, warm yellow-green, a few tall weeds` | Un pissenlit unique, plus grand que de raison |
| `FloorTiled` | **32 × 32** raccordable | `seamless clean tile floor, cream and pale mint checker, pharmacy or shop interior` | Un carreau posé à l'envers |
| `FloorDirty` | **32 × 32** raccordable | `seamless dirty indoor floor, scattered papers and dust, warm muted tones` | Un ticket de caisse démesurément long qui traverse la tuile |

### 4.3 Sols de zone (2)

| Tuile | Dimensions | Sujet | Détail décalé |
|-------|-----------|-------|---------------|
| `FloorPharmacy` | **32 × 32** raccordable | `seamless pharmacy floor, cream tiles with a soft green cross pattern` | La croix légèrement décentrée, comme posée à la main |
| `FloorCamp` | **32 × 32** raccordable | `seamless packed dirt camp ground with wooden planks laid over it, warm browns` | Une planche peinte en rose, sans raison |

### 4.4 Objets au sol (14)

Chacune de ces tuiles est **aussi** l'icône d'inventaire de l'objet (section 4.7) : elle sera vue de
loin sur un sol chargé et de près dans un emplacement, et doit tenir dans les deux cas. Ajouter au
préambule : `small pickup item lying on the ground, seen from above`.

| Tuile | Dimensions | Sujet | Détail décalé |
|-------|-----------|-------|---------------|
| `ItemCannedFood` | **32 × 32** | `a stack of two food cans, warm ochre labels` | Une étiquette au haricot souriant, franchement trop content |
| `ItemWaterCan` | **32 × 32** | `a plastic water jerrycan, pale blue, translucent look` | Un autocollant de vague en forme de sourire |
| `ItemBandage` | **32 × 32** | `a rolled white gauze bandage, half unrolled` | Le bout de la bande roulé en petite spirale coquette |
| `ItemFuelCan` | **32 × 32** | `a red metal fuel jerrycan with a spout` | Une tête de mort peinte à la main, plus attendrissante qu'effrayante |
| `ItemAmmoBox` | **32 × 32** | `a small green cardboard ammunition box, open, brass rounds visible` | Une cartouche qui a roulé hors de la boîte |
| `ItemFlashlight` | **32 × 32** | `a yellow plastic flashlight, lens facing up, small warm glow` | Du ruban adhésif qui tient le boîtier fermé |
| `ItemRadio` | **32 × 32** | `a chunky handheld walkie-talkie, warm orange, short antenna` | Une antenne tordue à 30 degrés |
| `ItemFirecracker` | **32 × 32** | `a small bundle of red firecrackers tied with string, fuse visible` | Un emballage de fête très joyeux, complètement hors sujet |
| `ItemToolbox` | **32 × 32** | `a red metal toolbox, closed, sturdy handle` | Un autocollant « papa » à demi décollé |
| `WeaponRevolver` | **32 × 32** | `a snub-nosed revolver seen from above, warm grey steel, wooden grip` | Une crosse en bois clair au grain exagéré, presque un jouet |
| `WeaponShotgun` | **32 × 32** | `a pump-action shotgun seen from above, wooden stock, short barrel` | Un autocollant de fleur sur la crosse |
| `WeaponBaseballBat` | **32 × 32** | `a wooden baseball bat, warm honey wood, worn grip tape` | Un prénom gravé maladroitement sur le manche |
| `WeaponCrowbar` | **32 × 32** | `a red steel crowbar, curved claw end` | Un bout de peinture écaillée en forme de cœur |
| `WeaponMachete` | **32 × 32** | `a machete with a wide blade and a black rubber grip` | Un fourreau tressé main, avec un pompon |

Les cinq armes existent en **deux images distinctes** : la tuile au sol ci-dessus (32 × 32, vue de
dessus), qui sert aussi d'icône d'inventaire, et le sprite tenu (32 × 16, profil, section 5.1). Deux
cadrages, deux tailles, le même objet.

### 4.5 Objets lourds et barricades (5)

Les deux barricades sont des **calques** posés sur la tuile de porte ou de fenêtre, jamais des tuiles de
cellule : elles font donc exactement la taille d'une tuile, avec un centre transparent.

| Tuile | Dimensions | Sujet | Détail décalé |
|-------|-----------|-------|---------------|
| `HeavyCrate` | **32 × 32** | `a wooden crate seen from above, chunky planks, warm honey wood` | Un tampon « FRAGILE » posé à l'envers |
| `HeavyShelf` | **32 × 32** | `a tall metal shelving unit seen from above, warm grey, partly loaded` | Une étagère qui penche visiblement d'un côté |
| `HeavyDumpster` | **32 × 32** | `a large industrial dumpster seen from above, heavy, warm rusted green` | Un graffiti minuscule et très soigné dans un coin |
| `BarricadeDoor` | **32 × 32** calque | `wooden planks nailed diagonally across a doorway, overlay sprite, transparent center` | Une planche qui est manifestement un pied de table |
| `BarricadeWindow` | **32 × 32** calque | `wooden planks nailed across a window, overlay sprite, transparent center` | Un panneau de signalisation détourné en planche |

### 4.6 Camp (12 images)

Les trois réserves ont **trois états**. Le prompt est le même, seul le niveau change : générer les trois
d'un seul tenant garantit qu'ils partagent la palette.

| Tuile | Dimensions | Sujet | Détail décalé |
|-------|-----------|-------|---------------|
| `CampFoodStore` ×3 | **32 × 32** chacun | `a wooden shelf stocked with food cans, seen from above — three versions: fully stocked, half empty, completely bare` | Un bocal de cornichons que personne ne prend jamais, présent dans les trois états |
| `CampWaterStore` ×3 | **32 × 32** chacun | `a water tank with plastic jugs beside it — three versions: full, low, empty` | Un canard en plastique posé sur la cuve |
| `CampFuelStore` ×3 | **32 × 32** chacun | `a row of red fuel jerrycans against a wall — three versions: six cans, two cans, none` | Un jerrycan repeint en jaune vif, dépareillé |
| `CampFire` | **32 × 32** | `a small campfire with a ring of stones and a hanging pot, warm orange glow` | Une chaussette qui sèche sur un bâton planté à côté |
| `CampTent` | **32 × 32** | `a small canvas tent seen from above, warm beige, guy ropes` | Une guirlande lumineuse tendue au-dessus |
| `CampWorkbench` | **32 × 32** | `a cluttered wooden workbench seen from above, tools scattered, vice on the corner` | Un mug de café oublié, cerné de traces circulaires |

Les trois états d'une réserve doivent être **exactement superposables** : même cadrage, même empreinte,
seul le contenu change. C'est ce qui fait qu'on lit la jauge et non trois dessins différents. Le plus
simple est de dessiner l'état plein, puis d'effacer.

### 4.7 Icônes d'inventaire (0 — ce sont les tuiles)

**Rien à produire ici.** L'icône d'inventaire d'un objet est sa tuile de la section 4.4, affichée dans
un emplacement du panneau latéral à un facteur d'échelle entier. `medikit` compris.

La version précédente de cette section commandait quinze dessins de plus, vus de trois quarts et en
48 × 48. Ce n'est pas seulement une économie de quinze images. Deux dessins pour un objet, ce sont deux
vérités à tenir accordées : la première fois qu'on repeint l'une sans l'autre, une conserve n'a plus la
même tête selon qu'on la regarde par terre ou dans un sac, et le lien entre l'objet ramassé et l'objet
porté — qui est tout ce que l'inventaire a à raconter — se défait. Ici, repeindre l'atlas repeint
l'inventaire.

Deux conséquences pour la table 4.4, dont les tuiles portent maintenant les deux usages :

* **un objet doit rester lisible isolé**, sur fond de panneau, et pas seulement posé sur un sol. C'est
  ce que garantissent déjà le contour d'1 px et le fond transparent ;
* **l'agrandissement se fait à l'entier**, jamais à 1,5×. Une tuile de 32 px s'affiche à 32 ou à 64,
  au plus proche voisin ; toute autre valeur donne des pixels de largeurs inégales dans la même image.

Côté code, c'est `TileLegend.TileOf(id)` qui donne l'adresse de la tuile dans l'atlas, et `ItemIcons`
qui la découpe. Aucun nom de fichier d'icône n'existe nulle part.

### 4.8 Zones (6)

| Tuile | Dimensions | Sujet | Détail décalé |
|-------|-----------|-------|---------------|
| `FuelPump` | **32 × 32** | `a fuel pump seen from above, warm red and cream, hose coiled on the side` | Un prix affiché absurdement bas, figé depuis le premier jour |
| `FuelPumpEmpty` | **32 × 32** | `the same fuel pump, hose torn off and lying on the ground, nozzle missing` | Le même prix, toujours affiché |
| `WindowBarred` | **32 × 32** | `a window with thick vertical iron bars, seen from outside, warm stone frame` | Une plante en pot morte derrière les barreaux |
| `ShutterHalf` | **32 × 32** | `a metal roller shutter half lowered, corrugated warm grey, gap underneath` | Un « OUVERT » peint sur un rideau à moitié fermé |
| `Cot` | **32 × 32** | `a folding camp bed with a rumpled blanket, seen from above, warm grey canvas` | Un ours en peluche posé sur l'oreiller |
| `Locker` | **32 × 32** | `a row of two school lockers seen from above, pale mint green metal doors` | Un autocollant de groupe de musique sur une porte |

### 4.9 Tuiles déjà livrées, à refaire

Les huit images de `Assets/Lists/` et de `Assets/atlas.png` sont en 16 × 16 et doivent être reprises en
même temps que le reste — sans quoi la carte mélange deux échelles.

| Tuile | Dimensions | Source actuelle |
|-------|-----------|-----------------|
| `Wall` | **32 × 32** | `0:5,0` — `Assets/Lists/Wall.png` |
| `FloorIndoor` | **32 × 32** raccordable | `0:6,0` — `Indoor_Floor.png` |
| `FloorOutdoor` | **32 × 32** raccordable | `0:7,0` — `Outdoor_Floor.png` |
| `Window` | **32 × 32** | `0:4,0` — `Window.png` |
| `WindowBroken` | **32 × 32** | `0:3,0` — `Broken_Window.png` |
| `DoorClosed` | **32 × 32** | `1:0,0` — `Door_Closed.png` |
| `DoorOpen` | **32 × 32** | `2:0,0` — `Door_Open.png` |
| `Medicine` | **32 × 32** | `0:2,0` — `Medikit.png` |

Les clés `source:x,y` de [tile_legend.json](../config/tile_legend.json) et le `tile_map_data` de
`Scenes/Main.tscn` sont en **coordonnées de cellule** : ils ne changent pas. La carte dessinée à la main
survit intacte au changement d'échelle.

### 4.10 Géométrie de l'atlas

`Assets/atlas.png` fait aujourd'hui **256 × 16**, avec `separation = Vector2i(1, 0)` dans
`Assets/poc.tres` — donc un pas de 17 px, huit colonnes occupées sur 135 px, et 121 px inutilisés.

| Point | Aujourd'hui | À produire |
|---|---|---|
| `Assets/atlas.png` | 256 × 16, une rangée | **256 × 256**, grille 8 × 8, pas de 32 |
| `separation` de la source | `Vector2i(1, 0)` | **`Vector2i(0, 0)`** |
| `tile_size` de `poc.tres` | absent (défaut 16 de Godot) | **`Vector2i(32, 32)`, à déclarer explicitement** |
| PNG individuels de `Assets/Lists/` | 16 × 16 | **32 × 32** |

La gouttière d'1 px sert à empêcher le saignement de texture en filtrage bilinéaire ou en mipmap. Or
`project.godot` fixe `default_texture_filter=0` (plus proche voisin) et les fichiers `.import` portent
`mipmaps/generate=false` : elle ne protège de rien et décale toutes les coordonnées de 1. Comme tout est
redessiné, c'est le moment de la retirer et de retrouver une grille en multiples exacts de 32.

**Pourquoi 8 × 8.** Les 49 images de cette section plus les 8 tuiles déjà livrées font 57, donc 64
emplacements suffisent avec sept de marge. Si tu tiens à conserver la séparation d'1 px, l'atlas devient
263 × 263 — ça marche, mais les coordonnées cessent d'être rondes.

## 5. Personnages et interface

Un survivant est identifié en jeu par un **anneau de couleur au sol et son nom au-dessus de la tête**,
tous deux tirés par le code dans [SurvivorNode.cs](../Scripts/Agents/SurvivorNode.cs) — le corps reste le
cercle actuel. **Aucun sprite de personnage (« pawn »), de zombie ou d'état de van n'est prévu** : ce
n'est pas une étape reportée, c'est hors périmètre. Le seul art de personnage produit est le
**portrait**, utilisé par le panneau latéral et la grille du bas ; il est déjà livré dans
`Assets/Portraits/`.

### 5.1 Portraits — six personnages

**Trois femmes et trois hommes.** Le générateur de survivants du livrable 12 leur attribue un nom, une
couleur d'anneau et des caractéristiques ; il ne touche pas au dessin.

#### Femmes

| Portrait | Sujet | Détail décalé |
|----------|-------|---------------|
| `portrait_female_1` | `a young adult woman survivor, lean build, a hooded sweatshirt under a light utility vest with pockets, alert focused expression` | Le gilet est couvert de badges brodés dépareillés, dont un clairement issu d'un parc d'attractions |
| `portrait_female_2` | `a woman survivor in her sixties, small and square, a thick knitted cardigan, grey hair pulled into a tight bun, reading glasses hanging on a cord, unimpressed expression` | Elle porte encore ses pantoufles à motif de canards, qu'elle n'a jamais jugé utile de remplacer |
| `portrait_female_3` | `a young woman survivor, wiry build, a cropped jacket, short hair in two stubby braids, a bicycle helmet, restless expression` | Le casque est saturé d'autocollants de groupes de musique, dont deux se recouvrent |

#### Hommes

| Portrait | Sujet | Détail décalé |
|----------|-------|---------------|
| `portrait_male_1` | `a young adult man survivor, sturdy build, plain t-shirt under an open work jacket, tired but determined expression` | Le sac est manifestement un cartable d'écolier réutilisé, avec un porte-clés en peluche accroché à la fermeture |
| `portrait_male_2` | `a tall thin man survivor, a long overcoat several sizes too big, a scarf wrapped twice around the neck, a woollen hat pulled down low, anxious expression` | Une cravate nouée par-dessus le manteau, comme s'il partait travailler |
| `portrait_male_3` | `a stocky middle-aged man survivor, sleeveless dungarees over a striped shirt, heavy work gloves, bald head, calm patient expression` | L'un des outils de la ceinture est une spatule de cuisine, glissée entre deux clés à molette |

#### Prompt

```text
cute chibi pixel art character portrait bust, 64x64 pixels, front three-quarter view,
head and shoulders only, large expressive eyes, flat shading with two tones,
1px dark outline in a darker warm brown, rounded friendly shapes, single static pose,
transparent background, no anti-aliasing, crisp hard pixel edges,
colony sim character portrait style — {l'un des six sujets ci-dessus}
```

| Groupe | Dimensions | Images |
|--------|-----------|--------|
| Portraits | **64 × 64** | **6** |

La vignette de la grille du bas entoure le portrait de l'**anneau de couleur** du survivant : c'est le
même signe que sur la carte, donc le même geste de reconnaissance — et c'est lui qui distingue deux
survivants partageant un portrait.

### 5.2 Interface

Pour l'interface, générer des **planches groupées** plutôt qu'un élément par prompt : la cohérence d'une
interface tient à ce que ses pièces soient dessinées ensemble.

| Planche | Dimensions | Prompt |
|---------|-----------|--------|
| Cadres de panneaux | **bordure 16 px, coin 32 × 32** (9-slice) | `cute pixel art UI panel frame set, 9-slice ready, warm cream paper background with a hand-drawn brown border, slightly uneven lines as if drawn by hand, corner ornaments, three sizes, flat colors, no anti-aliasing, game HUD asset sheet` |
| Jauges | **96 × 16** chacune | `cute pixel art horizontal gauge bars, 96x16 pixels each, rounded ends, empty and full versions, three colors: warm green health, cold blue panic, pale yellow stamina, 1px dark outline, flat fill, no gradient` |
| Icônes d'état | **24 × 24** chacune | `cute pixel art status icon set, 24x24 pixels each, transparent background, flat colors, thick outline: bandaged head for wounded, wide eyes for panicked, heavy backpack for overloaded, small weapon for armed, antenna for radio` |
| Curseurs | **32 × 32** chacun | `cute pixel art cursor set, 32x32 pixels: a rounded selection bracket, a chunky move arrow, a forbidden zone cross, a target reticle, warm cream with dark brown outline` |
| Boutons de menu | **bordure 8 px, coin 16 × 16** (9-slice) | `cute pixel art menu button set, 9-slice ready, warm ochre wooden plank look with rounded corners, three states: idle, hovered slightly brighter, pressed slightly sunken, no text` |
| Fond de titre (1 seul) | **1920 × 1080** | `cute pixel art title screen background, top-down view of a small survivor camp at dusk, tents and a campfire, warm oranges against a cool blue sky, soft and inviting, no characters, no text, wide format` |

Six planches, **23 images une fois découpées** : 3 cadres, 3 boutons, 6 jauges (3 × vide et plein),
6 icônes d'état, 4 curseurs, 1 fond. Les noms de fichiers sont dans le catalogue de sprites du
livrable 12. **Un seul fond plein écran** pour les quatre écrans : les trois autres réutilisent le cadre
de panneau sur un aplat de la palette, ce qui économise trois grands formats et donne une interface plus
cohérente qu'un fond illustré par écran.

Les deux planches en 9-slice n'ont pas de taille finale : c'est la **largeur de bordure** qui est
normative, puisque le centre est étiré par le moteur. Les valeurs ci-dessus sont le double des
précédentes, comme partout ailleurs.

Le fond de menu est la seule image du projet qui n'est pas du pixel art à densité fixe : il est rendu
plein écran, sans mise à l'échelle entière, et 1920 × 1080 couvre les résolutions courantes sans
interpolation visible.

**Rappel de dépendance** : `AgentCard.CardWidth` vaut 168 dans
[AgentCard.cs](../Scripts/UI/AgentCard.cs) et n'a **pas** à doubler — c'est une largeur de disposition
en espace écran, pas une dimension d'image. Seules les images de cette table changent.

**Note de ton** : les cadres « dessinés à la main, lignes légèrement irrégulières » sont ce qui fait
basculer une interface propre du côté chaleureux. C'est le détail le plus rentable de toute cette
section.

## 6. Prompts — Livrable 13 : bords de quartier

Les ceintures sont ce que le joueur regarde le plus souvent sans s'en rendre compte
([13-generation-de-cartes.md](./13-generation-de-cartes.md)). Elles appartiennent à la famille de
palette **froide** : le bord doit être moins accueillant que l'intérieur du quartier, c'est ce qui donne
envie d'y rester.

### 6.1 Ceintures (6 types, tuiles raccordables)

Ajouter au préambule : `seamless tileable edge texture, cold desaturated palette`.

| Type | Dimensions | Sujet | Détail décalé |
|------|-----------|-------|---------------|
| `grillage` | **32 × 32** raccordable | `a chain-link construction fence with barbed wire on top, seen from above, cold grey-blue, sagging in places, warning signs` | Un panneau « chantier interdit au public » toujours impeccable |
| `gravats` | **32 × 32** raccordable | `a collapsed building spilling concrete rubble and twisted rebar, cold grey with warm brick accents` | Une baignoire intacte au milieu des décombres |
| `remblai` | **32 × 32** raccordable | `a raised highway embankment with a guardrail and a jam of abandoned cars, cold slate and muted colors` | Une caravane de vacances coincée en travers, auvent déployé |
| `canal` | **32 × 32** raccordable | `a concrete-banked canal with murky still water, cold blue-green, iron railing` | Un canard en plastique géant, échoué |
| `foret` | **32 × 32** raccordable | `a dense overgrown treeline with brambles, seen from above, muted cold green, impenetrable` | Une cabane d'enfant dans un arbre, en bon état |
| `barrage` | **32 × 32** raccordable | `a military quarantine roadblock, concrete blocks, sandbags, an abandoned armored vehicle, cold grey and olive` | Un distributeur de boissons intact, calé entre deux blocs |

Une ceinture fait 4 à 8 cases d'épaisseur et court sur tout un côté de carte : c'est le motif le plus
répété du jeu. Le raccord doit donc tenir **dans les deux axes** sur 32 px, et le détail décalé doit
rester assez discret pour supporter d'apparaître des dizaines de fois côte à côte.

### 6.2 Terminaisons de rue (6)

**La tuile la plus importante du livrable 13** : c'est elle qui fait la différence entre un quartier et
un rectangle découpé dans une ville. Une par type de ceinture, à l'endroit exact où une rue la
rencontre.

| Terminaison | Dimensions | Sujet |
|-------------|-----------|-------|
| `grillage_gate` | **32 × 32** | `a welded shut chain-link gate closing off a street, seen from above, chains and padlocks, cold grey-blue` |
| `gravats_collapse` | **32 × 32** | `a collapsed building spilling across a street and completely blocking it, seen from above` |
| `remblai_jam` | **32 × 32** | `a dense traffic jam of abandoned cars completely blocking a street, seen from above, cold muted colors` |
| `canal_bridge` | **32 × 32** | `a collapsed bridge over a canal, the roadway broken and fallen into the water, seen from above` |
| `foret_overgrowth` | **32 × 32** | `a street swallowed by dense brambles and fallen trees, seen from above, muted cold green` |
| `barrage_block` | **32 × 32** | `a military checkpoint blocking a street, concrete barriers and sandbags, seen from above, cold grey and olive` |

Une terminaison se raccorde à **la ceinture du même type d'un côté et à une rue de l'autre** : ses deux
bords doivent coïncider au pixel près avec `grillage` etc. et avec `FloorAsphalt`. C'est la seule tuile
du projet contrainte sur deux voisins différents, et c'est ce qui la rend difficile.

### 6.3 Brèche (1)

**Dimensions : 32 × 32.**

```text
a torn open section of chain-link fence, the wire peeled back and curled,
a dark gap leading into shadow, seen from above, cold grey-blue,
faint drag marks on the ground leading through it
```

**Pas de détail décalé sur celle-ci.** C'est par là qu'entrent les renforts de horde : elle relève de la
menace, donc de la règle de la section 1.

### 6.4 Décor hors carte (3 planches)

Rendu seul, jamais dans la `GridMap`. Ajouter : `background scenery, no gameplay elements`.

Le décor occupe 8 à 16 cases au-delà de la ceinture et se pose **en tuiles** comme le reste
([13-generation-de-cartes.md](./13-generation-de-cartes.md), couche 3). Chaque planche est donc un jeu
de tuiles raccordables de 32 × 32, livré en une image de 4 × 4 variantes.

| Planche | Dimensions | Sujet |
|---------|-----------|-------|
| `backdrop_roofs` | **128 × 128** — 16 tuiles de 32 × 32 raccordables | `rooftops of small town buildings seen from directly above, tiled roofs, chimneys, water tanks, warm terracotta fading to cold grey with distance` |
| `backdrop_canopy` | **128 × 128** — 16 tuiles de 32 × 32 raccordables | `dense tree canopy seen from directly above, rounded treetops, muted green fading to cold blue-grey with distance` |
| `backdrop_silhouettes` | **128 × 128** — 16 tuiles de 32 × 32 raccordables | `distant building silhouettes seen from above, progressively flatter and darker, fading into dark blue-grey` |

Seize variantes par planche est un choix, pas une contrainte du moteur : c'est le seuil au-delà duquel
l'œil cesse de repérer la répétition sur une bande de 16 cases. Le fondu vers le noir de la couche 4 est
produit par le code (`Modulate`), jamais dessiné dans ces tuiles — sans quoi elles ne seraient plus
raccordables.

Ces trois planches se **dégradent vers le froid et le sombre** à mesure qu'elles s'éloignent : c'est ce
qui prépare le fondu vers le noir de la quatrième couche, sans qu'une ligne nette apparaisse.

## 7. Récapitulatif des images à produire

Les survivants et les zombies n'ont pas de sprite de corps : ils restent le cercle + anneau produit par
code dans [SurvivorNode.cs](../Scripts/Agents/SurvivorNode.cs) et
[ZombieNode.cs](../Scripts/Agents/ZombieNode.cs). Le van garde son unique skin déjà livré
(`Assets/Others/Van.png`, non répertorié ci-dessous car hors périmètre de production restant).

| Livrable | Groupe | Dimensions | Images |
|----------|--------|-----------|--------|
| 11 | Tuiles (décor, sols, objets, lourds, camp, zones) | **32 × 32** | 49 |
| 11 | Tuiles déjà livrées, à refaire (section 4.9) | **32 × 32** | 8 |
| 12 | Portraits — un par personnage | **64 × 64** | 6 |
| 12 | Interface (6 planches groupées) | variable, section 5.2 | 6 planches |
| 13 | Ceintures, terminaisons, brèche | **32 × 32** | 13 |
| 13 | Décor hors carte | **128 × 128** (16 tuiles de 32) | 3 planches |
| | **Total** | | **76 images + 9 planches** |

**Les neuf planches sont comptées à part parce qu'elles ne se comptent pas pareil.** Les six planches
d'interface sont **découpées** et donnent 23 fichiers ; les trois planches de décor **restent trois
fichiers**, chacune étant une feuille de 16 tuiles raccordables que le moteur découpe à l'affichage,
comme `atlas.png`.

### Toutes les dimensions, en un coup d'œil

| Élément | Dimensions |
|---------|-----------|
| Tuile de carte (toutes sections 4 et 6) | **32 × 32** |
| Icône d'inventaire | **la tuile de l'objet, 32 × 32** |
| Portrait / buste | **64 × 64** |
| Van (skin fixe, déjà livré) | **64 × 96** |
| Jauge | **96 × 16** |
| Icône d'état | **24 × 24** |
| Curseur | **32 × 32** |
| Planche de décor hors carte | **128 × 128** |
| Fond de menu | **1920 × 1080** |
| Atlas `Assets/atlas.png` | **256 × 256** (8 × 8) |
| Contour | **1 px** |
| Ombre portée | **2 px** |
| Couleurs par sprite | **4 à 6** |

**Aucune de ces images n'est une frame d'animation.**

**Ordre de production recommandé**, pour que chaque étape débloque du jeu jouable :

0. **Les 8 tuiles déjà livrées, refaites en 32** — avant tout le reste, parce que c'est le seul lot qui
   permet de vérifier la chaîne complète (atlas, `poc.tres`, `tile_legend.json`, caméra) sur du contenu
   connu. Tant que ces huit-là ne s'affichent pas correctement, produire les 49 autres est un pari.
1. **Les 49 tuiles du livrable 11** — c'est ce qui débloque le livrable prioritaire.
2. **Les 6 portraits** — ce que le joueur regarde le plus souvent dans le panneau latéral et la grille
   du bas. Déjà livrés.
3. **Les planches d'interface** — après les portraits, pour que les cadres soient dessinés autour d'eux.
4. **Les bords de quartier** — en dernier : le livrable 13 est P3.
