# Progression and save data — design

**Date:** 2026-08-27
**Status:** approved, not yet implemented
**Related:** `docs/balloon-game-gdd.md` §15, §16, §17.2 · `docs/menu-shell.md` §5, §7 ·
`docs/superpowers/specs/2026-08-24-menus-and-level-list-design.md` §2

Phase C of the three-phase build declared in the menus spec §6. Phase A (the list) and
phase B (the shell) are built; this is the last of the three.

---

## The problem

Nothing persists. The level select draws level ids and nothing else, the Results screen
draws a touch count that vanishes the moment you leave it, and closing the game loses every
clear. GDD §15 wants the grid showing per-level completion, a touch-count medal against a
par, and a best time — all three of which need save data that does not exist.

The pieces are already in place to feed it. `SimWorld.WinningTouches` records the medal
number at the win. `LevelData.meta.par_touches` carries the par. `LevelIndex` knows the
worlds and their order, and `LevelSelectModel` already has the cursor that would draw
against them. What is missing is somewhere to put the result and rules for grading it.

## What was already decided

| Source | Decision |
|---|---|
| Menus spec §2 | One JSON blob in `PlayerPrefs`; `cleared` / `bestTouches` / `bestTimeFrames`; frames not seconds; a `version` field; world unlock derived, never stored |
| Menus spec §2 | Death counts deliberately not persisted — §15 wants them shown, says nothing about keeping them, and a per-session count is more useful while levels are being tuned |
| GDD §15 | Par set from playtest metrics, never guesswork; no stars, no currency, no cosmetics; progression tolerates skipped levels |
| GDD §4.2, §15 | The optimal touch line is the dangerous line — the medal is what makes running a Critical balloon worth it |
| CLAUDE.md | Any change adding state to the simulation must join the replay-equality test in the same commit |

## Decisions taken here

1. **Display only.** The derived world-unlock rule is written and tested, and gates nothing.
2. **The medal is graded at win time and stored**, not re-derived in the menu from par.
3. **Three medal tiers** against `par_touches`.
4. **Best time is the fastest single attempt**, not time accumulated across deaths.
5. **`ISaveStore`**, mirroring `ILevelSource` / `ITuningSource`; rules are pure and tested.
6. **Results shows the medal and the time, and flags new bests.**
7. **A `LEVEL_EDITOR`-gated wipe.**

---

## 1. The data

Lives in `Assets/_Project/Scripts/Shell/Save/`, inside the existing `KeepyUppy.Shell`
assembly — the same home as `ScreenStack` and `LevelSelectModel`, and for the same reason:
these are rules a test can reach without a scene.

```csharp
[Serializable] public sealed class SaveData
{
    public const int CurrentVersion = 1;
    public int version = CurrentVersion;
    public LevelRecord[] levels = new LevelRecord[0];
}

[Serializable] public sealed class LevelRecord
{
    public string id;
    public bool cleared;
    public int bestTouches;
    public int bestTimeFrames;
    public int medal;          // 0 none - 1 bronze - 2 silver - 3 gold
}
```

Modelled for `JsonUtility` under the same constraints `LevelData` and `LevelIndex` already
work within: a flat array of objects, no nested arrays, no dictionaries.

**Frames, not seconds.** Integers, exactly reproducible, and it keeps `SimClock` the only
measure of time in the project.

**`version` exists so a schema change migrates rather than wipes.** Today there is one
version and the reader's only rule is that anything it cannot understand — malformed JSON,
or a `version` from the future — yields a fresh `SaveData` rather than a throw. Both cases
are tested. A player who opens an older build after a newer one loses records; that is
accepted over a crash, and is the reason the field is there to do better later.

### Why the medal is stored rather than graded in the menu

Par lives in `meta.par_touches`, inside each level file. Grading in the level select would
mean holding every level's par while drawing a menu — either parsing every level in the game
on entering the screen (a bulk fetch on `PackedLevelSource`, and wrong at seventy levels), or
mirroring par into `index.json` and taking on a second place for it to live plus the editor
sync and lint that keeps the two honest.

Grading at the win costs neither: the level is loaded, its par is in hand, and the number is
written once.

**The cost is real and accepted:** re-tuning a par does not re-grade existing records. A
level whose par tightens keeps handing out the old medal until that level is played again.
Par is set from playtest metrics (§15) and moves during tuning, so this will happen. It is
the right trade while the alternative is parsing the whole game to draw a menu; if par ever
needs to re-grade retroactively, mirroring par into the manifest is the change to make, and
`bestTouches` is stored precisely so a re-grade is possible without a replay.

