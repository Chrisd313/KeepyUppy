# [WORKING TITLE] — Game Design Document

**Version:** 0.1
**Last updated:** 2026-08-21
**Engine:** Unity (2D)
**Author:** Chris

---

## How to read this document

This is a living document. Every section carries a status:

| Status | Meaning |
|---|---|
| **LOCKED** | Changing this changes the game. Requires a deliberate decision and a dated entry in §22. |
| **WORKING** | Current best answer. Expected to move as the prototype teaches us things. |
| **SPECULATIVE** | Parked idea with reasons. Not a commitment. |

**Level content is deliberately absent from this document.** Levels live in the editor as JSON (see §18). This document holds the *rules that generate levels*, not lists of them. The moment a GDD contains "World 3, Level 7: the saw comes from the left," it goes stale and stops being trusted.

Keep this file in git alongside `/levels/`. Diffs and history, no versioned filenames.

**Working title candidates:** *Keepy Uppy* · *Don't Pop* · *Loft* · *Ceiling or Bust* · *One More Bump* · *Uppy*

---

## 1. Overview — LOCKED

A 2D single-screen pixel-art platformer in which the player escorts a balloon across a hostile room by keeping it in the air — heading it, palming it, and dodging everything trying to pop it.

**Elevator pitch:** *Level Devil* meets the playground game Keepy Uppy. You can't win by being good at platforming. You win by never letting the balloon touch the ground, in a room that is actively lying to you about where the ground is.

**Genre:** Trap-based precision platformer / physics toy
**Session length:** 15–45 seconds per level
**Audience:** Web and mobile casual-to-core; the *Level Devil* / *Getting Over It* clip-sharing audience
**Comparables:** Level Devil, Trap Adventure 2, Rhythm-free precision platformers, Bluey's "Keepy Uppy"

---

## 2. Design pillars — LOCKED

1. **The balloon is the character.** The player is the support act. Every system, every camera decision, every art choice must keep the balloon the most legible object on screen.
2. **The room lies, the physics doesn't.** Traps may betray you. The balloon's behaviour never does. Deterministic, learnable, identical on attempt 40 as on attempt 1.
3. **Failure is fast and funny.** Sub-200ms restart. The pop is the punchline, not a punishment.
4. **One joke per level.** If a level can't be described in six words, it isn't a level yet.
5. **Cruel, never unfair.** Every trap is survivable on first sight *or* is an obvious gag that the player will enjoy losing to. Generous to the player, stingy to the level.

---

## 3. Core loop — LOCKED

```
Enter room  →  read the layout  →  keep the balloon aloft while repositioning
            →  route it past hazards  →  land it in the exit  →  next room
                                     ↘  pop  →  instant restart
```

The moment-to-moment loop is a tug-of-war: **the balloon needs you underneath it, the level needs you somewhere else.** Levers, plates, props and geometry all exist to pull you away from the balloon. Every level is a question about how long you can afford to be absent.

---

## 4. The three laws — LOCKED

These three rules generate most of the design. Break any of them and the game stops working.

### 4.1 Linked fate

If the balloon pops, the player pops. If the player dies, the balloon pops in the same frame. One explosion, one sound, one restart. The player is never left watching a doomed balloon after they've already lost.

**Asymmetric fragility:** the balloon dies to almost anything it touches. The player dies only to explicitly lethal geometry — spikes, pits, crushers. Everything else (wind, bumps, conveyors, animals) displaces or stuns the player without killing them. This keeps the balloon as the primary object of attention while still letting the room threaten the player directly.

### 4.2 Passive contact loses energy. Only active input adds it.

A balloon landing on a stationary player's head rebounds at ~0.5 restitution — it is still losing altitude. To *gain* height the player must jump into it, palm it or spike it.

Without this law, the optimal strategy in every level is to stand under the balloon and do nothing.

### 4.3 Gravity is the clock

There are no countdown timers. The balloon is always falling. All time pressure is emergent.

---

## 5. Controls & input — WORKING

**Orientation: forced landscape on all platforms.** LOCKED. Rooms are authored at a fixed 16:9 aspect; a portrait presentation would require re-authoring every level.

### 5.1 Verb layer — LOCKED

All balloon contact resolves through a single function:

```csharp
ApplyHit(HitSource source, Vector2 direction, float power, float aimOffset)
```

Jump-bump, palm and spike are the same call with different parameters. Adding or removing a verb must be configuration, not code — this is what makes §5.3 possible.

### 5.2 Schemes under evaluation — WORKING

A `ControlProfile` enum, hot-swappable from the tuning panel mid-run:

| Profile | Balloon verbs | Props | Notes |
|---|---|---|---|
| **A — Jump only** | Bump | Auto-activate on contact, or absent | Purest, most Level Devil. Loses prop depth. |
| **B — Jump + context** | Bump, palm, spike | Full manual | Maximum expressiveness. Highest touch-input burden. |
| **C — Jump + context (props only)** | Bump | Full manual | Body is the only thing that ever strikes the balloon. All aiming is positional. **Watch this one.** |

### 5.3 The experiment — WORKING

**Deadline: end of prototype Phase 2. Must be settled before authoring past level 10.**

Jump-only is a strict subset of jump-plus-context, so a level authored for B will always feel worse under A. A naive A/B test will produce a false result.

Protocol:

1. Author 3 test levels *designed for* A, and 3 *designed for* C.
2. Cross-play all 6 under all 3 profiles.
3. Judge on the metrics the editor already captures (touches, deaths, completion time, death heatmap) — not on five minutes of feel.
4. The real question is *"does jump-only still generate interesting levels once props exist?"*, not *"which is nicer to hold?"*

### 5.4 Context button semantics (profiles B and C) — WORKING

| Situation | Tap | Hold |
|---|---|---|
| Balloon within arm's reach | Palm — light directional tap *(B only)* | Spike — charge and release *(B only)* |
| Balloon touching player, low speed | — | Carry |
| Standing on a prop | Pick up / drop | — |
| Holding a prop | Use (blower blast, place trampoline, tie string) | — |

### 5.5 Keyboard & gamepad — LOCKED

**Keyboard is the first input built, before touch, regardless of the touch-first release order.** You cannot tune balloon physics through an on-screen thumb pad in the Unity editor. Every tuning session in §6.4, the whole of §5.3's control experiment, and all level authoring in the editor happen on keyboard. Touch is a second `IInputSource` implementation added once feel is settled.

#### Input abstraction — LOCKED

The input layer emits **intents**, never raw keys:

```csharp
interface IInputSource {
    float  MoveAxis      { get; }   // -1 .. +1
    bool   JumpPressed   { get; }
    bool   JumpHeld      { get; }
    bool   ContextPressed{ get; }
    bool   ContextHeld   { get; }
    Vector2 AimHint      { get; }   // normalised; zero = no directional intent
}
```

Three implementations — `KeyboardInput`, `GamepadInput`, `TouchInput` — feeding identical downstream code. `ApplyHit` (§5.1) never learns which device produced the intent.

