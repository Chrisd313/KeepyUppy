# Menu-first boot, and an editor-authored shell — design

**Date:** 2026-08-25
**Status:** approved, not yet implemented
**Related:** `docs/superpowers/specs/2026-08-24-menus-and-level-list-design.md` · `docs/balloon-game-gdd.md` §3, §13.3, §15, §17.2, §22 · `CLAUDE.md`

---

## The problem

Three things, from playing the built shell.

**The menu has a level running behind it.** That was deliberate — a balloon bouncing behind the title was meant to sell the premise — and it reads as clutter. The title screen should be a title screen.

**There is no way out of a level.** Pause offers resume and nothing else. Once you are in a room you stay there.

**Menu layout lives in code.** Positions are float literals in `ShellView`; moving the title three pixels left means editing `x = 7f` and pressing Play. Dropping in artwork means writing more of the same. That is the wrong tool for a visual problem, and it makes the art pass a coding task.

Underneath all three: `SimBootstrap.Awake` builds the world, loads a level, starts ticking, and *then* installs the shell on top. Level loading is a startup step rather than something the menu asks for.

## Decisions taken

1. **Boot goes to the menu.** No world exists until a level is selected.
2. **Full teardown on quit.** Leaving a level destroys its world and views; re-entering builds fresh.
3. **Prefabs for the shell only.** Menu screens are authored in the Unity editor. Sim, levels and tuning stay code-built and file-driven.
4. **Prefabs load through `Resources`.** The bootstrap installs itself into an empty scene with no inspector references to hang prefabs from, so `Resources.Load` is the only zero-infrastructure route. Unity discourages it at scale; at four menu prefabs the objection does not bite, and it behaves identically on WebGL.
5. **The editor opens from level select via a dev key**, not a menu entry.
6. **Results offers next, retry and quit.**

---

## 1. Lifecycle

`SimBootstrap` splits along the line the current bug sits on: what always exists, and what a level needs.

**Always** — created once at boot, never destroyed:
camera, `ShellView`, shell input, `FileTuningSource`, `FileLevelSource`, the loaded `LevelIndex`.

**Per level** — created when a level starts, destroyed when it ends:
`SimDriver` (and therefore `SimWorld`), tile / entity / exit / balloon / player views, the debug overlay, the tuning panel, the level editor.

The per-level half lives under **one root `GameObject`**. Starting a level instantiates it; quitting calls `Destroy` on the root and nulls the reference. That is the whole teardown mechanism — no manual unhooking, no per-view cleanup to forget.

**Why teardown rather than hide-and-freeze.** A hidden, frozen world is invisible state, and invisible state is what produced the Results soft-lock: three correct components interacting through a flag nobody could see. A destroyed world cannot be accidentally ticking behind a menu. The cost is rebuilding on every level entry, which at 32×18 tiles is milliseconds.

**Determinism is unaffected, and slightly better for it:** every run now starts from a freshly constructed world rather than a `Restart()` on a reused one. The replay-equality test already covers both paths.

## 2. Screens are prefabs

Each screen is a prefab under `Assets/_Project/Resources/Shell/`, holding a `ScreenLayout` MonoBehaviour with serialised anchor `Transform`s and whatever `SpriteRenderer`s the artwork needs.

**Positions come from the prefab's transforms**, not from code. `ShellView` reads an anchor's position and draws dynamic text there; it never decides where anything sits.

Serialised references, not find-by-name: a renamed child breaks a name lookup silently at runtime, and breaks a serialised reference visibly in the inspector.

**The split between prefab and code is by lifetime, not by type:**

| Prefab | Code |
|---|---|
| Static labels, logos, backgrounds, decoration | Level names, touch counts, the cursor, anything that changes at runtime |

Dynamic text stays `TextView` quads, so `BitmapFont` keeps earning its place.

**What this costs, stated plainly.** Prefab YAML diffs badly and merges worse — with one author that is near-zero, and a real cost the day someone joins. Menu layout stops being reviewable in a diff: nobody can see in a pull request that the title moved. Accepted, because positioning artwork by editing float literals is worse.

## 3. Flow

```
Title ──confirm──► LevelSelect ──confirm──► Playing ──win──► Results
  ▲                    │  ▲                   │  ▲              │
  └──────back──────────┘  │                   └──back──► Paused │
                          └──────── quit ─────────────────┴─────┘
```

**Pause** offers resume, restart and quit to levels.

**Results** offers next, retry and quit. "Next" is answerable because the manifest declares an order — `LevelIndex` gains a `NextAfter(id)` query. **At the end of a world, Next is absent** and quit is the default; crossing into the next world is a progression question that belongs with save data in phase C, not here.

**Every one of those transitions is a rule**, so every one lives in `ScreenStack` or `LevelIndex` and is tested. `ShellView` only draws and dispatches. The Results soft-lock is the argument: the single decision that leaked into the untested view was the one that shipped broken, and the whole-branch review then found two more of the same shape in the same file.

## 4. The level editor

`LEVEL_EDITOR` builds only: pressing the dev key on a highlighted level in the level select opens that level in the editor instead of playing it.

Mechanically the editor is a *mode of a level session*, not a separate path — it needs the same world, views and camera. So it starts a session and hands the editor the level, rather than existing alongside one.

`Esc` from the editor tears the session down and returns to level select, the same as quitting a level.

## 5. Rule changes

Two, both landing in the same commit as the code that relies on them.

**`CLAUDE.md`'s code-only constraint gets scoped.** It currently reads *"Do not create or edit `.unity`, `.prefab` or `.asset` files. Everything is constructed in code from the bootstrap."* That rule earned its keep for the **simulation** — it is why the sim is testable without a scene, why determinism holds, and why there is no prefab drift. None of that reasoning covers where a logo sits.

Scoped form: sim, levels, tuning and gameplay views stay code-built and file-driven; **shell presentation may be authored as prefabs under `Resources/Shell/`**. The reasoning goes in the file so nobody later "fixes" the shell back into code.

**A GDD §22 row for the boot flow**, because the shipped shell documents the sim as running behind the menus and that stops being true.

## 6. Testing

Everything below the view stays plain C# and tested in EditMode:

- `LevelIndex.NextAfter(id)` — the next level in the same world, and *nothing* at a world's end; unlisted ids answer nothing rather than throwing
- `ScreenStack` transitions for the three Results actions and the three Pause actions, including that quit from either reaches `LevelSelect`
- the level-select cursor already has coverage; the editor-entry key must not disturb it

**Not covered, and stated rather than implied:** prefab wiring, teardown itself, and anything about how a screen looks. A missing serialised reference on a prefab is a runtime null this suite cannot see. That is the price of visual authoring, and it is why the manual checklist below exists.

## 7. Out of scope

Save data, medals and completion times (phase C). Settings and accessibility toggles (GDD §16). The artwork itself — the prefab structure lands with placeholder quads so the flow can be tested before any sprite exists; GDD §13.3 defers the *gameplay* art pass until the control-scheme experiment closes, which does not block menu art. Crossing worlds on completion.

## 8. Manual verification this needs

Because the untested surface grows here, the checklist is part of the work rather than an afterthought:

- boot shows the title with nothing behind it
- selecting a level builds it; quitting destroys it — check the hierarchy, not just the screen
- pause resume continues the same run; pause restart replays it; pause quit returns to the list
- results next advances, retry replays, quit returns
- the last level of a world offers no next
- entering and leaving a level repeatedly leaks nothing — the hierarchy returns to its boot state each time
- `LEVEL_EDITOR` build: the dev key opens the editor, `Esc` returns to the list
