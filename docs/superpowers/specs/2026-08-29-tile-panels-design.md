# Tile panels — floors a trigger can take away — design

**Date:** 2026-08-29
**Status:** built (2026-08-29)
**Related:** `docs/superpowers/specs/2026-08-28-triggers-and-links-design.md` ·
`docs/balloon-game-gdd.md` §4.1, §8, §9.1, §9.3 · `docs/level-editor-spec.md` §3, §4, §7

Triggers and links shipped on 2026-08-28 and can arm entities and switch air zones. They
cannot touch geometry. GDD §9.3's first named betrayal trap — "a floor that falls away not to
kill you, but so you can't reach the balloon" — is therefore still unbuildable, and it is the
one the room's whole shape depends on.

---

## The problem

The trigger spec deferred tile toggling, and gave three reasons. Two have since weakened and
one still stands.

| Its objection | Where it stands now |
|---|---|
| "It puts mutation inside `TileGrid`, which is otherwise loaded once and read-only for the run" | Real, and this design keeps the loaded rows immutable — the mask is a separate layer |
| "It adds grid contents to the replay-equality comparison" | Overstated. What varies is a handful of panel flags, not the grid; the mask is derived from them |
| "It makes a link target a rectangle rather than an id, so `to[]` would need two kinds of entry" | **Stands.** Solved here by giving the region an id |

It also grouped tile toggling with moving-solid collision, which was **wrong for this case**. A
tile that vanishes never pushes a body — the body simply falls, and the existing swept
collision finds nothing where the floor was. Crushers and closing doors need a solid that
moves *into* a body; a disappearing floor does not. That grouping is what kept this feature
sitting behind a deferral it never belonged to.

One further fact makes this cheap: **every collision query in the simulation funnels through
`TileGrid`'s `this[x, y]` indexer** — `IsSolid`, `IsBlockingBalloon`, `IsBlockingPlayer`, and
every `Hazards` lookup. A mask consulted there covers the player, the balloon and the hazard
tests at once, from a single edit.

## Decisions taken here

1. **A panel is a named rect over authored tiles**, in a new `panels` array. Links target it by
   id, so `to[]` keeps exactly one kind of entry.
2. **A panel masks whatever tiles are in its rect** — solid, spike, one-way, rough. One rule.
3. **`start_state: "off"` means the tiles are hidden at load**, which makes an *appearing*
   platform the same object as a vanishing one, with no extra machinery.
4. **Re-solidifying kills whatever is inside it.** Balloon and player together (GDD §4.1).
5. **No built-in crumble warning.** A telegraph is authored from the link's `delay` plus a
   `visible: true` trigger.
6. **The loaded tile rows stay immutable.** The mask is a parallel layer, derived from panel
   state, rebuilt rather than accumulated.
7. **A vanishing panel's tiles visibly fall.** Presentation only: collision goes at the switch
   step, and the view drops debris quads that the simulation cannot observe.
8. **The balloon dies when it leaves the room**, as the player already does. Without this, a
   floor taken away under the balloon is a level that never ends.

### On masking rather than editing the grid

Writing `TileKind.Empty` into `_cells` and writing the old value back on restart would be
fewer lines. It would also make the level's geometry a mutable thing that the run can corrupt:
a restart that missed one cell leaves a room subtly different from the one on disk, and
nothing would catch it — the file still says `#`, and the level still loads correctly next
time. Keeping the rows immutable means the worst a panel bug can do is show the wrong tiles
this run, never edit the level.

The mask is also the cheaper reset. `Restart()` sets every panel back to `StartEnabled` and
rebuilds the mask from those flags in one pass over a handful of rects — no per-cell
bookkeeping, and nothing to leak into the 200 ms budget (GDD §8).

### On killing rather than blocking

A panel that refuses to close while the player stands in it is a trap the player can defuse by
standing still, which is the opposite of what GDD §3's tug-of-war wants. A panel that closes
*through* a body leaves that body inside solid geometry, where swept collision has no defined
behaviour and the player can end up stuck in a wall. Killing is the only one of the three that
is both unambiguous and consistent with the genre: linked fate already means the balloon and
the player die together, and being crushed reads instantly.

