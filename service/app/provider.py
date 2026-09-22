"""L'adaptateur vers le fournisseur de modeles, et le plafond de depense.

OpenRouter est l'unique fournisseur. L'inference locale est retiree de la conception et des tests :
la machine cible ne dispose pas de la VRAM necessaire pour l'executer de facon fiable. LangChain
fournit l'adaptateur, avec acces a plusieurs familles de modeles derriere une seule API — ce qui est
exactement ce que demande le tableau d'evaluation du livrable : **comparer** des modeles, pas en
heberger un.

Le protocole `Provider` existe pour que le graphe n'ait jamais besoin d'un reseau. Les tests lui
passent un `ScriptedProvider` : ils eprouvent le graphe, la grammaire, les familles de refus et le
plafond, sans cle d'API et sans connexion.
"""

from __future__ import annotations

import json
import time
from dataclasses import dataclass, field
from typing import Protocol

from .settings import Settings


class BudgetExhausted(RuntimeError):
    """Le plafond de depense est atteint. Ce n'est pas une panne : c'est une decision qui tient."""


@dataclass
class Completion:
    """Ce qu'un appel rend : du texte, et ce qu'il a coute."""

    text: str
    model: str = ""
    prompt_tokens: int = 0
    completion_tokens: int = 0
    cost_usd: float = 0.0
    latency_ms: int = 0


class Provider(Protocol):
    """Ce que le graphe attend d'un fournisseur : une completion, et rien d'autre."""

    def complete(self, system: str, user: str) -> Completion: ...


@dataclass
class Budget:
    """Le compteur de depense du processus.

    Il est ici et non dans le graphe parce qu'il survit a une requete : un plafond par requete ne
    plafonnerait rien du tout.
    """

    ceiling: float
    spent: float = 0.0
    calls: int = 0

    @property
    def exhausted(self) -> bool:
        return self.spent >= self.ceiling

    def charge(self, amount: float) -> None:
        self.spent += max(0.0, amount)
        self.calls += 1


@dataclass
class ScriptedProvider:
    """Un fournisseur de test : il rend les reponses qu'on lui a donnees, dans l'ordre.

    Il n'est pas un bouchon de commodite mais la condition pour que le graphe soit testable. Le noeud
    `self_check` a une reprise : l'eprouver demande un fournisseur qui se trompe d'abord et se corrige
    ensuite, ce qu'aucun vrai modele ne fait sur commande.
    """

    replies: list[str] = field(default_factory=list)
    model: str = "test/modele"
    cost_each: float = 0.0
    seen: list[tuple[str, str]] = field(default_factory=list)

    def complete(self, system: str, user: str) -> Completion:
        self.seen.append((system, user))

        text = self.replies.pop(0) if self.replies else "{}"

        return Completion(
            text=text,
            model=self.model,
            prompt_tokens=len(user) // 4,
            completion_tokens=len(text) // 4,
            cost_usd=self.cost_each,
        )


