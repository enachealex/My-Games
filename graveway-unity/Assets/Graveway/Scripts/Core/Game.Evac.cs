// The evac (playtest note 6): after every second boss (beaten, skipped or
// outrun), a helicopter lands up the road, with a ring and a smoke flare,
// and the way past it blocked. Walking into the ring plays a short cut
// scene: the survivor runs aboard, it lifts off up the road, and the screen
// goes to black with the next place's name; it comes down again there, the
// survivor steps out, and it leaves. The run goes on where it was, in the
// new place (Relocate). After the first, a tap skips the cut scene.

using UnityEngine;

namespace Graveway
{
    public sealed partial class Game
    {
        enum EvacPhase { Inbound, Down, Aboard, Arriving, Leaving }

        sealed class Evac
        {
            public GameObject go;
            public Transform rotor, tail;
            public MeshRenderer shadow, ring;
            public Vfx.Emitter flare;
            public float x, y, z, t, padX, padZ;
            public EvacPhase phase;
            public Vector2 walkFrom;
        }

        const float EvacRing = 3.2f, Landed = 1.1f;       // the ring's radius; the body's height with its skids down
        Evac evac;
        Vector3? cutFrom;                                   // a cut scene's camera, and what it looks at
        Vector3 cutAt;

        public bool EvacCalled => evac != null;
        public bool InCutScene => state == State.Evac;

        // A boss beaten, skipped or outrun: after every second, the evac.
        void BossPassed()
        {
            if (run == null) return;
            run.bossesPassed++;
            if (run.bossesPassed % Tunables.BossesPerEvac == 0) CallEvac();
        }

        public void CallEvac()
        {
            if (run == null || evac != null || state != State.Playing) return;
            var p = player;
            float padZ = p.z - Tunables.EvacAhead;
            var e = evac = new Evac { padX = 0, padZ = padZ, x = 12, y = 16, z = padZ - 40 };
            e.go = HeliModel(out e.rotor, out e.tail);
            e.go.transform.SetParent(worldRoot, false);
            e.shadow = Art.BlobShadow(worldRoot, 5, 0.45f);
            e.ring = Art.GlowDecal(worldRoot, 0x9be15d, EvacRing * 2, 0.55f, Tex.Ring);
            e.ring.transform.localPosition = new Vector3(e.padX, 0.04f, padZ);
            e.flare = vfx.Emit(new Vfx.Emitter { kind = "smoke", x = e.padX + 2.8f, y = 0.3f, z = padZ + 1.6f, life = 1e9f, rate = 8,
                size = 1.2f, alpha = 0.55f, color = new Vector3(0.45f, 0.95f, 0.4f) });
            // Clear where it lands, and block the road past it: the only way
            // on is aboard.
            foreach (var pr in world.props)
            {
                if (pr.destroyed) continue;
                float dx = pr.x - e.padX, dz = pr.z - padZ;
                if (dx * dx + dz * dz > 7.5f * 7.5f) continue;
                pr.destroyed = pr.removed = true;
                pr.blocking = false;
                pr.group.gameObject.SetActive(false);
            }
            int chunk = world.ChunkAt(padZ - 8);
            for (float x = -World.WalkHalf + 0.6f; x < World.WalkHalf + 0.5f; x += 2.05f)
            {
                var b = world.AddProp("jersey", Models.Jersey(), x, padZ - 8, 0, chunk);
                if (b != null) b.indestructible = true;
            }
            world.navDirty = true;
            hud.Banner("Evac incoming", "Get to the helicopter up the road", 2.6f);
            Sfx.Rotor();
        }

        // The playtest tool: the evac now. The run's off the scoreboard.
        public void PlaytestEvac()
        {
            if (run == null) return;
            run.fair = false;
            CallEvac();
        }

