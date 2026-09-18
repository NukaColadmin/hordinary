"""L'endpoint, vu du jeu.

Le service demarre **sans cle** : `/health` repond et `/instruction` refuse avec
`provider_unavailable`. C'est volontaire — refuser de demarrer obligerait a configurer OpenRouter
pour seulement verifier que le jeu se replie correctement, c'est-a-dire a payer pour tester le cas
gratuit.

Aucun test de ce fichier n'emet de requete vers un fournisseur.
"""

from __future__ import annotations

import json
from pathlib import Path

import pytest
from fastapi.testclient import TestClient

DIGEST = json.loads((Path(__file__).parent / "domain.digest.json").read_text(encoding="utf-8"))


@pytest.fixture
def client(monkeypatch):
    monkeypatch.delenv("OPENROUTER_API_KEY", raising=False)

    import importlib

    from app import main

    importlib.reload(main)

    return TestClient(main.app)


def test_le_service_repond_sans_cle(client):
    response = client.get("/health")

    assert response.status_code == 200
    assert response.json()["configured"] is False


def test_sans_cle_une_instruction_est_refusee_et_non_plantee(client):
    """Un refus motive, pas une 500.

    Le jeu traite un refus et une panne de la meme facon — repli sur les instructions pre-ecrites, en
    le signalant — mais une 500 le priverait de la raison a afficher au joueur.
    """
    response = client.post(
        "/instruction",
        json={"utterance": "Ne cassez aucune vitre.", "seed": 7, "domain_digest": DIGEST, "max_chars": 200},
    )

    assert response.status_code == 200

    body = response.json()

    assert body["status"] == "rejected"
    assert body["reason"] == "provider_unavailable"
    assert body["message"]


def test_le_contrat_de_sortie_ne_porte_jamais_de_pddl(client):
    """Le service ne renvoie jamais de patch de domaine, jamais de PDDL, jamais de JSON d'action.

    L'assertion est grossiere exprès : elle attrape le jour ou quelqu'un voudra « juste renvoyer le
    domaine compile pour deboguer ». C'est la restriction qui reduit d'un ordre de grandeur ce qu'il y
    a a valider cote jeu, et elle ne se relache pas discretement.
    """
    response = client.post(
        "/instruction",
        json={"utterance": "Ne cassez aucune vitre.", "seed": 7, "domain_digest": DIGEST},
    )

    text = response.text

    assert "(:action" not in text
    assert "(:predicates" not in text
    assert "(define" not in text

    assert set(response.json()) <= {
        "status",
        "constraint",
        "rationale",
        "reason",
        "message",
        "model",
        "latency_ms",
        "tokens",
        "cost_usd",
    }


def test_une_requete_sans_digest_reste_acceptable(client):
    """Le digest a un defaut vide : le service ne doit pas tomber sur un appelant maladroit."""
    response = client.post("/instruction", json={"utterance": "test"})

    assert response.status_code == 200
    assert response.json()["status"] == "rejected"
