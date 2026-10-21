// The game: one endless run up the dead road. A port of graveway/src/
// game.js, split across files by topic (Game.Zombies.cs, Game.Combat.cs,
// Game.Pickups.cs). Game owns the camera, the lights and everything moving,
// and wires the parts together:
//   World      (World.cs)       streams the street in chunks
//   Director   (Director.cs)    spawns zombies by distance and time
//   NavGrid    (NavGrid.cs)     zombie pathfinding, rebuilt as the street changes
//   PropSystem (PropSystem.cs)  destructible props, debris, rubble
//   Sight      (Sight.cs)       fog of war
//   Vfx        (Vfx.cs)         fire, smoke, dust and sparks
//   Weapon     (Weapon.cs)      the guns in your hands
//   Hud        (Hud.cs)         everything on screen that isn't the world
//
// It builds itself: Boot() runs when any scene loads and makes one Game, so
// the scene can stay empty.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;

namespace Graveway
{
    public sealed class Player
    {
        public float x, z, yaw, hp, maxHp, armor;
        public PlayerStats stats;
        public List<Weapon> weapons = new List<Weapon>();
        public int wi;
        public float swapT, kick, walk, moving, hurtT, deadT, furthest;
        public bool firing, reported;
        public Target target;
        public readonly Dictionary<string, float> buffs = new Dictionary<string, float>();     // timed power-ups: seconds left
        public readonly Dictionary<string, float> buffMax = new Dictionary<string, float>();   // and what the timer started at
        public Weapon Weapon => weapons[wi];
    }

    // What the auto-aim picked: a zombie, a barrel to set off, or a spot (the
    // base of a condemned building).
    public struct Target
    {
        public bool has;
        public float x, z;
        public Zombie zombie;
        public Prop prop;
    }

    public sealed class RunStats
    {
        public int kills, bonus, startAt;
        public float coins, time, startZ;
        public Zombie boss;
    }

    public sealed partial class Game : MonoBehaviour
    {
        public enum State { Menu, Playing, Paused, Dying }

        static readonly Color Fog = Hex.C(0x0d1418);
        const float Tau = Mathf.PI * 2;

        public State state;
        // Playtest tool (web: Settings → Playtest tools → Invincible): hits
        // still land and flinch, but do no damage.
        public bool invincible;
        bool Invincible => invincible || Save.Data.settings.god;

        [System.NonSerialized] public Rng rng = new Rng(7);
        [System.NonSerialized] public World world;
        [System.NonSerialized] public Player player;
        [System.NonSerialized] public RunStats run;
        public readonly List<Zombie> zombies = new List<Zombie>();
        public float time, shake, fps = 60;

        [System.NonSerialized] public Director director;
        [System.NonSerialized] public NavGrid nav;
        [System.NonSerialized] public PropSystem propSys;
        [System.NonSerialized] public Vfx vfx;
        [System.NonSerialized] public Sight sight;
        [System.NonSerialized] public Hud hud;
        StickInput input;
        public StickInput Stick => input;
        public Camera Camera => cam;
        public CharacterDef Character => character;

        Camera cam;
        Light flashlight, muzzleLight, blastLight;
        Transform worldRoot;
        CharacterDef character;
        Humanoid surv;
        readonly Dictionary<string, Humanoid> survivors = new Dictionary<string, Humanoid>();
        Vector3 camPos = new Vector3(0, 12, 14), camLook = new Vector3(0, 0, -4);
        float groanT, noiseAt = -1, flashT, blastT, blastPeak;
        uint seed;

        static bool booted;

        // With domain reload off (Enter Play Mode settings), statics live on
        // between Play presses; start each one clean.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => booted = false;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (booted) return;
            booted = true;
            var go = new GameObject("Graveway");
            DontDestroyOnLoad(go);
            go.AddComponent<Game>();
        }

