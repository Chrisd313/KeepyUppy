# Roadmap

The phase plan lives in [`docs/balloon-game-gdd.md`](docs/balloon-game-gdd.md) §20; this file is the short version, kept current. Each piece of work goes spec → plan → build, and the specs and plans are in [`docs/superpowers/`](docs/superpowers/).

## Built

- **Simulation core** — hand-integrated, deterministic balloon physics at 60 Hz; player movement; the single `ApplyHit` path for jump-bump, palm and spike; linked-fate popping; instant restart. Covered by EditMode tests, including a replay-equality determinism test.
- **Level editor** — in-game, dev-only: tile painting, entity placement, save/load to JSON, `Tab` play toggle, live tuning panel.
- **Level systems** — inflation bands and size-locked exits, hazards, zones, triggers and links, tile panels, moving panels that carry and shove bodies.
- **Front end** — title, level select, pause and results, drawn from code with a bitmap font; fast-forward and slow-motion via extra/fewer fixed steps.
- **Progression** — clears, medals and best times saved and shown on level select and results.
- **WebGL build size** measured and reduced (GDD §17.2).

## In progress

- **Moving panels** — exit direction and remaining edge cases ([spec](docs/superpowers/specs/2026-09-02-moving-panels-design.md)).

## Next

- **Control-scheme experiment** (GDD §5.3) — author test levels for each `ControlProfile`, cross-play them, and lock one in. This blocks content past level 10 and the art pass.
- **Props** — GDD §10.2's set, string and leaf blower first.
- **Leaks** (GDD §7.3).
- **Level select scrolling** — the list runs off-screen past nine entries.
- **Settings screen** — expose the GDD §16 accessibility options (slow motion, reduced screen shake, remapping).
- **World unlock gating** — the rule exists and is tested but nothing calls it yet.
- **Gamepad and touch input** — `IInputSource` implementations after keyboard tuning settles.

## Later

- **Content** — Worlds 1–4, about 48 levels, built against the world template (GDD §11.2).
- **Art and audio pass** — after the control scheme is locked.
- **Web release**, then mobile, then PC/Steam.

## Open questions

Tracked in GDD §21 — control profile, touch scheme, whether the balloon has a face, title, and story premise.
