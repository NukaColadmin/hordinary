# Hordinary

*[Version française](README.md)*

**A tactical survival game where the player controls no one.** They assign goals; three autonomous
agents perceive, remember, plan, coordinate — and change their minds.

<!-- TODO: replace with a 90 s GIF showing a live replan -->
![Demo](docs/demo.gif)

---

## What this project demonstrates

A complete autonomous-agent architecture, written from scratch, **with no external planning engine
or AI library**:

| Layer | What it solves |
|---|---|
| **Utility AI** | Goal selection: 9 goals composed from 12 reusable primitive considerations, each passed through a response curve; hysteresis to prevent oscillation. A goal is **data** — adding one recompiles nothing |
| **Symbolic planning** | State-space task planning. The domain is exported as valid, versioned **PDDL** ([`tools/pddl/`](tools/pddl/)) — a single compiler emits both the text a third-party planner reads *and* the domain the game executes. **GBFS** search with relevance-based action filtering |
| **Replanning** | Execution monitored at 2 Hz across 10 triggers; plan repair rather than full replanning; traceable decision journal |
| **Partial observability** | An agent cannot reason about what it has not observed. Field of view, line of sight, auditory perception |
| **Belief states** | Per-agent world model: a position ages, a trajectory is extrapolated, a memory decays, a hypothesis is invalidated |
| **Multi-agent coordination** | Belief sharing within communication range only; frontier-based exploration assignment; intent broadcasting |
| **A\* navigation** | Costs weighted by **remembered** danger — the agent avoids what it *believes* is dangerous, not what actually is |

The central architectural point: **the decision logic never references the game engine.**
`Hordinary.Core` is a pure .NET project, testable without Godot; the engine layer is only a
renderer and a perception provider.

**609 tests**: 551 in C# (`xUnit`, ~44 s) run without the engine, against the shipped map and the
actual `config/` files — not fixtures — plus 58 in Python covering the language service, with no
network and no API key.

## The LLM translates, it does not decide

The player gives tactical orders in plain French — *"Avoid forcing doors"*, *"Regroup before entering
a building"*. A model translates them into a **closed grammar of five forms**; then **four
deterministic gates** decide, and only they: typed grammar validation, compilation into PDDL
preconditions, satisfiability proof via Fast Downward, and trajectory checking on every plan
produced. A model that gets it wrong breaks nothing — it gets caught.

Fifteen models were put through a **66-utterance corpus**, 26 of which *must* be rejected:
preferences disguised as rules, conditionals, a prompt injection, and orders the grammar cannot
express. Full write-up: **[`service/evals/README.md`](service/evals/README.md)** (in French).

| Model | Compiled | Correct refusals | **False accepts** | p95 |
|---|---|---|---|---|
| `deepseek/deepseek-v4.1-flash` | 80 % | 85 % | **0 %** | 2,771 ms |
| `mistralai/mistral-medium-3-5` | 95 % | 81 % | 8 % | 3,376 ms |
| `inference-net/schematron-v2-turbo` | 90 % | 8 % | **85 %** | 6,201 ms |

The deciding column is **false accepts**, not compilation. `schematron-v2-turbo` compiles 90 % of the
corpus and would top a dishonest table: it translates *everything*, including the corpus's prompt
injection. That is the justification for the column — and it turned up on its own in the sample.

Three findings only execution produced: the quality ceiling was **the prompt, not the model** — 163
translatable utterances refused against 29 mistranslated, and a rewrite that took one same model from
42 % to 78 % compiled, at a measured cost of 4 points of false accepts; **latency decided
before accuracy** (the best rates of the first generation came with p95 of 22 s and 267 s); and three
of the newest models in the catalogue **refuse to have reasoning disabled**, which rules them out of
real-time use.

## Getting started

```bash
dotnet build Hordinary.sln
dotnet test Tests/Hordinary.Tests.csproj   # 551 tests, no Godot required
```

The language service is optional and ships its own suite (Python 3.12):

```bash
cd service
python -m venv .venv && ./.venv/Scripts/python.exe -m pip install -r requirements.txt
./.venv/Scripts/python.exe -m pytest tests/ -q          # 58 tests, offline
```

Then open the folder in **Godot 4.7** and run `Scenes/Main.tscn`.

Pick your agents, pick a mission, `START`. Keys `F1`–`F7` open the inspection overlays: mental map,
Utility AI scores, current plan, decision journal.

## No gameplay value is hard-coded

The planning domain, the goals, the agent profiles and the missions are JSON files under
[`config/`](config/). Adding a planner action or a Utility AI goal requires no recompilation.

## Going further

* **[Detailed architecture](docs/architecture.md)** (French) — every system, its design decisions,
  and its acknowledged limitations.
* **[Specifications](Specs/00-INDEX.md)** (French) — the 8 MVP deliverables and the extensions,
  each with its acceptance criterion.

## Stack

Godot 4.7 · C# / .NET 8 · xUnit · no external services

## License

This repository is **dual-licensed**:

| Scope | License |
|---|---|
| Source code (`Hordinary.Core/`, `Scripts/`, `Tests/`, `config/`) | [MIT](LICENSE) |
| Assets and content (`Assets/`, missions, texts, `Specs/F-*.md`) | [All rights reserved](Assets/LICENSE) |

© 2026 Raphaël JANSON
