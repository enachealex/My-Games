# Graveway Unity port plan

The goal is a complete Unity version of Graveway for Android phones: everything
the web version (`graveway/`) does, with real 3D art instead of box people.
[docs/web-version-reference.md](docs/web-version-reference.md) lists every
mechanic, number, screen and quirk of the web version; it is the checklist
for "done".

Each phase is one or more PRs. Tick items off here as they land.

## Decisions (October 2026)

- **Engine:** Unity 6000.5.8f1 (installed on the dev PC), URP with the
  mobile renderer, the Input System package, IL2CPP, ARM64, portrait only.
- **Art: real art from the start.** Rigged, animated characters and real
  models for guns, props, vehicles and the street, brought in phase by
  phase. Gameplay numbers stay identical to the web version
  (`Tunables.cs` = `config.js`).
- **The web version is frozen** as the reference: bug fixes only, and
  new features go into Unity.
- **Builds are made on the dev PC** without opening the editor
  (`Unity -batchmode -executeMethod ...`), installed over USB with adb, or
  attached to a My-Games GitHub Release.
- **Phone updates:** builds are published as My-Games GitHub Releases. The
  app checks for a newer one on start and offers "New version · tap to
  install". (A Unity app can't swap its own code the way the web app swaps
  its files.)
- **Signing:** one permanent release keystore from the very first build,
  kept outside the repo and backed up. Losing it means every phone has to
  uninstall (and lose its progress) before the next update installs.
- **App id `com.thejumpvault.gravewayunity`**, so it installs next to the
  web app. Web progress can't be carried over (different app, different
  storage).

## Open decisions

- **Where the art comes from** (needed before Phase 1). The recommendation is
  free CC0 low-poly packs (Quaternius, Kenney): one consistent stylised
  look, light enough for 64 animated zombies on a phone, and no licence
  worries. The alternatives are paid stylised packs (for example Synty's
  POLYGON apocalypse and military sets), which are richer and still
  consistent, or more realistic Asset Store art, which is heavier and harder
  to keep consistent.
- **Sound:** port the web version's synthesised sounds first. If they jar
  next to real art, swap in recorded CC0 sounds one category at a time.
- **Leaving a run early:** the web version throws away a run's coins and
  kills on Restart or Quit, and if the app is killed. Keep that, or bank
  them?
- **Gun part prices** still need a playtest verdict.

## Art pipeline

- **Style:** stylised low-poly that reads from the high camera at night:
  strong silhouettes, emissive zombie eyes that show through fog, rim light
  on the survivor, emissive building windows.
- **Characters:** one humanoid rig (Mecanim Humanoid) with retargeted
  animations:
  - survivors: idle, walk and run, aiming (upper-body layer), reload, hit,
    death
  - zombies: shamble, run, attack, smash, hit, death (several variants),
    and a boss slam
  - four distinct survivors (Rook, Bram, Juno, Wren). Walkers and runners
    come as variants, the tank is bulky and the boss is huge.
- **Guns:** six models (pistol, SMG, shotgun, rifle, grenade launcher,
  minigun), each with sockets for muzzle, sight, magazine, handguard,
  stock and grip, so fitted parts show on the gun as they do on the web.
- **Props:** cars (several colours plus a burnt wreck look), fuel tanker,
  jersey barriers, barricades, explosive barrels, fire barrels, brick
  walls, street lamps, the condemned building (scaffolding, CONDEMNED
  sign, tape), slabs, rubble and debris chunks.
- **Street:** modular building fronts with lit windows, awnings, alleys,
  sidewalks and kerbs, a road with markings and stains, litter.
- **Pickups:** coin, health pack, armor vest, five power-up badges.
- **Budget for a mid-range phone:**
  - about 150k triangles on screen
  - a zombie under about 2k triangles and 30 bones, one material and
    atlas per character
  - Animator culling on
  - If 64 skinned zombies are too slow, bake their animations into
    textures (vertex animation) and draw them instanced.
- **Licences:** every third-party asset goes under
  `Assets/ThirdParty/<pack>/` and is listed with its licence in
  `CREDITS.md`. Each download is named, with its source and size, and
  approved before it happens.

## Phases

### Phase 0: make the project real

The first slice had never been opened in Unity. This phase gets it
compiling, running and on the phone, on the foundation the rest needs.
Everything but the phone is done: the APK build needs Unity's Android
Build Support module, which isn't installed yet.

