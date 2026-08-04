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
   résultat que n'importe quel prompt sur les personnages. Les calques de pawn relèvent de cet outil,
   pas d'un prompt : ils doivent s'empiler au pixel près sur une même grille, ce qu'aucune génération ne
   garantit.

Les prompts ci-dessous sont donc écrits comme des **spécifications visuelles** : ils décrivent ce qu'il
faut obtenir, que le chemin passe par une génération, une planche de référence ou un pixel posé à la
main.

## 2. Charte technique

Contraintes non négociables, valables pour toute image du projet.

| Contrainte | Valeur | Source |
|------------|--------|--------|
| **Frames d'animation** | **Aucune** | Tout mouvement est produit par le code : rotation, oscillation, teinte, bond d'attaque. Voir section 5.0 |
| Taille de tuile | **32 × 32 px** | `tile_size` de [tile_legend.json](../config/tile_legend.json) |
| Taille de personnage | **32 × 48 px** | Modèle *pawn* (section 5), 3 vues fixes. Plus haut que la tuile : la tête dépasse, c'est ce qui fait la lisibilité et le côté mignon |
| Portrait / buste | **64 × 64 px** | Grille du bas et panneau latéral (livrable 12) |
| Icône d'inventaire | **48 × 48 px** | Vue de trois quarts, pas de dessus |
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

