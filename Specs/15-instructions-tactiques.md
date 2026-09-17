# Livrable 15 — Instructions tactiques en langage naturel

> Voir [00-INDEX.md](./00-INDEX.md) pour le contexte général du projet.

**Priorité : P2 — Haute.** C'est le livrable qui change le plus la démonstration : le joueur écrit une
phrase, et le comportement des trois survivants change, de façon vérifiable. Il est optionnel au sens
où `instructions.enabled: false` rend le jeu intégralement jouable sans lui — mais il n'est pas
accessoire : c'est lui qui fait de la planification symbolique une chose qu'un lecteur non spécialiste
peut voir fonctionner.

**Dépendances** : livrable 5 (le domaine), livrable 11 (dernier à faire bouger `planner_domain.json`),
et surtout le document transverse [G-export-pddl.md](./G-export-pddl.md) — **prérequis dur, terminé
avant que celui-ci commence**. Pas « utile pour le portillon 3 » : structurellement en amont.

Ne dépend **pas** des livrables 12, 13 ni 14.

**Les instructions portent sur le domaine PDDL, pas sur le JSON.** C'est le choix qui structure toute
la suite. `config/planner_domain.json` reste la **source** du domaine et n'est jamais modifié ; la
**cible** de la compilation est le domaine PDDL produit par la spec G. Une contrainte devient donc un
objet PDDL, sa compilation est une réécriture PDDL → PDDL décrite dans la littérature, et le portillon
qui la vérifie est un planificateur qui lit nativement ce qu'on lui donne. Il n'y a plus de format
privé au milieu de la chaîne.

---

## Objectif

Le joueur tape une phrase :

> « Avant d'entrer dans un bâtiment, regroupez-vous. »

Elle devient une **contrainte formelle**, compilée dans le domaine de planification. Les survivants ne
reçoivent pas un ordre de plus : leurs plans possibles ont changé. Un survivant qui allait entrer seul
dans l'entrepôt ira d'abord rejoindre le groupe — non parce qu'une règle spéciale l'y force, mais parce
que le plan qui l'aurait fait entrer seul n'existe plus.

Et si l'instruction rend la mission impossible, le jeu le dit **avant** que le joueur en souffre.

## Règle non négociable

**Le modèle de langage ne décide de rien. Il traduit.**

Il produit une contrainte dans une grammaire fermée. La contrainte est ensuite compilée par du code
déterministe dans `Hordinary.Core`, vérifiée par quatre portillons, et seulement alors appliquée. Une
instruction qui ne compile pas est **refusée avec sa raison**, affichée au joueur.

Trois conséquences, les mêmes qu'au livrable 14 :

1. `Hordinary.Core` ne connaît ni HTTP, ni LangChain, ni FastAPI. Le service est appelé depuis
   `Scripts/`, au-dessus de la simulation ;
2. `instructions.enabled: false` retire la saisie libre et laisse une **liste d'instructions
   pré-écrites** — les mêmes contraintes, choisies dans un menu. Le jeu reste intégralement jouable, et
   toute la mécanique de contraintes reste démontrable sans modèle ;
3. la suite de tests passe entièrement avec le service arrêté. Aucun test ne dépend d'un modèle.

Le point est important et mérite d'être écrit noir sur blanc : **le modèle n'est responsable que de la
traduction français → contrainte.** Toute la valeur mécanique — la compilation, la vérification, le
refus motivé — est déterministe et testée. C'est ce qui rend ce livrable défendable, et c'est aussi ce
qui le rend petit.

---

## Ce qu'est une contrainte

Ni une action, ni un but : une **contrainte de trajectoire**. C'est un objet standard, les *state
trajectory constraints* introduites par **PDDL 3** (Gerevini & Long, 2005) : un bloc `(:constraints …)`
qui porte sur la **suite des états** traversés par le plan, et non sur son état final.

Le point décisif : **ni GBFS ni Fast Downward ne savent les résoudre.** Notre planificateur ne les
connaît pas, et Fast Downward ne prend pas les contraintes de trajectoire PDDL 3 en entrée — il traite
STRIPS, ADL et les coûts d'action, pas le fragment PDDL 3. Compiler la contrainte **dans le domaine**
n'est donc pas une élégance : c'est la seule façon d'obtenir un portillon 3 qui existe. La méthode est
celle de l'article — traduire la modalité temporelle en **préconditions supplémentaires**, quitte à
scinder une action en deux.

Conséquence sur les `:requirements` : le domaine compilé reste dans **exactement le même fragment que
celui de la spec G** (`:adl` + `:action-costs`). Aucun `(:constraints …)` n'est jamais soumis à un
planificateur. Le bloc PDDL 3 déclaratif est bien produit, mais pour la vérification seule — voir le
portillon 4.

> À confirmer pendant le pic technique de la spec G, en même temps que le `forall` : que Fast Downward
> rejette bien un `(:constraints …)` qu'on lui soumettrait. Si par surprise il l'accepte, ça ne change
> rien à la conception — ça ajoute une corroboration de plus.

### La grammaire fermée

Cinq formes, et rien d'autre. C'est le premier garde-fou, et le plus efficace : la surface que le
modèle peut produire est minuscule.

| Forme | Sens | Modalité PDDL 3 | Exemple de phrase |
|---|---|---|---|
| `require_before(P, A)` | `A` n'est jouable que si `P` est vrai | `sometime-before` | « regroupez-vous avant d'entrer dans un bâtiment » |
| `require_after(P, A)` | `A` doit poser `P`, ou être suivie de ce qui le pose | `sometime-after` | « fouillez avant de repartir » |
| `forbid(A)` | l'action est retirée du domaine | *aucune* — retrait du schéma | « ne cassez aucune vitre » |
| `require_always(P)` | `P` doit être vrai à chaque état du plan | `always` | « restez toujours à couvert » |
| `penalize(A, k)` | le coût de `A` est multiplié par `k` | *aucune* — `:action-costs` | « évitez de forcer les portes » |

