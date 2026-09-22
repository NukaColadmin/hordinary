"""Le harnais d'evaluation (livrable 15).

    python -m evals.run --model deepseek/deepseek-v4.1-flash
    python -m evals.run --model x --no-digest          # ablation 1
    python -m evals.run --model x --few-shot 4         # ablation 2
    python -m evals.run --dry-run                      # sans reseau, pour verifier le harnais

Ce qui est mesure, et pourquoi :

| Mesure                  | Ce qu'elle dit                                                     |
|-------------------------|--------------------------------------------------------------------|
| Taux de compilation     | combien de phrases recevables deviennent une contrainte valide      |
| Taux de refus correct   | combien d'irrecevables sont refusees, **et pour la bonne raison**   |
| **Taux de faux acceptes** | **combien d'irrecevables sont traduites quand meme**              |
| Exactitude de la forme  | la bonne des cinq formes a-t-elle ete choisie                       |
| Exactitude exacte       | la contrainte est-elle celle attendue, mot pour mot                 |
| Latence p50 / p95       |                                                                     |
| Cout par instruction    | tel qu'OpenRouter le rapporte                                       |

Le taux de faux acceptes est le chiffre a mettre en avant, **y compris s'il est mauvais** : c'est ce
que les quatre portillons existent pour rattraper, et c'est ce qui rend la demonstration honnete. Un
tableau qui ne publierait que le taux de compilation dirait qu'un modele complaisant est le meilleur.
"""

from __future__ import annotations

import argparse
import json
import re
import sys
import time
from dataclasses import dataclass, field, replace
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent

sys.path.insert(0, str(ROOT))

from app.contracts import ConstraintReply, DomainDigest, InstructionRequest  # noqa: E402
from app.graph import build, run  # noqa: E402
from app.grammar import FAMILIES, check, to_text  # noqa: E402
from app.provider import Budget, OpenRouterProvider, ScriptedProvider  # noqa: E402
from app.settings import Settings  # noqa: E402

CORPUS = HERE / "utterances.jsonl"
DIGEST = ROOT / "tests" / "domain.digest.json"


@dataclass
class Case:
    id: str
    utterance: str
    expect: str
    constraint: str | None = None
    reason: str | None = None
    factor_any: bool = False
    note: str = ""


@dataclass
class Outcome:
    case: Case
    produced: str | None
    form: str | None
    refused: str | None
    latency_ms: int
    cost_usd: float

    @property
    def exact(self) -> bool:
        """La contrainte attendue, mot pour mot — le multiplicateur et le mot de classe mis a part.

        Aucune phrase de joueur ne designe un `k` : exiger « penalize(ForceDoor, 3) » plutot que
        « penalize(ForceDoor, 2) » mesurerait la chance et non la comprehension. Et `enter:alley` est
        `move:alley` — voir `_same_family`.
        """
        if self.produced is None or self.case.constraint is None:
            return False

        produced = _comparable(self.produced)
        expected = _comparable(self.case.constraint)

        if self.case.factor_any:
            return _without_factor(produced) == _without_factor(expected)

        return produced == expected

    @property
    def right_form(self) -> bool:
        return self.form is not None and self.form == _form_of(self.case.constraint)

    @property
    def false_accept(self) -> bool:
        """Une phrase irrecevable traduite quand meme. La mesure qui compte."""
        return self.case.expect == "rejected" and self.produced is not None

    @property
    def right_refusal(self) -> bool:
        return self.case.expect == "rejected" and self.refused == self.case.reason


