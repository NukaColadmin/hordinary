"""Le harnais d'evaluation lui-meme.

Il produit les chiffres du livrable, donc ses regles de comparaison sont du code livre et non de
l'outillage : une regle trop stricte accuse un modele d'une erreur qu'il n'a pas faite, une regle
trop laxiste publie un taux qui ne veut rien dire. Les deux se testent ici, sans reseau.
"""

from __future__ import annotations

import json
import sys
from pathlib import Path

import pytest

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


@pytest.mark.parametrize("panne", [True, False])
def test_facturation_inconnue_interrompt_le_corpus_sans_publier_de_taux(monkeypatch, panne):
    from evals import run as harness
    from app.provider import Completion
    from app.settings import Settings

    class Provider:
        calls = 0

        def complete(self, *args):
            self.calls += 1
            if panne:
                raise TimeoutError("erreur confidentielle")
            return Completion('{"status":"rejected","reason":"out_of_scope","message":"refus"}', cost_known=False)

    provider = Provider()
    monkeypatch.setattr(harness, "OpenRouterProvider", lambda settings: provider)
    checkpoints = []
    report = harness.evaluate("essai", ablation="aucune", digest=harness.load_digest(),
                              shots=0, dry=False, settings=Settings(), on_progress=checkpoints.append)
    assert provider.calls == 1
    assert len(report.outcomes) == 1
    assert not report.completed
    assert report.incomplete_reason
    assert "INCOMPLET" in report.row()
    assert "%" not in report.row()
    assert len(checkpoints) == 1


def test_reglage_de_reprise_effectivement_applique(monkeypatch, tmp_path):
    from evals import run as harness
    from app.provider import ScriptedProvider
    from app.settings import Settings

    corpus = tmp_path / "corpus.jsonl"
    corpus.write_text(json.dumps({"id": "essai", "utterance": "Ne forcez aucune porte.",
                                  "expect": "constraint", "constraint": "forbid(ForceDoor)"}), encoding="utf-8")
    provider = ScriptedProvider(replies=['{}', '{"status":"constraint","constraint":{"form":"forbid","action_class":"ForceDoor"}}'])
    monkeypatch.setattr(harness, "OpenRouterProvider", lambda settings: provider)
    harness.evaluate("essai", ablation="aucune", digest=harness.load_digest(), shots=0,
                     dry=False, path=corpus, settings=Settings(self_check_retries=0))
    assert len(provider.seen) == 1


def test_sorties_verifiees_avant_toute_evaluation(monkeypatch, tmp_path):
    from evals import run as harness

    def interdit(*args, **kwargs):
        pytest.fail("Le fournisseur ne doit pas etre construit avant verification des sorties.")

    monkeypatch.setattr(harness, "evaluate", interdit)
    monkeypatch.setattr(sys, "argv", ["evals.run", "--out", str(tmp_path / "absent" / "rapport.md")])
    with pytest.raises(SystemExit) as error:
        harness.main()
    assert error.value.code == 2


def test_cli_conserve_le_point_de_reprise_et_arrete_les_tirages(monkeypatch, tmp_path):
    from evals import run as harness
    from app.provider import Completion

    output = tmp_path / "rapport.md"
    provider_calls = []

    class Provider:
        def complete(self, *args):
            provider_calls.append(1)
            # Le rapport et le manifeste existent deja avant l'appel facture.
            assert "INCOMPLET" in output.read_text(encoding="utf-8")
            assert json.loads(output.with_suffix(".manifest.json").read_text())["status"] == "running"
            return Completion('{"status":"rejected","reason":"out_of_scope","message":"refus"}', cost_known=False)

    monkeypatch.setattr(harness, "OpenRouterProvider", lambda settings: Provider())
    monkeypatch.setattr(sys, "argv", ["evals.run", "--model", "essai", "--repeat", "3", "--out", str(output)])
    assert harness.main() == 1
    assert len(provider_calls) == 1
    manifest = json.loads(output.with_suffix(".manifest.json").read_text())
    assert manifest["status"] == "incomplete"
    assert manifest["reports"][0]["observations"] == 1
    assert "INCOMPLET" in output.read_text(encoding="utf-8")


def test_verrou_windows_transitoire_ne_perd_pas_le_rapport(monkeypatch, tmp_path):
    from evals import run as harness

    original = Path.replace
    attempts = []

    def replace(source, target):
        attempts.append(1)
        if len(attempts) == 1:
            raise PermissionError("Verrou Windows transitoire")
        return original(source, target)

    monkeypatch.setattr(harness.sys, "platform", "win32")
    monkeypatch.setattr(Path, "replace", replace)
    monkeypatch.setattr(harness.time, "sleep", lambda delay: None)
    output = tmp_path / "rapport.md"
    harness._write_atomic(output, "rapport conserve")
    assert output.read_text(encoding="utf-8") == "rapport conserve"
    assert len(attempts) == 2
