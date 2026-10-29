# In-Game Level Editor — Technical Spec

**Status:** Dev-only tool. Not a shipping feature.
**Purpose:** Collapse the author → test → tune loop for a trap-based physics platformer down to seconds.
**Audience:** Implementer (solo dev / Claude Code).

---

## 1. Why this is worth building

Three properties of the game make an in-game editor unusually cheap and unusually valuable:

1. **Fixed single-screen rooms.** A level is one screen. That makes it a small, fixed-size data structure with no streaming, no chunking, no camera authoring, and no scroll-region tooling. This is the single biggest enabler — the editor is tractable *because* of the camera decision.
2. **Deterministic fixed-timestep physics.** Because the sim is deterministic and driven at 60 Hz from a fixed step counter, input recording gives you replay, rewind and ghost trails almost for free.
3. **The game is 90% feel.** Balloon handling will be tuned by hundreds of small iterations, not designed on paper. Live tuning sliders inside the running game are worth more than any other feature on this list.

**Non-goal:** replacing the engine's scene editor for anything structural. This tool authors *level content only* — tiles, entities, zones, links. Prefabs, art, shaders and systems stay in the engine.

---

## 2. Scope guardrails

### Build
- Tile painting, entity placement, zone drawing, trigger linking
- Save / load / duplicate levels
- One-key Edit ⇄ Play toggle
- Time controls (pause, frame-step, slow-mo, rewind)
- Live physics tuning sliders
- Playtest metrics capture
- Save-time validation

### Explicitly do NOT build
- Any UI polish. Immediate-mode/debug UI throughout. Ugly is correct.
- Undo history beyond a flat 50-step stack
- Player-facing level sharing, workshop, cloud sync, accounts
- Multi-room or scrolling level support
- Mouse-free / gamepad editing
- Auto-solvers or difficulty estimators
- A second level format for shipping (see §9)

---

## 3. Level data model

### Grid

| Property | Value |
|---|---|
| Room size | 32 × 18 tiles |
| Tile size | 20 px |
| Base resolution | 640 × 360 (fixed 16:9) |
| Presentation | Integer scale only — ×2 → 720p, ×3 → 1080p, ×6 → 4K |
| World scale | 1 tile = 1 metre |

**Pixel-art caveat:** do **not** pixel-snap moving entities. At 20 px/metre a Nominal balloon falls 0.67 px per frame; snapping it to the pixel grid produces visible judder on exactly the object the player is tracking. Snap the tile grid only; render the balloon, props and moving hazards at sub-pixel positions with point filtering.

**Mobile safe area:** on touch builds the bottom-left and bottom-right corners are occluded by thumbs. Treat a ~5 × 5 tile block in each bottom corner as non-critical space — no exits, no single-frame timing hazards, no prop pickups the player must see mid-run.

Static geometry snaps to the tile grid. Entities snap to half-tile. Zone rects snap to tile. Path waypoints are free-position.

### Format

One JSON file per level, human-readable, one file per level, committed to git. Text diffs are a feature — you will want to see exactly what changed when a level stops being fun.

```
/levels/
  w1_01.json
  w1_02.json
  ...
/levels/_tuning/
  default.json          # physics profile
```

### Schema

