"""Le graphe LangGraph a cinq noeuds (livrable 15).

    normalize -> context -> generate -> self_check -> project

Chaque noeud fait une chose, et le decoupage n'est pas decoratif : `self_check` doit pouvoir renvoyer
a `generate` avec une raison, ce qui demande un cycle, et `project` doit etre le **seul** endroit qui
fabrique une reponse — sans quoi il y aurait plusieurs sorties a verifier au lieu d'une.

La regle non negociable du livrable tient dans ce fichier : **le modele ne decide de rien, il
traduit.** Il produit une contrainte dans une grammaire fermee ; tout ce qui a une consequence
mecanique — la compilation, les portillons, le refus motive — est deterministe et vit dans
`Hordinary.Core`.
"""

from __future__ import annotations

import time
from typing import Annotated, Any, TypedDict

from .contracts import (
    ConstraintReply,
    DomainDigest,
    InstructionRequest,
    RefusalFamily,
    RejectedReply,
    Reply,
    ServiceConstraint,
    ServiceTokens,
)
from .grammar import FAMILIES, check
from .provider import Budget, Completion, Provider, parse_json
from .settings import Settings

SYSTEM = """Tu traduis une consigne de joueur en UNE contrainte formelle, et rien d'autre.

Tu ne decides jamais. Tu ne donnes jamais de conseil tactique. Tu ne produis jamais de PDDL, de code,
ni de texte a afficher au joueur : uniquement l'objet JSON demande.

Les cinq formes, et aucune autre :

- require_before(P, A) : l'action A n'est jouable que si le fait P est vrai juste avant.
- require_after(P, A)  : l'action A doit etre suivie de ce qui rend P vrai.
- forbid(A)            : l'action A est retiree du domaine.
- require_always(P)    : le fait P doit etre vrai a chaque etat du plan.
- penalize(A, k)       : le cout de l'action A est multiplie par k (k > 0).

A est soit un schema d'action declare, soit une classe ecrite « move:<categorie> » ou
« enter:<categorie> », ou la categorie est une categorie de lieu declaree.

P est un fait « predicat(objet, objet) » dont le predicat et chaque objet sont declares, et dont
chaque objet a le type que le predicat attend a cette position. Un predicat sans argument s'ecrit
sans parentheses : « knowledge_shared », pas « knowledge_shared() ».

Tu REFUSES quand la phrase :
- exprime une preference d'objectif et non une regle de plan -> preference_not_constraint
- est conditionnelle ou chainee (« si …, alors … ») -> conditional_not_supported
- ne parle pas du jeu -> out_of_scope
- ne peut pas s'ecrire dans les cinq formes -> not_expressible
- nomme quelque chose qui n'est pas dans le vocabulaire fourni -> unknown_symbol

Refuser est un resultat correct, pas un echec. Une contrainte plausible mais fausse est la pire des
sorties : dans le doute, refuse.

Reponds par un objet JSON, l'une des deux formes exactement :

{"status":"constraint","constraint":{"form":"...","predicate":"...","action_class":"...","factor":null},"rationale":"..."}
{"status":"rejected","reason":"<famille>","message":"<une phrase, en francais, pour le joueur>"}
"""


class State(TypedDict, total=False):
    """Ce qui circule entre les noeuds. Tout y est explicite : un graphe se relit."""

    request: InstructionRequest
    phrase: str
    language: str
    digest: DomainDigest
    prompt: str
    raw: dict[str, Any] | None
    constraint: ServiceConstraint | None
    rationale: str
    refusal: tuple[str, str] | None
    complaint: str | None
    attempts: int
    retries: int
    usage: Annotated[list[Completion], lambda a, b: (a or []) + (b or [])]
    reply: Reply | None


# --- 1. normalize --------------------------------------------------------------------------------


def normalize(state: State) -> State:
    """Nettoie la phrase, detecte la langue, coupe au-dela de `max_chars`.

    La troncature est refaite ici alors que le jeu la fait deja. Les deux sont voulues : celle du jeu
    protege sa facture, celle-ci protege le service d'un appelant qui ne serait pas le jeu.

    La detection de langue est une heuristique de dix mots et rien de plus. Elle ne sert pas a
    router : elle sert a dire « ce n'est pas du francais » dans la reponse, ce qui explique au joueur
    un refus qui serait autrement mysterieux. Une dependance de detection pour ce seul signal
    couterait plus qu'elle ne rapporte.
    """
    request = state["request"]
    phrase = " ".join((request.utterance or "").split())

    if request.max_chars > 0:
        phrase = phrase[: request.max_chars]

    return {
        "phrase": phrase,
        "language": _language(phrase),
        "digest": request.domain_digest,
        "attempts": 0,
        "usage": [],
    }


_FRENCH = frozenset(
    {"le", "la", "les", "un", "une", "des", "avant", "apres", "ne", "pas", "vous", "dans", "de"}
)


