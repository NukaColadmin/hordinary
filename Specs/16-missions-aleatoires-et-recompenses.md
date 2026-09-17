# Livrable 16 — Missions aléatoires et récompenses

## Objectif

Remplacer le catalogue de missions entièrement écrit à l’avance par des missions générées au début
d’une partie. Une mission possède toujours une zone cible, un objectif mesurable et une récompense
annoncée avant le départ. La génération ne produit aucun texte de description : l’interface affiche
uniquement les informations utiles à la décision du joueur.

Le système doit produire des missions différentes d’une partie à l’autre, tout en restant parfaitement
reproductible avec la même graine.

## Offre de missions

À l’ouverture de l’écran de préparation, le générateur propose trois missions. Le nombre d’offres est
configurable, avec `3` comme valeur par défaut. Une offre est figée dès qu’elle est affichée : changer
de sélection, ouvrir l’écran de détail ou relancer l’interface ne doit pas modifier son contenu.

Chaque offre contient :

```text
id                 identifiant stable de l’offre
seed               graine utilisée pour cette offre
type               KillZombies, CollectObjects ou CollectPriorityResources
target_zone        zone dans laquelle l’objectif doit être réalisé
requirements       objectifs et quantités à atteindre
rewards            récompenses reçues en cas de victoire
recommended_team   nombre conseillé de survivants
difficulty         étiquette lisible calculée à partir des contraintes
time_limit         durée limite de la mission
entry_fuel_cost    essence consommée pour entrer dans la mission
```

Une mission générée ne possède pas de champ `description`. Le titre est construit à partir du type et
du nom de la zone, par exemple `Élimination — Supermarché` ou `Ravitaillement — Station-service`.
Les textes de l’interface doivent rester génériques et ne doivent pas inventer de briefing narratif.

Une offre ne peut pas cibler le camp, la zone d’extraction seule ou une zone sans case praticable. La
génération élimine aussi une offre dont la zone ne permet pas de placer les objectifs demandés.

## Types d’objectifs

### Éliminer des zombies — `KillZombies`

Le joueur doit tuer `count` zombies dans la zone cible.

```json
{
  "type": "KillZombies",
  "target_zone": "supermarche",
  "requirements": {
    "zombies": 8
  }
}
```

Un zombie compte lorsque sa mort est enregistrée alors qu’il se trouve dans la zone cible. Les zombies
tués hors de cette zone ne font pas progresser l’objectif. Un compteur visible indique la progression,
par exemple `Zombies éliminés : 5 / 8`.

Le nombre demandé doit être inférieur ou égal au nombre de zombies effectivement assignés à la zone.
Le générateur augmente la densité de la zone ou régénère l’offre jusqu’à obtenir une mission réalisable.

### Récupérer des objets — `CollectObjects`

Le joueur doit récupérer un ou plusieurs objets placés dans la zone cible. Chaque ligne indique un
identifiant d’objet et une quantité.

```json
{
  "type": "CollectObjects",
  "target_zone": "poste_police",
  "requirements": {
    "items": [
      { "item": "ammo_box", "quantity": 1 },
      { "item": "crowbar", "quantity": 1 }
    ]
  }
}
```

Un objet est validé lorsqu’un survivant le ramasse dans la zone cible. Le groupe peut répartir les
objets entre plusieurs inventaires. Les objets doivent être rapportés au camp pour terminer la
mission ; les perdre après le ramassage rend l’objectif à nouveau incomplet.

Le générateur ne choisit que des objets déclarés dans `config/items.json` et compatibles avec la table
de butin de la zone. Il ne peut pas demander un objet qui ne peut pas être placé ou ramassé.

### Récupérer les ressources prioritaires — `CollectPriorityResources`

Le joueur doit rapporter les ressources indispensables au camp. Les ressources prioritaires sont :

```text
food  nourriture
water eau
fuel  essence
```

Une mission peut demander une seule ressource ou plusieurs ressources avec des quantités distinctes.

```json
{
  "type": "CollectPriorityResources",
  "target_zone": "station_service",
  "requirements": {
    "resources": [
      { "resource": "fuel", "quantity": 2 },
      { "resource": "water", "quantity": 1 }
    ]
  }
}
```

Les ressources sont des objets transportables et suivent les règles normales d’inventaire, de perte à
la mort et de retour au camp. Leur ajout au stock du camp n’est effectué qu’à la fin d’une mission
victorieuse.

## Récompenses

Les récompenses sont générées avec la mission, affichées sur la carte de l’offre et attribuées une
seule fois lorsque le groupe revient au camp avec l’objectif rempli.

Les catégories disponibles sont :

```text
weapon   arme
medikit  médikit
rare     objet rare
```

Exemple :

```json
{
  "rewards": [
    { "category": "weapon", "item": "shotgun", "quantity": 1 },
    { "category": "medikit", "item": "medikit", "quantity": 2 },
    { "category": "rare", "item": "radio", "quantity": 1 }
  ]
}
```

