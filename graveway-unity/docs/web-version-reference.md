# Web version reference

Everything the web version of Graveway (`graveway/`) does, read from its
code in October 2026. The web version is frozen as the reference for the
Unity port, so this is the checklist for matching it: every mechanic,
number, screen and quirk that the Unity version has to reproduce (or
deliberately change). See [PORT_PLAN.md](../PORT_PLAN.md) for the plan.

Paths are relative to `graveway/`. Coordinates are the web version's:
it runs up the road toward −z, while the Unity version runs toward +z.

## 0. Files and line counts

| File | Lines | File | Lines |
|---|---|---|---|
| src/game.js | 1498 | src/save.js | 138 |
| src/ui.js | 1008 | src/input.js | 120 |
| src/models.js | 923 | src/main.js | 109 |
| index.html | 614 | src/director.js | 87 |
| src/world.js | 493 | src/fog.js | 85 |
| src/props.js | 464 | src/weapons.js | 77 |
| src/config.js | 433 | sw.js | 65 |
| src/vfx.js | 393 | src/preview.js | 57 |
| src/audio.js | 260 | | |
| src/nav.js | 227 | | |
| src/updater.js | 219 | | |

The 19 files total 7,270 lines.

Other files:
- `vendor/three.module.min.js` (three r170)
- `fonts/bebas-neue.woff2`
- `icons/icon-192.png` and `icon-512.png`
- `assets/icon.png` and `splash.png` (sources for the Android launcher icon and splash)
- `manifest.webmanifest`, `android.json`
- `../tools/android/build-apk.mjs`, `../.github/workflows/android.yml`

There are no audio, model or image files apart from the icons and the font.

---

## 1. Per-file inventory

### main.js (109): bootstrap

**Capacitor detection:** `native = window.Capacitor?.isNativePlatform?.() ? Capacitor : null`. The App plugin is detected through `PluginHeaders`, and it is called with `native.nativePromise(...)` because Capacitor's JS is not bundled.

**Start-up:**
- Waits up to 1.5 s for the Bebas Neue font, because canvas textures use it (the CONDEMNED sign).
- Builds the `UI` and `Game`. If WebGL fails it shows `#nogl` and hides the menu.
- Builds `Input(#touch, #joy, #joy-knob)` and `Updater(native)`, then calls `ui.attach` and `game.showMenu()`.
- After 300 ms, `ui.setPortraits(game.renderPortraits())`.

**Events:**
- Audio unlocks on the first `pointerdown` or `keydown` (capture phase).
- The `contextmenu` (long-press) menu is blocked everywhere except textareas.
- `visibilitychange` to hidden calls `ui.pause()`.

**Android:**
- App `backButton` calls `ui.back()`.
- App `pause` calls `ui.pause()`.
- `ui.onExit` calls App `exitApp`.
- App `getInfo` sets the version label: `v{version} (build {build}) · Android`.

**Live-update flow (native only):**
1. `updater.init()`.
2. Wait until 10 frames have been drawn.
3. `updater.confirm()`.
4. `updater.check()`; if a build is ready, `ui.updateReady()`.
5. On App `resume`, check again at most every 15 minutes.

**Frame loop:** `requestAnimationFrame`.
- Raw dt is clamped to `min(raw, 1/20)`.
- fps is smoothed: `fps += (1/raw - fps) * 0.05`.
- Each frame calls `game.update(dt, input)` then `game.render()`.

**Other:**
- Registers the service worker only on the web (https or localhost), never in the app.
- Exposes `window.graveway = { game, ui, save, input, updater }` for debugging.
- Sets `document.documentElement.dataset.ready = '1'`, which the update watchdog checks.

**Tunables:** none.

### config.js (433): every tunable

The full contents are summarised in section 3. Helper functions:
- `weaponDef`, `characterDef`
- `pickPowerup(r)`: a weighted pick
- `partsFor(def, slot)`: filters parts by `fits` and `not`
- `fittedPart`
- `partCost`: `round(price × (1 + tier × 0.35) / 5) × 5`, where tier is the gun's index in `WEAPONS`
- `weaponStats(def, fitted, perk)`
- `playerStats(character, ups)`
- `stageScaling(stage)`
- `playerUpgradeCost`: `round(baseCost × 1.65^level / 5) × 5`

`weaponStats` applies each fitted part's mods in turn:
- Multiplies damage, fireRate, range, ammoCapacity, reloadTime, spread and knockback.
- `recoil` multiplies `recoil.per`.
- `stability` divides `recoil.max` and multiplies `recoil.recover`.
- `pierce` is added; `pellets` replaces the value.
- `blast` multiplies the blast radius and `impulse` multiplies the blast impulse.
- Merges each part's `fx`.

Then:
- `fx.noPierce` sets pierce to 0.
- `ammoCapacity = max(1, round(...))`.
- `reloadTime *= perk.reloadMult`.

### game.js (1498): the `Game` class

The run, player, auto-aim, firing, zombies, bullets, grenades, explosions, coins, items, power-ups, camera and HUD state. It talks to the UI only through `hooks` (`hud`, `banner`, `toast`, `hurt`, `finish`).

**Renderer and scene**
- `WebGLRenderer({ antialias: true, powerPreference: 'high-performance', stencil: true })`. The stencil buffer is needed by the fog of war.
- Clear colour and fog colour `0x0d1418`. ACES Filmic tone mapping, exposure 1.15.
- Linear `Fog(0x0d1418, 20, 60)`, refitted every frame by the camera.
- `PerspectiveCamera(60, 1, 0.1, 90)`. FOV is reset in `resize()` to 68 in portrait and 52 in landscape.
- Pixel ratio is `min(devicePixelRatio, 2)`, or 1 on the low quality setting.

**Lights**
- Hemisphere light `0x8aa0c8 / 0x2a2018`, intensity 1.25.
- Directional "moon" `0xb0c4ff`, 0.9, at (−8, 20, 6).
- **Flashlight** spotlight `0xfff0d0`: intensity 70, distance 34, angle 0.6, penumbra 0.55, decay 1.2.
  - Sits 0.3 m ahead of the survivor at height 2.3 and aims 9 m ahead along the survivor's facing.
  - The weapon-light part raises it to intensity 105, distance 48, angle 0.8.
  - Hidden while dying.
- **Muzzle point light** `0xffc070`: distance 10, decay 1.5. Intensity 28 for 0.045 s per shot.
- **Blast point light** `0xff9040`: distance = radius × 4. Peak intensity `60 + radius × 12`, fading as k² over 0.3 s. One light is shared by all explosions and keeps the higher peak.
- No shadow maps anywhere: shadows are fake blob decals.

**Instanced meshes** (dynamic draw, frustum culling off)

| Mesh | Cap | Geometry and material |
|---|---|---|
| Coins | 500 | Cylinder r 0.24, 0.07 thick, Lambert `0xffc928`, emissive `0x7a5200` |
| Tracers | 260 | Box 0.06×0.06×1.1, additive, colour per instance |
| Particles | 700 | Unit cube, Basic material, colour per instance |
| Grenades | 24 | Sphere r 0.13, Lambert with emissive |

**Other meshes:**
- Muzzle flash: a 0.9 plane with the glow texture, additive, rolled randomly. Scaled 1.8 for multi-pellet guns and the launcher, otherwise 1.1–1.6.
- Laser sight: a 0.025 red box, additive, opacity 0.75, stretched from the muzzle to the target.

**Colour tables:**
- Tracers by ammo: fmj and buck `ffe2a0`, hollow `fff3dc`, ap `9fd2ff`, incendiary `ff8a30`, slug `ffffff`, dragon `ff6a1a`, flechette `d2e2ff`. All tracers turn red-hot `ff7a5a` under Double Damage.
- Grenade shells: he `4a5a2a`, incendiary-g `a8481a`, cluster `8a7a2a`, concussion `3a5a7a`.

**Survivor models**
- `setCharacter(id)` builds one model per character the first time it is needed.
- All six guns are attached to its body, hidden.
- `fitGun(w)` rebuilds a gun model when its fitted parts change (the key is `JSON.stringify(fitted)`).
- `muzzle()` works out where the muzzle is in the world from `GUN_MOUNT` and the gun's length.

**States:** `menu`, `playing`, `paused`, `dying`.

**Menu scene: `showMenu()`**
- World seed 7. The survivor stands at z −6 facing up the road.
- 9 "calm" walkers wander 12–34 m up the road. Calm zombies never notice gunfire.
- The camera sways (see section 8).

**Starting a run: `start()`**
- Starting point: `startZ = START_PZ − settings.startAt` (a playtest tool).
- Seed: `(Date.now() & 0xffff) ^ 0x5bd1`, fed to a mulberry32 RNG.
- Builds a new `World`. Coins along the road are spawned through `world.onCoins`.
- `world.lotsOfPowerups` comes from the playtest setting.
- Resets the director and the nav grid.
- The camera starts at (0, 30, startZ + 30) and swoops down.
- Banner: the survivor's name, then "Get as far up the road as you can" or "Starting at N m".

**Distance and stage**
- Distance is the **furthest** point reached: `START_PZ − player.furthest`. You can walk back without losing it.
- `stage = floor(distance / 100)`.

**Player: `updatePlayer`**
- Speed is `stats.speed`, × 0.75 on rubble (`nav.isRough`).
- Zombies are solid. On overlap the player is pushed 90% of the way out of a big zombie (scale > 1.2) and 65% out of others; the zombie takes the rest.
- Then `resolveColliders` against props.
- x is clamped to ±7.4 (`WALK_HALF`).
- The player can't go past `startZ + 4` (the start barricade) or more than 20 m back down from the furthest point.
- Facing:
  - Turns toward the target with smoothing `1 − e^(−22·dt)`.
  - With no target it turns toward the move direction with `1 − e^(−12·dt)`.
- Buffs count down. Kick decays at 9/s. The 0.25 s weapon-swap delay counts down.
- Firing: `w.tick(dt, hasTarget)`. Fires only when the weapon can fire, the swap delay is over, and the gun is within **0.3 rad** of the target direction.

**Auto-aim: `chooseTarget`**

1. **Zombies.** Considered if alive, `seen` (fog of war), and within `[minRange, range]`.
   - Score = distance, +4 if the zombie is more than 1 m behind the player (down the road), −1.5 for the current target (stickiness).
   - Bullet guns:
     - Skip if a building front is in the way (`world.facadeT`).
     - If a prop is in the way: skip if it's indestructible (`hp === null`, i.e. wrecks); skip if it's explosive and the player is within its blast radius + 1 of it; otherwise score +8 (shoot through breakable cover).
   - Grenade launcher: skip if a prop taller than 2 m stands within blast radius + 0.5 of the player on the line of fire. The arc clears low things.
2. **Explosive barrels.**
   - Only when the player is between r + 1 and range away (r = 3.8).
   - For bullet guns the ray must actually reach the barrel.
   - At least **2** live zombies within r of the barrel. Score = distance − 3 × count.
3. **Condemned buildings (launcher only).**
   - Within range + 4, with the player not inside the building's fall footprint (pad 1).
   - At least **3** zombies inside the footprint.
   - Aims at the building's base on the road side (1.6 m out). Score = distance − 3n.

Returns `{ x, z, prop, zombie }`.

**Firing: `fire(target)`**
- Damage × 2 under Double Damage.
- `consume()`: under Rapid Fire the rate is × 2 and no ammo is used.
- Grenades: the landing point is the target jittered by ±(spread × 4) in x and z; then `lob()`.
- Bullets: one per pellet.
  - Direction = yaw + random(±spread), where spread includes recoil bloom.
  - Speed 62–70 m/s; life = range / speed + 0.05.
  - Each bullet starts at the **survivor's centre**, not the muzzle (so a gun pushed against a wall can't fire through it).
  - Carries pierce, knockback, fx and colour.
- Muzzle light, flash, kick = 1, camera shake = `max(shake, gun.shake)`, `sfx.shot(model)`.
- Gunfire `noise()` unless the gun is suppressed (`fx.quiet`).
- Auto-reloads when the magazine empties.

**Noise:** at most every 0.25 s, idle non-calm zombies within `GUNFIRE_NOISE` (22 m) start chasing.

**Ignite:** a burning zombie takes `dps` damage per second for `time` seconds. A new flame doesn't stack: the zombie keeps the longer time and the stronger dps.

**Lob:** fixed horizontal speed (15 by default, 11 for Frag Barrage). The vertical speed is solved so the shell lands on the target: `vy = (0 − y + 0.5·g·t²)/t`, with g = 20 and `t = max(d/speed, 0.15)`. Life is t + 0.6. A `safe` flag means it can't hurt the player.

