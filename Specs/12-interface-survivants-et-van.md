# Livrable 12 — Refonte de l'interface et survivants générés

> Voir [00-INDEX.md](./00-INDEX.md) pour le contexte général du projet.

**Priorité : P2 — Haute.** Second livrable post-MVP. Le livrable 11 ajoute une matière que l'interface
actuelle ne sait pas montrer : un inventaire, une arme équipée, un stock de camp, des survivants qui ne
s'appellent plus Alice, Bob et Claire. Sans ce livrable, le 11 est jouable mais illisible.

**Dépendances** : livrable 11 (le panneau latéral affiche l'inventaire et le stock de camp).
**Bloqué au départ** par le prérequis humain ci-dessous.

## Objectif

Remplacer l'interface de démonstration du livrable 8 par une interface de jeu : grille de survivants en
bas d'écran, panneau latéral détaillé, menus.

Le rendu en jeu des survivants et des zombies **reste le cercle + anneau** déjà produit par
[SurvivorNode.cs](../Scripts/Agents/SurvivorNode.cs) et
[ZombieNode.cs](../Scripts/Agents/ZombieNode.cs) : aucun sprite de personnage (« pawn ») n'est prévu, ni
pour ce livrable ni pour un suivant. La zone d'extraction porte le skin du van
([VanNode.cs](../Scripts/World/VanNode.cs), `Assets/Others/Van.png`) : une image posée au centre de la
zone de camp, sans aucune logique de jeu associée — ni stock d'essence, ni capacité, ni bruit de
démarrage. Mécaniquement, l'extraction reste entièrement portée par `ZoneKind.Extraction`, comme
n'importe quelle autre zone.

## Prérequis (humain, bloquant)

Le registre graphique et la charte technique sont dans
[F-direction-artistique-et-prompts.md](./F-direction-artistique-et-prompts.md). Les portraits
(`Assets/Portraits/`) et le skin du van (`Assets/Others/Van.png`) sont déjà livrés. Il ne reste que
l'interface :

* [ ] **Interface** : 6 planches, soit **24 images** une fois découpées.

Comme au livrable 11, le code peut être écrit et testé avant, mais le livrable n'est pas terminé tant
que le résultat n'est pas visible en jeu.

## Identification des survivants : l'anneau et le nom

Déjà implémenté, inchangé par ce livrable — documenté ici parce que le panneau latéral et la grille du
bas s'appuient dessus.

| Signe | Production | Règle |
|-------|-----------|-------|
| **Anneau au sol** | Ellipse aplatie de 2 px sous les pieds, couleur tirée par le code | **Unique parmi les survivants vivants** |
| **Nom au-dessus de la tête** | Texte taille 8, contour sombre de 2 px, **teinté de la couleur de l'anneau** | Toujours affiché, jamais réservé au mode debug |
| Sélection | Arc blanc à l'extérieur de l'anneau | S'ajoute à l'anneau, ne le remplace pas |

Les deux signes existent déjà dans [SurvivorNode.cs](../Scripts/Agents/SurvivorNode.cs), avec les
couleurs de `RingColors` dans [GameRoot.cs](../Scripts/Game/GameRoot.cs). **Un zombie n'a ni anneau ni
nom** — ce qui porte une couleur au sol est quelqu'un.

## Catalogue de sprites

Ce qu'il reste à produire pour ce livrable, au-delà de ce qui est déjà livré (portraits, van).

### Portraits — 6 images de 64 × 64 px (déjà livrées)

Buste de face trois quarts, tête et épaules, pour la grille du bas et le panneau latéral.

| # | Portrait | Fichiers | Images |
|---|----------|----------|--------|
| 1 | Un par personnage | `portrait_female_{1,2,3}` | **3** |
| 2 | Un par personnage | `portrait_male_{1,2,3}` | **3** |
| | | | **6** |

Le portrait ne suit pas les trois vues : il est toujours de face. La vignette de la grille du bas
l'entoure de l'**anneau de couleur** du survivant : c'est le même signe que sur la carte, donc le même
geste de reconnaissance — et c'est lui qui distingue deux survivants partageant un portrait.

### Interface — 6 planches, 24 images

Les planches se génèrent groupées, pour que les pièces d'une interface soient dessinées ensemble ; elles
se découpent ensuite en images. Le décompte ci-dessous est celui des images découpées.

| Planche | Fichiers | Dimensions | Images |
|---------|----------|-----------|--------|
| Cadres de panneaux | `ui_frame_{small,medium,large}` | 9-slice, bordure **16 px** | **3** |
| Boutons de menu | `ui_button_{idle,hover,pressed}` | 9-slice, bordure **8 px** | **3** |
| Jauges | `ui_gauge_{health,panic,stamina}_{empty,full}` | **96 × 16** | **6** |
| Icônes d'état | `ui_icon_{wounded,panicked,overloaded,armed,radio}` | **24 × 24** | **5** |
| Curseurs | `ui_cursor_{select,move,forbidden,target}` | **32 × 32** | **4** |
| Fond de titre | `ui_bg_title` | **1920 × 1080** | **1** |
| | | | **22** |

Les trois jauges correspondent aux valeurs affichées par le panneau latéral : santé, panique, stamina.
Les cinq icônes d'état correspondent exactement aux cinq états listés par la grille des survivants —
blessé, paniqué, surchargé, arme équipée, radio. Aucune des deux listes n'est décorative : les
ajouter ou en retirer demande de toucher au panneau.

**Un seul fond plein écran.** Les quatre écrans du livrable — titre, camp, préparation de mission, fin
de partie — ne demandent pas quatre fonds : les trois derniers réutilisent le cadre de panneau sur un
aplat de la palette.

`ui_bg_title` est la seule image du projet qui n'est pas du pixel art à densité fixe : elle est rendue
plein écran sans mise à l'échelle entière.

## Interface

L'interface actuelle ([Hud.cs](../Scripts/UI/Hud.cs), [AgentPanel.cs](../Scripts/UI/AgentPanel.cs),
[PlanPanel.cs](../Scripts/UI/PlanPanel.cs), [InfoPanel.cs](../Scripts/UI/InfoPanel.cs)) était une
interface d'observation : trois blocs de texte dont deux ne s'affichent qu'en mode debug
(`debug.hud_text`). Elle est refondue en trois zones permanentes.