The check runs **only on the step a panel turns on**, against the two bodies, so it costs
nothing on an ordinary step.

---

## Architecture

### Data model

```json
"panels": [
  { "id": "p01", "rect": [12, 4, 6, 1], "start_state": "on" }
]
```

- `rect` is `[x, y, w, h]` in tiles, tile-snapped, exactly as a zone's is. It arrives as
  `float[4]` because `JsonUtility` has no int arrays worth using, is read through
  `ZoneGeometry.RectOf` like every other rect in the schema, and is rounded to a `RectInt` once
  at load — the mask indexes cells, so a half-tile panel is not a thing that can exist.
- `start_state` is `on` (tiles present) or `off` (tiles hidden), matching the zone vocabulary
  rather than the entity's `active | dormant` — a panel is a region, not an actor.
- Ids are `pNN`, allocated lowest-unused, as `zNN` and `tNN` are.

`LevelData` gains `LevelPanel[] panels`, and `LevelValidator.ValidateIds` registers panel ids
alongside entity, zone and trigger ids so duplicates and dangling link targets are caught by
the code that already does that job.

### The mask

`TileGrid` gains a `bool[]` mask parallel to `_cells`, and the indexer's getter returns
`TileKind.Empty` for a masked cell. The out-of-bounds branch is untouched: outside the room
still reads `Solid`, so a runaway body still cannot leave the world.

```csharp
public TileKind this[int x, int y]
    => InBounds(x, y)
        ? (_masked[y * Width + x] ? TileKind.Empty : _cells[y * Width + x])
        : TileKind.Solid;
```

Two methods maintain it: `SetMasked(RectInt, bool)` and `ClearMask()`. Both bump `Version`,
which is what `TileViewSync` already watches — so the drawn room follows a panel with no view
work at all.

The setter — the editor's paint path — writes to `_cells` and is unchanged. Painting a tile
under an off panel therefore edits the level correctly while the panel continues to hide it.

### `PanelField`

New `Sim/Panels/PanelField.cs`, a plain C# class beside `AirField` and `TriggerField`:

```
SimPanel
  Id             string
  Rect           RectInt      (tile coordinates)
  Enabled        bool         (run state)
  StartEnabled   bool
```

`PanelField` owns a `List<SimPanel>` — ordered, never a dictionary — plus:

- `Apply(TileGrid grid)` — rebuilds the whole mask from the current flags. Called on load, on
  restart, and after any change. Rebuilding rather than toggling cells means two overlapping
  panels compose correctly: a cell is hidden if *any* panel covering it is off.
- `SetEnabled(string id, bool enabled)` — flips a flag and records the id in
  `TurnedOnThisStep` when it goes false-to-true.
- `TurnedOnThisStep` — a `List<int>` of panel indices, cleared at the start of each step.
- `Reset()` — every `Enabled` back to `StartEnabled`, `TurnedOnThisStep` cleared.

Overlapping panels are worth stating explicitly because the rebuild makes them free: a cell
under two panels is visible only when both are on. An author who overlaps two traps gets an
AND, which is the intuitive reading.

### Wiring and the kill

`TriggerField.Apply` already tries an entity id, then a zone id. Panels are tried last, so its
routing gains one branch and no new concept. `LinkAction.On` shows a panel's tiles, `Off`
hides them, `Toggle` flips — the same three verbs, applied to geometry.

**The kill is `SimWorld`'s, not `PanelField`'s.** GDD §4.1 is LOCKED and its rule is that
nothing else in the codebase may set a death flag. So `PanelField` only records what turned on,
and `SimWorld.Tick` — immediately after `Triggers.Step`, before `Clock.Advance` — tests those
rects against the player's box and the balloon's circle and calls `Kill(DeathCause.Crushed)`.
One explosion, one restart, both bodies, in the same step.

`DeathCause` gains `Crushed = 5`, and `BalloonLeftRoom = 6` for the bounds check below (the
enum currently ends at `PlayerFellOut = 4`; values are explicit because they are recorded, not
branched on). The death is not shrunk by the GDD §8 fairness margin: the
hazard here is the tile arriving, not a hitbox the player was asked to read, and a generous
inset would let a body sit visibly inside solid geometry and live.