**Reload and swap**
- `reload()` only works if the magazine isn't full.
- `selectWeapon(i)`: cancels any reload, 0.25 s swap delay, auto-reloads if the new gun is empty, `sfx.swap`.
- `swapWeapon(±1)` cycles.

**Hurting the player: `hurtPlayer`**
- Only while playing.
- Invincible playtest setting: damage becomes 0, but the flinch still happens.
- Blast damage × (1 − explosionResist).
- Armor soaks `min(armor, dmg × ARMOR_ABSORB)`; `ARMOR_ABSORB` is 1, so armor takes everything first.
- Hurt timer 0.35, shake 0.35, UI vignette flash, `sfx.hurt`, vibrate 45 ms, 6 blood particles.
- The player is shoved away from the source: 0.35 m, or 0.8 m for blasts.
- At hp ≤ 0: state `dying`, `sfx.lose`.

**Dying:** the body falls over (rotation.x = −π/2 × k², k over 0.6 s). After 1.6 s, `finish()`.

**Survivor animation: `syncSurvivor`**
- Legs swing ±`0.7 × min(moving × 1.5, 1)` on a walk phase that advances by `11 × speed fraction`.
- Body bobs `|cos| × 0.06`.
- Arms point forward: `−π/2 + 0.12 + kick × 0.18`, plus 0.6 while reloading; the left arm also pumps `sin(t × 18) × 0.3` while reloading.
- The gun slides back `kick × 0.12`.
- Only the current gun is visible.

**Zombies: `spawnZombie` and `updateZombies`**

Per zombie:
- Stats scaled by the stage it spawned in.
- Walkers get a random scale of 0.92–1.08.
- Speed rolled within the type's range × the stage speed multiplier.
- Random yaw and lean (0.1–0.35), head tilt ±0.3.
- An alley spawn is flagged `emerging` until it reaches x < 7.1.

Despawn: more than 45 m behind the player (60 m for the boss; an outrun boss clears the boss bar).

Burning: ticks every 0.25 s; wakes an idle zombie; flame sprites at about 14 per second.

Fog of war: every 0.1 s, `canSee(world, player, zombie, radius)`. Seen at once when in view; hidden after **0.15 s** out of view. `model.visible = seen`.

AI states:
- **idle**: every 1.5–4.5 s picks a new heading. 70% of the time it walks at 0.35 × speed, otherwise stands. Starts chasing when the player is within `aggro` (40% chance of a groan).
- **chase**:
  - Switches to attack when within `reach × scaleRatio + player radius`.
  - Line-of-sight check every 0.25 s: distance < 14 **and** `nav.clear()` along grid cells. With line of sight (or while emerging) it walks straight at the player.
  - Otherwise it follows the flow field via `nav.steer`.
  - If there's no path and it's inside the grid, it is marked `stuck` and walks straight.
  - Speed × 0.55 on rubble.
- **attack**:
  - Period = 1 / attackRate. Wind-up = `min(0.42, period × 0.5)`.
  - Lands if the player is within `reach + player radius + 0.35`.
  - Boss hit: `sfx.slam` and shake 0.6.
  - Returns to chase after the period.
- **smash**:
  - Beats on the prop in the way with the same wind-up.
  - Damage per hit: zombie damage × 3 for smashers (tank, boss) or × 1.5 for a stuck zombie, with cause `'smash'` (no damage multiplier), plus `sfx.thunk`.
  - Stops when the prop is destroyed, or the player is within 2 m.

Movement and collision:
- Separation: an O(n²) pass with an early reject; overlapping zombies are each pushed half the overlap apart.
- Knockback velocity `kx, kz` decays with `1 − e^(−9·dt)`.
- `resolveColliders` returns the prop bumped. While chasing, a smasher or a stuck zombie that bumps a breakable prop goes into **smash**.
- A smasher that bumps an indestructible prop with mass (a wreck) shoves it: impulse `(boss 160 | tank 60) × dt / mass / speed` times its velocity.
- x is clamped to ±7.4 unless emerging; z can't go past `startZ + 4.5`.
- Zombies can't overlap the player (the zombie takes half the push).
- Facing turns with `1 − e^(−3·dt)` when idle and `1 − e^(−8·dt)` otherwise.

Zombie animation:
- Phase speed `(1.2 + speed × 2.2) / scaleRatio`. Legs swing up to 0.75.
- Body pitch = lean, +0.25 for runners. Roll `sin × 0.06`, bob 0.05, head wobble.
- Arms stretched forward (zombie pose) with sway. When attacking they raise to −π/2 − 0.9, then strike to −π/2 + 0.5.

Hit flash: all meshes swap to `FLASH_MAT` (`0xffd8c8`, unlit) for 0.07 s.

Ambient groan: every 2.5–6.5 s from a non-idle zombie within 18 m of the player, panned.

**Damaging and killing zombies**

`damageZombie`:
- Wakes an idle zombie.
- Knock: `knock / scale²` × 2.2 into the knockback velocity.
- Hit flash, 3 blood particles (`0x6e0b0b`), `sfx.hit` panned by `(x − player.x)/10`.

`crush(fp, dmg)` (falling props):
- Zombies inside the footprint take dmg with knock 2 and launch 3.
- The player takes `min(dmg × 0.35, 70)`.

`killZombie`:
- A launch over 0.5 throws the body (vy = launch, random tumble ±5 rad/s).
- The body falls away from the shot. 10 + 6 × scale blood particles. `sfx.die`.
- Coins: the type's coin count, +1 with 30% chance, flung out at 1.5–4 m/s and 4–7 m/s up.
- **Boss:** shake 0.8, banner "Boss down!" / "+100 coins on the ground", slam sound, drops armor and a random power-up.
- **Tank:** 30% chance of a health pack, else 25% chance of a power-up.

Death animation:
- Falls flat over 0.45 s with arms raised. Knockback slides it.
- A thrown body flies with gravity 20 and puffs dust when it lands.
- After 3 s it sinks into the ground at 0.5 m/s; removed at 4.5 s.

**Bullets: `updateBullets`** (swept, no tunnelling)
1. Each step, find the first prop that stops bullets (`rayProps`) and the building front (`facadeT`); the wall is whichever is nearer.
2. Find the first zombie hit on the segment (hit radius = zombie radius + 0.12).
3. Hitting a zombie:
   - Damage × `fx.big` (tanks and bosses) or × `fx.small` (walkers and runners).
   - Ignite if the ammo has `fx.fire`.
   - If pierce is left, decrement it and remember the zombie; otherwise the bullet stops.
4. Hitting a wall or prop:
   - Damage to the prop × `fx.props`.
   - `vfx.impact` at height `min(1.35, top − 0.08)` (1.4 for building fronts).
   - Ricochet sound on metal, chip sound otherwise.

**Grenades: `updateGrenades`**
- Gravity 20. A smoke puff trails behind (60% chance per frame). The shell spins.
- Explodes on any of:
  - y ≤ 0.15
  - life runs out
  - touching a zombie (y < 2 × scale and within radius + 0.2)
  - touching a prop below its height, but only once more than 1.5 m from where it was fired
- Explosion uses `{ mine: true, scorch: true, safe, fire }`.
- **Cluster:** n bomblets at angle `k/n × 2π + random`, distance radius × 0.8–1.3, with radius × 0.6, damage × 0.55 and impulse × 0.5, delayed 0.14 + k × 0.09 s.
- `explodeLater` puts explosions in a delayed queue (`pending`).

**Explosions: `explode(x, z, {radius, damage, impulse}, opts)`**
- Falloff `f = max(0.25, (1 − min(d/r, 1))^0.7)`.
- Zombies within r + their radius: damage × f, knock impulse × 0.5, launch `(4 + impulse × 0.35) × f / scale`, ignite if the blast has fire.
- Player, unless `safe`, when within r: damage × f × (0.4 for your own grenades, 0.6 for anything else), flagged as a blast.
- Props within r + their radius: damage × falloff measured from `d − r_prop × 0.5`, cause `'blast'`.
- `propSys.impulse(x, z, r × 1.3, impulse)`, then marks the nav grid dirty.
- Effects:
  - `vfx.explosion`
  - blast light
  - shake `max(0.25, 0.6 + 0.06r − d_player/30)`
  - `bigBoom` sound for vehicles or r > 6, else `boom`
  - vibrate 60 ms if the player is within 10 m, else 25 ms
  - scorch decal of size r × 1.2 (added to the chunk)

**Coins: `updateCoins`**
- Dropped coins fall (gravity 22) and bounce: y floor 0.35, vy × −0.35, horizontal × 0.6; they settle when |vy| < 0.8.
- Removed when more than 45 m behind the player.
- Pull starts when within the magnet radius and the coin is older than 0.35 s:
  - normal pull speed `9 + (mag − d) × 4`, never overshooting
  - under Vacuum: radius 32, speed `16 + 1.5d`
  - a pulled coin rises toward y 1.1
- Collected within 0.7 m: `run.coins += coinMult` (can be fractional for Wren), `sfx.coin`.

**Items: `updateItems`**
- Spin 1.6 rad/s. Bob `0.15 + sin(3t + x) × 0.1`. A power-up's ground decal pulses `2.4 ± 0.3`.
- Picked up within 1.1 m:
  - Health is refused at full HP; armor is refused at 100.
  - Health +35 HP, toast "+35 health" (green).
  - Armor +50 (max 100), toast "+50 armor" (blue).
  - Power-up: `powerUp(id)` plus 16 coloured particles.

**Power-ups: `powerUp`**
- Toast `"{name} · {tag}"` in the power-up's colour, `sfx.powerup`, vibrate 30, ground ring.
- Timed ones add their duration, capped at 2× the duration; `buffMax` drives the HUD bar.
- **Rapid Fire** also refills every gun and cancels reloads.
- **Frag Barrage** (`fragBarrage`):
  - Among zombies within 18 m (ignoring those closer than 3 m), picks for each of 4 grenades the zombie with the most others within 3.6 m.
  - Skips zombies within 3.6 m of a crowd already aimed at.
  - Spare grenades go to random points 7–13 m ahead (x ±4.5).
  - Thrown 0.14 s apart from the player at height 1.7, speed 11, `safe`, damage 150 (× 2 under Double Damage). Each re-aims at its zombie's current position, `sfx.toss`.
- **Shockwave** (`shockwave`):
  - Zombies within 7 m: f = `max(0.35, 1 − d/7)`, damage 180f, knock 24f, launch 5f/scale.
  - Props are only shoved (radius 7, strength 24 × 0.6), then the nav grid is marked dirty.
  - `vfx.shockwave`, shake 0.5, `sfx.shock`, vibrate 60.
  - Never hurts the player.

**Particles (blood, pickup bursts): `burst` and `updateParticles`**
- Speed and spray parameters per call; default gravity 14.
- Floor at y 0.03 with 0.8 friction.
- Drawn as instanced cubes, rotated by velocity, shrinking in the second half of their life.

**Per-frame effects: `updateFx`**
- Flashlight, muzzle flash and blast-light timers.
- Instanced coins (placed coins bob 0.08), tracers (drawn 0.5 m behind the head at y 1.4), grenades, laser.
- Laser is visible when the gun has `fx.laser`, there is a target, and the game is playing.
- Particles, then `propSys.draw()` and `vfx.draw(fog)`.

**Results: `finish()`**
- Result: `{ character, distance, stage, kills, coins = floor(run.coins), bonus, total = coins + bonus, time, best, startAt }`.
- `save.coins += total`, `save.kills`, `save.runs++`.
- `save.best` updates only if the run didn't start further up the road.
- `hooks.finish(result)`.

**HUD state: `hudState()`**
- distance, stage, stageProgress
- coins = wallet + floor(run coins) + bonus
- hp, maxHp, armor, maxArmor 100
- weapons, wi, kills
- boss hp fraction (or null)
- `low` when hp < 30%
- `free` under Rapid Fire
- buffs `[{ id, left, max }]`
- fps (only if the setting is on), god, zombies alive

**Portraits: `renderPortraits(256)`**
- A throwaway second WebGL renderer, ACES exposure 1.2.
- Hemisphere `c8d8ff / 3a2a20` 1.6, key light `fff0dd` 2.2 at (2, 3, 4), green rim `9be15d` 1.6 at (−3, 2, −3).
- Camera FOV 30 at (1.1, 1.75, 3.2), looking at (0, 1.45, 0).
- Each survivor holds their own gun with its fitted parts, scaled 0.7, arms posed (−1.3 and −1.2), root turned 0.7 rad.
- Saved as PNG data URLs. The context is disposed and force-lost afterwards.

