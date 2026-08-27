# The menu shell

Everything outside a running room: the title, the level select, the pause overlay and the
results screen — plus the time controls that share their machinery.

Peer document to `level-editor-spec.md`. That one is the in-game level editor; this one is
the front end. They are different features and only meet at one point: the editor is opened
from the level select (`E`).

---

## 1. What it is

Four screens drawn with the game's own quads. **There is no UI package.** uGUI was dropped
alongside URP during the WebGL payload cut (GDD §22, 2026-08-22), and these screens are eight
items and a grid, not a form worth pulling a framework back in for.

- `BitmapFont` — a 5×7 glyph table authored in code, no font asset
- `TextView` — turns a string into one quad per lit pixel, through the same `QuadView`
  primitive every other piece of placeholder geometry uses
- `ScreenLayout` — optional per-screen prefab supplying anchor positions (§5)

## 2. The flow

```
Title ──confirm──► LevelSelect ──confirm──► Playing ──win──► Results
  ▲                    │  ▲                   │  ▲              │
  └──────back──────────┘  │                   └──back──► Paused │
                          └──────── quit ─────────────────┴─────┘
```

| Screen | Offers |
|---|---|
| Title | press to continue |
| LevelSelect | worlds as tabs, levels in manifest order |
| Playing | nothing drawn by the shell |
| Paused | resume, restart, quit to levels |
| Results | next level, retry, quit to levels — **next is absent at the end of a world** |

Crossing from one world into the next on completion is deliberately not a thing: that is a
progression question, and it belongs with save data (§7).

## 3. Bindings

| Key | Does |
|---|---|
| arrows / `WASD` | move the cursor |
| `Space` / `Enter` | confirm |
| `Esc` | back — pauses during play |
| `F` | hold to fast-forward |
| `E` | open the highlighted level in the editor (`LEVEL_EDITOR` builds only) |

These share keys with gameplay on purpose — the shell and the room are never both taking
input. Before adding a binding, check `CLAUDE.md`'s reserved-key list, which covers gameplay,
the editor and the shell together.

**Fast-forward is more fixed steps per render frame, never a longer `dt`** (`TimeControl`).
The simulation cannot observe it, so a fast-forwarded run is bit-identical and the determinism
test cannot tell the difference. Slow motion is the same mechanism inverted, and is GDD §16's
accessibility assist — implemented, not yet exposed in a settings screen.

## 4. Where the rules live

**Every screen rule is in a tested plain C# class. The view only draws.**

| Class | Owns |
|---|---|
| `ScreenStack` | which screen is current, what Back means, the win edge |
| `LevelSelectModel` | the world/level cursor and what is selectable |
| `OptionMenu` | the cursor over pause and results options |
| `LevelIndex.NextAfter` | what level follows this one |
| `SaveModel` | grading a run against par, merging it into a record, world unlock, time formatting |
| `SaveService` | the save in memory, and the only thing that touches an `ISaveStore` |

`ShellView` is a MonoBehaviour with no automated tests, by design. It samples input, calls
into those classes, and redraws when their state changes.

That boundary earned itself. The one piece of real decision logic that leaked into `ShellView`
was the one that shipped broken: win detection was level-triggered rather than edge-triggered,
and because `IsWon` clears only via a `Restart()` that needs fixed steps a paused Results
screen prevents, pressing Back bounced straight back into Results forever. The fix moved the
edge into `ScreenStack.ObserveWin(bool)`, where it is now a unit test. A later whole-branch
review found two more leaks of exactly that shape in the same file.

**A level session exists only while a level is being played.** `SimBootstrap` builds just the
camera and the shell at boot; choosing a level builds one `LevelSession` root holding the
driver, the world and every view; quitting is a single `Destroy` on that root. On the menus
there is no world at all — every read through `Bootstrap.Session` must tolerate null as the
normal case.

## 5. Authoring a screen prefab

Menu text drew at float literals hardcoded in `ShellView`, so moving a heading meant editing a
number and pressing Play. `ScreenLayout` lets you position that text, and add artwork behind
it, visually in the editor instead.

**This is optional per screen.** Every position is still hardcoded as a fallback. If no prefab
exists, or a prefab exists but does not fill in every anchor, the missing positions draw
exactly where they do today. Nothing needs a prefab to keep working.

### To author one

1. In the Hierarchy, create an empty GameObject and add a `ScreenLayout` component
   (`Component → Scripts → KeepyUppy.Runtime → Screen Layout`, or drag the script onto it).
