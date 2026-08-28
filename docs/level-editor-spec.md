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

  "links": [
    {
      "from": "trigger_a",
      "to": ["z01", "e01"],
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
| Select/move | `3` | Click entity to select. Drag to move. Shift-click multi-select. |
| Zone | `4` | Drag empty space to create, drag a zone to move, drag a corner handle to resize. `N` toggles a forced create so an overlapping zone can be drawn. `Delete` or right-click removes one. Type, rect, `params` and `start_state` in the inspector. |
| Path | `5` | With an entity selected, click to append waypoints. Drag a handle to move one, right-click to delete. Handle 0 is the entity and moves the whole trap. |
| Link | `6` | Click source, then target. Drawn as an arrow. |
| Place | `P` | Click drops the palette entity at the cursor. Numbered 7 internally so the reserved digit bindings above do not shuffle. |
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
- `par_touches` unset

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
| **5 — Time** | Pause, frame-step, slow-mo, input-recording rewind, ghost trails, teleport-test | 1 day |
| **6 — Feedback** | Validation lint, metrics capture, death heatmap | 0.5 day |

Phases 1 and 2 alone already pay for the whole tool.

Zones and `start_state` are built (2026-08-28). Triggers, links and delays are not: they need
a way to draw a relationship rather than a rectangle, which is a different problem and gets
its own spec.

All six zone types are authorable, but `LevelLoader.LoadZones` reads only `type: "air"` — the
other five parse, save and draw while doing nothing. The inspector marks them inert. When
inflation (GDD §7.2) lands, `heat` and `cold` acquire behaviour and any level already using
one changes.