class OpenRouterProvider:
    """OpenRouter via LangChain.

    Le mode JSON est demande au fournisseur quand il le supporte, et la sortie est **revalidee de
    toute facon** par le noeud `self_check`. On ne fait jamais confiance au drapeau `json_mode` : il
    reduit le taux d'echec, il ne le supprime pas.

    Le cout reel est demande en ligne (`usage.include`, voir `_extra_body`) et arrive **dans**
    `token_usage`, a cote des compteurs de tokens — et non a la racine des metadonnees, ou une
    premiere version le cherchait et ne trouvait jamais rien. On ne l'estime jamais a partir d'une
    table de tarifs : elle serait fausse le lendemain. Un fournisseur qui ne le renvoie pas rend
    donc zero, et le plafond ne protege alors que contre un jeu qui boucle.
    """

    def __init__(self, settings: Settings, model: object | None = None) -> None:
        """`model` est injectable pour que le contrat de sortie soit verifiable sans cle ni reseau.

        C'est la seule facon de tester cet adaptateur : ce qu'il fait, c'est traduire la forme d'une
        reponse LangChain en `Completion`, et cette traduction est precisement ce qui casse quand une
        version de LangChain change la place des metadonnees.
        """
        self._settings = settings

        if model is not None:
            self._model = model
            return

        if not settings.configured:
            raise RuntimeError(
                "Aucune cle d'API. Renseignez OPENROUTER_API_KEY dans l'environnement du service — "
                "jamais dans un fichier de configuration."
            )

        from langchain_openai import ChatOpenAI  # importe ici : les tests n'en ont pas besoin

        self._model = ChatOpenAI(
            model=settings.model,
            api_key=settings.api_key,
            base_url=settings.base_url,
            temperature=settings.temperature,
            timeout=settings.timeout_s,
            model_kwargs={"response_format": {"type": "json_object"}},
            extra_body=_extra_body(settings),
        )

    def complete(self, system: str, user: str) -> Completion:
        started = time.perf_counter()

        response = self._model.invoke([("system", system), ("human", user)])

        elapsed = int((time.perf_counter() - started) * 1000)
        metadata = getattr(response, "response_metadata", {}) or {}
        usage = metadata.get("token_usage", {}) or {}

        return Completion(
            text=_text_of(response),
            model=metadata.get("model_name", self._settings.model),
            prompt_tokens=int(usage.get("prompt_tokens", 0) or 0),
            completion_tokens=int(usage.get("completion_tokens", 0) or 0),
            cost_usd=float(usage.get("cost", metadata.get("cost", 0.0)) or 0.0),
            latency_ms=elapsed,
        )


def _extra_body(settings: Settings) -> dict:
    """Les champs propres a OpenRouter, hors du vocabulaire OpenAI.

    Ils passent par `extra_body` et non par `model_kwargs` : LangChain avertit sur tout ce qu'il ne
    reconnait pas comme parametre OpenAI, et un avertissement par requete finit par etre ignore, y
    compris le jour ou il dit quelque chose.

    `usage.include` fait renvoyer le cout reel **dans la reponse**. Sans lui il faut interroger la
    generation apres coup, ce que ce service ne fait pas — d'ou une colonne de cout vide dans les
    tableaux d'evaluation, qui est la seule chose qu'on ne peut pas reconstituer plus tard.

    `reasoning` n'est ajoute que si `Settings.reasoning` n'est pas vide. Le defaut livre est
    desormais `off` — le modele livre sait raisonner, et le p95 qui a decide de son choix a ete
    mesure sans reflexion. Passer `HORDINARY_REASONING=` vide rend chaque modele a son reglage
    d'usine, ce qu'il faut faire pour comparer : on ne compare pas des modeles en en bridant un seul.
    """
    body: dict = {"usage": {"include": True}}

    mode = (settings.reasoning or "").strip().lower()

    if mode in ("off", "false", "disabled"):
        body["reasoning"] = {"enabled": False}
    elif mode:
        body["reasoning"] = {"effort": mode}

    return body


def _text_of(response: object) -> str:
    """Le contenu textuel d'une reponse LangChain, quelle que soit la forme qu'elle a prise."""
    content = getattr(response, "content", response)

    if isinstance(content, str):
        return content

    if isinstance(content, list):
        return "".join(
            part.get("text", "") if isinstance(part, dict) else str(part) for part in content
        )

    return str(content)


def parse_json(text: str) -> dict | None:
    """Lit le JSON d'une completion, y compris quand le modele l'a enrobe de prose ou de balises.

    Ce rattrapage n'est pas une faveur faite au modele : c'est la difference entre un taux de
    compilation qui mesure la comprehension et un taux qui mesure la discipline de formatage. Ce qui
    est verifie ensuite, ce sont les symboles — et la, rien n'est rattrape.
    """
    if not text:
        return None

    stripped = text.strip()

    if stripped.startswith("```"):
        stripped = stripped.strip("`")
        _, _, stripped = stripped.partition("\n")

    try:
        parsed = json.loads(stripped)
        return parsed if isinstance(parsed, dict) else None
    except json.JSONDecodeError:
        pass

    start = stripped.find("{")
    end = stripped.rfind("}")

    if start < 0 or end <= start:
        return None

    try:
        parsed = json.loads(stripped[start : end + 1])
        return parsed if isinstance(parsed, dict) else None
    except json.JSONDecodeError:
        return None
