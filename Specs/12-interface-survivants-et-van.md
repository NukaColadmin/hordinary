# Livrable 12 — Refonte de l'interface, survivants générés et van d'extraction

> Voir [00-INDEX.md](./00-INDEX.md) pour le contexte général du projet.

**Priorité : P2 — Haute.** Second livrable post-MVP. Le livrable 11 ajoute une matière que l'interface
actuelle ne sait pas montrer : un inventaire, une arme équipée, un stock de camp, des survivants qui ne
s'appellent plus Alice, Bob et Claire. Sans ce livrable, le 11 est jouable mais illisible.

**Dépendances** : livrable 11 (le van consomme l'essence du stock de camp ; le panneau latéral affiche
l'inventaire). **Bloqué au départ** par le prérequis humain ci-dessous.

## Objectif

Remplacer l'interface de démonstration du livrable 8 par une interface de jeu : grille de survivants en
bas d'écran, panneau latéral détaillé, menus, sprites de survivants ; et faire de la zone d'extraction
un véhicule qui coûte de l'essence à faire rouler.

## Prérequis (humain, bloquant)

Livrés dans `Assets/`, au format des existants. Le registre graphique, la charte technique et **un
prompt de génération par sprite** sont dans
[F-direction-artistique-et-prompts.md](./F-direction-artistique-et-prompts.md), section 5. Le détail
image par image — nom de fichier, dimensions, vue, recolorabilité — est dans le **catalogue de sprites**
plus bas ; les six lignes ci-dessous sont la liste de contrôle.

* [ ] **Survivants** : 30 images de **32 × 48 px** — corps (2 × 3 vues), têtes (2 × 3), coiffures
      (3 × 3), tenues (3 × 3) ;
* [ ] **Zombies** : 15 images de **32 × 48 px** — 3 corps, 2 têtes, en 3 vues. Ils réutilisent les
      coiffures des survivants, recolorées ;
* [ ] **Armes tenues** : 5 sprites de profil en **32 × 16 px** ;
* [ ] **Portraits** : 6 images de **64 × 64 px** — 2 bustes × 3 couches ;
* [ ] **Van d'extraction** : 3 états de **64 × 96 px** ;
* [ ] **Interface** : 6 planches, soit **25 images** une fois découpées.

**65 livraisons au total** — 59 images individuelles plus 6 planches d'interface, soit **84 fichiers**
une fois les planches découpées. C'est 65 qui sert à planifier et 84 qui sert à cocher ; les deux
chiffres sont dans le catalogue, avec leur détail. Aucun de ces fichiers n'est une frame d'animation.

Comme au livrable 11, le code peut être écrit et testé avant, mais le livrable n'est pas terminé tant
que le résultat n'est pas visible en jeu.

## Personnages : modèle pawn, animation par code

**Aucun sprite de personnage n'est une frame d'animation.** Un survivant comme un zombie est un *pawn*
au sens de RimWorld : un empilement d'images fixes — corps, vêtement, tête, cheveux, arme — dessiné en
**trois vues** (face, dos, profil), la quatrième orientation étant le profil retourné par le code.

Tout le mouvement est produit par `Scripts/Agents/` et remplace le cercle actuellement dessiné par
[SurvivorNode.cs](../Scripts/Agents/SurvivorNode.cs) et
[ZombieNode.cs](../Scripts/Agents/ZombieNode.cs) :

| Effet | Production |
|-------|-----------|
| Orientation | Choix parmi les 3 vues, miroir horizontal pour l'ouest |
| Marche / course | Oscillation verticale de 2 à 4 px, cadence proportionnelle à la vitesse |
| Attaque | Bond de 6 px vers la cible puis retour |
| Visée | Rotation libre du sprite d'arme — le seul sprite du jeu qui pivote à 360° |
| Dégât reçu | Teinte blanche 0,1 s (`Modulate`) |
| Blessé | Inclinaison permanente de 6°, légère désaturation |
| Panique | Tremblement de 2 px, haute fréquence |
| Fouille | Oscillation accélérée et léger rétrécissement vertical |
| À terre / mort | Rotation à 90° du pawn entier, puis fondu |
| État du zombie | Les quatre teintes déjà en place dans `ZombieNode.cs`, appliquées au pawn |

Les amplitudes sont en **pixels monde** et valent le double de ce qu'elles valaient avant le passage de
la tuile à 32 px : une oscillation de 1 px sur une tuile de 32 se verrait deux fois moins qu'elle ne se
voyait sur une tuile de 16. Les angles et les durées ne dépendent pas de l'échelle et ne changent pas.

Ce choix divise la production graphique par deux et rend les zombies possibles dans le même budget —
voir [F-direction-artistique-et-prompts.md](./F-direction-artistique-et-prompts.md), section 5.

## Catalogue de sprites

L'équivalent, pour ce livrable, du catalogue de tuiles du livrable 11 : la liste exhaustive de ce qu'il
faut produire, nommée, dimensionnée, prête à servir de bon de commande. Les prompts correspondants sont
dans la section 5 de [F-direction-artistique-et-prompts.md](./F-direction-artistique-et-prompts.md).

**Trois règles valables pour tout ce qui suit :**

* **chaque calque occupe la toile entière**, même une coiffure qui ne couvre que le haut du crâne. Le
  reste est transparent. C'est ce qui permet d'empiler sans calculer un seul décalage — et un décalage
  d'un pixel sur une tête se voit immédiatement ;
* **les trois vues sont `face` (sud), `dos` (nord) et `profil` (est)**. L'ouest est le profil retourné
  par le code. Il n'existe pas de quatrième vue à dessiner ;
* **les calques recolorables sont livrés en niveaux de gris à deux tons.** La teinte vient du code, à
  partir d'`appearance.palette`. Un calque livré en couleur ne peut pas servir au générateur.

### Convention de nommage

```text
pawn_{couche}_{variante}_{vue}.png      survivants
zpawn_{couche}_{variante}_{vue}.png     zombies
held_{arme}.png                         armes tenues
portrait_{couche}_{variante}.png        portraits
van_{etat}.png                          van
ui_{famille}_{variante}.png             interface
```

### 1. Survivants — 30 images de 32 × 48 px

Ordre d'empilement, de bas en haut. Il est normatif : c'est celui que le code applique.

| # | Calque | Fichiers | Variantes | Vues | Images | Recolorable |
|---|--------|----------|-----------|------|--------|-------------|
| 1 | Corps | `pawn_body_{male,female}_{face,dos,profil}` | 2 | 3 | **6** | oui — teint |
| 2 | Vêtement | `pawn_cloth_{1,2,3}_{face,dos,profil}` | 3 | 3 | **9** | oui — couleur de tenue |
| 3 | Tête | `pawn_head_{a,b}_{face,dos,profil}` | 2 | 3 | **6** | oui — teint, **même teinte que le corps** |
| 4 | Cheveux | `pawn_hair_{1,2,3}_{face,dos,profil}` | 3 | 3 | **9** | oui — couleur de cheveux |
| | | | | | **30** | |

Les deux corps sont `pawn_male` et `pawn_female` de la section 5.1 de la spec F : **ce ne sont pas deux
personnages**, mais les deux bases que le générateur habille et recolore pour produire tout le roster.

La tête et le corps partagent la même entrée de palette. Les dessiner dans deux séances différentes est
le meilleur moyen d'obtenir un cou qui ne raccorde pas : le point de jonction se fixe une fois, sur le
corps `male` vu de face, et les cinq autres corps s'y alignent.

**La vue de dos est la moins intéressante à dessiner et la plus regardée.** Un survivant qui explore
s'éloigne du joueur : c'est le dos qu'on voit la majorité du temps. Ne pas la bâcler.

#### Jeu étendu (facultatif)

Le jeu minimal ci-dessus — 2 corps, 2 têtes, 3 coiffures, 3 tenues — suffit à jouer et à générer un
roster varié. Deux extensions sont prévues, dans cet ordre de rendement :

| Extension | Images ajoutées | Effet |
|-----------|-----------------|-------|
| Coiffures 3 → 8 | +15 (5 × 3 vues) | Le plus rentable : c'est la couche qu'on lit en premier sur un pawn |
| Tenues 3 → 10 | +21 (7 × 3 vues) | Variété du roster, mais moins distinctif à cette taille |

`config/survivor_generation.json` est **créé par ce livrable** : ses compteurs `appearance` valent donc
`{ "body": 2, "hair": 3, "clothing": 3 }` dès le premier jour, et montent à 8 et 10 **le jour où les
images correspondantes existent**, jamais avant. Un `hair: 8` avec trois coiffures sur disque produirait
cinq survivants sur huit sans cheveux, et le défaut ne se voit qu'en jeu.

Les valeurs 8 et 10 apparaissent dans la section 5.1 de
[F-direction-artistique-et-prompts.md](./F-direction-artistique-et-prompts.md) : ce sont des **valeurs
cibles de production**, pas des valeurs de configuration.

Deux façons de tenir cette règle ; celle retenue est la seconde. Que le générateur **borne
silencieusement son tirage** au nombre de fichiers présents fait dépendre le comportement du contenu
d'`Assets/`, ce qui se découvre trois semaines plus tard. Qu'il **échoue au chargement** quand un
compteur dépasse le nombre de calques livrés est plus brutal, se teste, et donne l'erreur au bon moment.
C'est ce que vérifie le test « aucun index d'apparence ne dépasse le nombre de calques présents ».

> **`body` ne monte jamais.** Le modèle pawn ne prévoit que **2 corps** (homme, femme) : la variété
> vient des couches et de la recoloration, pas de silhouettes supplémentaires. Un `appearance.body`
> supérieur à `2` est une erreur, pas une extension.

### 2. Zombies — 15 images de 32 × 48 px

Même squelette, mêmes vues, mêmes points de jonction. Ce qui change est la palette (famille froide) et
la posture — voûtée, bras pendants, tête inclinée.

| # | Calque | Fichiers | Variantes | Vues | Images | Recolorable |
|---|--------|----------|-----------|------|--------|-------------|
| 1 | Corps | `zpawn_body_{civil,employe,secours}_{face,dos,profil}` | 3 | 3 | **9** | oui — teint froid |
| 2 | Tête | `zpawn_head_{a,b}_{face,dos,profil}` | 2 | 3 | **6** | oui — teint froid |
| | | | | | **15** | |

Le vêtement fait partie du corps chez le zombie : les trois variantes *sont* trois tenues (civil, tablier
d'employé, gilet de secours). Il n'y a donc pas de calque de vêtement séparé, et c'est ce qui fait tenir
les zombies dans 15 images au lieu de 24.

**Les coiffures sont celles des survivants**, recolorées en tons froids et ternes par le code. Aucune
image supplémentaire — et c'est la seule mutualisation du livrable, donc la seule à ne pas casser.

**Aucun détail décalé, aucune blague, aucun gore.** C'est la seule famille de sprites du projet à
laquelle la règle du décalé ne s'applique pas : si la menace ne pèse pas, l'arbitrage fuir / combattre /
aider de la Utility AI perd son enjeu.

### 3. Armes tenues — 5 images de 32 × 16 px

| Fichier | Arme | Vue |
|---------|------|-----|
| `held_revolver` | Revolver | profil, poignée à gauche |
| `held_shotgun` | Fusil à pompe | profil, poignée à gauche |
| `held_baseball_bat` | Batte de baseball | profil, poignée à gauche |
| `held_crowbar` | Pied-de-biche | profil, poignée à gauche |
| `held_machete` | Machette | profil, poignée à gauche |

**Le seul sprite du jeu qui pivote à 360°**, donc le seul dont la position sur la toile est une donnée
et pas une mise en page. La poignée doit tomber **au même pixel sur les cinq**, sinon les armes ne
tiennent pas dans la main au même endroit et le défaut se voit à chaque rotation. Le point de pivot se
fixe une fois, sur `held_revolver`, et les quatre autres s'y calent.

Ces cinq armes existent déjà en deux autres versions au livrable 11 — tuile au sol 32 × 32 vue de dessus
et icône d'inventaire 48 × 48 en trois quarts. Trois cadrages, trois tailles, le même objet, et le même
détail décalé sur les trois.

Non recolorable : une arme a sa couleur.

### 4. Portraits — 6 images de 64 × 64 px

Buste de face trois quarts, tête et épaules, pour la grille du bas et le panneau latéral.

| # | Calque | Fichiers | Variantes | Images | Recolorable |
|---|--------|----------|-----------|--------|-------------|
| 1 | Visage | `portrait_face_{male,female}` | 2 | **2** | oui — teint |
| 2 | Cheveux | `portrait_hair_{male,female}` | 2 | **2** | oui — couleur de cheveux |
| 3 | Vêtement | `portrait_cloth_{male,female}` | 2 | **2** | oui — couleur de tenue |
| | | | | **6** | |

Les trois couches se recolorent avec **les mêmes entrées de palette que le pawn du même survivant** :
c'est toute la raison d'être du découpage en couches ici. Un portrait dont les cheveux ne sont pas de la
couleur de ceux du pawn donne l'impression que la grille du bas montre quelqu'un d'autre.

Le portrait ne suit pas les trois vues : il est toujours de face. Deux couches de cheveux pour trois
coiffures de pawn — huit à terme — est un écart assumé : la coiffure du portrait est générique, elle
n'est pas la même que celle du pawn et n'a pas à la suivre. Seule la **couleur** est partagée.

### 5. Van — 3 images de 64 × 96 px

Empreinte de 2 × 3 cases, donc 64 × 96 px à 32 px la case. **L'empreinte est la donnée normative** ; les
pixels s'en déduisent.

| Fichier | État | Quand |
|---------|------|-------|
| `van_idle` | À l'arrêt, portière latérale ouverte | Garé au camp, ou en attente au point de dépose |
| `van_running` | Moteur en marche, phares allumés | Pendant `departure_delay`, et au départ |
| `van_dry` | Penché sur un pneu à plat, bouchon ouvert | `fuel_return_reserve: false` et réservoir vide |

Les trois états sont **superposables au pixel près** : le van ne doit pas se déplacer d'un cheveu en
changeant d'état. Le panache d'échappement et la trépidation du moteur sont des particules et une
oscillation de 2 px produites par le code, pas des images.

C'est le sprite le plus regardé du jeu — départ et retour de chaque mission. C'est celui sur lequel il
faut passer le plus de temps.

### 6. Interface — 6 planches, 25 images

Les planches se génèrent groupées, pour que les pièces d'une interface soient dessinées ensemble ; elles
se découpent ensuite en images. Le décompte ci-dessous est celui des images découpées.

| Planche | Fichiers | Dimensions | Images |
|---------|----------|-----------|--------|
| Cadres de panneaux | `ui_frame_{small,medium,large}` | 9-slice, bordure **16 px** | **3** |
| Boutons de menu | `ui_button_{idle,hover,pressed}` | 9-slice, bordure **8 px** | **3** |
| Jauges | `ui_gauge_{health,sanity,panic,stamina}_{empty,full}` | **96 × 16** | **8** |
| Icônes d'état | `ui_icon_{wounded,panicked,overloaded,armed,radio,recalled}` | **24 × 24** | **6** |
| Curseurs | `ui_cursor_{select,move,forbidden,target}` | **32 × 32** | **4** |
| Fond de titre | `ui_bg_title` | **1920 × 1080** | **1** |
| | | | **25** |

Les quatre jauges correspondent aux quatre valeurs affichées par le panneau latéral : santé,
`sanity` (motivation), panique, stamina. Les six icônes d'état correspondent exactement aux six états
listés par la grille des survivants — blessé, paniqué, surchargé, arme équipée, radio, rappelé au camp.
Aucune des deux listes n'est décorative : les ajouter ou en retirer demande de toucher au panneau.

**Un seul fond plein écran.** Les quatre écrans du livrable — titre, camp, préparation de mission, fin
de partie — ne demandent pas quatre fonds : les trois derniers réutilisent le cadre de panneau sur un
aplat de la palette. C'est une économie de trois images grand format, et ça donne une interface plus
cohérente qu'un fond illustré par écran.

`ui_bg_title` est la seule image du projet qui n'est pas du pixel art à densité fixe : elle est rendue
plein écran sans mise à l'échelle entière.

### Récapitulatif

| Groupe | Dimensions | Images |
|--------|-----------|--------|
| Survivants — 4 couches × 3 vues | 32 × 48 | 30 |
| Zombies — 2 couches × 3 vues | 32 × 48 | 15 |
| Armes tenues | 32 × 16 | 5 |
| Portraits — 3 couches | 64 × 64 | 6 |
| Van — 3 états | 64 × 96 | 3 |
| Interface — 6 planches découpées | variable | 25 |
| **Total** | | **84** |

Le total de **65 livraisons** annoncé dans les prérequis est le même travail compté à la commande : les
59 images des cinq premières lignes, plus les six planches d'interface comptées **comme six planches**
et non comme les 25 fichiers qu'elles donnent une fois découpées. 65 pour planifier, 84 pour cocher.

**Extensions facultatives** : +15 images pour porter les coiffures à 8, +21 pour porter les tenues à 10.

### Ordre de production

Chaque étape doit rendre le jeu plus jouable que la précédente, sinon l'ordre ne sert à rien.

1. **`pawn_body_male` et `pawn_head_a`, trois vues chacun — 6 images.** C'est le lot qui permet de
   remplacer le cercle de `SurvivorNode.cs` et de vérifier toute la chaîne : empilement, choix de vue,
   miroir ouest, recoloration, oscillation de marche. Tant que ces six-là ne s'affichent pas
   correctement, produire les 78 autres est un pari.
2. **Le corps féminin, la seconde tête, une coiffure, une tenue — 12 images.** Le générateur produit
   alors des survivants réellement distincts.
3. **Les trois corps de zombie — 9 images.** La menace cesse d'être un rond vert.
4. **Le van et les portraits — 9 images.** Ce que le joueur regarde à chaque mission.
5. **Les jauges, icônes d'état et curseurs — 18 images.** L'interface devient lisible sans texte.
6. **Le reste — 30 images** : les deux coiffures et les deux tenues restantes (12), les deux têtes de
   zombie (6), les cinq armes tenues (5), les cadres, les boutons et le fond de titre (7). Les armes
   tenues sont dans ce lot parce qu'un pawn sans arme visible reste jouable, pas parce qu'elles sont
   faciles — leur point de pivot commun est le calage le plus exigeant du livrable.

## Interface

L'interface actuelle ([Hud.cs](../Scripts/UI/Hud.cs), [AgentPanel.cs](../Scripts/UI/AgentPanel.cs),
[PlanPanel.cs](../Scripts/UI/PlanPanel.cs), [InfoPanel.cs](../Scripts/UI/InfoPanel.cs)) était une
interface d'observation : trois blocs de texte dont deux ne s'affichent qu'en mode debug
(`debug.hud_text`). Elle est refondue en trois zones permanentes.

```text
┌──────────────────────────────────────────────┬────────────────────────┐
│                                              │  PANNEAU SURVIVANT     │
│                                              │  ────────────────      │
│                                              │  Portrait  Nom, rôle   │
│                CARTE                         │                        │
│                                              │  Jauges                │
│                                              │  Caractéristiques      │
│                                              │  Personnalité          │
│                                              │  Aptitudes             │
│                                              │  Inventaire (6 slots)  │
│                                              │  Objectif / action     │
│                                              │  Plan courant          │
├──────────────────────────────────────────────┴────────────────────────┤
│ [Alice] [Bob] [Claire] [+]        GRILLE DES SURVIVANTS       Camp: 🍖12 💧10 ⛽40 │
└───────────────────────────────────────────────────────────────────────┘
```

### Grille des survivants (bas d'écran)

Remplace [AgentRoster.cs](../Scripts/UI/AgentRoster.cs) / [AgentCard.cs](../Scripts/UI/AgentCard.cs).
Une vignette par survivant, toujours visible :

* portrait, nom, jauges de santé et de panique en barres courtes ;
* icônes d'état : blessé, paniqué, surchargé, arme équipée, radio, rappelé au camp ;
* clic = sélection (remplace la sélection actuelle), `Ctrl`+clic = ajout à la sélection ;
* double-clic = centrer la caméra sur le survivant ;
* bordure de couleur reprenant la couleur de silhouette du survivant sur la carte, pour qu'on le
  retrouve à l'œil ;
* le stock du camp (livrable 11) occupe le coin droit de la barre : nourriture, eau, essence, munitions.

### Panneau latéral

Remplace `AgentPanel` et `PlanPanel`, et les rend permanents plutôt que conditionnés au mode debug. Il
affiche, pour le survivant sélectionné :

* **Identité** : portrait, nom, rôle, description ;
* **Jauges** : santé / santé max, motivation (`sanity`), panique, stamina ;
* **Caractéristiques** : vitesse de marche et de course, portée de vision, angle de vue, multiplicateur
  de bruit, rayon de fouille, montant de soin — celles de
  [survivors.json](../config/survivors.json), pas une liste réinventée ;
* **Personnalité** : `courage`, `loyalty`, `caution`, en trois barres, avec la phrase de lecture qui va
  avec (« prudente », « téméraire », « loyal ») dérivée de seuils configurables, jamais écrite en dur ;
* **Aptitudes** : `exploration`, `support`, `breaching`, `stealth` et toute aptitude déclarée dans le
  profil — le panneau lit le dictionnaire, il ne connaît pas les noms ;
* **Compétences** : les entrées `skills` du profil, avec leur `description` telle qu'elle est écrite
  dans le fichier de configuration ;
* **Inventaire** : les 6 emplacements du livrable 11, avec l'arme équipée mise en évidence, le poids
  porté et la limite ;
* **Décision** : objectif courant, action courante, plan, dernière cause de replanification. Les
  informations exigées par le livrable 8 restent toutes présentes ; elles cessent seulement d'être
  réservées au mode debug.

Le mode debug ([08-interface-et-boucle-complete.md](./08-interface-et-boucle-complete.md)) n'est pas
supprimé : il conserve les overlays (F1 à F7), les scores Utility AI, les nœuds GBFS et les plans
rejetés. Ce qui change, c'est que le joueur n'en a plus besoin pour jouer.

### Menus

* **Écran titre** : nouvelle partie, continuer, options, quitter ;
* **Écran de camp** : stock, roster complet, départ en expédition (livrable 11), choix de la mission ;
* **Écran de préparation de mission** : refonte de
  [MissionSetupScreen.cs](../Scripts/UI/MissionSetupScreen.cs) — briefing, composition d'équipe,
  chargement de l'inventaire de chacun, coût en essence du van, bouton de départ désactivé si le
  réservoir ne suffit pas ;
* **Écran de fin** : résultat, journal de mission, pertes, butin rapporté.

## Survivants générés aléatoirement

Alice, Bob et Claire cessent d'être le roster : ils deviennent trois entrées d'exemple parmi d'autres.

### Principe directeur

Le générateur **écrit exactement la donnée que `config/survivors.json` décrit déjà**. Il n'existe pas de
second chemin de chargement : un survivant généré et un survivant écrit à la main sont indiscernables
pour `Hordinary.Core`. C'est ce qui garantit que rien de ce qui a été construit aux livrables 4 à 8 ne
doit être adapté.

### Composition

`config/survivor_generation.json` :

```json
{
  "seed": 20260803,

  "names": {
    "first": ["Alice", "Bob", "Claire", "Diane", "Emile", "Farid", "Greta", "Hugo"],
    "last": ["Marchand", "Okonkwo", "Sorensen", "Vargas", "Nowak"]
  },

  "archetypes": [
    {
      "id": "eclaireur",
      "weight": 25,
      "role": "Eclaireur",
      "personality": { "courage": [0.4, 0.6], "loyalty": [0.5, 0.8], "caution": [0.7, 0.9] },
      "max_health": [70, 90],
      "walk_speed": [4.5, 5.5],
      "vision_range": [10, 12],
      "noise_multiplier": [0.5, 0.7],
      "aptitudes": { "exploration": [0.85, 1.0], "stealth": [0.8, 0.95], "support": [0.2, 0.4], "breaching": [0.1, 0.3] },
      "skills": ["Explore", "Observe", "MoveToLocation"]
    }
  ],

  "traits": [
    { "id": "insomniaque", "weight": 10, "text": "Dort mal. Recupere moins vite sa motivation.", "modifiers": { "sanity_recovery_per_second": 0.6 } },
    { "id": "estomac_solide", "weight": 10, "text": "Mange n'importe quoi sans broncher.", "modifiers": { "hunger_sanity_penalty": 0.5 } }
  ],

  "appearance": {
    "body": 2,
    "hair": 3,
    "clothing": 3,
    "palette": ["#8d5524", "#c68642", "#e0ac69", "#f1c27d", "#ffdbac"]
  }
}
```

Règles :

* toute valeur d'archétype est un **intervalle** ; le générateur tire dedans avec la graine ;
* un à deux `traits` par survivant, qui multiplient un réglage existant de `config/game_config.json` —
  aucun trait n'introduit une mécanique nouvelle, sinon le générateur devient une seconde source de
  règles ;
* l'apparence est un tirage de couches (`body`, `hair`, `clothing`, teinte), pas un sprite tiré au sort ;
* **les trois compteurs d'`appearance` valent le nombre de fichiers réellement livrés**, jamais un
  objectif. Un `hair: 8` avec trois coiffures sur disque produit cinq survivants sur huit sans cheveux,
  et le défaut ne se voit qu'en jeu. Ils montent à 8 et 10 le jour où les extensions du catalogue de
  sprites sont livrées, pas avant — et un test le vérifie ;
* **déterminisme** : à graine égale, roster identique, apparences comprises. Un roster généré peut être
  écrit dans un fichier au format `config/survivors.json` pour être rejoué ou corrigé à la main ;
* les trois profils du livrable 7 restent livrés en dur dans `config/survivors.json` et servent de
  roster de départ et de référence de test. La génération est ce qui peuple le camp **ensuite** — au
  recrutement (livrable 14).

## Zone d'extraction : le van

La zone d'extraction cesse d'être un rectangle de `config/map_locations.json` et devient un véhicule.

```json
{
  "van": {
    "footprint": [2, 3],
    "fuel_per_cell": 0.08,
    "fuel_minimum": 4,
    "fuel_return_reserve": true,
    "capacity_survivors": 4,
    "capacity_weight": 60,
    "engine_noise": 9,
    "departure_delay": 3
  }
}
```

Règles :

* le van est **garé au camp** au départ et roule jusqu'à un point de dépose près de l'objectif ; le coût
  en essence est `distance × fuel_per_cell`, au minimum `fuel_minimum` ;
* `fuel_return_reserve` à `true` réserve le carburant du retour au moment du départ : le jeu refuse une
  mission qu'on ne pourrait pas terminer. À `false`, partir avec un aller simple devient un choix
  possible, et une panne sèche une manière de perdre ;
* l'essence vient du stock du camp (livrable 11) et se trouve en `fuel_can` ou en expédition ;
* le van a une **capacité** : quatre survivants, et un poids total. Une caisse encombrante peut obliger
  à laisser quelqu'un — ou à faire deux voyages ;
* démarrer le moteur émet `engine_noise 9` pendant `departure_delay` secondes : la horde entend le
  départ. L'extraction cesse d'être une case sûre et devient un moment à défendre ;
* mécaniquement, le van reste une `MapZone` de `kind: Extraction` pour tout le reste du code : les
  conditions de victoire du livrable 8 et l'action `ReachExtraction` du domaine de planification ne
  changent pas.

## Architecture concernée

`Scripts/UI/*` (refonte complète), `Game/SurvivorGenerator`, `Game/Van`, `Config/VanConfig`,
`Config/SurvivorGenerationConfig` — voir [A-architecture-et-donnees.md](./A-architecture-et-donnees.md).
La séparation simulation / IA / interface exigée par
[D-qualite-et-definition-of-done.md](./D-qualite-et-definition-of-done.md) est le point de vigilance de
ce livrable : le générateur de survivants et la logique du van vivent dans `Hordinary.Core`, testables
sans Godot ; `Scripts/UI` ne fait qu'afficher, et ne décide de rien — pas même du texte de lecture d'un
trait de personnalité, qui vient de seuils de configuration.

Nouveaux fichiers de configuration : `config/survivor_generation.json`, section `van` dans
`config/game_config.json`.

## Étapes à créer

* [ ] **(humain)** Livrer les 84 fichiers du **catalogue de sprites** (65 livraisons), dans l'ordre de
      production qu'il donne — les 6 premiers avant tout le reste, ce sont eux qui valident la chaîne
      d'empilement.
* [ ] Écrire `config/survivor_generation.json` avec `appearance` aligné sur les calques réellement
      livrés (`body` toujours à `2`), et faire échouer le chargement quand un compteur les dépasse.
* [ ] Refondre le HUD en trois zones (carte, panneau latéral, grille du bas).
* [ ] Construire la grille des survivants (vignettes, jauges, icônes d'état, sélection multiple).
* [ ] Construire le panneau latéral complet (identité, jauges, caractéristiques, personnalité,
      aptitudes, compétences, inventaire, décision).
* [ ] Rendre les informations du livrable 8 visibles hors mode debug, sans supprimer le mode debug.
* [ ] Ajouter le pawn en couches (corps, vêtement, tête, cheveux) et sa recoloration.
* [ ] Implémenter le choix des trois vues et le miroir horizontal pour l'ouest.
* [ ] Implémenter les effets par code : oscillation, bond d'attaque, teinte de dégât, inclinaison de
      blessure, tremblement de panique, rotation à terre.
* [ ] Faire pivoter le sprite d'arme tenue vers la cible.
* [ ] Remplacer les cercles de `SurvivorNode` et `ZombieNode` par le pawn, en conservant les quatre
      teintes d'état du zombie.
* [ ] Écrire le générateur de survivants (archétypes, intervalles, traits, apparence), déterministe.
* [ ] Permettre l'export d'un roster généré au format `config/survivors.json`.
* [ ] Ajouter le van : empreinte, capacité, coût en essence, réserve de retour, bruit de démarrage.
* [ ] Brancher le coût en essence sur le stock du camp et bloquer le départ en cas de réservoir vide.
* [ ] Refondre l'écran de préparation de mission et ajouter l'écran de camp.
* [ ] Ajouter l'écran titre et l'écran de fin de partie.

## Critère de validation

Une partie se joue de bout en bout sans jamais activer le mode debug : le joueur compose une équipe de
survivants générés, lit leurs caractéristiques et leur personnalité dans le panneau latéral, paie
l'essence du van au départ, et voit dans la grille du bas lequel est blessé sans avoir à cliquer.

## Tests associés

À ajouter dans `Tests/` (voir [C-tests.md](./C-tests.md)) :

* deux générations avec la même graine produisent des rosters identiques, traits et apparences compris ;
* aucun index d'apparence tiré par le générateur ne dépasse le nombre de calques réellement présents
  dans `Assets/` — le test qui empêche un survivant chauve par erreur de configuration ;
* les quatre couches d'un pawn ont toutes les mêmes dimensions et les trois mêmes vues, sans quoi
  l'empilement est faux avant même de s'afficher ;
* tout survivant généré est chargeable par le lecteur de `config/survivors.json` sans traitement
  particulier ;
* un trait ne modifie qu'un réglage existant de `config/game_config.json` ;
* le coût en essence d'un trajet est proportionnel à la distance et jamais inférieur à `fuel_minimum` ;
* avec `fuel_return_reserve: true`, une mission dont le retour n'est pas payable est refusée au départ ;
* le van refuse un embarquement dépassant sa capacité en poids ou en survivants ;
* le démarrage du van émet un bruit du barème et est perçu par les zombies à portée ;
* `Hordinary.Core` ne référence aucun type Godot dans le générateur ni dans la logique du van.