**Tunables read:** `WEAPONS`, `ZOMBIES`, `PICKUPS`, `ARMOR_ABSORB`, `DIRECTOR.stageLength`, `CHARACTERS`, `RUBBLE_SLOW`, `RUBBLE_SLOW_PLAYER`, `POWERUPS`, `POWERUP_DROPS.tank`, `GUNFIRE_NOISE`, plus the helpers `playerStats`, `stageScaling`, `characterDef`, `pickPowerup`.

**Hard-coded in game.js:** the caps above, `GRAVITY` 20, the bullet speed, the 0.3 rad firing cone, the despawn distances, the noise throttle, and the line-of-sight, visibility and nav rates.

### weapons.js (77): the `Weapon` class

- Holds `def`, `fitted`, `stats` (from `weaponStats`), `ammo`, `reloadT`, `cooldown`, `bloom`.
- `spread = stats.spread + bloom`.
- `tick(dt, firing)`:
  - Cooldown goes down, floored at **−0.05** so leftover time carries into the next shot and fire rate isn't rounded to whole frames.
  - Bloom only recovers (`recoil.recover`/s) when **not** firing, i.e. when there is no target.
  - When a reload finishes: magazine full, bloom 0, returns true (the reload sound plays).
- `canFire()`: not reloading, ammo > 0, cooldown ≤ 0.
- `consume(rateMult, free)`: cooldown += `1/(fireRate × mult)`; ammo−− unless free; bloom = `min(bloom + recoil.per, recoil.max)`; returns whether the magazine is now empty.
- `startReload()` refuses if already reloading or full. `cancelReload()`. `reloadProgress` is 0..1.
- **Reserve ammo is unlimited.**

### director.js (87): `Director`

- `SPAWN_AHEAD = 52`.
- `reset(distance)`: stage from distance; first horde at distance + 6; first straggler at 4 s. The next boss is at stage 5, stepping by 5 until it reaches the current stage, so a run started at 500 m or 1000 m brings a boss at once.
- `update` on entering a new stage: `game.onStage(stage)`.
  - Adds `stageBonus(stage)` to the run's bonus.
  - Banner "Stage {s+1}" / "{s×100} m · +{bonus} coins · they're getting tougher" for 1.8 s, `sfx.milestone`.
- Boss: when `stage ≥ nextBoss`, `spawnBoss()`.
  - 38 m ahead at x 0, pushed clear of props (radius 1.3), chasing.
  - Banner "Something big is coming" / "Put it down for a fat reward", big groan.
- **Hordes** (by distance):
  - Next one after `hordeEvery(stage) × (0.75–1.25)` metres.
  - Count is random within `hordeSize(stage)`.
  - With probability `sideAmbush(stage)`, if there's an alley 12–40 m ahead, they come out of that alley chasing (banner "Ambush!" for 1.2 s if 3 or more).
  - Otherwise they spawn idle 52–60 m ahead.
- **Stragglers** (by time):
  - Next one after `trickleEvery(stage) × (0.7–1.3)` seconds.
  - 20% (from stage 1): one from behind, 22 m back, chasing.
  - 25%: one from an alley within 30 m.
  - Otherwise 1, or 2 with 30% chance, 46–52 m ahead.
- `spawnGroup` (in game.js):
  - Clamped to `maxActive(stage) − alive`.
  - Road spawns: x ±6.25, z ±4, pushed out of props (pad 0.6).
  - Alley spawns: `alley.x + side × rand × 2.5`, z ±1.
- `pickKind`:
  - Tank with chance `tankShare` (from stage 3).
  - Runner with chance `runnerShare` (from stage 1).
  - Otherwise walker.

**Tunables read:** all of `DIRECTOR`.

### world.js (493): `World`, the endless street

**Constants:**
- `ROAD_HALF` 6: asphalt from −6 to 6.
- `WALK_HALF` 7.4: sidewalk edge, the movement limit.
- `START_PZ` 2.
- `FACADE_X` 8.0: where building fronts are.
- `CHUNK` **32 m**.
- `EDGE` 14: chunk 0 starts here.
- `BUILD_AHEAD` 78 m, `KEEP_BEHIND` 40 m.
- `mulberry32` RNG. Each chunk is seeded by `seed × 7919 + i × 104729`, so the same run seed always lays out the same road.

**Streaming:**
- Builds chunks up to 78 m ahead and drops chunks more than 40 m behind; dropped chunks are never rebuilt.
- Dropping disposes geometry, marks props `removed` (which stops their emitters), filters props, rubble, items, alleys and animations, and marks nav dirty.

**Chunk contents: `build(i)`**
1. **Ground:** 4 shared meshes.
   - Road plane 12 × 32 (road texture repeated ×2).
   - Sidewalks: merged 3 × 0.16 boxes on both sides.
   - Kerbs: 0.18 × 0.2.
   - Dark under-plane 80 wide.
2. **Buildings on each side.**
   - Segments 6–13 m long, height 6–24, depth 7–11.
   - 18 facade materials: 6 tints × 3 window textures with matching emissive maps (emissive intensity 0.85, lit windows glow).
   - Box UVs scaled to real size (`facadeGeo`, 8 m tile).
   - 30% chance of an awning (one of 3 colours) at y 3.
3. **Alleys.**
   - 15% per segment (when the segment is longer than 4 m and more than 8 m past the start).
   - 3.2 m wide gap with a 14 m back wall at x ±16.4.
   - Recorded as `{ x: ±9.0, z, side }`. These are ambush points and holes in the building-front ray test.
4. **Condemned building** (set piece).
   - When `i % 7 == 5` and the chunk is more than 40 m past the start; random side.
   - 9 m wide, 13 m tall prop at `±(FACADE_X + 0.6)`, rotated to fall into the road.
   - A plain 7 × 9 × 11 building stands behind it, for after the collapse.
5. **Lamps.** Two per chunk, at chunk start − 6 and − 22, alternating sides, at x ±6.6. They are props (stop bullets, topple). 22% flicker: a sine-based on/off of the head and its light pool.
6. **Litter:** 11 random boxes merged into one mesh (70% on sidewalks).
7. **Start barricade** (the chunk holding `startZ + 5.4`).
   - 6 concrete jersey barriers from x −6 to 6, spaced 2.4.
   - A "bus" (a car scaled 1.2 / 1.2 / 1.4) turned across the road behind them.
   - Decoration only; the hard limit is in code.
8. **Prop rows.** First row at chunk start − 2..8, then every 9–15 m, only once more than 14 m past the start (`placeRow`):
   - **Tanker:** more than 120 m in and 6% chance. x ±2.4–4.4, roughly along the road.
   - **r < 0.36: car(s).** x on one side (±1.4–4.8); 30% turned across the road (0.6–1.4 rad), else ±0.35. 45% chance of an explosive barrel ±2.6 m along the road. 25% chance of a second car on the other side at x ±3.9–5.
   - **r < 0.60: barricades.** 35% are a chokepoint: 4 barricades from x −4.8 with one 2.8 m gap at a random index. Otherwise 1–2 barricades, rotated ±0.25.
   - **r < 0.75: a cluster of 2–4 explosive barrels.**
   - **r < 0.92: brick wall,** 3.5–6.5 m wide, against one sidewalk edge.
   - **Otherwise: fire barrel** (animated flames) at x ±5.
9. **Coins.**
   - A line every 14–22 m: 4–7 coins spaced 1.3 m along the road, x ±5, 40% curving (±0.5 per coin), clamped to ±6.5.
   - Skipped where a prop is in the way, or within 6 m of the start.
10. **Pickups** (chunks more than 30 m past the start): 40% chance of a health pack, 25% chance of an armor vest.
11. **Power-ups** (more than 60 m past the start): 50% chance of one per chunk. With "lots of power-ups" on: from 10 m, two per chunk. Type is a weighted pick.
    - `placeItem` tries 8 random x in ±5.5, clear of props by 0.8.

**Functions:**
- `addProp(kind, model, x, z, rot, chunk)` creates the prop record:

  ```
  { kind, def, chunk, x, z, y, rot, hw, hd, height, cos, sin, r = hypot(hw, hd),
    hp, maxHp, blocking: true, destroyed, group, meshes, model, flash,
    vx, vy, vz, va, moving, airborne }
  ```

  Used at load time and mid-run (slabs from a collapse).
- `addRough`, `addToChunk` (mid-run things like rubble and scorch marks belong to the chunk they landed in), `chunkAt`.
- `facadeT(x, z, dx, dz, len)`: where a ray meets the building-front plane at x = ±8.0, ignoring alley gaps (|z − alley.z| < 1.5).
- `makeItem`:
  - Power-ups: badge sprite plus a ring decal of size 2.4.
  - Health and armor: model plus a glow decal of size 2.2 (`4dff6a` / `4aa8ff`).
- `dropItem` puts an item where a zombie fell.
- `animate`:
  - Fire barrel flames scale-flicker on two sine waves, and the glow pool's opacity follows.
  - Flickering lamps toggle visibility.
- `resolveColliders(props, p, radius)`: circle against rotated boxes, solved in each box's local frame. Leaves by the nearest face if the centre is inside. Returns the last prop hit.
- `rayProps(props, x, z, dx, dz, len, test?)`: slab test in each box's frame. Returns the first `{ t, prop }`. The default filter is not destroyed and `def.blocksBullets`.

**Tunables read:** `PROPS`, `SET_PIECES`, `POWERUPS` (colours), `POWERUP_DROPS.perChunk` and `.from`, `pickPowerup`.

### nav.js (227): `NavGrid`, the flow-field pathfinding

**Grid:**
- `CELL` **0.5 m**.
- Covers x −7.4..7.4: **30 columns**.
- Covers 60 m ahead to 26 m behind the player: **172 rows**. That's **5,160 cells**.
- Typed arrays: `blocked` (Uint8), `rough` (Uint8), `dist` (Float32), plus a binary heap with lazy deletion (`Float32` keys and `Int32` values, size n × 8).

**Sliding window:** when the player is more than 6 m from the window's centre row, the window re-centres (`z0 = pz − 60`), marks dirty, and rebuilds at once.

**Rebuild throttle:** at most every **0.15 s**. Work happens only if the player's cell changed or the grid is dirty.

**Rasterize** (when dirty):
- Clears both arrays.
- Every standing, blocking, not-destroyed prop marks the cells under its rotated box, grown by **0.32 m** (`INFLATE`), as blocked. This includes wrecks (hp null), which still block.
- Rubble footprints mark cells as rough, without growing.
- The world sets `navDirty` on chunk build or drop, prop add, destroy, settle or stop, explosions, shockwaves and rubble.