Les récompenses sont toujours connues avant l’acceptation de la mission. Elles ne doivent pas être
remplacées par une phrase vague comme « récompense aléatoire ». La quantité et l’identifiant de chaque
récompense sont affichés.

Le générateur applique une table de récompenses configurée par type et difficulté :

| Difficulté | Récompense minimale | Récompense possible supplémentaire |
|---|---|---|
| Facile | 1 médikit ou 1 objet rare | — |
| Moyenne | 1 médikit ou 1 arme | 1 objet rare |
| Difficile | 1 arme | 1 médikit et 1 objet rare |

Une arme doit référencer `config/weapons.json`. Un médikit et un objet rare doivent référencer
`config/items.json`. Une récompense invalide fait échouer la génération de l’offre et déclenche une
régénération contrôlée.

Les récompenses sont transférées au stock du camp. Si le stock est plein, elles restent dans une file
de récompenses à récupérer dans l’écran de camp ; elles ne sont jamais supprimées silencieusement.

## Coût d’entrée

Entrer dans une mission consomme de l’essence selon sa difficulté. Le coût est affiché sur chaque offre
avant l’acceptation et fait partie des données figées de la mission.

| Difficulté | Coût d’entrée par mission |
|---|---:|
| Facile | 1 essence |
| Moyenne | 2 essences |
| Difficile | 3 essences |

Le coût est débité une seule fois au lancement de la mission, après la validation de l’offre et avant le
départ du groupe. Consulter une offre, l’ouvrir ou la remplacer ne consomme aucune essence. Le lancement
est refusé si le stock du camp est inférieur au coût demandé. L’essence consommée n’est pas remboursée en
cas de défaite, d’abandon ou de mort du groupe.

Le coût doit être configurable par difficulté. La valeur calculée est enregistrée dans
`entry_fuel_cost` afin que l’interface, le jeu et les tests utilisent exactement le même montant.

## Génération aléatoire

La génération doit être déterministe et indépendante de l’ordre d’affichage des éléments dans les
dictionnaires de configuration.

La graine d’une offre est dérivée de quatre valeurs :

```text
mission_seed + jour_de_campagne + index_de_l_offre + version_du_generateur
```

La version du générateur permet de modifier les règles plus tard sans prétendre qu’une ancienne graine
produit encore le même contenu. À graine et version identiques, les éléments suivants doivent être
identiques : type, zone, exigences, difficulté, durée, coût d’entrée, récompenses et équipe conseillée.

Le générateur suit ce processus :

1. choisir le type parmi les types activés dans la configuration ;
2. choisir une zone éligible et distincte des autres offres autant que possible ;
3. choisir les exigences compatibles avec la zone et son butin ;
4. calculer la difficulté, le temps limite et l’équipe conseillée ;
5. choisir les récompenses correspondant à la difficulté ;
6. valider l’offre complète ;
7. recommencer avec un sous-seed déterministe si une contrainte échoue.

Le générateur possède un nombre maximal de tentatives par offre. Si aucune combinaison valide n’est
trouvée, il utilise un profil de secours explicitement configuré et écrit la raison dans le journal de
debug. Il ne doit jamais afficher une mission impossible.

Les poids de sélection, les bornes de quantité, le nombre d’offres, les récompenses et la version du
générateur sont configurés dans un nouveau bloc `mission_generation` de `config/game_config.json`.

Exemple minimal :

```json
"mission_generation": {
  "offer_count": 3,
  "generator_version": 1,
  "max_attempts_per_offer": 40,
  "types": {
    "KillZombies": 0.35,
    "CollectObjects": 0.35,
    "CollectPriorityResources": 0.30
  },
  "zombies": { "min": 3, "max": 12 },
  "objects": { "min_distinct_items": 1, "max_distinct_items": 3 },
  "resources": { "min_quantity": 1, "max_quantity": 3 },
  "entry_fuel_cost_by_difficulty": {
    "Easy": 1,
    "Medium": 2,
    "Hard": 3
  }
}
```

Les missions écrites à la main dans `config/missions.json` restent acceptées pour la carte de
démonstration et les tests ciblés. Le mode généré les utilise seulement lorsque la génération
aléatoire est activée. Une mission manuelle et une mission générée utilisent le même suivi de partie,
le même planificateur et les mêmes règles de victoire.

## Interface de préparation

Chaque carte de mission affiche, sans description :

```text
Élimination — Supermarché
Difficulté : Moyenne
Zone : Supermarché
Objectif : 0 / 8 zombies éliminés
Équipe conseillée : 2
Temps : 180 s
Coût d’entrée : 2 essences

Récompenses :
  Fusil ×1
  Médikit ×1
  Objet rare : Radio ×1
```

Pour une mission de récupération, l’interface affiche la liste des objets et les quantités. Pour une
mission de ressources, elle affiche `Nourriture`, `Eau` et `Essence` avec leur quantité respective.
L’offre sélectionnée doit conserver exactement les valeurs présentées lors du départ.
Le bouton de départ affiche le coût et le stock disponible, par exemple `Entrer dans la mission — 2
essences`, et est désactivé si le camp ne possède pas assez d’essence.