```jsonc
{
  "id": "w1_03",
  "name": "The Radiator",
  "world": 1,
  "index": 3,

  "spawn": {
    "player": [3.0, 14.0],
    "balloon": [4.5, 6.0],
    "balloon_inflation": "nominal"      // shrivelled|slack|nominal|full|critical
  },

  "exit": {
    "pos": [29.0, 4.0],
    "shape": "ring",                    // ring|slot
    "diameter": 1.6,                    // metres — gates required inflation band
    "required_band": "full"             // derived; stored for lint + level select icon
  },

  // Static geometry. Run-length encoded rows, one string per row.
  // Chars: '.' empty  '#' solid  '=' oneway  '~' rough(pops)  '^' spike-up
  //        'v' spike-down  '<' '>' spike-side  'x' player-lethal only
  //        'o' rubber — bounces the balloon at a fixed speed, never ends the run
  "tiles": [
    "................................",
    "###...........................##"
    // ... 18 rows total
  ],

  "entities": [
    {
      "id": "e01",
      "type": "saw",
      "pos": [12.0, 9.0],
      "params": { "radius": 0.8, "speed": 3.0 },
      "path": [[12.0, 9.0], [20.0, 9.0]],
      "path_mode": "pingpong",          // pingpong|loop|once
      "phase": 0.0,                     // 0..1 offset so traps can be desynced
      "start_state": "active"           // active|dormant
    },
    {
      "id": "e02",
      "type": "prop_leafblower",
      "pos": [6.0, 14.0],
      "params": { "battery": 4.0 }
    },
    {
      "id": "e03",
      "type": "cat",
      "pos": [24.0, 15.0],
      "params": { "aggro_radius": 4.0, "patience": 1.5 }
    }
  ],

  "zones": [
    {
      "id": "z01",
      "type": "air",                    // air|heat|cold|kill|static|light
      "rect": [10, 2, 6, 8],            // x, y, w, h in tiles
      "params": { "vx": 0.0, "vy": -6.0 },   // v_air for air zones
      "start_state": "off"
    },
    {
      "id": "z02",
      "type": "heat",
      "rect": [18, 12, 4, 6],
      "params": { "rate": 0.33 }        // inflation units/sec (1.0 = full range)
    }
  ],

  "panels": [
    {
      "id": "p01",
      "rect": [16, 0, 4, 1],             // x, y, w, h in tiles — rounded to whole tiles
      "start_state": "on",               // on|off — whether its tiles are present at the start
      "exit_dir": "down"                 // down|up|left|right — which way the tiles leave.
                                         // Presentation only; absent reads as "down".
    }
  ],

  "links": [
    {
      "from": "trigger_a",
      "to": ["z01", "e01", "p01"],
      "action": "toggle",               // toggle|on|off|arm|fire
      "delay": 0.4
    }
  ],

  "triggers": [
    {
      "id": "trigger_a",
      "type": "player_region",          // player_region|balloon_region|timer|
                                        // lever|balloon_touches|player_grounded|
                                        // on_first_entry
      "rect": [14, 10, 4, 8],
      "once": true
    }
  ],

  "meta": {
    "par_touches": 7,
    "author_note": "The heat only kicks in after they commit to the right ledge.",
    "tuning_profile": "default"
  }
}
```

### Notes on the model

- **Zones are one system.** Fans, vents, thermals, gusts and the vacuum cleaner are all `type: "air"` with a different `v_air` vector — they compose correctly when they overlap because the drag equation already handles it. Do not add per-hazard wind code.
- **`start_state` + `links` is the whole betrayal toolkit.** Every Level Devil-style trap is "something dormant, armed by the player committing." `player_region` triggers with `once: true` are the workhorse.
- **`phase`** on periodic entities is essential and easy to forget. Without it every saw in the room moves in lockstep and levels look mechanical.

### Deviations from the sketch above — 2026-08-22, as built

- **`tiles` rows are plain, not run-length encoded.** The schema text said RLE; the example above did not. RLE saves a few hundred bytes on a file that is already tiny and destroys the readable diff this section calls a feature. Plain rows won.
- **`path` is a flat list of coordinates**, `[12, 9, 20, 9]` rather than `[[12,9],[20,9]]`. Unity's `JsonUtility` cannot serialise nested arrays. Read it as consecutive x,y pairs; the validator rejects an odd-length path.
- **`params` is a fixed superset, not a free-form object.** `JsonUtility` has no dictionaries, so every known entity parameter is a declared field and unused ones serialise as `0`. The cost is noise in the file; the benefit is that a load/save cycle cannot silently delete a parameter it does not recognise. If this grows unwieldy, that is the signal to add Newtonsoft.
- **Plain JSON, not `jsonc`.** No `//` comments — use `meta.author_note`.

The whole schema is modelled in code, including entities, triggers and links that no system reads yet. `JsonUtility` discards fields it has no member for, so a partial model would mean the editor deleting every entity in a file the moment it saved one.

---

### Inspector — 2026-08-24, as built

