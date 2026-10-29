# Moving panels — solids that travel while they are still solid — design

**Date:** 2026-09-02
**Status:** designed
**Related:** `docs/superpowers/specs/2026-08-29-tile-panels-design.md` ·
`docs/superpowers/specs/2026-08-28-triggers-and-links-design.md` ·
`docs/balloon-game-gdd.md` §4.1, §4.2, §5.1, §6.1, §6.4, §8, §9.3 ·
`docs/level-editor-spec.md` §3, §4, §7

A panel today is a mask: its tiles are present or absent, and the switch takes one fixed step.
That builds a floor that falls away. It cannot build a wall that slides up out of the way while
still being a wall, a block that pops out of the ground and shoves the player back two tiles, or
a ceiling that closes. Those need a solid that is *solid while it moves*, which is the
moving-solid problem the tile-panels design explicitly deferred.

This design reverses that deferral for panels, and adds a travel verb to entities that reuses
what they already do.

---

## The problem

The tile-panels design deferred moving solids and was right to, for the case it was solving: a
tile that vanishes never pushes a body, so a disappearing floor needed no push resolution at
all. The reasoning does not extend to a panel that travels. A travelling panel meets bodies,
and every question it raises — does it carry, does it shove, does it crush, does it add energy
to the balloon — is a question the mask model has no answer for, because in the mask model a
tile is either there or not there and never in between.

A second attempt at this, earlier on 2026-09-02, added an `exit_dir` that moved *debris* rather
than the panel: collision ended on the switch step and the tiles were scenery from that instant.
That is the right shape for a panel that vanishes and the wrong shape for a panel that retracts,
because the whole point of a retracting wall is that it is a wall the entire way out.

Three constraints make this harder here than in an engine with kinematic bodies:

| Constraint | Consequence |
|---|---|
| Every collision query funnels through `TileGrid`'s `this[x, y]` indexer, which is cell-addressed | A solid at x = 12.4 cannot be expressed at all today |
| GDD §6.1: no accumulation, no wall clock, replay-identical on attempt 40 | Panel position must be a pure function of the step counter, as `EntityPath` already is |
| GDD §8: restart under 200 ms | Whatever state a moving panel has must reset in one pass, not be unwound |

## Decisions taken here

1. **A panel with a `path` is a moving solid; a panel without one is the mask it is today.**
   The presence of the field decides, so no shipped level changes meaning and there is no mode
   flag to get out of sync.
2. **A moving panel's tiles leave the grid at load** and travel with the panel, carrying their
   kinds. A spike on a moving panel is lethal the whole way.
3. **Offset is a pure function of `step - RunStep`**, evaluated the way `EntityPath` evaluates a
   saw. Nothing accumulates; a restart is `RunStep = 0`.
4. **Panel velocity is derived from two evaluations**, `(Offset(step + 1) - Offset(step)) / dt`,
   never stored and integrated.
5. **New link actions `run` and `return`.** `on`, `off` and `toggle` keep their present instant
   meaning for panels and entities both.
6. **Contact reuses the existing doors.** Shove-into-geometry is `Kill(DeathCause.Crushed)`;
   balloon-off-a-moving-face is `ApplyHit`; a stationary face is passive contact at ~0.5
   restitution.
7. **Entities gain the same two verbs**, driving the path machinery they already have. No new
   collision work: an entity that travels is an entity moving along a path, which is a saw.
8. **`exit_dir` stays, scoped to pathless panels.** A vanishing panel still needs a debris
   direction; a travelling panel does not, because its tiles are not debris.

### On lifting tiles out of the grid rather than rasterising the panel into it

The cheaper-looking option is to keep panels in the grid and move them a whole tile at a time,
which is how a mask can appear to travel. It was rejected in brainstorming (option A/B there),
and the reason it stays rejected is that quantised motion cannot honour §8's asymmetry: at any
sub-tile position the drawn panel and the collidable panel disagree by up to a tile, and for a
panel that kills, a disagreement in the wrong direction is a death the player could not read.
Lifting the tiles out costs a second cast source in the two sweeps; rasterising costs the
player's trust in what they are looking at.

### On reversing a recorded deferral

`docs/level-editor-spec.md` records moving solids as deferred, and the tile-panels design leaned
on that deferral to justify presentation-only debris. Both statements were correct for a panel
that vanishes and are now scoped to that case. Both documents get updated in the implementing
commits, and GDD §22 gets a dated row: "the world is static geometry plus hazards" is an
assumption `SweptAabb` and `SweptCircle` were written against, and it stops being true here.

---

## Architecture

### Data model

`LevelPanel` gains the fields an entity already has, with the same names and the same meanings,
because an author who can path a saw should not have to learn a second vocabulary:

```jsonc
{
  "id": "p01",
  "rect": [16, 0, 4, 1],          // authored position, in tiles
  "start_state": "on",            // unchanged
  "exit_dir": "down",             // pathless panels only — debris direction
  "path": [0, 3, 0, 0],           // FLAT x,y pairs of tile OFFSETS from rect. [] = no path.
  "path_mode": "once",            // once | loop | pingpong
  "speed": 6.0,                   // tiles per second
  "hold": 0.5,                    // seconds paused at EVERY waypoint, ends included
  "phase": 0.0                    // 0..1, desyncs two identical panels
}
```