Pour un personnage, remplacer `32x32 pixels` par `32x48 pixels` et
`single object centered` par `single character centered, chibi proportions, head about 40% of height`.

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
#8fa87c  vert olive désaturé (peau de zombie)
#5a7a6b  vert-gris (végétation morte, métal oxydé)
#3d5a6c  bleu ardoise (béton, grillage, ombre)
#2b3a45  bleu nuit (contours froids)
#c94f4f  rouge signal (uniquement : santé critique, sang de jauge — jamais en décor)
```

Un sprite ne mélange **pas** les deux familles, sauf pour son contour. C'est ce qui garantit qu'un
zombie reste identifiable en périphérie de l'écran, à toute vitesse de jeu.

## 4. Prompts — Livrable 11 : 57 tuiles et 15 icônes (72 images)

Prompt complet = préambule + colonne « Sujet » + suffixe. Le **détail décalé** est l'avant-dernière
colonne : c'est là, et seulement là, que vit le « décalé » de la direction artistique. La colonne
**Dimensions** donne la taille exacte à produire, et remplace la mention de taille du préambule.

**Toutes les tuiles de cette section font 32 × 32 px**, sans exception : c'est la définition d'une
tuile. Elle est répétée ligne à ligne pour que la table serve de bon de commande sans qu'on ait à
remonter au texte. La seule table qui n'est pas faite de tuiles est la 4.7, les icônes d'inventaire, en
48 × 48 : 49 tuiles à produire, plus 8 à refaire (4.9), plus 15 icônes.

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

Chacun a aussi une **icône d'inventaire** (section 4.7). Ajouter au préambule :
`small pickup item lying on the ground, seen from above`.

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

Les cinq armes existent en **trois images distinctes** : la tuile au sol ci-dessus (32 × 32, vue de
dessus), l'icône d'inventaire (48 × 48, trois quarts, section 4.7) et le sprite tenu (32 × 16, profil,
section 5.1). Trois cadrages, trois tailles, le même objet.

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

### 4.7 Icônes d'inventaire (15)

Mêmes objets qu'en 4.4, plus `medikit`, mais **vue de trois quarts** et en **48 × 48 px**. Préambule
modifié :

```text
cute chibi pixel art inventory icon, 48x48 pixels, three-quarter view,
single item centered on transparent background, limited palette of 4 to 6 colors,
flat shading with two tones, 1px dark outline in a darker shade of the base hue,
warm saturated colors, rounded friendly shapes, no anti-aliasing,
crisp hard pixel edges, RPG inventory icon style —
```

| Icône | Dimensions |
|-------|-----------|
| `IconCannedFood`, `IconWaterCan`, `IconBandage`, `IconMedikit`, `IconFuelCan` | **48 × 48** chacune |
| `IconAmmoBox`, `IconFlashlight`, `IconRadio`, `IconFirecracker`, `IconToolbox` | **48 × 48** chacune |
| `IconRevolver`, `IconShotgun`, `IconBaseballBat`, `IconCrowbar`, `IconMachete` | **48 × 48** chacune |

Le sujet et le détail décalé sont **identiques** à la table 4.4 : c'est le même objet, sous un autre
angle. Réutiliser les mêmes descriptions garantit la cohérence entre le sol et l'inventaire.

48 plutôt que 64 : l'icône n'est pas une tuile, elle vit dans un emplacement du panneau latéral
(livrable 12) et n'a aucune raison de s'aligner sur la grille. 48 est le double de la valeur d'origine,
donc la même densité de pixel que tout le reste — c'est le seul critère qui compte ici.

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

## 5. Personnages — modèle Pawn

Survivants et zombies partagent **un seul modèle**, repris de RimWorld : le *pawn*. C'est ce qui rend
la production tenable, et ce qui garantit qu'un zombie et un survivant se lisent comme deux états d'une
même chose — ce qu'ils sont.

> **Répartition avec le livrable 12.** Cette section donne les **prompts** et les dimensions ; le
> **catalogue de sprites** de
> [12-interface-survivants-et-van.md](./12-interface-survivants-et-van.md) donne les **noms de
> fichiers**, la convention de nommage, l'ordre d'empilement normatif et l'ordre de production. Un
> chiffre qui apparaît des deux côtés — 30 survivants, 15 zombies, 5 armes, 6 portraits, 3 états de van
> — doit être le même dans les deux documents.

### 5.0 Le modèle Pawn

#### Aucune frame d'animation

**Aucun sprite de ce projet n'est une frame d'animation.** Un pawn est un empilement d'images fixes ;
tout mouvement est produit par le code. C'est exactement la méthode de RimWorld, dont les pawns ne
possèdent aucune animation dessinée et restent pourtant parfaitement lisibles.

Ce que le code produit, et qu'il ne faut donc **pas** dessiner :

| Effet | Comment le code le produit | Remplace |
|-------|---------------------------|----------|
| Orientation | Choix parmi 3 vues + miroir horizontal pour l'ouest | 4 jeux directionnels |
| Marche | Oscillation verticale de **2 px**, sinusoïdale, à la cadence du déplacement | 4 frames de marche |
| Course | La même oscillation, plus rapide et d'amplitude **4 px** | 4 frames de course |
| Attaque | Bond de **6 px** vers la cible puis retour (interpolation courte) | 3 frames d'attaque |
| Visée | **Rotation libre du sprite d'arme** vers la cible | Un jeu d'armes par direction |
| Dégât reçu | Teinte blanche pendant 0,1 s (`Modulate`) | Frame de touche |
| Blessé | Inclinaison permanente de 6°, teinte légèrement désaturée | Jeu de sprites « blessé » |
| Panique | Tremblement de **2 px**, haute fréquence | Jeu de sprites « paniqué » |
| À terre / mort | Rotation à 90° du pawn entier + fondu | Frame « à terre » |
| Fouille | Bob accéléré et léger rétrécissement vertical | 2 frames de fouille |

Les amplitudes en pixels de cette table sont en **pixels monde** et ont toutes doublé avec la tuile :
une oscillation de 1 px sur une tuile de 32 se verrait deux fois moins qu'elle ne se voyait sur une
tuile de 16. Les angles et les durées, eux, ne changent pas — ils ne dépendent pas de l'échelle.

Le rendu actuel dessine déjà des cercles par code dans
[SurvivorNode.cs](../Scripts/Agents/SurvivorNode.cs) et
[ZombieNode.cs](../Scripts/Agents/ZombieNode.cs) : ces effets viennent **remplacer le cercle**, pas
s'ajouter à un système d'animation qui n'existe pas.

#### Les trois vues

Un pawn n'est pas librement pivoté — une tête vue de dessus n'est qu'une tache. Comme dans RimWorld, on
dessine **trois vues** et le code choisit :

```text
face (sud)   →  le pawn vient vers le joueur, visage visible
dos  (nord)  →  le pawn s'éloigne, arrière du crâne
profil (est) →  vue de côté
profil miroir (ouest) →  le même sprite, retourné par le code
```

Quatre orientations pour trois images. **Seule l'arme tenue tourne librement**, à 360°, parce qu'un
objet allongé reste lisible sous tous les angles — c'est la réconciliation exacte entre « rotation gérée
dans le code » et « pawn RimWorld ».

#### Les couches

Empilées dans cet ordre, chacune en trois vues :

```text
1. corps      (peau, torse, membres)     ← recolorable : teint
2. vêtement   (haut, bas, chaussures)    ← recolorable : couleur de tenue
3. tête       (visage, traits)           ← recolorable : teint, aligné sur le corps
4. cheveux    (coiffure)                 ← recolorable : couleur de cheveux
5. arme tenue (si équipée)               ← rotation libre, dessinée à côté du pawn
```

Chaque calque est dessiné en **niveaux de gris à deux tons**, puis teinté par le code selon
`appearance.palette` de `config/survivor_generation.json` (livrable 12). C'est ce qui donne un roster
entier à partir d'une poignée d'images.

#### Préambule personnages

Toutes les couches d'un pawn font **32 × 48 px**, alignées au pixel près sur la même grille : elles
s'empilent sans décalage, et un décalage d'un seul pixel sur la tête se voit immédiatement.

```text
cute chibi pixel art character layer, 32x48 pixels, top-down view tilted about 60 degrees,
RimWorld pawn style: a static standing figure seen from above and slightly in front,
chibi proportions with head about 40% of total height,
simple readable face with dot eyes, flat shading with two tones,
1px dark outline in a darker warm brown (never pure black),
rounded friendly shapes, clear silhouette readable at small size,
single static pose, no motion, transparent background, no anti-aliasing,
crisp hard pixel edges, paper-doll character layer for a top-down colony game —
```

Pour un calque recolorable, ajouter : `drawn in greyscale with two tones only, to be tinted in code`.

### 5.1 Survivants — deux silhouettes

Ce ne sont pas deux personnages, mais les deux bases que le générateur habille et recolore.

#### Homme — `pawn_male`

```text
a young adult man survivor, sturdy build, plain t-shirt under an open work jacket,
cargo trousers, worn boots, a small canvas backpack,
tired but determined expression
```

**Détail décalé** : le sac est manifestement un cartable d'écolier réutilisé, avec un porte-clés en
peluche accroché à la fermeture.

#### Femme — `pawn_female`

```text
a young adult woman survivor, lean build, a hooded sweatshirt under a light utility
vest with pockets, rolled-up trousers, sturdy sneakers, a satchel worn across the body,
alert focused expression
```

**Détail décalé** : le gilet est couvert de badges brodés dépareillés, dont un clairement issu d'un
parc d'attractions.

#### Ce qu'il faut produire

Jeu **minimal**, qui suffit à jouer et à générer un roster varié :

Dans l'ordre d'empilement, qui est celui du code — voir le catalogue de sprites du livrable 12 :

| Calque | Dimensions | Variantes | Vues | Images |
|--------|-----------|-----------|------|--------|
| 1. Corps | **32 × 48** | 2 (homme, femme) | 3 | 6 |
| 2. Vêtement | **32 × 48** | 3 tenues | 3 | 9 |
| 3. Tête | **32 × 48** | 2 | 3 | 6 |
| 4. Cheveux | **32 × 48** | 3 coiffures | 3 | 9 |
| **Total survivants** | | | | **30** |

Chaque couche occupe la **toile entière de 32 × 48**, même quand elle ne dessine qu'une coiffure : c'est
ce qui garantit l'empilement sans calcul d'offset. Le reste est transparent.

Jeu **étendu**, si le temps le permet, et dans cet ordre de rendement : porter les coiffures à 8
(+15 images) puis les tenues à 10 (+21 images) — les valeurs cibles de `appearance.hair` et
`appearance.clothing` dans `config/survivor_generation.json`.

Ces compteurs de configuration valent toujours le **nombre de fichiers réellement livrés**, jamais
l'objectif : un `hair: 8` avec trois coiffures sur disque produit des survivants chauves. Ils montent
en même temps que les images, et [12-interface-survivants-et-van.md](./12-interface-survivants-et-van.md)
porte le test qui le vérifie.

#### Armes tenues (5)

Une image par arme, **vue de profil**, tournée librement par le code vers la cible. C'est le seul sprite
du jeu qui pivote à 360°.

```text
cute chibi pixel art weapon, 32x16 pixels, side profile view, held-weapon sprite
for a top-down colony game, flat shading with two tones, 1px dark outline,
horizontal orientation with the grip on the left, transparent background,
no anti-aliasing, crisp hard pixel edges —
{revolver | pump-action shotgun | wooden baseball bat | red steel crowbar | machete}
```

| Sprite tenu | Dimensions |
|-------------|-----------|
| `held_revolver`, `held_shotgun` | **32 × 16** chacun |
| `held_baseball_bat`, `held_crowbar`, `held_machete` | **32 × 16** chacun |

Les détails décalés sont ceux de la table 4.4 : c'est la même arme que celle posée au sol.

**Point de pivot.** Ce sprite tourne, donc sa toile compte autant que son dessin : la poignée est à
gauche et le code fait pivoter autour d'elle. Placer la poignée au même pixel sur les cinq armes, sans
quoi elles ne tiendront pas dans la main au même endroit. Le doublement à 32 × 16 rend ce calage deux
fois plus précis, et c'est le gain le plus utile de toute la section 5.

#### Portraits — `portrait_male`, `portrait_female`

```text
cute chibi pixel art character portrait bust, 64x64 pixels, front three-quarter view,
head and shoulders only, large expressive eyes, flat shading with two tones,
1px dark outline in a darker warm brown, rounded friendly shapes, single static pose,
transparent background, no anti-aliasing, crisp hard pixel edges,
colony sim character portrait style — {sujet homme ou femme ci-dessus}
```

Trois calques recolorables (visage, cheveux, vêtement), pour que le portrait et le pawn du même
survivant se ressemblent : **2 × 3 = 6 images**.

| Calque de portrait | Dimensions | Variantes | Images |
|--------------------|-----------|-----------|--------|
| Visage | **64 × 64** | 2 (homme, femme) | 2 |
| Cheveux | **64 × 64** | 2 | 2 |
| Vêtement | **64 × 64** | 2 | 2 |

Comme pour le pawn, chaque calque occupe la toile entière et s'empile sans offset.

### 5.2 Zombies — le même pawn, refroidi

Un zombie est un pawn : même squelette, mêmes trois vues, mêmes calques. Ce qui change est la **palette**
— famille froide, section 3 — et la posture. C'est la règle de ton de la section 1 rendue littérale :
même registre graphique, et pourtant on ne les confond jamais.

**Ce qui distingue un zombie d'un survivant, et qui doit être dessiné :**

* peau vert olive désaturé (`#8fa87c`), jamais un teint chaud ;
* posture voûtée, épaules tombantes, bras pendants — visible dès la silhouette ;
* tête inclinée sur le côté, traits creusés, yeux sans pupille ;
* vêtements déchirés et délavés vers le gris-vert, dont on reconnaît encore le métier d'avant.

