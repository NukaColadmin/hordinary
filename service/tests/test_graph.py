"""Le graphe a cinq noeuds, sans reseau et sans cle d'API.

Le fournisseur est un `ScriptedProvider` : chaque test lui donne la reponse qu'il veut eprouver. Ce
n'est pas une commodite — le noeud `self_check` a une reprise, et l'eprouver demande un fournisseur
qui se trompe d'abord et se corrige ensuite, ce qu'aucun vrai modele ne fait sur commande.

Ce que ces tests ne verifient **pas** : que le modele traduit bien. Cela ne se teste pas, cela se
mesure — c'est `evals/` et le tableau des modeles.
"""

from __future__ import annotations

import json

import pytest

from app.contracts import ConstraintReply, RefusalFamily, RejectedReply
from app.graph import build, run
from app.provider import Budget, ScriptedProvider


def constraint_json(form: str, predicate: str | None = None, action: str | None = None, factor=None) -> str:
    return json.dumps(
        {
            "status": "constraint",
            "constraint": {
                "form": form,
                "predicate": predicate,
                "action_class": action,
                "factor": factor,
            },
            "rationale": "test",
        }
    )


def rejected_json(reason: str, message: str = "non") -> str:
    return json.dumps({"status": "rejected", "reason": reason, "message": message})


def graph_of(*replies: str, ceiling: float = 1.0, cost_each: float = 0.0):
    provider = ScriptedProvider(replies=list(replies), cost_each=cost_each)
    budget = Budget(ceiling=ceiling)

    return build(provider, budget), provider, budget


# --- La sortie ------------------------------------------------------------------------------------


def test_le_noeud_project_ne_rend_qu_une_contrainte_ou_un_refus(request_of):
    """La garantie du livrable, verifiee par le type plutot que par une relecture.

    Il n'y a qu'une sortie au graphe, donc qu'un seul endroit ou quelqu'un pourrait un jour laisser
    passer du texte libre. Les quatre cas ici sont les quatre facons d'y arriver.
    """
    cases = [
        constraint_json("forbid", action="BreakWindow"),
        rejected_json(RefusalFamily.PREFERENCE),
        "ceci n'est pas du JSON",
        json.dumps({"status": "constraint", "constraint": {"form": "forbid", "action_class": "DigTunnel"}}),
    ]

    for reply in cases:
        compiled, _, _ = graph_of(reply, reply)
        result = run(compiled, request_of("peu importe"))

        assert isinstance(result, (ConstraintReply, RejectedReply))
        assert result.status in ("constraint", "rejected")


def test_l_exemple_du_livrable_devient_une_contrainte(request_of):
    compiled, _, _ = graph_of(
        constraint_json("require_before", "at(agent, group)", "enter:building")
    )

    result = run(compiled, request_of("Avant d'entrer dans un batiment, regroupez-vous."))

    assert isinstance(result, ConstraintReply)
    assert result.constraint.form == "require_before"
    assert result.constraint.predicate == "at(agent, group)"
    assert result.constraint.action_class == "enter:building"


# --- Les refus ------------------------------------------------------------------------------------


def test_une_preference_est_refusee_avec_sa_famille(request_of):
    """« Priorite aux blesses » exprime une preference d'objectif, pas une regle de plan.

    La famille compte autant que le refus : c'est elle que le corpus d'evaluation compare, et une
    famille libre rendrait le taux de refus correct incalculable.
    """
    compiled, _, _ = graph_of(
        rejected_json(
            RefusalFamily.PREFERENCE,
            "« priorite aux blesses » exprime une preference d'objectif, pas une regle de plan.",
        )
    )

    result = run(compiled, request_of("Priorite aux blesses."))

    assert isinstance(result, RejectedReply)
    assert result.reason == RefusalFamily.PREFERENCE


def test_une_famille_inventee_est_ramenee_a_la_famille_generale(request_of):
    compiled, _, _ = graph_of(rejected_json("je_ne_sais_pas", "..."))

    result = run(compiled, request_of("quelque chose"))

    assert isinstance(result, RejectedReply)
    assert result.reason == RefusalFamily.OUT_OF_SCOPE