Deux des cinq formes ne sont pas des contraintes PDDL 3, et il faut le dire plutôt que de faire
semblant. `forbid` se compile par simple retrait du bloc `(:action …)` : plus court que n'importe
quelle modalité, et exact. `penalize` ne touche pas la trajectoire du tout, seulement la fonction de
coût — c'est une **préférence**, et elle est dans la grammaire exprès, parce qu'une partie des phrases
d'un joueur sont des préférences et que les rendre absolues produirait des refus incompréhensibles.

`P` est un **prédicat déclaré dans le `(:predicates …)`** du domaine exporté. `A` est un **schéma
d'action déclaré**, ou une **classe de schémas** désignée par le type de son paramètre
(`enter:building`, `move:street`). Aucun symbole libre n'est accepté : c'est le portillon 1, et son
vocabulaire de référence est maintenant le domaine PDDL lui-même, pas une liste tenue à part.

**Avertissement sur `penalize`.** Le livrable 5 assume `priorité = h` : la recherche est gloutonne et
**n'utilise pas `g`**. Un coût multiplié par `k` n'a donc, en l'état, aucun effet sur le plan choisi en
jeu — alors qu'il en a un parfait sous Fast Downward, qui minimise `total-cost`. La forme reste dans la
grammaire, mais le portillon 2 émet un **avertissement « sans effet sur ce moteur de recherche »** tant
que la recherche active ignore `g`, exactement comme pour une contrainte portant sur une classe de
lieux absente de la carte. C'est le seul endroit du livrable où le domaine PDDL et le jeu ne disent pas
la même chose, et il est signalé au joueur plutôt que caché.

### La compilation, sur l'exemple

« Regroupez-vous avant d'entrer dans un bâtiment » →
`require_before(at(agent, group), enter:building)`

**La forme déclarative**, celle qui dit ce que le joueur voulait. Elle n'est donnée à aucun
planificateur :

```lisp
(:constraints
  (forall (?b - location)
    (sometime-before (and (building ?b) (at agent ?b))
                     (at agent group))))
```

**La forme compilée**, celle qui tourne. Le schéma `MoveToLocation` de la spec G se scinde en deux :
une précondition PDDL est conjonctive, on ne peut pas y écrire « si bâtiment alors regroupé ». On écrit
« bâtiment **et** regroupé » d'un côté, « pas bâtiment » de l'autre.

```lisp
(:action MoveToLocation
  :parameters (?loc - location)
  :precondition (and (known ?loc) (not (at agent ?loc))
                     (not (building ?loc)))
  :effect (and (forall (?l - location) (not (at agent ?l)))
               (at agent ?loc)
               (increase (total-cost) 1)))

; Genere par la contrainte « regroupez-vous avant d'entrer dans un batiment ».
(:action EnterBuilding
  :parameters (?loc - location)
  :precondition (and (known ?loc) (not (at agent ?loc))
                     (building ?loc)
                     (at agent group))
  :effect (and (forall (?l - location) (not (at agent ?l)))
               (at agent ?loc)
               (increase (total-cost) 1)))
```

L'union des préconditions des deux schémas produits couvre exactement les instances du schéma
d'origine, et leur intersection est vide : c'est ce qu'un test vérifie, et c'est la propriété qui rend
la scission sûre.

#### La compilation est plus stricte que la contrainte, et c'est voulu

`sometime-before(φ, ψ)` demande que `ψ` ait été vrai **à un moment quelconque** avant `φ`. La forme
compilée, elle, exige `(at agent group)` dans l'état **immédiatement** précédent. Elle est donc :

* **correcte** (*sound*) — tout plan accepté par le domaine compilé satisfait la contrainte d'origine ;
* **incomplète** — un plan qui rejoint le groupe, traverse une rue, puis entre dans le bâtiment
  satisfait `sometime-before` mais est rejeté par le domaine compilé.

L'approximation va dans le sens de l'intention du joueur : quand il dit « regroupez-vous avant
d'entrer », il veut le regroupement à l'entrée, pas un regroupement quelconque dans le passé. On
retient donc la lecture stricte, en le documentant plutôt qu'en le découvrant plus tard. Le sens de
l'implication compte pour le portillon 4, qui vérifie la correction — la direction qui peut faire du
mal — et pas la complétude.

### Où vit le domaine compilé

`config/planner_domain.json` n'est **jamais** modifié, et le `domain.pddl` versionné de la spec G non
plus. La compilation produit, pour la durée de la mission :

* un objet en mémoire, rendu vers GBFS pour que le jeu change réellement de comportement ;
* `missions/<id>/domain.constrained.pddl`, écrit sur disque pour le portillon 3, l'inspection et les
  tests.

Retirer toutes les instructions rend le jeu bit à bit identique à ce qu'il était.

---

## Prérequis

Deux bloquants — le premier est un contrat avec la spec G, le second une modification bornée du jeu —
et un troisième qui ne bloque rien mais délimite ce que la grammaire peut adresser.

### 1. La spec G doit livrer une représentation, pas seulement des fichiers

Un `tools/pddl/domain.pddl` seul — un artefact texte, versionné, généré une fois — suffit à prouver que
le domaine est bien formé, et ne suffit pas à ce livrable, qui doit **transformer** ce domaine à chaque
instruction.

Trois exigences, désormais **écrites dans la spec G elle-même** (section « Ce que l'export doit
exposer ») et rappelées ici parce qu'elles conditionnent tout ce qui suit — aucune ne change son
périmètre ni son budget :

1. **L'exportateur vit dans `Hordinary.Core`**, sous `AI/Planning/Pddl/`, pas dans un script de
   `tools/`. Le dossier `tools/pddl/` reste ce qu'il est : la sortie, versionnée.
2. **L'export passe par une représentation intermédiaire explicite** — `PddlDomain`, `PddlAction`,
   `PddlFormula` — et non par une concaténation de chaînes. `PddlWriter` rend le texte à partir de
   cette structure. C'est le seul vrai ajout, et il est de l'ordre de la demi-journée : l'exportateur
   construit déjà cette information, il la garde au lieu de l'aplatir tout de suite.