@dataclass
class Report:
    model: str
    ablation: str
    outcomes: list[Outcome] = field(default_factory=list)

    def rate(self, subset: list[Outcome], predicate) -> float:
        return 100.0 * sum(1 for o in subset if predicate(o)) / len(subset) if subset else 0.0

    @property
    def acceptable(self) -> list[Outcome]:
        return [o for o in self.outcomes if o.case.expect == "constraint"]

    @property
    def refusable(self) -> list[Outcome]:
        return [o for o in self.outcomes if o.case.expect == "rejected"]

    def row(self) -> str:
        latencies = sorted(o.latency_ms for o in self.outcomes) or [0]
        cost = sum(o.cost_usd for o in self.outcomes)

        # Les barres encadrantes ne sont pas decoratives : sans elles GitHub ne reconnait pas la
        # ligne comme une ligne de tableau et rend le rapport en paragraphe.
        return "| " + " | ".join(
            [
                f"`{self.model}`",
                self.ablation,
                f"{self.rate(self.acceptable, lambda o: o.produced is not None):.0f} %",
                f"{self.rate(self.acceptable, lambda o: o.right_form):.0f} %",
                f"{self.rate(self.acceptable, lambda o: o.exact):.0f} %",
                f"{self.rate(self.refusable, lambda o: o.right_refusal):.0f} %",
                f"**{self.rate(self.refusable, lambda o: o.false_accept):.0f} %**",
                f"{_percentile(latencies, 50)} / {_percentile(latencies, 95)} ms",
                f"{cost / max(1, len(self.outcomes)):.5f} $",
            ]
        ) + " |"

    def failures(self) -> list[str]:
        """Le detail de ce qui a rate. Un taux sans ses cas ne s'ameliore pas."""
        lines = []

        for outcome in self.outcomes:
            if outcome.false_accept:
                lines.append(
                    f"- **faux accepte** `{outcome.case.id}` — « {outcome.case.utterance} » "
                    f"→ `{outcome.produced}` (attendu : refus `{outcome.case.reason}`)"
                )
            elif outcome.case.expect == "constraint" and outcome.produced is None:
                lines.append(
                    f"- non traduite `{outcome.case.id}` — « {outcome.case.utterance} » "
                    f"→ refus `{outcome.refused}` (attendu : `{outcome.case.constraint}`)"
                )
            elif outcome.case.expect == "constraint" and not outcome.exact:
                lines.append(
                    f"- traduction differente `{outcome.case.id}` — `{outcome.produced}` "
                    f"(attendu : `{outcome.case.constraint}`)"
                )
            elif outcome.case.expect == "rejected" and not outcome.right_refusal:
                lines.append(
                    f"- famille de refus `{outcome.case.id}` — `{outcome.refused}` "
                    f"(attendu : `{outcome.case.reason}`)"
                )

        return lines


def _form_of(constraint: str | None) -> str | None:
    return constraint.split("(", 1)[0] if constraint else None


_FAMILY = re.compile(r"\b(?:" + "|".join(FAMILIES) + r"):", re.IGNORECASE)


def _comparable(constraint: str) -> str:
    """La contrainte reduite a ce qui a une consequence mecanique, pour la comparer au corpus.

    Le mot de classe de schemas — `move:` ou `enter:` — n'en a aucune. `ActionFamilies.Members` rend
    le **meme** ensemble de schemas pour les deux (tous ceux qui ont une destination), et le
    compilateur pose la **meme** precondition de categorie : `enter:alley` et `move:alley` compilent
    a l'identique. Les deux mots existent parce qu'un joueur ecrit l'un ou l'autre.

    Le corpus doit pourtant en choisir un dans sa colonne « attendu », et ce choix est arbitraire :
    comparer au mot pres faisait compter une traduction juste comme une erreur, et le chiffre publie
    accusait alors le modele d'un defaut de redaction du corpus. Le rendu montre au joueur, lui, garde
    le mot qu'il a employe : c'est la mesure qu'on normalise, pas le produit.

    Rien d'autre n'est efface ici. Un predicat different, une action differente, une forme differente
    restent des erreurs — c'est le seul synonyme que le domaine declare.
    """
    return _FAMILY.sub("move:", constraint)


def _without_factor(constraint: str) -> str:
    head, _, _ = constraint.rpartition(",")
    return head + ")" if head else constraint


def _percentile(values: list[int], share: int) -> int:
    if not values:
        return 0

    index = min(len(values) - 1, int(round((share / 100.0) * (len(values) - 1))))

    return values[index]


def load_corpus() -> list[Case]:
    cases = []

    for line in CORPUS.read_text(encoding="utf-8").splitlines():
        if not line.strip():
            continue

        raw = json.loads(line)
        cases.append(
            Case(
                id=raw["id"],
                utterance=raw["utterance"],
                expect=raw["expect"],
                constraint=raw.get("constraint"),
                reason=raw.get("reason"),
                factor_any=bool(raw.get("factor_any", False)),
                note=raw.get("note", ""),
            )
        )

    return cases


def load_digest(empty: bool = False) -> DomainDigest:
    """Le digest du jeu, ou un digest vide pour l'ablation.

    L'ablation « sans digest » n'envoie pas un digest partiel : elle en envoie un vide. Un digest
    partiel mesurerait la qualite de la coupe, pas l'apport du contexte.
    """
    if empty:
        return DomainDigest()

    return DomainDigest.model_validate(json.loads(DIGEST.read_text(encoding="utf-8")))