Shown on the right whenever an entity is selected. Fields are generated from
`EntityFields`, which lists the params each type actually uses, rather than from a panel
written per hazard — so a new entity type appears in the editor with no UI code, the same
way GDD §5.1 makes a new verb configuration rather than code. `EntityFields` lives in
`Sim/Level` beside `EntityFactory` because both answer "what does a saw have"; two lists
that could disagree would be one too many.

A param of `0` means *unset* — `JsonUtility` writes every field of `EntityParams` whether
the author touched it or not — so the inspector shows the type's default in brackets beside
an empty field instead of pretending the value is zero.

Entity edits are pushed into the running world immediately (`ILevelEditorHost.SyncEntities`),
so a dragged saw moves on screen as it is dragged, matching how painting a tile behaves.
That path reloads zones and entities only: geometry, spawns and the paused run are left
alone, because reloading the level on every drag would snap the balloon back to its spawn.

Typed numbers go through `DevNumber`, shared with the tuning panel, so "2." and ".5" mean
the same thing in both. It sits in the `Sim` assembly for the unglamorous reason that
`Runtime` depends on `LevelEditor` and not the reverse, making `Sim` the only assembly both
can see.

---

### The manifest — 2026-08-24, as built

`levels/index.json` declares which levels are in the game and in what order:

```json
{
    "version": 1,
    "worlds": [
        { "id": "w1", "name": "The Living Room", "levels": ["w1_01"] }
    ]
}
```

**The manifest is authoritative for ordering.** The `world` and `index` fields inside each
level file are advisory and lint-checked against it; nothing reads them to decide what comes
next.

**A level on disk but absent from the manifest is a draft** — the editor opens it, the game
does not list it. That is the capability a derived list cannot offer, and it is why this file
exists rather than the game scanning the directory.

`L` opens the level browser: open, create, reorder, unlist, and save the list. Unlisting
never deletes the file.

---

## 4. Editing

### Layout

```
┌─────────────────────────────────────────────┬──────────┐
│                                             │ INSPECTOR│
│                                             │          │
│              LEVEL CANVAS                   │ selected │
│              (1:1, no zoom for v1)          │ entity   │
│                                             │ params   │
│                                             │          │
├─────────────────────────────────────────────┤          │
│ PALETTE  [tiles][hazards][props][zones][fx] │          │
└─────────────────────────────────────────────┴──────────┘
```

Immediate-mode / debug UI only (Unity IMGUI, Godot Control nodes, or Dear ImGui). Zero layout work, zero art.

### Tools

| Tool | Key | Behaviour |
|---|---|---|
| Paint | `1` | Click-drag paints selected tile. Right-drag erases. |
| Rect fill | `2` | Drag a rectangle of the selected tile. |
| Select/move | `3` | Click entity to select. Drag to move. Shift-click multi-select. Also picks up the **player and balloon spawns** and the exit — tested in that order, since the gate is large enough to swallow a marker sitting in the doorway. A dragged spawn moves the frozen body with it, so the body is the marker; coordinates are typable in the inspector. A level with no `spawn` block still shows both markers, at the loader's own defaults. |
| Zone | `4` | Drag empty space to create, drag a zone to move, drag a corner handle to resize. `N` toggles a forced create so an overlapping zone can be drawn. `Delete` or right-click removes one. Type, rect, `params` and `start_state` in the inspector. |
| Path | `5` | With an entity selected, click to append waypoints. Drag a handle to move one, right-click to delete. Handle 0 is the entity and moves the whole trap. |
| Link | `6` | Click a trigger then a target to wire it, right-click an arrow to unwire it. With a source selected, *drag* a band instead to wire everything it touches in one edit; right-drag unwires the same way. Targets append to one link per source. |
| Place | `P` | Click drops the palette entity at the cursor. Numbered 7 internally so the reserved digit bindings above do not shuffle. |
| Trigger | `8` | Drag empty space to create, drag a trigger to move, drag a corner handle to resize. `Delete` or right-click removes one. Type, rect, `once`, `visible`, `seconds` and the outgoing link in the inspector. |
| Panel | `9` | Drag a rect of tiles a link can take away. Hatched when it starts hidden. |
| Eyedropper | `alt` | Held — picks the tile/entity under the cursor. |