- [x] Open the project in 6000.5.8f1 without a window, fix compile errors, and
      commit the generated `ProjectSettings/`, `.meta` files and
      `Packages/packages-lock.json`.
- [x] Packages: URP, Input System, Test Framework, Newtonsoft JSON, Android
      Logcat, an IDE package.
- [x] URP mobile renderer, linear colour space, ACES tone mapping
      (exposure about 1.15, as on the web), and fog set in the scene so it
      isn't stripped.
- [x] Material assets (or one material library) instead of `Shader.Find`
      at runtime. The URP shaders and keywords need to survive stripping,
      and the blob shadow has to be transparent.
- [x] A `PanelSettings` asset (runtime-created ones go blank on devices).
- [x] All input behind one facade on the Input System (EnhancedTouch for
      the floating joystick, with keys in the editor). The Android back
      button goes through it too.
- [x] Assemblies: `Graveway` (runtime), `Graveway.Editor`,
      `Graveway.Tests` (EditMode tests for `WeaponStats`, `Director`,
      `Rng`).
- [x] Fix the small bugs found in review:
      - `Rng.Next()` can return exactly 1.0, which breaks indexing.
      - `N0` number formatting follows the phone's locale.
      - 120 fps with vSync off: use 60 fps.
      - The muzzle and blast lights stay enabled at intensity 0.
      - Found while testing: part prices were worked out in floats and
        rounded halves to even, so 11 of the 147 were 5 coins off the web
        price (the rifle's long barrel was 510, not 515). A test now
        checks every price against the web version.
- [x] Android settings: min API 26 (Unity's default) and the newest target,
      Vulkan with a GLES3 fallback, optimised frame pacing, fullscreen.
      (The VIBRATE permission comes with haptics in Phase 5.)
- [x] Release keystore, in `%USERPROFILE%\.graveway\` with its passwords
      (back that folder up), and `*.keystore`, `*.jks` and
      `signing.properties` in `.gitignore`.
- [x] `Editor/BuildScript.cs`: an APK build with no window, version name
      and code passed in, and an optional `adb install -r`.
- [x] A PlayMode smoke test: boot, start a run, simulate 60 s, no
      exceptions.
- [ ] **Done when** the current slice runs on the phone at 60 fps.

### Phase 1: restructure, and the first real characters

- [ ] Split `Game` into systems owned by it, mirroring the web files:
      players, zombies, combat, and later props, nav, sight, pickups,
      power-ups, VFX and audio.
- [ ] Entity views as pooled prefabs: no `new GameObject` or `Destroy` per
      spawn, and moving objects at the root of the hierarchy.
- [ ] Bullets, coins, particles and debris drawn instanced from the
      simulation arrays.
- [ ] Animated survivors and zombies replace the box people (Humanoid.cs).
      Animator parameters come from the simulation (speed, aiming,
      reloading, attacking, dying).
- [ ] Real gun models with part sockets.
- [ ] Save v2 on Newtonsoft: coins, best, runs, kills, character, owned,
      loadout, parts, survivor upgrades and settings, with notes kept
      separately (web `save.js`).
- [ ] HUD: stop rebuilding strings and changing widths every frame, and
      split it into one view per screen.
- **Done when** 64 animated zombies hold 60 fps on the phone.

### Phase 2: the street and destruction (`props.js`, `world.js`)

- [ ] Street art: building fronts with lit windows, awnings, alleys,
      lamps, litter, the start barricade.
- [ ] All 9 prop types with web hp, mass, materials and explosions.
- [ ] Damage states: hit flash, walls darkening, cars and tankers smoking
      then burning.
- [ ] Wrecks: thrown and tumbling, burnt look, still blocking.
- [ ] Toppling walls and lamps that crush whatever they land on, and the
      condemned building's creak, collapse, rubble field and slabs.
- [ ] Debris (420 cap), rubble, scorch decals, chained barrel explosions,
      blast impulse on props.
- [ ] Bullets stop at props and building fronts, and auto-aim shoots
      barrels into crowds and drops condemned buildings on them.
- [ ] Road coins, health packs and armor vests.

### Phase 3: zombie AI and fog of war (`nav.js`, `fog.js`)

- [ ] Flow-field nav grid (0.5 m cells, 30 × 172, with rough cost ×3),
      covered by EditMode tests.
- [ ] Line of sight, steering, getting stuck, smashing (tanks and bosses),
      shoving wrecks, rubble slowdown.
- [ ] Sight checks every 0.1 s, zombies hidden behind tall cover,
      auto-aim limited to what can be seen.
- [ ] Ground shadows behind walls, tankers and buildings (a dynamic mesh
      with a stencil shader in URP).

### Phase 4: finish combat (`game.js`, `weapons.js`)

- [ ] Every ammo effect: hollow-point and armour-piercing bonuses,
      incendiary burning, damage to props, pierce, slug, dragon's breath,
      flechette.
- [ ] Cluster, incendiary and concussion grenades.
- [ ] Laser sight, weapon light, suppressed guns not drawing zombies.
- [ ] Tracer colours by ammo, muzzle flash, recoil, camera shake table.
- [ ] Five power-ups with drops (per chunk, tanks, boss), the timed-buff
      HUD, the coin magnet and vacuum.

### Phase 5: effects, sound and haptics (`vfx.js`, `audio.js`)

- [ ] Fire and smoke particles that fade into the fog: explosions, sparks,
      embers, dust, shock rings, emitters on burning props, flames on
      burning zombies.
- [ ] Sounds: the 30 web recipes generated with `AudioClip.Create`, plus
      a compressor, stereo panning and per-sound cooldowns.
- [ ] Haptics through Android's Vibrator, with patterns.
- [ ] A Battery saver quality level: render scale and effect counts.

### Phase 6: menus and the meta game (`ui.js`, `index.html`, `preview.js`)

- [ ] Menu scene, survivor select (swipe and arrows, 3D portraits), loadout.
- [ ] The Armory: guns, parts with a 3D gun preview and effect chips, and
      survivor upgrades.
- [ ] Results with tips, pause, settings, playtest tools.
- [ ] Playtest notes, and Send to Claude as a GitHub issue on My-Games.
- [ ] Back-button stack, safe areas, Bebas Neue font, banners and toasts.

### Phase 7: Android polish and delivery

- [ ] Icon, splash, fullscreen, frame pacing, heat over a 20-minute run.
- [ ] Profile the worst case on the phone: 64 zombies plus a building
      collapse.
- [ ] Releases: build here, attach the APK to a My-Games Release. In the
      app, check Releases on start and offer the new version, which opens
      the APK download.

### Phase 8: check everything against the web version, then switch

- [ ] Go through `docs/web-version-reference.md` item by item.
- [ ] Playtest, then retire the web APK. The web version stays in the repo.

## After the port: playtest notes (20 October 2026)

From playtesting web build 0.8.1 (build 13), to do once the port is
complete. The web version stays frozen, so these land in Unity only.

1. **Bullets come out of the survivor's chest, not the barrel.** The web
   version starts each bullet at the survivor's centre on purpose (so a
   gun pressed against a wall can't shoot through it). The Unity port
   keeps that hit test but draws the tracer and muzzle flash from the
   barrel, so it looks right. *(Done during the port, Phase 4.)*
2. **New power-up: mines.** Lays down 3 mines staggered behind the
   survivor, thrown out like grenades.
3. **Weapon pickups:** temporary boosts to damage, accuracy, rate of fire,
   reload speed and range.
4. **A shop for coins**, for when favourite guns are maxed out. Bought
   items go in a **backpack on the left of the screen**, within thumb
   reach, and are used from there:
   - airstrikes: cluster strike, strafing run, incendiary
   - helicopter air support for 30 s (the timer starts at its first shot)
   - extra survivors to fight alongside you, each with a random weapon
   - skip the next boss

   None of them are immediate: you hear the plane or helicopter coming
   first.
5. **New weapons:** sniper rifle and rocket launcher.
6. **New environments after every 2 bosses.** A helicopter lands about
   25 m ahead; walking up to it plays a cut scene of the survivor climbing
   aboard and being dropped off in the new environment.

## How work gets checked

- Unity runs without a window on the dev PC (`-batchmode`), so builds,
  tests and screenshots never take over the screen.
- Every change gets a compile and the EditMode tests, and gameplay changes
  get the PlayMode smoke test plus screenshots rendered offscreen.
- On the phone: an APK over USB, plus the Unity Profiler and Logcat for
  frame times and errors.