**Aucun détail décalé, aucune blague, aucun gore.** C'est la seule famille de sprites du projet à
laquelle la règle du décalé ne s'applique pas : la menace doit peser, sinon l'arbitrage fuir / combattre
/ aider de la Utility AI n'a plus d'enjeu. Un zombie n'est pas effrayant par le sang, il l'est par
l'immobilité de son visage.

#### Prompt

```text
{préambule personnages}
a shambling zombie, hunched posture with drooping shoulders and slack hanging arms,
head tilted to one side, hollow cheeks, blank pupil-less eyes, no blood no gore,
desaturated olive-green skin, torn faded clothing washed out to grey-green,
cold desaturated palette only —
{variante ci-dessous}
```

| Variante | Sujet | Ce qu'elle raconte |
|----------|-------|--------------------|
| `zombie_civil` | `wearing a torn shirt and trousers, ordinary and unremarkable` | Le fond de la horde |
| `zombie_employe` | `wearing a shop apron over a uniform shirt, name badge still pinned on` | Il travaillait au supermarché où vous êtes entré |
| `zombie_secours` | `wearing a torn high-visibility jacket with faded reflective stripes` | Les secours sont venus, et sont restés |

Le troisième est le plus utile des trois : une veste réfléchissante délavée reste identifiable de loin,
ce qui donne un point de repère dans une horde.