#### Bindings

| Action | Keyboard | Gamepad |
|---|---|---|
| Move | `A` / `D` and `←` / `→` | Left stick, D-pad |
| Jump | `Space`, `W`, `↑` | A (South) |
| Context | `J` and `Left Shift` | X (West) |
| Aim hint | held movement / `↑` / `↓` | Right stick |
| Restart | `R` | Y (North) |
| Pause | `Esc` | Start |
| Fast-forward | `F` (held) | — |

`F` (held) and `Esc` are shell concerns read through `IShellInput`, not `IInputSource` — see the 2026-08-24 decision log entry on why `PausePressed` never joined the intent interface. Fast-forward and pause are therefore sampled per render frame rather than per fixed step.

All bindings remappable (§16). Both keyboard sets are live simultaneously — arrows for right-handers, WASD for left-hand-on-keyboard players. Web players in particular arrive with no patience for a controls screen.

#### Tap vs hold

`Context` held **< 150 ms** = palm. Held **≥ 150 ms** = charge; fires the spike on release. Charge state must be visible on the character — a wind-up pose plus a subtle particle — or players will never discover it.

#### Aim resolution

Palm and spike need a direction, and the game has no aim stick on keyboard. Resolve in this order:

| Input held | Result |
|---|---|
| Nothing | Straight up |
| `←` / `→` | ~55° off vertical in that direction |
| `↑` | Straight up, +15% power |
| `↓` | Downward smash — only useful from above, e.g. off a ledge |
| Right stick (gamepad) | Direct analogue aim, overrides the above |

Bump (jump contact) ignores all of this and uses contact-offset aiming (§6.5) exclusively — that stays purely positional in every profile.

### 5.6 Touch input — WORKING

- Left thumb: on-screen movement pad. Precise lateral positioning is non-negotiable; tap-to-move and tilt are both unacceptable.
- Right thumb: jump, plus context if the profile requires it.
- **Gestural alternative (strong candidate):** right thumb *flicks the balloon directly* — swipe up to bump, sideways to palm, hard flick to spike. This is the literal real-world Keepy Uppy gesture. Constrained by an arm's-reach rule so positioning still matters. Degrades to §5.4 / §5.5 on keyboard.
- **Safe area:** thumbs occlude the bottom-left and bottom-right ~5×5 tiles. See §11.5.
- Touch is built **after** keyboard (§5.5) and must not introduce a single new verb — if a gesture can't be expressed as one of the intents in the `IInputSource` contract, it doesn't ship.

### 5.7 Player movement — WORKING

| Parameter | Value |
|---|---|
| Run speed | 6.0 m/s |
| Jump height | ~3.2 tiles |
| Double jump | No |
| Wall jump | No |
| Coyote time | 6 frames |
| Input buffer | 10 frames |
| Ground accel | 4 frames to full speed |
| Air accel | 8 frames to full speed |
| Jump cut on release | × 0.39 while rising |
| Body size | 0.8 × 1.6 m |
| Death → restart hold | 6 frames (100 ms) — inside §8's 200 ms budget |
| Gravity | 40 m/s² |
| Terminal fall | 18 m/s (§6.4) |

The player is deliberately simple. Every point of movement complexity is attention taken away from the balloon.

Jump velocity is **derived**, never stored: `v = sqrt(2 * g * h)`, so the height stays the number that gets tuned. At 3.2 tiles and gravity 40 that is 16.0 m/s, reaching apex in 0.40 s.

**Player gravity is 40, not 9.8, and is deliberately unrelated to the balloon's 1.0 (§6.4).** Real-world gravity gave a 0.81 s apex and a 1.6 s hop — sluggish next to the 0.35–0.45 s most platformers use. Raising `g` raises the launch speed with it, so the height is unchanged and only the *pace* of the arc changes. The 40:1 split between player and balloon is the §6.4 contrast made literal: the player is heavy and immediate, the balloon is not.

The bottom five rows were added 2026-08-22 — values the model cannot avoid choosing and the table did not previously state. All are sliders (§18).

---

## 6. Balloon physics — LOCKED (model) / WORKING (values)

### 6.1 Determinism

**Non-negotiable.** Players learn these rooms across dozens of attempts; if attempt 30 doesn't replay identically to attempt 12, the genre contract breaks.

- Custom integration in `FixedUpdate` at 60 Hz. Engine gravity, drag and restitution all disabled.
- Circle collider on a kinematic body. No `Time.deltaTime` anywhere in balloon code.
- No wall-clock, no `Random` — noise is driven by the fixed-step counter with a per-level seed.
- Swept collision (cast the movement each step). A small collider at 16 m/s will tunnel through a thin spike at 60 Hz otherwise.

### 6.2 Motion model

```
a = gravity * gravityMult(inflation)
  + (-k * |v - v_air| * (v - v_air))
```

The `v_air` term is the single most important architectural decision in the game. **Fans, vents, thermals, gusts and the vacuum cleaner are all the same system** — a zone that sets `v_air`. They compose correctly when they overlap. Never write per-hazard wind code.

Drag `k` scales with balloon radius, so a large balloon is dramatically more wind-affected and a shrivelled one shrugs gusts off — that emerges for free rather than being authored.

### 6.3 Split drag — LOCKED (rationale)

A real balloon bleeds horizontal speed almost instantly. That would be fatal here, because the balloon must cross the room. So: **vertical drag high, horizontal drag much lower.** This is a deliberate lie and it is the biggest feel knob in the game.

### 6.4 Tuning table — WORKING

Starting values. Every one of these is a slider in the tuning panel (§18).

