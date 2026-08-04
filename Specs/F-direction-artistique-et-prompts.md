# F — Direction artistique et prompts de génération

> Document transverse référencé par les livrables [11](./11-objets-armes-et-economie-du-camp.md),
> [12](./12-interface-survivants-et-van.md) et [13](./13-generation-de-cartes.md), qui portent tous
> trois un prérequis graphique. Voir [00-INDEX.md](./00-INDEX.md) pour le contexte général.

## 1. Avis sur la direction artistique

Avis demandé, avis donné franchement : **le pixel art mignon est le bon choix, et le « décalé » est
celui des trois mots qu'il faut manier avec précaution.**

### Ce qui plaide pour

**La lisibilité est une exigence fonctionnelle, pas esthétique.** Sur une grille de 120 cases de côté à
16 px par tuile, le joueur doit distinguer d'un coup d'œil une conserve d'un bidon d'eau, un rayonnage
d'un mur, un survivant blessé d'un survivant paniqué. Le registre mignon — formes rondes, palette
chaude et saturée, contours francs, silhouettes simplifiées — est précisément celui qui reste lisible en
petit. Le registre réaliste et sombre, non : à 16 px, il produit de la bouillie brune.

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

**Les modèles d'image ne produisent pas de vrai pixel art à 16×16.** Ils produisent une image qui
*ressemble* à du pixel art : grille irrégulière, anticrénelage résiduel, palette dérivante, taille non
respectée. Ces prompts ne sont donc pas des prompts de production, et il serait faux de les présenter
comme tels.

Trois usages honnêtes, par ordre de rendement :

1. **Planche de références** — générer à 512 ou 1024 px pour fixer la silhouette, la palette et le ton,
   puis pixeliser à la main. C'est l'usage recommandé, et de loin celui qui rend le plus.
2. **Génération large + réduction contrôlée** — générer grand, réduire au plus proche voisin, recaler
   sur la palette imposée, nettoyer. Fonctionne pour les objets et le décor, jamais pour les
   personnages animés.
3. **Outil dédié au pixel art** — un modèle spécialisé ou un éditeur (Aseprite) donne un meilleur
   résultat que n'importe quel prompt sur les sprites animés. Les 50 frames de survivants relèvent de
   cet outil, pas d'un prompt.

Les prompts ci-dessous sont donc écrits comme des **spécifications visuelles** : ils décrivent ce qu'il
faut obtenir, que le chemin passe par une génération, une planche de référence ou un pixel posé à la
main.

## 2. Charte technique

Contraintes non négociables, valables pour toute image du projet.

| Contrainte | Valeur | Source |
|------------|--------|--------|
| **Frames d'animation** | **Aucune** | Tout mouvement est produit par le code : rotation, oscillation, teinte, bond d'attaque. Voir section 5.0 |
| Taille de tuile | **16 × 16 px** | `tile_size` de [tile_legend.json](../config/tile_legend.json) |
| Taille de personnage | **16 × 24 px** | Modèle *pawn* (section 5), 3 vues fixes. Plus haut que la tuile : la tête dépasse, c'est ce qui fait la lisibilité et le côté mignon |
| Portrait / buste | **32 × 32 px** | Grille du bas et panneau latéral (livrable 12) |
| Icône d'inventaire | **24 × 24 px** | Vue de trois quarts, pas de dessus |
| Vue | **Dessus, légèrement inclinée (~60°)** | Assez de face pour qu'on reconnaisse les objets |
| Contour | **1 px, teinte foncée de la couleur de base** | Jamais noir pur : le noir pur écrase et rend l'image dure |
| Ombre portée | **1 px, sous l'objet, couleur unique** | Fixe. Aucune ombre dirigée |
| Anticrénelage | **Aucun** | — |
| Fond | **Transparent** | — |
| Palette | **4 à 6 couleurs par sprite**, issues de la palette globale | Section 3 |

### Prompt de base

Tout prompt du document est la composition de trois parties :

```text
PRÉAMBULE + SUJET (colonnes ci-dessous) + SUFFIXE
```

**Préambule** (invariant, en anglais — les modèles y répondent mieux) :

```text
cute chibi pixel art game asset, 16x16 pixels, top-down view slightly tilted,
limited palette of 4 to 6 colors, flat shading with two tones per material,
1px dark outline in a darker shade of the base hue (never pure black),
warm saturated colors, rounded friendly shapes, clearly readable silhouette,
single object centered, transparent background, no anti-aliasing,
crisp hard pixel edges, 16-bit game asset sheet style —
```