---

## 2. The rules

One static class, `SaveModel`, holding every decision. No Unity types, no IO.

### Grading

```csharp
public static Medal Grade(int touches, int parTouches)
```

| Result | Condition |
|---|---|
| Gold | `touches <= par` |
| Silver | `touches <= par + 3` |
| Bronze | any other clear |

Three tiers rather than one because the gradient is the point. GDD §4.2 notes that the
fewest-touches line is the most dangerous line in the game, and a medal-chaser will run the
balloon at bursting point for it; a single pass/fail badge gives a player who missed gold by
one touch nothing to aim at.

**`par_touches == 0` means unset**, and an unset par grades bronze on any clear and can never
award gold. Levels are authored before their par is measured, so this case is the normal one
during authoring, not an edge case — and a level that hands out gold because nobody has set
its par yet would make the medal meaningless exactly when it is being tuned.

### Merging a run

```csharp
public static SaveResult Merge(SaveData data, RunResult run)
```

`RunResult` is `(string levelId, int touches, int timeFrames, int parTouches)`.
`SaveResult` carries the updated `SaveData` plus `NewBestTouches` and `NewBestTime`, which is
what the Results screen renders.

Each field takes its own best independently: a slow gold run and a fast bronze run both leave
their mark, and the stored `medal` follows `bestTouches`. A first clear sets both and flags
neither as a new best — there is nothing to have beaten.

`Merge` never mutates its input and never removes a record. A level id absent from the save
gets appended; ids for levels no longer in the manifest are left alone rather than pruned,
because a level absent from the manifest is a draft (menus spec §1) and may well come back.

### World unlock

```csharp
public static bool WorldUnlocked(LevelIndex index, SaveData data, int worldNumber)
```

World 1 is always open; world N is open when every level of world N−1 is cleared. Derived,
never stored, per menus spec §2.

**This is implemented and tested, and called by nothing.** Gating is deliberately out of
scope (§6), and the rule is written now because it is where the reader will look for it and
because it is three lines of logic whose skipped-level behaviour is worth pinning down while
the rest is fresh. If it is still dormant when gating is designed, it costs nothing to
delete.

### Time formatting

```csharp
public static string FormatTime(int frames)   // "0:47.32"
```

Pure, at 60 Hz, tested at zero, at the minute boundary, and past ten minutes.

---

## 3. Storage

```csharp
public interface ISaveStore
{
    string Read();              // null when nothing has been saved
    void Write(string json);
}
```

Two members, deliberately: the interface knows about a string, not about save data. All the
meaning lives in `SaveModel`, so the only untested code is the part that cannot be tested.

- **`PlayerPrefsSaveStore`** (Runtime) — one key, roughly fifteen lines. Calls
  `PlayerPrefs.Save()` explicitly on write: on WebGL the IndexedDB flush does not otherwise
  happen until the page decides to, and a player who closes the tab after a clear loses it.
- **`MemorySaveStore`** (tests) — a string field.

`SaveService` sits above both: loads once at boot, holds `SaveData` in memory, writes on a
clear. It is the only thing that touches an `ISaveStore`.

`PlayerPrefs` is IndexedDB on WebGL and a file everywhere else, so there is no
platform-specific code to write. A JSON file under `Application.persistentDataPath` would
give readable, inspectable saves — but that path does not exist on WebGL, which is the
primary shipping target (GDD §17.2), so it would need the `PlayerPrefs` backend anyway and
become two backends for one blob. Menus spec §2 already ruled this out.

---

## 4. The simulation side

One change, and it is the only part of this work that touches `KeepyUppy.Sim`:

`SimWorld` gains **`WinningFrames`**, set in `CheckExit()` from `Clock.Step` beside the
existing `WinningTouches`, and surviving `Restart()` the way `WinCount` and `WinningTouches`
already do. `Clock` resets on every restart, so this is the fastest single attempt with no
accumulator to maintain — and no new state that has to survive a restart correctly.

Time since first entering the level was the alternative. It punishes learning a room in a
genre built on dying forty times, and it needs an accumulator that deliberately survives
`Restart()` — more simulation state, for a worse number.

**`WinningFrames` is new simulation state, so it joins `SimSnapshot.Capture` in the same
commit.** This is CLAUDE.md's rule, and it is the rule most likely to be skipped here: the
field is a record rather than something the integrator reads, so it feels exempt. It is not.
A field outside the two-run comparison lets the determinism test keep passing while the
guarantee rots.

