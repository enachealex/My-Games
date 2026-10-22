# Graveway Unity port plan

The goal is a complete Unity version of Graveway for Android phones: everything
the web version (`graveway/`) does, with real 3D art instead of box people.
[docs/web-version-reference.md](docs/web-version-reference.md) lists every
mechanic, number, screen and quirk of the web version; it is the checklist
for "done".

Each phase is one or more PRs. Tick items off here as they land.

## Where it stands (21 October 2026)

The whole game is ported in code: the street, props and destruction, the
zombie AI and fog of war, combat, power-ups, effects, sound, haptics, and
every menu (Black Market, notes, settings). The survivors and zombies are
real animated models now (Quaternius's Zombie Apocalypse Kit, CC0), and the
UI uses Bebas Neue. Still to come:

- **The rest of the real art:** the street's buildings (after profiling
  on the phone), and a grenade launcher and minigun. The cars, lamps,
  barricades, fire barrels and litter are the kit's now (see
  [CREDITS.md](CREDITS.md)).
- **The phone** (Phase 0's last item, Phase 7): the APK builds (38.9 MB,
  signed with the release key, `build-android.ps1`); trying it needs the
  phone on USB.
- **Phase 7's update check** and **Phase 8's** item-by-item comparison.

### How the port maps the web version's coordinates

The simulation uses the web version's numbers as they are: the road runs up
−z from z = 2, and `world.js`, `props.js`, `nav.js`, `fog.js`, `vfx.js`,
`models.js` and `game.js` port almost line for line. three.js and Unity
build rotation matrices with the same components, so Unity draws a mirror
image of the web street. Only the joystick and keys account for it
(screen-right is world −x), signs and billboards flip their texture so text
reads, and the Black Market's gun pictures are shot from the other side.

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
- **Progress is never lost** (21 October 2026). The save is a file in the
  app's own storage, written the moment anything changes, swapped in only
  once it's safely on the disk, with the one before kept as a backup; a
  file that won't read is set aside, never overwritten. A run's coins and
  kills are kept however it ends: falling, Quit, Restart, or the app
  closing mid-run (written down every 10 s and on going to the background,
  and banked on the next start). Unlike the web version, which threw them
  away. The app keeps Android's backup on and, when uninstalled, offers to
  keep its data.
- **Art: Quaternius's CC0 packs** (21 October 2026): the Zombie Apocalypse
  Kit for the people, props and vehicles, Ultimate Guns for gun parts, the
  Downtown City MegaKit for the street later. One cartoon style, light
  enough for a phone, and free to keep in a public repo.
  [CREDITS.md](CREDITS.md) lists every pack with its source.

## Open decisions

- **Sound:** port the web version's synthesised sounds first. If they jar
  next to real art, swap in recorded CC0 sounds one category at a time.
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
Everything but the phone is done. The first APK built on 21 October 2026:
38.9 MB, arm64, Android 8.0 (API 26) and up, signed with the release key.

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

- [x] Split `Game` into systems owned by it, mirroring the web files:
      players, zombies, combat, and later props, nav, sight, pickups,
      power-ups, VFX and audio. (`Game.*.cs`, `PropSystem`, `NavGrid`,
      `Sight`, `Vfx`, `Director`, `Sfx`, `Hud.*.cs`.)
- [x] Entity views pooled: zombie models are reused, and nothing that
      moves per frame is a GameObject of its own. Props are built per
      chunk, as on the web.
- [x] Bullets, coins, particles and debris drawn instanced from the
      simulation arrays.
- [x] Animated survivors and zombies replace the box people (Humanoid.cs).
      Rook, Bram, Juno and Wren are the kit's Shaun, Matt, Sam and Lis;
      walkers, runners, tanks and the boss are its Basic, Ribcage, Chubby and
      Arm zombies (`Scripts/Art/Rigs.cs`). Each model gets an Animator
      controller with a state per clip, made by Set up project
      (`Editor/ArtImport.cs`).
      Animator parameters come from the simulation (speed, aiming,
      reloading, attacking, dying).
- [x] Real gun models with part sockets: the kit's pistol, SMG, shotgun
      and rifle (`Scripts/Art/Guns.cs`), wearing Ultimate Guns accessories
      for the parts that show, at points measured from each gun's mesh.
- [ ] Grenade launcher and minigun models: neither pack has one, so these
      two are still built in code. Quaternius's Toon Shooter Game Kit (CC0)
      has a grenade launcher, and a rocket launcher, sniper rifle and
      landmine for the after-the-port ideas; nothing has a minigun yet.
- [x] Save v2 on Newtonsoft: coins, best, runs, kills, character, owned,
      loadout, parts, survivor upgrades and settings, with notes kept
      separately (web `save.js`). Crash-safe files since 21 October 2026,
      covered by EditMode tests.
- [x] HUD: stop rebuilding strings and changing widths every frame, and
      split it into one view per screen.
- **Done when** 64 animated zombies hold 60 fps on the phone.

### Phase 2: the street and destruction (`props.js`, `world.js`)