### Editing keys

| Key | Action |
|---|---|
| Arrows | Nudge selection by half-tile (`shift` = 1/8 tile) |
| `Ctrl+D` | Duplicate selection in place |
| `Ctrl+C/V` | Copy/paste (paste at cursor) |
| `Delete` | Delete selection |
| `Ctrl+Z / Ctrl+Y` | Undo / redo (flat 50-step stack, whole-level snapshots — the level is small, don't be clever) |
| `Ctrl+S` | Save |
| `L` | Open the level browser (open, create, reorder, unlist) |
| `G` | Toggle grid overlay |
| `H` | Toggle hitbox overlay (shows the asymmetric player-generous / hazard-stingy volumes) |
| `T` | Toggle mobile safe-area overlay (thumb-occluded bottom corners) |
| `I` | Flip the inspector to the other edge, to reach what it is covering |

---

## 5. The play loop

This is the actual product. Everything above is data entry.

| Key | Action |
|---|---|
| `Tab` | Toggle Edit ⇄ Play. Play starts instantly from current unsaved state. |
| `R` | Restart level (must be < 200 ms, same as shipping) |
| `P` | Pause / resume — **not `Space`**, which is Jump in gameplay |
| `.` | Frame-step (one 60 Hz tick) while paused |
| `-` / `=` | Slow-mo 0.25× / 0.5× / 1× |
| `Q` | Rewind 1 s. Hold to scrub backwards. |
| `Ctrl+click` (in play) | **Teleport-test:** move player + balloon to cursor and continue. Test the back half of a level without playing the front half. |

**Keybind collision rule:** editor keys must never shadow gameplay keys. Gameplay reserves `Space` `W` `A` `D` `J` `R` `Esc` and the arrow keys. `R` is intentionally shared — it means "restart" in both contexts. Every other editor binding must avoid that set.

### Live edit during play

While paused mid-run, the select/move tool stays active. Drag a spike, nudge a platform, change a slider — then resume without restarting. For a trap game where the question is always *"is this two tiles too far left?"*, this is the difference between a 30-second iteration and a 3-second one.

### Rewind implementation

Do **not** snapshot game state. The sim is deterministic and fixed-step, so:

- Record `(frame_index, input_bitmask)` for the run — a few KB for a whole level.
- Rewind to frame N = reset to spawn, replay N frames with input playback, with rendering disabled. At 60 Hz a 20-second rewind is ~1200 steps, which is a handful of milliseconds.
- Free consequence: **every playtest is a replay file.** Save them.

### Ghost trail

After each attempt, draw the balloon's path from the previous 3 attempts as coloured dotted lines, with a marker at the pop point. Editor-only — this is exactly the prediction aid the shipping game deliberately does not have, and it is invaluable when authoring.

---

## 6. Live tuning

A collapsible panel exposing the physics constants as sliders, applied immediately, with **Save profile / Load profile / Reset to default**.

Minimum set:

- `gravity`
- `drag_kx`, `drag_ky`
- `restitution_wall`, `restitution_player_passive`
- `bump_velocity`, `bump_inherit_y`, `bump_inherit_x`
- `palm_speed`, `palm_cooldown`, `spike_speed`, `spike_cooldown`
- `aim_offset_max_angle`
- `inflation_rate_heat`, `inflation_rate_cold`, `inflation_rate_pump`, `leak_rate`
- Per-band: `radius`, `gravity_mult`, `restitution`, `fragility_tier`
- `input_buffer_frames`, `min_bounce_speed`, `max_speed`
- Player: `run_speed`, `jump_velocity`, `coyote_frames`

Profiles are JSON in `/levels/_tuning/`. A level may reference a non-default profile via `meta.tuning_profile` — useful for prototyping a world that wants different handling before you commit to it globally.

**This panel is the highest-value item in the spec. If you build nothing else from §5–§8, build this.**

---

## 7. Validation (runs on save)

Cheap to write, saves hours. Emit warnings in a panel, never block saving.

- No player spawn / no balloon spawn / no exit
- Entity or spawn outside room bounds
- Link or trigger referencing a missing id
- Path with fewer than 2 waypoints on a moving entity
- Exit `required_band` unreachable: no heat zone, cold zone or pump present that could get the balloon from `spawn.balloon_inflation` to the required band
- Balloon spawn overlapping solid geometry or a hazard
- Two `once: true` triggers with identical rects (usually a duplication mistake)
- Trigger type parsed but not simulated (counted, never blocked)
- Timer trigger with no `seconds` set — it can never fire
- `player_region` / `balloon_region` trigger with a zero-area rect
- A trigger nothing links from
- Link action parsed but not simulated
- `par_touches` unset
- Panel with a zero-area rect
- Nothing links to a panel — it can never switch
- Panel rect covers no tiles — it would hide nothing

Do **not** attempt automated solvability checking. It's a research project and it will be wrong.

---

## 8. Playtest metrics

Every play session appends a row to `/levels/_metrics/<level_id>.csv`:

```
timestamp, outcome, duration_s, touches, inflation_at_end, death_x, death_y, death_cause
```

Then two views in the editor:

- **Death heatmap** — dots at every death position, overlaid on the level. After 20 attempts you can see exactly which trap is doing the work and which is decorative.
- **Touch/time distribution** — a text summary (min/median/max) so `par_touches` is set from evidence, not vibes.

~40 lines of code. Disproportionately useful for a difficulty-tuned troll game.

---

## 9. Shipping build

- Strip the editor behind a compile define (`LEVEL_EDITOR`). Strip the metrics writer too.
- **Keep the loader.** The shipping game reads the exact same JSON. Never maintain two level formats — the moment they diverge, the editor stops being trustworthy.
- Ship levels as a single bundled/packed manifest if load time matters, but generate it from the JSON as a build step. JSON stays the source of truth.

**Forward compatibility note (build nothing now):** because a level is a small JSON blob, a future player-facing "share a level" feature is just base64 of a gzipped file — a copy-pasteable code. Level-sharing is a strong viral vector for this genre. Don't build it, but don't paint yourself into a corner: keep levels self-contained, avoid absolute paths, and don't reference anything outside the file.

---

## 10. Build order

Each phase should be independently useful. Don't start phase N+1 before N is being used to make real levels.

| Phase | Contents | Rough size |
|---|---|---|
| **1 — Author** | Grid, tile painting, entity placement, save/load, `Tab` play toggle, `R` restart | 1–2 days |
| **2 — Tune** | Live tuning panel + profile save/load | 0.5 day |
| **3 — Inspect** | Inspector panel, entity params, paths, `phase`, multi-select, copy/paste, undo | 1 day |
| | *Built 2026-08-24: inspector, params, paths, phase, retype, start_state. Multi-select and copy/paste still outstanding.* | |
| **4 — Trap logic** | Zones, triggers, links, `start_state`, delays | 1 day |
| | *Built 2026-08-28: triggers, links, delays and the spike bar. See "Triggers and links — 2026-08-28, as built" below.* | |
| **5 — Time** | Pause, frame-step, slow-mo, input-recording rewind, ghost trails, teleport-test | 1 day |
| **6 — Feedback** | Validation lint, metrics capture, death heatmap | 0.5 day |

Phases 1 and 2 alone already pay for the whole tool.

Zones and `start_state` are built (2026-08-28). Triggers and links are built too, later the
same day — the "way to draw a relationship rather than a rectangle" this section used to say
they were waiting on turned out to be small: click a trigger, click a target, drag nothing.

All six zone types are authorable, but `LevelLoader.LoadZones` reads only `type: "air"` — the
other five parse, save and draw while doing nothing. The inspector marks them inert. When
inflation (GDD §7.2) lands, `heat` and `cold` acquire behaviour and any level already using
one changes.

### Triggers and links — 2026-08-28, as built

Two new schema fields ride in with triggers. `visible` defaults to `false`: GDD §9.3's
workhorse is an *unmarked* region that fires when the player commits, so invisible is the
default a level falls into rather than something an author has to opt into. `seconds` is
timer-only and is converted to whole fixed steps at load (`SimClock.StepsIn`) — the region
types ignore it — so nothing multiplies a fractional second during a run, the same reasoning
§6.1 already applies to `delay`.

Of the schema's seven trigger types, three are simulated: `player_region`, `balloon_region`
and `timer`. The other four — `lever`, `balloon_touches`, `player_grounded`, `on_first_entry`
— parse, save and round-trip like everything else this build does not understand yet; they
load inert and are counted (`TriggerField.UnknownCount`), never silently dropped. Of the five
link actions, `on`, `off` and `toggle` are simulated; `arm` and `fire` load inert the same way.
The inspector marks both kinds of inert declaration on screen rather than hiding them.

Firing is edge-triggered: a trigger fires on the false-to-true edge of its condition, never on
every step the condition holds, the same shape `ScreenStack.ObserveWin` already uses and for
the same reason — a level-triggered check is the one that shipped broken once already. `once:
true` latches after its first fire; `once: false` re-fires on every re-entry, which is what a
switch meant to stay live wants.

**Effects land on the step *after* the trigger fires — `delay: 0` means next step, not this
one.** `TriggerField.Step` runs last in `SimWorld.Tick`, after contact and the exit check, but
"last" still is not "immediate": a trap armed by the player's own movement must not be able to
materialise inside the body that armed it, in the frame it armed it — that would be a death
with no frame in which it was avoidable (GDD §8). Queuing every fired link for
`step + 1 + delaySteps` rather than applying it in place is what makes `delay: 0` a fairness
floor instead of an instant kill.

A fired link is undone by `Restart()`: `SimEntity.StartActive` and `AirField.ResetStates()`
put every entity and zone back to what the level file declared before `Triggers.Reset()` runs,
so the room a retry drops you into is the room you died in, not the one your last attempt left
armed.

### Tile panels — 2026-08-29, as built

A panel is a named rect of tiles a link can take away — GDD §9.3's "floor that falls away not
to kill you, but so you can't reach the balloon", the first betrayal trap the trigger work
above could not build because it never touched geometry.

**A panel masks tiles; it never edits them.** `TileGrid` keeps the authored tiles exactly as
painted and layers a bitmask on top (`SetMasked` / `ClearMask` / `IsMasked`). The indexer
reports a masked cell as `Empty` no matter what is actually painted there; `BaseAt` bypasses
the mask and reads what was authored. `PanelField.Apply` rebuilds the whole mask from every
panel's current flag each time it runs rather than toggling cells one at a time — that is what
makes overlapping panels compose for free (a cell is hidden the moment any panel covering it
is off, with no ordering to get wrong) and keeps a restart's cost to one pass over a handful of
rects rather than per-cell bookkeeping (GDD §8's 200 ms budget).

**`ToRows` — the level's save path — serialises `BaseAt`, never the masked view.** This was a
mid-plan correction, not the original design: the editor's canvas and the running world share
one `TileGrid`, which is what lets a dragged tile show up immediately, but it also meant that
serialising the masked indexer baked whatever a panel happened to be hiding *at that instant*
into the file — Tab-into-play, an undo snapshot, and an ordinary save could each turn an
authored tile into a permanent hole. A panel hides tiles; it must never be able to delete them.

**Links switch panels the same way they switch zones and entities.** `TriggerField.Apply`
resolves a link's target id against entities first, then air zones, then panels last — a panel
is geometry, and nothing else in the level can share its id. `on`, `off` and `toggle` all
apply (`PanelField.SetEnabled`); `On` shows the panel's tiles, `Off` takes them away.
`start_state: "off"` (`LevelLoader.LoadPanels`) hides a panel from the first frame — an
appearing platform rather than a disappearing one, the same schema field doing double duty
zones already established.

**Tiles that return can crush.** `SimWorld.CrushBodiesInReturningPanels` runs once per tick,
after the mask is rebuilt from that step's link effects, and checks only the panels that just
switched *on* — tiles leaving never hurt anyone, so only the off-to-on edge is recorded
(`PanelField.TurnedOnThisStep`). Within a returning panel's rect it checks only the cells that
actually hold a tile; a panel whose rect covers empty space crushes nothing. A body caught
inside dies via `SimWorld.Kill(DeathCause.Crushed)` — the same single door every other death
goes through, which is precisely why GDD §4.1's linked fate still holds without a special
case here. The GDD §8 fairness shrink is deliberately not applied to a crush: the hazard is
the tile arriving, not a hitbox the player was ever asked to read, and shrinking it would let
a body sit visibly inside solid geometry and live.

**The room's floor is now open.** `TileGrid`'s out-of-bounds branch returns `Empty` below
`y = 0` and `Solid` on the other three sides — a mid-plan ruling, because a sealed box made a
falling death impossible even once a panel could remove the floor under someone. Both bodies
now die on falling out: the player via `DeathCause.PlayerFellOut` (pre-existing, and still a
safety net that should be unreachable in a walled level) and the balloon via the new
`DeathCause.BalloonLeftRoom`, reachable once a panel can take away a wall or the floor itself.

**Falling debris is presentation only, and tracks the mask per cell, not per panel.**
`PanelViewSync` compares `TileGrid.IsMasked` against the previous render frame for every cell
in the room; a cell that went visible-to-hidden spawns one falling quad, whatever combination
of panels caused it. An earlier per-panel model was tried and discarded — with overlapping
panels, "which panel hid this cell" has to reason about what every other panel did and when,
and that reasoning kept being wrong. Nothing here is readable by the simulation, so this is
also the one place in the presentation layer allowed to use `Time.deltaTime` (the same
exception `StepInterpolator` and `TimeControl` already carry) — determinism is untouched
because the sim never observes it.

**Editor:** `9` is the Panel tool — drag a rect of tiles, same gesture as Trigger on `8`.
Selecting a panel shows its `id`, `rect`, `start_state` and `exit_dir` in the inspector
(`PanelInspector`). A panel is drawn amber (not the trigger's violet, a hazard's red, or the
balloon's yellow — GDD §13.1) and hatched when `start_state` is `"off"`, so the author can see
geometry the player will not.

### Panel exit direction, and banding a link — 2026-09-02, as built

**`exit_dir` says which way a panel's tiles leave, and it is presentation only.** A wall that
retracts upward and a floor that drops are the same switch: the mask changes on one fixed step
either way, collision ends on that step, and nothing under `Sim/` reads the field during a tick
(`SimPanel.ExitDir` is load-time config, untouched by `Reset()`). `PanelDirections.Parse`
answers `down` for an absent or unrecognised value, so every level authored before the field
existed keeps falling; `LevelValidator` warns about a value nothing can draw rather than
refusing to load it, because a half-finished level must always be saveable.

**The per-cell mask edge stayed; only the lookup is new.** `PanelViewSync` still asks
`TileGrid.IsMasked` per cell rather than per panel — the reasoning that killed the per-panel
model has not changed. What a cell now needs is a *direction*, so `DirectionAt` traces the cell
back to the first panel in level order that covers it and is in the state which caused the
edge. Where panels overlap that is a tie broken by file order; the view is its only reader, so
nothing about the simulation depends on the answer.

**Tiles that come back slide in, and the slide is deliberately too short to be fair.** A
returning tile is solid, lethal and crushing from the switching step (`CrushBodiesInReturningPanels`),
so any arrival animation is showing empty space where a solid already is. The arrival quad
therefore covers 1 tile in 0.1 s — six fixed steps, short enough that the gap cannot be acted
on. Making it honest instead (delaying the sim's switch by the slide) was considered and
rejected: it adds per-panel countdown state to the tick, and it retimes every level already
authored against the instant switch.

**Wiring a row is a drag, not twelve clicks.** With a trigger selected, the Link tool's press
is held until release: a drag under 0.35 m is the old click, anything longer is a rubber band.
`EditorSession.TargetIdsIn(Rect)` answers with the ids the band touches in the same precedence
order a single click resolves — entities (by point), then zones, triggers and panels (by rect
overlap, because dragging across a row clips its ends far more often than it encloses them) —
so which gesture the author used never shows up in the file. The whole band is one
`BeginEdit`: a band over the wrong row is one undo back, not one per saw. Right-drag unwires
by the same rule. This is authoring only; there is no group object in the schema, because a
link already names as many targets as it likes.