Offsets, not absolute positions: `rect` stays the authored home, so moving a panel in the editor
does not invalidate its path, and `path: [0, 3, 0, 0]` reads as "up three, then home" wherever
the panel sits.

`SimPanel` gains config — `Points`, `Mode`, `Speed`, `HoldSteps`, `Phase` — and exactly two
pieces of run state: `RunStep` (the step its current travel began) and `Direction` (+1 running,
−1 returning). `Offset` and `Velocity` are derived per step and stored only as a convenience for
the same step's readers, the way `SimEntity.Position` and `KillEnd` already are.

`IsMoving => Points.Length > 1` is the whole discriminator. A pathless panel takes the existing
path through `PanelField.Apply` and the mask, untouched.

### `PanelPath`, not a change to `EntityPath`

`EntityPath.Evaluate` has no dwell concept, and entities have never needed one. Rather than add
a parameter to the function every hazard's position already flows through, `PanelPath.Evaluate`
is its own pure function with the same contract — waypoints, mode, speed, phase, elapsed steps
— plus `holdSteps`, and it delegates to `EntityPath` when `holdSteps == 0`. This is the same
reasoning that kept `SweptCircle` untouched when `SweptAabb` was written: the balloon's
guarantees should not be able to regress through an edit made for something else.

Dwell is expressed in whole steps, converted from seconds at load, so the cycle length is an
integer count of steps and two panels with the same config stay in lockstep forever.

### Collision: a second cast source

Both sweeps gain an optional `SolidField` beside the `TileGrid`:

- `SolidField` holds the moving panels' current rects and their per-step velocity, in list
  order — a `List`, never a `Dictionary`, for the reason every other field in the sim is one.
- `SweptAabb.Cast` gains a pass over those rects, cast in the panel's frame: the body's velocity
  minus the panel's, resolved against a static rect, then the panel's velocity added back. This
  is the standard kinematic-platform resolution and it reuses the AABB math already there.
- `SweptCircle` gains a circle-vs-rect case for the balloon. Its tile path is not touched.
- A hit against a moving panel reports the panel's index as a `carrier`, which is what lets the
  step after the sweep tell "stood on a moving floor" from "stood on the world".

Tunnelling is not reachable: a panel is at least one tile thick, and nothing in the game exceeds
~0.35 m per fixed step. This is asserted by a test, not by the argument.

### Order in the step

`SimWorld.Tick` runs panels before bodies, and resolves what the panels' motion did before
anything reads the result:

1. Advance moving panels: evaluate `Offset(step)`, derive `Velocity`, publish to `SolidField`.
2. Resolve panel-versus-body displacement (below).
3. Entities step (unchanged).
4. Player and balloon step, now sweeping against grid *and* `SolidField`.
5. Contact, hazards, exit, triggers — unchanged in order.

Panels move first for the same reason triggers evaluate last: a body must never be able to be
placed inside geometry that arrived in the same frame without the game noticing.

### Contact outcomes

**Carry.** A body whose sweep ended on a moving panel's upward-facing surface is displaced by
that panel's per-step motion. Displacement, not a change to `Velocity` — a player riding a
rising floor must jump exactly as high as one standing on the world (GDD §5.7's feel is tuned in
`PlayerTuning` and must not vary with what you are standing on).

**Shove.** A panel that moves into a body displaces it along the panel's motion axis, by the
overlap depth. The body's velocity is untouched: being pushed is a change of position, not of
momentum, and letting a panel inject velocity into the player is how a shove turns into a launch.

**Crush.** If the shove cannot place the body clear — static geometry, another panel, or the
room bound behind it — the body dies through `SimWorld.Kill(DeathCause.Crushed)`, the same door
`CrushBodiesInReturningPanels` uses. GDD §4.1's linked fate therefore holds with no new case.
As with the existing crush, the §8 fairness shrink is deliberately **not** applied: the hazard is
the panel arriving, not a hitbox the player was asked to read.

**The balloon.** A balloon meeting a panel face that is moving toward it goes through
`ApplyHit(HitSource.Panel, faceNormal, power, 0)`, with `power` derived from the panel's
speed along the normal through a coefficient in the tuning profile under
`Assets/StreamingAssets/levels/_tuning/` — a slider, never a literal, as everything in GDD §6.4
is — GDD §5.1's single function, setting velocity, never adding force. A
face that is stationary this step is passive contact and rebounds at the existing ~0.5
restitution (GDD §4.2), so a parked panel cannot be farmed for energy and a moving one is a
deliberate, authored launcher.

`HitSource.Panel` is a new enum member. §5.1's whole point is that a new verb is configuration:
nothing downstream of `ApplyHit` learns that a panel produced this hit.

**Hazard tiles.** `Hazards` gains a pass over moving panels' carried tiles at their offset
positions. A spike on a retracting wall kills for the whole retraction, which is the feature.

### Firing

