"""La grammaire fermee, cote service (noeud `self_check`).

Ce fichier verifie une contrainte contre le digest du domaine. Il ne remplace pas le portillon 1 : le
jeu revalide tout, avec le domaine complet, et c'est lui qui fait autorite. Ce qui est fait ici sert
a une autre chose — **rattraper l'erreur assez tot pour la corriger**. Une contrainte qui cite un
predicat inexistant peut etre renvoyee au modele avec la raison, une fois ; arrivee au jeu, elle est
un refus seche affiche au joueur.

Les deux verifications sont donc redondantes par construction, et elles doivent l'etre. Celle du jeu
est la seule qui protege ; celle-ci est la seule qui ameliore le taux de compilation.

Corollaire qu'il a fallu apprendre : **si l'une des deux est plus permissive que l'autre, c'est la
redondance qui est perdue**, pas seulement une occasion de reprise. Le controle de type des arguments
manquait ici et la-bas — `equipped(agent)` passait les deux, parce que `agent` existe et que
`equipped` prend bien un argument. Le digest ne transmettait d'ailleurs pas de quoi le voir : les
symboles y arrivaient sans leur type.
"""

from __future__ import annotations

import re

from .contracts import DomainDigest, ServiceConstraint

FAMILIES = ("move", "enter")
"""Les deux classes de schemas nommables.

Elles designent le **meme** ensemble de schemas, et c'est voulu : PDDL ne connait pas la notion
d'« entrer ». Ce qui distingue « entrer dans un batiment » de « traverser une rue » n'est pas le
verbe mais la categorie du lieu vise. Les deux mots existent parce qu'un joueur ecrit l'un ou
l'autre, pas parce qu'ils selectionnent autre chose.
"""

NEEDS_PREDICATE = frozenset({"require_before", "require_after", "require_always"})
NEEDS_ACTION = frozenset({"require_before", "require_after", "forbid", "penalize"})

_ATOM = re.compile(r"^\s*([A-Za-z_][A-Za-z0-9_-]*)\s*(?:\(\s*(.*?)\s*\))?\s*$", re.DOTALL)


def parse_atom(fact: str) -> tuple[str, list[str]] | None:
    """`at(agent, group)` devient `("at", ["agent", "group"])`. `None` si ce n'est pas un fait."""
    match = _ATOM.match(fact or "")
    if match is None:
        return None

    name = match.group(1)
    body = (match.group(2) or "").strip()
    arguments = [argument.strip() for argument in body.split(",")] if body else []

    if any(argument == "" for argument in arguments):
        return None

    return name, arguments


def check_predicate(fact: str, digest: DomainDigest) -> str | None:
    """La raison du refus d'un fait, ou `None` s'il est recevable."""
    parsed = parse_atom(fact)
    if parsed is None:
        return f"« {fact} » n'est pas un fait de la forme predicat(arg, arg)."

    name, arguments = parsed
    declared = digest.predicate(name)

    if declared is None:
        return f"Le predicat « {name} » n'existe pas dans le domaine."

    if declared.arity != len(arguments):
        return f"Le predicat « {name} » prend {declared.arity} argument(s), pas {len(arguments)}."

    for position, argument in enumerate(arguments):
        if argument.startswith("?"):
            return (
                f"« {fact} » contient la variable « {argument} » : une contrainte nomme des objets, "
                "jamais des variables."
            )

        actual = digest.type_of(argument)

        if actual is None:
            return f"L'objet « {argument} » n'existe pas dans le domaine."

        expected = declared.params[position]

        if not DomainDigest.type_fits(actual, expected):
            candidates = sorted(
                obj.name for obj in digest.objects if DomainDigest.type_fits(obj.type, expected)
            )
            return (
                f"« {fact} » donne « {argument} », qui est de type {actual}, a une position que "
                f"« {name} » type {expected}. Objets de type {expected} : "
                + (", ".join(candidates) or "aucun")
                + "."
            )

    return None


def check_action(target: str, digest: DomainDigest) -> str | None:
    """La raison du refus d'une cible d'action, ou `None` si elle est recevable."""
    text = (target or "").strip()

    if ":" not in text:
        if digest.knows_action(text):
            return None
        return f"L'action « {text} » n'existe pas dans le domaine."

    family, _, category = text.partition(":")
    family = family.strip().lower()
    category = category.strip().lower()

    if family not in FAMILIES:
        return f"« {family} » n'est pas une classe de schemas. Attendu : {' ou '.join(FAMILIES)}."

    if not digest.knows_kind(category):
        return f"« {category} » n'est pas une categorie de lieu du domaine."

    return None


def check(constraint: ServiceConstraint, digest: DomainDigest) -> str | None:
    """La raison du refus d'une contrainte entiere, ou `None` si elle est recevable."""
    form = constraint.form

    if form in NEEDS_PREDICATE:
        if not constraint.predicate:
            return f"La forme « {form} » exige un fait."
        error = check_predicate(constraint.predicate, digest)
        if error is not None:
            return error

    if form in NEEDS_ACTION:
        if not constraint.action_class:
            return f"La forme « {form} » exige une action ou une classe de schemas."
        error = check_action(constraint.action_class, digest)
        if error is not None:
            return error

    if form == "penalize":
        factor = constraint.factor
        if factor is None or factor <= 0:
            return "La forme « penalize » exige un multiplicateur de cout strictement positif."

    return None


def canonical_fact(fact: str | None) -> str:
    """Le fait tel que le jeu l'ecrit : miroir de `PddlAtom.AsFact`.

    Un fait sans argument s'ecrit `knowledge_shared`, **jamais** `knowledge_shared()` — c'est ce que
    rend `PddlAtom.AsFact`, donc ce que le jeu figera dans la sauvegarde de mission. Le service
    recopiait au contraire la chaine du modele telle quelle, et les deux langages ne rendaient plus la
    meme contrainte pour la meme contrainte. Le harnais d'evaluation comptait alors une traduction
    juste comme une erreur, ce qui est la pire sorte de defaut de mesure : il accusait le modele.

    Les espaces entre arguments sont normalises pour la meme raison — `at(agent,group)` et
    `at(agent, group)` nomment la meme chose et le jeu ecrit la seconde.
    """
    text = (fact or "").strip()
    parsed = parse_atom(text)

    if parsed is None:
        return text

    name, arguments = parsed

    return f"{name}({', '.join(arguments)})" if arguments else name


def to_text(constraint: ServiceConstraint) -> str:
    """La contrainte dans la grammaire fermee, telle que le jeu la relira.

    Le rendu est strictement le meme que `TacticalConstraint.Text` cote C#. Il est duplique plutot
    que partage parce qu'il n'y a rien a partager entre deux langages sans un troisieme format ; ce
    que les deux ont en commun est teste des deux cotes, sur les memes cas — et c'est bien ce qui a
    fini par diverger, voir `canonical_fact`.
    """
    form = constraint.form
    fact = canonical_fact(constraint.predicate)

    if form == "require_before":
        return f"require_before({fact}, {constraint.action_class})"
    if form == "require_after":
        return f"require_after({fact}, {constraint.action_class})"
    if form == "forbid":
        return f"forbid({constraint.action_class})"
    if form == "require_always":
        return f"require_always({fact})"
    if form == "penalize":
        factor = constraint.factor if constraint.factor is not None else 1.0
        return f"penalize({constraint.action_class}, {factor:g})"

    return ""