Everything here works as in the web version. The cars, lamps,
barricades, fire barrels and litter are the Zombie Apocalypse Kit's
(`Scripts/Art/Kit.cs`); the tanker, explosive barrels, brick walls and the
condemned building keep their built models, which the kit has nothing like.

- [ ] Buildings from the Downtown City MegaKit. The free kit has three
      prebuilt buildings of fixed proportions and modular pieces with
      heavy textures (about 11 MB of colour maps, more with normals), and
      the street's fronts vary from 6 to 13 m wide and 6 to 24 m tall. That
      needs a modular builder and a frame-rate budget, so it waits for the
      first profile on the phone; the built fronts with lit windows stand
      in until then.

- [x] Street art: building fronts with lit windows, awnings, alleys,
      lamps, litter, the start barricade.
- [x] All 9 prop types with web hp, mass, materials and explosions.
- [x] Damage states: hit flash, walls darkening, cars and tankers smoking
      then burning.
- [x] Wrecks: thrown and tumbling, burnt look, still blocking.
- [x] Toppling walls and lamps that crush whatever they land on, and the
      condemned building's creak, collapse, rubble field and slabs.
- [x] Debris (420 cap), rubble, scorch decals, chained barrel explosions,
      blast impulse on props.
- [x] Bullets stop at props and building fronts, and auto-aim shoots
      barrels into crowds and drops condemned buildings on them.
- [x] Road coins, health packs and armor vests.

### Phase 3: zombie AI and fog of war (`nav.js`, `fog.js`)

- [x] Flow-field nav grid (0.5 m cells, 30 × 172, with rough cost ×3).
- [x] EditMode tests for the nav grid.
- [x] Line of sight, steering, getting stuck, smashing (tanks and bosses),
      shoving wrecks, rubble slowdown.
- [x] Sight checks every 0.1 s, zombies hidden behind tall cover,
      auto-aim limited to what can be seen.
- [x] Ground shadows behind walls, tankers and buildings (a dynamic mesh
      with a stencil shader in URP).

### Phase 4: finish combat (`game.js`, `weapons.js`)

- [x] Every ammo effect: hollow-point and armour-piercing bonuses,
      incendiary burning, damage to props, pierce, slug, dragon's breath,
      flechette.
- [x] Cluster, incendiary and concussion grenades.
- [x] Laser sight, weapon light, suppressed guns not drawing zombies.
- [x] Tracer colours by ammo, muzzle flash, recoil, camera shake table.
      Tracers now start at the muzzle (playtest note 1).
- [x] Five power-ups with drops (per chunk, tanks, boss), the timed-buff
      HUD, the coin magnet and vacuum.

### Phase 5: effects, sound and haptics (`vfx.js`, `audio.js`)

- [x] Fire and smoke particles that fade into the fog: explosions, sparks,
      embers, dust, shock rings, emitters on burning props, flames on
      burning zombies.
- [x] Sounds: the 29 web recipes generated with `AudioClip.Create`, plus
      a compressor, stereo panning and per-sound cooldowns.
- [x] Haptics through Android's Vibrator, with patterns.
- [x] A Battery saver quality level: effect counts, and render scale
      capped like the web's pixel ratio (2 on High, 1 on Battery saver; in
      player builds only, so the editor's URP asset isn't changed).

### Phase 6: menus and the meta game (`ui.js`, `index.html`, `preview.js`)

UI Toolkit, styled by `Resources/UI/Graveway.uss` (a port of the web
CSS). Portraits and gun pictures are rendered from the 3D models
(`UI/Studio.cs`).

- [x] Menu over the street, survivor select (swipe and arrows, 3D
      portraits), loadout.
- [x] The Black Market (the web version's Armory, renamed on 21 October
      2026): guns, parts with a 3D gun preview and effect chips, and
      survivor upgrades.
- [x] Results with tips, pause, settings, playtest tools.
- [x] Playtest notes, and Send to Claude as a GitHub issue on My-Games.
- [x] Back-button stack, safe areas, banners and toasts.
- [x] Bebas Neue font, wherever the web version uses it.
- [x] PlayMode tests that work the menus by their buttons, and pictures of
      every screen with the UI over the street.

### Phase 7: Android polish and delivery

- [x] Icon (the web version's, as an adaptive icon), no Unity splash,
      fullscreen, frame pacing.
- [ ] Heat over a 20-minute run (on the phone).
- [ ] Profile the worst case on the phone: 64 zombies plus a building
      collapse.
- [x] Releases: `build-android.ps1 -Version 0.2 -Code 2 -Release` builds
      from a pushed commit and publishes a My-Games pre-release tagged
      `graveway-unity-v<version>-<code>` with the APK. The app checks
      Releases as it loads (`Scripts/Core/Updates.cs`) and offers a newer
      build on the menu and in Settings; tapping it downloads the APK, and
      Android installs it over the old one. The web app's updater only
      reads `graveway-v…` tags, so it ignores these. 0.1 (code 1, 21 October
      2026) predates the check; 0.2 is the first build that has it.

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