Pendant la partie, le HUD affiche :

* le type d’objectif et la zone cible ;
* la progression de chaque exigence ;
* les récompenses promises ;
* le temps restant ;
* l’état de livraison au camp.

Après la victoire, l’écran de résultat affiche les récompenses obtenues et leur transfert vers le
stock du camp. Après une défaite ou un abandon, aucune récompense n’est attribuée.

## Règles de victoire et d’échec

La mission est remplie quand toutes les exigences sont atteintes dans la zone cible et que les objets
ou ressources nécessaires sont revenus au camp avec le groupe. Pour `KillZombies`, seul le compteur de
morts dans la zone est requis ; il n’y a pas d’objet à rapporter.

Le fonctionnement actuel du rappel du van, du temps limite et du retour de tout le groupe reste
inchangé. La condition de victoire est donc évaluée par le type de mission :

```text
KillZombies                 exigences remplies + groupe au camp
CollectObjects              objets requis dans les inventaires + groupe au camp
CollectPriorityResources    ressources requises dans les inventaires + groupe au camp
```

Les rewards sont attribuées après le verdict de victoire, jamais au moment où un zombie est tué ou un
objet ramassé. Cette règle empêche une mission perdue de produire une récompense partielle.

## Architecture attendue

Ajouter ou faire évoluer les composants suivants :

* `MissionGenerator` : construit les offres depuis une graine et les catalogues de zones, objets et
  armes ;
* `GeneratedMission` ou extension équivalente de `MissionDefinition` : porte le type, les exigences,
  les récompenses et la graine ;
* `MissionObjectiveTracker` : suit les compteurs propres aux trois types ;
* `MissionRewardService` : valide et attribue les récompenses une seule fois ;
* le chargeur de configuration : lit `mission_generation` et vérifie les références ;
* l’écran de préparation et le HUD : affichent les données structurées de l’offre.

Le domaine de planification ne doit pas contenir un nom de zone, d’objet, d’arme ou de récompense
générée. Il reçoit les faits symboliques nécessaires à l’objectif courant. La génération reste une
responsabilité du système de mission et ne doit pas être déplacée dans `Hordinary.Core` sous forme de
cas particuliers pour chaque zone.

## Configuration complémentaire

Les catalogues existants restent les sources de vérité :

| Donnée | Source |
|---|---|
| Zones éligibles et butin disponible | `config/zones.json` |
| Objets et ressources | `config/items.json` |
| Armes | `config/weapons.json` |
| Paramètres de génération | `config/game_config.json` |
| Missions manuelles de démonstration | `config/missions.json` |

Une zone peut déclarer qu’elle accepte ou refuse un type de mission. À défaut, les règles générales
s’appliquent : une zone avec des zombies peut recevoir `KillZombies`, une zone avec un butin compatible
peut recevoir `CollectObjects`, et une zone contenant une ressource prioritaire peut recevoir
`CollectPriorityResources`.

## Tests d’acceptation

Les tests à ajouter dans `Tests/` doivent vérifier :

* trois offres sont produites avec la configuration par défaut ;
* une mission générée ne possède ni description ni texte narratif obligatoire ;
* deux générations avec la même graine et la même version produisent des offres identiques ;
* deux graines différentes produisent au moins une différence sur une série de générations ;
* chaque zone cible existe, est praticable et n’est pas le camp ;
* chaque type d’objectif est généré et contient des exigences valides ;
* un zombie tué hors de la zone cible ne valide pas `KillZombies` ;
* un zombie tué dans la zone cible incrémente le bon compteur une seule fois ;
* les objets requis sont présents dans la zone et la quantité demandée est respectée ;
* une ressource de type nourriture, eau ou essence est suivie avec le bon compteur ;
* les objets et ressources doivent être ramenés au camp pour terminer une récupération ;
* le coût d’entrée affiché correspond à la difficulté de la mission ;
* le lancement débite le coût d’entrée une seule fois et refuse le départ si le stock est insuffisant ;
* consulter ou remplacer une offre ne débite aucune essence, et une mission perdue ne rembourse pas le coût ;
* les récompenses affichées avant le départ sont celles attribuées après la victoire ;
* une défaite, un abandon ou une mort du groupe n’attribue aucune récompense ;
* une mission générée n’est pas régénérée pendant son exécution ;
* une offre invalide est rejetée puis remplacée par une offre valide ou par le profil de secours ;
* les récompenses invalides ou inconnues sont refusées au chargement ;
* une récompense est attribuée au plus une fois, même si le verdict est évalué plusieurs fois ;
* les missions manuelles existantes restent chargeables et jouables.

## Critère de validation

Au lancement d’une partie, le joueur voit trois missions différentes avec leur zone, leurs exigences,
leurs récompenses exactes et leur coût d’entrée en essence. Il en choisit une, observe sa progression pendant l’expédition et reçoit
les armes, médikits et objets rares annoncés uniquement après un retour victorieux au camp. Relancer la
même partie avec la même graine reproduit les trois offres à l’identique.