        void UpdateEvac(float dt)
        {
            var e = evac;
            if (e == null) return;
            e.t += dt;
            e.rotor.Rotate(0, 1500 * dt, 0, Space.Self);
            e.tail.Rotate(2200 * dt, 0, 0, Space.Self);
            var p = player;
            var next = Tunables.PlaceAt(run.place + 1);
            switch (e.phase)
            {
                case EvacPhase.Inbound:
                {
                    // In from up the road, over the pad, and down.
                    var to = new Vector3(e.padX, e.t < 4 ? 6 : Landed, e.padZ);
                    float k = 1 - Mathf.Exp(-(e.t < 4 ? 1.1f : 1.8f) * dt);
                    e.x += (to.x - e.x) * k; e.y += (to.y - e.y) * k; e.z += (to.z - e.z) * k;
                    if (e.t > 6) { e.phase = EvacPhase.Down; e.t = 0; }
                    break;
                }
                case EvacPhase.Down:
                {
                    float k = 1 - Mathf.Exp(-3 * dt);
                    e.x += (e.padX - e.x) * k; e.y += (Landed - e.y) * k; e.z += (e.padZ - e.z) * k;
                    float dx = p.x - e.padX, dz = p.z - e.padZ;
                    if (state == State.Playing && dx * dx + dz * dz < EvacRing * EvacRing) Board();
                    break;
                }
                case EvacPhase.Aboard:
                {
                    // Runs to the door and climbs in; it lifts off up the road;
                    // the screen goes to black with where it's going.
                    var door = new Vector2(e.padX - 1.6f, e.padZ + 0.4f);
                    if (e.t < 0.9f)
                    {
                        var at = Vector2.Lerp(e.walkFrom, door, e.t / 0.9f);
                        p.x = at.x; p.z = at.y;
                        p.yaw = Mathf.Atan2(door.x - e.walkFrom.x, door.y - e.walkFrom.y);
                        p.moving = 1;
                    }
                    else if (surv.root.activeSelf)
                    {
                        surv.root.SetActive(false);
                        p.moving = 0;
                        vfx.Dust(door.x, door.y, 0.6f, 4, null, 1);
                    }
                    if (e.t > 1.1f)
                    {
                        float lift = e.t - 1.1f;
                        e.y += (3 + lift * 6) * dt;
                        e.z -= lift * 8 * dt;
                    }
                    cutAt = new Vector3(e.x, Mathf.Min(e.y, 6), e.z);
                    hud.Curtain(Mathf.Clamp01((e.t - 1.9f) / 0.6f), next.name, next.line, Save.Data.seenEvac);
                    if (e.t > 2.6f) Arrive();
                    break;
                }
                case EvacPhase.Arriving:
                {
                    // Down in the new place, the screen back, and out.
                    var here = Place;
                    hud.Curtain(1 - Mathf.Clamp01((e.t - 0.4f) / 0.7f), here.name, here.line, Save.Data.seenEvac);
                    float k = 1 - Mathf.Exp(-2.4f * dt);
                    e.y += (Landed - e.y) * k;
                    if (e.t > 2.4f) Release();
                    break;
                }
                case EvacPhase.Leaving:
                    // Off up the road and away.
                    e.y += (2 + e.t * 3) * dt;
                    e.z -= (4 + e.t * 6) * dt;
                    e.x += 2 * dt;
                    if (e.t > 4) EndEvac();
                    break;
            }
            if (evac == null) return;
            var tr = e.go.transform;
            tr.localPosition = new Vector3(e.x, e.y, e.z);
            float pitch = e.phase == EvacPhase.Leaving || e.phase == EvacPhase.Aboard && e.t > 1.1f ? 0.22f : e.y > Landed + 0.3f ? 0.08f : 0;
            tr.localRotation = M.Yaw(Mathf.PI) * M.Euler(pitch, 0, Mathf.Sin(time * 0.9f) * (e.y > Landed + 0.3f ? 0.04f : 0));
            e.shadow.transform.localPosition = new Vector3(e.x, 0.04f, e.z);
            if (e.ring) e.ring.transform.localScale = Vector3.one * (1 + Mathf.Sin(time * 4) * 0.06f);
        }

        // Into the ring: the cut scene. Zombies stand about (they only go
        // for you in play), and the camera takes its own shot.
        void Board()
        {
            var e = evac;
            e.phase = EvacPhase.Aboard;
            e.t = 0;
            e.walkFrom = new Vector2(player.x, player.z);
            state = State.Evac;
            input.enabled = false;
            hud.CutScene();
            cutFrom = new Vector3(e.padX - 9, 4.2f, e.padZ + 6.5f);
            cutAt = new Vector3(e.padX, 1.2f, e.padZ);
            if (e.flare != null) e.flare.dead = true;
            if (e.ring) e.ring.gameObject.SetActive(false);
            Sfx.Rotor();
        }

        // At black: the next place, built from here, and the helicopter
        // coming down in it.
        void Arrive()
        {
            var e = evac;
            int n = run.place + 1;
            Relocate(n);
            int coins = Tunables.EvacCoins * n;
            run.bonus += coins;
            Save.Running(Record());
            e.padX = 0;
            e.padZ = player.z - 2.5f;
            e.x = e.padX;
            e.y = 9;
            e.z = e.padZ;
            e.phase = EvacPhase.Arriving;
            e.t = 0;
            cutFrom = new Vector3(e.padX - 8, 4.5f, e.padZ + 7);
            cutAt = new Vector3(e.padX, 1.4f, e.padZ);
            camPos = cutFrom.Value;
            camLook = cutAt;
            hud.Toast($"+{coins} coins for making it out");
        }

        // Out of the door and back in play; it leaves.
        void Release()
        {
            var e = evac;
            var p = player;
            p.x = e.padX - 2;
            p.z = e.padZ + 0.6f;
            p.furthest = Mathf.Min(p.furthest, p.z);
            p.moving = 0;
            p.yaw = Mathf.PI;
            surv.root.SetActive(true);
            StandUp();
            cutFrom = null;
            state = State.Playing;
            input.enabled = true;
            hud.CurtainOff();
            hud.ShowHud();
            var here = Place;
            hud.Banner(here.name, here.line, 2.6f);
            if (!Save.Data.seenEvac) { Save.Data.seenEvac = true; Save.Persist(); }
            e.phase = EvacPhase.Leaving;
            e.t = 0;
        }

        // A tap in the cut scene, once one's been seen through: straight to
        // the new place.
        public void SkipEvac()
        {
            var e = evac;
            if (e == null || !Save.Data.seenEvac) return;
            if (e.phase == EvacPhase.Aboard)
            {
                surv.root.SetActive(false);
                Arrive();
            }
            if (e.phase == EvacPhase.Arriving)
            {
                e.y = Landed;
                Release();
            }
        }

        void EndEvac()
        {
            var e = evac;
            evac = null;
            cutFrom = null;
            if (e == null) return;
            if (e.flare != null) e.flare.dead = true;
            Destroy(e.go);
            if (e.shadow) Destroy(e.shadow.gameObject);
            if (e.ring) Destroy(e.ring.gameObject);
            hud.CurtainOff();
        }
    }
}
