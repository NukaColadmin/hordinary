"""Le contrat d'API du livrable 15, cote service.

C'est le miroir exact de `Hordinary.Core/AI/Instructions/InstructionProtocol.cs` et de
`DomainDigest.cs`. Deux programmes ecrits dans deux langages doivent lire le meme JSON : snake case
des deux cotes, et rien d'autre a se dire.

Le service ne renvoie **jamais** de patch de domaine, jamais de PDDL, jamais de JSON d'action, jamais
de texte libre a afficher tel quel. Sa sortie est une contrainte de la grammaire fermee, ou un refus.
Cette restriction est deliberee : elle reduit d'un ordre de grandeur ce qu'il y a a valider cote jeu,
et elle est verifiee ici par le type de retour plutot que par une relecture.
"""

from __future__ import annotations

from typing import Literal

from pydantic import BaseModel, Field

ROOT_TYPE = "object"
"""Le type racine de PDDL, celui des symboles qu'aucune action ne type mieux.

Miroir de `PddlDomainBuilder.RootType`. La hierarchie du domaine est plate — tous les types derivent
directement de lui — donc une position typee `object` accepte tout et une position typee autrement
n'accepte que son type exact.
"""


class DigestPredicate(BaseModel):
    """Un predicat declare : son nom et le type de ses arguments."""

    name: str
    params: list[str] = Field(default_factory=list)

    @property
    def arity(self) -> int:
        return len(self.params)


class DigestObject(BaseModel):
    """Un symbole nommable : son nom et son type.

    Le type n'etait pas transmis, et c'etait un trou reel. Le service voyait `agent` et `toolbox`
    dans la meme liste plate, sans rien pour distinguer celui qu'on equipe de celui qui equipe :
    `equipped(agent)` etait indiscernable de `equipped(toolbox)` et `self_check` n'avait pas de quoi
    le rattraper. Un modele a fini par produire le premier, et le jeu l'acceptait aussi.
    """

    name: str
    type: str = ROOT_TYPE


class DomainDigest(BaseModel):
    """La projection du domaine PDDL, telle que le jeu la derive de son `PddlDomain`.

    Elle n'est jamais redigee ici. Le service se contente de la lire : c'est ce qui garantit que le
    vocabulaire montre au modele est exactement celui que le portillon 1 acceptera ensuite. Un digest
    compose cote service aurait pris du retard sur le domaine des la premiere action ajoutee au jeu,
    et le symptome aurait ete un refus incomprehensible pour le joueur.
    """

    domain_hash: str = ""
    types: list[str] = Field(default_factory=list)
    predicates: list[DigestPredicate] = Field(default_factory=list)
    actions: list[str] = Field(default_factory=list)
    location_kinds: list[str] = Field(default_factory=list)
    objects: list[DigestObject] = Field(default_factory=list)
    active_constraints: list[str] = Field(default_factory=list)
    glossary: dict[str, str] = Field(default_factory=dict)
    """Ce que chaque symbole veut dire, dans les mots d'un joueur — section `glossary` de
    `config/planner_domain.json`. Le nom et le type disent ou un symbole se met, pas ce qu'il designe :
    sans ce champ, « soignez les blesses avant d'entrer » etait refusee faute de rien qui la relie a
    `band_tended`. Absent, le digest se lit comme avant."""

    def meaning(self, name: str) -> str | None:
        """La definition d'un symbole, ou `None` s'il n'en a pas."""
        return self.glossary.get(name)

    def predicate(self, name: str) -> DigestPredicate | None:
        lowered = name.strip().lower()
        return next((p for p in self.predicates if p.name.lower() == lowered), None)

    def knows_action(self, name: str) -> bool:
        lowered = name.strip().lower()
        return any(action.lower() == lowered for action in self.actions)

    def knows_kind(self, name: str) -> bool:
        lowered = name.strip().lower()
        return any(kind.lower() == lowered for kind in self.location_kinds)

    def knows_object(self, name: str) -> bool:
        return self.type_of(name) is not None

    def type_of(self, name: str) -> str | None:
        """Le type declare d'un symbole, ou `None` s'il n'appartient pas au domaine."""
        lowered = name.strip().lower()
        return next((obj.type for obj in self.objects if obj.name.lower() == lowered), None)

    @staticmethod
    def type_fits(actual: str, expected: str) -> bool:
        """Miroir de `PddlDomain.TypeFits` : la hierarchie est plate, donc deux cas et pas trois."""
        return expected.lower() == ROOT_TYPE or actual.lower() == expected.lower()


class InstructionRequest(BaseModel):
    """Ce que le jeu envoie : une phrase, une graine, et de quoi la traduire."""

    utterance: str
    seed: int = 0
    domain_digest: DomainDigest = Field(default_factory=DomainDigest)
    max_chars: int = 200


class ServiceConstraint(BaseModel):
    """La contrainte rendue, dans la grammaire fermee.

    `factor` ne figurait pas au contrat initial de la specification et son absence rendait la
    cinquieme forme intraduisible : « evitez de forcer les portes » n'a pas de sens sans un `k`. Le
    portillon 1 le borne cote jeu, donc le laisser libre ici ne libere rien.
    """

    form: Literal["require_before", "require_after", "forbid", "require_always", "penalize"]
    predicate: str | None = None
    action_class: str | None = None
    factor: float | None = None


class ServiceTokens(BaseModel):
    prompt: int = 0
    completion: int = 0


class ConstraintReply(BaseModel):
    """Traduction reussie. Ne veut pas dire « appliquee » : les quatre portillons tournent ensuite."""

    status: Literal["constraint"] = "constraint"
    constraint: ServiceConstraint
    rationale: str = ""
    model: str = ""
    latency_ms: int = 0
    tokens: ServiceTokens = Field(default_factory=ServiceTokens)
    cost_usd: float = 0.0


class RejectedReply(BaseModel):
    """Refus du service lui-meme (noeud `project`).

    `reason` est une **famille**, pas une phrase : c'est elle que le corpus d'evaluation compare, et
    une famille libre rendrait le taux de refus correct incalculable.
    """

    status: Literal["rejected"] = "rejected"
    reason: str
    message: str
    model: str = ""
    latency_ms: int = 0
    tokens: ServiceTokens = Field(default_factory=ServiceTokens)
    cost_usd: float = 0.0


Reply = ConstraintReply | RejectedReply


class RefusalFamily:
    """Les familles de refus. Fermees, pour la meme raison que la grammaire l'est.

    Un modele qui invente sa propre famille de refus rend le tableau d'evaluation illisible : on ne
    peut plus dire « refusee pour la bonne raison », seulement « refusee ». Le noeud `project` ramene
    donc toute famille inconnue a `out_of_scope`.
    """

    PREFERENCE = "preference_not_constraint"
    OUT_OF_SCOPE = "out_of_scope"
    UNGRAMMATICAL = "not_expressible"
    UNKNOWN_SYMBOL = "unknown_symbol"
    CONDITIONAL = "conditional_not_supported"
    EMPTY = "empty_utterance"
    PROVIDER = "provider_unavailable"
    BUDGET = "budget_exhausted"

    ALL = frozenset(
        {
            PREFERENCE,
            OUT_OF_SCOPE,
            UNGRAMMATICAL,
            UNKNOWN_SYMBOL,
            CONDITIONAL,
            EMPTY,
            PROVIDER,
            BUDGET,
        }
    )