        void Awake()
        {
            // 60 fps: smooth enough for this game, and half the heat and
            // battery of running a 120 Hz phone flat out.
            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 0;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            input = new StickInput();
            Sfx.Init(transform);
            Sfx.Enabled = Save.Data.settings.sound;
            Haptics.Enabled = Save.Data.settings.haptics;

            // The web version's lights, converted: three.js divides diffuse
            // light by pi and Unity doesn't, three's lights fall off as
            // distance^-decay and URP's as distance^-2, and three's ACES
            // brightens by 1/0.6 (made up for by the +0.94 EV exposure in
            // Settings/Graveway_PostFX). The sky light is three's hemisphere
            // light (8aa0c8 over 2a2018, intensity 1.25).
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = Fog;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = Hex.C(0x8aa0c8) * 0.65f;
            RenderSettings.ambientEquatorColor = Hex.C(0x404c5e);
            RenderSettings.ambientGroundColor = Hex.C(0x2a2018) * 0.58f;

            cam = new GameObject("Camera").AddComponent<Camera>();
            cam.transform.SetParent(transform);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Fog;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 90;
            cam.tag = "MainCamera";
            // Tonemapping (ACES, like the web version) comes from the pipeline's
            // volume profile, Settings/Graveway_PostFX.
            cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;

            // Moonlight from above, the survivor's flashlight, and two lights
            // that only switch on for muzzle flashes and explosions.
            var moon = new GameObject("Moon").AddComponent<Light>();
            moon.transform.SetParent(transform);
            moon.type = LightType.Directional;
            moon.color = Hex.C(0xb0c4ff);
            moon.intensity = 0.9f / Mathf.PI;
            moon.transform.rotation = Quaternion.LookRotation(new Vector3(8, -20, -6));
            moon.shadows = LightShadows.None;
            flashlight = MakeLight("Flashlight", LightType.Spot, Hex.C(0xfff0d0), 70, 34);
            // Off until a shot or a blast: a light left on at zero still costs
            // a pass over everything it touches.
            muzzleLight = MakeLight("Muzzle", LightType.Point, Hex.C(0xffc070), 0, 10);
            muzzleLight.enabled = false;
            blastLight = MakeLight("Blast", LightType.Point, Hex.C(0xff9040), 0, 16);
            blastLight.enabled = false;

            worldRoot = new GameObject("World").transform;
            nav = new NavGrid();
            vfx = new Vfx(worldRoot);
            SetQuality(Save.Data.settings.quality);
            sight = new Sight(worldRoot);
            propSys = new PropSystem(this);
            director = new Director(this);
            hud = new GameObject("HUD").AddComponent<Hud>();
            hud.transform.SetParent(transform);
            hud.Init(this);
            input.overUi = hud.IsOverButton;
            InitBatches();
            ShowMenu();
        }

        Light MakeLight(string name, LightType type, Color c, float intensity, float range)
        {
            var l = new GameObject(name).AddComponent<Light>();
            l.transform.SetParent(transform);
            l.type = type;
            l.color = c;
            l.intensity = intensity;
            l.range = range;
            l.shadows = LightShadows.None;
            l.renderMode = LightRenderMode.ForcePixel;
            return l;
        }