def _language(phrase: str) -> str:
    words = {word.strip(".,;:!?'\"").lower() for word in phrase.split()}
    return "fr" if words & _FRENCH else "und"


# --- 2. context ----------------------------------------------------------------------------------


def context(state: State) -> State:
    """Compose le contexte donne au modele, a partir du digest et de lui seul.

    Le digest est **derive du `PddlDomain`** par le jeu, jamais redige ici. Le service et le
    portillon 1 lisent donc la meme source de verite sur ce qui est nommable, et un symbole que le
    modele a vu dans son contexte ne peut pas etre un symbole que la grammaire refuse ensuite.

    Ce bloc pese quelques kilo-octets et tient entierement dans la fenetre de contexte. Il n'y a donc
    pas de recuperation documentaire ici, et il ne faut pas en ajouter : mettre une base vectorielle
    devant huit kilo-octets de texte resout un probleme qui n'existe pas.
    """
    digest = state["digest"]

    predicates = "\n".join(
        f"  {p.name}({', '.join(p.params)})" if p.params else f"  {p.name}()"
        for p in digest.predicates
    )

    objects = "\n".join(f"  {obj.name} : {obj.type}" for obj in digest.objects)

    lines = [
        "VOCABULAIRE DU DOMAINE — rien en dehors de ces listes n'existe.",
        "",
        "Predicats :",
        predicates or "  (aucun)",
        "",
        "Actions : " + (", ".join(digest.actions) or "(aucune)"),
        "Categories de lieu : " + (", ".join(digest.location_kinds) or "(aucune)"),
        "",
        # Le type, et pas seulement le nom : une liste plate laissait `equipped(agent)` aussi
        # plausible que `equipped(toolbox)`, et un modele a fini par ecrire le premier. Le portillon
        # le refuse desormais, mais refuser mieux sans expliquer mieux ne fait que deplacer l'echec —
        # la contrainte est valide ou elle ne l'est pas, et le modele n'avait pas de quoi choisir.
        "Objets nommables, avec leur type — un fait doit respecter le type de chaque position :",
        objects or "  (aucun)",
        "",
        "Classes de schemas : " + ", ".join(f"{family}:<categorie>" for family in FAMILIES),
    ]

    if digest.active_constraints:
        lines += [
            "",
            "Contraintes deja actives — ne propose pas leur contraire :",
            *(f"  {text}" for text in digest.active_constraints),
        ]

    return {"prompt": "\n".join(lines)}


# --- 3. generate ---------------------------------------------------------------------------------


def make_generate(provider: Provider, budget: Budget):
    """Le noeud d'appel au modele. Le fournisseur et le plafond sont injectes : le graphe ne les cree pas."""

    def generate(state: State) -> State:
        if budget.exhausted:
            return {
                "refusal": (
                    RefusalFamily.BUDGET,
                    f"Plafond de depense du service atteint ({budget.spent:.4f} / {budget.ceiling:.2f} USD).",
                ),
                "constraint": None,
            }

        user = state["prompt"] + "\n\nCONSIGNE DU JOUEUR :\n" + state["phrase"]

        # La reprise porte la raison du refus precedent. Sans elle, le second appel est le premier
        # avec une autre graine — et le taux de correction observe n'est que du bruit.
        if state.get("complaint"):
            user += (
                "\n\nTA REPONSE PRECEDENTE A ETE REJETEE : "
                + state["complaint"]
                + "\nCorrige en restant dans le vocabulaire ci-dessus, ou refuse."
            )

        try:
            completion = provider.complete(SYSTEM, user)
        except Exception as error:  # noqa: BLE001 — un fournisseur injoignable est un cas normal
            return {
                "refusal": (RefusalFamily.PROVIDER, f"Fournisseur injoignable : {error}"),
                "constraint": None,
            }

        budget.charge(completion.cost_usd)

        return {
            "raw": parse_json(completion.text),
            "usage": [completion],
            "attempts": state.get("attempts", 0) + 1,
        }

    return generate


# --- 4. self_check -------------------------------------------------------------------------------


