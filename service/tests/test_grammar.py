"""La grammaire fermee, cote service.

Ces tests eprouvent le rattrapage precoce, pas la protection : c'est le portillon 1 du jeu qui fait
autorite, et il revalide tout. Ce qui est verifie ici est qu'une erreur du modele est reconnue
**assez tot pour etre renvoyee au modele**, avec sa raison.
"""

from __future__ import annotations

import pytest

from app.contracts import ServiceConstraint
from app.grammar import check, parse_atom, to_text


def test_un_fait_se_lit_en_predicat_et_arguments():
    assert parse_atom("at(agent, group)") == ("at", ["agent", "group"])
    assert parse_atom("explored") == ("explored", [])
    assert parse_atom("  hidden  ") == ("hidden", [])


def test_un_fait_mal_ferme_n_est_pas_un_fait():
    assert parse_atom("at(agent, group") is None
    assert parse_atom("at(agent, )") is None
    assert parse_atom("") is None


def test_l_exemple_du_livrable_est_recevable(digest):
    constraint = ServiceConstraint(
        form="require_before", predicate="at(agent, group)", action_class="enter:building"
    )

    assert check(constraint, digest) is None
    assert to_text(constraint) == "require_before(at(agent, group), enter:building)"


def test_un_predicat_inexistant_est_refuse(digest):
    """Le cas d'ecole de la specification : `barricaded` n'existe pas, et il sonne juste."""
    constraint = ServiceConstraint(
        form="require_before", predicate="barricaded(door_1)", action_class="enter:building"
    )

    reason = check(constraint, digest)

    assert reason is not None
    assert "barricaded" in reason


def test_une_arite_fausse_est_refusee(digest):
    constraint = ServiceConstraint(form="require_always", predicate="at(agent)")

    reason = check(constraint, digest)

    assert reason is not None
    assert "argument" in reason


def test_une_variable_n_est_pas_un_objet(digest):
    """Une contrainte nomme des objets : personne ne saurait par quoi lier une variable."""
    constraint = ServiceConstraint(form="require_always", predicate="at(agent, ?loc)")

    assert check(constraint, digest) is not None


def test_un_objet_invente_est_refuse(digest):
    constraint = ServiceConstraint(form="require_always", predicate="at(agent, cave)")

    reason = check(constraint, digest)

    assert reason is not None
    assert "cave" in reason


def test_une_action_inexistante_est_refusee(digest):
    assert check(ServiceConstraint(form="forbid", action_class="DigTunnel"), digest) is not None


def test_une_classe_de_schemas_se_lit(digest):
    assert check(ServiceConstraint(form="forbid", action_class="move:street"), digest) is None
    assert check(ServiceConstraint(form="forbid", action_class="fly:street"), digest) is not None
    assert check(ServiceConstraint(form="forbid", action_class="enter:cave"), digest) is not None


@pytest.mark.parametrize("factor", [0.0, -1.0, None])
def test_penalize_exige_un_multiplicateur_positif(digest, factor):
    constraint = ServiceConstraint(form="penalize", action_class="ForceDoor", factor=factor)

    assert check(constraint, digest) is not None


def test_les_cinq_formes_se_rendent_en_texte(digest):
    rendered = {
        to_text(ServiceConstraint(form="require_before", predicate="at(agent, group)", action_class="enter:building")),
        to_text(ServiceConstraint(form="require_after", predicate="searched(objective_zone)", action_class="MoveToLocation")),
        to_text(ServiceConstraint(form="forbid", action_class="BreakWindow")),
        to_text(ServiceConstraint(form="require_always", predicate="explored")),
        to_text(ServiceConstraint(form="penalize", action_class="ForceDoor", factor=3.0)),
    }

    assert rendered == {
        "require_before(at(agent, group), enter:building)",
        "require_after(searched(objective_zone), MoveToLocation)",
        "forbid(BreakWindow)",
        "require_always(explored)",
        "penalize(ForceDoor, 3)",
    }


def test_un_argument_du_mauvais_type_est_refuse(digest):
    """Le cas vient d'un vrai passage d'evaluation, pas d'une relecture.

    `nvidia/nemotron-3-super-120b-a12b` a produit `require_before(equipped(agent),
    AttackWithWeapon)`, et les deux portillons l'acceptaient : `agent` est declare, `equipped` prend
    bien un argument. Il est de type `object` la ou `equipped` attend un `item`.
    """
    constraint = ServiceConstraint(
        form="require_before", predicate="equipped(agent)", action_class="AttackWithWeapon"
    )

    reason = check(constraint, digest)

    assert reason is not None
    assert "item" in reason

    # La raison sert de reprise au modele : elle doit nommer les objets qui, eux, conviennent.
    assert "toolbox" in reason


def test_le_bon_type_reste_accepte(digest):
    """Le controle ne ferme pas la forme : il ne retire que ce qui etait mal type."""
    constraint = ServiceConstraint(
        form="require_before", predicate="equipped(toolbox)", action_class="BarricadeDoor"
    )

    assert check(constraint, digest) is None


def test_un_fait_sans_argument_se_rend_sans_parentheses():
    """Miroir de `PddlAtom.AsFact` : c'est le jeu qui fixe l'ecriture, pas le modele.

    Sans cela le service rendait `knowledge_shared()` la ou le jeu ecrit `knowledge_shared`, et le
    harnais comptait une traduction juste comme une erreur — un defaut de mesure qui accusait le
    modele.
    """
    assert to_text(ServiceConstraint(form="require_always", predicate="knowledge_shared()")) == (
        "require_always(knowledge_shared)"
    )
    assert to_text(
        ServiceConstraint(form="require_before", predicate="hidden()", action_class="move:alley")
    ) == "require_before(hidden, move:alley)"

    # Les espaces entre arguments aussi : le jeu ecrit « at(agent, group) ».
    assert to_text(ServiceConstraint(form="require_always", predicate="at(agent,group)")) == (
        "require_always(at(agent, group))"
    )
