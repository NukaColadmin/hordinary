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
[F-direction-artistique-et-prompts.md](./F-direction-artistique-et-prompts.md), section 5.

* [ ] **Survivants — deux pawns, un homme et une femme** (`pawn_male`, `pawn_female`), en 16 × 24 px.
      Ce ne sont pas deux personnages mais les deux bases que le générateur habille et recolore pour
      produire tout le roster. **30 images** : corps (2 × 3 vues), têtes (2 × 3), coiffures (3 × 3),
      tenues (3 × 3) ;
* [ ] **Zombies — le même pawn, refroidi** : 3 corps (`civil`, `employe`, `secours`) et 2 têtes, en
      3 vues, soit **15 images**. Ils réutilisent les coiffures des survivants, recolorées ;
* [ ] **Armes tenues** : 5 sprites de profil, tournés à 360° par le code vers la cible ;
* [ ] **Couches recolorables** : corps, vêtement, tête, cheveux, en niveaux de gris à deux tons, teintés
      par le code depuis `appearance.palette` — c'est ce qui donne un roster varié sans une image par
      survivant possible ;
* [ ] **Portraits** : `portrait_male` et `portrait_female` en 32 × 32 px, mêmes couches, pour la grille
      du bas et le panneau latéral ;
* [ ] **Van d'extraction** : sprite au sol (2×3 cases), états `à l'arrêt` / `moteur en marche` /
      `en panne sèche` ;
* [ ] **Habillage d'interface** : cadres de panneaux, fond de grille, jauges (santé, motivation,
      panique, stamina), icônes d'état (blessé, paniqué, surchargé, arme équipée, radio), curseurs de
      sélection et d'ordre ;
* [ ] **Menus** : fonds et boutons pour l'écran titre, l'écran de préparation de mission, l'écran de
      camp et l'écran de fin de partie.

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
| Marche / course | Oscillation verticale de 1 à 2 px, cadence proportionnelle à la vitesse |
| Attaque | Bond de 3 px vers la cible puis retour |
| Visée | Rotation libre du sprite d'arme — le seul sprite du jeu qui pivote à 360° |
| Dégât reçu | Teinte blanche 0,1 s (`Modulate`) |
| Blessé | Inclinaison permanente de 6°, légère désaturation |
| Panique | Tremblement de 1 px, haute fréquence |
| À terre / mort | Rotation à 90° du pawn entier, puis fondu |
| État du zombie | Les quatre teintes déjà en place dans `ZombieNode.cs`, appliquées au pawn |

Ce choix divise la production graphique par deux et rend les zombies possibles dans le même budget —
voir [F-direction-artistique-et-prompts.md](./F-direction-artistique-et-prompts.md), section 5.

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
    "body": 6,
    "hair": 8,
    "clothing": 10,
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
sans Godot ; `Scripts/UI` n'affiche.

Nouveaux fichiers de configuration : `config/survivor_generation.json`, section `van` dans
`config/game_config.json`.

## Étapes à créer

* [ ] **(humain)** Livrer les sprites et l'habillage listés dans « Prérequis ».
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
* tout survivant généré est chargeable par le lecteur de `config/survivors.json` sans traitement
  particulier ;
* un trait ne modifie qu'un réglage existant de `config/game_config.json` ;
* le coût en essence d'un trajet est proportionnel à la distance et jamais inférieur à `fuel_minimum` ;
* avec `fuel_return_reserve: true`, une mission dont le retour n'est pas payable est refusée au départ ;
* le van refuse un embarquement dépassant sa capacité en poids ou en survivants ;
* le démarrage du van émet un bruit du barème et est perçu par les zombies à portée ;
* `Hordinary.Core` ne référence aucun type Godot dans le générateur ni dans la logique du van.