def test_une_phrase_vide_ne_vaut_pas_un_appel(request_of):
    compiled, provider, budget = graph_of(constraint_json("forbid", action="BreakWindow"))

    result = run(compiled, request_of("   "))

    assert isinstance(result, RejectedReply)
    assert result.reason == RefusalFamily.EMPTY
    assert provider.seen == []
    assert budget.calls == 0


# --- La reprise -----------------------------------------------------------------------------------


def test_une_contrainte_hors_vocabulaire_est_reprise_une_fois(request_of):
    """Le premier essai invente un predicat, le second se corrige. Une reprise, et une seule."""
    compiled, provider, _ = graph_of(
        constraint_json("require_before", "barricaded(door_1)", "enter:building"),
        constraint_json("require_before", "at(agent, group)", "enter:building"),
    )

    result = run(compiled, request_of("Regroupez-vous avant d'entrer."), retries=1)

    assert isinstance(result, ConstraintReply)
    assert len(provider.seen) == 2


def test_la_reprise_porte_la_raison_du_refus(request_of):
    """Sans la raison, le second appel est le premier avec une autre graine."""
    compiled, provider, _ = graph_of(
        constraint_json("require_before", "barricaded(door_1)", "enter:building"),
        constraint_json("require_before", "at(agent, group)", "enter:building"),
    )

    run(compiled, request_of("Regroupez-vous avant d'entrer."), retries=1)

    _, second = provider.seen[1]

    assert "barricaded" in second
    assert "REJETEE" in second


def test_deux_echecs_de_suite_deviennent_un_refus_motive(request_of):
    compiled, provider, _ = graph_of(
        constraint_json("require_always", "barricaded(door_1)"),
        constraint_json("require_always", "barricaded(door_1)"),
        constraint_json("require_always", "explored"),
    )

    result = run(compiled, request_of("barricadez tout"), retries=1)

    assert isinstance(result, RejectedReply)
    assert result.reason == RefusalFamily.UNGRAMMATICAL
    assert "barricaded" in result.message
    assert len(provider.seen) == 2, "la reprise doit etre unique"


def test_un_json_enrobe_de_prose_est_quand_meme_lu(request_of):
    """Le rattrapage de format n'est pas une faveur : il separe la comprehension de la discipline."""
    wrapped = "Voici la contrainte :\n```json\n" + constraint_json("forbid", action="BreakWindow") + "\n```"

    compiled, _, _ = graph_of(wrapped)
    result = run(compiled, request_of("Ne cassez aucune vitre."))

    assert isinstance(result, ConstraintReply)


# --- Le contexte ----------------------------------------------------------------------------------


def test_le_contexte_ne_montre_que_le_vocabulaire_du_digest(request_of, digest):
    compiled, provider, _ = graph_of(constraint_json("forbid", action="BreakWindow"))

    run(compiled, request_of("Ne cassez aucune vitre."))

    _, prompt = provider.seen[0]

    for name in ("at", "known", "building"):
        assert name in prompt

    for obj in ("group", "agent"):
        assert obj in prompt

    # Temoin negatif : `distraction_created` a ete retire du domaine par l'epuration (faire du bruit
    # expres est un sacrifice calcule, pas un reflexe). Le choix du symbole n'est pas libre — il doit
    # etre absent du digest, sinon l'assertion passe pour une bonne raison qui n'est pas la sienne.
    # C'est exactement l'erreur que ce test portait : il visait `barricaded`, qui est au vocabulaire.
    absent = "distraction_created"
    assert absent not in {p.name for p in digest.predicates}, "temoin negatif invalide"
    assert absent not in prompt