        // ── Setup ────────────────────────────────────────────────────────
        public void SetQuality(string q)
        {
            vfx.SetQuality(q);
            // As the web version caps the device pixel ratio: 2 on High, 1 on
            // Battery saver. Not in the editor, where changing the pipeline
            // asset would change the file.
            if (!Application.isEditor && UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
            {
                float dpr = Screen.dpi > 0 ? Screen.dpi / 160f : 2.75f;
                urp.renderScale = Mathf.Clamp(Mathf.Min(dpr, q == "low" ? 1 : 2) / dpr, 0.25f, 1);
            }
        }

        // Swaps the survivor model to a character from Tunables.Characters.
        void SetCharacter(string id)
        {
            var c = Tunables.Character(id);
            if (!survivors.TryGetValue(c.id, out var m))
            {
                m = Models.Survivor(c.look);
                m.root.name = c.name;
                m.root.transform.SetParent(transform, false);
                foreach (var w in Tunables.Weapons)
                {
                    var g = Models.Gun(w.model, null);
                    g.key = "{}";
                    g.group.transform.SetParent(m.body, false);
                    g.group.SetActive(false);
                    m.guns[w.id] = g;
                }
                survivors[c.id] = m;
            }
            if (surv != null && surv != m) surv.root.SetActive(false);
            surv = m;
            surv.root.SetActive(true);
            character = c;
            StandUp();
        }

        void StandUp()
        {
            surv.root.SetActive(true);
            surv.body.localRotation = Quaternion.identity;
            surv.body.localPosition = Vector3.zero;
        }

        void ClearRun()
        {
            world?.Dispose();
            world = null;
            foreach (var z in zombies) Recycle(z);
            zombies.Clear();
            ClearCombat();
            propSys.Reset();
            vfx.Clear();
            sight.Clear();
            run = null;
        }

        // The menu shows the first stretch of road with a few zombies about.
        public void ShowMenu()
        {
            ClearRun();
            state = State.Menu;
            rng = new Rng(7);
            SetCharacter(Save.Data.character);
            world = new World(worldRoot, 7, World.StartPz);
            player = MakePlayer();
            player.z = -6;
            world.Update(player.z, 0);
            for (int i = 0; i < 9; i++) SpawnZombie("walker", (rng.Next() - 0.5f) * 11, -12 - rng.Next() * 22, 0, calm: true);
            input.enabled = false;
            SyncSurvivor();
        }

        Player MakePlayer()
        {
            var c = character;
            var stats = Tunables.StatsFor(c, Save.Data.pup);
            // Your survivor's own gun first, then the ones equipped in the
            // Black Market, each with its parts, and the models in their hands to match.
            var p = new Player
            {
                x = 0, z = World.StartPz, yaw = Mathf.PI, hp = stats.maxHp, maxHp = stats.maxHp,
                armor = Mathf.Min(Tunables.MaxArmor, stats.startArmor), stats = stats, furthest = World.StartPz,
            };
            foreach (var id in Save.RunWeapons(c))
            {
                var w = new Weapon(id, Save.FittedParts(id), c.perk);
                FitGun(w);
                p.weapons.Add(w);
            }
            return p;
        }

        // Rebuilds the survivor's model of a gun when its parts have changed.
        void FitGun(Weapon w)
        {
            var m = surv;
            string key = PartsKey(w.fitted);
            m.guns.TryGetValue(w.Id, out var old);
            if (old != null && old.key == key) return;
            var g = Models.Gun(w.def.model, w.fitted);
            g.key = key;
            g.group.transform.SetParent(m.body, false);
            g.group.SetActive(false);
            if (old != null)
            {
                Destroy(old.group);
                Art.DestroyAll(old.own);
            }
            m.guns[w.Id] = g;
        }

        static string PartsKey(IDictionary<string, string> fitted)
        {
            if (fitted == null || fitted.Count == 0) return "{}";
            var keys = new List<string>(fitted.Keys);
            keys.Sort(System.StringComparer.Ordinal);
            var sb = new System.Text.StringBuilder();
            foreach (var k in keys) sb.Append(k).Append(':').Append(fitted[k]).Append(';');
            return sb.ToString();
        }

        // Where the current gun's muzzle is, in the world.
        public Vector3 Muzzle()
        {
            var p = player;
            var gun = surv.guns[p.Weapon.Id];
            float sin = Mathf.Sin(p.yaw), cos = Mathf.Cos(p.yaw);
            float reach = Models.GunMount.z + gun.len, gx = Models.GunMount.x;
            return new Vector3(p.x + sin * reach + gx * cos, Models.GunMount.y + 0.05f, p.z + cos * reach - gx * sin);
        }

        public void StartRun()
        {
            ClearRun();
            SetCharacter(Save.Data.character);
            var settings = Save.Data.settings;
            int startAt = settings.startAt;
            float startZ = World.StartPz - startAt;
            seed = (uint)(System.DateTime.Now.Ticks / 10000 & 0xffff) ^ 0x5bd1u;
            rng = new Rng(seed);
            world = new World(worldRoot, seed, startZ) { lotsOfPowerups = settings.powers };
            world.onCoins = spots => { foreach (var s in spots) AddCoin(s.x, 0.55f, s.y, 0, 0, 0, true); };
            player = MakePlayer();
            player.z = player.furthest = startZ;
            run = new RunStats { startAt = startAt, startZ = startZ };
            director.Reset(startAt);
            nav.Reset(startZ);
            world.Update(startZ, time);
            shake = 0;
            groanT = 2;
            state = State.Playing;
            StandUp();
            SyncSurvivor();
            camPos = new Vector3(0, 30, startZ + 30);
            input.enabled = true;
            hud.ShowHud();
            hud.Banner(character.name, startAt > 0 ? $"Starting at {startAt} m" : "Get as far up the road as you can");
        }

        // Metres up the road: the furthest point reached this run.
        public float Distance => Mathf.Max(0, World.StartPz - player.furthest);
        public int Stage => Mathf.FloorToInt(Distance / Tunables.Director.StageLength);
        public Weapon Weapon => player.Weapon;

        public void Pause() { if (state == State.Playing) { state = State.Paused; input.enabled = false; hud.ShowPause(); } }
        public void Resume() { if (state == State.Paused) { state = State.Playing; input.enabled = true; hud.ShowHud(); } }

        void OnApplicationPause(bool paused) { if (paused) Pause(); }
        void OnApplicationFocus(bool focus) { if (!focus) Pause(); }

        // ── Frame ────────────────────────────────────────────────────────
        void Update()
        {
            float raw = Time.deltaTime;
            float dt = Mathf.Min(raw, 1 / 20f);
            if (raw > 0) fps += (1 / raw - fps) * 0.05f;
            time += dt;
            input.Update();
            if (GameKeys.Back) hud.Back();
            if (state == State.Playing) Keys();

            var p = player;
            if (world != null && state != State.Paused) world.Update(p.z, time);

            if (state == State.Playing)
            {
                run.time += dt;
                UpdatePlayer(dt, input.Move);
                director.Update(dt, Distance);
            }
            else if (state == State.Dying)
            {
                p.deadT += dt;
                float k = Mathf.Min(p.deadT / 0.6f, 1);
                surv.body.localRotation = M.Euler(-Mathf.PI / 2 * k * k, 0, 0);
                surv.body.localPosition = new Vector3(0, 0.15f * k, 0);
                if (p.deadT > 1.6f && !p.reported) { p.reported = true; Finish(); }
            }

            if (state != State.Paused)
            {
                if (world.navDirty) { nav.dirty = true; world.navDirty = false; }
                if (state != State.Menu) nav.Update(dt, p.x, p.z, world.props, world.rough);
                UpdatePending(dt);
                UpdateThrows(dt);
                UpdateZombies(dt);
                UpdateBullets(dt);
                UpdateGrenades(dt);
                propSys.Update(dt);
                vfx.Update(dt);
                UpdateCoins(dt);
                UpdateItems(dt);
                UpdateParticles(dt);
            }
            UpdateCamera(dt);
            SyncSurvivor();
            if (world != null && state != State.Paused) sight.Update(p.x, p.z, world.props);
            UpdateFx(dt);
            // While dying too, so the bars show the hit that finished you.
            if (state == State.Playing || state == State.Dying) hud.UpdateHud();
            hud.UpdateStick(input);
        }

        void Keys()
        {
            if (GameKeys.Down(Key.R) || GameKeys.Down(Key.Space)) Reload();
            if (GameKeys.Down(Key.Q)) SwapWeapon(-1);
            if (GameKeys.Down(Key.E) || GameKeys.Down(Key.Tab)) SwapWeapon(1);
            if (GameKeys.Down(Key.P)) Pause();
            for (int i = 0; i < 9; i++) if (GameKeys.Down(Key.Digit1 + i)) SelectWeapon(i);
        }

        // ── Player ───────────────────────────────────────────────────────
        static float Damp(float k, float dt) => 1 - Mathf.Exp(-k * dt);

        static float LerpAngle(float a, float b, float t)
        {
            float d = Mathf.Repeat(b - a + Mathf.PI, Tau) - Mathf.PI;
            return a + d * t;
        }

        void UpdatePlayer(float dt, Vector3 mv)
        {
            var p = player;
            var props = world.props;
            float len = Mathf.Sqrt(mv.x * mv.x + mv.z * mv.z);
            float slow = nav.IsRough(p.x, p.z) ? Tunables.RubbleSlowPlayer : 1;
            p.moving = len * slow;
            p.x += mv.x * p.stats.speed * slow * dt;
            p.z += mv.z * p.stats.speed * slow * dt;
            // Zombies are solid: a crowd blocks the road until you shoot a way
            // through or go round. Bigger ones shove you harder.
            foreach (var z in zombies)
            {
                if (z.dead) continue;
                float dx = p.x - z.x, dz = p.z - z.z, min = z.radius + p.stats.radius;
                if (Mathf.Abs(dx) > min || Mathf.Abs(dz) > min) continue;
                float d = Mathf.Sqrt(dx * dx + dz * dz);
                if (d < min && d > 1e-4f)
                {
                    float share = z.scale > 1.2f ? 0.9f : 0.65f, push = min - d;
                    p.x += dx / d * push * share; p.z += dz / d * push * share;
                    z.x -= dx / d * push * (1 - share); z.z -= dz / d * push * (1 - share);
                }
            }
            World.ResolveColliders(props, ref p.x, ref p.z, p.stats.radius);
            p.x = Mathf.Clamp(p.x, -World.WalkHalf, World.WalkHalf);
            // No going back past the start barricade, or more than 20 m back
            // down the road (it's being torn down behind you).
            p.z = Mathf.Min(p.z, Mathf.Min(run.startZ + 4, p.furthest + 20));
            p.furthest = Mathf.Min(p.furthest, p.z);
            p.walk += len * slow * dt * 11;

            var w = p.Weapon;
            var target = ChooseTarget(w);
            p.target = target;

            float desired = p.yaw;
            if (target.has) desired = Mathf.Atan2(target.x - p.x, target.z - p.z);
            else if (len > 0.05f) desired = Mathf.Atan2(mv.x, mv.z);
            p.yaw = LerpAngle(p.yaw, desired, Damp(target.has ? 22 : 12, dt));

            TickBuffs(dt);
            p.kick = Mathf.Max(0, p.kick - dt * 9);
            if (p.swapT > 0) p.swapT -= dt;
            p.firing = false;
            if (w.Tick(dt, target.has)) Sfx.Reload();
            if (target.has && w.CanFire && p.swapT <= 0)
            {
                float off = Mathf.Abs(LerpAngle(0, desired - p.yaw, 1));
                if (off < 0.3f) { Fire(target); p.firing = true; }
            }
            if (p.hurtT > 0) p.hurtT -= dt;
        }

        // Is (x, z) inside the rotated box b, grown by pad?
        static bool InBox(PropSystem.Footprint b, float x, float z, float pad)
        {
            float dx = x - b.x, dz = z - b.z;
            float lx = dx * b.cos - dz * b.sin, lz = dx * b.sin + dz * b.cos;
            return Mathf.Abs(lx) < b.hw + pad && Mathf.Abs(lz) < b.hd + pad;
        }

        // Auto-aim. The nearest zombie in range, preferring ones up the road
        // and ones with a clear line of fire. Zombies behind a wreck you can't
        // shoot through are skipped; behind something breakable, they're a
        // last resort. An explosive barrel with a crowd round it beats any
        // single zombie, and with a launcher, so does a condemned building
        // that would fall on one.
        Target ChooseTarget(Weapon w)
        {
            var p = player;
            float range = w.stats.range, minRange = w.stats.minRange;
            var props = world.props;
            Target best = default;
            float bestScore = float.PositiveInfinity;
            foreach (var z in zombies)
            {
                if (z.dead || !z.seen) continue;          // can't aim at what you can't see
                float dx = z.x - p.x, dz = z.z - p.z, d2 = dx * dx + dz * dz;
                if (d2 > range * range || d2 < minRange * minRange) continue;
                float d = Mathf.Sqrt(d2);
                float score = d + (dz > 1 ? 4 : 0) + (p.target.zombie == z ? -1.5f : 0);
                float t = World.RayProps(props, p.x, p.z, dx / d, dz / d, d - z.radius, out var hit);
                if (!w.Explosive)
                {
                    // Round a building corner, into a wreck, or into a car or
                    // barrel close enough to take you with it: no shot.
                    if (world.FacadeT(p.x, p.z, dx / d, dz / d, d) < d) continue;
                    if (hit != null)
                    {
                        if (hit.indestructible) continue;
                        var ex = hit.def.explodes;
                        if (ex != null && t < ex.radius + 1) continue;
                        score += 8;
                    }
                }
                else if (hit != null && hit.height > 2 && t < w.stats.blast.radius + 0.5f) continue;   // a grenade's arc clears a car, not a wall beside you
                if (score < bestScore) { bestScore = score; best = new Target { has = true, x = z.x, z = z.z, zombie = z }; }
            }
            // Barrels worth shooting: two or more zombies in the blast, and you clear of it.
            foreach (var b in props)
            {
                if (b.kind != "barrel" || b.destroyed) continue;
                float dx = b.x - p.x, dz = b.z - p.z, d = Mathf.Sqrt(dx * dx + dz * dz), r = b.def.explodes.radius;
                if (d > range || d < r + 1) continue;
                // Bullets have to actually reach it: not through a wreck.
                if (!w.Explosive) { World.RayProps(props, p.x, p.z, dx / d, dz / d, d, out var first); if (first != b) continue; }
                int n = 0;
                foreach (var z in zombies) if (!z.dead && Mathf.Sqrt((z.x - b.x) * (z.x - b.x) + (z.z - b.z) * (z.z - b.z)) < r) n++;
                if (n < 2) continue;
                float score = d - n * 3;
                if (score < bestScore) { bestScore = score; best = new Target { has = true, x = b.x, z = b.z, prop = b }; }
            }
            if (w.Explosive)
            {
                foreach (var b in props)
                {
                    if (b.kind != "condemned" || b.destroyed) continue;
                    float d = Mathf.Sqrt((b.x - p.x) * (b.x - p.x) + (b.z - p.z) * (b.z - p.z));
                    if (d > range + 4) continue;
                    var fp = propSys.FootprintOf(b, b.cos, -b.sin);
                    if (InBox(fp, p.x, p.z, 1)) continue;
                    int n = 0;
                    foreach (var z in zombies) if (!z.dead && InBox(fp, z.x, z.z, 0)) n++;
                    if (n < 3) continue;
                    // aim at its base, on the road side
                    float tx = b.x + b.cos * 1.6f, tz = b.z - b.sin * 1.6f;
                    float score = Mathf.Sqrt((tx - p.x) * (tx - p.x) + (tz - p.z) * (tz - p.z)) - n * 3;
                    if (score < bestScore) { bestScore = score; best = new Target { has = true, x = tx, z = tz, prop = b }; }
                }
            }
            return best;
        }

        public void Reload()
        {
            if (state != State.Playing) return;
            if (player.Weapon.StartReload()) Sfx.Reload();
        }

        public void SelectWeapon(int i)
        {
            var p = player;
            if (state != State.Playing || i == p.wi || i < 0 || i >= p.weapons.Count) return;
            p.Weapon.CancelReload();
            p.wi = i;
            p.swapT = 0.25f;
            if (p.Weapon.ammo <= 0) Reload();
            Sfx.Swap();
        }

        public void SwapWeapon(int dir)
        {
            int n = player.weapons.Count;
            if (n > 1) SelectWeapon((player.wi + dir + n) % n);
        }

        public void HurtPlayer(float dmg, float fromX, float fromZ, bool blast = false)
        {
            var p = player;
            if (state != State.Playing) return;
            if (Invincible) dmg = 0;     // playtest tool: still flinch, never hurt
            if (blast) dmg *= 1 - p.stats.explosionResist;
            if (p.armor > 0)
            {
                float soak = Mathf.Min(p.armor, dmg * Tunables.ArmorAbsorb);
                p.armor -= soak;
                dmg -= soak;
            }
            p.hp -= dmg;
            p.hurtT = 0.35f;
            shake = Mathf.Max(shake, 0.35f);
            hud.FlashHurt();
            Sfx.Hurt();
            Haptics.Vibrate(45);
            Burst(p.x, 1.3f, p.z, 6, Hex.C(0x8a0d0d), speed: 2.5f);
            // a shove away from the attacker
            float dx = p.x - fromX, dz = p.z - fromZ, l = Mathf.Sqrt(dx * dx + dz * dz);
            if (l == 0) l = 1;
            p.x += dx / l * (blast ? 0.8f : 0.35f);
            p.z += dz / l * (blast ? 0.8f : 0.35f);
            if (p.hp <= 0)
            {
                p.hp = 0;
                state = State.Dying;
                p.deadT = 0;
                input.enabled = false;
                Sfx.Lose();
            }
        }

        void SyncSurvivor()
        {
            var p = player;
            if (p == null || surv == null) return;
            var m = surv;
            m.root.transform.localPosition = new Vector3(p.x, 0, p.z);
            m.root.transform.localRotation = M.Yaw(p.yaw);
            string id = p.Weapon.Id;
            foreach (var kv in m.guns) if (kv.Value.group.activeSelf != (kv.Key == id)) kv.Value.group.SetActive(kv.Key == id);
            var gun = m.guns[id].group.transform;
            gun.localPosition = new Vector3(Models.GunMount.x, Models.GunMount.y, Models.GunMount.z - p.kick * 0.12f);
            float sw = Mathf.Sin(p.walk) * 0.7f * Mathf.Min(p.moving * 1.5f, 1);
            m.legL.localRotation = M.Euler(sw, 0, 0);
            m.legR.localRotation = M.Euler(-sw, 0, 0);
            if (state != State.Dying) m.body.localPosition = new Vector3(0, Mathf.Abs(Mathf.Cos(p.walk)) * 0.06f * p.moving, 0);
            bool reloading = p.Weapon.Reloading;
            // Arms angled a little down, so the hands meet the gun.
            float aim = -Mathf.PI / 2 + 0.12f + p.kick * 0.18f + (reloading ? 0.6f : 0);
            m.armR.localRotation = M.Euler(aim, 0, 0.3f);
            m.armL.localRotation = M.Euler(aim + (reloading ? Mathf.Sin(time * 18) * 0.3f : 0), 0, -0.62f);
        }

        // ── Camera and lights ────────────────────────────────────────────
        void UpdateCamera(float dt)
        {
            var p = player;
            if (p == null) return;
            // The camera's own shape, not the screen's: they differ when it
            // renders to a texture (screenshots).
            cam.fieldOfView = cam.aspect <= 1 ? 68 : 52;
            float tanH = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * cam.aspect;
            float dist = Mathf.Clamp(5.8f / tanH, 13, 20), lookAhead = 4;
            if (state == State.Menu)
            {
                dist = 14;
                lookAhead = 3;                 // puts the survivor just above the menu buttons
                p.yaw = Mathf.PI;
            }
            RenderSettings.fogStartDistance = dist * 0.85f;
            RenderSettings.fogEndDistance = dist + 36;
            const float el = 0.92f;
            float tx = p.x * 0.55f, tz = p.z - lookAhead;
            camLook.x += (tx - camLook.x) * Damp(5, dt);
            camLook.z += (tz - camLook.z) * Damp(5, dt);
            camLook.y = 0.8f;
            var want = new Vector3(tx, Mathf.Sin(el) * dist, tz + Mathf.Cos(el) * dist + lookAhead * 0.4f);
            camPos = Vector3.Lerp(camPos, want, Damp(state == State.Playing ? 4.5f : 2, dt));
            var pos = camPos;
            if (shake > 0)
            {
                float s = shake * 0.35f;
                pos.x += (rng.Next() - 0.5f) * s;
                pos.y += (rng.Next() - 0.5f) * s;
                shake = Mathf.Max(0, shake - dt * 2.2f);
            }
            if (state == State.Menu) pos.x += Mathf.Sin(time * 0.15f) * 2;
            cam.transform.position = pos;
            cam.transform.LookAt(camLook);
        }

        // ── Stages and the end ───────────────────────────────────────────
        public void OnStage(int stage)
        {
            int bonus = Tunables.Director.StageBonus(stage);
            run.bonus += bonus;
            hud.Banner($"Stage {stage + 1}", $"{stage * Tunables.Director.StageLength:0} m · +{bonus} coins · they're getting tougher", 1.8f);
            Sfx.Milestone();
        }

        void Finish()
        {
            var r = run;
            var s = Save.Data;
            int distance = Mathf.FloorToInt(Distance), coins = Mathf.FloorToInt(r.coins);
            var result = new Hud.Result
            {
                character = character.name, distance = distance, stage = Stage, kills = r.kills, coins = coins, bonus = r.bonus,
                time = r.time, best = distance > s.best, startAt = r.startAt,
            };
            s.coins += coins + r.bonus;
            s.kills += r.kills;
            s.runs++;
            // A run started further up the road (playtest tool) doesn't set records.
            if (r.startAt == 0) s.best = Mathf.Max(s.best, distance);
            else result.best = false;
            result.wallet = s.coins;
            result.bestEver = s.best;
            Save.Persist();
            hud.ShowResult(result);
        }

        public int AliveCount() { int n = 0; foreach (var z in zombies) if (!z.dead) n++; return n; }
        public bool BuffOn(string id) => player.buffs.TryGetValue(id, out var t) && t > 0;
    }
}