**Suffixe** (invariant, à ajouter en négatif) :

```text
NEGATIVE: anti-aliasing, gradients, blur, soft shadows, drop shadow,
text, letters, watermark, signature, 3D render, photorealism, gore, blood,
grim desaturated palette, multiple objects, frame, border, background scenery
```

Pour un personnage, remplacer `16x16 pixels` par `16x24 pixels` et
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

## 4. Prompts — Livrable 11 : tuiles (49 images)

Prompt complet = préambule + colonne « Sujet » + suffixe. Le **détail décalé** est la troisième colonne :
c'est là, et seulement là, que vit le « décalé » de la direction artistique.

### 4.1 Décor bloquant (5)

| Tuile | Sujet | Détail décalé |
|-------|-------|---------------|
| `ObstacleCarWreck` | `a small abandoned hatchback car seen from above, doors ajar, one wheel missing, dusty pastel blue paint` | Un sapin désodorisant encore accroché au rétroviseur |
| `ObstacleRubble` | `a pile of concrete rubble and broken wooden beams, chunky rounded blocks, warm ochre and brown` | Un lavabo intact posé en équilibre au sommet |
| `ObstacleCounter` | `a shop checkout counter seen from above, warm wood top, small cash register` | Un pot à stylos renversé, stylos éparpillés en éventail |
| `ObstacleShelfFull` | `a supermarket shelving unit seen from above, still stocked with tiny colorful boxes and cans` | Une rangée entière du même produit invendable, couleur criarde |
| `ObstacleBin` | `a green metal dumpster seen from above, lid half open, warm rust patches` | Une patte de chat qui dépasse du couvercle entrouvert |

### 4.2 Variantes de sol (5)

Pour les sols, ajouter au préambule : `seamless tileable texture, no outline, no centered object`.

| Tuile | Sujet | Détail décalé |
|-------|-------|---------------|
| `FloorAsphalt` | `seamless asphalt road texture, warm dark grey, subtle cracks and patches` | Un marquage au sol repeint de travers |
| `FloorSidewalk` | `seamless pavement texture, pale warm grey slabs with visible joints` | Une dalle avec une empreinte de main séchée |
| `FloorGrass` | `seamless overgrown grass texture, warm yellow-green, a few tall weeds` | Un pissenlit unique, plus grand que de raison |
| `FloorTiled` | `seamless clean tile floor, cream and pale mint checker, pharmacy or shop interior` | Un carreau posé à l'envers |
| `FloorDirty` | `seamless dirty indoor floor, scattered papers and dust, warm muted tones` | Un ticket de caisse démesurément long qui traverse la tuile |

### 4.3 Sols de zone (2)

| Tuile | Sujet | Détail décalé |
|-------|-------|---------------|
| `FloorPharmacy` | `seamless pharmacy floor, cream tiles with a soft green cross pattern` | La croix légèrement décentrée, comme posée à la main |
| `FloorCamp` | `seamless packed dirt camp ground with wooden planks laid over it, warm browns` | Une planche peinte en rose, sans raison |

### 4.4 Objets au sol (14)

Chacun a aussi une **icône d'inventaire** (section 4.7). Ajouter au préambule :
`small pickup item lying on the ground, seen from above`.