### Order in the step

Unchanged from the trigger design: triggers evaluate last, effects land on the following step.
A panel therefore vanishes on the step after the player enters the region, and the crush check
runs in the same step as the switch that caused it. `delay: 0` still means next step.

### Falling debris

A floor that blinks out of existence reads as a rendering glitch; a floor that falls reads as a
trap. But a tile that falls *as a solid* is moving-solid collision, which is deferred and is
exactly the thing this feature does not need. So the fall is **presentation only**.

The rule the project already lives by makes this safe: a view reads the simulation and can
never write to it. `PanelViewSync` watches each panel's `Enabled` flag, and on a true-to-false
edge spawns one quad per non-empty tile in that panel's rect — the tile's own colour, at its own
position — then accelerates them downward, fades them, and destroys each one when it leaves the
room. The simulation has no idea any of this happened; collision ended on the switch step.

Debris animates on **render time**, not on the fixed step. That is allowed here and nowhere in
`Sim/`: views already sample render time for interpolation (`StepInterpolator.AlphaOf`), and the
determinism test cannot tell the difference because nothing reads back.

Two details that will otherwise be found by hand:

- **A restart must clear the debris.** `Clock.Step` running backwards is the signal — the same
  one `StepInterpolator.Sync` already uses to know a room reset. Debris surviving into the
  retry would be tiles falling through a floor that is back (GDD §8: the room resets, whole).
- **Debris draws behind the player and the balloon.** It is scenery at that point, and the
  balloon is always the highest-contrast thing on screen (GDD §13.1).

A panel turning back **on** does not animate. The tiles are simply there again, and the drama of
that moment is the crush, not a rising animation.

### Leaving the room

The player already dies 2 m outside the room in any direction — `SimWorld.StepPlayer` calls
`Kill(DeathCause.PlayerFellOut)`, described in the code as a safety net "should be impossible in
a walled level". Panels make it reachable on purpose, and it needs no change.

**The balloon has no equivalent check, and that is a soft-lock — but the original diagnosis of
it was wrong.** This section originally reasoned that with the floor masked away there is no
floor to hit, so an unfloored balloon falls forever and the level never ends. That is not what
happens: a ruling taken during implementation opened `TileGrid`'s out-of-bounds read on the
floor side only (`this[x, y]` returns `Empty` below `y = 0`, still `Solid` on the other three
sides), specifically so a level with no floor tile produces a Mario-style falling death instead
of landing on an implicit floor no one placed. A balloon with no floor beneath it now falls
through the bottom of the room and is caught by the border wall's usual `BalloonHitFloor` — the
game still ends, just later than it would with a real floor tile.

`StepBalloon` still gains the same bounds test the player already had, with the same 2 m margin
and all four directions checked, killing with `DeathCause.BalloonLeftRoom = 6`. But because only
the floor side of `TileGrid` was opened, the sideways and upward legs of that check are
unreachable in practice: the ceiling and side walls stay solid out to infinity, exactly as the
player's own check already was before this work, so a runaway body still cannot fly off sideways
or upward. Only a fall out the bottom — past whatever floor tiles exist, and then past the room's
open lower bound — can actually reach this check. It is kept for symmetry with the player's
check and because "impossible today" is not a promise about every future panel shape, not because
escaping upward is a real outcome to design for.

The margin is deliberately outside the room rather than at its edge — a bump can carry a body a
little past the wall and back, and killing at the boundary would make the walls feel like
hazards.

## The editor

`EditorTool.Panel = 9`, bound to `9` — free against the reserved set (gameplay holds
`Space W A D J R Esc` and the arrows; the shell holds `E` and `F`; 1-5, 6, 8 and `P` are taken).
Drag to create, drag the interior to move, a corner handle to resize, `Delete` or right-click to
remove. All of it reuses `ZoneGeometry` unchanged — panels are tile rects on the same grid as
zones and triggers, and a fourth geometry helper would be a fourth place for one bug.

