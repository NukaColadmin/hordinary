"""Le harnais d'evaluation lui-meme.

Il produit les chiffres du livrable, donc ses regles de comparaison sont du code livre et non de
l'outillage : une regle trop stricte accuse un modele d'une erreur qu'il n'a pas faite, une regle
trop laxiste publie un taux qui ne veut rien dire. Les deux se testent ici, sans reseau.
"""

from __future__ import annotations

from evals.run import Case, Outcome, Report


def _outcome(produced: str, expected: str, factor_any: bool = False) -> Outcome:
    case = Case(
        id="essai",
        utterance="peu importe",
        expect="constraint",
        constraint=expected,
        factor_any=factor_any,
    )

    return Outcome(case=case, produced=produced, form=produced.split("(", 1)[0], latency_ms=0, cost_usd=0.0, refused=None)


def test_enter_et_move_designent_la_meme_chose():
    """`ActionFamilies.Members` rend le meme ensemble pour les deux, et le compilateur la meme
    precondition. Le corpus doit pourtant choisir un mot dans sa colonne « attendu », et ce choix est
    arbitraire : le compter comme une erreur accuserait le modele d'un defaut de redaction du corpus.
    """
    assert _outcome(
        "require_before(at(agent, group), enter:building)",
        "require_before(at(agent, group), move:building)",
    ).exact

    assert _outcome("penalize(enter:alley, 2)", "penalize(move:alley, k)", factor_any=True).exact


def test_la_normalisation_ne_pardonne_que_le_mot_de_classe():
    """Le seul synonyme est celui que le domaine declare. Tout le reste reste une erreur."""
    # Categorie differente : deux ensembles de lieux differents.
    assert not _outcome("forbid(enter:alley)", "forbid(move:street)").exact

    # Predicat different.
    assert not _outcome(
        "require_before(has(agent, cover), move:alley)",
        "require_before(at(agent, cover), move:alley)",
    ).exact

    # Forme differente.
    assert not _outcome("forbid(enter:building)", "penalize(move:building, k)").exact

    # Schema nomme different — et qui contient le mot d'une classe sans en etre une.
    assert not _outcome("forbid(MoveToLocation)", "forbid(SearchRoom)").exact


def test_un_faux_accepte_reste_un_faux_accepte():
    """La mesure de tete ne depend d'aucune normalisation : elle compte les irrecevables traduites."""
    case = Case(id="essai", utterance="peu importe", expect="rejected", reason="out_of_scope")

    traduite = Outcome(case=case, produced="forbid(Hide)", form="forbid", latency_ms=0, cost_usd=0.0, refused=None)
    refusee = Outcome(case=case, produced=None, form=None, latency_ms=0, cost_usd=0.0, refused="out_of_scope")

    assert traduite.false_accept
    assert not refusee.false_accept
    assert refusee.right_refusal


def test_plusieurs_tirages_donnent_un_taux_moyen_et_la_frequence_de_chaque_echec():
    """Un passage unique a publie trois « regressions » qui etaient du bruit. Sur trois tirages, une
    phrase ratee une fois doit se lire 1/3, et le taux doit etre la moyenne des trois."""
    juste = _outcome("forbid(Wait)", "forbid(Wait)")
    ratee = _outcome("penalize(Wait, 3)", "forbid(Wait)")

    report = Report(model="m", ablation="a", outcomes=[juste, ratee, juste], draws=3)

    assert round(report.rate(report.acceptable, lambda o: o.exact)) == 67
    assert report.failures() == [
        "- (1/3) traduction differente `essai` — `penalize(Wait, 3)` (attendu : `forbid(Wait)`)"
    ]

    # Un seul tirage : le rapport garde la forme des passages publies, sans frequence.
    assert Report(model="m", ablation="a", outcomes=[ratee]).failures()[0].startswith("- traduction")
