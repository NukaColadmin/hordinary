"""Le harnais d'evaluation (livrable 15).

    python -m evals.run --model deepseek/deepseek-v4.1-flash
    python -m evals.run --model x --no-digest          # ablation 1
    python -m evals.run --model x --few-shot 4         # ablation 2
    python -m evals.run --model x --no-glossary        # ablation 3 : le sens des symboles retire
    python -m evals.run --corpus evals/heldout.jsonl   # les phrases tenues a l'ecart
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
import hashlib
from importlib.metadata import distributions
import json
import re
import sys
import tempfile
import time
from collections.abc import Callable
from collections import Counter
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
HELDOUT = HERE / "heldout.jsonl"
"""Phrases tenues a l'ecart : jamais lues pour ecrire le prompt ni le glossaire du domaine. Un gain
qui n'existerait que sur `CORPUS` serait de l'apprentissage deguise ; c'est ici qu'il se verifie."""
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
    # Le nombre de passages fusionnes dans `outcomes`. A temperature nulle, le modele livre ne rend
    # pas la meme reponse d'un tirage a l'autre — OpenRouter repartit les appels entre plusieurs
    # hebergeurs. Un passage unique a publie « trois regressions » qui etaient du bruit et un « 0 % de
    # faux acceptes » qui etait de la chance : les taux se lisent donc sur plusieurs tirages, et
    # chaque echec avec sa frequence.
    draws: int = 1
    completed: bool = True
    incomplete_reason: str | None = None

    def rate(self, subset: list[Outcome], predicate) -> float:
        return 100.0 * sum(1 for o in subset if predicate(o)) / len(subset) if subset else 0.0

    @property
    def acceptable(self) -> list[Outcome]:
        return [o for o in self.outcomes if o.case.expect == "constraint"]

    @property
    def refusable(self) -> list[Outcome]:
        return [o for o in self.outcomes if o.case.expect == "rejected"]

    def row(self) -> str:
        if not self.completed:
            return f"| `{self.model}` | {self.ablation} — INCOMPLET | — | — | — | — | — | — | — |"
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
        """Le detail de ce qui a rate. Un taux sans ses cas ne s'ameliore pas.

        Sur plusieurs tirages, un meme echec n'est ecrit qu'une fois, precede de sa frequence : « 1/3 »
        est une phrase instable, « 3/3 » une phrase que le modele ne sait pas traduire, et ce ne sont
        pas les memes chantiers.
        """
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

        if self.draws <= 1:
            return lines

        counts = Counter(lines)

        # Dans l'ordre du corpus, et non de premiere apparition : une phrase ratee de deux facons
        # differentes doit se lire en deux lignes voisines.
        order = {outcome.case.id: index for index, outcome in reversed(list(enumerate(self.outcomes)))}
        unique = sorted(dict.fromkeys(lines), key=lambda line: order[line.split("`", 2)[1]])

        return [f"- ({counts[line]}/{self.draws}) {line[2:]}" for line in unique]


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


def load_corpus(path: Path = CORPUS) -> list[Case]:
    cases = []

    for line in path.read_text(encoding="utf-8").splitlines():
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


def load_digest(empty: bool = False, glossary: bool = True) -> DomainDigest:
    """Le digest du jeu, ou un digest vide pour l'ablation.

    L'ablation « sans digest » n'envoie pas un digest partiel : elle en envoie un vide. Un digest
    partiel mesurerait la qualite de la coupe, pas l'apport du contexte.

    L'ablation « sans glossaire » est la seule coupe partielle, et elle est voulue : elle retire les
    definitions et rien d'autre, ce qui rend au noeud `context` la mise en page exacte des passages
    anterieurs au glossaire. Elle mesure donc ce que le sens des mots apporte, a vocabulaire egal.
    """
    if empty:
        return DomainDigest()

    digest = DomainDigest.model_validate(json.loads(DIGEST.read_text(encoding="utf-8")))

    return digest if glossary else digest.model_copy(update={"glossary": {}})


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


def evaluate(model: str, *, ablation: str, digest: DomainDigest, shots: int, dry: bool,
             path: Path = CORPUS, settings: Settings | None = None,
             on_progress: Callable[[Report], None] | None = None) -> Report:
    corpus = load_corpus(path)
    report = Report(model=model, ablation=ablation, completed=False)
    settings = replace(settings or Settings.from_env(), model=model)
    # Valider tout le corpus avant le premier appel payant, exemples compris.
    requests = [InstructionRequest(utterance=case.utterance + few_shot(case, corpus, shots),
                                   domain_digest=digest, max_chars=4000) for case in corpus]

    if dry:
        provider = ScriptedProvider(replies=['{"status":"rejected","reason":"out_of_scope","message":"essai a vide"}'] * len(corpus) * 2)
    else:
        provider = OpenRouterProvider(settings)

    budget = Budget(ceiling=float("inf"), max_calls=0)
    compiled = build(provider, budget, settings)

    for case, request in zip(corpus, requests):
        started = time.perf_counter()
        reply = run(compiled, request, retries=settings.self_check_retries)
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

        if budget.uncertain:
            report.incomplete_reason = "Facturation inconnue ou panne fournisseur : passage interrompu."
        if on_progress:
            on_progress(report)
        if report.incomplete_reason:
            return report

        print(f"  {case.id:<20} {'ok' if (produced or case.expect == 'rejected') else '--'}", flush=True)

    report.completed = True
    return report


def prepare_outputs(paths: list[Path]) -> None:
    """Eprouve les droits du montage avant tout appel facture, sans effacer les rapports existants."""
    for path in paths:
        if path.exists():
            with path.open("a", encoding="utf-8"):
                pass
        with tempfile.TemporaryFile(dir=path.parent) as probe:
            probe.write(b"verification")
            probe.flush()


def _write_atomic(path: Path, text: str) -> None:
    """Chaque point de reprise remplace un fichier entier, jamais un rapport a moitie ecrit."""
    temporary = None
    try:
        with tempfile.NamedTemporaryFile(mode="w", encoding="utf-8", dir=path.parent, delete=False) as stream:
            temporary = Path(stream.name)
            stream.write(text)
        # Sous Windows, l'indexeur ou l'antivirus peut ouvrir brievement le rapport entre deux
        # points de reprise. Reessayer le remplacement, sans rejouer aucun appel fournisseur.
        for attempt in range(6):
            try:
                temporary.replace(path)
                break
            except PermissionError:
                if sys.platform != "win32" or attempt == 5:
                    raise
                time.sleep(0.02 * (2 ** attempt))
    finally:
        if temporary is not None:
            temporary.unlink(missing_ok=True)


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
    parser.add_argument("--no-glossary", action="store_true", help="Ablation : le digest sans les definitions des symboles.")
    parser.add_argument("--few-shot", type=int, default=0, help="Ablation : k exemples proches.")
    parser.add_argument("--corpus", default=str(CORPUS), help=f"Le jeu de phrases ; {HELDOUT.name} pour les phrases tenues a l'ecart.")
    parser.add_argument("--repeat", type=int, default=1, help="Tirages par modele, fusionnes en une ligne ; 3 pour conclure.")
    parser.add_argument("--dry-run", action="store_true", help="Sans reseau : verifie le harnais, pas les modeles.")
    parser.add_argument("--out", default=str(HERE / "results.md"))

    args = parser.parse_args()
    settings = Settings.from_env()
    models = args.model or ["dry-run" if args.dry_run else settings.model]
    output = Path(args.out)
    manifest_path = output.with_suffix(".manifest.json")
    try:
        prepare_outputs([output, manifest_path])
    except OSError:
        parser.error("Rapport ou manifeste non inscriptible : verifier le dossier et les droits avant evaluation.")

    ablation = "aucune"
    if args.no_digest:
        ablation = "sans digest"
    elif args.no_glossary:
        ablation = "sans glossaire"
    elif args.few_shot:
        ablation = f"few-shot {args.few_shot}"

    corpus = Path(args.corpus)
    if corpus.resolve() != CORPUS.resolve():
        ablation += f", {corpus.name}"

    draws = max(1, args.repeat)
    if draws > 1:
        ablation += f", {draws} tirages"

    reports = []

    # Liste blanche des reglages publics : aucun secret de Settings n'est serialise.
    from app.graph import SYSTEM
    digest = load_digest(empty=args.no_digest, glossary=not args.no_glossary)
    manifest = {
        "models_requested": models,
        "dry_run": args.dry_run,
        "draws": draws,
        "few_shot": args.few_shot,
        "temperature": settings.temperature,
        "reasoning": settings.reasoning,
        "timeout_s": settings.timeout_s,
        "self_check_retries": settings.self_check_retries,
        "max_output_tokens": 512,
        "system_sha256": hashlib.sha256(SYSTEM.encode()).hexdigest(),
        "digest_sha256": hashlib.sha256(json.dumps(digest.model_dump(), sort_keys=True).encode()).hexdigest(),
        "corpus_sha256": hashlib.sha256(corpus.read_bytes()).hexdigest(),
        "dependencies": {d.metadata["Name"]: d.version for d in distributions()},
    }

    def save(current: list[Report], status: str) -> str:
        lines = [HEADER, *(report.row() for report in current), ""]
        for report in current:
            failures = report.failures()
            if not report.completed:
                failures = [f"- **INCOMPLET** : {report.incomplete_reason or 'passage en cours'} ; "
                            f"{len(report.outcomes)} observations conservees, taux non publies.", *failures]
            if failures:
                lines += [f"### `{report.model}` — {report.ablation}", "", *failures, ""]
        text = "\n".join(lines)
        manifest["status"] = status
        manifest["reports"] = [{"model": r.model, "completed": r.completed,
                                "observations": len(r.outcomes), "reason": r.incomplete_reason} for r in current]
        _write_atomic(output, text)
        _write_atomic(manifest_path, json.dumps(manifest, indent=2, sort_keys=True))
        return text

    for model in models:
        merged = Report(model=model, ablation=ablation, draws=draws, completed=False)
        save([*reports, merged], "running")

        def checkpoint(partial: Report) -> None:
            snapshot = replace(merged, outcomes=[*merged.outcomes, *partial.outcomes],
                               incomplete_reason=partial.incomplete_reason)
            save([*reports, snapshot], "incomplete" if partial.incomplete_reason else "running")

        for draw in range(draws):
            print(f"[{model}] {ablation}" + (f" — tirage {draw + 1}" if draws > 1 else ""))
            report = evaluate(
                model,
                ablation=ablation,
                digest=digest,
                shots=args.few_shot,
                dry=args.dry_run,
                path=corpus,
                settings=settings,
                on_progress=checkpoint,
            )
            merged.outcomes += report.outcomes
            if not report.completed:
                merged.incomplete_reason = report.incomplete_reason
                break
        else:
            merged.completed = True

        reports.append(merged)
        if not merged.completed:
            break  # Un autre tirage ne doit pas contourner l'arret pour facturation inconnue.

    complete = all(report.completed for report in reports)
    text = save(reports, "complete" if complete else "incomplete")

    print()
    print(text)
    print(f"\nEcrit dans {args.out}")

    return 0 if complete else 1


if __name__ == "__main__":
    raise SystemExit(main())
