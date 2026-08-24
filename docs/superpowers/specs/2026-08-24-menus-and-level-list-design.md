# Menus and the level list — design

**Date:** 2026-08-24
**Status:** approved, not yet implemented
**Related:** `docs/balloon-game-gdd.md` §12, §15, §16, §17.2, §22 · `docs/level-editor-spec.md` §3, §9

---

## The problem

The game boots straight into `w1_01`. There is no title, no way to pick a level, no pause,
and nothing that survives a restart. Adding levels has no effect on anything a player can
see, because nothing enumerates them.

Ordering is currently implied twice and the two can disagree. `LevelData` carries `world`
and `index`; separately `ILevelSource.List()` sorts filenames ordinally, so `w1_01` precedes
`w1_02` by naming luck rather than because anything reads `index`. Nothing reconciles them
and nothing prevents two levels claiming the same slot.

## What was already decided

| Source | Decision |
|---|---|
| GDD §15 | Level-select grid per world; per-level death counter; touch-count medal against a par; completion time shown; no unlock gating beyond world completion; progression tolerates skipped levels |
| GDD §12 | Seven worlds, ship 1–4, structure data and tooling for all seven now |
| Spec §9 | Levels ship as a packed manifest generated from the JSON at build time; JSON stays the source of truth |
| GDD §22 | `PausePressed` to follow when a pause system exists |

## Decisions taken here

1. **Both, in order.** The level list is designed once to carry the shipping front-end; only
   the level browser is built now.
2. **A declared manifest**, not a derived list.
3. **Menus draw with the game's own quads**, not uGUI or UI Toolkit.
4. **Completion, best touches and best time persist.** Death counts stay per-session.
5. **Title, level select, pause, results.** No separate world-select screen.

---

## 1. The level list

`Assets/StreamingAssets/levels/index.json`:

```json
{
    "version": 1,
    "worlds": [
        {
            "id": "w1",
            "name": "The Living Room",
            "levels": ["w1_01", "w1_02"]
        }
    ]
}
```

Modelled for `JsonUtility`: an array of world objects, each holding a `string[]`. No nested
arrays and no dictionaries, the same constraints `LevelData` already works within.

**The manifest is authoritative for order.** The `world` and `index` fields inside each level
file stay — removing them would lose data on round-trip, and the schema is a contract — but
they become advisory. The validator warns when a level's fields disagree with its manifest
position.

**A level on disk but absent from the manifest is a draft.** It opens in the editor and does
not appear in the game. This is the capability a derived list cannot offer, and it matters as
soon as level 5 is being authored while level 4 is still under test.

### Interface

`ILevelSource` grows two members beside the existing three:

```csharp
bool TryLoadIndex(out LevelIndex index);
void SaveIndex(LevelIndex index);   // throws when CanSave is false
```

`List()` is unchanged and still returns every file, because the editor needs drafts. The
packed web source supplies the same manifest shape as a build artefact.

### Why declared rather than derived

Derived ordering (scan, group by `world`, sort by `index`) cannot express a draft, makes
renumbering a multi-file edit, and has nothing to arbitrate a duplicate `index`. Its one real
advantage — no drift possible — is bought back by the editor maintaining the manifest and the
validator linting it. A declared list is only better than a derived one if the tool keeps it
current; §5 is therefore not optional.

---

## 2. Save data

One JSON blob in `PlayerPrefs` under a single key. That is IndexedDB on WebGL and a file
elsewhere, so no platform-specific code is needed.

```csharp
[Serializable] sealed class LevelRecord
{
    public string id;
    public bool cleared;
    public int bestTouches;
    public int bestTimeFrames;
}
```

**Frames, not seconds.** Integers, and it keeps `SimClock` the only measure of time in the
project.

A `version` field on the root so a later schema change migrates rather than wipes.

**World unlock is derived, never stored:** world N is open when every level of world N−1 is
cleared. GDD §15 permits no gating beyond world completion, and skipped levels stay tolerated
within a world.

Death counts are deliberately excluded. §15 wants them displayed and never hidden; it says
nothing about persisting them, and a per-session counter is the more useful number while
levels are still being tuned.