| Tuile | Sujet | Détail décalé |
|-------|-------|---------------|
| `ItemCannedFood` | `a stack of two food cans, warm ochre labels` | Une étiquette au haricot souriant, franchement trop content |
| `ItemWaterCan` | `a plastic water jerrycan, pale blue, translucent look` | Un autocollant de vague en forme de sourire |
| `ItemBandage` | `a rolled white gauze bandage, half unrolled` | Le bout de la bande roulé en petite spirale coquette |
| `ItemFuelCan` | `a red metal fuel jerrycan with a spout` | Une tête de mort peinte à la main, plus attendrissante qu'effrayante |
| `ItemAmmoBox` | `a small green cardboard ammunition box, open, brass rounds visible` | Une cartouche qui a roulé hors de la boîte |
| `ItemFlashlight` | `a yellow plastic flashlight, lens facing up, small warm glow` | Du ruban adhésif qui tient le boîtier fermé |
| `ItemRadio` | `a chunky handheld walkie-talkie, warm orange, short antenna` | Une antenne tordue à 30 degrés |
| `ItemFirecracker` | `a small bundle of red firecrackers tied with string, fuse visible` | Un emballage de fête très joyeux, complètement hors sujet |
| `ItemToolbox` | `a red metal toolbox, closed, sturdy handle` | Un autocollant « papa » à demi décollé |
| `WeaponRevolver` | `a snub-nosed revolver seen from above, warm grey steel, wooden grip` | Une crosse en bois clair au grain exagéré, presque un jouet |
| `WeaponShotgun` | `a pump-action shotgun seen from above, wooden stock, short barrel` | Un autocollant de fleur sur la crosse |
| `WeaponBaseballBat` | `a wooden baseball bat, warm honey wood, worn grip tape` | Un prénom gravé maladroitement sur le manche |
| `WeaponCrowbar` | `a red steel crowbar, curved claw end` | Un bout de peinture écaillée en forme de cœur |
| `WeaponMachete` | `a machete with a wide blade and a black rubber grip` | Un fourreau tressé main, avec un pompon |

### 4.5 Objets lourds et barricades (5)

| Tuile | Sujet | Détail décalé |
|-------|-------|---------------|
| `HeavyCrate` | `a wooden crate seen from above, chunky planks, warm honey wood` | Un tampon « FRAGILE » posé à l'envers |
| `HeavyShelf` | `a tall metal shelving unit seen from above, warm grey, partly loaded` | Une étagère qui penche visiblement d'un côté |
| `HeavyDumpster` | `a large industrial dumpster seen from above, heavy, warm rusted green` | Un graffiti minuscule et très soigné dans un coin |
| `BarricadeDoor` | `wooden planks nailed diagonally across a doorway, overlay sprite, transparent center` | Une planche qui est manifestement un pied de table |
| `BarricadeWindow` | `wooden planks nailed across a window, overlay sprite, transparent center` | Un panneau de signalisation détourné en planche |

### 4.6 Camp (12 images)

Les trois réserves ont **trois états**. Le prompt est le même, seul le niveau change : générer les trois
d'un seul tenant garantit qu'ils partagent la palette.

| Tuile | Sujet | Détail décalé |
|-------|-------|---------------|
| `CampFoodStore` ×3 | `a wooden shelf stocked with food cans, seen from above — three versions: fully stocked, half empty, completely bare` | Un bocal de cornichons que personne ne prend jamais, présent dans les trois états |
| `CampWaterStore` ×3 | `a water tank with plastic jugs beside it — three versions: full, low, empty` | Un canard en plastique posé sur la cuve |
| `CampFuelStore` ×3 | `a row of red fuel jerrycans against a wall — three versions: six cans, two cans, none` | Un jerrycan repeint en jaune vif, dépareillé |
| `CampFire` | `a small campfire with a ring of stones and a hanging pot, warm orange glow` | Une chaussette qui sèche sur un bâton planté à côté |
| `CampTent` | `a small canvas tent seen from above, warm beige, guy ropes` | Une guirlande lumineuse tendue au-dessus |
| `CampWorkbench` | `a cluttered wooden workbench seen from above, tools scattered, vice on the corner` | Un mug de café oublié, cerné de traces circulaires |

### 4.7 Icônes d'inventaire (15)

Même objets qu'en 4.4, plus `medikit`, mais **vue de trois quarts** et en **24 × 24 px**. Préambule
modifié :

```text
cute chibi pixel art inventory icon, 24x24 pixels, three-quarter view,
single item centered on transparent background, limited palette of 4 to 6 colors,
flat shading with two tones, 1px dark outline in a darker shade of the base hue,
warm saturated colors, rounded friendly shapes, no anti-aliasing,
crisp hard pixel edges, RPG inventory icon style —
```

Le sujet et le détail décalé sont **identiques** à la table 4.4 : c'est le même objet, sous un autre
angle. Réutiliser les mêmes descriptions garantit la cohérence entre le sol et l'inventaire.

### 4.8 Zones (6)