| Parameter | Value | Note |
|---|---|---|
| `gravity` | 1.0 m/s² | **No longer shared with the player** (who runs at 40). Read it as net weight after buoyancy. Tuned 2026-08-22, was 9.8. |
| `drag_ky` | 0.25 | → terminal fall ≈ 2.0 m/s at Nominal, unchanged. Tuned 2026-08-22, was 2.45. |
| `drag_kx` | 0.09 | drifts ~3–4 tiles before settling. Tuned 2026-08-24, was 0.9 — see below. |
| Player terminal fall | ~18 m/s | the contrast *is* the feel |
| `restitution_wall` | 0.75 | |
| `restitution_player_passive` | 0.5 | see Law 4.2 |
| `bump_velocity` | sets `v.y = 20.0` | **set**, never add force. Tuned 2026-08-22, was 9.0 — see the note below on why it had to move. |
| `bump_inherit_y` | × 0.35 | |
| `bump_inherit_x` | × 0.5 | running bumps travel |
| `palm_speed` | 6.0 | 0.25 s cooldown |
| `spike_speed` | 14.0 | 0.6 s cooldown, screen shake |
| `aim_offset_max_angle` | 40° | see §6.5 |
| `min_bounce_speed` | 0.3 m/s | below this, no bounce — kills jitter |
| `max_speed` | 24 m/s | sanity guard; collision is swept (6.1), so this is not what prevents tunnelling. Raised 2026-08-24 from 16, which silently capped `bump_velocity`. |
| `aim_side_angle` | 55° | §5.5 aim table, left/right off vertical |
| `aim_up_power_bonus` | +15% | §5.5 aim table, straight up |
| `context_hold_frames` | 9 (150 ms) | §5.5 tap/hold split: under = palm, over = spike charge |
| `palm_cooldown_frames` | 15 (0.25 s) | |
| `spike_cooldown_frames` | 36 (0.6 s) | |
| `arm_reach_radius` | 1.4 m | §5.4 "within arm's reach" — gates palm and spike |
| `active_contact_min_player_vy` | 1.0 m/s | **LOCKED §4.2 threshold.** Above this, or a jump pressed inside the buffer, contact is active. Below both, it is passive. |
| `player_bump_radius_bonus` | +0.15 m | §8 — player's bump volume, larger than the balloon's visual |
| `hazard_pop_radius_shrink` | −0.15 m | §8 — every hazard's pop volume, smaller than its visual |
| `exit_accept_bonus` | +0.15 m | §8 — the exit's accept volume, larger than its visual. The generous mirror of the row above; tuned separately because generosity at the goal and cruelty at the hazards are different feelings. Added 2026-08-24. |
| `fast_forward_factor` | 2 | Steps per render frame while fast-forward is held. Feel value: too high and hazards stop being reactable. Added 2026-08-24. |

Rows below `max_speed` added 2026-08-22: numbers §5.4, §5.5 and §8 describe in words but never quantified.

#### Why gravity and drag moved together — 2026-08-22

The original 9.8 / 2.45 pair gave a bump only **0.62 m** of rise. Quadratic drag makes rise logarithmic in launch speed:

```
rise       = ln(1 + k·v0²/g) / (2k)
fall speed = sqrt(g / k)
```

so raising `bump_velocity` alone is nearly useless — reaching 2 m of rise at the old drag would have needed roughly 269 m/s. Fall speed depends only on the **ratio** `g/k`, and rise scales as `1/g` when that ratio is held. Keeping `g/k = 4` preserves the 2.0 m/s descent exactly while scaling the bounce: 9.8/2.45 → 1.0/0.25 buys about 6× the height for an identical fall.

With drag that low, `bump_velocity` becomes a real lever again, hence 9.0 → 20.0. **Retune the drag pair before the bump speed, not after** — at the old drag the bump slider does almost nothing.

Watch this against §4.3: gravity is the clock, and a very low balloon gravity weakens the room's time pressure. This is the knob to check first if levels start feeling slack.

#### Why `drag_kx` and `max_speed` followed — 2026-08-24

Two consequences of the 2026-08-22 retune that the tables did not catch at the time.

**`drag_kx` had to fall with `drag_ky`.** §6.3's split is *vertical drag high, horizontal drag low* — that asymmetry is what lets the balloon cross the room instead of stalling above where it was hit. Dropping `drag_ky` 2.45 → 0.25 while leaving `drag_kx` at 0.9 inverted it: horizontal drag became 3.6× vertical, the exact opposite of the rule. Restoring the original 0.9 : 2.45 ratio against the new `drag_ky` gives 0.09. **Whenever `drag_ky` moves, check `drag_kx` against it** — the ratio is the design, not either number.

**`max_speed` was silently eating the bump.** `ApplyHit` clamps total speed, so `bump_velocity` 20 left the balloon at 16 — the slider did nothing above the ceiling, and a *running* bump lost inherited horizontal too, because the clamp scales the whole vector rather than truncating one axis (3.0 → 2.37 m/s at run speed). Raised to 24. This is safe because §6.1 collision is swept: the cast finds the wall wherever it is, so `max_speed` is a sanity bound on the tuning panel, not the tunnelling defence. The tunnelling test runs at whatever `max_speed` currently is, so it re-verifies the new ceiling automatically.

**Set velocity, don't add force.** Adding means a bump's outcome depends on the balloon's incoming velocity, so the same input produces different results. Poison for a memorisation game.

### 6.5 Contact-offset aiming — LOCKED

The horizontal distance between the player's head centre and the balloon's centre, normalised, tilts the outgoing angle up to ±40° off vertical. Breakout paddle physics.

This gives skilled players precise directional control out of a plain jump with no additional input — which is what makes profiles A and C viable at all, and what makes the touch-count medal meaningful.

### 6.6 Presentation — LOCKED (importance) / WORKING (values)

Physics gets ~40% of the way to "that's a balloon." The rest is presentation.

- **Squash & stretch** on every contact. Scale along impact normal to ~0.7, damped spring back at ~10 Hz, damping 0.3, settling over ~0.4 s. Visual only — never touches the collider.
  - *Pixel-art constraint:* use 6–8 pre-drawn deformation frames indexed by impact severity. Free-transform scaling a pixel sprite looks mushy and breaks the grid. Free transform is acceptable for everything except the balloon.
- **Lagging knot.** The knot points opposite velocity but arrives late — spring-lerp render rotation toward velocity direction. This single detail does more than anything else to sell "inflated, light, air-resisted."
- **Idle flutter.** Small lateral force from deterministic noise when moving slowly; amplitude scales *inversely* with speed. The falling-leaf wobble. Alive, but learnable.
- **Audio.** Latex squeak on wall contact, soft *bomf* on player contact, rubbery stretch on spikes. The pop should be genuinely loud.

---

## 7. Inflation & size state — WORKING

**Thesis: buoyancy and fragility are the same axis.** The more inflated the balloon, the easier it is to keep aloft — and the easier it is to kill.

### 7.1 Bands

Simulate continuously (0–1); **snap the art to five discrete sizes** so the player can always read the band at a glance. If the smooth underlay proves troublesome, ship fully discrete.

| Band | Radius | Fall speed | Pops on | Feel |
|---|---|---|---|---|
| Shrivelled | 0.25 m | ~3.5 m/s | sharp only | Drops like a stone. Fits anywhere. Nearly unkillable. |
| Slack | 0.38 m | ~2.8 m/s | sharp | Fast, precise, exhausting |
| Nominal | 0.50 m | ~2.0 m/s | sharp | Default. Levels authored here. |
| Full | 0.63 m | ~1.1 m/s | sharp, rough | Generous hang time, clips hazards |
| Critical | 0.75 m | ~0.6 m/s | **everything** | Near-neutral buoyancy. Free keepy uppy. Ticking bomb. |

**Hit response inverts.** A shrivelled balloon rockets off the player — fast, far, precisely aimable. A critical balloon is sluggish and mushy. A second axis running underneath the first: **hang time vs. control**.

### 7.2 Sources of change

