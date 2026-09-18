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

**606 tests**: 548 in C# (`xUnit`, ~26 s) run without the engine, against the shipped map and the
actual `config/` files — not fixtures — plus 50 in Python covering the language service, with no
network and no API key.

## Getting started

```bash
dotnet build Hordinary.sln
dotnet test Tests/Hordinary.Tests.csproj   # 548 tests, no Godot required
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