3. **L'export est appelable sur un domaine en mémoire**, pas seulement sur
   `config/planner_domain.json` lu depuis le disque.

**Ce n'est pas un import PDDL.** La spec G exclut d'écrire un parseur, et cette exclusion tient : rien
ici ne lit du texte PDDL. Le chemin est `planner_domain.json → PddlDomain → { texte pour Fast Downward,
PlanningDomain pour GBFS }`. La conversion `PddlDomain → PlanningDomain` est un rendu en mémoire, pas
une analyse syntaxique — mêmes structures de données, tout en processus.

C'est cette représentation partagée qui garantit sans effort la propriété qui compte : **le domaine
donné à Fast Downward et le domaine donné à GBFS sont deux rendus du même objet.** Il n'y a pas deux
compilateurs à maintenir d'accord, donc pas de dérive possible entre ce que le portillon 3 vérifie et
ce que le jeu exécute. Un test compare les deux rendus schéma par schéma ; il devrait être trivialement
vert, et c'est exactement pour ça qu'il est là.

### 2. Les bâtiments doivent exister pour le planificateur

**Fait.** Le domaine ne nomme aucun lieu de mission : `LocateObjective`, `SearchRoom` et
`PickUpObjective` portent sur `objective_zone`, le lieu que `config/missions.json` désigne — une
pharmacie, un entrepôt ou un hangar, indifféremment. Et le cas général est couvert : **tous** les
bâtiments, rues et ruelles sont nommés, pas seulement celui qu'on vise.

Trois changements, bornés :

* `PerceptionSystem.RememberZones` — apercevoir un bout d'entrepôt, c'est savoir qu'il y a un
  entrepôt là. Seules les catégories dont la carte compte plusieurs exemplaires passent par là
  (`ZoneKinds.IsCommon`) : la pharmacie, le camp et l'extraction sont annoncés au briefing et se
  reconnaissent au sol, et déplacer leur souvenir à chaque coup d'œil changerait un lieu que tout le
  reste du jeu tient pour fixe. Le premier regard fait foi, sauf quand il n'avait vu qu'une façade —
  une case praticable du même lieu la remplace, parce que c'est le moment où l'on apprend par où
  entrer ;
* `SymbolicStateBuilder.ObjectNameOf` — `Entrepôt ouest` → `entrepot_ouest`. Les accents tombent et
  les espaces deviennent des soulignés : ce nom traverse le PDDL, où un symbole est un mot sans
  ponctuation ;
* `SymbolicStateBuilder.KindNameOf` — un prédicat de catégorie par objet : `building(entrepot_ouest)`,
  `street(rue_principale)`, `alley(ruelle)`, et `building(objective_zone)` pour celui de la mission,
  sans quoi l'instruction vaudrait pour tous les bâtiments sauf celui qu'on envoie fouiller.

Les nouveaux lieux héritent du traitement des lieux réels — accessibilité, distance A\*, seuil d'un
lieu fermé — plutôt que de celui des repères recalculés (`cover`, `frontier`, `group`). Une
**exception** : une rue n'a pas de porte. Rattacher une ouverture à un lieu `IsOpenAir()` ferait
passer un quartier entier pour une pièce verrouillée dès qu'un survivant apercevrait une porte close
depuis le trottoir, et `Relocate` ramènerait la rue à ce pas de porte.

**Pourquoi un prédicat et pas un sous-type PDDL.** La tentation est d'écrire
`(:types building street alley - location)` et de typer directement le schéma scindé
(`:parameters (?b - building)`), ce qui serait plus propre. Ça ne marche pas pour la moitié négative :
le typage PDDL est une hiérarchie de subsomption **sans négation**, et il n'existe aucun type
« location qui n'est pas un building ». Le `MoveToLocation` résiduel aurait besoin de
`(not (building ?loc))` de toute façon, donc du prédicat. On garde le prédicat des deux côtés, par
symétrie et parce qu'un seul mécanisme se teste mieux que deux. La hiérarchie de types reste celle de
la spec G, inchangée.

Effet de bord surveillé : le nombre d'objets `location` augmente, donc le facteur de branchement de
`MoveToLocation`. La vérification est une **mesure**, sur un quartier généré — le cas défavorable,
bien plus fourni que les sept zones de la carte dessinée : aucune recherche n'atteint
`planner.max_nodes: 1000` ni `planner.max_time_ms: 50`
(`Nommer_tous_les_lieux_ne_fait_pas_deborder_les_budgets_du_planificateur`). Un plafond atteint un
jour se relèverait dans `config/game_config.json`, jamais en dur.

### 3. Le répertoire réellement joué est plus court que le domaine

**Non bloquant, mais à connaître avant d'écrire la grammaire.** Le domaine compte vingt-quatre schémas ;
le survivant n'en joue pas vingt-quatre. Une action n'est atteignable que si une chaîne arrière la relie à
un but de la section `goals` de [`config/planner_domain.json`](../config/planner_domain.json), **et**
qu'un objectif de [`config/utility_goals.json`](../config/utility_goals.json) réclame ce but. Cinq
schémas n'ont ni l'un ni l'autre :

| Schéma | Ce qui manque |
|---|---|
| `Observe`, `Wait`, `UseItem`, `PushHeavyObject` | leurs effets — `observed`, `waited`, `used`, `pushed` — ne sont lus par aucune précondition et par aucun but. `ActionRelevance` les écarte pour **tous** les objectifs : elles ne peuvent apparaître dans aucun plan. |
| `ShareKnowledge` | le but symbolique existe, aucun objectif d'utilité ne le demande. |

`DropItem` s'y ajoute à demi : elle passe le tri de pertinence par `item_at` et `!has`, mais rien ne
demande jamais de poser un objet, et elle ne peut apparaître que dans un cycle que GBFS ne préfère pas.

`BarricadeDoor` est le cas inverse : la [spec 11](./11-objets-armes-et-economie-du-camp.md) la décrit,
`WorldPlanRuntime.SetFlag` sait l'exécuter (branche `door_barricaded`) — mais elle n'a **aucune entrée**
dans le domaine. Le moteur sait barricader une porte ; personne ne sait le lui demander.

