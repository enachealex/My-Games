# Graveway for Unity

The Unity version of [Graveway](../graveway/README.md), being ported from
the web version one system at a time. The web version stays the one you
play and test until this one catches up.

## Opening it

1. Install [Unity Hub](https://unity.com/download) and, from it, **Unity
   6000.5.8f1** with **Android Build Support** (tick OpenJDK and the
   Android SDK & NDK under it).
2. In Unity Hub: **Add → Add project from disk**, pick this
   `graveway-unity` folder.
3. The first open takes a few minutes while Unity imports. Then open
   `Assets/Graveway/Scenes/Main.unity`. It's empty on purpose: the game
   builds itself when it starts.
4. Press **Play**. In the Game view, pick a phone size (for example
   1080×2400 Portrait) from the resolution menu. Drag with the mouse to
   move (or use WASD); aiming and shooting are automatic. R reloads, 1–4 or
   Q/E switch guns, P or Esc pauses.

If something's wrong, the **Console** window (Window → General → Console)
says what. Copy its red lines into a message to Claude.

Everything Unity needs is committed: project settings, `.meta` files, the
render pipeline, base materials and the scene. **Graveway → Set up
project** (`Editor/ProjectSetup.cs`) remakes the generated ones after a
change to that script.

## Tests and builds without the editor

Unity runs with no window (`-batchmode`), so none of this takes over the
screen. Close the project in the editor first: Unity can't open it twice.

```powershell
$unity = "C:\Program Files\Unity\Hub\Editor\6000.5.8f1\Editor\Unity.exe"
# Unit tests: random numbers, gun stats with parts, director, part prices
& $unity -batchmode -nographics -projectPath graveway-unity -runTests -testPlatform EditMode -testResults graveway-unity/Logs/editmode.xml | Out-Null
# The game running: menu, a minute up the road, dying; screenshots to Logs/shots/
& $unity -batchmode -projectPath graveway-unity -runTests -testPlatform PlayMode -testResults graveway-unity/Logs/playmode.xml | Out-Null
# An APK, installed on a phone plugged in over USB
graveway-unity/build-android.ps1 -Version 0.1 -Code 1 -Install
```

APKs are signed with the release key from
`%USERPROFILE%\.graveway\signing.properties` (see
`Editor/BuildScript.cs`), so every build installs over the last one. Keep
a backup of that folder: without it, phones have to uninstall (and lose
their progress) before the next build will install.

## How it's put together

Everything is made in code at start-up, the way the web version is: no
prefabs, no scene setup, and placeholder art (boxes and painted textures)
until real models arrive. Real models can be dropped in later without
touching the game logic: `Humanoid` is the one place a character's look
lives, and `Art` is the one place materials and meshes come from.

| File | What's in it | Web version |
| --- | --- | --- |
| `Scripts/Core/Tunables.cs` | Every number: guns, parts, survivors, zombies, the director | `src/config.js` |
| `Scripts/Core/Game.cs` | Set-up, the frame loop, the survivor, auto-aim, camera, lights | `src/game.js` |
| `Scripts/Core/Game.Zombies.cs` | Spawning, chasing, attacking, dying | `src/game.js` |
| `Scripts/Core/Game.Combat.cs` | Firing, bullets, grenades, explosions, blood | `src/game.js` |
| `Scripts/Combat/Weapon.cs` | A gun's stats from its parts, ammo, reloads, recoil | `src/weapons.js` |
| `Scripts/Combat/Director.cs` | Who spawns, where and when, from the distance | `src/director.js` |
| `Scripts/World/Road.cs` | The endless street in 32 m chunks | `src/world.js` |
| `Scripts/Actors/Humanoid.cs` | Survivors and zombies (box people for now) | `src/models.js` |
| `Scripts/Core/StickInput.cs` | The floating joystick, any number of fingers, and keyboard keys (Input System) | `src/input.js` |
| `Scripts/UI/Hud.cs` + `Resources/UI/Graveway.uss` | HUD, menu, pause, results (UI Toolkit) | `src/ui.js`, `index.html` |
| `Scripts/Core/Art.cs` | Materials (copies of `Resources/Materials`), meshes and textures made in code | `src/models.js` |
| `Scripts/Core/Save.cs` | Progress in PlayerPrefs | `src/save.js` |
| `Settings/` | The URP pipeline (mobile renderer, no shadow maps) and its post-processing: ACES and +0.94 EV, the web version's look | `game.js` renderer |
| `Editor/ProjectSetup.cs` | App and Android settings, base materials, panel settings, the scene (menu: Graveway → Set up project) | `android.json` |
| `Editor/BuildScript.cs` + `build-android.ps1` | The APK build with no window, and installing it over USB | `tools/android/build-apk.mjs` |
| `Tests/` | EditMode tests against the web version's numbers, and PlayMode smoke tests | |

Coordinates: **+z is up the road**, +x is right, y is up. The survivor
starts at z = 0. (The web version runs the other way, toward −z.)

Rendering is URP. Materials are never made from a shader looked up by
name: a phone build only keeps shaders (and shader variants) that some
material asset uses, so `Art` copies the base materials in
`Resources/Materials`. Input is the Input System package only;
`UnityEngine.Input` throws in this project.

## Porting plan

[PORT_PLAN.md](PORT_PLAN.md) has the plan for the complete port: the
decisions (real art from the start, URP, builds on the dev PC, updates
through GitHub Releases), the open questions, and the phases with their
progress. [docs/web-version-reference.md](docs/web-version-reference.md)
lists everything the web version does, as the checklist for matching it.

Phase 0 got the first slice compiling and running in Unity 6000.5 with
URP and the Input System, with tests. It hasn't been on a phone yet.

Done in this first slice (some of it partly, see Phase 0 and 1):

- [x] Most tunables from `config.js`, including gun parts (power-ups,
      survivor upgrades and props are still to come)
- [x] The endless street: chunks, buildings, alleys, lamps, litter, the start barricade
- [x] Survivors (all four) with their stats, perks and signature guns
- [x] Floating joystick with multi-finger hand-off, keyboard in the editor
- [x] Auto-aim, firing, spread and recoil, reloads, weapon switching
- [x] Bullets swept per frame with pierce; grenades and explosions that throw zombies
- [x] Zombies: walker, runner, tank, boss; wander, chase, attack, death fall
- [x] The Director: stages, hordes, stragglers, alley ambushes, bosses
- [x] HUD (distance, stage, coins, health, armor, ammo, weapon bar, boss bar), menu with survivor pick, pause, results
- [x] Saving coins, best distance, runs and kills

Everything else is in the plan's phases.

## Updates on the phone

The web version's Android app updates itself, because its game is web
files it can download. A Unity game is compiled into the app, so new
builds need installing again. Builds go to My-Games GitHub Releases, and
the app offers "New version · tap to install" when there's a newer one
(Phase 7). Android still asks you to tap Install.