#### Ce qu'il faut produire

| Calque | Dimensions | Variantes | Vues | Images |
|--------|-----------|-----------|------|--------|
| Corps | **32 × 48** | 3 (`civil`, `employe`, `secours`) | 3 | 9 |
| Tête | **32 × 48** | 2 | 3 | 6 |
| **Total zombies** | | | | **15** |

Les zombies **réutilisent les calques de cheveux des survivants**, recolorés en tons froids et ternes.
Aucune image supplémentaire.

#### L'état du zombie reste du code

`ZombieNode.cs` colore déjà le zombie selon son état (`Calm`, `Curious`, `Searching`, `Hunting`). Cette
signalétique est **conservée telle quelle** et appliquée en teinte sur le pawn plutôt que sur un cercle :
c'est une information de debug et de lisibilité, pas une variante de sprite. Aucune image à produire
pour les quatre états.

### 5.3 Van (3 états)

Empreinte de **2 × 3 cases**, soit **64 × 96 px**. Préambule modifié : `64x96 pixels`.

| Sprite | Dimensions | Sujet | Détail décalé |
|--------|-----------|-------|---------------|
| `van_idle` | **64 × 96** | `a small delivery van seen from above, warm cream and faded orange paint, side door slid open, roof rack loaded with jerrycans and rope` | Le logo d'une entreprise de livraison de pizzas, à moitié effacé |
| `van_running` | **64 × 96** | `the same van, headlights on, rear door closed` | Le même logo, plus une guirlande de fanions sur le pare-chocs |
| `van_dry` | **64 × 96** | `the same van, tilted on a flat tire, fuel cap open, no lights, dust settled on the roof` | Un essuie-glace resté levé, comme un bras qui salue |

