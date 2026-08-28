# Triggers and links — design

**Date:** 2026-08-28
**Status:** designed
**Related:** `docs/level-editor-spec.md` §3, §4 (tool table), §7 (lints, phases) ·
`docs/balloon-game-gdd.md` §8, §9.2a, §9.3, §13.1, §18 ·
`docs/superpowers/specs/2026-08-28-zone-tool-design.md`

Phase 4 of the level-editor spec's build order is "Trap logic — zones, triggers, links,
`start_state`, delays". The zone tool built the first of those on 2026-08-28 and deferred the
rest with the note that they "need a way to draw a relationship, not a rectangle, and that is
a different problem". This spec is that problem.

---

## The problem

GDD §9.3 states the betrayal trap as a single pattern: **something dormant, armed by the
player committing.** Every world needs at least two, and §18 calls `start_state` + `links` +
`once: true` triggers "the entire betrayal toolkit".

Half of that toolkit is built. `SimEntity.Active` and `AirZone.enabled` are honoured, and
`EntityField.SetActive` is already commented as "the hook GDD §9.3 triggers will use". The
other half does not exist: `LevelTrigger` and `LevelLink` are modelled in `LevelData` purely
so `JsonUtility` does not delete them on save, and nothing reads either. A level cannot arm
anything, so a dormant hazard is a hazard that never happens.

The immediate ask is broader than the region trigger: spikes rising from the ground, fans
switching on, zones activating, saws appearing — and the target's own controls (a saw's path,
its phase) must keep working while a trigger governs whether it is running at all.

## What was already decided

| Source | Decision |
|---|---|
| GDD §9.3 | The trap pattern is `start_state: dormant` + a `player_region` trigger with `once: true` |
| GDD §9.2a | Entity position is a pure function of the step counter; anything that cannot be written that way carries explicit state and joins the replay-equality test |
| GDD §8 | Generous to the player, stingy to the level |
| GDD §13.1 | Reserved colours: hazard red and balloon yellow are worn by nothing else |
| GDD §22, 2026-08-24 | Moving-solid collision is deferred on design grounds, not effort |
| level-editor-spec §4 | Link is tool `6`; the digit bindings must not shuffle |
| level-editor-spec §7 | Lints already specified: link or trigger referencing a missing id; two `once: true` triggers with identical rects |
| CLAUDE.md | Rules live in tested plain C# classes; MonoBehaviours sample input and draw |
| CLAUDE.md | Any change that adds state to the simulation is covered by the replay-equality test in the same commit |
| zone-tool spec | Rect editing is direct manipulation; rules in `EditorSession`, geometry in a pure helper, grammar in the controller |

## Decisions taken here

1. **Retractable spikes are a new entity type, `spike_bar`** — not toggled tiles. Tile spikes
   stay static geometry.
2. **A visible trigger is the same trigger with a `visible` flag**, not a physical object.
   Pressure plates and levers are not built.
3. **Three trigger types are simulated:** `player_region`, `balloon_region`, `timer`. The
   other four load inert and are counted.
4. **Three link actions are simulated:** `on`, `off`, `toggle`. `arm` and `fire` load inert.
5. **Trigger rects are their own tool (`8`); wiring is the Link tool (`6`).**
6. **Triggers evaluate last in the step.** Effects land on the next step.

### On the spike bar rather than toggled tiles

Toggling a rect of tiles is the cheaper authoring story and the worse engineering one. It puts
mutation inside `TileGrid`, which is otherwise loaded once and read-only for the run; it adds
grid contents to the replay-equality comparison; and it makes a link target a rectangle rather
than an id, so `to[]` would need two kinds of entry. An entity costs one `EntityKind` value and
one row in `EntityFields`, and inherits `path`, `path_mode`, `phase` and `start_state` for
free — which is precisely the "the target's own controls still apply" requirement.

Falling floors and closing doors (GDD §9.1) do want tile-rect toggling eventually. They also
want moving-solid collision, already deferred. They belong in that session, not this one.

### On authoring inert types

