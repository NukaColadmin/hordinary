"""L'adaptateur OpenRouter et le plafond, avec des reponses simulees.

Aucune cle n'est necessaire et aucun appel n'est emis. Ce qui est eprouve ici est la traduction d'une
reponse LangChain en `Completion` — c'est-a-dire precisement ce qui casse quand une version de
LangChain deplace ses metadonnees, et qu'aucun test d'integration ne verrait sans facturer un appel.
"""

from __future__ import annotations

import pytest

from app.provider import Budget, Completion, OpenRouterProvider, ScriptedProvider, parse_json
from app.settings import Settings


class FakeResponse:
    def __init__(self, content, metadata=None):
        self.content = content
        self.response_metadata = metadata or {}


class FakeModel:
    def __init__(self, response):
        self._response = response
        self.seen = []

    def invoke(self, messages):
        self.seen.append(messages)
        return self._response


# --- L'adaptateur ---------------------------------------------------------------------------------


def test_l_adaptateur_rend_le_contrat_attendu():
    response = FakeResponse(
        '{"status":"constraint"}',
        {
            "model_name": "qwen/qwen3.8-flash",
            "token_usage": {"prompt_tokens": 890, "completion_tokens": 64},
            "cost": 0.0012,
        },
    )

    provider = OpenRouterProvider(Settings(api_key="test"), model=FakeModel(response))
    completion = provider.complete("systeme", "joueur")

    assert isinstance(completion, Completion)
    assert completion.text == '{"status":"constraint"}'
    assert completion.model == "qwen/qwen3.8-flash"
    assert completion.prompt_tokens == 890
    assert completion.completion_tokens == 64
    assert completion.cost_usd == pytest.approx(0.0012)
    assert completion.latency_ms >= 0


def test_l_adaptateur_survit_a_des_metadonnees_absentes():
    """Un cout absent vaut zero, jamais une estimation.

    Estimer a partir d'une table de tarifs serait faux le lendemain, et un chiffre faux dans un
    tableau d'evaluation est pire qu'une case vide.
    """
    provider = OpenRouterProvider(Settings(api_key="test"), model=FakeModel(FakeResponse("{}")))
    completion = provider.complete("systeme", "joueur")

    assert completion.cost_usd == 0.0
    assert completion.prompt_tokens == 0


def test_l_adaptateur_lit_un_contenu_en_morceaux():
    """Certains modeles rendent une liste de blocs plutot qu'une chaine."""
    response = FakeResponse([{"type": "text", "text": '{"a":'}, {"type": "text", "text": "1}"}])

    provider = OpenRouterProvider(Settings(api_key="test"), model=FakeModel(response))

    assert provider.complete("s", "u").text == '{"a":1}'


def test_sans_cle_l_adaptateur_refuse_de_se_construire():
    with pytest.raises(RuntimeError, match="OPENROUTER_API_KEY"):
        OpenRouterProvider(Settings(api_key=""))


# --- Le JSON --------------------------------------------------------------------------------------


@pytest.mark.parametrize(
    "text",
    [
        '{"status":"rejected"}',
        '```json\n{"status":"rejected"}\n```',
        'Voici : {"status":"rejected"} — voila.',
    ],
)
def test_le_json_se_lit_meme_enrobe(text):
    assert parse_json(text) == {"status": "rejected"}


@pytest.mark.parametrize("text", ["", "aucun json ici", "[1, 2, 3]"])
def test_ce_qui_n_est_pas_un_objet_json_ne_l_est_pas(text):
    assert parse_json(text) is None


# --- Le plafond -----------------------------------------------------------------------------------


def test_le_plafond_compte_les_appels_et_pas_les_requetes():
    budget = Budget(ceiling=0.01)

    budget.charge(0.004)
    budget.charge(0.004)

    assert not budget.exhausted
    assert budget.calls == 2

    budget.charge(0.004)

    assert budget.exhausted


def test_un_cout_negatif_ne_rembourse_personne():
    budget = Budget(ceiling=0.01)
    budget.charge(-5.0)

    assert budget.spent == 0.0


def test_le_fournisseur_de_test_rend_ses_reponses_dans_l_ordre():
    provider = ScriptedProvider(replies=["un", "deux"])

    assert provider.complete("s", "u").text == "un"
    assert provider.complete("s", "u").text == "deux"
    assert provider.complete("s", "u").text == "{}"