2. For each anchor the screen uses, add a child empty GameObject, position it where that piece
   of text should sit, and drag it into the next open slot of the `Anchors` array, **in
   order**. The array is positional, not matched by name — anchor 0 must be the first entry in
   the table below, whatever the child is called.
3. Add `SpriteRenderer` children for background art. They are not anchors and do not go in the
   array; they only need to live under the same prefab so they draw with it.
4. Save the root into `Assets/_Project/Resources/Shell/`, named exactly for its `AppScreen`
   value: `Title.prefab`, `LevelSelect.prefab`, `Paused.prefab`, `Results.prefab`. Both the
   `Resources/Shell/` path and the exact name are load-bearing — `ShellView` looks the prefab
   up with `Resources.Load<GameObject>("Shell/" + screen)`.

**Leaving a slot unassigned, or the whole array short, is safe and supported.** That slot falls
back to its code position, exactly as if no prefab existed. It never produces text at the
origin or an invisible screen — there is no half-finished state to be careful about.

### Anchor order per screen

| Screen | 0 | 1 | 2 | 3 |
|---|---|---|---|---|
| `Title` | "KEEPY UPPY" heading | "PRESS SPACE" prompt | — | — |
| `LevelSelect` | world name header (or "NO LEVELS" when the manifest is empty — the two never draw together, so they share the slot) | top of the level list; rows step down from it by the code default's spacing | "< WORLD >" hint, drawn only with more than one world | — |
| `Paused` | "PAUSED" heading | top of the option list | — | — |
| `Results` | "ROOM CLEARED" heading | touch count and medal | completion time | top of the option list |

This table is duplicated in the `///` comment above each `Draw*` method in `ShellView.cs`. If
the two disagree, **the code comment is the one actually read at runtime** and this table is
stale.

## 6. Known gaps

- **The level list does not scroll.** Past nine entries it runs off the bottom of the 640×360
  screen. Worlds are planned at ~10 levels (GDD §12), so this will be hit; `w1` has three
  today. Marked at the drawing site.
- **`ShellView`, `LevelSession`, `SimBootstrap`'s wiring and `KeyboardShellInput` have no
  automated tests** and cannot get them — the first three are MonoBehaviours, and the test
  assembly cannot reference `KeepyUppy.Input.Devices`. Every rule they act on is tested; the
  wiring is checked by hand.
- **No settings screen.** GDD §16's accessibility toggles — slow motion, reduced screen shake,
  remapping — have nowhere to live yet.
- **A re-tuned par does not re-grade existing records.** The medal is graded at win time and
  stored, because par lives inside each level file and grading in the menu would mean parsing
  every level in the game to draw it. `bestTouches` is stored too, so a retroactive re-grade
  is possible later without a replay — see the phase C spec §1.
- **World unlock is derived and tested but gates nothing.** `SaveModel.WorldUnlocked` exists
  and is called by no one; there is one world to gate today.

## 7. Progression

Built in phase C. Clears, the touch-count medal and best times persist and read back on the
level select and the Results screen.

- One JSON blob in `PlayerPrefs` under `keepyuppy.save` — IndexedDB on WebGL, a file
  elsewhere, so no platform-specific code. `PlayerPrefsSaveStore` calls `PlayerPrefs.Save()`
  explicitly; without it a tab closed straight after a clear loses it.
- Per level: `cleared`, `bestTouches`, `bestTimeFrames`, `medal` — **frames, not seconds**,
  keeping `SimClock` the only measure of time in the project.
- The medal is three tiers against `meta.par_touches`: gold at or under par, silver within
  three over, bronze otherwise. **An unset par (`0`) never awards gold** — par arrives from
  playtest metrics, so unset is the normal state while a level is being authored.
- Best time is the fastest single attempt. `SimWorld.WinningFrames` is a duration — `Clock.Step
  + 1` at the moment the gate accepts, i.e. how many steps the winning attempt took — and
  `Clock` resets every attempt, so deaths cost nothing but the attempt.
- Death counts are still per-session and deliberately not persisted.
- A `LEVEL_EDITOR`-only wipe: hold `Backspace` on the level select for about a second.

The design and its rejected alternatives are in
`docs/superpowers/specs/2026-08-27-progression-save-data-design.md`.

**What progression still does not do:** gate anything (§6), scroll a long level list (§6), or
offer a settings screen for GDD §16's accessibility toggles.