```text
┌──────────────────────────────────────────────┬────────────────────────┐
│                                              │  PANNEAU SURVIVANT     │
│                                              │  ────────────────      │
│                                              │  Portrait  Nom         │
│                CARTE                         │                        │
│                                              │  Jauges                │
│                                              │  Caractéristiques      │
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
* icônes d'état : blessé, paniqué, surchargé, arme équipée, radio ;
* clic = sélection (remplace la sélection actuelle), `Ctrl`+clic = ajout à la sélection ;
* double-clic = centrer la caméra sur le survivant ;
* **bordure de la couleur de son anneau sur la carte**, pour qu'on le retrouve à l'œil. C'est le seul
  signe qui tient quand deux survivants partagent un portrait : ni le portrait ni la couleur au sol ne
  les distinguent l'un sans l'autre, ensemble ils suffisent ;
* le stock du camp (livrable 11) occupe le coin droit de la barre : nourriture, eau, essence, munitions.

### Panneau latéral

Remplace `AgentPanel` et `PlanPanel`, et les rend permanents plutôt que conditionnés au mode debug. Il
affiche, pour le survivant sélectionné :

* **Identité** : portrait, nom. Il n'y a ni rôle ni description : un profil ne déclare qu'un
  identifiant, un nom et une image ;
* **Jauges** : santé / santé max, panique, stamina ;
* **Caractéristiques** : vitesse de marche et de course, portée de vision, angle de vue, rayon de
  fouille, montant de soin — celles de la section `survivor` de
  [game_config.json](../config/game_config.json), et les mêmes pour tout le monde. Le panneau les
  montre parce qu'elles expliquent ce qu'on voit à l'écran, pas pour distinguer celui-ci de
  celui-là ;
* **Inventaire** : les 6 emplacements du livrable 11, avec l'arme équipée mise en évidence, le poids
  porté et la limite. Un emplacement montre la **tuile 32 × 32** de l'objet, à un facteur d'échelle
  entier — il n'existe pas de série d'icônes séparée. `ItemIcons` (`Scripts/UI/`) la découpe dans
  l'atlas à partir de l'`id` ;
* **Décision** : objectif courant, action courante, plan, dernière cause de replanification. Les
  informations exigées par le livrable 8 restent toutes présentes ; elles cessent seulement d'être
  réservées au mode debug.

Le mode debug ([08-interface-et-boucle-complete.md](./08-interface-et-boucle-complete.md)) n'est pas
supprimé : il conserve les overlays (F1 à F7), les scores Utility AI, les nœuds GBFS et les plans
rejetés. Ce qui change, c'est que le joueur n'en a plus besoin pour jouer.

### Menus

* **Écran titre** : nouvelle partie, continuer, options, quitter ;
* **Écran de camp** : stock, roster complet, choix de la mission ;
* **Écran de préparation de mission** : refonte de [CampScreen.cs](../Scripts/UI/CampScreen.cs) —
  briefing, composition d'équipe, chargement de l'inventaire de chacun, bouton de départ ;
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
    "first_female": ["Alice", "Claire", "Diane", "Greta"],
    "first_male": ["Bob", "Emile", "Farid", "Hugo"],
    "last": ["Marchand", "Okonkwo", "Sorensen", "Vargas", "Nowak"]
  },

  "appearance": {
    "portrait_female": 3,
    "portrait_male": 3,
    "ring_colors": ["#4fc7ff", "#ffb54a", "#8ceb8c", "#eb8ceb", "#f2f280", "#ff7f6b"]
  }
}
```

