# Graveway for Unity

The Unity version of [Graveway](../graveway/README.md). The whole game is
ported in code, the survivors, zombies and most guns are real models, and
props and the street are on their way. It hasn't been on a phone
yet. The web version stays the one you play and test until this one
catches up.

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
   Q/E switch guns, P or Esc pauses, N leaves a playtest note.

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
# Unit tests: random numbers, gun stats with parts, director, part prices, sounds, nav grid
& $unity -batchmode -nographics -projectPath graveway-unity -runTests -testPlatform EditMode -testResults graveway-unity/Logs/editmode.xml | Out-Null
# The game running: menu, a minute up the road, dying, the menus worked by
# their buttons; pictures of every screen to Logs/shots/
& $unity -batchmode -projectPath graveway-unity -runTests -testPlatform PlayMode -testResults graveway-unity/Logs/playmode.xml | Out-Null
# An APK, installed on a phone plugged in over USB
graveway-unity/build-android.ps1 -Version 0.1 -Code 1 -Install
```

The tests run silent. APKs are signed with the release key from
`%USERPROFILE%\.graveway\signing.properties` (see
`Editor/BuildScript.cs`), so every build installs over the last one. Keep
a backup of that folder: without it, phones have to uninstall (and lose
their progress) before the next build will install.

## How it's put together

Everything is put together in code at start-up, the way the web version
is: no prefabs and no scene set-up. The survivors and zombies are
Quaternius models (`Assets/ThirdParty/`, listed in [CREDITS.md](CREDITS.md))
that `Rigs` loads through the catalog `Editor/ArtImport.cs` makes. The rest
is still built in code from the web version's recipes until its real
models arrive: `Models` is the one place a model's look lives, and `Art`
the one place materials come from, so real models replace them without
touching the game logic.

| File | What's in it | Web version |
| --- | --- | --- |
| `Scripts/Core/Tunables.cs` | Every number: guns, parts, survivors, zombies, props, power-ups, upgrades, the director | `src/config.js` |
| `Scripts/Core/Game.cs` | Set-up, the frame loop, the survivor, auto-aim, camera, lights | `src/game.js` |
| `Scripts/Core/Game.Zombies.cs` | Spawning, steering, attacking, smashing, dying | `src/game.js` |
| `Scripts/Core/Game.Combat.cs` | Firing, bullets, ammo effects, grenades, explosions | `src/game.js` |
| `Scripts/Core/Game.Pickups.cs` | Coins, health, armor and power-ups | `src/game.js` |
| `Scripts/Combat/Weapon.cs` | A gun's stats from its parts, ammo, reloads, recoil | `src/weapons.js` |
| `Scripts/Combat/Director.cs` | Who spawns, where and when, from the distance | `src/director.js` |
| `Scripts/World/World.cs` | The endless street in chunks: buildings, alleys, props, pickups | `src/world.js` |
| `Scripts/World/PropSystem.cs` | Prop damage, fires, wrecks, toppling, the condemned building | `src/props.js` |
| `Scripts/World/NavGrid.cs` | The zombies' flow-field pathfinding | `src/nav.js` |
| `Scripts/World/Sight.cs` | Fog of war and its ground shadows | `src/fog.js` |
| `Scripts/Fx/Vfx.cs` | Particles: explosions, sparks, dust, smoke, fire | `src/vfx.js` |
| `Scripts/Art/Rigs.cs`, `ArtCatalog.cs` | The animated survivors and zombies, from the art catalog | `src/models.js` |
| `Scripts/Art/Guns.cs` | The real guns, with their fitted parts | `src/models.js`, `src/preview.js` |
| `Scripts/Art/` (the rest) | Guns, props and pickups built in code, and painted textures | `src/models.js` |
| `Scripts/Render/Batches.cs` + `Shaders/` | Instanced drawing of coins, bullets, particles and debris; the fog shadow shader | |
| `Scripts/Audio/Sfx.cs`, `Haptics.cs` | The 29 sounds, synthesised at start-up; vibration | `src/audio.js` |
| `Scripts/Core/StickInput.cs` | The floating joystick, any number of fingers, and keyboard keys (Input System) | `src/input.js` |
| `Scripts/UI/Hud.cs` | HUD, banners, toasts, the back button | `src/ui.js`, `index.html` |
| `Scripts/UI/Hud.Screens.cs` | Menu, survivor select, pause, results, settings | `src/ui.js` |
| `Scripts/UI/Hud.Market.cs` | The Black Market (the web's Armory): guns, loadout, parts and survivor upgrades | `src/ui.js` |
| `Scripts/UI/Hud.Notes.cs` | Playtest notes and Send to Claude | `src/ui.js` |
| `Scripts/UI/Studio.cs`, `Icons.cs` | Portraits and gun pictures rendered from the models; vector icons | `src/preview.js` |
| `Resources/UI/Graveway.uss` | The UI's look | `index.html` CSS |
| `Scripts/Core/Art.cs` | Materials (copies of `Resources/Materials`) | `src/models.js` |
| `Scripts/Core/Save.cs` | Progress and notes in PlayerPrefs | `src/save.js` |
| `Settings/` | The URP pipeline (mobile renderer, no shadow maps) and its post-processing: ACES and +0.94 EV, the web version's look | `game.js` renderer |
| `Editor/ProjectSetup.cs` | App and Android settings, base materials, panel settings, the scene (menu: Graveway → Set up project) | `android.json` |
| `Editor/ArtImport.cs` | How third-party models import, the art catalog with an Animator controller per character, and a report of every model's clips, bones and size | |
| `Editor/BuildScript.cs` + `build-android.ps1` | The APK build with no window, and installing it over USB | `tools/android/build-apk.mjs` |
| `Tests/` | EditMode tests against the web version's numbers; PlayMode tests of the game and its menus | |

Coordinates are the web version's: the road runs up **−z** from z = 2,
+x is right on the web's screen, y is up. Unity draws the same numbers as
a mirror image, so the joystick maps screen-right to world −x, and signs
flip their texture to read the right way round. (More in
[PORT_PLAN.md](PORT_PLAN.md).)

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

Still to come: the real art, the Bebas Neue font, the phone (the APK build
needs Unity's Android Build Support module), the update check, and a check
of everything against the web version.

## Updates on the phone

The web version's Android app updates itself, because its game is web
files it can download. A Unity game is compiled into the app, so new
builds need installing again. Builds go to My-Games GitHub Releases, and
the app offers "New version · tap to install" when there's a newer one
(Phase 7). Android still asks you to tap Install.