`lever`, `balloon_touches`, `player_grounded` and `on_first_entry` parse, save, draw and do
nothing. So do `arm` and `fire`. The inspector says **inert** on screen and the validator warns,
rather than hiding them — the same call the zone tool made for its five unsimulated types, for
the same reason: a level authored around one will change behaviour when it lands, and the author
must be told at authoring time rather than left to infer it from nothing happening.

`lever` is the one worth naming. It needs a physical object with a position, a sprite and a
pressed state, plus a context-key interaction — a prop, not a trigger. GDD §9.1 notes pressure
plates are "the purest expression of §3's tug-of-war" because the balloon is too light to fire
one; that is a level-design payload worth its own session.

---

## Architecture

### The simulation

**New `Sim/Triggers/TriggerField.cs`**, a plain C# class with no scene dependency, held by
`SimWorld` beside `AirField` and `EntityField` and stepped by `Tick()`.

```
SimTrigger
  Id          string
  Kind        PlayerRegion | BalloonRegion | Timer | Unknown
  Rect        Rect          (tile-aligned, metres, bottom-left origin — as AirZone)
  Once        bool
  Visible     bool
  AtStep      int           (timer only; seconds converted at load)
  ---- run state ----
  WasInside   bool
  Fired       bool
```

**Firing is edge-triggered, never level-triggered.** A trigger fires on the false-to-true edge
of its condition; `WasInside` holds the previous value. `once: true` latches `Fired` and never
fires again; `once: false` re-fires on each fresh entry, so a `toggle` link flips back when the
player re-enters. This is the lesson `ScreenStack.ObserveWin` already paid for in the shell: the
one piece of decision logic that leaked into a view shipped broken because it tested a level
rather than an edge. The same shape, in a tested class, from the start.

A `Timer` fires once when `Clock.Step == AtStep`, regardless of `once` — a timer that fires
repeatedly is a period, and no level wants one yet.

`Unknown` never fires. It is counted and surfaced, matching `EntityField.UnknownCount`: a level
file outlives any one build, and an editor that silently drops what it cannot parse is not
trustworthy.

**Links** are resolved at load into a parallel list:

```
SimLink
  FromIndex   int        (index into the trigger list, resolved once at load)
  ToIds       string[]
  Action      On | Off | Toggle | Inert
  DelaySteps  int        (SimClock.StepsIn(delay), converted once at load)
```

`delay` is converted to whole fixed steps **at load**, so no float seconds value is ever
multiplied by a timestep during the run. When a trigger fires, each of its links pushes
`(applyStep, linkIndex)` onto a pending list; the list is drained in insertion order on the step
its entries come due. A `List`, never a `Dictionary` or `HashSet` — unordered iteration is on
CLAUDE.md's list of the things that break §6.1 silently.

**Targets are ids.** An id naming an entity routes to `EntityField.SetActive`; an id naming a
zone routes to `AirField.SetEnabled`. Both already exist. A fan is both an entity and a zone
with the same id, and `EntityField.Step` republishes its `AirZone` from `entity.Active` every
step — so setting the entity is sufficient and there is one code path, not a fan special case.

**Order in the step.** `SimWorld.Tick()` today runs: input, entities, player, balloon, contact,
verbs, exit. Triggers evaluate **last**, after `CheckExit` and before `Clock.Advance()`. Effects
therefore land on the following step.

That ordering is a fairness decision, not an implementation convenience. Evaluating triggers
early would let a spike bar armed by the player's own movement materialise inside the body that
armed it, in the frame it armed it — a death with no frame in which it was avoidable. §8's
generosity is meant to be felt and not noticed; this is exactly that kind of rule. It also
leaves the existing "hazards move before either body does" ordering untouched, so no current
behaviour shifts by a frame.

The consequence to document: `delay: 0` means *next step*, not *this step*.

### Determinism

`TriggerField` is the second system in the sim to carry real per-step state, after the lamp's
exposure counter. `WasInside`, `Fired` and the pending list are all mutable run state and all
must be cleared by `Restart()` — which is synchronous and must stay inside the 200 ms budget,
so clearing is a loop over a small list and nothing more.

