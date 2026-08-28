# The level editor's zone tool — design

**Date:** 2026-08-28
**Status:** approved, not yet implemented
**Related:** `docs/level-editor-spec.md` §3, §4 (tool table), §7 (phases) ·
`docs/balloon-game-gdd.md` §6.2, §13.1, §13.3

Phase 4 of the level-editor spec's build order is "Trap logic — zones, triggers, links,
`start_state`, delays". This spec covers **zones only**. Links, triggers and delays are
deferred to their own spec; they need a way to draw a relationship, not a rectangle, and
that is a different problem.

---

## The problem

Air zones act on the balloon and cannot be seen, created, or edited.

`w1_02` carries `z_updraught` — a 6 × 10 tile rect at `[20, 1, 6, 10]` with `vy: +4.5`,
covering roughly a fifth of a room whose tiles are otherwise empty. Nothing drew it. The
balloon crossing its boundary was reported as "lightly bouncing in mid-air, as if hitting
invisible platforms", and that is exactly what it looked like: quadratic drag turns a
one-frame step in `v_air` from 0 to 4.5 m/s into roughly 9.6 m/s² of upward acceleration
arriving between two fixed steps, at an edge with nothing drawn on it.

`ZoneViewSync` (added 2026-08-28) now draws zones, which fixes the seeing. This spec fixes
the authoring: `EditorTool.Zone = 4` exists as a reserved enum value with no keybinding, no
dispatch case, no toolbar button and no inspector. The only way to change a zone today is
to hand-edit the level JSON.

## What was already decided

| Source | Decision |
|---|---|
| level-editor-spec §3 | Zone rects snap to **tile** (entities snap to half-tile, waypoints are free) |
| level-editor-spec §4 | Zone is tool `4`; the digit bindings must not shuffle |
| level-editor-spec §5 | Typed numbers go through `DevNumber`, shared with the tuning panel |
| GDD §6.2 | Zones are one system. Overlapping zones **sum** — so overlap is a supported design, not an edge case |
| GDD §13.1 | Reserved colours: hazard red and balloon yellow are never worn by anything else |
| GDD §13.3 | Placeholder blocks only until the control-scheme decision (§5.3) closes |
| CLAUDE.md | The editor and the game read the same JSON. Never a second format |
| CLAUDE.md | Rules live in tested plain C# classes; MonoBehaviours only sample input and draw |

## Decisions taken here

1. **All six zone types are authorable** (`air | heat | cold | kill | static | light`),
   even though only `air` has simulation behaviour. The inspector marks the other five
   **inert** rather than hiding them.
2. **Rect editing is direct manipulation** — interior drag moves, corner handles resize —
   not typed numbers alone.
3. **`N` arms a forced create**, so a zone can be drawn overlapping an existing one.
4. **Air zones are visible to the player**, not only to the author.
5. **Zone rules live in `EditorSession` and a new pure `ZoneGeometry`**, not in the
   controller.

### On authoring inert types

Only `air` is read by `LevelLoader.LoadZones`. A `heat` zone authored today parses, saves,
draws, and does nothing at all until inflation (GDD §7.2) is implemented — at which point
levels built around one will change behaviour. That is an accepted cost of authoring ahead
of the sim, and the reason the inspector must say so on screen rather than leaving the
author to infer it from the balloon not inflating.

---

## Architecture

The only real choice is where the rules live, and the project already answers it.

**Rejected — all of it in `LevelEditorController`.** That file is 1180 lines and has no
tests by design. Rect normalisation and handle hit-testing are precisely the kind of
geometry that a unit test catches instantly and a human playtest never does.

**Rejected — edit `SimWorld.Air` directly and serialise back out on save.** Two sources of
truth for the same data. The JSON is the source of truth; the moment the editor's model and
the level file diverge, the editor stops being trustworthy.