| Tuile | Sujet | Détail décalé |
|-------|-------|---------------|
| `FuelPump` | `a fuel pump seen from above, warm red and cream, hose coiled on the side` | Un prix affiché absurdement bas, figé depuis le premier jour |
| `FuelPumpEmpty` | `the same fuel pump, hose torn off and lying on the ground, nozzle missing` | Le même prix, toujours affiché |
| `WindowBarred` | `a window with thick vertical iron bars, seen from outside, warm stone frame` | Une plante en pot morte derrière les barreaux |
| `ShutterHalf` | `a metal roller shutter half lowered, corrugated warm grey, gap underneath` | Un « OUVERT » peint sur un rideau à moitié fermé |
| `Cot` | `a folding camp bed with a rumpled blanket, seen from above, warm grey canvas` | Un ours en peluche posé sur l'oreiller |
| `Locker` | `a row of two school lockers seen from above, pale mint green metal doors` | Un autocollant de groupe de musique sur une porte |

## 5. Personnages — modèle Pawn

Survivants et zombies partagent **un seul modèle**, repris de RimWorld : le *pawn*. C'est ce qui rend
la production tenable, et ce qui garantit qu'un zombie et un survivant se lisent comme deux états d'une
même chose — ce qu'ils sont.

### 5.0 Le modèle Pawn

#### Aucune frame d'animation

**Aucun sprite de ce projet n'est une frame d'animation.** Un pawn est un empilement d'images fixes ;
tout mouvement est produit par le code. C'est exactement la méthode de RimWorld, dont les pawns ne
possèdent aucune animation dessinée et restent pourtant parfaitement lisibles.

Ce que le code produit, et qu'il ne faut donc **pas** dessiner :

| Effet | Comment le code le produit | Remplace |
|-------|---------------------------|----------|
| Orientation | Choix parmi 3 vues + miroir horizontal pour l'ouest | 4 jeux directionnels |
| Marche | Oscillation verticale de 1 px, sinusoïdale, à la cadence du déplacement | 4 frames de marche |
| Course | La même oscillation, plus rapide et d'amplitude 2 px | 4 frames de course |
| Attaque | Bond de 3 px vers la cible puis retour (interpolation courte) | 3 frames d'attaque |
| Visée | **Rotation libre du sprite d'arme** vers la cible | Un jeu d'armes par direction |
| Dégât reçu | Teinte blanche pendant 0,1 s (`Modulate`) | Frame de touche |
| Blessé | Inclinaison permanente de 6°, teinte légèrement désaturée | Jeu de sprites « blessé » |
| Panique | Tremblement de 1 px, haute fréquence | Jeu de sprites « paniqué » |
| À terre / mort | Rotation à 90° du pawn entier + fondu | Frame « à terre » |
| Fouille | Bob accéléré et léger rétrécissement vertical | 2 frames de fouille |

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