def few_shot(case: Case, corpus: list[Case], count: int) -> str:
    """Les `k` exemples les plus proches, pris dans le corpus **prive de la phrase courante**.

    C'est la seule recuperation documentaire du projet, et elle est optionnelle : un vrai corpus, une
    vraie selection, un effet mesurable sur le taux de compilation. Elle entre dans l'evaluation comme
    une ablation parmi d'autres, avec obligation de publier le chiffre **y compris s'il est nul ou
    negatif**.

    Fuite de donnees : la phrase evaluee est retiree des candidats. Sans cela l'exemple le plus proche
    serait la reponse, et le chiffre publie serait une fraude.
    """
    if count <= 0:
        return ""

    target = set(case.utterance.lower().split())
    scored = []

    for other in corpus:
        if other.id == case.id:
            continue

        overlap = len(target & set(other.utterance.lower().split()))
        scored.append((overlap, other))

    scored.sort(key=lambda pair: (-pair[0], pair[1].id))

    # Sur une seule ligne : le noeud `normalize` reduit les espaces, sauts de ligne compris, et un
    # bloc multi-ligne arriverait de toute facon aplati au modele. Autant qu'il soit lisible tel
    # qu'il sera lu.
    examples = " ; ".join(
        f"« {other.utterance} » -> "
        + (other.constraint if other.expect == "constraint" else f"refus ({other.reason})")
        for _, other in scored[:count]
    )

    return f" EXEMPLES : {examples}"


def evaluate(model: str, *, ablation: str, digest: DomainDigest, shots: int, dry: bool) -> Report:
    corpus = load_corpus()
    report = Report(model=model, ablation=ablation)

    if dry:
        provider = ScriptedProvider(replies=['{"status":"rejected","reason":"out_of_scope","message":"essai a vide"}'] * len(corpus) * 2)
    else:
        provider = OpenRouterProvider(replace(Settings.from_env(), model=model))

    budget = Budget(ceiling=float("inf"))
    compiled = build(provider, budget)

    for case in corpus:
        utterance = case.utterance + few_shot(case, corpus, shots)

        started = time.perf_counter()
        reply = run(compiled, InstructionRequest(utterance=utterance, domain_digest=digest, max_chars=4000))
        elapsed = int((time.perf_counter() - started) * 1000)

        produced = None
        form = None

        if isinstance(reply, ConstraintReply):
            # Le harnais revalide : une contrainte que le portillon 1 refuserait n'est pas un succes,
            # et la compter comme tel gonflerait le taux de compilation de tout ce que le jeu rejette.
            if check(reply.constraint, digest) is None:
                produced = to_text(reply.constraint)
                form = reply.constraint.form

        report.outcomes.append(
            Outcome(
                case=case,
                produced=produced,
                form=form,
                refused=getattr(reply, "reason", None),
                latency_ms=reply.latency_ms or elapsed,
                cost_usd=reply.cost_usd,
            )
        )

        print(f"  {case.id:<20} {'ok' if (produced or case.expect == 'rejected') else '--'}", flush=True)

    return report


HEADER = (
    "| Modele | Ablation | Compilation | Forme | Exacte | Refus correct | Faux acceptes | "
    "Latence p50/p95 | Cout/instr. |\n"
    "|---|---|---|---|---|---|---|---|---|"
)


def main() -> int:
    # La sortie standard de Windows est en cp1252, et le rapport contient des fleches. Sans cela
    # un harnais qui a tout reussi meurt sur son dernier print.
    for stream in (sys.stdout, sys.stderr):
        if hasattr(stream, "reconfigure"):
            stream.reconfigure(encoding="utf-8", errors="replace")

    parser = argparse.ArgumentParser(description="Evaluation des modeles sur le corpus d'instructions.")
    parser.add_argument("--model", action="append", default=[], help="Repetable : un passage par modele.")
    parser.add_argument("--no-digest", action="store_true", help="Ablation : contexte de domaine vide.")
    parser.add_argument("--few-shot", type=int, default=0, help="Ablation : k exemples proches.")
    parser.add_argument("--dry-run", action="store_true", help="Sans reseau : verifie le harnais, pas les modeles.")
    parser.add_argument("--out", default=str(HERE / "results.md"))

    args = parser.parse_args()
    models = args.model or ["dry-run" if args.dry_run else Settings.from_env().model]

    ablation = "aucune"
    if args.no_digest:
        ablation = "sans digest"
    elif args.few_shot:
        ablation = f"few-shot {args.few_shot}"

    reports = []

    for model in models:
        print(f"[{model}] {ablation}")
        reports.append(
            evaluate(
                model,
                ablation=ablation,
                digest=load_digest(empty=args.no_digest),
                shots=args.few_shot,
                dry=args.dry_run,
            )
        )

    lines = [HEADER, *(report.row() for report in reports), ""]

    for report in reports:
        failures = report.failures()

        if not failures:
            continue

        lines += [f"### `{report.model}` — {report.ablation}", "", *failures, ""]

    text = "\n".join(lines)

    # Le fichier d'abord, l'affichage ensuite. Un passage complet coute des appels factures : il
    # ne doit pas se perdre parce que la console ne sait pas rendre un caractere. Cas reel : la
    # console Windows est en cp1252 et le tableau contient des fleches.
    Path(args.out).write_text(text, encoding="utf-8")

    print()
    print(text)
    print(f"\nEcrit dans {args.out}")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