`DeterminismTests` gains a level exercising all three in one run: a `once: true` region trigger,
a `once: false` one the input re-enters, and a link with a non-zero delay whose application must
land on the same step in both runs. Two runs, bit-identical output, in the same commit as the
feature. Without it the guarantee rots silently, which is the failure mode CLAUDE.md calls out
by name.

### The spike bar

`EntityKind.SpikeBar`, with a capsule kill volume from `Position` along `Angle` for `Length` —
the same `KillEnd` extension `Lamp` already performs, so `OverlapsCircle` and `OverlapsBox` need
no change at all.

- `KillsOnContact` — true. `KillsPlayer` — **true**. `Hazards.KillsPlayer` returns true for every
  tile spike; a spike bar that killed only the balloon would be a lie told by the same sprite.
- Params: `Radius`, `Length`, `Angle`, plus `Speed` for a moving one. Defaults in
  `EntityDefaults` beside the saw's and the lamp's.
- Everything else is inherited: `path`, `path_mode`, `phase`, `start_state`, and one row in
  `EntityFields` puts it in the inspector with no UI code — the §5.1 principle that adding a verb
  is configuration rather than code, applied to hazards.

A dormant entity is already skipped by `ResolveBalloon` and `ResolvePlayer` and parked at
`Points[0]` by `Place`, so "dormant spike bar underfoot, armed as the player commits" works the
moment a link can flip the flag.

## Data model

Two additive fields on `LevelTrigger`. Nothing else in the schema changes.

```json
{
  "id": "t01",
  "type": "player_region",
  "rect": [14, 10, 4, 8],
  "once": true,
  "visible": false,
  "seconds": 0.0
}
```

- **`visible`** defaults `false`, so every trigger authored today keeps its §9.3 meaning and no
  existing file changes behaviour. `true` draws the rect in game as well as in the editor.
- **`seconds`** is timer-only, converted to `AtStep` at load. A timer needs a time and the schema
  had nowhere to put one; overloading `rect[0]` was the alternative and is the kind of cleverness
  that makes a format unreadable.

`links[]` needs nothing: `from`, `to[]`, `action`, `delay` are already right.

`start_state` gains meaning it did not have. It was honoured but permanent; it becomes the
initial value of a flag a link can flip. No file rewrites — the flag was already read.

Ids follow the existing scheme: `NextTriggerId()` returns the lowest unused `tNN`, copying
`NextEntityId` and `NextZoneId`. The schema example's `trigger_a` is a placeholder, not a
convention.

## Validation

`LevelValidator` already registers entity and zone ids for duplicate detection, and
level-editor-spec §7 already specifies two of these lints. Added:

**Errors**

- A link's `from` naming no trigger, or a `to` entry naming no entity, zone or trigger
- A duplicate trigger id
- A `timer` trigger with `seconds <= 0`
- A region trigger with a zero-area rect (unselectable, and it can never fire)

**Warnings**

- A trigger nothing links from — authored and then forgotten is the common mistake
- Two `once: true` triggers with identical rects (level-editor-spec §7, verbatim)
- `action: arm` or `action: fire`, and the four unsimulated trigger types: *parsed, not simulated*

## The editor

`EditorTool.Trigger = 8` bound to `8`; `EditorTool.Link = 6` bound to `6`. Both are free against
the reserved set — gameplay holds `Space W A D J R Esc` and the arrows, the shell holds `E` and
`F` — and neither shuffles an existing digit.

**Trigger tool.** Drag empty space to create, drag the interior to move, drag a corner handle to
resize, `Delete` or right-click to remove. All of it reuses `ZoneGeometry` unchanged: tile snap,
handle hit-test, 1 × 1 minimum. Triggers are rects on the same grid as zones, so a second
geometry helper would be a second place for the same bug.

**Link tool.** Click a trigger, then click an entity or zone; the link draws as an arrow from the
trigger's centre to the target's. Clicking a further target on the same trigger appends to
`to[]` rather than creating a second link. Right-click an arrow deletes that link.