---

## 3. Screens and menu input

A code-driven state machine. No `.unity` files — everything is constructed in code from the
bootstrap, as with the rest of the project.

`AppRoot` owns a screen stack:

| Screen | Notes |
|---|---|
| `Title` | The sim runs behind it on a demo level |
| `LevelSelect` | Worlds as tabs; seven worlds do not justify a second screen |
| `Playing` | The existing `SimDriver` path |
| `Paused` | Overlay; the screen beneath stays alive |
| `Results` | Overlay; touches against par |

Pause reuses `SimDriver.Paused`, which already exists from the editor work.

### Menu input is its own interface

```csharp
public interface IMenuInput
{
    Vector2Int Navigate { get; }   // one step per press, not per frame
    bool Confirm { get; }
    bool Back { get; }
}
```

Deliberately **not** new members on `IInputSource`. GDD §5.5's rule is that simulation code
must never learn which device produced an intent; menus are not simulation, and folding menu
navigation into the gameplay intent set would pollute the interface `ApplyHit` sits behind.

`IInputSource` does gain **`PausePressed`**, as §22 anticipated. That is a change to a LOCKED
interface and requires a dated §22 row in the same commit, along with updates to
`KeyboardInput` and `ScriptedInputSource`.

---

## 4. Text

A 5×7 bitmap font. Glyph data lives as a constant in code and is blitted to a `Texture2D` at
boot — no font asset, nothing to import, consistent with the rest of the project being built
in code. A `TextView` turns a string into quads on the 640×360 grid.

Roughly 150 lines, and the one piece of real work the own-quads decision adds. It pays for
itself immediately: the death counter and the touch medal (§15) need on-screen text whether
or not menus exist.

### Why not a UI package

uGUI left with URP during the payload cut (GDD §17.2, 11.81 MB → 3.42 MB). Re-adding it
returns packages to that payload and pulls TextMeshPro for legible text, then needs fighting
to behave at integer scale. UI Toolkit is a second rendering path with the weakest
pixel-perfect story of the three.

The deciding argument: **these menus are eight items and a grid, not a form.** What a UI
package buys — layout under arbitrary resize — is what integer scaling already forbids.

---

## 5. Editor integration

The editor gains:

- **New level** — writes the file *and* appends it to the manifest
- **Open** — pick from the list, including drafts
- **Reorder** — move a level within its world

And the validator gains manifest lints:

- a level listed in the manifest but missing on disk
- a level file whose `world` / `index` contradicts its manifest slot
- duplicate ids within or across worlds
- a level file on disk in no world (reported as a draft, not an error)

---

## 6. Build order

Three sub-projects, each independently useful, each getting its own plan.

| Phase | Contents |
|---|---|
| **A — the list** | Manifest, `ILevelSource` index support, editor new/open/reorder, validator lints |
| **B — the shell** | Bitmap font, screen stack, title, level select, pause, results |
| **C — progression** | Save data, medals and times wired into the grid |

**Phase A is built first.** The editor needs it, it is useful the moment a second level
exists, and phase B is far easier to design once the list it displays is real.

---

## Testing

Everything below the UI is plain C# and testable in EditMode, which is what makes this
design worth its shape:

- `LevelIndex` round-trips through JSON with stable key order
- ordering comes from the manifest, not the filenames — proven by a manifest whose order
  contradicts the ordinal filename sort
- a level absent from the manifest is a draft: listed by `List()`, absent from the game's list
- each validator lint fires on its own case and stays silent on a clean level set
- world unlock is derived correctly, including the skipped-level case
- save data round-trips and survives a version bump

Menu rendering, screen transitions and the font atlas are not covered by EditMode tests, in
line with the existing loop. Their correctness is judged by looking at them.

## Out of scope

Settings (deferred to GDD §16 accessibility), level sharing (spec §9 says build nothing,
break nothing), world-select splash screens, and any art pass — GDD §13.3 keeps placeholder
blocks until the control-scheme experiment (open question #1) closes.