**Flow (Dijkstra from the player's cell):**
- If the player's cell is blocked, starts from the nearest open cell in a ring search up to radius 4.
- 8 neighbours, step 1 or √2, × **3** on rough cells (`ROUGH_COST`).
- Never cuts a corner past a blocked orthogonal neighbour.

**`steer(x, z, out)`:**
- Picks the neighbour with the lowest distance (looks 2 rings out when the zombie's own cell is blocked, i.e. wedged in padding; no corner rule at reach 2).
- Returns a unit vector toward that cell's centre, or null if there's no path.

**Other functions:**
- `isRough` is used for both zombie and player slowdown.
- `clear(x0, z0, x1, z1)` samples the line every 0.25 m for blocked cells; used for zombie line of sight.
- `cellOf`, `cx`, `cz`; `reachable` is unused.

**How zombies use it:**
- Line of sight (distance < 14 and clear) means walk straight.
- Otherwise follow `steer`.
- No path means walk straight (`stuck`) and smash whatever breakable prop they hit.
- Tanks and bosses always smash breakable props they walk into, and shove wrecks with mass.
- Rubble isn't blocked, only costlier: zombies go round if cheaper, otherwise wade through at 0.55× speed.
- Fallen lamp poles count as rough.

**Unity note:** this is self-contained and ports directly to C# (or Burst/Jobs). A NavMesh with carving would be the alternative, but the rough-cost cells and the "no path, so smash" rule would need custom work.

**Tunables read:** none from config; uses `WALK_HALF`.

### props.js (464): `PropSystem`, destruction

**`damage(p, dmg, dx, dz, cause)`**
- Does nothing to destroyed props or `hp === null`.
- Cause multipliers: `'blast'` × `blastMult`, `'bullet'` × `bulletMult`, `'smash'` unscaled.
- Hit flash 0.05 s by swapping to `FLASH_MAT` (not on wrecks).
- Walls and condemned buildings darken: colour scalar `0.45 + 0.55 × hpFraction`.
- **Burns** (car, tanker), while hp > 0:
  - **Smoking** below `smokeAt` of max hp: smoke emitter following the prop, dy 1.3, rate 6, endless, size 1 (tanker 1.6), alpha 0.4.
  - **Burning** below `fireAt`: fire emitter, dy 1.1, rate 16, size 1.1 × big, spread 1, `sfx.ignite`.
  - Neither does damage over time; they are visual warnings only.
- At hp ≤ 0: `destroy`.

**`destroy`**
- Restores materials, marks nav dirty, normalises the hit direction.
- Car or tanker: `wreck()`.
- Otherwise `destroyed = true`, then:
  - Toppling props: `topple()`.
  - **Barrel:** hidden; 12 metal debris at speed 7; `explodeLater(def.explodes, 0.09 s if set off by a blast, else 0)`, which makes chains ripple; scorch decal 4.5.
  - **Others** (barricade, fire barrel, slab): hidden; 18 debris (barricade) or 12, in its material, at speed 5; dust ×5; rubble (`hw×2` by `hd×2 + 0.6`) if `def.rubble`; `sfx.crumble`; shake 0.2.

**`wreck()`: vehicle explodes**
- `wreck = true`, `hp = null`: it can never be destroyed again but keeps blocking bullets and paths.
- Swaps to burnt materials (body `1e1b18`, glass `0a0a0a`, lamps `1a1210`). Stops the smoke and fire emitters.
- `explodeLater(def.explodes, 0.12 s if set off by a blast, else 0, { vehicle: true })`.
- **Thrown:**
  - Airborne with vy 7–11 (tanker 5–7), plus random horizontal ±1.5.
  - Random tumble spin vector: x ±2.5 (tanker ±0.75), y ±1, z ±3.
- 18 or 30 metal debris.
- New fire emitter (life 11 s, tanker 16 s; rate 22 or 34; size 1.7 or 2.6; spread 1.6 or 3.4).
- New smoke emitter (life 30 s; rate 6 or 10; size 1.5 or 2.2; dark colour).
- Scorch 6.5 or 11. Tanker banner "Tanker down!".

**`topple(p, dx, dz)`**
- Fall direction:
  - Condemned building: always its local +x, into the road.
  - Wall: along its own local z, whichever way the hit pushed.
  - Lamp: the hit direction, flipped away from the buildings if |x| > 3.
- A lamp's head and light pool go dark.
- Rotation of π/2 about the axis (fz, 0, −fx) through the base, eased k², over `0.35 + 0.28 × √height`: lamp about 1.0 s, wall 0.78 s, condemned 1.36 s.
- **Condemned only: a creak phase of 1.3 s first.**
  - Shudder `sin(40t) × 0.012 × progress` plus a lean of 0.02 × progress.
  - 30% chance of dust per frame, shake 0.12.
  - Banner "It's coming down!" / "Get clear of the building" for 1.6 s, `sfx.rumble`, vibrate [40, 60, 40].
- Otherwise `sfx.crumble`.

**`footprint(tp)`:** a box from the base out along the fall direction, length = height, width = the prop's extent across the fall. Also used by the auto-aim.

**`land(tp)`**
- `game.crush(footprint, def.crush)`: wall 220, lamp 90, condemned 2000.
- **Lamp:** stays lying where it fell (still visible). Its footprint becomes rough ground. 6 debris, sparks ×10, dust, `sfx.clang`, shake 0.2.
- **Wall:** hidden. 26 debris, `dustCloud` over the footprint, rubble footprint, shake 0.45, `sfx.slam`.
- **Condemned:** `collapseRubble`.
  - Rubble-field mesh across the whole footprint (colour `75706a`), marked rough.
  - 2–3 **slab** props spread along the footprint (sizes w 1.6–3, h 0.9–1.7, d 1.2–2.4, random tilt and yaw). They block, have 320 hp, and leave rubble when broken. A slab is skipped if |x| > 6.4.
  - 60 concrete debris, `dustCloud` ×46, dust ×22 out to radius 9, ground ring of size 26, shake **1.1**, `sfx.collapse`, vibrate [120, 40, 200].
- The 13 m fall reaches from x ±8.6 to about ∓4.4, which covers most of the road.

**`leaveRubble`:** a merged rubble mesh plus a rough footprint in the chunk. `scorch(x, z, size)` adds a scorch decal.

**Debris: `spray`**
- Up to **420** instanced Lambert cubes. The oldest is dropped (FIFO) when full.
- Counts × 0.6 on the low quality setting.
- Material palettes:
  - metal `3a3c40 / 5a5c60 / 2a2622`
  - concrete `8d8a82 / 7a766e / 6a665e`
  - brick `8a5a44 / 7a4a36 / 6a6460`
  - wood `7a5a34 / 5a4024`
  - each with ±5% lightness jitter
- 15% big chunks (0.35–0.7), else 0.1–0.32. Life 6–10 s.
- Physics: gravity 22, bounce −0.3, friction 0.55, spin damping 0.5, come to rest when slow.
- Shrink over the last second while sinking 0.2. Drawn in one call (`draw()`).

**`impulse(x, z, radius, strength)`** (blast shove)
- Props with mass within radius + their radius: `k = strength × (1 − min(d, R)/(R + r)) / mass`.
- Adds velocity and random spin ±0.3k.
- k > 2.5: thrown into the air with vy = `min(12, 1.1k)` and a random tumble.
- Debris within 1.5R is kicked too.

**`update`: moving props**
- Hit-flash timers.
- **Airborne:** gravity 22; tumble by quaternion integration (`setFromAxisAngle(spin/|spin|, |spin|·dt)`, premultiplied).
  - Landing with vy < −4: bounce (vy × −0.28, spin × 0.45, horizontal × 0.6, shake 0.25, clang, dust).
  - Otherwise `settle()`: keep the yaw, set right-side up; if it landed upside down, flip onto its roof at y = height; nav dirty; dust.
- **Sliding:** deceleration 9 m/s², angular damping e^(−3t). Stops when speed < 0.05 and spin < 0.05, then nav dirty.
- Clamped to the sidewalk edge (`WALK_HALF − min(hw, hd)`) with a −0.3 bounce.
- **Toppling** as above.

**Tunables read:** the `PROPS` entries via `p.def`: hp, material, mass, explodes, burns, smokeAt, fireAt, rubble, topples, crush, collapse, bulletMult, blastMult.

### fog.js (85): fog of war

**What blocks sight:** `blocksSight(c)` = not destroyed, not airborne, height ≥ **2** (`SIGHT_HEIGHT`), and min(hw, hd) > 0.2.
- Blockers: walls (2.4), tankers (3.1), condemned buildings (13), plus building fronts via `facadeT`.
- Don't block: cars (1.6), barrels, barricades, slabs, lamps (too thin).

**`canSee(world, x, z, tx, tz, pad)`**
- True within 0.5 m.
- Otherwise casts a ray of length `d − pad` against sight blockers, then against building fronts (with alley gaps).
- `pad` is the zombie's radius. Zombies are checked every 0.1 s.

**`SightShadows`: shadow rendering**
- **No render target and no shadow map.** One dynamic `BufferGeometry` of up to **48 quads** (6 vertices each).
- Rebuilt every frame from the player's position (`sight.update`, also while dying):
  - For each blocker within 36 m (`REACH`) of the player, and more than its own radius + 0.3 away, find the two box corners with the extreme angles as seen from the player.
  - Extrude them 44 m (`FAR`) away from the player into a quad lying flat at **y 0.17**, so sidewalks are shaded too.
- Material:
  - `MeshBasicMaterial` `0x020406`, opacity 0.7, transparent, no depth write, double-sided, `renderOrder −2`.
  - **Stencil:** write on, ref 1, func NotEqual, zPass Replace. Each pixel is darkened only once where shadows overlap.
- Cost: an O(props) CPU loop, at most 288 floats uploaded, **one draw call**.
- Building fronts hide zombies but cast **no** shadow quad.

**Unity:** a dynamic `Mesh` with a custom stencil shader, or a ground projector or decal approach.

### vfx.js (393): `VFX` sprite effects

**Two `SpritePool`s.** Each is one `InstancedBufferGeometry` (a unit quad) with per-instance attributes `offset` (vec3), `tint` (vec4) and `sizeRot` (vec2), drawn with a custom `ShaderMaterial`:
- The vertex shader billboards in view space: `mv.xy += rotate(position) × size`.
- The fragment shader applies its own linear fog (`smoothstep(fogNear, fogFar, depth)`):
  - Additive pool: `rgb × a × (1 − fog)`, blend One/One.
  - Normal pool: `mix(col, fogColor, fog)` with alpha `a × (1 − 0.6 fog)`.
- **glow** (additive): `TEX.flame`, max **420**, renderOrder 3.
- **smokes** (normal blend): `TEX.smoke`, max **300**, renderOrder 2.
- When full, the oldest sprite is recycled.

Per sprite: x/y/z, velocity, `drag` (exp), `lift` (buoyancy; negative means it falls), `floor`, `life`, `s0 → s1` with a `grow` easing exponent, `c0 → c1` with `colorSpeed`, alpha `a`, `fadeIn` (fraction), `fadePow`, rotation and spin.
- fade = `min(1, k/fadeIn) × (1 − k^fadePow)`.
- size = `s0 + (s1 − s0)(1 − (1 − k)^grow)`.

**Ground shock rings:** 6 pooled meshes (ring texture, additive, y 0.06). Scale `0.5 + size × (1 − (1 − k)^2.5)`, opacity `0.9(1 − k)`.

**Quality scale:** 1, or **0.5** on Battery saver. `n(count)` scales counts.

**Recipes** (s = size/4):
- `explosion(x, y, z, size)`:
  1. Flash: a 0.09 s sprite of size × 1.1 → 1.7.
  2. Fireball: `22s + 6` sprites; the first 4 are white-hot cores (`fff0c8`), the rest orange `ff8a24` or deep `e24a0c`, cooling to `4a1206`. Drag 3, lift 1.6, life 0.6–1.2.
  3. Smoke: `14s + 4` sprites, dark `262422` to light `8a8680`, life 3–6 s, rising and spreading.
  4. Sparks: `26s + 8`.
  5. Embers: `10s`, drifting up for 2–4 s.
  6. Dust ring along the ground ×`10s + 4`, plus a ground ring of size × 3.2.
- `sparks(n, speed, life, dir?)`: a cone back toward the shooter when `dir` is given. lift −18, floor 0.04, colour `ffd890 → ff5a14`.
- `dust(x, z, radius, n, color, life)`: a ring rolling out along the ground.
- `dustCloud(x, z, w, d, rot, n, height)`: an area cloud for collapses.
- `impact(material)`: metal gives 5 sparks back toward the shooter; concrete, brick or wood give 2 grit puffs (brick `9a6a52`, wood `8a6a44`, else dust colour) plus a 40% chance of one spark.
- `ring`; `flame` (a lick of flame on a burning zombie).
- `shockwave(x, z, radius, color)`: flash + two rings (radius × 2.4 coloured, radius × 1.4 white) + dust + 24 sparks.
- `pop` is unused.

**Emitters:** `emit({ kind: 'fire'|'smoke', follow: prop, dy, rate, life, size, spread, alpha, color })`.
- Follows the prop's position, and stops when the prop's chunk is dropped.
- Spawn rate × quality scale × fade-out over the last 1.5 s.
- Fire emitters add a glow flame each, plus a smoke puff 35% of the time.

### models.js (923): every model and texture, built in code

**Helpers:**
- `boxGeo`.
- `mergeGeos`: a custom merge into one indexed buffer (Uint32 indices above 65,535 vertices).
- `lambert` and `basic`: material caches keyed by colour and options (a texture is keyed by its uuid).
- `canvasTex`: sRGB, anisotropy 4.

**Canvas textures (`initTextures`):**
- Power-up badges ×5, 128 px: a dark disc ringed in the power-up's colour with a symbol:
  - frag: grenade with lever and pin
  - rapid: lightning bolt
  - double: "2×"
  - shock: dot, ring and four arcs
  - vacuum: horseshoe magnet
- glow (radial gradient), ring (radial ring), fire (teardrop flame 64 × 128).
- road (256 × 512): asphalt speckle, 7 cracks, 4 dark-red stains, white edge lines, dashed yellow centre line.
- sidewalk (128 px slabs).
- 3 facade variants with 4 × 4 windows (18%, 23%, 28% lit; warm `ffcf7a` or cool `9fd4ff` in the emissive map; 15% of unlit windows broken).
- brick, scorch (blotches), smoke (16 soft blobs with the edges faded).
- flame (hot core).
- condemned (grimy panels, dead windows, cracks).
- danger: yellow and black stripes with "CONDEMNED" in Bebas Neue.
- sign: "SAFE ZONE" in green. **Unused.**

**Shared geometry:**
- Humanoid: leg 0.24×0.82×0.26, arm 0.19×0.72×0.2, hand 0.16, torso 0.62×0.72×0.36, head 0.4×0.42×0.4, eyes.
- Backpack; headgear: cap, helmet, goggles, hood.
- Grenade sphere, flat plane, coin, tracer, particle cube.
- `FLASH_MAT` `ffd8c8` (Basic).
- `blobShadow(size, opacity)`: a black glow-texture decal (y 0.02, renderOrder −1). These are the only shadows in the game.
- `glowDecal`: an additive ground light pool.

**`makeHumanoid`:**
- Groups `root → body`, with pivots at the hips (±0.15, 0.82), shoulders (±0.42, 1.48) and neck (0, 1.54).
- Torso at y 1.18.
- Bare arms merge with their hands; sleeved arms are separate meshes.
- Eyes are Basic, unlit, ignoring fog, so zombie eyes glow in the dark.
- Blob shadow 1.1.
- Returns `{ root, body, legL, legR, armL, armR, head, meshes }`.

**`makeZombie(kind, rng, eyeColor)`:** random skin, shirt and pants from palettes.
- Runner: pale skin `a9b49c`.
- Tank: shirt = skin `5f7a50`.
- Boss: `7a5a50 / 5a3a34`.
- 50% of walkers have sleeves.
- Eye colour from config: walker `d8ff6a`, runner `ff3a2a`, tank `ffa020`, boss `ff2020`.

**`makeSurvivor(look)`:**
- Coloured humanoid with dark eyes.
- Green ring decal underfoot (`9be15d`, 2.1 m, opacity 0.4) so you never lose yourself in a crowd.
- Backpack `3d3226`.
- Headgear by `look.hat` (goggles use an unlit material).
- `bulk` scales the torso in x and z (Bram 1.12).

**`makeGun(id, fitted)`:**
- `GUN_SCALE` **1.7** (drawn bigger than life so it reads from the camera). `GUN_MOUNT` (−0.3, 1.42, 0.36), the right shoulder.
- Base dimensions:

  | Gun | len | w | h | colour | extra |
  |---|---|---|---|---|---|
  | pistol | 0.40 | 0.15 | 0.16 | `5a5e68` | |
  | smg | 0.50 | 0.15 | 0.16 | | magazine |
  | shotgun | 0.82 | 0.14 | 0.13 | `8a5a36` wood | barrel tube `4e525a` |
  | rifle | 0.80 | 0.14 | 0.15 | `5c6a48` | magazine |
  | minigun | 0.86 | 0.26 | 0.20 | | 6-barrel cluster |
  | launcher | 0.72 | 0.22 | 0.17 | `6a7a40` | drum |

- Materials are Lambert with an emissive glow of 0.22 so guns don't vanish on a dark street. A light top strip `8c929c` is what you see from above.
- **Fitted parts are shown:**
  - Barrel: long × 1.22 length, short × 0.82, heavy adds a thick sleeve.
  - Grip: colour and width by grip type.
  - Muzzle: compensator, brake, choke boxes; suppressor cylinder 0.28 long.
  - Magazine: box under the gun (in the grip for the pistol); extended is longer; drum is a sideways cylinder; launcher drum radius 0.15, or 0.19 when extended; quick mag adds a yellow pull tab.
  - Sight: red dot (with a red lens), holo, scope cylinder.
  - Stock: light (skeleton), heavy (padded), precision, standard.
  - Under the handguard: vertical foregrip, angled foregrip, laser box with a red emitter, light with a white lens.
  - Trigger and ammo are not shown on the model.
- Returns `{ group, len: muzzle distance × 1.7 }`.

**Props** (each returns `{ group, meshes (flashable, original material kept in userData.mat), hw, hd, height, own? }`):

| Model | Pieces | Collider hw × hd, height |
|---|---|---|
| `makeCar` | merged body (7 colours), dark cabin, 4 merged wheels, tail lights, headlights (25% lit), blob shadow 4.4 | 0.95 × 2.1, 1.6 |
| `makeTanker` | cab (3 colours), tank cylinder r 1.15 × 5.2, orange bands, glass, chassis, 8 wheels, shadow 3.2 × 8 | 1.15 × 3.6, 3.1 |
| `makeCondemned(9, 13)` | 1.2 deep slab with condemned texture, merged scaffolding (poles every w/4, planks every 2.5 m), CONDEMNED sign 2.6 × 0.98, yellow tape | 1.5 × 4.5, 13 |
| `makeSlab` | box | w/2 × d/2, h |
| `makeBarrier` (jersey barrier) | 2.4 long block + orange stripe | 1.2 × 0.32, 0.95 |
| `makeLamp` | pole + arm merged, unlit head `ffc979`, glow pool decal 7.5 m (no real light) | 0.13 × 0.13, 5.4 |
| `makeFireBarrel` | drum + 2 crossed additive flame planes + its own glow pool 6 m | 0.36 × 0.36, 0.95 |
| `makeExplosiveBarrel` | red body, unlit yellow band, lid, blob shadow | 0.34 × 0.34, 1.0 |
| `makeWall(w)` | brick-texture box w × 2.4 × 0.45 with its own material (so damage can darken it) + cap | w/2 × 0.225, 2.4 |

**Pickups and decals:**
- `makeHealthPack`: white box with a red cross.
- `makeArmorVest`: blue vest with a light-blue band.
- `makePowerup`: two camera-facing sprites, a coloured additive halo (2.5) and the badge (1.35), at y 1.05.
- `makeRubble(w, d)`: merged random small boxes, count `max(6, w × d × 3)`.
- `scorchDecal`: opacity 0.7.
- `powerIconURL(id)`: a 64 px data URL for the HUD.

**Tunables read:** `POWERUPS` (colours), `WEAPONS` (slots), `fittedPart`.

### preview.js (57): Armory gun pictures

- A single persistent 480 × 200 WebGL renderer (alpha, preserved drawing buffer, ACES exposure 1.25), so the game doesn't run out of WebGL contexts.
- Hemisphere `d8e4ff / 30281e` 1.7, key `fff2e0` 2.4 at (−3, 4, 2), green rim `9be15d` 1.2.
- **Orthographic** camera framed to the gun's bounding box (`half = max(size.z × 0.56, size.y × 0.56 × aspect)`), placed at the box centre + (−6, 1.2, −0.6), so the gun is seen side-on with the muzzle pointing right.
- Renders `makeGun(model, fitted)`, saves a PNG data URL, disposes the geometry.
- **Cached** by `model + JSON(fitted)`. Returns null without WebGL.
- **Unity:** a RenderTexture with a dedicated camera and layer; cache Texture2Ds.

### audio.js (260): synthesised sound with Web Audio

- `unlockAudio()` (first tap or key):
  - `AudioContext`.
  - Master gain **0.55** (0 when muted) into a `DynamicsCompressor` (threshold −16 dB, ratio 6) into the output.
  - A 1 s white-noise buffer.
- `noise({ dur, freq, q, type = bandpass|lowpass, vol, attack, pan, sweep })`: a buffer source with playback rate 0.8–1.2 and a random start offset, then a biquad filter (exponential frequency sweep to `freq × sweep`), then an exponential gain envelope (attack, then decay to 0.0001).
- `tone({ freq, dur, type, vol, to, delay, attack, pan })`: an oscillator with an optional exponential glide and the same envelope.
- Stereo panning: `pan = (x − player.x)/10`, clamped to ±1.
- `throttle(name, gap)` drops a repeat of the same sound within its gap (s).

| Sound | Gap (s) | Recipe |
|---|---|---|
| shot(model) | 0.035 | noise bandpass q 0.7, sweep 0.35, dur and freq per gun + triangle "thump" down to 40 Hz. Per gun (freq, dur, thump Hz, vol): pistol 1700, .13, 140, .55 · smg 2300, .07, 170, .40 · shotgun 900, .32, 75, .80 · rifle 1400, .16, 110, .55 · minigun 2600, .06, 190, .32 · launcher 500, .18, 70, .50 |
| hit(pan) | .04 | lowpass noise 500 Hz, .08 s |
| die(pan) | .06 | sawtooth 160→55 + lowpass noise 300 |
| groan(pan, big) | .7 | sawtooth 55 (big) or 85 Hz + rand 30, glides × 0.7 over 0.9 s, LFO vibrato 7–11 Hz (depth 6%), bandpass 380 or 650 Hz q 3, 1 s |
| coin | .05 | square 1320, then 1760 |
| pickup | .1 | triangle arpeggio 523 / 659 / 784 / 1047 |
| powerup | .15 | saw sweep 392→784 + square arpeggio 784 / 988 / 1175 / 1568 |
| shock | .2 | lowpass noise 320 for .7 s + sine 180→40 |
| toss | .05 | bandpass noise 900, sweep ×2.5 |
| hurt | .12 | lowpass noise 220 + square 120→60 |
| reload | .2 | two clicks (3000 Hz, then 2200 Hz 160 ms later) |
| swap | .1 | bandpass click 2600 |
| click (UI) | .05 | triangle 880 |
| buy | .1 | square 660, then 990 |
| denied | .15 | square 180 |
| win | 1 | triangle arpeggio. **Unused** |
| lose | 1 | descending sawtooth 330 / 262 / 196 / 131 |
| crumble | .15 | lowpass 400 + bandpass 1800 |
| boom | .06 | lowpass 700, .9 s, sweep .15 + sine 90→28 |
| bigBoom | .1 | lowpass 500, 1.8 s + bandpass 2000 + sine 70→20 |
| thunk | .08 | triangle 220→90 + noise |
| ignite | .3 | bandpass 900, .6 s, slow attack |
| clang | .08 | square 420–540 → 300 + noise 2400 |
| ricochet(pan) | .05 | triangle 2200–3600 → 900 |
| chip(pan) | .05 | bandpass noise 1800 |
| rumble | 1 | lowpass 120, 1.6 s + sine 45→32 |
| collapse | 1 | lowpass 260, 2.8 s + bandpass 1400 + sine 60→22 |
| milestone | .5 | triangle 523, then 784 |
| slam | .3 | lowpass 160 + sine 70→30 |

- `setSoundEnabled(on)`.
- `vibrate(ms or pattern, on)` calls `navigator.vibrate`.

**Unity:** generate `AudioClip`s from PCM at start-up (`AudioClip.Create`) with the same oscillator and noise maths, or use recorded sounds. Use an AudioMixer with a compressor, `AudioSource.panStereo`, and per-sound cooldowns.

### input.js (120): `Input`

Detailed in section 6.

### save.js (138): progress and notes

Schema in section 4.
- Storage: `localStorage` with try/catch everywhere; the game still runs (and forgets) if storage is blocked.
- `merge` is a deep, typed merge of the stored save over the defaults.
- `refundOldUpgrades`: old per-stat gun upgrades are refunded at `round(OLD[stat] × (1 + tier × 0.45) × 1.65^lv / 5) × 5`, with OLD = damage 80, fireRate 90, ammoCapacity 60. Saved at once so it isn't paid twice.
- `resetSave()` keeps settings (and notes, which are stored separately).
- `gunParts(id)`, `fittedParts(id)` (keeps only parts that are owned and still fit).
- `loadout()`: until first chosen, defaults to your priciest owned guns, up to 3.
- `runWeapons(character)`: the survivor's own gun first, then the equipped ones in `WEAPONS` order (only if owned and not the survivor's own).
- `notes` and `persistNotes()`.

### ui.js (1008): `UI`

Covers all DOM: the menu, select, Armory, HUD, results, notes, settings, updater and back-button handling. Detailed in section 2.

### updater.js (219): live self-update (Android only)

**State** in localStorage `graveway.update.v1`: `{ trying, lastTried, tries, bad: [builds] }`.

**`init()`:**
- Fetches `build.json` (`{ app, version, build, native, sha }`).
- Enabled only if the Filesystem, WebView and CapacitorHttp plugins exist.
- `live` = the WebView's server base path contains `/live/`.
- If the build it was trying isn't the one running, that attempt failed; after 2 tries it's added to `bad`.

**`confirm()`** (once 10 frames have been drawn):
- Clears `trying`.
- `WebView.persistServerBasePath`.
- Deletes every other downloaded build under `DATA/live/`.

**`check({ manual })`:**
- GitHub API `repos/{UPDATE_REPO}/releases?per_page=30`.
- Tags must match `graveway-v{version}-{build}`.
- Wants the asset `graveway-web-{native}.json.gz` with build > current and not in `bad`.
- A newer release without a matching package (a different native side, or an APK) is reported as `newApp`.
- Local builds (build 0) only check when asked by hand.

**`download()`:**
- `CapacitorHttp` GET as a blob (base64).
- Checks GitHub's SHA-256 digest when present.
- Gunzips (`DecompressionStream`) and parses JSON `{ build, native, version, files: [[path, kind 't'|binary, data]] }`.
- Validates it, rejects `..` paths, writes the files with `Filesystem.writeFile` to `DATA/live/{build}/`, returns its path.

**`apply()`:** counts tries, sets `trying`, then `WebView.setServerBasePath` (the app restarts into the new files).

**`revert()`:** marks the current build bad, `WebView.setServerAssetPath('public')`.

**`label`:** e.g. `0.7 (build 9) · updated`.

**Tunables read:** `UPDATE_REPO` (`enachealex/My-Games`).

### index.html (614): DOM, CSS and the update watchdog

**Viewport:** no zoom, `viewport-fit=cover`, theme `#0d1418`, dark colour scheme.

**CSS:**
- Variables: bg `#0d1418`, accent green `#9be15d`, blood `#ff4d4d`, armor `#5db4ff`, gold `#ffc928`, display font Bebas Neue.
- Safe-area insets (`env(safe-area-inset-*)`) are used in all padding.
- `touch-action: none` on the game surfaces; text selection and the long-press callout are disabled.
- Animations:
  - nudge: a denied-purchase shake
  - banner pop-in: scale 1.4 to 1, 0.35 s, overshoot curve
  - toast: rise, fade in and out over 1.3 s
  - vignette `hit` (0.45 s red flash) and `pulse` (1.1 s loop when HP is low)
- Respects `prefers-reduced-motion`.

**Elements:**
- `#scene` canvas, `#touch` steering surface, `#joy` / `#joy-knob`, `#vignette`.
- Screens and modals: `#menu`, `#select`, `#armory`, `#hud`, `#banner`, `#toast`, `#pause`, `#result`, `#settings`, `#note`, `#notes`, `#nogl`.

**Watchdog script:** if an update was being tried (`graveway.update.v1.trying`) and the game hasn't set `dataset.ready` within **12 s**, it calls `WebView.setServerAssetPath('public')` to go back to the APK's own files.

### sw.js (65): service worker (web only)

- Cache `graveway-v6`. Precaches `./`, the manifest, icons, font, three.js and all 17 `src` files.
- `skipWaiting` and `clients.claim`; deletes old `graveway-*` caches.
- **Network-first:** every same-origin GET is fetched, then cached; the cache is only used when offline. Navigations map to `./`.
- Not used in the Android app. No Unity equivalent is needed.

---

## 2. UI screens and flows (ui.js + index.html)

**Common:**
- Every button click plays `sfx.click`.
- Banner (`hooks.banner(title, sub, ms = 2200)`) appears 30% from the top.
- Toast (`hooks.toast(text, kind, color)`) appears at the top for 1.3 s. Kinds: heal (green), armor (blue), power (display font, the power-up's colour).
- Purchases that can't be afforded nudge the button and play `sfx.denied`.
- Visible screen switching is in `show(name)`.

### Main menu (`#menu`)
- The 3D street with wandering walkers shows behind a top and bottom gradient.
- Top bar:
  - Wallet pill (coins).
  - Settings icon with a gold **badge counting unsent playtest notes**.
- Title "GRAVE**WAY**" (WAY in red) with the tagline "Walk the dead road. Get as far as you can."
- Bottom:
  - Current survivor card (small portrait and name; display only).
  - "Best run" (metres, or —) with "N runs · N kills" or "No runs yet".
  - **Update button** (Android only): "Update ready: X · tap to restart" or "New app version X · tap to get it" (opens the release page).
  - **Play** goes to survivor select.
  - **Armory**.
  - Hint: "Drag anywhere to move. Aiming and shooting are automatic."
- One-off banner after a refund: "Guns take parts now" / "Your old upgrades were refunded: +N coins" (4.5 s).
- Keyboard: Enter goes to survivor select.

### Survivor select (`#select`)
- Back button, title "Survivor", ‹ › arrows, **swipe the card** (> 40 px horizontally and 1.5× more horizontal than vertical), dot buttons, keyboard ←/→ or A/D, Enter to play.
- The card shows:
  - Large portrait (rendered from the 3D model), name, role pill, blurb.
  - **Health** bar (relative to the highest base health; the text includes upgrades).
  - **Speed** bar (m/s, with upgrades).
  - **Perk** name and text.
  - **"Brings"**: gun icon, gun name, and "Their own gun" or "Theirs for the run, even before you buy it".
  - **"Also carrying"**: the equipped extra guns, or "Nothing else", with a **Loadout** button that opens the Armory on Weapons and returns here.
- Changing survivor saves `save.character` at once and rebuilds the menu scene with the new survivor.
- Footer button "Run as {Name}" starts the run.

### Armory (`#armory`)
- Back, title, wallet; tabs **Weapons** and **Survivor**. The scroll position is kept across re-renders.

**Weapons tab:**
- **Loadout strip**, captioned "Loadout · n/3 slots + {Name}'s own gun".
  - First slot: the survivor's own gun.
  - Then the equipped extras; if one is the survivor's own gun it is labelled "Theirs anyway".
  - Then dashed "Empty" slots. Tapping a slot selects that gun.
- **Weapon grid**: 6 cards, each with an SVG icon, name, and a tag (price / "Equipped" / "Owned"), plus "{Name}'s gun" for signature guns.
- **Detail panel**:
  - Name, plus an "Equipped" or "{Name}'s own" pill.
  - **3D gun picture** with the fitted parts (preview.js).
  - Blurb.
  - **8 stat bars**, each relative to the best gun with its parts, longer is better:
    - Damage (or Blast), shown as "16×7" for pellets
    - Fire rate /s
    - Accuracy ±°
    - Recoil °/shot
    - Stability "settles Xs" (max / recover)
    - Range m
    - Magazine
    - Reload s
  - Fine print: blast radius, "goes through N", and the special effects of fitted parts.
  - Not owned: "**Unlock for {price}**", plus "N more coins needed".
    - Buying puts the gun straight into the loadout. If the loadout is full, it replaces the cheapest equipped gun, with a toast.
  - Owned: **Equip / Unequip**. Disabled when the loadout is full ("Loadout full. Unequip a gun to make room."). The survivor's own gun shows a note.
- **Parts** section:
  - One row per slot the gun takes, showing the slot name and the fitted part. Tapping opens a list.
  - Each part shows its name, blurb (or "What it comes with."), and **effect chips** (green good, red bad), e.g. "Damage +20%", "Accuracy −15%", "Reload 10% faster", "Through 1 more zombie", "Sets zombies alight", "+30% vs walkers & runners", "2× damage to cover", "Quiet: zombies don't hear it", "3 bomblets", "Laser line", "Bigger flashlight", "Throws 2× harder", "One heavy slug", "Never goes through".
  - Action: a "Fitted" pill, a **Fit** button (owned), or a **buy** button with the tier-scaled cost. **Buying also fits it.**
- Closing the Armory after part changes re-renders the portraits and rebuilds the menu survivor.

**Survivor tab:**
- "These stay with you, whichever survivor you pick. Shown for {Name}."
- Three upgrade rows: Max health, Move speed, Coin magnet. Each shows the current value, the step per level, 5 pips, and a buy button with the cost, or a "Max" pill.

### HUD (`#hud`)
Updated through a set-only-if-changed cache.

**Top:**
- **Distance** "N m" (furthest point reached).
- "Stage N" with a **progress bar** to the next stage.
- **Coins**: wallet + this run's coins + bonuses.
- **Pause** button.

**Below the top bar:**
- Kills "N kills".
- Debug line: "INVINCIBLE · N fps · M up", shown only when either tool is on.
- **Boss bar**: "BOSS" with a red bar, while a boss is alive.

**Bottom-left:**
- **Timed power-up chips**: badge icon, seconds left, a bar draining in the power-up's colour.
- Survivor portrait (52 px).
- **Armor bar** (shield icon and number; hidden at 0).
- **Health bar** (heart icon and number).

**Bottom-right:**
- **Weapon bar** (only with 2 or more guns): one 64 × 40 button per gun with its SVG icon, an ammo strip, and its number (1–4). The selected one is highlighted green. Tap to switch.
- **Weapon card**: gun name; ammo count, or "∞" under Rapid Fire, or "···" while reloading; "/ capacity"; "TAP TO RELOAD"; a green reload progress strip; ammo turns red when empty and green while reloading. Tap to reload.

**Screen effects:**
- Red edge **vignette** pulses when HP is under 30%, and flashes on every hit.
- HUD buttons act on `pointerdown` and stop the event, so a second finger works while the first is steering (a phone sends no click for a second finger) and doesn't start a joystick.

### Pause (`#pause` modal)
- "Paused" with "N m · stage N".
- **Resume**, **Restart**, **Quit** (to the menu), **Leave a note**.
- Opened by the HUD pause button, P, Escape or Android back, leaving the app (visibilitychange), or the App `pause` event.
- N pauses and opens a note.
- **Restart and Quit don't save the run's coins, kills or run count** (see section 10).

### Results (`#result` modal), when you die
- Title "**Overrun**"; the big distance.
- Subtitle: "New best!" in gold, or "Stage N · Ts · Survivor · started at X m".
- Rows: Zombies down, Coins picked up, Stage bonuses (N), **Total**.
- "Wallet: … · Best: … m".
- **Tip**: "You can afford {cheapest upgrade} in the Armory." if one is affordable (it looks at guns, parts and survivor upgrades), otherwise one of 13 random tips (keep moving, barrels, armor, runners' red eyes, tanks smash barricades, rubble slows, coins kept, Juno's launcher, power-ups, hiding behind walls, suppressor, parts trade-offs, loadout).
- Buttons: **Run again**, **Armory**, **Menu**, **Leave a note about this run**.

### Settings (`#settings` modal)
- **Sound** On/Off.
- **Vibration** On/Off.
- **Graphics**: High or "Battery saver".
- **Playtest notes** (count) opens the notes list.
- **Updates** (Android, when the updater is enabled): "Check now" / status / "Restart to update", with a note "Game files: {label}. …".
- "**Go back to the version the app came with**" (only when running downloaded files).
- **Playtest tools:**
  - **+1,000 coins** (instant, with a toast).
  - **Start runs at** 0 / 200 / 500 / 1000 m (cycles). Runs started further up don't set records, and the director starts at that stage (500 and 1000 bring a boss at once).
  - **Invincible**: damage becomes 0, the hit still flinches; the HUD shows "INVINCIBLE".
  - **Show frame rate**: HUD shows "N fps · M up" (zombies alive).
  - **Lots of power-ups**: two per chunk from 10 m.
- **Reset progress**: tap twice to confirm. Keeps settings and notes.
- **Done**.
- Credits line with the version label ("Graveway · v… (build …) · Android", or "Web").

### Leave a note (`#note` modal)
- Context line, auto-filled. Depending on where you are:
  - During a run: survivor, metres, stage, gun, seconds in, HP and armor, kills, zombies up, fps.
  - After a run: the run summary plus the loadout text.
  - In a menu: where you are plus the loadout text ("coins · best · guns[parts]*equipped").
- 9 toggle tag chips: Bug, Too hard, Too easy, Controls, Camera, Looks, Sound, Slow / laggy, Idea.
- Text box "What should change?".
- Cancel, **Save note** (needs text or at least one tag). Toast: "Note saved · N to send (Settings → Playtest notes)".

### Playtest notes (`#notes` modal)
- Count ("N notes · M not sent yet" or "· all sent").
- Read-only monospace text box with every note: a header with build, screen size and count, then numbered notes with tags, context, time, and whether sent.
- **Send to Claude** ("Send all again" once all are sent):
  - Opens `https://github.com/enachealex/My-Games/issues/new?title=…&body=…`.
  - Title: "Playtest notes: N from {build}".
  - Body: a `<!-- graveway-playtest -->` marker, build and screen, sent time, then numbered notes.
  - The URL is kept to 7,000 characters by sending fewer notes per go (they come next time). A single huge note is cut to 2,500 characters.
  - Marks the sent notes with a timestamp and copies the body to the clipboard.
  - Opens externally: in the Android app via `location.href` (handed to the phone's browser), on the web via `window.open`.
- **Copy all**: clipboard; falls back to selecting the text plus `execCommand('copy')`.
- **Delete all**: tap twice.
- **Done**.

### No-WebGL screen (`#nogl`)
"3D graphics aren't available…"

### Tutorials and hints
There is no tutorial. The only guidance is:
- the menu hint
- the "TAP TO RELOAD" label
- the run-start banner
- stage banners, plus "Ambush!", the boss banners, "It's coming down!" and "Tanker down!"
- results tips

### Back button and Escape (`ui.back()`)
Closes, in order, whichever is open first:
1. note
2. notes
3. settings
4. results (goes to the menu)
5. Armory (back to the menu, or to select if opened from there)
6. survivor select (goes to the menu)

If none is open: playing pauses, paused resumes, and on the main menu the Android app **exits**. On the web, Escape on the bare menu does nothing.

---

## 3. config.js by section

### Weapons

`LOADOUT_SLOTS = 4`: the survivor's own gun plus 3 equipped. Reserve ammo is unlimited.

| id | Price | Dmg | Rate/s | Spread (rad) | Mag | Reload (s) | Range (m) | Pellets | Pierce | Knock | Shake | Recoil per / max / recover | Slots |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| pistol | 0 | 22 | 3.2 | .03 | 12 | 1.0 | 17 | 1 | 0 | 1.2 | .04 | .008 / .03 / .4 | all but stock |
| smg | 350 | 13 | 11 | .08 | 32 | 1.5 | 15 | 1 | 0 | .6 | .03 | .006 / .06 / .3 | all 9 |
| shotgun | 750 | 16 | 1.3 | .30 | 6 | 2.0 | 11 | 7 | 0 | 3.0 | .16 | .03 / .05 / .5 | all 9 |
| rifle | 1500 | 24 | 9 | .02 | 30 | 1.7 | 21 | 1 | 1 | 1.2 | .06 | .014 / .16 / .35 | all 9 |
| launcher (grenade) | 2200 | 120 | 1.0 | .02 | 4 | 2.4 | 19 (min 4) | 1 | 0 | 0 | .14 | .01 / .02 / .3 | all but muzzle |
| minigun | 3200 | 17 | 20 | .10 | 160 | 3.2 | 18 | 1 | 0 | .8 | .03 | .002 / .05 / .2 | all but muzzle and stock |

Launcher blast: radius 3.4, impulse 10. Each gun also has a blurb.

### Gun parts

Slots and their labels: barrel (Barrel), muzzle (Muzzle), mag (Magazine), sight (Sight), stock (Stock), trigger (Trigger), grip (Grip), rail (Handguard), ammo (Ammo).

**45 part entries.** The first part that fits a gun in each slot is what it comes with (free).

**Part cost** = base × (1 + 0.35 × gun index), rounded to 5. The multiplier per gun is pistol ×1, smg ×1.35, shotgun ×1.7, rifle ×2.05, launcher ×2.4, minigun ×2.75.

Mods are multipliers unless marked; "+pierce" is added, "pellets" replaces the value.

**Barrel**
| Part | Base price | Mods |
|---|---|---|
| factory | free | |
| long | 250 | range 1.2, spread .85, rate .95 |
| short | 200 | rate 1.1, reload .9, range .85, spread 1.15 |
| heavy | 400 | dmg 1.2, stability 1.3, rate .9, reload 1.1 |

**Muzzle**
| Part | Base price | Mods | Fits |
|---|---|---|---|
| none | free | | |
| comp | 200 | recoil .7 | not shotgun |
| brake | 300 | recoil .55, spread 1.1 | |
| suppressor | 350 | recoil .85, dmg .92, fx quiet | |
| choke | 250 | spread .65, range 1.15 | shotgun only |

**Magazine**
| Part | Base price | Mods | Fits |
|---|---|---|---|
| factory | free | | |
| extended | 250 | mag 1.5, reload 1.15 | |
| drum | 450 | mag 2.2, reload 1.4, stability .9 | not launcher |
| quick | 300 | reload .6, mag .85 | |

**Sight**
| Part | Base price | Mods |
|---|---|---|
| iron | free | |
| reddot | 200 | spread .85 |
| holo | 300 | spread .8, stability 1.1 |
| scope | 450 | range 1.35, spread .7, rate .9 |

**Stock**
| Part | Base price | Mods |
|---|---|---|
| factory | free | |
| light | 200 | reload .85, stability .9 |
| heavy | 300 | recoil .75, stability 1.25, reload 1.1 |
| precision | 350 | spread .85, stability 1.15, rate .95 |

**Trigger**
| Part | Base price | Mods |
|---|---|---|
| factory | free | |
| hair | 300 | rate 1.15, spread 1.08 |
| match | 300 | spread .85, dmg 1.05 |

**Grip**
| Part | Base price | Mods |
|---|---|---|
| factory | free | |
| rubber | 150 | recoil .85 |
| stippled | 250 | stability 1.2, reload .95 |
| ergo | 250 | rate 1.05, recoil .92 |

**Handguard (rail)**
| Part | Base price | Mods | Fits |
|---|---|---|---|
| none | free | | |
| foregrip | 250 | recoil .75, stability 1.15 | not pistol |
| angled | 300 | stability 1.35, reload .95 | not pistol |
| laser | 300 | spread .8, fx laser | |
| light | 200 | range 1.1, fx light | |

**Ammo**
| Part | Base price | Mods and effects | Fits |
|---|---|---|---|
| fmj | free | | pistol, smg, rifle, minigun |
| buck | free | | shotgun |
| he | free | | launcher |
| hollow | 300 | knock 1.2; ×1.3 vs walkers/runners, ×0.8 vs tanks/bosses, never pierces | bullet guns |
| ap | 400 | +1 pierce; ×1.5 vs tanks/bosses, ×2 damage to props | bullet guns |
| incendiary | 450 | dmg .9; fire = 0.35 × hit damage per second for 3 s | bullet guns |
| slug | 300 | pellets 1, dmg 5.5, spread .25, +1 pierce, range 1.3, knock 1.3; ×2 to props | shotgun |
| dragon | 450 | dmg .85, range .85; fire 0.5 × damage per second for 3 s | shotgun |
| flechette | 350 | dmg .85, spread .9, +1 pierce | shotgun |
| incendiary-g | 450 | dmg .8; fire 0.12 × damage per second for 4 s | launcher |
| cluster | 550 | dmg .75; 3 bomblets | launcher |
| concussion | 300 | dmg .6, blast radius 1.35, impulse ×2 | launcher |

`UPGRADE_COST_GROWTH` = 1.65.

### Survivors

| id | Name / role | HP | Speed (m/s) | Gun | Perk | Look |
|---|---|---|---|---|---|---|
| rook | Rook, Ex-cop | 100 | 6.2 | pistol | Quick Hands: reloadMult 0.6 | cap |
| bram | Bram, Riot guard | 140 | 5.4 | shotgun | Riot Gear: startArmor 75 | helmet, bulk 1.12 |
| juno | Juno, Demolitions | 90 | 6.0 | launcher | Blast Proof: explosionResist 0.8 | goggles |
| wren | Wren, Courier | 80 | 7.2 | smg | Scavenger: magnetBonus +2 m, coinMult 1.25 | hood |

Each also has a blurb and colours for skin, shirt, pants and hat.

The survivor's own gun is always carried, even if not bought.

### Player base and upgrades

- `PLAYER`: magnet 3 m, radius 0.45.
- Upgrades (5 levels each; cost = `round(base × 1.65^lv / 5) × 5`):

| Upgrade | Per level | Costs by level | Total |
|---|---|---|---|
| maxHp | +20 HP | 100, 165, 270, 450, 740 | 1,725 |
| speed | +6% | 120, 200, 325, 540, 890 | 2,075 |
| magnet | +1 m | 70, 115, 190, 315, 520 | 1,210 |

- `ARMOR_ABSORB` = 1.
- `PICKUPS`: health +35, armor +50, max armor 100.

### Power-ups

| id | Name | Tag | Colour | Weight | Effect |
|---|---|---|---|---|---|
| frag | Frag Barrage | Grenades out | `9be15d` | 3 | 4 grenades within 18 m, damage 150, blast 3.6 / 12 |
| rapid | Rapid Fire | No reloads | `ffb02e` | 2 | 10 s, fire rate ×2, no ammo used |
| double | Double Damage | 2× damage | `ff5a4a` | 2 | 10 s, damage ×2 |
| shock | Shockwave | Get back! | `7ac8ff` | 2 | radius 7, damage 180, impulse 24 |
| vacuum | Coin Vacuum | Every coin, to you | `ffc928` | 1.5 | 8 s, radius 32 |

`POWERUP_DROPS`: perChunk 0.5, from 60 m, tank 0.25. A boss always drops one.

### Zombies

| Type | HP | Speed (m/s) | Dmg | Attacks/s | Reach | Radius | Scale | Coins | Aggro (m) | Eyes | Smashes |
|---|---|---|---|---|---|---|---|---|---|---|---|
| walker | 45 | 1.4–2.2 | 10 | 1.0 | 1.1 | .42 | 1.0 | 3 | 15 | `d8ff6a` | |
| runner | 28 | 4.0–4.8 | 7 | 1.4 | 1.0 | .38 | .92 | 3 | 24 | `ff3a2a` | |
| tank | 260 | 1.1–1.4 | 24 | 0.7 | 1.5 | .70 | 1.5 | 12 | 16 | `ffa020` | yes |
| boss | 1400 | 1.6 | 35 | 0.6 | 2.2 | 1.15 | 2.4 | 100 | 30 | `ff2020` | yes |

`GUNFIRE_NOISE` = 22 m.

### Director (stage = metres / 100)

Formulas:
- hp × (1 + 0.15s)
- damage × (1 + 0.05s)
- speed × min(1 + 0.025s, 1.45)

| Stage | Max zombies up | Horde every (m) | Horde size | Straggler every (s) | Runner share | Tank share | Stage bonus |
|---|---|---|---|---|---|---|---|
| 0 | 20 | 24 | 2–4 | 3.5 | – | – | – |
| 1 | 25 | 22.8 | 2–4 | 3.25 | .12 | – | 15 |
| 3 | 35 | 20.4 | 3–6 | 2.75 | .20 | .075 | 25 |
| 5 (boss) | 45 | 18 | 5–8 | 2.25 | .28 | .105 | 35 |
| 7 | 55 | 15.6 | 6–10 | 1.75 | .35 (cap) | .135 | 45 |
| 9 | **64** (cap) | 13.2 | 7–12 | 1.25 | .35 | .16 (cap) | 55 |
| 10 (boss) | 64 | 12 (floor) | 8–13 | 1.0 | .35 | .16 | 60 |

The straggler interval floors at 0.8 s (stage 11). Alley ambush chance is 0 at stage 0 and 0.25 after. Unlocks: runner at stage 1, tank at 3, boss at 5, then a boss every 5 stages. Stage bonus = 10 + 5 × stage.

### Props

| Kind | HP | Material | Mass | Explodes (radius / dmg / impulse) | Other |
|---|---|---|---|---|---|
| car | 420 | metal | 8 | 5 / 170 / 18 | burns: smoke < .6, fire < .3 |
| tanker | 520 | metal | 24 | 9 / 320 / 30 | burns: smoke < .6, fire < .35 |
| barricade | 260 | concrete | 3 | | rubble |
| barrel | 30 | metal | 1 | 3.8 / 150 / 12 | |
| wall | 480 | brick | fixed | | rubble, topples, crush 220, blastMult 1.5 |
| lamp | 70 | metal | fixed | | topples, crush 90 |
| fire (barrel) | 60 | metal | 1 | | |
| slab | 320 | concrete | fixed | | rubble |
| condemned | 900 | concrete | fixed | | topples, collapse, crush 2000, bulletMult .15, blastMult 1.6 |

All props have `blocksBullets: true`.

`SET_PIECES`: a condemned building every 7 chunks (about 224 m); tanker chance 0.06 per row, from 120 m.

`RUBBLE_SLOW` 0.55 for zombies, `RUBBLE_SLOW_PLAYER` 0.75.

### Economy (coin sources)

- **Road coins:** lines of 4–7 every 14–22 m, 1 coin each.
- **Kills:** walker and runner 3, tank 12, boss 100, plus 1 with 30% chance. Dropped coins must be picked up; any left more than 45 m behind vanish.
- **Stage bonus:** 10 + 5 × stage, for each stage reached during the run (not stages skipped by starting further up). Paid at the end of the run.
- Wren's × 1.25 applies to coins picked up, not to stage bonuses.
- Banked only on death (`finish`).
- Other sources: the +1,000 playtest button, and the refund of old upgrades.
- **Spending:** guns (350–3200), parts (about 150–1,515 after the tier multiplier), survivor upgrades.

### Other config

`FEEDBACK_REPO` and `UPDATE_REPO` are both `enachealex/My-Games`. `GAME_NAME` is 'Graveway'.

---

## 4. Save data schema (save.js)

**`localStorage['graveway.save.v2']`** (a v1 save is ignored):

```
{
  coins: number,
  best: number,          // metres
  runs: number,
  kills: number,
  character: 'rook'|'bram'|'juno'|'wren',
  owned: string[],       // gun ids, default ['pistol']
  loadout: string[]|null,// equipped extra guns (max 3); null until first chosen
  parts: { [gunId]: { owned: ['slot:partId', ...], fitted: { [slot]: partId } } },
  wup: {},               // legacy per-stat upgrades, refunded and cleared on load
  refunded: number,      // coins refunded, shown once in a banner then reset to 0
  pup: { maxHp?: n, speed?: n, magnet?: n },   // survivor upgrade levels 0-5
  settings: { sound: true, haptics: true, quality: 'high'|'low',
              god: false, fps: false, startAt: 0|200|500|1000, powers: false }
}
```

**`localStorage['graveway.notes.v1']`:** an array of `{ at: ISO, tags: string[], text: string, ctx: string, sent?: ISO }`. Kept apart from progress so Reset doesn't delete feedback.

**`localStorage['graveway.update.v1']`:** `{ trying, lastTried, tries, bad[] }` (updater).

**When it's saved:**
- each purchase, fit, equip or upgrade
- settings changes
- character changes
- the run result
- the refund

**Unity:** `PlayerPrefs` or a JSON file in `persistentDataPath`. Web saves can't be carried over: they live in the WebView's localStorage, under a different app ID.

---

## 5. Platform and native integration, with Unity equivalents

| Web / Capacitor feature | Where | Unity 6 equivalent |
|---|---|---|
| Android back button: steps back through the game, exits from the menu | main.js, `ui.back()` | `Input.GetKeyDown(KeyCode.Escape)` or an Input System back action; `Application.Quit()` |
| App `pause` event and `visibilitychange` pause the game | main.js | `OnApplicationPause(true)`, `OnApplicationFocus(false)` |
| App `resume`: update check every 15 min | main.js | `OnApplicationPause(false)` |
| App `getInfo`: version label | main.js | `Application.version`; the version code must be baked in at build time |
| Vibration (`navigator.vibrate`): hurt 45, explosion 60/25, power-up 30, shockwave 60, topple [40, 60, 40], collapse [120, 40, 200]; `VIBRATE` permission | audio.js, game.js, props.js | `Handheld.Vibrate()` has a fixed duration. Durations and patterns need an `AndroidJavaObject` Vibrator with `VibrationEffect.createOneShot` / `createWaveform` |
| Portrait only | android.json, manifest | Player Settings: default orientation Portrait |
| Fullscreen immersive: system bars hidden, shown briefly by swipe | build-apk.mjs `MainActivity` | Unity Android is fullscreen by default; hide the navigation bar; render outside the safe area |
| Safe-area insets in CSS | index.html | `Screen.safeArea` applied to the UI root |
| Splash and icon from `assets/icon.png`, `splash.png`; background `#0d1418` | build-apk.mjs | Player Settings icons and splash |
| Service worker (offline web play) | sw.js | Not needed |
| Live self-update (CapacitorHttp, Filesystem, WebView base path, 12 s watchdog, revert, bad-build list) | updater.js, index.html | **No direct equivalent.** Compiled code can't be hot-swapped. Options: Play Console internal testing; an in-app "new APK" prompt that downloads it and launches the installer (needs `REQUEST_INSTALL_PACKAGES`); Addressables or remote config for data only |
| "New app version" button that opens the release page | ui.js | `Application.OpenURL` |
| Opening external URLs (GitHub issue) | ui.js `openExternal` | `Application.OpenURL` (URL length limits still apply) |
| Clipboard | ui.js | `GUIUtility.systemCopyBuffer` |
| localStorage | save.js | PlayerPrefs or a JSON file |
| Web Audio synthesis | audio.js | Procedural `AudioClip.Create` or real clips; AudioMixer compressor |
| Audio unlock on first tap | main.js | Not needed |
| Long-press menu and text selection blocked | main.js, CSS | Not needed |
| The game-font wait | main.js | TextMeshPro or UI Toolkit font asset (Bebas Neue, OFL) |
| `window.graveway` console handle | main.js | Debug console or dev menu |
| CI: GitHub Actions, debug keystore cached for a week; tags `graveway-v{ver}-{run}` | android.yml | Needs a Unity licence in repository secrets (GameCI); keep signing consistent |

---

## 6. Input (input.js + ui.js)

**Floating joystick:**
- Press anywhere on the `#touch` surface (full screen, beneath the HUD). The base (120 px, centred on the thumb) and knob (54 px) appear there.
- `RADIUS` = **56 px** to full speed.
- `DEADZONE` 0.12, rescaled: `m = (mag − 0.12)/(1 − 0.12)`.
- Past **1.35 × radius** (75.6 px) the base follows the thumb, so changing direction never means dragging back across the circle.
- Output: stick x is across the road; stick y (screen down) is toward the camera (+z). Pushing up walks up the road.

**Multiple fingers:**
- Every finger down is tracked.
- The first finger steers.
- When the steering finger lifts and others are still down, the **most recently pressed** one takes over the stick at its current position.
- `pointercancel` and window `blur` release everything.
- Input is disabled (`setEnabled(false)`) in menus, pause and results.

**HUD buttons** (pause, weapon card for reload, weapon bar for switching) fire on `pointerdown` and stop propagation, so they work with a second finger and never start the joystick.

**Keyboard:**

| Key | Action |
|---|---|
| WASD / arrows | Move (normalised; overrides the stick) |
| R or Space | Reload |
| Q / E (also Tab) | Previous / next gun |
| 1–9 | Select gun |
| P | Pause / resume |
| Escape | Back |
| N | Pause and leave a note |
| Enter | Menu: survivor select; select: play |
| ←/→ or A/D | On select: cycle survivors |

Tab and Space have their browser defaults blocked during play. Keys are ignored while typing in a text field, and repeats are ignored.

---

## 7. Phone performance measures

- **Zombie cap** `maxActive = min(20 + 5 × stage, 64)`. "64: what a mid-range phone draws smoothly."
- **Hard caps and pools:**

  | What | Cap |
  |---|---|
  | Coins | 500 |
  | Bullets | 260 |
  | Particles | 700 |
  | Grenades | 24 |
  | Debris | 420 (FIFO) |
  | Glow sprites | 420 (oldest recycled) |
  | Smoke sprites | 300 (oldest recycled) |
  | Ground rings | 6 |
  | Fog shadow quads | 48 |

- **One draw call each:** coins, tracers, particles, grenades and debris (InstancedMesh); glow and smoke sprites (custom instanced shader); fog shadows.
- **Merged geometry:** litter per chunk, sidewalks, kerbs, rubble, car, tanker and barrier parts, scaffolding, gun parts, humanoid arm-plus-hand, eyes.
- **Shared geometry and cached materials**: by colour and options, so identical props share them.
- **Chunks** dispose their own geometry and materials when dropped. Scenery is only built 78 m ahead and kept 40 m behind; fog hides the edges and the camera far plane is 90 m.
- **No shadow maps:** blob decals instead. Mostly Lambert (per-vertex) and unlit Basic materials. Light pools are decals, not lights. Only 4 real lights plus the flashlight spot (hemisphere, moon, spot, muzzle point, blast point).
- **Pixel ratio:** at most 2 on High, 1 on Battery saver.
- **Battery saver** also halves effect sprite counts and emitter rates (vfx scale 0.5) and debris (× 0.6).
- **Throttles:**
  - frame dt clamped to 50 ms
  - zombie line of sight every 0.25 s
  - zombie visibility every 0.1 s
  - nav rebuild at most every 0.15 s, and only when dirty or the player's cell changes
  - gunfire noise every 0.25 s
  - per-sound cooldowns
- **Cheap tests:** an AABB early-out before zombie separation, collision and ray tests. Typed arrays and a binary heap for the flow field.
- **Despawning:** zombies 45 m behind (boss 60), coins 45 m behind, chunks 40 m behind.
- **HUD:** DOM updates only when a value changes.
- **Hit flash** swaps to a shared material instead of making a new one.
- Portraits are rendered once; gun pictures are cached on one small persistent renderer.
- **Not optimised:** each zombie is about 7–10 separate meshes (torso, legs, head, arms, hands, eyes, shadow), so 64 zombies is roughly 500–650 draw calls. In Unity, consider SRP batching or GPU instancing, or skinned characters.

---

## 8. Camera (game.js `updateCamera`)

**Projection:** perspective; FOV **68° in portrait, 52° in landscape**; near 0.1, far 90.

**Distance:** `dist = clamp(5.8 / tanH, 13, 20)`, where `tanH = tan(fov/2) × aspect`. This fits about ±5.8 m of road across the screen.

**Angle:** elevation 0.92 rad (about 52.7°) above the road, looking up the road from behind and above the survivor.

**Follow:**
- The look target is (`player.x × 0.55`, 0.8, `player.z − 4`): 4 m ahead, and only 55% of the sideways movement, so the road stays centred.
- The look point is smoothed with `1 − e^(−5·dt)`.
- The camera sits at (`tx`, `sin(el) × dist`, `tz + cos(el) × dist + 1.6`), smoothed with `1 − e^(−4.5·dt)` while playing and `1 − e^(−2·dt)` otherwise.
- The run starts from (0, 30, startZ + 30) and swoops in.

**Fog** is refitted every frame: near = dist × 0.85, far = dist + 36.

**Shake:**
- Random x and y offset of ±shake × 0.175. Shake decays at 2.2 per second.
- Sources (each takes the max of its value and the current shake):

  | Source | Shake |
  |---|---|
  | Each shot | the gun's `shake`, .03–.16 |
  | Player hurt | .35 |
  | Boss hit | .6 |
  | Boss death | .8 |
  | Explosion | max(.25, .6 + .06r − d/30) |
  | Shockwave | .5 |
  | Prop destroyed | .2 |
  | Lamp lands | .2 |
  | Wall lands | .45 |
  | Airborne prop bounces | .25 |
  | Condemned creaking | .12 |
  | Collapse | 1.1 |

**Menu:** distance 14, look-ahead 3, survivor faces up the road; the camera drifts sideways `sin(0.15t) × 2`.

---

## 9. Look and rendering summary (for matching in URP)

- Background and fog `#0d1418`, linear fog. ACES tone mapping, exposure 1.15.
- Night lighting: blue hemisphere and moon, warm flashlight cone, emissive building windows, lamp light pools (decals).
- Coins have an emissive tint; zombie eyes are unlit and glow through the fog; guns have an emissive tint.
- Additive effects: muzzle flash, tracers, fire and glow sprites, rings, power-up halos, survivor ring.
- Decals: blob shadows, scorch marks, light pools.
- Fog-of-war ground shadows use a stencil buffer.

---

## 10. Quirks, dead code and README-vs-code differences

1. **Restart and Quit throw away the run.** `finish()` only runs after death, so coins picked up, stage bonuses, kills and the run count are lost. Killing the app mid-run loses them too. The README says coins are "kept even when you're overrun", which is true, but leaving any other way isn't covered.
2. Burning cars and tankers, and fire barrels, are **visual only**. There is no damage over time to the prop or to anyone nearby.
3. Recoil bloom only recovers when there's **no target**. Under constant fire it only resets on a reload or when targets run out.
4. Bullets start at the survivor's centre; tracers are drawn at y 1.4; aiming is by the survivor's yaw, so a shot only fires when within 0.3 rad of the target.
5. Building fronts hide zombies (at alleys) but cast no fog-of-war shadow.
6. The HUD stage label starts as "Stage 0" in the HTML until the first update.
7. **Dead or leftover code:**
   - `TEX.sign` ("SAFE ZONE"): unused. main.js's font-wait comment mentions the sign, but the font actually matters for the CONDEMNED sign.
   - `vfx.pop`, `sfx.win`, `nav.reachable`: unused.
   - CSS `#result[data-won]`: unused; the title is always "Overrun".
   - The meta description and manifest still mention "the safe zone … each level".
8. Escape on the bare main menu does nothing on the web; Android back exits.
9. The README's "every row leaves a gap of at least 2.5 m" is only guaranteed for barricade chokepoints (2.8 m). Other row types are placed randomly.
10. Randomness: mulberry32 per run (seed from the clock) and per chunk. Gameplay shares one RNG; effects use `Math.random`. Runs are not fully deterministic.
11. Coordinates: the web version runs up the road toward −z, and yaw = `atan2(dx, dz)` (π means facing up the road). The Unity scaffold flips this so +z is up the road.

Everything else in the README (systems, power-up behaviour, unlock stages, boss every 500 m, parts list, fog-of-war blockers) matches the code.
