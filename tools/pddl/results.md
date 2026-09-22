# Fast Downward sur les artefacts versionnés

Ce que le planificateur tiers dit du domaine et des dix instantanés. **Ce fichier n'est pas généré** :
il consigne un passage, avec sa date et sa version, parce qu'un résultat d'outil externe n'est pas
reproductible par la suite de tests seule.

Passage du **21 septembre 2026**, image `aibasel/downward` (`sha256:328732c5cb87…`), via Docker :

```bash
docker pull aibasel/downward

docker run --rm -v "<dossier>:/benchmarks" -w /benchmarks aibasel/downward \
    /benchmarks/domain.pddl /benchmarks/problem.pddl --search "astar(lmcut())"
```

Le binaire n'a pas besoin d'être installé, et le jeu n'acquiert toujours aucune dépendance : c'est une
image tirée une fois, à la main. `Tests/SatisfiabilityTests.cs` la trouve seule et s'en sert quand elle
est là ; absente, les mêmes tests se rabattent sur ce qu'ils peuvent vérifier sans elle.

## Le domaine livré n'était pas du PDDL valide

**C'est le premier appel qui l'a dit, et il l'a dit en une ligne.** Quatre défauts d'export, aucun
visible autrement, tous survivants de toute la spec G :

| | Ce que Fast Downward a répondu | Ce qui n'allait pas |
|---|---|---|
| 1 | `Undefined object: objective_zone` | Une action nomme un lieu que le domaine ne déclare pas. PDDL exige qu'un symbole cité dans un corps d'action soit une constante du domaine ou un paramètre du schéma. Les lieux étaient tous laissés au problème. |
| 2 | `Fractional numbers are not supported. Got: 2.5` | `PushBackZombie` coûte 2,5. PDDL n'admet que des entiers. |
| 3 | `Found the following duplicate objects: cover, extraction, frontier, objective_zone` | Conséquence du correctif de 1 : le problème redéclarait ce que le domaine déclarait désormais. |
| 4 | `Undefined object: group` | Le même défaut que 1, **après compilation d'une contrainte** : le schéma scindé par « avant d'entrer dans un bâtiment, regroupez-vous » nomme `group`, qu'aucune action de base ne nommait. |

Le quatrième est le plus instructif, parce qu'il survit au correctif évident. Déclarer les symboles
*qu'une action nomme* suffisait au domaine livré et cassait à la première consigne du joueur. La règle
retenue est donc plus large, et structurelle : **le vocabulaire nommable est exactement l'ensemble des
constantes du domaine**. Tout ce que le joueur peut citer peut finir en précondition, donc tout doit
être déclaré d'avance — `Le_vocabulaire_nommable_est_exactement_les_constantes_du_domaine` le
verrouille.

Le coût fractionnaire est **multiplié**, pas arrondi : un facteur commun (ici 2, écrit en commentaire
en tête du domaine) laisse l'ordre des plans exactement où il était, alors qu'arrondir 2,5 à 3 rendrait
`PushBackZombie` aussi cher qu'une action à 3, en silence. La structure garde ses flottants ; GBFS
continue de lire les coûts réels.

## Les dix instantanés

Domaine seul, `astar(lmcut())`, tous résolus — code de retour 0.

| Instantané | Coût | Variables | Opérateurs | Plan |
|---|---|---|---|---|
| `01-exploreunknownarea` | 4 | 1 | 1 | `(explore)` |
| `02-exploreunknownarea` | 4 | 1 | 1 | `(explore)` |
| `03-exploreunknownarea` | 4 | 1 | 1 | `(explore)` |
| `04-exploreunknownarea` | 4 | 1 | 1 | `(explore)` |
| `05-exploreunknownarea` | 4 | 1 | 1 | `(explore)` |
| `06-exploreunknownarea` | 4 | 1 | 1 | `(explore)` |
| `07-exploreunknownarea` | 4 | 1 | 1 | `(explore)` |
| `08-exploreunknownarea` | 4 | 1 | 1 | `(explore)` |
| `09-searchforobjective` | 8 | 28 | 39 | `(movetolocation objective_zone)` `(searchroom objective_zone)` `(locateobjective)` |
| `10-completeplayermission` | 2 | 28 | 39 | `(movetolocation mission)` |

Les coûts sont ceux du domaine exporté : la **part fixe** seule, multipliée par 2. Ils ne se comparent
pas à ceux de GBFS, qui compte en plus la distance parcourue.

Le contraste entre les huit premiers et les deux derniers est le résultat le plus parlant du tableau.
Le but `(explored)` est posé directement par `Explore`, donc le traducteur réduit le problème entier à
une variable et un opérateur : les 12 à 25 objets de l'instantané ne servent à rien pour ce but-là.
C'est la même observation qu'`ActionRelevance` fait côté jeu, confirmée par un outil qui ne connaît pas
le jeu.

## Le domaine compilé reste du PDDL accepté

Les dix instantanés, recompilés avec `require_before(at(agent, group), enter:building)` active :
**dix acceptations**, aucun refus de forme. C'est le test que la spécification réclame
(`Les_instantanes_contraints_restent_du_pddl_accepte`), et il n'avait pas de sujet tant qu'aucun
planificateur n'était disponible. Il tourne en 10 s.

Ce qu'il surveille est l'acceptation, pas l'existence d'un plan : une contrainte a parfaitement le
droit de rendre une mission impossible — c'est la raison d'être du portillon 3 — mais jamais celui de
produire un domaine illisible.

