"""Les reglages du service, lus dans l'environnement.

La cle d'API n'est **jamais** dans un fichier : `config/game_config.json` ne porte que le *nom* de la
variable d'environnement qui la contient, et ce fichier-ci ne porte rien du tout. Un depot est
publie ; une cle qui y entre est publiee avec lui.

Le reste — modele, plafond de depense, temperature — a un defaut utilisable et se surcharge par
l'environnement, pour que comparer deux modeles ne demande pas de toucher au code.
"""

from __future__ import annotations

import os
from dataclasses import dataclass


def _float(name: str, fallback: float) -> float:
    try:
        return float(os.environ[name])
    except (KeyError, ValueError):
        return fallback


def _int(name: str, fallback: int) -> int:
    try:
        return int(os.environ[name])
    except (KeyError, ValueError):
        return fallback


@dataclass(frozen=True)
class Settings:
    """Ce dont le service a besoin pour tourner. Immuable : relu au demarrage, jamais en cours."""

    api_key: str = ""
    model: str = "anthropic/claude-haiku-4.5"
    base_url: str = "https://openrouter.ai/api/v1"

    #: Plafond dur de depense, en dollars, pour la duree du processus. Atteint, le service refuse
    #: avec la famille `budget_exhausted` — et le jeu bascule sur les instructions pre-ecrites en le
    #: signalant au joueur. Le meme plafond existe cote jeu : deux garde-fous pour deux pannes
    #: differentes, un service oublie allume et un jeu qui boucle.
    max_cost_usd: float = 0.50

    #: Zero par defaut. Le determinisme du jeu ne vient jamais de la, mais du cache cote client —
    #: une temperature basse reduit seulement la dispersion des traductions d'une phrase a l'autre.
    temperature: float = 0.0

    #: Le mode de raisonnement demande a OpenRouter, pour les modeles qui en ont un. Vide par
    #: defaut : le service n'impose rien et laisse le fournisseur a son reglage d'usine.
    #:
    #: Il existe parce qu'un modele a raisonnement facture ses tokens de reflexion en sortie **et**
    #: les fait attendre. Sur une tache dont la sortie utile fait cent tokens, c'est la difference
    #: entre trois secondes et trois minutes — mesure : un passage a 32 s de latence mediane et
    #: 267 s en p95, pour un `timeout_s` de 20. Comparer un modele a raisonnement sans ce reglage
    #: mesure son bavardage, pas sa comprehension.
    #:
    #: `off` coupe la reflexion ; `minimal` / `low` / `medium` / `high` la bornent.
    #:
    #: **Ne pas confondre avec `exclude`**, qui est le piege : sur OpenRouter, `reasoning.exclude`
    #: dit « ne me renvoie pas la trace », pas « ne reflechis pas ». Mesure sur qwen3.7-flash, une
    #: phrase triviale : sans reglage 6 454 ms et 268 tokens de reflexion, avec `exclude` 4 843 ms
    #: et les memes 268 tokens — factures et attendus pour rien. Avec `off` : 983 ms, zero token de
    #: reflexion, 22 fois moins cher. `effort: minimal` ne coupe rien non plus.
    reasoning: str = ""

    #: Une reprise, puis abandon (noeud `self_check`).
    self_check_retries: int = 1

    timeout_s: float = 20.0

    @property
    def configured(self) -> bool:
        """Faux si aucune cle n'est presente : le service repond alors, mais refuse tout appel."""
        return bool(self.api_key)

    @staticmethod
    def from_env(api_key_env: str = "OPENROUTER_API_KEY") -> "Settings":
        return Settings(
            api_key=os.environ.get(api_key_env, ""),
            model=os.environ.get("HORDINARY_MODEL", Settings.model),
            base_url=os.environ.get("HORDINARY_BASE_URL", Settings.base_url),
            max_cost_usd=_float("HORDINARY_MAX_COST_USD", Settings.max_cost_usd),
            temperature=_float("HORDINARY_TEMPERATURE", Settings.temperature),
            reasoning=os.environ.get("HORDINARY_REASONING", Settings.reasoning),
            self_check_retries=_int("HORDINARY_SELF_CHECK_RETRIES", Settings.self_check_retries),
            timeout_s=_float("HORDINARY_TIMEOUT_S", Settings.timeout_s),
        )