**Il n'y a pas d'archétypes**, et c'est la conséquence directe du profil réduit : un survivant ne
déclare qu'un identifiant, un nom et une image, donc générer un survivant, c'est tirer un nom et un
portrait. Rien d'autre ne le distinguerait — les caractéristiques sont communes, et ce qui décide de
son comportement est ce qu'il a vu et ce que le joueur lui dit.

Règles :

* l'apparence est **un portrait tiré parmi les six** et **une couleur d'anneau**, rien d'autre ;
* **le prénom suit le portrait.** Un portrait visiblement féminin nommé « Bob » est un défaut, pas une
  variation : le générateur tire d'abord le portrait, puis le prénom dans la liste correspondante ;
* **les deux compteurs d'`appearance` valent le nombre de portraits réellement livrés**, jamais un
  objectif, et le chargement échoue s'ils les dépassent — un test le vérifie ;
* **la couleur d'anneau est unique parmi les survivants vivants.** `ring_colors` en contient au moins
  autant que le roster peut en compter ; épuiser la liste est une erreur de chargement, pas un recyclage
  silencieux. Deux survivants peuvent partager un portrait, jamais une couleur ;
* **déterminisme** : à graine égale, roster identique, apparences comprises. Un roster généré peut être
  écrit dans un fichier au format `config/survivors.json` pour être rejoué ou corrigé à la main ;
* les trois profils du livrable 7 restent livrés en dur dans `config/survivors.json` et servent de
  roster de départ et de référence de test. La génération est ce qui peuple le camp **ensuite**.

## Architecture concernée

`Scripts/UI/*` (refonte complète), `Game/SurvivorGenerator`, `Config/SurvivorGenerationConfig` — voir
[A-architecture-et-donnees.md](./A-architecture-et-donnees.md). La séparation simulation / IA /
interface exigée par
[D-qualite-et-definition-of-done.md](./D-qualite-et-definition-of-done.md) est le point de vigilance de
ce livrable : le générateur de survivants vit dans `Hordinary.Core`, testable sans Godot ;
`Scripts/UI` ne fait qu'afficher, et ne décide de rien.