| Source | Rate | Notes |
|---|---|---|
| Heat zone | ~3 s per band, up | Candles, hobs, radiators, sunbeams |
| Cold zone | ~3 s per band, down | Fridges, freezers, AC vents |
| Pump station | ~1 s per band | Player-activated. Also the room's "reset to nominal" affordance. |
| Leak | ~6 s per band, down | From a *graze* — a near-miss that punctures without popping |

**Constraint — LOCKED: inflation only changes inside marked zones or at stations. Never as ambient drift.** A constantly-moving value destroys the player's mental model, and a trap game lives on predictability.

### 7.3 Leaks

The leak is the most valuable item in this section because it creates a **middle state between fine and dead**. A graze becomes a scramble to a tape station rather than an instant explosion. Most trap games have nothing between those two states.

### 7.4 Size-locked exits — LOCKED

The level ends by putting the balloon through a gate of a specific diameter. A wide ring near the ceiling requires arriving **Full** or **Critical**. A floor-level letterbox slot requires arriving **Shrivelled** — which also means falling at 3.5 m/s with almost no reaction time.

**The shape of the exit states the level's answer, visually, on a single screen.** This is what turns inflation from a modifier into the goal.

#### Implementation — 2026-08-24

The gate is a **fit test**: `balloon diameter <= exit diameter`, judged on the balloon's
centre being inside the aperture. A slot is measured on its *height*, which is the whole
point of a letterbox — being wide does not help you through it. Position is what makes a
ceiling ring demand buoyancy; the diameter alone never encodes height.

`required_band` stays **derived** from the diameter and is never read by the simulation. The
editor recomputes it whenever the diameter changes, and the validator warns if a hand-edited
file disagrees, so the level-select icon cannot lie.

A balloon too large **passes over the gate** rather than bouncing off the rim. That is
deliberate only while inflation is unimplemented: the radius is fixed at spawn, so a gate
too small is an unwinnable level rather than a moment of play, and the validator says so on
save. **When §7.2 inflation lands, a too-large balloon meeting a gate becomes a real event
and needs a collision** — `SimWorld.CheckExit` is where that goes.

Death beats arrival on the same step. §8's generosity lives in the accept volume, not in
letting a pop be overridden by arriving.

The win is held for 60 frames and then the room restarts, because there is no room list to
advance to yet. That hold is the hook a level sequence replaces.

---

### 7.5 Readability — LOCKED

No HUD bar. The balloon *is* the readout:

- Size (hence the snapped art bands)
- Latex thins toward Critical: paler, translucent, visible stretch marks
- Rising creak/strain sound, going continuous at Critical
- Fine tremble at Critical

Audio carries the warning, because the player's eyes are already occupied.

### 7.6 Emergent consequence worth protecting

A Critical balloon needs the fewest touches to cross a room. So **the optimal line for the touch-count medal is the most dangerous line in the game.** Medal-chasers will voluntarily run the balloon at bursting point for score. This falls out of two existing systems at zero cost — do not accidentally design it away.

---

## 8. Fairness rules — LOCKED

- **Generous to the player, stingy to the level.** The player's bump hit-volume is noticeably *larger* than the balloon's visual. Every hazard's pop hitbox is noticeably *smaller* than its visual. Players never notice this and always feel it.
- **Buffer the input.** A jump pressed up to 10 frames before the balloon is in range still fires on arrival.
- **Never demand precise platforming and precise balloon-tracking in the same two seconds.** Alternate them. When the jump is hard, the balloon is parked high and safe; when the balloon is in trouble, the ground is simple. Breaking this reads as unfair rather than cruel-but-learnable.
- **Restart under 200 ms.** No exceptions, no transitions, no ad breaks (§17.4). Implemented as a synchronous state reset, not a process — the death is held for 6 frames (100 ms) so the cause registers, then the room resets in the same step. Every counter, buffer and cooldown clears; the per-level death count (§15) deliberately survives.

Numeric form of the first rule, both sliders (§6.4): `player_bump_radius_bonus` +0.15 m, `hazard_pop_radius_shrink` −0.15 m. Tuned independently — generosity and cruelty are separate knobs.

---

## 9. Hazards — WORKING

### 9.1 Generic tier

Spikes, saws, crushers, moving platforms, disappearing floors, conveyors, ice, one-way platforms, glass panes, closing doors, pressure plates. Connective tissue. Necessary, not interesting.

**Pressure plates deserve a note:** the balloon is too light to trigger them. Only the player can. This is the purest expression of §3's tug-of-war and should be used often.

### 9.2 Signature hazards

| Hazard | Behaviour | Why it earns its place |
|---|---|---|
| **Heat / cold zones** | Drive inflation (§7.2) | Real balloon physics, free depth |
| **Sunbeams / heat lamps** | Sustained light pops after ~3 s | Time pressure without a clock; forces movement |
| **Rough surfaces** | Popcorn ceiling, brick, sandpaper — pops on contact | Walls stop being safe; inverts the player's spatial read |
| **Static patches** | Rub to charge; balloon sticks to ceilings/walls briefly, but attracts pins and metal debris | Risk/reward, unmistakably balloon-like, nothing else has it |
| **The animal** | Tracks the balloon, waits, lunges. Cat (W1) → dog (W3) → toddler (W5) | One behaviour tree, three skins. Players will learn to dread the silhouette. |
| **Leading dart trap** | Fires at where the balloon *will be*, not where it is | The "the level is sentient" moment |
| **Water** | Safe landing — the balloon floats. Comes out wet: heavier, falls fast until dry | Relief valve that costs something |
| **Vacuum intakes / fans** | `v_air` zones; hazard when on, tool when switchable, puzzle when timed | Best dual-purpose object in the game |
| **The false exit** | Slides two tiles left as the balloon arrives | Mandatory |

### 9.2a Implementation — 2026-08-24

Three of the above are simulated: **saw** (generic tier, path-following, lethal to both),
**lamp** (the sunbeam; sustained exposure pops the balloon) and **fan** (an air zone, not a
wind system of its own — GDD 6.2 has only one). The cat is deferred: "tracks, waits, lunges"
is a behaviour tree, and it wants the mover working underneath it first.