```text
cute chibi pixel art character layer, 16x24 pixels, top-down view tilted about 60 degrees,
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

| Calque | Variantes | Vues | Images |
|--------|-----------|------|--------|
| Corps | 2 (homme, femme) | 3 | 6 |
| Tête | 2 | 3 | 6 |
| Cheveux | 3 coiffures | 3 | 9 |
| Vêtement | 3 tenues | 3 | 9 |
| **Total survivants** | | | **30** |

Jeu **étendu**, si le temps le permet, et dans cet ordre de rendement : porter les coiffures à 8
(+15 images) puis les tenues à 10 (+21 images), ce qui rejoint les valeurs
`appearance.hair: 8` et `appearance.clothing: 10` de `config/survivor_generation.json`.

#### Armes tenues (5)

Une image par arme, **vue de profil**, tournée librement par le code vers la cible. C'est le seul sprite
du jeu qui pivote à 360°.

```text
cute chibi pixel art weapon, 16x8 pixels, side profile view, held-weapon sprite
for a top-down colony game, flat shading with two tones, 1px dark outline,
horizontal orientation with the grip on the left, transparent background,
no anti-aliasing, crisp hard pixel edges —
{revolver | pump-action shotgun | wooden baseball bat | red steel crowbar | machete}
```

Les détails décalés sont ceux de la table 4.4 : c'est la même arme que celle posée au sol.

#### Portraits — `portrait_male`, `portrait_female`

```text
cute chibi pixel art character portrait bust, 32x32 pixels, front three-quarter view,
head and shoulders only, large expressive eyes, flat shading with two tones,
1px dark outline in a darker warm brown, rounded friendly shapes, single static pose,
transparent background, no anti-aliasing, crisp hard pixel edges,
colony sim character portrait style — {sujet homme ou femme ci-dessus}
```

Trois calques recolorables (visage, cheveux, vêtement), pour que le portrait et le pawn du même
survivant se ressemblent : **2 × 3 = 6 images**.

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

| Calque | Variantes | Vues | Images |
|--------|-----------|------|--------|
| Corps | 3 (`civil`, `employe`, `secours`) | 3 | 9 |
| Tête | 2 | 3 | 6 |
| **Total zombies** | | | **15** |

Les zombies **réutilisent les calques de cheveux des survivants**, recolorés en tons froids et ternes.
Aucune image supplémentaire.

#### L'état du zombie reste du code

`ZombieNode.cs` colore déjà le zombie selon son état (`Calm`, `Curious`, `Searching`, `Hunting`). Cette
signalétique est **conservée telle quelle** et appliquée en teinte sur le pawn plutôt que sur un cercle :
c'est une information de debug et de lisibilité, pas une variante de sprite. Aucune image à produire
pour les quatre états.

### 5.3 Van (3 états)

Empreinte de **2 × 3 cases**, soit **32 × 48 px**. Préambule modifié : `32x48 pixels`.

| Sprite | Sujet | Détail décalé |
|--------|-------|---------------|
| `van_idle` | `a small delivery van seen from above, warm cream and faded orange paint, side door slid open, roof rack loaded with jerrycans and rope` | Le logo d'une entreprise de livraison de pizzas, à moitié effacé |
| `van_running` | `the same van, headlights on, rear door closed` | Le même logo, plus une guirlande de fanions sur le pare-chocs |
| `van_dry` | `the same van, tilted on a flat tire, fuel cap open, no lights, dust settled on the roof` | Un essuie-glace resté levé, comme un bras qui salue |

Trois images fixes, comme le reste : le panache d'échappement et la trépidation du moteur en marche sont
des particules et une oscillation de 1 px produites par le code, pas des frames.

Le van est le sprite le plus regardé du jeu — départ et retour de chaque mission. C'est celui sur lequel
il faut passer le plus de temps.

### 5.4 Interface

Pour l'interface, générer des **planches groupées** plutôt qu'un élément par prompt : la cohérence d'une
interface tient à ce que ses pièces soient dessinées ensemble.

| Planche | Prompt |
|---------|--------|
| Cadres de panneaux | `cute pixel art UI panel frame set, 9-slice ready, warm cream paper background with a hand-drawn brown border, slightly uneven lines as if drawn by hand, corner ornaments, three sizes, flat colors, no anti-aliasing, game HUD asset sheet` |
| Jauges | `cute pixel art horizontal gauge bars, 48x8 pixels each, rounded ends, empty and full versions, four colors: warm green health, warm amber motivation, cold blue panic, pale yellow stamina, 1px dark outline, flat fill, no gradient` |
| Icônes d'état | `cute pixel art status icon set, 12x12 pixels each, transparent background, flat colors, thick outline: bandaged head for wounded, wide eyes for panicked, heavy backpack for overloaded, small weapon for armed, antenna for radio, home arrow for recalled` |
| Curseurs | `cute pixel art cursor set, 16x16 pixels: a rounded selection bracket, a chunky move arrow, a forbidden zone cross, a target reticle, warm cream with dark brown outline` |
| Boutons de menu | `cute pixel art menu button set, 9-slice ready, warm ochre wooden plank look with rounded corners, three states: idle, hovered slightly brighter, pressed slightly sunken, no text` |
| Fonds de menu | `cute pixel art title screen background, top-down view of a small survivor camp at dusk, tents and a campfire, warm oranges against a cool blue sky, soft and inviting, no characters, no text, wide format` |

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

| Type | Sujet | Détail décalé |
|------|-------|---------------|
| `grillage` | `a chain-link construction fence with barbed wire on top, seen from above, cold grey-blue, sagging in places, warning signs` | Un panneau « chantier interdit au public » toujours impeccable |
| `gravats` | `a collapsed building spilling concrete rubble and twisted rebar, cold grey with warm brick accents` | Une baignoire intacte au milieu des décombres |
| `remblai` | `a raised highway embankment with a guardrail and a jam of abandoned cars, cold slate and muted colors` | Une caravane de vacances coincée en travers, auvent déployé |
| `canal` | `a concrete-banked canal with murky still water, cold blue-green, iron railing` | Un canard en plastique géant, échoué |
| `foret` | `a dense overgrown treeline with brambles, seen from above, muted cold green, impenetrable` | Une cabane d'enfant dans un arbre, en bon état |
| `barrage` | `a military quarantine roadblock, concrete blocks, sandbags, an abandoned armored vehicle, cold grey and olive` | Un distributeur de boissons intact, calé entre deux blocs |

### 6.2 Terminaisons de rue (6)

**La tuile la plus importante du livrable 13** : c'est elle qui fait la différence entre un quartier et
un rectangle découpé dans une ville. Une par type de ceinture, à l'endroit exact où une rue la
rencontre.

| Terminaison | Sujet |
|-------------|-------|
| `grillage_gate` | `a welded shut chain-link gate closing off a street, seen from above, chains and padlocks, cold grey-blue` |
| `gravats_collapse` | `a collapsed building spilling across a street and completely blocking it, seen from above` |
| `remblai_jam` | `a dense traffic jam of abandoned cars completely blocking a street, seen from above, cold muted colors` |
| `canal_bridge` | `a collapsed bridge over a canal, the roadway broken and fallen into the water, seen from above` |
| `foret_overgrowth` | `a street swallowed by dense brambles and fallen trees, seen from above, muted cold green` |
| `barrage_block` | `a military checkpoint blocking a street, concrete barriers and sandbags, seen from above, cold grey and olive` |

### 6.3 Brèche (1)

```text
a torn open section of chain-link fence, the wire peeled back and curled,
a dark gap leading into shadow, seen from above, cold grey-blue,
faint drag marks on the ground leading through it
```

**Pas de détail décalé sur celle-ci.** C'est par là qu'entrent les renforts de horde : elle relève de la
menace, donc de la règle de la section 1.

### 6.4 Décor hors carte (3 planches)

Rendu seul, jamais dans la `GridMap`. Ajouter : `background scenery, no gameplay elements`.

| Planche | Sujet |
|---------|-------|
| `backdrop_roofs` | `rooftops of small town buildings seen from directly above, tiled roofs, chimneys, water tanks, warm terracotta fading to cold grey with distance` |
| `backdrop_canopy` | `dense tree canopy seen from directly above, rounded treetops, muted green fading to cold blue-grey with distance` |
| `backdrop_silhouettes` | `distant building silhouettes seen from above, progressively flatter and darker, fading into dark blue-grey` |

Ces trois planches se **dégradent vers le froid et le sombre** à mesure qu'elles s'éloignent : c'est ce
qui prépare le fondu vers le noir de la quatrième couche, sans qu'une ligne nette apparaisse.

## 7. Récapitulatif des images à produire

| Livrable | Groupe | Images |
|----------|--------|--------|
| 11 | Tuiles (décor, sols, objets, lourds, camp, zones) | 49 |
| 11 | Icônes d'inventaire | 15 |
| 12 | Pawns survivants — corps, têtes, cheveux, vêtements × 3 vues | 30 |
| 12 | Pawns zombies — corps, têtes × 3 vues | 15 |
| 12 | Armes tenues (profil, tournées par le code) | 5 |
| 12 | Portraits — 2 bustes × 3 couches | 6 |
| 12 | Van | 3 |
| 12 | Interface (6 planches groupées) | 6 planches |
| 13 | Ceintures, terminaisons, brèche | 13 |
| 13 | Décor hors carte | 3 planches |
| | **Total** | **136 images + 9 planches** |

**Aucune de ces 136 images n'est une frame d'animation.** Le passage au modèle pawn a supprimé les
50 frames de survivants au profit de 30 calques fixes, et a rendu les zombies possibles pour 15 images
de plus — le total est resté identique, et il couvre désormais deux familles de personnages au lieu
d'une.

**Ordre de production recommandé**, pour que chaque étape débloque du jeu jouable :

1. **Les 49 tuiles du livrable 11** — c'est ce qui débloque le livrable prioritaire.
2. **Les pawns : 2 corps de survivants, 3 de zombies, et leurs têtes** — 12 images qui remplacent déjà
   les cercles actuels. Les cheveux et les tenues viennent après et n'ajoutent que de la variété.
3. **Le van et les portraits** — ce que le joueur regarde à chaque mission.
4. **Les planches d'interface** — après les portraits, pour que les cadres soient dessinés autour d'eux.
5. **Les bords de quartier** — en dernier : le livrable 13 est P3.