**La diversion est dans le même état, et c'est ce livrable qui l'y a mise.**
`CreateNoiseDistraction`, `ThrowFirecracker`, le but `CreateDistraction` et le prédicat
`distraction_created` ont été retirés : se faire du bruit sur soi pour dégager un allié est un sacrifice
calculé, pas un réflexe. Le verbe `emitNoise` et `WorldPlanRuntime.ThrowNoise` restent exécutables, et
« jette un pétard là-bas » est l'une des instructions tactiques les plus évidentes à rendre au joueur —
elle vise une case, ce qu'aucun objectif d'utilité ne savait exprimer.

Deux trous de couverture qui touchent des gestes qu'on croirait acquis :

* **`PickUpItem` ne sert plus qu'à s'armer.** Le seul chemin qui y mène est `EquipWeapon` →
  `AttackWithWeapon` → `FightZombie` ; le second, `ThrowFirecracker` → `CreateDistraction`, est parti
  avec la diversion. Aucun but ne dit « ramasse ce qui traîne » : ramasser des vivres n'est pas
  planifiable, et un pétard au sol ne sert plus à rien tant que le joueur ne le réclame pas.
* **Un blessé ne peut pas planifier de ramasser un bandage qu'il voit.** `HealSelf` exige
  `has(agent, remedy)`, et `SymbolicStateBuilder` ne déclare `remedy` que si le sac en contient **déjà**
  un ; ce qui soigne au sol reste un `item_at(bandage, spot_x_y)` que rien ne relie au soin. L'objectif
  d'utilité `HealSelf` a la même précondition (`has_remedy`) et ne score même pas.

C'est ce livrable-ci qui doit leur donner un demandeur, et c'est cohérent avec sa règle non
négociable : « utiliser un objet », « pousser ce meuble », « barricade cette porte », « ramasse ce qui
traîne » sont des **stratégies**, pas des réflexes. Elles n'ont donc rien à faire dans un objectif
d'utilité de plus — elles appartiennent à l'instruction tactique du joueur. Les schémas sont déjà
écrits et déjà exécutables : ce qui manque est le but qui les réclame.

---

## Les quatre portillons

Aucun n'est facultatif, et aucun ne fait confiance au précédent.

### 1. Grammaire — déterministe, dans `Hordinary.Core`

La contrainte se parse dans l'une des cinq formes ; chaque prédicat cité figure dans le
`(:predicates …)` du domaine exporté, avec la bonne arité ; chaque schéma d'action ou classe de schémas
citée existe dans ce même domaine ; les valeurs numériques de `penalize` sont dans les bornes de la
configuration.

Le vocabulaire de référence est **le domaine PDDL, pas une liste tenue à la main**. C'est un gain réel :
il n'y a plus d'endroit où oublier de mettre à jour la liste des symboles autorisés quand le livrable 11
touche au domaine.

Un symbole inventé est rejeté ici. C'est ce portillon qui empêche le cas d'école : le modèle propose
`require_before(barricaded(?door), enter:building)`, `barricaded` n'est pas dans `(:predicates …)`,
refus immédiat.

### 2. Compilation — déterministe, dans `Hordinary.Core`

Le compilateur prend un `PddlDomain` et une liste de contraintes actives, et rend un `PddlDomain`. Rien
d'autre n'entre, rien d'autre ne sort — c'est une fonction pure, ce qui la rend facile à tester
exhaustivement.

Les cinq formes se compilent ainsi :

| Forme | Réécriture PDDL |
|---|---|
| `require_before(P, A)` | scission du schéma en deux, `P` ajouté à la précondition de la moitié visée |
| `require_after(P, A)` | `P` ajouté aux effets de `A` s'il l'établit déjà ; sinon scission et précondition sur la moitié qui l'établit |
| `forbid(A)` | retrait du bloc `(:action A …)` |
| `require_always(P)` | régression de `P` à travers les effets de chaque schéma, ajoutée en précondition |
| `penalize(A, k)` | `(increase (total-cost) 1)` devient `(increase (total-cost) k)` |

La régression de `require_always` est simple ici parce que tous les effets du domaine sont des
littéraux : régresser un littéral positif `P` à travers un schéma vaut *vrai* si le schéma ajoute `P`,
*faux* s'il le retire, et `P` lui-même sinon. Le seul cas particulier est l'effet universel
`(forall (?l - location) (not (at agent ?l)))` de `MoveToLocation`, qui retire `at` pour toute
instance : une contrainte `require_always` sur `at` rendrait tout déplacement impossible, et le
compilateur doit le détecter et le dire, pas produire un domaine mort.

Le compilateur échoue explicitement quand :

* `forbid` retirerait le dernier schéma produisant un prédicat exigé par le but courant ;
* deux contraintes actives se contredisent syntaxiquement (`forbid(A)` et `require_after(P, A)`) ;
* la régression de `require_always` est *faux* pour tout schéma applicable.

Et il **avertit sans refuser** quand :

* la scission produirait un schéma sans instance possible sur cette carte — pas de fenêtre, aucun
  bâtiment connu : l'instruction est acceptée et signalée comme sans effet ici ;
* la contrainte est un `penalize` alors que la recherche active ignore `g` (voir plus haut).

### 3. Satisfiabilité — Fast Downward, hors ligne

**C'est le portillon qui justifie [G-export-pddl.md](./G-export-pddl.md), et c'est aussi celui que le
passage au PDDL simplifie le plus.**

Il n'y a plus d'étape d'export à ce moment-là : le domaine compilé **est déjà** un `PddlDomain`.
`PddlWriter` le rend en texte, l'état courant donne le problème comme dans la spec G, et Fast Downward
les prend tels quels. Le portillon 3 se réduit à écrire deux fichiers et à lire un code de retour.

S'il prouve qu'aucun plan n'existe pour le but actif, l'instruction est **refusée avant d'être
appliquée**, et le joueur lit pourquoi :