**Chosen — rules in `EditorSession`, geometry in a pure helper, grammar in the controller.**
This mirrors what already exists for entities (`EntityAt`, `PlaceEntity`, `MoveEntity`,
`SelectedEntityId`, `NextEntityId`) so a reader who knows the entity path knows this one.

### The live-push path already exists

`LevelLoader.ApplyEntities` calls `LoadZones` before `LoadEntities`, and
`ILevelEditorHost.SyncEntities` documents itself as refreshing "zones and entities from the
level being edited, leaving geometry, the spawns and the run alone". So a zone edit already
has a route into the running world that does not restart the room and does not snap the
balloon back to spawn. **Only the method's name is wrong.** Rename it `SyncDynamic`.

### Files

| File | Change |
|---|---|
| `LevelEditor/ZoneGeometry.cs` | **New.** Pure statics: `Normalise(a, b)` → tile-snapped `Rect`; `HandleAt(rect, world, radius)` → which corner or none; `Resize(rect, corner, world)`; the minimum-size clamp |
| `LevelEditor/EditorSession.cs` | `SelectedZoneId` plus `AddZone`, `ZoneAt` (spatial hit test), `FindZone` (by id), `MoveZone`, `ResizeZone`, `DeleteZone`, `NextZoneId` |
| `LevelEditor/ZoneInspector.cs` | **New.** The IMGUI block for the selected zone, kept out of the controller |
| `LevelEditor/LevelEditorController.cs` | `digit4Key` binding, toolbar button, `case EditorTool.Zone`, `ZoneTool(mouse, world)`, the zone overlay, `Delete` and right-click wired to zones |
| `LevelEditor/ILevelEditorHost.cs`, `Runtime/BootstrapEditorHost.cs` | `SyncEntities` → `SyncDynamic` |

The controller is already large. The inspector goes in its own file rather than growing it
further. Refactoring the *existing* inspector is out of scope — the goal is to not make the
problem worse.

## Data model

**No schema change.** `LevelData.LevelZone` already carries `id`, `type`, `rect[4]`,
`params { vx, vy, rate }` and `start_state`, and zones already round-trip safely:
`EditorSession.Flush()` overwrites only `Level.tiles` and returns the same `LevelData`, so a
zone survives an editor save today even with no tool to edit it.

- **Ids** follow the existing scheme: `NextZoneId()` returns the lowest unused `zNN`,
  copying `NextEntityId()` so ids stay short and predictable after deletions.
  `LevelValidator` already registers zone ids for duplicate detection.
- **Rects snap to whole tiles**, per level-editor-spec §3.
- **Minimum size is 1 × 1.** A drag that ends on the tile it began creates a 1 × 1 rect, not
  a degenerate one. A zero-area zone is unselectable and does nothing.

## Interaction

Tool `4`. `N` and `4` are both free: the editor uses Tab, Ctrl+S/Z/Y, digits 1/2/3/5, P, U,
G, H, T, L and Delete, and neither collides with gameplay's `Space W A D J R Esc` and arrows
or the shell's `E` and `F`.

| Input | Result |
|---|---|
| Left-drag on empty space | Create; on release the new zone becomes selected |
| Left-press inside a zone | Select, and drag to move — offset preserved, as `SelectTool` does |
| Left-press on a corner handle | Resize from that corner |
| `N` then drag | Force-create, even inside an existing zone. `N` toggles it; it also clears on release and on tool change. Not Escape — that is the shell's Back and is reserved. Status line reads `new zone: drag to place` |
| `Delete` | Delete the selected zone, extending the binding that already deletes a selected entity |
| Right-click inside a zone | Delete it, matching Path's right-click-deletes-waypoint |

Two rules carry the weight:

**Overlap selects the smallest zone containing the point.** Otherwise a small vent drawn
inside a room-wide thermal is permanently unreachable. This is the same reasoning already
written into `SelectTool`, where entities are hit-tested before the exit "because the gate
is large".

**Handles are drawn and hit-tested for the selected zone only**, with a radius in world
units sized to roughly 4 px. Every zone showing handles at once is unreadable in a room with
several.