def test_le_contexte_donne_le_sens_de_chaque_symbole(request_of, digest):
    compiled, provider, _ = graph_of(constraint_json("forbid", action="BreakWindow"))

    run(compiled, request_of("Ne cassez aucune vitre."))

    _, prompt = provider.seen[0]

    # Un fait, une action, une categorie, un repere : les quatre listes portent le sens a cote du nom,
    # et ce sens est celui du digest — le noeud n'en ecrit aucun.
    assert f"band_tended() — {digest.meaning('band_tended')}" in prompt
    assert f"TendAlly — {digest.meaning('TendAlly')}" in prompt
    assert f"building — {digest.meaning('building')}" in prompt
    assert f"patient : location — {digest.meaning('patient')}" in prompt


def test_sans_glossaire_le_contexte_reste_celui_des_passages_publies(digest):
    from app.contracts import InstructionRequest

    bare = digest.model_copy(update={"glossary": {}})
    compiled, provider, _ = graph_of(constraint_json("forbid", action="BreakWindow"))

    run(compiled, InstructionRequest(utterance="Ne cassez aucune vitre.", domain_digest=bare))

    _, prompt = provider.seen[0]

    # L'ablation --no-glossary doit rendre le contexte des passages anterieurs, a la lettre : sinon
    # elle mesurerait la mise en page en meme temps que les definitions.
    assert "Actions : " + ", ".join(digest.actions) in prompt
    assert not [line for line in prompt.splitlines() if line.startswith("  ") and " — " in line]


def test_les_contraintes_actives_sont_montrees(request_of, digest):
    active = digest.model_copy(update={"active_constraints": ["forbid(BreakWindow)"]})

    from app.contracts import InstructionRequest

    compiled, provider, _ = graph_of(constraint_json("forbid", action="ForceDoor"))

    run(compiled, InstructionRequest(utterance="ne forcez pas les portes", domain_digest=active))

    _, prompt = provider.seen[0]

    assert "forbid(BreakWindow)" in prompt


def test_la_phrase_est_coupee_a_max_chars(request_of):
    compiled, provider, _ = graph_of(constraint_json("forbid", action="BreakWindow"))

    run(compiled, request_of("a" * 4000, max_chars=200))

    _, prompt = provider.seen[0]

    assert "a" * 201 not in prompt


# --- Le plafond -----------------------------------------------------------------------------------


def test_le_plafond_de_cout_coupe_les_appels(request_of):
    """Atteint, le service refuse — et c'est le jeu qui bascule sur les instructions pre-ecrites.

    Les deux plafonds, celui du service et celui du client, protegent de deux pannes differentes : un
    service oublie allume, et un jeu qui boucle.
    """
    compiled, provider, budget = graph_of(
        constraint_json("forbid", action="BreakWindow"),
        constraint_json("forbid", action="ForceDoor"),
        ceiling=0.002,
        cost_each=0.0015,
    )

    first = run(compiled, request_of("Ne cassez aucune vitre."))
    second = run(compiled, request_of("Ne forcez aucune porte."))
    third = run(compiled, request_of("Restez a couvert."))

    assert isinstance(first, ConstraintReply)
    assert isinstance(second, ConstraintReply)

    assert isinstance(third, RejectedReply)
    assert third.reason == RefusalFamily.BUDGET
    assert len(provider.seen) == 2


def test_un_fournisseur_injoignable_devient_un_refus(request_of):
    class Broken:
        def complete(self, system: str, user: str):
            raise RuntimeError("connexion refusee")

    compiled = build(Broken(), Budget(ceiling=1.0))
    result = run(compiled, request_of("Ne cassez aucune vitre."))

    assert isinstance(result, RejectedReply)
    assert result.reason == RefusalFamily.PROVIDER


# --- Le compte ------------------------------------------------------------------------------------


def test_le_cout_et_les_jetons_sont_rapportes(request_of):
    compiled, _, _ = graph_of(constraint_json("forbid", action="BreakWindow"), cost_each=0.0012)

    result = run(compiled, request_of("Ne cassez aucune vitre."))

    assert result.cost_usd == pytest.approx(0.0012)
    assert result.tokens.prompt > 0
    assert result.model == "test/modele"
