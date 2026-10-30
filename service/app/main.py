"""L'endpoint `/instruction` (livrable 15).

Le service est volontairement mince : il recoit une phrase et un digest, deroule le graphe, rend une
contrainte ou un refus. Il ne garde aucun etat de partie, ne connait pas la carte, ne sait pas qui
joue. Tout ce qui a une consequence sur le jeu se decide dans `Hordinary.Core`, apres.

Il est aussi le seul a connaitre la cle d'API. Le jeu parle a `127.0.0.1` ; la cle vit dans
l'environnement de ce processus-ci, et n'entre ni dans le depot, ni dans le binaire Godot, ni dans la
memoire du processus de jeu.
"""

from __future__ import annotations

import logging
import asyncio
import secrets
import time
from collections import deque
from threading import BoundedSemaphore, Lock

from fastapi import FastAPI, Request
from fastapi.exceptions import RequestValidationError
from fastapi.responses import JSONResponse

from .contracts import InstructionRequest, RefusalFamily, RejectedReply
from .graph import build, run
from .provider import Budget, OpenRouterProvider
from .settings import Settings

log = logging.getLogger("hordinary.instruction")

app = FastAPI(
    title="Hordinary — instructions tactiques",
    summary="Traduit une consigne de joueur en contrainte de la grammaire fermee du livrable 15.",
    version="1.0",
)

_settings = Settings.from_env()
_budget = Budget(ceiling=_settings.max_cost_usd)
_compiled = None
_slots = BoundedSemaphore(1)
_rate_lock = Lock()
_arrivals = deque()


@app.exception_handler(RequestValidationError)
async def invalid_request(request, error):
    return JSONResponse({"detail": "Requete invalide ou limites depassees."}, status_code=422)


@app.middleware("http")
async def protect(request: Request, call_next):
    if request.url.path != "/instruction":
        return await call_next(request)
    if _settings.configured or _settings.service_token:
        if not _settings.service_token:
            return JSONResponse({"detail": "Configurez HORDINARY_SERVICE_TOKEN."}, status_code=503)
        supplied = request.headers.get("authorization", "")
        if not secrets.compare_digest(supplied.encode(), ("Bearer " + _settings.service_token).encode()):
            return JSONResponse({"detail": "Authentification requise."}, status_code=401)
    with _rate_lock:
        now = time.monotonic()
        while _arrivals and _arrivals[0] <= now - 60:
            _arrivals.popleft()
        if len(_arrivals) >= 10:
            return JSONResponse({"detail": "Limite de requetes atteinte."}, status_code=429, headers={"Retry-After": "60"})
        _arrivals.append(now)
    # Verifier le flux reel, y compris sans Content-Length.
    body = bytearray()
    try:
        async with asyncio.timeout(5):
            async for chunk in request.stream():
                if len(body) + len(chunk) > 65536:
                    return JSONResponse({"detail": "Corps trop volumineux."}, status_code=413)
                body.extend(chunk)
    except TimeoutError:
        return JSONResponse({"detail": "Envoi du corps trop lent."}, status_code=408)
    request._body = bytes(body)
    if not _slots.acquire(blocking=False):
        return JSONResponse({"detail": "Service occupe."}, status_code=429, headers={"Retry-After": "2"})
    try:
        return await call_next(request)
    finally:
        _slots.release()


def _graph():
    """Le graphe, compile une seule fois et seulement si une cle existe.

    Compiler paresseusement permet au service de demarrer sans cle : `/health` repond, `/instruction`
    refuse avec `provider_unavailable`. Un service qui refuserait de demarrer obligerait a configurer
    OpenRouter pour seulement verifier que le jeu se replie correctement — c'est-a-dire a payer pour
    tester le cas gratuit.
    """
    global _compiled

    if _compiled is None:
        _compiled = build(OpenRouterProvider(_settings), _budget, _settings)

    return _compiled


@app.get("/health")
def health() -> dict:
    """De quoi savoir si le service est la, et ce qu'il a deja depense."""
    return {
        "ok": True,
        "configured": _settings.configured,
        "model": _settings.model,
        "spent_usd": round(_budget.spent, 6),
        "ceiling_usd": _budget.ceiling,
        "calls": _budget.calls,
    }


@app.post("/instruction")
def instruction(request: InstructionRequest) -> JSONResponse:
    """Une phrase entre, une contrainte ou un refus sort. Jamais autre chose.

    La methode ne leve pas : le jeu traite un refus et une panne de la meme facon — il se replie sur
    les instructions pre-ecrites en le signalant — et une erreur 500 ne lui apprendrait rien de plus
    qu'un refus motive, tout en le privant de la raison.
    """
    if not _settings.configured:
        return _refuse(
            RefusalFamily.PROVIDER,
            "Le service tourne sans cle d'API : aucun modele n'est joignable.",
        )

    if _budget.exhausted:
        return _refuse(
            RefusalFamily.BUDGET,
            f"Plafond de depense du service atteint ({_budget.spent:.4f} / {_budget.ceiling:.2f} USD).",
        )

    try:
        reply = run(_graph(), request, retries=_settings.self_check_retries)
    except Exception as error:  # noqa: BLE001 — voir la docstring
        log.error("Le graphe a echoue (%s).", type(error).__name__)
        return _refuse(RefusalFamily.PROVIDER, "Le service n'a pas abouti.")

    return JSONResponse(reply.model_dump())


def _refuse(family: str, message: str) -> JSONResponse:
    return JSONResponse(RejectedReply(reason=family, message=message).model_dump())