**Deleting a trigger cascades to its links.** An orphaned link is a validator error, and leaving
an author to clean up after their own delete is how a file rots.

**Rules in `EditorSession`**, mirroring the zone and entity paths exactly: `SelectedTriggerId`,
`AddTrigger`, `TriggerAt`, `FindTrigger`, `MoveTrigger`, `ResizeTrigger`, `DeleteTrigger`,
`NextTriggerId`, plus `AddLink`, `RemoveLink`, `LinkAt`. `LevelEditorController` is 1444 lines
and untested by design; id allocation, hit-testing and cascade-on-delete are what a unit test
catches instantly and a human playtest never does.

**Selection stays mutually exclusive.** Entity, zone, exit and now trigger — one at a time. That
invariant was established deliberately (commits of 2026-08-28); a fourth selection type ignoring
it would undo the work.

**`TriggerInspector.cs`**, its own file so the controller does not grow further: type, `once`,
`visible`, `seconds` for timers, and the outgoing links listed with `action` and `delay`
editable. Numbers through `DevNumber`, shared with the tuning panel, so `"2."` and `".5"` mean
the same thing in both. Refactoring the existing inspector stays out of scope.

**Live push.** `SyncDynamic` already refreshes zones and entities without touching geometry, the
spawns or the run. Triggers and links join it, so wiring a link shows up the moment it is wired,
the way dragging a saw does. Trigger run state resets on that sync — mid-edit, a fresh arm is
what the author wants.

## Rendering

In the editor, every trigger draws: `visible: true` filled, invisible outlined, so the author can
always see what the player cannot. In game, a new `TriggerViewSync` — mirroring `ZoneViewSync`,
which exists for the same reason, that an invisible field acting on a body reads as a bug —
draws only the `visible: true` ones.

Colour is violet: not hazard red, not balloon yellow (§13.1). Trigger rects are static, so no
`StepInterpolator` involvement; a trigger attached to a moving entity is not a thing this design
has.

Placeholder blocks only, per §13.3 — the art pass waits on the §5.3 control decision.

## Testing

| Test | Covers |
|---|---|
| `TriggerFieldTests` | Edge firing, `once` latching, `once: false` re-entry, timer at step, delayed application order, unknown types inert, `Restart` clearing run state |
| `DeterminismTests` (extended) | A level with both `once` modes and a delayed link, run twice, bit-identical |
| `EditorTriggerTests` | Create, move, resize, delete, cascade-to-links, id allocation after deletion, link add/remove/hit-test |
| `LevelValidatorTests` (extended) | Every error and warning listed above |
| `LevelFormatTests` (extended) | `visible` and `seconds` round-trip; a file with neither loads with the documented defaults |

What the tests cannot cover: whether a betrayal trap reads as cruel-but-learnable or as unfair.
That is §8's "never demand precise platforming and precise balloon-tracking in the same two
seconds", and it is judged by playing it. A level authored with a spike bar and a `once: true`
trigger needs playtesting before the pattern is used more widely — look for whether the arming
moment is legible in the frame it happens, and whether the one-step effect delay is felt.

## Not in this pass

- Pressure plates and levers (physical trigger objects)
- `balloon_touches`, `player_grounded`, `on_first_entry`, `lever`
- `arm` and `fire` link actions
- Tile-rect toggling: falling floors, closing doors, disappearing platforms
- Moving-solid collision, deferred on design grounds by GDD §22, 2026-08-24

## Documentation to update in the same commits

- `docs/level-editor-spec.md` §3 — the two new fields and an "as built" block on trigger and
  link semantics, beside the zone one; §4 — Trigger on `8` in the tool table; §7 — the phase-4
  row, and the new lints
- `docs/balloon-game-gdd.md` §9.2a — the spike bar, and the replacement of "nothing arms it yet,
  because triggers do not exist"
- `EntityKind`'s doc comment — the new kind

No §22 row is required. Nothing LOCKED changes; this completes §9.3 rather than contradicting it.
