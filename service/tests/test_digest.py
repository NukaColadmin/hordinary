"""Le digest dit la verite sur le domaine.

Ces tests ne comparent pas le digest a lui-meme : ils le comparent au **texte PDDL** versionne dans
`tools/pddl/domain.pddl`. Les deux sont deux rendus du meme `PddlDomain` cote jeu — l'un pour Fast
Downward, l'autre pour le modele de langage — et leur accord est la seule chose qui garantisse qu'un
symbole montre au modele est un symbole que le planificateur connait.

C'est le pendant, cote service, du test .NET « les deux rendus du meme domaine compile s'accordent ».
Il est ici plutot que la-bas parce qu'il vaut mieux qu'il soit ecrit par le programme qui *consomme*
le digest : un test ecrit des deux cotes par la meme main verifie surtout que la main est constante.
"""

from __future__ import annotations

import re
from pathlib import Path

import pytest

DOMAIN_PATH = Path(__file__).resolve().parents[2] / "tools" / "pddl" / "domain.pddl"


def block(text: str, header: str) -> str:
    """Le contenu d'un bloc `(:header …)`, parentheses appariees.

    Une expression reguliere ne suffit pas : les blocs contiennent des parentheses, et `.*?\\)`
    s'arreterait a la premiere fermante venue.
    """
    start = text.find(f"({header}")

    if start < 0:
        return ""

    depth = 0

    for index in range(start, len(text)):
        if text[index] == "(":
            depth += 1
        elif text[index] == ")":
            depth -= 1
            if depth == 0:
                return text[start + len(header) + 1 : index]

    return ""


#: La marque du depot. Le service tourne aussi en conteneur, ou il n'emporte que `service/` — le
#: texte PDDL du jeu n'y est pas, et ne peut pas y etre sans recopier une source de verite.
REPOSITORY = DOMAIN_PATH.parents[2] / "Hordinary.sln"


@pytest.fixture(scope="module")
def domain_text() -> str:
    # Hors du depot, ces trois tests n'ont pas de sujet, et le dire vaut mieux que d'echouer : le
    # passage qui fait autorite est celui du depot. Dans le depot, en revanche, un domaine absent
    # reste une panne — c'est precisement la derive que ce fichier surveille.
    if not REPOSITORY.exists():
        pytest.skip(f"{DOMAIN_PATH} est hors de portee : execution hors du depot (conteneur).")

    assert DOMAIN_PATH.exists(), (
        f"{DOMAIN_PATH} est absent. Regenerer : "
        "HORDINARY_PDDL_REGEN=1 dotnet test --filter PddlArtifactsTests"
    )

    return DOMAIN_PATH.read_text(encoding="utf-8")


@pytest.fixture(scope="module")
def declared_predicates(domain_text: str) -> dict[str, int]:
    """Les predicats du fichier PDDL, avec leur arite, lus dans le bloc `(:predicates …)`."""
    predicates: dict[str, int] = {}

    for line in block(domain_text, ":predicates").splitlines():
        line = line.strip()

        if not line.startswith("("):
            continue

        inner = line.strip("()").strip()
        if not inner:
            continue

        name, *rest = inner.split()
        predicates[name] = sum(1 for token in rest if token.startswith("?"))

    return predicates


@pytest.fixture(scope="module")
def declared_actions(domain_text: str) -> set[str]:
    return set(re.findall(r"\(:action\s+([A-Za-z_][A-Za-z0-9_]*)", domain_text))


def test_chaque_predicat_du_digest_est_declare_dans_le_pddl(digest, declared_predicates):
    for predicate in digest.predicates:
        assert predicate.name in declared_predicates, (
            f"Le digest annonce « {predicate.name} », absent de tools/pddl/domain.pddl."
        )

        assert declared_predicates[predicate.name] == predicate.arity, (
            f"« {predicate.name} » : arite {predicate.arity} dans le digest, "
            f"{declared_predicates[predicate.name]} dans le PDDL."
        )


def test_le_digest_ne_cache_aucun_predicat_declare(digest, declared_predicates):
    """L'inverse compte autant.

    Un predicat que le PDDL declare et que le digest tait est un symbole que le planificateur
    accepterait et que le modele ne peut pas nommer : la contrainte correspondante devient
    inexprimable sans que personne ne sache pourquoi.
    """
    shown = {predicate.name for predicate in digest.predicates}

    assert set(declared_predicates) == shown


def test_les_actions_du_digest_sont_exactement_celles_du_domaine(digest, declared_actions):
    assert set(digest.actions) == declared_actions


def test_le_digest_porte_une_empreinte(digest):
    assert digest.domain_hash.startswith("sha256:")
    assert len(digest.domain_hash) == len("sha256:") + 64


def test_le_vocabulaire_du_digest_n_est_pas_vide(digest):
    """Un digest vide passerait toutes les assertions ci-dessus sans rien garantir."""
    assert len(digest.predicates) > 10
    assert len(digest.actions) > 10
    assert len(digest.objects) > 1
    assert digest.location_kinds


def test_chaque_objet_du_digest_porte_son_type(digest):
    """Le nom seul ne suffit pas, et c'est ce que le trou de typage a montre.

    Une liste plate laissait `equipped(agent)` aussi plausible que `equipped(toolbox)` pour le modele
    comme pour le noeud `self_check`. Montrer le vocabulaire sans son typage, c'est exiger une
    exactitude qu'on n'a pas donne les moyens d'atteindre.
    """
    types = {obj.name: obj.type for obj in digest.objects}

    assert types["toolbox"] == "item"
    assert types["group"] == "location"

    # `agent` n'est type par aucune action : il reste au type racine, et c'est precisement ce qui
    # rend `equipped(agent)` mal type.
    assert types["agent"] == "object"

    # Aucun symbole ne doit arriver sans type : un type vide ferait passer tout controle.
    assert all(obj.type for obj in digest.objects)


def test_tout_type_annonce_par_un_predicat_est_un_type_declare(digest):
    """Le digest doit etre refermable sur lui-meme : un predicat ne type pas une position dans le vide.

    Sans cela, un controle de type refuserait tout a une position dont le type n'est nomme nulle
    part, et le refus serait imputable au digest et non a la contrainte.
    """
    declared = set(digest.types) | {"object"}
    used = {type_ for predicate in digest.predicates for type_ in predicate.params}

    assert used <= declared

    assert {obj.type for obj in digest.objects} <= declared


def test_chaque_symbole_du_digest_a_sa_definition(digest):
    """Le sens de chaque nom voyage avec lui, et rien d'autre n'est defini.

    Miroir de `InstructionServiceTests.Chaque_symbole_nommable_a_sa_definition`, sur le fichier que ce
    service lit reellement : un digest regenere sans glossaire ramenerait le modele a deviner
    `band_tended` sur son seul nom, et aucun autre test d'ici ne le verrait.
    """
    names = (
        {p.name for p in digest.predicates}
        | set(digest.actions)
        | set(digest.location_kinds)
        | {obj.name for obj in digest.objects}
    )

    assert {name for name in names if not digest.meaning(name)} == set()
    assert set(digest.glossary) <= names