def self_check(state: State) -> State:
    """Relit la sortie du modele contre le digest. Une reprise, puis abandon.

    On ne fait jamais confiance au drapeau `json_mode` : il reduit le taux d'echec, il ne le supprime
    pas. Ce noeud revalide donc de toute facon — forme, arite, symboles — et c'est lui qui transforme
    une sortie plausible en refus motive plutot qu'en contrainte fausse.
    """
    if state.get("refusal"):
        return {"complaint": None}

    raw = state.get("raw")

    if not isinstance(raw, dict):
        return {"complaint": "La reponse n'etait pas un objet JSON.", "constraint": None}

    status = str(raw.get("status", "")).strip().lower()

    if status == "rejected":
        family = str(raw.get("reason", "")).strip().lower()
        message = str(raw.get("message", "")).strip()

        # Une famille inventee rendrait le taux de refus correct incalculable : on la ramene a la
        # famille generale plutot que de la propager.
        if family not in RefusalFamily.ALL:
            family = RefusalFamily.OUT_OF_SCOPE

        return {
            "refusal": (family, message or "Cette consigne ne s'exprime pas comme une regle de plan."),
            "constraint": None,
            "complaint": None,
        }

    body = raw.get("constraint")

    if not isinstance(body, dict):
        return {"complaint": "Ni contrainte ni refus dans la reponse.", "constraint": None}

    try:
        constraint = ServiceConstraint.model_validate(body)
    except Exception as error:  # noqa: BLE001 — pydantic decrit deja ce qui manque
        return {"complaint": f"Contrainte mal formee : {error}", "constraint": None}

    complaint = check(constraint, state["digest"])

    if complaint is not None:
        return {"complaint": complaint, "constraint": None}

    return {
        "constraint": constraint,
        "rationale": str(raw.get("rationale", "")).strip(),
        "complaint": None,
    }


def after_check(state: State) -> str:
    """Reprendre ou conclure. Une seule reprise : insister davantage ne traduit rien de plus."""
    if state.get("constraint") is not None or state.get("refusal"):
        return "project"

    retries = state.get("retries", 1)

    return "generate" if state.get("attempts", 0) <= retries else "project"


# --- 5. project ----------------------------------------------------------------------------------


def project(state: State) -> State:
    """Rend une contrainte de la grammaire fermee, ou un refus motive. Jamais autre chose.

    C'est la seule sortie du graphe, et c'est ce qui rend la garantie verifiable : il n'y a qu'un
    endroit ou un jour quelqu'un pourrait laisser passer du texte libre, et un test le surveille.
    """
    usage = state.get("usage") or []
    model = next((completion.model for completion in reversed(usage) if completion.model), "")
    tokens = ServiceTokens(
        prompt=sum(completion.prompt_tokens for completion in usage),
        completion=sum(completion.completion_tokens for completion in usage),
    )
    cost = sum(completion.cost_usd for completion in usage)
    latency = sum(completion.latency_ms for completion in usage)

    if state.get("constraint") is not None:
        return {
            "reply": ConstraintReply(
                constraint=state["constraint"],
                rationale=state.get("rationale", ""),
                model=model,
                latency_ms=latency,
                tokens=tokens,
                cost_usd=cost,
            )
        }

    if state.get("refusal"):
        family, message = state["refusal"]
    elif state.get("complaint"):
        # Deux tentatives, deux contraintes hors vocabulaire : ce n'est plus un accident de format,
        # c'est que la phrase ne se dit pas dans cette grammaire. Le joueur a droit a la raison exacte.
        family, message = RefusalFamily.UNGRAMMATICAL, state["complaint"]
    else:
        family, message = RefusalFamily.OUT_OF_SCOPE, "Consigne non traduite."

    return {
        "reply": RejectedReply(
            reason=family,
            message=message,
            model=model,
            latency_ms=latency,
            tokens=tokens,
            cost_usd=cost,
        )
    }


def empty_or_context(state: State) -> str:
    """Une phrase vide ne vaut pas un appel de modele."""
    return "context" if state.get("phrase") else "empty"


def empty(state: State) -> State:
    return {"refusal": (RefusalFamily.EMPTY, "Consigne vide.")}


# --- Le graphe -----------------------------------------------------------------------------------


def build(provider: Provider, budget: Budget, settings: Settings | None = None):
    """Assemble les cinq noeuds. Compile une fois, s'appelle autant de fois qu'on veut."""
    from langgraph.graph import END, StateGraph

    settings = settings or Settings()

    graph = StateGraph(State)

    graph.add_node("normalize", normalize)
    graph.add_node("empty", empty)
    graph.add_node("context", context)
    graph.add_node("generate", make_generate(provider, budget))
    graph.add_node("self_check", self_check)
    graph.add_node("project", project)

    graph.set_entry_point("normalize")
    graph.add_conditional_edges("normalize", empty_or_context, {"context": "context", "empty": "empty"})
    graph.add_edge("empty", "project")
    graph.add_edge("context", "generate")
    graph.add_edge("generate", "self_check")
    graph.add_conditional_edges(
        "self_check", after_check, {"generate": "generate", "project": "project"}
    )
    graph.add_edge("project", END)

    return graph.compile()


def run(compiled, request: InstructionRequest, retries: int = 1) -> Reply:
    """Deroule le graphe et rend la reponse. Le seul point d'entree des tests et de l'API."""
    started = time.perf_counter()

    state = compiled.invoke({"request": request, "retries": retries})
    reply = state.get("reply")

    if reply is None:  # le graphe s'est arrete sans passer par project : cela ne doit pas arriver
        reply = RejectedReply(reason=RefusalFamily.OUT_OF_SCOPE, message="Le graphe n'a rien rendu.")

    if not reply.latency_ms:
        reply.latency_ms = int((time.perf_counter() - started) * 1000)

    return reply