`LinkAction` gains `Run` and `Return`; `TriggerFactory.ParseAction` maps `"run"` and `"return"`,
which are two of the strings the schema already reserves space for alongside `arm` and `fire`.

- `run` sets `RunStep = step + 1`, `Direction = +1`. Re-firing a panel already running is a
  no-op, matching `SetActive`'s existing "only a false-to-true edge re-arms" rule.
- `return` sets `Direction = −1` and rebases `RunStep` so the panel continues from where it
  currently is rather than snapping — the same correction `SimEntity.ArmedStep` exists to make.
- A panel with `path_mode: loop` or `pingpong` and `start_state: "on"` runs from load, as a saw
  does.
- `on` and `off` keep meaning "tiles present" and "tiles absent" for a moving panel too: `off`
  removes it from `SolidField` and from the view entirely — it is not a solid anywhere — and
  `on` restores it at its authored position with `RunStep` cleared. A moving panel is therefore
  switched by the same two words as a pathless one and *travelled* by two different ones, which
  is the whole reason `run` and `return` are separate actions rather than an overload.

Entities take the same two actions against `SimEntity`: `run` is `SetActive(true, step)` with
the path measured from that step — which is what `ArmedStep` already does — and `return` drives
the rewind that `SimEntity.Place`'s existing comment anticipates ("the phase-5 rewind will ask
for exactly that"). No entity collision work; an entity that travels is an entity on a path.

### Presentation

`PanelViewSync` keeps its per-cell mask watch for pathless panels — the cell-level edge model
earned itself and is not being revisited. Moving panels are drawn as their own quads, positioned
from `Offset`, and interpolated between fixed steps through `StepInterpolator` exactly as the
balloon is: the sim ticks at 60 Hz, the display does not, and sampling a moving solid raw on a
render frame judders for the reason CLAUDE.md already documents. Nothing the view computes is
readable by the simulation.

## The editor

- Panel waypoints reuse the entity waypoint gesture, including drag-to-move and the existing
  `_draggingWaypoint` handling, so the two objects are authored the same way.
- `PanelInspector` gains `speed`, `hold`, `path_mode` and the waypoint list, beneath the fields
  it has now. `exit_dir` is shown only when the panel has no path, because on a moving panel it
  means nothing and showing a dead control is worse than hiding it.
- `TriggerInspector`'s action buttons gain `run` and `return`.
- A moving panel is drawn at its authored position with its path overlaid, the same amber the
  panel already uses and the same line style an entity path uses.
- The link band added earlier the same day is untouched.

## Validation

New warnings in `LevelValidator`, all non-blocking:

- A panel with `path` but `speed <= 0` — it will never move.
- A panel whose path leaves the room entirely with `path_mode: pingpong` — it will return from
  outside the room, which is legal but almost never intended.
- A link with action `run` or `return` targeting something with no path.
- A moving panel overlapping another moving panel's swept rect — two solids that will fight.
- `exit_dir` on a panel that has a path — the field is ignored there.

## Testing

Everything below is EditMode, plain C#, no scene:

- `PanelPath.Evaluate` is a pure function: same inputs, same output, asked out of order.
- Dwell: a panel with `hold` sits still for exactly the expected step count.
- Carry: a player standing on a rising panel rises with it and jumps to the same apex as one
  standing on the world.
- Shove: a panel moving into a standing player displaces them and leaves their velocity alone.
- Crush: a player between a moving panel and static geometry dies with `DeathCause.Crushed`, and
  the balloon dies in the same step (GDD §4.1).
- Balloon: a face moving toward the balloon produces an `ApplyHit`; a stationary face produces a
  passive rebound at ~0.5.
- Lethality: a spike tile on a moving panel kills mid-travel.
- Tunnelling: a body at maximum speed cannot cross a one-tile panel — the assertion behind the
  argument above.
- **Determinism: the replay-equality test is extended to include `RunStep`, `Direction` and
  every panel offset.** A moving panel is new simulation state, and CLAUDE.md requires it in the
  two-run comparison in the same commit that adds it. A run with a pathed panel, a carried
  player and a fast-forwarded clock must be bit-identical to the same run at normal speed.

## Not in this pass

- **Rotating or non-axis-aligned panels.** Everything here is an axis-aligned rect, which is
  what the two sweeps can express.
- **Per-leg speeds** (brainstorming option C). One `speed` per panel until an authored level
  wants otherwise.
- **Moving zones.** A kill or air zone that travels with a panel is a separate design; nothing
  currently needs it.
- **Panel-versus-panel collision.** Two moving panels pass through each other; the validator
  warns rather than the sim resolving it.
- **Panels pushing entities.** A saw and a panel occupy the same room and ignore each other.

## Documentation to update in the same commits

- `docs/level-editor-spec.md` §3 (schema), §4 (tool table, panel inspector), §7 (validation),
  and the tile-panels "as built" section — its moving-solid deferral is now scoped to pathless
  panels.
- `docs/balloon-game-gdd.md` §9.3 (the trap vocabulary this adds) and a dated §22 row for the
  reversal.
- `CLAUDE.md`'s tile-panel notes, if the `SolidField` changes what "all collision funnels
  through `TileGrid`" means — it does.