Grading, merging and persistence all live outside `KeepyUppy.Sim`. The simulation records two
integers and knows nothing about medals, saves or pars.

---

## 5. Wiring

All of it in `ShellView`, which stays a MonoBehaviour with no automated tests, by design
(`menu-shell.md` §4). It samples, calls into tested classes, and draws.

**On the win edge** — where `ScreenStack.ObserveWin` already fires, so there is no second
edge to get wrong — read `WinningTouches`, `WinningFrames` and the loaded level's
`meta.par_touches`, call `SaveService.RecordClear(...)`, and keep the returned `SaveResult`
for the draw. Par comes from the level already in memory; nothing is loaded to grade a run.

**Results** gains a line: touches and medal, then the time with a `BEST!` marker when the run
beat one. This adds **anchor 3** to the Results screen, so both `menu-shell.md` §5's anchor
table and the `///` comment above `DrawResults` are updated in the same commit — that
document says the code comment is the one actually read at runtime, so the two must not drift
apart in the very commit that adds a row.

The moment a record is set is the only moment showing it is free: the data is in hand and the
player is looking at the screen.

**The level select** draws each row as `> W1_01  * G  0:47.32`; an uncleared level draws as
it does today. The medal is a letter and the clear mark is `*`, because **every character
here already exists in `BitmapFont`** — the table is A–Z, 0–9 and a short punctuation set,
with no tick and no medal glyph. `>` is already the cursor prefix, hence `*` for the clear.
Drawing a real tick means authoring a glyph, and a real medal means an art pass, which GDD
§13.3 holds until the control-scheme question closes. `:` and `.` are present, so
`FormatTime`'s output needs nothing new either.

`ShellView`'s redraw guard is keyed on cursor and screen state, so it gains the save
generation counter. Without it the grid does not repaint after a clear and the level you just
finished still reads as unplayed — the same class of stale-view bug the guard exists to
prevent.

**Writes happen on a clear and nowhere else.** It is the only event that changes persisted
state; death counts stay per-session by design.

---

## 6. The dev wipe

Behind `#if LEVEL_EDITOR`, on the level select: hold `Backspace` for about a second to clear
the blob and redraw. Held rather than tapped so it cannot be hit by accident.

**`Backspace`, not `Delete`.** `Delete` reads free in CLAUDE.md's shell list, but
`LevelEditorController` already binds `deleteKey` to "delete the selected entity", and
CLAUDE.md's rule is that a new binding avoids the *combined* gameplay + editor + shell set.
The two would never be live at the same moment — the editor's `Delete` only acts inside an
editing session — but "the destructive key is a different key in each context" is exactly the
kind of thing that is discovered by wiping a save. `Backspace` is bound nowhere in the
project.

Without it, re-testing a first clear means editing the registry or IndexedDB by hand. It does
not ship: a player-facing wipe needs a confirmation flow and a settings screen to live in, and
there is no settings screen (`menu-shell.md` §6).

---

## Testing

| Test | Covers |
|---|---|
| `SaveModelTests` | grading at each tier boundary; unset par grades bronze and never gold; merge takes each field's best independently; medal follows `bestTouches`; first clear flags no new best; `Merge` does not mutate its input; unknown ids appended, stale ids kept; `FormatTime` at 0, at 1:00, past 10:00 |
| `SaveModelTests` | `WorldUnlocked`: world 1 always open, world N needs all of N−1, and the skipped-level case |
| `SaveStoreTests` | round-trip through `MemorySaveStore`; first boot with no key; malformed JSON yields a fresh save; a `version` above `CurrentVersion` yields a fresh save; neither throws |
| `SimSnapshot` | extended with `WinningFrames`, so the determinism tests cover it |
| `ExitTests` | `WinningFrames` is the winning attempt's step count and survives `Restart()` |

`ShellView`'s wiring, the level-select row layout and the Results line are checked by hand, as
in phases A and B. `PlayerPrefsSaveStore` has no automated test — it is the fifteen lines the
interface exists to isolate.

## Out of scope

- **Unlock enforcement.** The rule is tested and dormant (§2).
- **Level-select scrolling.** The list runs off-screen past nine rows today
  (`menu-shell.md` §6). Widening a row does not change that, and fixing it is not progression
  work.
- **Persisting death counts.** Menus spec §2, unchanged.
- **Re-grading records after a par change.** §1 states the cost and the escape hatch.
- **A settings screen**, and any art pass on the medal.