Rules go in `EditorSession`, mirroring the zone and trigger sections exactly: `SelectedPanelId`,
`FindPanel`, `PanelAt`, `AddPanel`, `SelectPanel`, `SetPanelRect`, `SetPanelEnabled`,
`DeletePanel`, `NextPanelId`. `SelectPanel` clears the other four selections, and `TargetCentre`
and `TargetIdAt` learn about panels so the Link tool can wire one.

`PanelInspector.cs` in its own file — id, rect through `DevNumber`, a `start_state` toggle, and
delete. `LevelEditorController` gets the binding, the palette button, the tool case, the
inspector branch and the overlay, and nothing else.

**An off panel is drawn hatched over its tiles in the editor**, so the author can always see
geometry the player cannot. Colour is the trigger violet at low alpha — not hazard red, not
balloon yellow (GDD §13.1). In game there is nothing to draw: the tiles are simply absent, and
`TileViewSync` already knows.

## Validation

`LevelValidator` gains three warnings, beside the trigger ones:

- A panel nothing links from — authored and forgotten, the same mistake as an unlinked trigger.
- A zero-area rect.
- **A panel whose rect covers no non-empty tile.** It masks nothing, and it is nearly always a
  rect placed a few tiles off from the floor it was meant to take away. This is the lint that
  will actually catch mistakes.

Duplicate ids and links naming a missing panel are already handled by `ValidateIds` once panel
ids are registered there.

## Testing

| Test | Covers |
|---|---|
| `PanelFieldTests` | Mask applies and lifts; overlapping panels AND correctly; `Reset` restores start state; `TurnedOnThisStep` records only false-to-true edges |
| `TileGridTests` (extended) | A masked cell reads Empty; out of bounds still reads Solid; painting under a mask edits the cell without revealing it; `Version` bumps |
| `PanelCrushTests` | A panel switching on with the player inside kills both bodies in one step; switching on with the room clear kills nobody; a body that has left is safe |
| `DeterminismTests` (extended) | A room whose floor is taken away by a `once: true` trigger, run twice, bit-identical, and identical again after a restart |
| `BalloonBoundsTests` | A balloon below the room dies with `BalloonLeftRoom`; one above it dies too; one a metre outside a wall does not, so a bump past the edge is survivable |
| `EditorPanelTests` | CRUD, `pNN` allocation after deletion, selection exclusivity, link wiring and the cascade on delete |
| `LevelValidatorTests` (extended) | The three new warnings |
| `LevelFormatTests` (extended) | `panels` round-trips, including a file with none |

The falling debris has no automated cover — it is a view, like every other view in this
project — so it is judged by watching it: whether the fall reads as the floor giving way rather
than as a glitch, and whether the debris clears cleanly on a restart.

What the tests cannot judge is whether a floor vanishing under the player reads as
cruel-but-learnable or as unfair (GDD §8) — that is a playtest, and the first level built with
one should be played before the pattern is used more widely.

## Not in this pass

- Crushers, closing doors and moving platforms: still deferred, and still genuinely blocked on
  moving-solid collision (GDD §22, 2026-08-24).
- A crumble *warning* — the shake or flash before a floor goes. The tiles fall when they go,
  but the telegraph that they are about to is authored from the link `delay`.
- Per-tile panels — a panel is a rect. An author who wants a ragged edge uses several.
- Panels as link *sources*. A panel has no condition to fire on; that is a trigger's job.

## Documentation to update in the same commits

- `docs/level-editor-spec.md` §3 — the `panels` array; §4 — Panel on `9` in the tool table;
  §7 — the three new lints.
- `docs/balloon-game-gdd.md` §9.1 — that disappearing floors are built, and that crushers and
  closing doors remain deferred for the reason §22 gives; §4.3 — that the balloon now dies on
  leaving the room, not only on touching a floor.
- The 2026-08-28 trigger spec's "Not in this pass" list — tile-rect toggling is no longer
  deferred, and the reason it was grouped with moving-solid collision was wrong.

No §22 row: nothing LOCKED changes. GDD §4.1's linked fate is honoured by routing the crush
through `SimWorld.Kill`, and §9.1 already lists disappearing floors as a generic-tier hazard.