L'empreinte est en **cases**, pas en pixels : c'est elle qui est normative. 2 × 3 cases de 32 px font
64 × 96, et les trois états doivent être superposables au pixel près pour que le van ne se déplace pas
d'un cheveu en changeant d'état.

Trois images fixes, comme le reste : le panache d'échappement et la trépidation du moteur en marche sont
des particules et une oscillation de **2 px** produites par le code, pas des frames.

Le van est le sprite le plus regardé du jeu — départ et retour de chaque mission. C'est celui sur lequel
il faut passer le plus de temps.

### 5.4 Interface

Pour l'interface, générer des **planches groupées** plutôt qu'un élément par prompt : la cohérence d'une
interface tient à ce que ses pièces soient dessinées ensemble.

| Planche | Dimensions | Prompt |
|---------|-----------|--------|
| Cadres de panneaux | **bordure 16 px, coin 32 × 32** (9-slice) | `cute pixel art UI panel frame set, 9-slice ready, warm cream paper background with a hand-drawn brown border, slightly uneven lines as if drawn by hand, corner ornaments, three sizes, flat colors, no anti-aliasing, game HUD asset sheet` |
| Jauges | **96 × 16** chacune | `cute pixel art horizontal gauge bars, 96x16 pixels each, rounded ends, empty and full versions, four colors: warm green health, warm amber motivation, cold blue panic, pale yellow stamina, 1px dark outline, flat fill, no gradient` |
| Icônes d'état | **24 × 24** chacune | `cute pixel art status icon set, 24x24 pixels each, transparent background, flat colors, thick outline: bandaged head for wounded, wide eyes for panicked, heavy backpack for overloaded, small weapon for armed, antenna for radio, home arrow for recalled` |
| Curseurs | **32 × 32** chacun | `cute pixel art cursor set, 32x32 pixels: a rounded selection bracket, a chunky move arrow, a forbidden zone cross, a target reticle, warm cream with dark brown outline` |
| Boutons de menu | **bordure 8 px, coin 16 × 16** (9-slice) | `cute pixel art menu button set, 9-slice ready, warm ochre wooden plank look with rounded corners, three states: idle, hovered slightly brighter, pressed slightly sunken, no text` |
| Fond de titre (1 seul) | **1920 × 1080** | `cute pixel art title screen background, top-down view of a small survivor camp at dusk, tents and a campfire, warm oranges against a cool blue sky, soft and inviting, no characters, no text, wide format` |

Six planches, **25 images une fois découpées** : 3 cadres, 3 boutons, 8 jauges (4 × vide et plein),
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