A fourth type joined 2026-08-28: the **spike bar** (§9.3's retractable trap), a capsule like
the lamp's but lethal on contact and to both bodies — every tile spike kills the player, and a
bar that did not would be a lie told by the same sprite. It stays a distinct entity type rather
than a tile because a trigger needs an id to link to, and a tile has none.

**Entity position is a pure function of the step counter**, evaluated from scratch each step
rather than advanced by `+= v * dt`. That single decision means attempt 40 replays attempt 1
by construction rather than by care (6.1), makes restart free — there is no motion state to
unwind, so the 8 budget is untouched — and lets any step be asked for out of order, which is
what the spec's phase-5 rewind and ghost trails will need. Anything that *cannot* be written
this way carries explicit state and must join the replay-equality test in the same commit;
the lamp's exposure is the first such case.

Kill volumes are capsules — a segment plus a radius — so a saw is a zero-length one and a
lamp beam is a long one. One shape means the 8 fairness shrink has a single call site, as it
does for tiles.

Lamp exposure is **unbroken**: leaving the beam resets it. A countdown that remembered you
would force leaving the room rather than moving within it, which is a different and worse
hazard than the one 9.2 describes.

Entities are **lethal but passable** for now. A saw kills what touches it; nothing pushes a
body. Crushers and moving platforms need moving-solid collision, and "what happens when a
platform pushes you into a spike" is a design question rather than a coding one — its own
session, alongside the triggers and links 9.3 needs.

`start_state: dormant` is honoured, and as of 2026-08-28 triggers and links can arm what it
leaves dormant: `player_region` and `balloon_region` region triggers, plus a `timer` trigger
for a hazard armed by the clock rather than by the player, `on` / `off` / `toggle` links with a
delay expressed in fixed steps, never seconds. A fired link is undone by `Restart()` — the
entity and zone states a link wrote are put back to what the level file declared before the
next attempt's first step runs, so the room the player retries is the room they died in, not
the one their last attempt left armed.

An entity type this build does not understand loads inert and is counted, never dropped. A
level file outlives any one build, and an editor that silently deletes what it cannot parse
is not trustworthy.

---

### 9.3 Betrayal traps

Every world needs at least two. The pattern is always the same: **something dormant, armed by the player committing.**

- A floor that falls away not to kill you, but so you can't reach the balloon
- A ceiling that drops to squash the balloon while you watch
- A heat lamp that switches on *after* you enter the corridor
- A pump that doesn't stop when you release it
- A "safe" landing zone that reveals spikes as the balloon descends

Implemented with `start_state: dormant` + a `player_region` trigger with `once: true` (§18).

---

## 10. Props & tools — WORKING

### 10.1 Rules — LOCKED

1. **Props live in the level, never in a loadout.** Picked up in the room, used in the room, not carried between levels.
2. **One prop per level, two at most.** A prop is a level's premise, not equipment.
3. **Every prop charges the same currency: the ability to bump.** Slower, can't jump, can't aim, can't fit. A free prop is a power-up, and power-ups are wrong for this game.
4. **Every prop needs a betrayal variant.** If you can't name how the level lies about it, don't ship it.

### 10.2 The set

| Prop | Function | Cost | Betrayal |
|---|---|---|---|
| **String** ★ | Tether balloon to player, ~2-tile radius. Cut at scissor stations. | Can't send it anywhere; drags it through everything | Tether you can't cut until the far side; a nail that snags it |
| **Leaf blower** ★ | Hold to emit a wind cone (a `v_air` volume parented to the player). ~4 s battery. | Recoil shoves you backwards | The one that's secretly a vacuum |
| **Bucket helmet** | Headbutt sends the balloon ~2× higher | **Flat top — contact-offset aiming disabled entirely.** All power, zero steering. | Low gap you can't fit through wearing it, past a one-way drop |
| **Trampoline** | Placeable bounce pad, ~0.9 restitution. One per level. | Placement is the puzzle | Pre-placed one with pins under the cover |
| **Sandbag** | Clip to balloon: heavy, wind-immune, predictable | Can't be sent high, falls fast | — |
| **Carry** *(core verb, not a prop)* | Hug the balloon and walk | **Can't jump.** Wriggles free after ~3 s — scaling with inflation: Shrivelled is near-indefinite, Critical escapes in under a second and risks popping in your arms. | — |

★ = if only two props ship, these are the two.

### 10.3 Level furniture

Pushable crates, levers, fan switches, pump stations, tape stations, launcher pads (fire the balloon on a fixed arc when triggered). Cheaper than carried props — no pick-up/drop state — and they're what makes "the switch is over there" a real level engine.

### 10.4 Cut

**Spring boots** (costless jump buff, duplicates the bucket) · **Racket/paddle** (a bigger hitbox is just "the game is easier now"; every cost is contrived) · **Oven mitts, nets, bags, recall whistles** (duplicate carry or solve non-problems).

### 10.5 Parked — SPECULATIVE

- **Umbrella** — shield above you, canopy against droppers, catches wind and drags you. Overlaps bucket and trampoline. Revive only if a full wind world needs a third verb.
- **Helium canister** — gravity inverts; the balloon rises and you're now stopping it hitting the *ceiling*. Too big to be a prop — this is a world gimmick or a late act break. One sprite and a sign flip on the existing model, which is an absurd return on investment.
- **The pin level** — carry a drawing pin across the room while keeping the balloon up. You are personally carrying the murder weapon and your own headbutt is now the most dangerous thing in the room. One prop, one joke, one perfect level.

---

## 11. Level design rules — WORKING

### 11.1 One joke per level — LOCKED

If a level can't be described in six words, it isn't a level yet. Write the six words into `meta.author_note` before building it.

### 11.2 The world template

Twelve levels per world. This is the primary authoring tool.

| Levels | Job |
|---|---|
| 1–2 | **Teach.** The new mechanic alone, no other hazards. Safe to fail. |
| 3–7 | **Develop.** New mechanic × each old mechanic, one pairing at a time. |
| 8–9 | **Combine.** Two or three systems at once. |
| 10 | **The trick.** One joke level. The pin level. The upside-down room. The one with no floor. |
| 11 | **The betrayal.** The level that lies hardest. This is the clip. |
| 12 | **The finale.** Long-ish, hard, no new ideas — pure execution of everything taught. |

*Web/mobile adjustment (§17):* the finale gets **denser, not longer**. All levels stay within the 15–45 s cap.

### 11.3 One mechanic per world — LOCKED

Every world owns exactly one new mechanic, and the theme is that mechanic's most obvious real-world home. A world that owns no mechanic is wallpaper, and players feel it.

### 11.4 Domestic settings justify everything

A balloon in a living room needs no explanation. A kitchen is already full of hot and sharp things. No fiction is required to place a single hazard.

### 11.5 Mobile safe area — LOCKED

Thumbs occlude the bottom-left and bottom-right ~5×5 tiles. Treat both as non-critical space: no exits, no single-frame timing hazards, no must-see prop pickups. This constrains **every level ever authored**, so it is enforced by an editor overlay (`T`) and by save-time lint.

---

## 12. World spine — WORKING

| # | World | Owns | Props introduced | Antagonist |
|---|---|---|---|---|
| 1 | **The Living Room** | Core keepy uppy. Nothing new. | Carry | The cat |
| 2 | **The Kitchen** | **Inflation** — heat (hob, oven, toaster) *and* cold (fridge, freezer) | String | Steam |
| 3 | **The Garden & Shed** | **Wind** — gusts, air zones. Water as safe landing. | Leaf blower, sandbag | The dog |
| 4 | **The Garage & Loft** | **Leaks & repair** — nails, tacks, tape stations, static | Trampoline, bucket helmet | The nail gun |
| 5 | **The Leisure Centre** | **Water & wetness** — public, crowded, tiled, loud | Umbrella | The toddler |
| 6 | **The Factory** | Nothing new — everything at once, faster | — | The machine |
| 7 | **The Sky** | **Runaway inflation** — pressure drops, the balloon expands and won't stop | Helium | Physics |

**Notes:**

- **The kitchen owning both heat and cold** is the tightest thematic fit available — a hob and a freezer sit three metres apart in a real kitchen. This gives the whole inflation system one home instead of two, saving a world and landing the mechanic harder.
- **World 6 introduces nothing, deliberately.** Late difficulty comes from combination and tempo, not from a seventh system arriving when the player is already at their limit.
- **Ship worlds 1–4** (§20). Structure data and tooling for all seven now; author no content past world 4 until world 1 has been played by strangers.

### 12.1 Parked worlds — SPECULATIVE

- **The Night Level** — living room after dark, visibility limited to lamp pools, balloon position inferred by sound. Possibly brilliant, possibly miserable. Prototype one level before committing a world.
- **The Air Vents** — claustrophobic, entirely air currents, no floor to speak of.
- **The Wind Farm / Rooftops** — the big-outdoor-wind world if W3 proves it has more in it.
- **The Supermarket** — freezer aisle, trolleys, conveyor checkouts. Only if the kitchen can't carry all of inflation.

---

## 13. Art direction — WORKING

**Style:** pixel art, flat and high-contrast, in the spirit of *Level Devil*.

| Property | Value |
|---|---|
| Base resolution | 640 × 360 |
| Presentation | **Integer scale only** — ×2 → 720p, ×3 → 1080p, ×6 → 4K |
| Tile size | 20 × 20 px |
| Room size | 32 × 18 tiles |
| World scale | 1 tile = 1 metre |
| Filtering | Point, no mipmaps |

### 13.1 The reserved-colour rule — LOCKED

Single global palette. Muted background, dark silhouette geometry, **one reserved colour for hazards, one reserved colour for the balloon.** The balloon must be the highest-contrast object on screen in every world, in every frame. Its colour is never used for anything else, ever.

Each world changes only the background hue and the prop set. A new world therefore costs a hue and a handful of sprites, not an art pass.

### 13.2 Sub-pixel rendering — LOCKED

**Do not pixel-snap moving entities.** At 20 px/metre a Nominal balloon falls 0.67 px per frame; snapping produces visible judder on exactly the object the player is tracking. Snap the tile grid only. Balloon, props and moving hazards render at sub-pixel positions.

**Interpolate moving entities between fixed steps.** Added 2026-08-28.

The same 0.67 px per frame is why the *clock* cannot be snapped either. The sim advances only in FixedUpdate at 60 Hz (§6.1) and the display is not phase-locked to it, so a view that samples the sim position raw on a render frame draws the previous step again on some frames and jumps two steps on others. The apparent velocity fluctuates, and on a slowly descending balloon it reads as bouncing off invisible platforms in mid-air — which is exactly how it was reported.

`StepInterpolator` (`Assets/_Project/Scripts/Runtime/StepInterpolator.cs`) draws between the two most recent steps. It takes **one sample per fixed step, not one per render frame** — pairing a render-frame sample with a fixed-step alpha puts a two-step jump against a one-step fraction on precisely the frames where the two clocks drift past each other, which reintroduces the hitch exactly where it hurts. That mistake was made and caught by `StepInterpolatorTests.ASteadyDescent_*`, which measures frame-to-frame gaps and fails on a 143× swing.

Two cases snap instead of tweening: the first sample, and a restart — detected by the world's step counter running backwards, since tweening across a respawn would drag the balloon over the room on the exact 200 ms path §8 wants to feel instant.

This is presentation only, and the simulation cannot observe it — the same rule `TimeControl` follows. Determinism is untouched. The cost is that the picture lags the sim by up to one step (16.7 ms), which is the standard trade for a fixed-timestep renderer; extrapolating forward instead would invent positions the sim never had and overshoot through walls on the frame a bounce lands.

### 13.3 Placeholder pipeline

Solid colour blocks on the final grid, with the reserved-colour rule enforced from day one. Hand-drawn sprites and animation drop in later without a layout pass. **Do not do an art pass before the control scheme decision (§5.3) is settled.**

---

## 14. Audio — WORKING

Placeholder direction only; not blocking.

- **Balloon** carries most of the audio design: squeak on wall contact, soft *bomf* on player contact, rubbery stretch on spikes, creak rising to continuous at Critical inflation.
- **The pop is the loudest sound in the game.** Immediately followed by the room detonating.
- Music: light, loopable, ducking hard on the pop so the silence lands.
- Restart must be silent-fast — no stinger that delays the retry.

---

## 15. Metagame & progression — WORKING

- Level-select grid per world — **built in phase B**, with completion, the touch-count medal
  and best times **added in phase C**. Death counts are still per-session and not shown on
  the grid.
- Per-level death counter, displayed and never hidden
- **Touch-count medal** — three tiers against `meta.par_touches`: gold at or under par, silver
  within three over, bronze otherwise. Par is set from playtest metrics (§18), never from
  guesswork, and a par left unset never awards gold. The medal is graded when the room is won
  and stored, so **re-tuning a par does not re-grade existing records** until the level is
  played again.
- Completion time tracked, shown, no leaderboard at launch — the fastest single attempt,
  stored in frames.
- No unlock gating beyond world completion. **The rule is derived and tested
  (`SaveModel.WorldUnlocked`) but gates nothing yet.** No stars, no currency, no cosmetics.
- Progression must tolerate skipped levels (§17.4)

---

## 16. Accessibility — WORKING

- Slow-motion assist toggle (0.5×) — the same mechanism as hold-to-fast-forward, inverted: both are `TimeControl` (§6.1) choosing how many 60 Hz steps run per render frame, never a change to `dt`. `TimeControl.SlowMotion` drops every other frame by frame parity.
- Optional balloon trajectory aid — **off by default**; deliberately absent from the base game, available as an accessibility option
- Colourblind-safe hazard colour, verified against the reserved-colour rule
- Fully remappable input; no timed input sequences anywhere
- Reduce-screenshake toggle
- No audio-only information — every audio cue (creak, strain, animal tell) has a visual counterpart

---

## 17. Platform, performance & release — WORKING

**Order: web and mobile first, then PC/Steam and mobile stores.** Touch-first → keyboard/gamepad later is the easy direction; the reverse is painful.

### 17.1 Targets

| Stage | Platform | Input |
|---|---|---|
| 1 | Web (itch / Poki-class portals) | Touch + keyboard |
| 2 | Mobile (iOS/Android) | Touch |
| 3 | PC / Steam | Keyboard + gamepad |

### 17.2 Unity WebGL risk — flagged

Build size and cold-load time are what kill web releases, and Unity's web export is not the lightest path available. Mitigate from week one, not month six:

- IL2CPP with aggressive code stripping
- Brotli compression
- Remove every unused package (especially unused physics)
- Single texture atlas per world

#### Measured baseline — 2026-08-22, end of P0

First WebGL build, Unity 6000.2.6f2. **11.81 MB** Brotli payload with no game content in it: 7.62 MB `wasm`, 4.10 MB `data`. Build time 23 minutes.

The data file was almost entirely things the game does not use — the Unity splash logo (2.7 MB uncompressed, 22.5% of all assets), and URP's post-processing kit: FilmGrain textures (~2.3 MB), SMAA, BlueNoise, UberPost. The game's own content is a rounding error.

This was over budget for a Poki-class portal before a single sprite existed, which is the risk this section was written about.

#### After reduction — same day

| | Baseline | Splash off, stripping High, 28 packages cut | URP dropped |
|---|---|---|---|
| `wasm.br` | 7.62 MB | 6.13 MB | **2.62 MB** |
| `data.br` | 4.10 MB | 3.50 MB | **0.71 MB** |
| **payload** | **11.81 MB** | **9.73 MB** | **3.42 MB** |
| build time | 23 min | 16 min | 6 min |

**−71%.** Dropping URP was worth 6.31 MB on its own, and most of that was *code*, not assets — the render pipeline was 3.51 MB of the wasm. A game of untextured quads on one unlit shader has no use for it.

Remaining payload is essentially Unity's floor: 2.62 MB of engine and IL2CPP runtime, plus `unity_builtin_extra` at 520 KB. Content budget from here is roughly 1.5 MB if the target is to stay under 5 MB — which makes §17.2's "one texture atlas per world" a real constraint, not a slogan.
- **Check the build size at the end of prototype week 1.** Retrofitting this is miserable.

### 17.3 Pacing consequences

- **15–45 s per level, hard cap.** Difficulty from precision, never length.
- **World 1 must be near-trivial.** Web players bounce in seconds; the first level should be effectively unloseable and the first 30 seconds need zero instruction.
- Every level self-contained with clean start/end beats.

### 17.4 Ad model constraints (if ad-funded)

- Interstitials **between levels only**. **Never in the death → retry loop.** An ad after a death would destroy a trap game outright.
- Rewarded "skip level" must exist → progression cannot assume every level was beaten.

### 17.5 Steam differences

The only real difference is the content promise: a paid PC release wants worlds 5–7. This is why the structure covers seven and the launch scope covers four.

---

## 18. Tools — see `level-editor-spec.md`

A dev-only in-game level editor. Full spec in the companion document. Summary of what this GDD depends on:

- **JSON levels**, one file per level, committed to git. Text diffs are a feature.
- **Zones are one system** — `type: "air"` with a `v_air` vector covers every fan, vent, thermal and gust.
- **`start_state` + `links` + `once: true` triggers** is the entire betrayal toolkit (§9.3).
- **`phase`** on periodic entities, so traps can be desynced. Easy to forget; without it every saw moves in lockstep.
- **Live tuning panel** exposing every value in §6.4 and §7.1, hot-reloadable, with saveable profiles. *The highest-value item in the entire toolchain* — this game is 90% feel, and feel is settled by hundreds of small iterations.
- **`ControlProfile` toggle** lives here (§5.2).
- **Input-recording rewind** — free because the sim is deterministic. Gives replays and ghost trails as a side effect.
- **Metrics capture** — death heatmaps and touch distributions. This is how `par_touches` gets set and how §5.3 gets decided.
- Editor is desktop-only and stripped behind a compile define. **The shipping game reads the same JSON.** Never two level formats.

---

## 19. Story concepts — SPECULATIVE

The game is built premise-agnostic. Picking one later must cost nothing.

**Evaluation criterion:** *which premise justifies linked fate (§4.1) without a single line of exposition?* A premise that explains the core rule for free is worth far more than one that merely looks good.

### 19.1 The Escaped Party Balloon
A balloon gets away from a birthday and travels progressively further from home — living room → kitchen → garden → out into the world → the sky, where it finally bursts. Wordless, warm, the backgrounds carry it entirely, and it hands you an ending: the pop is failure for the whole game right up until the last moment, when it is the point.
*Weakness: explains nothing about linked fate.*

### 19.2 The Symbiote
A lab creature bonded to a balloon; sever the bond and both die. **The premise IS the mechanic.** A lab setting also justifies escalating test chambers with no narrative work.
*Risk: well-trodden ground. The creature's design has to carry all the personality.*

### 19.3 The Breath
The balloon holds the character's last breath, or their soul. Zero exposition, works in total silence, and it turns a comedy troll game quietly haunting.
*Tonally the riskiest.*

### 19.4 The Delivery
A courier contractually bound to deliver one balloon across an indifferent world. Enormous danger, trivial stakes, nobody cares. Deadpan, and it suits the domestic settings.

### 19.5 Orthogonal decision: does the balloon have a face?
Independent of premise, and bigger than it looks. A face makes every pop hurt, makes clips funnier, and gives you a store-page mascot — but shifts the tone from "object" to "creature" and undercuts the clean physics-toy reading. **Decide before the art pass, not during.**

---

## 20. Scope & milestones — WORKING

### 20.1 Launch scope

**Worlds 1–4. ~48 levels.** That is a complete game. Worlds 5–7 are post-launch / Steam.

### 20.2 Prototype phases

| Phase | Goal | Exit criteria |
|---|---|---|
| **P0 — Vertical slice** | Balloon physics, player movement, one room, pop and restart | The balloon *feels* like a balloon to someone who isn't you. WebGL build size checked. |
| **P1 — Editor: author** | Grid, tile painting, entity placement, save/load, `Tab` play toggle | You can build a level without opening the Unity editor |
| **P2 — Editor: tune + control experiment** | Live tuning panel; §5.3 run and settled | **`ControlProfile` decided and locked.** No content authored past level 10 until this closes. |
| **P3 — Systems** | Inflation, zones, triggers, links, leaks, the six props | World 1 and 2 authorable end-to-end |
| **P4 — Content** | Worlds 1–4 authored against the template (§11.2) | 48 levels, metrics-tuned pars |
| **P5 — Ship** | Art pass, audio, accessibility, portal integration | Web release |

### 20.3 Ordering rules

- **Keyboard input before touch input**, always. Physics cannot be tuned through an on-screen thumb pad in the Unity editor (§5.5).
- Editor phases 1–2 come before content. They pay for themselves inside a week.
- No art pass before §5.3 closes.
- No content past level 10 before §5.3 closes.
- Nothing past world 4 before strangers have played world 1.

---

## 21. Open questions

| # | Question | Owner | Deadline |
|---|---|---|---|
| 1 | `ControlProfile` A vs B vs C (§5.3) | Chris | End of P2. **Blocks content and art.** |
| 2 | Gestural touch input vs on-screen context button (§5.6) | Chris | End of P2 |
| 3 | Does the balloon have a face? (§19.5) | Chris | Before art pass (P5) |
| 4 | Continuous inflation with snapped art, or fully discrete? (§7.1) | Chris | P3 |
| 5 | Ad-funded or premium on web? (§17.4) | Chris | Before P5 |
| 6 | Title | Chris | Before P5 |
| 7 | Story premise (§19) | Chris | Before P5 — deliberately deferred |

---

## 22. Decision log

| Date | Decision | Rationale |
|---|---|---|
| 2026-08-21 | Linked fate — balloon and player die together | Recovers Level Devil's full trap vocabulary without diluting the escort fantasy; a better joke than invincibility |
| 2026-08-21 | Fixed single-screen rooms, no scrolling | Solves two-focal-point camera framing; makes traps readable; makes the level editor a two-day tool instead of a two-week one |
| 2026-08-21 | No trajectory prediction aid in the base game | Surprise is the genre's currency. Retained as an accessibility option only. |
| 2026-08-21 | Single-player first; two-player considered post-launch | Keepy Uppy is inherently social and co-op is the most viral version, but it roughly doubles the design work |
| 2026-08-21 | Touch count tracked as a scoring hook | Speedrun/medal hook for free; interacts with inflation to make the optimal line the dangerous line |
| 2026-08-21 | Kitchen owns both heat and cold | Tightest thematic home for the whole inflation system; saves a world |
| 2026-08-21 | Web + mobile first, Steam later | Touch-first → keyboard later is the easy direction. Constrains controls and level length from the outset. |
| 2026-08-21 | Forced landscape | Rooms are authored at a fixed aspect; changing it later means re-authoring everything |
| 2026-08-21 | Pixel art, 640×360, integer scaling | Matches the Level Devil reference; placeholder blocks until feel is settled |
| 2026-08-21 | Control scheme deferred to a measured experiment | Jump-only is a strict subset, so a naive A/B produces a false result. Needs authored-for-each test levels. |
| 2026-08-21 | Keyboard built before touch, despite touch-first release | Balloon feel is tuned in the Unity editor across hundreds of iterations; an on-screen pad makes that impossible. Input abstracted behind `IInputSource` so neither device is privileged downstream. |
| 2026-08-22 | **LOCKED change — `IInputSource` (§5.5) gains `RestartPressed`, `Sample(int step)` and `ResetIntents()`** | `RestartPressed` is an intent: §5.5's own binding table lists `R` = Restart, and reading `R` downstream would be exactly the raw-key leak the interface exists to prevent. It also lets `ScriptedInputSource` drive the death→restart loop, which §8 puts a hard 200 ms budget on. `Sample(int step)` exists because presses arrive on render frames and must be committed to exactly one fixed step — below 60 fps a press would otherwise be silently lost. `ResetIntents()` stops a buffered press crossing a restart. The locked *principle* — intents, never raw keys — is unchanged. `PausePressed` to follow when a pause system exists. |
| 2026-08-22 | Balloon dies on floor contact; walls and ceilings still bounce at `restitution_wall` | The fail state of keepy uppy. Distinguished by contact normal, so one rule covers every surface. |
| 2026-08-22 | Directional spikes are lethal on their pointed face only | The flat back of a spike reads as ordinary geometry, so treating it as lethal feels arbitrary. Lets a floor spike sit beside standable ground and a wall spike threaten only the room. |
| 2026-08-22 | **Dropped URP for the built-in render pipeline** | Worth 6.31 MB of an 11.81 MB WebGL payload — 3.51 MB of it wasm, not assets. The game draws untextured quads through a single unlit shader; URP's post-processing kit (film grain, blue noise, SMAA, UberPost) was the entire asset payload, and it forced the physics and terrain modules back into the build. Taken now rather than after the art pass, when it would have been far more expensive. Cost: no 2D lights without reversing this. |
| 2026-08-22 | Unity splash disabled, managed stripping High, engine code stripping on, WebGL exceptions off | §17.2 mitigations. Applied as code in `WebGLBuild.ConfigureForWeb` rather than as editor settings, so the shipping configuration is version-controlled and reproducible. Exceptions-off is a real tradeoff: smallest and fastest, but a runtime error in the wild is much harder to diagnose. Revisit before public release. |
| 2026-08-22 | Package manifest cut from 44 to 16 | "Remove every unused package" (§17.2). Physics **and Physics2D** both went — the balloon and player are hand-integrated with custom swept collision per §6.1, so Unity physics was never used at all. uGUI went with URP; the dev panels are IMGUI. |
| 2026-08-24 | **Entity motion is derived from the step counter, never accumulated** | `position = f(step)` makes GDD 6.1 replay-equality structural rather than a thing to be careful about, costs nothing at restart, and is what later makes rewind and ghost trails (spec phase 5) possible at all. The constraint it imposes — reactive hazards like the cat need explicit state and explicit test coverage — is the price, and it is worth paying. |
| 2026-08-24 | Moving hazards are lethal but passable; nothing pushes a body yet | Crushers and moving platforms need moving-solid collision, which raises a design question (a platform pushing the player into a spike) rather than only an implementation one. Deferred to the session that also does triggers and links, since GDD 9.3 needs both. |
| 2026-08-22 | Hazard tiles block whatever they do *not* kill, and are transparent to whatever they do | A tile cannot both stop a body and pop it: collision holds the body outside, so the §8-mandated *smaller-than-visual* kill volume would never be reached. The balloon flies into a spike and pops; the player lands on the same spike and is killed by a volume that bleeds slightly through its surface. |
| 2026-08-24 | **Reverses the 2026-08-22 prediction: `PausePressed` does NOT join `IInputSource`** | `IInputSource.Sample(int step)` is called inside `SimWorld.Tick()`, so an intent committed there cannot be read while the world is not ticking — a pause bound that way can be entered and never left. Pause and fast-forward are shell concerns that no simulation code reads, so both live on `IShellInput`, sampled per render frame. Adding a member to a LOCKED interface for something the simulation never consumes is the widening §5.5 exists to prevent. `IInputSource` is therefore unchanged by the menu shell. |
| 2026-08-24 | **Hold-to-fast-forward, and the §16 slow-motion assist, are the same mechanism** | Speed is expressed as the *number* of 60 Hz steps run per render frame, never a longer `dt` and never `Time.timeScale`, so a fast-forwarded run is bit-identical and §6.1 is untouched. The in-game clock is unaffected too — every hazard period and cooldown scales together, so §4.3's pressure is intact and only the human's waiting compresses. The cost is the player's own reaction time, which is why it is held rather than toggled. |
| 2026-08-25 | **Boot goes to the menu; a level exists only while it is being played** | The title screen had a room running behind it, which read as clutter rather than as the premise. The bootstrap now splits into what always exists and what a level needs, the latter under one root destroyed on quit. Teardown rather than hide-and-freeze: a hidden frozen world is invisible state, and invisible state is what produced the Results soft-lock. Determinism is unaffected and marginally better for it — every run now starts from a freshly built world rather than a `Restart()` on a reused one. |