> « Impossible : Claire est déjà à l'intérieur de l'entrepôt. Personne ne peut se regrouper avant
> d'entrer sans qu'elle sorte d'abord. »

Quand plusieurs instructions sont actives, le refus doit désigner le **coupable minimal** : retirer les
contraintes une à une et relancer, jusqu'à trouver le plus petit ensemble qui reste insatisfiable.
C'est *n* appels supplémentaires à Fast Downward, hors boucle de jeu, et c'est ce qui transforme
« impossible » en une phrase utile.

Une preuve d'insatisfiabilité affichée au joueur : c'est le meilleur moment de démonstration du projet,
et il n'existe pas sans planificateur tiers.

### 4. Conformité — déterministe, dans `Hordinary.Core`

Le portillon 3 vérifie le domaine compilé. Le portillon 4 vérifie **la compilation elle-même** — ce qui
est un problème différent, et va dans l'autre sens.

C'est ici que la forme déclarative sert. Une contrainte a **deux rendus PDDL** :

* la forme compilée — des préconditions, dans le fragment ADL, donnée au planificateur ;
* la forme déclarative — un `(:constraints (sometime-before …))` PDDL 3, donnée à **personne qui
  planifie**, et qui existe uniquement pour vérifier la première.

Le portillon 4 est la question « ces deux rendus disent-ils la même chose ? ». Un vérificateur de
trajectoire rejoue le plan produit par GBFS état par état et contrôle qu'il satisfait la forme
**déclarative**. Une compilation fausse produit un plan valide au regard du domaine réécrit et faux au
regard de la phrase du joueur : rien d'autre dans la chaîne ne le voit.

Le vérificateur est écrit en C#, interprète les quatre modalités PDDL 3 utilisées (`sometime-before`,
`sometime-after`, `always`, plus le cas dégénéré du retrait de schéma) et tourne toujours, sans binaire
externe. Il n'a besoin d'aucun parseur : il lit la structure `PddlFormula`, pas du texte.

VAL sert de corroboration extérieure sur les instantanés de `tools/pddl/snapshots/`, en lui donnant le
domaine **avec** son bloc `(:constraints …)` et le plan de GBFS. C'est l'usage exact pour lequel les
contraintes PDDL 3 ont été ajoutées au langage, et VAL est l'outil qui les valide — mais son niveau de
support réel reste à confirmer pendant le pic technique de la spec G. Si le support n'est pas là, le
portillon 4 ne perd rien : il perd un témoin, pas sa fonction.

---

## Les refus sont le contenu, pas l'échec

Quatre familles, toutes à documenter, toutes à tester :

| Phrase | Verdict | Raison rendue au joueur |
|---|---|---|
| « Priorité aux blessés » | **Refus de grammaire** | Une préférence, pas une contrainte : relève du choix d'objectif (Utility AI), pas du plan |
| « Regroupez-vous avant d'entrer » avec un survivant déjà dedans | **Refus de satisfiabilité** | Portillon 3, avec le coupable nommé |
| « Ne cassez aucune vitre » sur une carte sans fenêtre | **Accepté, sans effet** | Portillon 2, avertissement affiché |
| « Ne cassez aucune vitre » + « entrez par la fenêtre » | **Refus de compilation** | Contradiction syntaxique, portillon 2 |

Un système qui n'affiche que ses succès ne prouve rien. Le journal des instructions garde le refus, sa
raison et le portillon qui l'a produit.

---

## Le service

### Architecture

```text
Godot (Scripts/Instructions/InstructionClient.cs)
   │  HTTP POST /instruction, JSON, asynchrone
   ▼
FastAPI  (service/  — projet Python separe, hors solution .NET, partage avec le livrable 14)
   │
   ▼
LangGraph — graphe a cinq noeuds
   │
   ├── 1. normalize   nettoie la phrase, detecte la langue, coupe au-dela de max_chars
   ├── 2. context     compose le digest, derive du domaine PDDL : predicats et leur arite,
   │                  schemas d'action, classes de lieux presentes sur CETTE carte,
   │                  contraintes deja actives
   ├── 3. generate    appel LangChain, sortie structuree contrainte par un schema JSON
   ├── 4. self_check  relit sa propre sortie contre le digest ; une reprise, puis abandon
   └── 5. project     renvoie la contrainte, ou un refus motive — jamais du texte libre
   │
   ▼
Fournisseur de modele : OpenRouter uniquement
```

Le nœud 3 demande une **sortie structurée** (schéma JSON côté fournisseur quand il le supporte), et le
nœud 4 la revalide de toute façon. On ne fait jamais confiance au drapeau `json_mode` : il réduit le
taux d'échec, il ne le supprime pas.

