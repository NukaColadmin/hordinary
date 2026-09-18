"""Le vocabulaire des tests vient du jeu, jamais d'ici.

`domain.digest.json` est **genere** par `Tests/PddlArtifactsTests.Le_digest_du_service_est_a_jour` et
versionne. Le lire plutot que d'ecrire un digest a la main est le point : un vocabulaire redige ici
serait une seconde liste de symboles, qui prendrait du retard sur `config/planner_domain.json` sans
que rien ne le signale — exactement ce que le digest existe pour supprimer.

Consequence pratique : changer le domaine du jeu et oublier de regenerer casse la suite .NET *avant*
de faire mentir ces tests-ci.
"""

from __future__ import annotations

import json
from pathlib import Path

import pytest

from app.contracts import DomainDigest

DIGEST_PATH = Path(__file__).parent / "domain.digest.json"


@pytest.fixture(scope="session")
def digest() -> DomainDigest:
    return DomainDigest.model_validate(json.loads(DIGEST_PATH.read_text(encoding="utf-8")))


@pytest.fixture
def request_of(digest):
    """Fabrique une requete du jeu, avec le vrai digest et la phrase donnee."""
    from app.contracts import InstructionRequest

    def make(utterance: str, seed: int = 7, max_chars: int = 200) -> InstructionRequest:
        return InstructionRequest(
            utterance=utterance, seed=seed, domain_digest=digest, max_chars=max_chars
        )

    return make