## Le portillon 3, prouvé de bout en bout

`Une_consigne_qui_rend_la_mission_impossible_est_prouvee_telle` : trois consignes actives sur
`10-completeplayermission`, dont une seule bloque.

| | |
|---|---|
| Consignes | `forbid(BreakWindow)`, `forbid(MoveToLocation)`, `forbid(ForceDoor)` |
| But | `(at agent mission)` |
| Verdict de Fast Downward | code **11** — tâche prouvée sans solution |
| Coupable désignée | `forbid(MoveToLocation)`, seule |
| Appels au planificateur | 5, pour 3 consignes (*n* + 2) |

Le verdict se lit dans le **code de retour** et non dans le texte de sortie, et la distinction décide
de tout : `11` est une preuve d'impossibilité, `12` est une recherche qui a renoncé. Accuser la
consigne d'un joueur sur un `12` serait précisément l'erreur judiciaire que `SatisfiabilityGate` existe
pour éviter. Le code précédent cherchait la chaîne « unsolvable » dans la sortie, ce qui confond les
deux.

## Les trois questions ouvertes du README, tranchées

**1. `ally` est à la fois un type et un objet — accepté.** Fast Downward lit `(:types ally …)` et la
constante `ally` sans se plaindre : les deux espaces de noms sont bien distincts pour son analyseur. Le
renommage que `SymbolicStateBuilder.LocationAliases` aurait permis n'est donc pas nécessaire, et il
n'a pas été fait. La collision reste un piège de lecture pour un humain, pas pour l'outil.

**2. L'agent en plusieurs endroits à la fois — accepté, et sans conséquence ici.**
`10-completeplayermission` porte `(at agent cover)` et `(at agent objective_zone)` ensemble ; le
traducteur l'accepte et le plan produit est correct. La divergence notée reste réelle — le premier
`MoveToLocation` détruit toutes ces positions et rien n'en restaure plus d'une — mais elle ne se
manifeste sur aucun des dix instantanés, parce qu'aucun but n'exige d'être à deux endroits ensuite.
Elle n'est donc pas close : elle est **sans effet mesuré**, ce qui n'est pas la même chose.

**3. Les actions mortes — non contredites.** Les huit premiers instantanés se réduisent à un opérateur,
ce qui est la version extrême du phénomène et confirme que le traducteur élague sur le but. Aucun faux
positif n'a pu être caractérisé sur ce passage, faute d'un cas où une action posée par le moteur
seulement aurait été déclarée morte à tort.

## VAL : le support PDDL 3 est au rendez-vous

C'était la réserve que la spec traînait depuis le début — « son niveau de support réel reste à
confirmer ». **Il est confirmé.** VAL 4, compilé depuis `tools/pddl/val/Dockerfile` (aucune image
officielle n'existe), lit le bloc `(:constraints …)` et applique `sometime-before` sur notre domaine :

```
(at agent objective_zone) satisfied without (at agent group) sometime before
```

`Val_confirme_que_la_compilation_dit_ce_que_le_joueur_a_dit` pose au validateur la question du
portillon 4, mais à quelqu'un d'autre que nous :

| | |
|---|---|
| Consigne | `require_before(at(agent, group), enter:pharmacy)` |
| Instantané | `09-searchforobjective` — il déclare `(pharmacy objective_zone)`, que le plan traverse |
| Plan du domaine **compilé** contre le domaine **déclaratif** | **valide** |
| Plan du domaine **d'origine** contre le même bloc PDDL 3 | **rejeté**, sur la contrainte |

La première ligne est celle qui compte : *ce que le compilateur autorise satisfait ce que le joueur a
dit*. `TrajectoryChecker` répond déjà à cette question en C#, mais il est écrit par la main qui a écrit
le compilateur — si les deux se trompaient de la même façon, rien ne le verrait. VAL ne sait rien de
notre compilation.

**Le témoin négatif ne peut pas s'adosser au domaine compilé**, et c'est une subtilité qu'un premier
essai a révélée : le plan libre n'y est même pas *exécutable*, puisque la compilation a scindé
`MoveToLocation` et qu'entrer dans une pharmacie passe désormais par un autre schéma. VAL le refusait
pour inapplicabilité — un refus qui ne dit rien de la contrainte. Le bloc PDDL 3 est donc adossé au
domaine **d'origine** pour ce cas-là, où le plan s'exécute et où il viole bel et bien la consigne.

Deux détails de mise en œuvre, appris à l'usage :

* **sans `-v`, VAL ne dit pas pourquoi.** Un plan refusé pour violation de contrainte rend
  « `Plan failed to execute` » et rien de plus. La raison est précisément ce qu'on vient chercher ;
* le drapeau `:constraints` dans `(:requirements …)` **n'est pas nécessaire** — VAL applique le bloc
  sans lui. Il est écrit quand même, parce que c'est ce que PDDL 3 exige et que ce fichier n'existe
  que pour être lu par des outils tiers. Le domaine donné à Fast Downward, lui, ne porte jamais ni le
  bloc ni le drapeau.

## Ce qui reste

**La colonne « plan GBFS » manque.** Le tableau des dix donne le plan de Fast Downward ; comparer les
deux demande de capturer le plan que GBFS avait produit au moment de l'instantané, ce que la capture ne
conserve pas aujourd'hui. On sait donc que le domaine est bien formé et que la compilation dit ce
qu'elle prétend — pas que les deux planificateurs choisissent le même chemin.