| Livrable | Groupe | Dimensions | Images |
|----------|--------|-----------|--------|
| 11 | Tuiles (décor, sols, objets, lourds, camp, zones) | **32 × 32** | 49 |
| 11 | Tuiles déjà livrées, à refaire (section 4.9) | **32 × 32** | 8 |
| 11 | Icônes d'inventaire | **48 × 48** | 15 |
| 12 | Pawns survivants — corps, têtes, cheveux, vêtements × 3 vues | **32 × 48** | 30 |
| 12 | Pawns zombies — corps, têtes × 3 vues | **32 × 48** | 15 |
| 12 | Armes tenues (profil, tournées par le code) | **32 × 16** | 5 |
| 12 | Portraits — 2 bustes × 3 couches | **64 × 64** | 6 |
| 12 | Van | **64 × 96** | 3 |
| 12 | Interface (6 planches groupées) | variable, section 5.4 | 6 planches |
| 13 | Ceintures, terminaisons, brèche | **32 × 32** | 13 |
| 13 | Décor hors carte | **128 × 128** (16 tuiles de 32) | 3 planches |
| | **Total** | | **144 images + 9 planches** |

Le total passe de 136 à 144 : les huit tuiles déjà livrées sont désormais comptées, puisqu'elles sont à
refaire à la nouvelle échelle.

**Les neuf planches sont comptées à part parce qu'elles ne se comptent pas pareil.** Les six planches
d'interface sont **découpées** et donnent 25 fichiers ; les trois planches de décor **restent trois
fichiers**, chacune étant une feuille de 16 tuiles raccordables que le moteur découpe à l'affichage,
comme `atlas.png`. Le périmètre graphique complet est donc de 144 images individuelles, plus 25 pièces
d'interface, plus 3 feuilles de décor. Le livrable 12 fait le même partage sur son propre périmètre,
avec 65 livraisons et 84 fichiers.

### Toutes les dimensions, en un coup d'œil

| Élément | Dimensions | Ancienne valeur |
|---------|-----------|-----------------|
| Tuile de carte (toutes sections 4 et 6) | **32 × 32** | 16 × 16 |
| Calque de pawn — survivant et zombie | **32 × 48** | 16 × 24 |
| Arme tenue | **32 × 16** | 16 × 8 |
| Icône d'inventaire | **48 × 48** | 24 × 24 |
| Portrait / buste | **64 × 64** | 32 × 32 |
| Van (2 × 3 cases) | **64 × 96** | 32 × 48 |
| Jauge | **96 × 16** | 48 × 8 |
| Icône d'état | **24 × 24** | 12 × 12 |
| Curseur | **32 × 32** | 16 × 16 |
| Planche de décor hors carte | **128 × 128** | — |
| Fond de menu | **1920 × 1080** | — |
| Atlas `Assets/atlas.png` | **256 × 256** (8 × 8) | 256 × 16 (8 × 1) |
| Contour | **1 px** | 1 px — inchangé |
| Ombre portée | **2 px** | 1 px |
| Couleurs par sprite | **4 à 6** | 4 à 6 — inchangé |

**Aucune de ces 144 images n'est une frame d'animation.** Le passage au modèle pawn a supprimé les
50 frames de survivants au profit de 30 calques fixes, et a rendu les zombies possibles pour 15 images
de plus — à budget d'images à peu près constant, le projet couvre désormais deux familles de
personnages au lieu d'une.

**Ordre de production recommandé**, pour que chaque étape débloque du jeu jouable. Les étapes 2 à 4
sont le résumé de l'ordre détaillé du catalogue de sprites du livrable 12, qui fait foi dès qu'on entre
dans les personnages :

0. **Les 8 tuiles déjà livrées, refaites en 32** — avant tout le reste, parce que c'est le seul lot qui
   permet de vérifier la chaîne complète (atlas, `poc.tres`, `tile_legend.json`, caméra) sur du contenu
   connu. Tant que ces huit-là ne s'affichent pas correctement, produire les 49 autres est un pari.
1. **Les 49 tuiles du livrable 11** — c'est ce qui débloque le livrable prioritaire.
2. **Un corps et une tête de survivant, en trois vues — 6 images.** C'est le lot qui remplace le cercle
   de `SurvivorNode.cs` et qui valide toute la chaîne : empilement, choix de vue, miroir ouest,
   recoloration, oscillation.
3. **Le reste des pawns — 21 images** : le second corps, la seconde tête, une coiffure et une tenue
   (12), puis les trois corps de zombie (9). La menace cesse d'être un rond vert.
4. **Le van et les portraits — 9 images** — ce que le joueur regarde à chaque mission.
5. **Les planches d'interface** — après les portraits, pour que les cadres soient dessinés autour d'eux.
6. **Les bords de quartier** — en dernier : le livrable 13 est P3.