Le nœud 2 mérite une note. Le digest est **dérivé du `PddlDomain`**, pas rédigé à la main : le service
et le portillon 1 partagent donc la même source de vérité sur ce qui est nommable, et un symbole que le
modèle a vu dans son contexte ne peut pas être un symbole que la grammaire refuse. Ce digest — une
trentaine de schémas d'action après le livrable 11, leurs prédicats, les classes de lieux — pèse
quelques kilo-octets et **tient entièrement dans la fenêtre de contexte**. Il n'y a donc pas de récupération documentaire ici, et il ne
faut pas en ajouter (voir [Sur le RAG](#sur-le-rag-il-ny-en-a-pas-et-cest-un-choix)).

Le digest reste une **projection** du domaine, en JSON, et non le texte PDDL brut. Les deux tiennent
dans la fenêtre ; la projection coûte moins de jetons et évite d'apprendre au modèle une syntaxe qu'il
n'a pas à produire — il rend une contrainte de la grammaire fermée, jamais du Lisp.

### Choix du fournisseur

OpenRouter est l'unique fournisseur de modèles. L'inférence locale est retirée de la conception et
des tests : la machine cible ne dispose pas de la VRAM nécessaire pour l'exécuter de façon fiable.
LangChain fournit l'adaptateur vers l'API OpenRouter, avec accès à plusieurs familles de modèles.

| | `openrouter` |
|---|---|
| Modèles | catalogue distant, plusieurs familles |
| Coût | facturé au jeton |
| Réseau | requis pour la traduction en langage naturel |
| Déterminisme | selon le fournisseur amont, **non garanti** |
| Sortie structurée | schéma JSON ou appel d'outil selon le modèle |
| Usage visé | **comparer les modèles**, produire le tableau d'évaluation |

**Le déterminisme du jeu ne vient jamais du modèle.** Il vient de deux choses : le cache
`(phrase normalisée, graine, empreinte du domaine)`, et le fait qu'une contrainte acceptée est **figée**
dans la sauvegarde de la mission. Rejouer une mission ne réinterroge aucun modèle. C'est ce qui permet
d'utiliser un fournisseur distant sans rendre les parties irreproductibles.

### Contrat d'API

```jsonc
// POST /instruction
{
  "utterance": "Avant d'entrer dans un batiment, regroupez-vous",
  "seed": 20260915,
  // projete depuis le PddlDomain de la spec G, jamais redige a la main
  "domain_digest": {
    "domain_hash": "sha256:1f3a...",
    "types": ["location", "door", "window", "item", "ally", "threat"],
    "predicates": [
      { "name": "at",        "params": ["object", "location"] },
      { "name": "known",     "params": ["location"] },
      { "name": "building",  "params": ["location"] },
      { "name": "door_open", "params": ["door"] }
    ],
    "actions": ["MoveToLocation", "OpenDoor", "ForceDoor", "BreakWindow", "..."],
    "location_kinds": ["building", "street", "alley", "pharmacy", "extraction", "start"],
    "active_constraints": []
  },
  "max_chars": 200
}

// 200 OK — accepte
{
  "status": "constraint",
  "constraint": {
    "form": "require_before",
    "predicate": "at(agent, group)",
    "action_class": "enter:building"
  },
  "rationale": "regroupement exige avant tout deplacement vers un lieu de type batiment",
  "model": "anthropic/claude-haiku-4.5",
  "latency_ms": 1240,
  "tokens": { "prompt": 890, "completion": 64 },
  "cost_usd": 0.0012
}

// 200 OK — refuse par le service lui-meme (noeud 4)
{
  "status": "rejected",
  "reason": "preference_not_constraint",
  "message": "« priorite aux blesses » exprime une preference d'objectif, pas une regle de plan",
  "model": "anthropic/claude-haiku-4.5"
}
```

Le service ne renvoie **jamais** de patch de domaine, jamais de PDDL, jamais de JSON d'action, jamais
de texte libre à afficher tel quel. Sa sortie est une contrainte de la grammaire fermée, ou un refus.
Cette restriction est délibérée : elle réduit d'un ordre de grandeur ce qu'il y a à valider côté jeu.
Le modèle n'écrit pas une ligne de ce qui sera donné à un planificateur — la totalité du PDDL sort du
compilateur déterministe.

`status: "constraint"` ne veut pas dire « appliquée ». Les quatre portillons tournent ensuite, côté
`Hordinary.Core`, et peuvent encore refuser.

### Configuration

```json
{
  "instructions": {
    "enabled": false,
    "provider": "openrouter",
    "endpoint": "http://127.0.0.1:8001/instruction",
    "openrouter": {
      "model": "anthropic/claude-haiku-4.5",
      "api_key_env": "OPENROUTER_API_KEY",
      "base_url": "https://openrouter.ai/api/v1",
      "max_cost_usd_per_session": 0.50
    },
    "timeout_ms": 8000,
    "retries": 1,
    "max_chars": 200,
    "max_active_constraints": 5,
    "cache_enabled": true,
    "presets_when_disabled": true
  }
}
```

Livré à `false`, comme le dialogue. La clé d'API se lit dans une variable d'environnement, **jamais**
dans un fichier de configuration versionné.

`timeout_ms` vaut 8000, contre 800 au livrable 14 : compiler une instruction n'est pas dans la boucle de
jeu. Le joueur a tapé une phrase et attend une réponse — deux secondes sont acceptables, une réplique
de bavardage en retard ne l'est pas. C'est la même architecture, ce n'est pas le même budget.

`max_cost_usd_per_session` est un plafond dur côté client : dépassé, les nouvelles requêtes OpenRouter
sont refusées et le client bascule sur les instructions pré-écrites en le signalant.

---

## Sur le RAG : il n'y en a pas, et c'est un choix

**Verdict : aucune récupération documentaire dans le chemin critique.** Ni ici, ni au livrable 14.

Les raisons, dans l'ordre :

* **Il n'y a pas de corpus.** Ce qu'il faut donner au modèle — une trentaine d'actions, leurs prédicats,
  les classes de lieux — pèse quelques kilo-octets et tient entièrement dans le contexte. Mettre une base
  vectorielle devant huit kilo-octets de texte résout un problème qui n'existe pas, et un lecteur
  technique le voit immédiatement.
* **Ce qu'un survivant sait n'est pas un document.** L'`AgentKnowledge` est un état structuré :
  l'interroger est un filtre typé, pas une recherche par similarité. L'appeler RAG serait un abus de
  vocabulaire.
* **La mesure vaut mieux que la mode.** Dire « le contexte tient dans la fenêtre, je l'ai mesuré, je
  n'ai pas ajouté d'index » est un signal plus fort que d'exhiber un Chroma inutile. La question à se
  poser devant un recruteur n'est pas « sais-tu faire du RAG » mais « sais-tu quand ne pas en faire ».

**La seule ouverture honnête, et elle est optionnelle.** Une fois quelques dizaines d'instructions
acceptées accumulées, sélectionner les *k* exemples passés les plus proches de la phrase courante pour
les injecter en few-shot est une vraie récupération, sur un vrai corpus, avec un effet mesurable sur le
taux de compilation. Elle arrive **après** le corpus, jamais avant, et elle entre dans l'évaluation
comme une ablation parmi d'autres — avec obligation de publier le chiffre, y compris s'il est négatif
ou nul.

---

## Évaluation

C'est la partie qui compte pour le portfolio, et elle ne demande aucun modèle pour être écrite.

**Le corpus.** Soixante phrases de joueur, écrites à la main, dans `service/evals/utterances.jsonl`.
Chacune porte son attendu : une contrainte exacte, ou un refus avec sa famille. Il contient des phrases
ambiguës, des phrases hors sujet, des préférences déguisées en règles, et des formulations que la
grammaire ne peut pas exprimer. Un corpus qui ne contient que des cas faciles ne mesure rien.

**Les mesures**, par modèle :

| Mesure | Pourquoi elle est là |
|---|---|
| Taux de compilation | combien de phrases deviennent une contrainte valide |
| Taux de refus correct | combien de phrases irrecevables sont refusées, et pour la bonne raison |
| **Taux de faux acceptés** | **la seule qui compte vraiment** : une contrainte plausible mais fausse |
| Exactitude de la forme | la bonne des cinq formes a-t-elle été choisie |
| Latence p50 / p95 | |
| Coût par instruction | mesuré via OpenRouter |

Le taux de faux acceptés est le chiffre à mettre en avant, y compris s'il est mauvais : c'est ce que
les quatre portillons existent pour rattraper, et c'est ce qui rend la démonstration honnête.

**Les comparaisons** : au moins trois modèles via OpenRouter, sur le même corpus, avec la même graine.
Plus deux ablations — avec et sans digest de domaine, avec et sans
few-shot. Le tableau de résultats vit dans `service/evals/results.md` et **un paragraphe** en sort pour
les README du projet.

---

## Architecture concernée

Venant de la spec G, dans `Hordinary.Core` : `AI/Planning/Pddl/PddlDomain`, `PddlAction`,
`PddlFormula`, `PddlWriter`, plus le rendu `PddlDomain → PlanningDomain` qui alimente GBFS. Ce
livrable les **consomme** ; il ne les écrit pas.

Ajouté ici, déterministe et testable sans réseau : `AI/Instructions/TacticalConstraint` (les cinq
formes, chacune avec ses deux rendus PDDL), `AI/Instructions/ConstraintGrammar` (portillon 1),
`AI/Instructions/DomainCompiler` (portillon 2, `PddlDomain → PddlDomain`),
`AI/Instructions/TrajectoryChecker` (portillon 4, interprète les modalités PDDL 3),
`AI/Instructions/InstructionLog`.

Ailleurs : `Scripts/Instructions/InstructionClient.cs` (Godot, HTTP asynchrone),
`Scripts/UI/InstructionPanel.cs` (saisie, refus motivés, liste des contraintes actives),
`tools/pddl/` (sorties versionnées et lancement de Fast Downward, spec G), `service/` (partagé avec le
livrable 14 : FastAPI, LangGraph, `requirements.txt`, `evals/`).

Modification de `SymbolicStateBuilder` (prérequis des bâtiments). Nouvelle section `instructions` dans
`config/game_config.json`. Nouveau fichier `config/instruction_presets.json` (les instructions
pré-écrites du mode désactivé). Nouveau fichier par mission
`missions/<id>/domain.constrained.pddl`.

## Étapes à créer

* [x] **Prérequis G** : sortir l'exportateur PDDL dans `Hordinary.Core`, derrière une représentation
      `PddlDomain` explicite, et écrire le rendu `PddlDomain → PlanningDomain` qui alimente GBFS.
      Vérifier que le jeu se comporte à l'identique en passant par ce chemin, **avant toute
      contrainte** : c'est un refactoring à comportement constant, et il se valide comme tel.
* [x] **Prérequis carte** : nommer les zones `Building`, `Street`, `Alley` comme objets de
      planification et émettre leurs prédicats de catégorie. Vérifier les budgets `max_nodes` /
      `max_time_ms`.
* [x] Définir les cinq formes de `TacticalConstraint`, chacune avec son rendu déclaratif PDDL 3 et sa
      règle de compilation, et la grammaire fermée (portillon 1).
* [x] Écrire le compilateur `PddlDomain → PddlDomain` : scission de schéma, préconditions, retrait,
      régression de `require_always`, coûts (portillon 2).
* [x] Écrire le vérificateur de trajectoire sur les modalités PDDL 3 (portillon 4).
* [x] Écrire `config/instruction_presets.json` : huit instructions pré-écrites couvrant les cinq formes.
* [x] Brancher les contraintes actives sur le planificateur. **La sauvegarde de mission n'est pas faite** :
      les contraintes ne sont pas figées, rejouer une mission demande de les reposer.
* [x] Interface : saisie, contraintes actives, refus motivés, journal.
* [x] **Vérifier ici que tout est jouable et démontrable sans modèle**, via les instructions
      pré-écrites. Tout ce qui suit est du supplément.
* [ ] Brancher le portillon 3 : rendu texte du domaine compilé, problème depuis l'état courant, appel
      à Fast Downward, recherche du coupable minimal.
* [ ] Créer l'endpoint `/instruction` dans `service/` et le graphe LangGraph à cinq nœuds.
* [ ] Implémenter l'adaptateur OpenRouter et le plafond de coût.
* [ ] Écrire `InstructionClient.cs` : asynchrone, timeout, cache, repli sur les instructions
      pré-écrites.
* [ ] Écrire le corpus d'évaluation (soixante phrases) et le harnais.
* [ ] Lancer les comparaisons de modèles et les deux ablations ; publier `service/evals/results.md`.
* [ ] Documenter l'installation du service et la configuration OpenRouter dans les README.

## Critère de validation

Trois validations, dans cet ordre :

1. **Service arrêté, `instructions.enabled: false`** : une instruction pré-écrite est appliquée, le
   domaine compilé change, le comportement des survivants change, et la totalité des tests passe.
2. **Service démarré** : « Avant d'entrer dans un bâtiment, regroupez-vous » produit la contrainte
   attendue, compile, passe les quatre portillons, et un survivant qui allait entrer seul rejoint
   d'abord le groupe.
3. **Refus prouvé** : la même instruction, dans une situation où un survivant est déjà à l'intérieur,
   est refusée avant application, avec la contrainte coupable nommée — et la mission reste jouable.

Le troisième critère est le plus important des trois.

## Tests associés

Dans `Tests/` (voir [C-tests.md](./C-tests.md)) — tous exécutables sans réseau :

* les cinq formes se compilent en un `PddlDomain` dont chaque schéma reste bien formé, et dont chaque
  prédicat cité est déclaré ;
* **le domaine compilé déclare exactement les mêmes `:requirements` que le domaine d'origine, et aucun
  bloc `(:constraints …)`** — la garantie qu'on ne sort jamais du fragment que Fast Downward accepte ;
* **les deux rendus du même `PddlDomain` compilé s'accordent** : le texte donné à Fast Downward et le
  `PlanningDomain` donné à GBFS déclarent les mêmes schémas, avec les mêmes préconditions et les mêmes
  effets. Test qui doit être trivialement vert, et qui casse le jour où quelqu'un ajoute un second
  chemin de compilation ;
* `require_before` scinde le schéma visé en deux : l'union de leurs préconditions couvre exactement les
  instances du schéma d'origine, et leur intersection est vide ;
* `require_always` sur un prédicat retiré par l'effet universel de `MoveToLocation` est refusée avec sa
  raison, et ne produit pas un domaine où plus rien n'est applicable ;
* une contrainte sur une classe de lieux absente de la carte produit un avertissement, pas un refus ;
* un `penalize` sous une recherche qui ignore `g` produit un avertissement « sans effet », pas un refus ;
* `forbid` sur le dernier schéma produisant un prédicat du but courant est refusé à la compilation ;
* deux contraintes syntaxiquement contradictoires sont refusées, en nommant les deux ;
* le vérificateur de trajectoire rejette un plan produit par une compilation volontairement fausse —
  **le test qui valide le portillon 4** ;
* le vérificateur accepte un plan qui satisfait `sometime-before` sans satisfaire la forme compilée
  (regrouper, traverser une rue, puis entrer) : c'est l'incomplétude assumée, et elle est testée pour
  qu'on ne la corrige pas par accident ;
* un prédicat ou un schéma inconnus sont rejetés par la grammaire ;
* le domaine livré `config/planner_domain.json` et le `tools/pddl/domain.pddl` versionné sont
  inchangés après compilation puis retrait de toutes les contraintes ;
* une mission rejouée avec la même graine et les mêmes contraintes figées produit le même plan, sans
  aucun appel réseau ;
* avec `instructions.enabled: false`, aucune requête n'est émise et les instructions pré-écrites
  restent disponibles ;
* un service qui ne répond pas dans le `timeout_ms` n'empêche ni la mission ni les instructions
  pré-écrites ;
* un service qui renvoie une contrainte hors grammaire voit sa réponse rejetée par le portillon 1 ;
* `Hordinary.Core` ne référence aucun client HTTP.

Dans `service/tests/` :

* le nœud `project` ne renvoie jamais autre chose qu'une contrainte de la grammaire ou un refus ;
* une phrase exprimant une préférence est refusée avec la famille `preference_not_constraint` ;
* le digest de domaine transmis ne contient que des prédicats et actions réellement déclarés ;
* l'adaptateur OpenRouter produit le contrat de sortie attendu, avec réponse simulée dans les tests ;
* le plafond de coût coupe les appels et bascule sur les instructions pré-écrites.

Tests d'intégration, marqués `Skip` si le binaire est absent :

* les dix instantanés de `tools/pddl/snapshots/`, recompilés avec une contrainte active, restent
  acceptés par Fast Downward — le domaine compilé est toujours du PDDL bien formé ;
* un domaine compilé rendu insatisfiable est bien détecté comme tel par Fast Downward, et la recherche
  du coupable minimal désigne la bonne contrainte ;
* VAL, sur un instantané, valide le plan de GBFS contre le domaine **avec** son bloc
  `(:constraints …)` — et le rejette quand la contrainte est violée. `Skip` aussi si le support PDDL 3
  n'est pas au rendez-vous.

## Limitations connues

* **Le regroupement est approximatif.** Chaque survivant planifie seul, et `group` est le barycentre des
  alliés — qui bouge quand ils bougent. « Être au barycentre » n'est pas exactement « être regroupés ».
  C'est déjà la modélisation qu'avait le but `Regroup` du livrable 4, retiré depuis : ce livrable n'empire rien, il hérite — et c'est désormais le joueur, et lui seul, qui demande le regroupement.
* **Les contraintes sont collectives.** Une instruction s'applique aux trois survivants. Des
  instructions individuelles demanderaient un domaine compilé par agent : possible, hors périmètre.
* **Pas de contrainte conditionnelle ni chaînée.** « Si vous êtes moins de deux, ne rentrez pas » sort
  de la grammaire, et l'y faire entrer ferait exploser le compilateur. Refusé explicitement.
* **La compilation est correcte, pas complète.** La lecture « immédiatement avant » est plus stricte
  que le `sometime-before` de PDDL 3 : des plans que la contrainte autorise sont rejetés. Choix
  documenté, testé, et conforme à ce que le joueur veut dire — mais un lecteur qui connaît PDDL 3 le
  remarquera, donc autant l'écrire.
* **`penalize` ne fait rien tant que la recherche est gloutonne.** Le coût est exact dans le PDDL et
  ignoré par GBFS, qui ordonne sur `h` seul. La forme est acceptée avec avertissement. La lever
  demanderait une recherche sensible à `g`, ce qui est une décision du livrable 5, pas d'ici.
* **Le portillon 3 voit le domaine, pas le moteur.** Certains prédicats sont posés par le runtime et
  jamais par une action ; Fast Downward peut donc déclarer morte une action qui ne l'est pas. La classe
  de faux positifs est documentée dans [G-export-pddl.md](./G-export-pddl.md), et vaut ici aussi.
* **Les portillons ne voient pas les préconditions du runtime.** `WorldPlanRuntime` a ses propres
  conditions en C#, invisibles au domaine. Une contrainte peut produire un plan symboliquement parfait
  dont une action échouera systématiquement à l'exécution. Seule une mission rejouée le montre.