Nouveau fichier de configuration : `config/survivor_generation.json`.

## Étapes à créer

**Ce livrable est le seul qui reste partiel.** L'interface est refondue et jouable ; le *générateur*
de survivants, qui en était la moitié testable sans moteur, n'est pas écrit. Le roster est donc
toujours celui, fixe, de [`config/survivors.json`](../config/survivors.json).

* [ ] **(humain)** Livrer les 22 images de la planche d'interface — les portraits et le skin du van
      sont déjà livrés. **13 sur 22** sont dans [`Assets/UI/`](../Assets/UI/).
* [ ] Écrire `config/survivor_generation.json` avec `appearance` aligné sur les portraits réellement
      livrés, et faire échouer le chargement quand un compteur les dépasse ou quand `ring_colors` est
      trop court. **Le fichier n'existe pas.**
* [x] Refondre le HUD en trois zones (carte, panneau latéral, grille du bas) — `Hud`, `SurvivorPanel`,
      `AgentRoster`.
* [ ] Construire la grille des survivants (vignettes, jauges, icônes d'état, sélection multiple).
      La grille existe (`AgentRoster`, `AgentCard`), la **sélection multiple** non : `AgentRoster`
      n'entretient qu'un sélectionné à la fois.
* [x] Construire le panneau latéral complet (identité, jauges, caractéristiques, inventaire,
      décision) — `SurvivorPanel`.
* [x] Rendre les informations du livrable 8 visibles hors mode debug, sans supprimer le mode debug.
      Le panneau et la grille sont toujours là ; les trois overlays (navigation, carte mentale, bruit)
      restent derrière `debug`, ce qui est leur place — ce sont des outils d'inspection, pas de
      l'information de jeu.
* [ ] Écrire le générateur de survivants (noms, apparence), déterministe. **Pas écrit.** C'est le
      point de vigilance annoncé plus haut : la partie de ce livrable qui vivrait dans
      `Hordinary.Core` et se testerait sans Godot est précisément celle qui manque, et les deux tests
      demandés ci-dessous n'ont donc pas de sujet.
* [ ] Permettre l'export d'un roster généré au format `config/survivors.json`. **Pas écrit**, et sans
      objet tant que le générateur manque.
* [x] Refondre l'écran de préparation de mission et ajouter l'écran de camp — `CampScreen`, complété
      par `LoadoutPanel` pour le paquetage.
* [ ] Ajouter l'écran titre et l'écran de fin de partie. L'écran titre manque. **L'écran de fin de
      partie a été écarté**, et la raison est dans `Scripts/UI/Hud.cs` : le verdict s'affiche là où il
      s'est toujours affiché, avec ce que la victoire a rapporté ; un écran de plus pour trois lignes
      ferait une transition à traverser, pas une information de plus.

## Critère de validation

Une partie se joue de bout en bout sans jamais activer le mode debug : le joueur compose une équipe de
survivants générés, lit leurs caractéristiques dans le panneau latéral, et voit dans la grille du bas
lequel est blessé sans avoir à cliquer.

## Tests associés

À ajouter dans `Tests/` (voir [C-tests.md](./C-tests.md)) :

* deux générations avec la même graine produisent des rosters identiques, apparences comprises ;
* aucun index de portrait tiré par le générateur ne dépasse le nombre de portraits réellement présents
  dans `Assets/` — le test qui empêche un survivant sans portrait par erreur de configuration ;
* **deux survivants vivants n'ont jamais la même couleur d'anneau**, et une liste `ring_colors` plus
  courte que le roster échoue au chargement plutôt que de recycler une couleur ;
* le prénom d'un survivant vient de la liste correspondant au genre de son portrait ;
* tout survivant généré est chargeable par le lecteur de `config/survivors.json` sans traitement
  particulier ;
* `Hordinary.Core` ne référence aucun type Godot dans le générateur.