Undo calls `Session.BeginEdit()` **once per stroke, not once per frame** — matching
`RectTool` and `SelectTool`. Every completed mutation then calls
`Host.SyncDynamic(Session.Flush())`.

## Inspector

Shown when a zone is selected:

- `id` — read-only
- `type` — dropdown over all six, with an **inert** marker on the five without sim behaviour
- `rect` — four `DevNumber` fields, so `"2."` and `".5"` parse as they do in the tuning panel
- `params` — by type: `air` shows `vx` / `vy`, `heat` and `cold` show `rate`, the rest none
- `start_state` — on/off toggle

## Rendering

Two separate paths, because they answer different questions.

**In play — `ZoneViewSync`, reading `World.Air`.** Unchanged by this work. `LoadZones` only
admits `type: "air"`, so the field holds exactly the zones that do something, and
`SetActive(zone.enabled)` is already the right rule: an `off` zone must not be shown to the
player as if it were live.

**In the editor — an overlay drawn by the controller, reading `Session.Level.zones`.**
This has to be a separate path rather than reusing `ZoneViewSync`, and the reason is
load-bearing: `LoadZones` drops every non-air zone, so the five inert types never reach
`AirField` and a view reading it could never draw them. Since all six types are authorable,
an overlay that cannot draw five of them is an overlay that cannot select or edit them
either. The editor edits `LevelData`, so the editor draws `LevelData`.

The overlay uses the controller's existing `DrawWorldRect`, alongside the grid, hitbox and
safe-area overlays. It draws every zone regardless of type or `start_state`, dims those set
`off`, and adds the selection outline and corner handles for the selected zone.
`ZoneViewSync` stays a plain view that knows nothing about selection.

Air zones are visible to the player, faint and translucent, in the fan's existing blue —
never a reserved colour (GDD §13.1), because wind is not lethal and the hazard hue means
"this hurts". An invisible force acting on the balloon is unreadable, and a player cannot
learn a room whose rules they cannot see.

## Testing

| Unit | Cases |
|---|---|
| `ZoneGeometry` | Normalisation from all four drag directions; tile snapping; the 1 × 1 minimum when a drag ends where it began; handle hit-testing with two corners close together; resize dragged past the opposite edge (dragging the top-right handle below the bottom edge re-normalises so the dragged corner becomes the new bottom, rather than yielding a negative height) |
| `EditorSession` | Add, select, move, resize, delete; `NextZoneId` reusing a freed id after a deletion; smallest-zone-wins hit-testing; an undo round-trip through `Flush` |
| `LevelFormatTests` | A zone authored, flushed, saved and reloaded compares equal — the test that catches the editor silently dropping a field |

No tests for `LevelEditorController` or `ZoneInspector`: MonoBehaviour and IMGUI, the same
boundary `ShellView` and the existing tools sit on.

Nothing here adds state to the simulation, so the replay-equality test needs no new
coverage. `AirField.Sample` is unchanged.

## Out of scope

- **Links, triggers and delays** — the rest of phase 4, deferred to their own spec.
- **Simulation behaviour for the five inert zone types** — `heat` and `cold` need inflation
  (GDD §7.2) first.
- **Zone visuals beyond a flat translucent rect** — GDD §13.3 holds placeholder art until
  the control-scheme decision closes. Direction indicators and particles come with the art
  pass.
- **Refactoring the existing entity inspector**, however tempting while working next to it.

## Risks

**Right-click deleting a large rect is easy to hit by accident.** Ctrl+Z covers it and it
matches the Path tool's existing meaning, so it is included — but `Delete` alone would be
the safer binding if this proves annoying in use.

**Authoring ahead of the sim.** Five of the six types do nothing. The inspector's inert
marker is the whole mitigation; if levels start appearing that lean on them, the honest fix
is to build the sim behaviour, not to quietly drop the marker.
