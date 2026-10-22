// Every sound in the game, synthesised, so there are still no audio files: a
// port of graveway/src/audio.js. The web builds each sound live with Web
// Audio. Here each recipe is rendered once at start-up (Init) into an
// AudioClip, with the same noise, filters, oscillators and envelopes done by
// hand, and a sound plays on the next free voice of a small pool.
//
// Recipes with randomness in them (the noise, the groan's pitch, the clang
// and the ricochet) get a few rendered takes, picked at random, and a slight
// random pitch on top.

using System.Collections.Generic;
using UnityEngine;
using Math = System.Math;
using MathF = System.MathF;

namespace Graveway
{
    public static class Sfx
    {
        public const int SampleRate = 32000;
        const int Voices = 20;
        const float MasterGain = 0.55f;
        const int NoiseLength = SampleRate;   // the web's one-second noise buffer

        enum Wave { Sine, Square, Sawtooth, Triangle }

        // Per gun model: the crack's band-pass centre and length, the thump's
        // pitch, and how loud.
        static readonly (string model, float freq, float dur, float thump, float vol)[] Shots =
        {
            ("pistol", 1700, 0.13f, 140, 0.55f),
            ("launcher", 500, 0.18f, 70, 0.5f),
            ("smg", 2300, 0.07f, 170, 0.4f),
            ("shotgun", 900, 0.32f, 75, 0.8f),
            ("rifle", 1400, 0.16f, 110, 0.55f),
            ("minigun", 2600, 0.06f, 190, 0.32f),
        };

        static readonly float[] noise = MakeNoise();
        static readonly Dictionary<string, Recipe> recipes = MakeRecipes();
        static readonly Dictionary<string, string> shotKeys = MakeShotKeys();

        static Dictionary<string, AudioClip[]> clips;
        static AudioSource[] voices;
        static long[] voiceStarted;
        static long played;
        static readonly Dictionary<string, float> last = new Dictionary<string, float>();
        static bool enabled = true;

        // With domain reload off, statics outlive a Play press but the voices
        // don't (they go with the scene), and the clock starts again from 0.
        // The clips are kept unless Unity has destroyed them.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            voices = null;
            loopVoice = null;
            looping = null;
            last.Clear();
        }

        // Renders every clip and makes the voices, on a child "Sfx" of parent.
        public static void Init(Transform parent)
        {
            if (voices != null && voices[0] != null) return;
            if (clips == null || clips["click"][0] == null) RenderAll();
            var go = new GameObject("Sfx");
            go.transform.SetParent(parent, false);
            // Nothing is heard without a listener. Every sound is 2D, so
            // where it sits doesn't matter.
            if (Object.FindAnyObjectByType<AudioListener>() == null) go.AddComponent<AudioListener>();
            voices = new AudioSource[Voices];
            voiceStarted = new long[Voices];
            for (int i = 0; i < Voices; i++)
            {
                var s = go.AddComponent<AudioSource>();
                s.playOnAwake = false;
                s.loop = false;
                s.spatialBlend = 0;
                voices[i] = s;
            }
        }

        // setSoundEnabled(). Off also silences anything still playing, as the
        // web's master gain going to 0 does.
        public static bool Enabled
        {
            get => enabled;
            set
            {
                enabled = value;
                if (!value && voices != null)
                    foreach (var v in voices) if (v != null) v.Stop();
            }
        }

        // Left-right position of a sound from x on the road: 10 m to the side
        // is all the way over.
        public static float Pan(float x, float playerX) => Mathf.Clamp((x - playerX) / 10, -1, 1);

        // ── The sounds ───────────────────────────────────────────────────
        public static void Shot(string model)
        {
            if (!Throttle("shot", 0.035f)) return;
            Play(model != null && shotKeys.TryGetValue(model, out var key) ? key : "shot:pistol");
        }

        public static void Hit(float pan) { if (Throttle("hit", 0.04f)) Play("hit", pan); }
        public static void Die(float pan) { if (Throttle("die", 0.06f)) Play("die", pan); }
        public static void Groan(float pan, bool big) { if (Throttle("groan", 0.7f)) Play(big ? "groan:big" : "groan", pan); }
        public static void Coin() { if (Throttle("coin", 0.05f)) Play("coin"); }
        public static void Pickup() { if (Throttle("pickup", 0.1f)) Play("pickup"); }
        public static void Powerup() { if (Throttle("powerup", 0.15f)) Play("powerup"); }
        public static void Shock() { if (Throttle("shock", 0.2f)) Play("shock"); }
        public static void Toss() { if (Throttle("toss", 0.05f)) Play("toss"); }
        public static void Hurt() { if (Throttle("hurt", 0.12f)) Play("hurt"); }
        public static void Reload() { if (Throttle("reload", 0.2f)) Play("reload"); }
        public static void Swap() { if (Throttle("swap", 0.1f)) Play("swap"); }
        public static void Click() { if (Throttle("click", 0.05f)) Play("click"); }
        public static void Buy() { if (Throttle("buy", 0.1f)) Play("buy"); }
        public static void Denied() { if (Throttle("denied", 0.15f)) Play("denied"); }
        public static void Win() { if (Throttle("win", 1)) Play("win"); }
        public static void Lose() { if (Throttle("lose", 1)) Play("lose"); }
        public static void Crumble() { if (Throttle("crumble", 0.15f)) Play("crumble"); }
        public static void Boom() { if (Throttle("boom", 0.06f)) Play("boom"); }
        public static void BigBoom() { if (Throttle("bigBoom", 0.1f)) Play("bigBoom"); }
        public static void Thunk() { if (Throttle("thunk", 0.08f)) Play("thunk"); }
        public static void Ignite() { if (Throttle("ignite", 0.3f)) Play("ignite"); }
        public static void Clang() { if (Throttle("clang", 0.08f)) Play("clang"); }
        public static void Ricochet(float pan) { if (Throttle("ricochet", 0.05f)) Play("ricochet", pan); }
        public static void Chip(float pan) { if (Throttle("chip", 0.05f)) Play("chip", pan); }
        public static void Rumble() { if (Throttle("rumble", 1)) Play("rumble"); }
        public static void Collapse() { if (Throttle("collapse", 1)) Play("collapse"); }
        public static void Milestone() { if (Throttle("milestone", 0.5f)) Play("milestone"); }
        public static void Slam() { if (Throttle("slam", 0.3f)) Play("slam"); }
        public static void Jet() { if (Throttle("jet", 1)) Play("jet"); }
        public static void Rotor() { if (Throttle("rotor", 1)) Play("rotor"); }

        // One looping sound (the helicopter overhead), on a voice of its own
        // so a busy fight can't take it. Cheap to call every frame.
        static AudioSource loopVoice;
        static string looping;

        public static void Loop(string key, bool on)
        {
            on &= enabled && voices != null && voices[0] != null;
            if (!on)
            {
                if (looping != null && loopVoice) loopVoice.Stop();
                looping = null;
                return;
            }
            if (looping == key) return;
            if (!loopVoice)
            {
                loopVoice = voices[0].gameObject.AddComponent<AudioSource>();
                loopVoice.playOnAwake = false;
                loopVoice.spatialBlend = 0;
                loopVoice.loop = true;
            }
            loopVoice.clip = clips[key][0];
            loopVoice.volume = 0.8f;
            loopVoice.Play();
            looping = key;
        }

        // ── Playing ──────────────────────────────────────────────────────
        // audio.js's throttle(): drops a sound if the same one played less
        // than `gap` seconds ago, so a minigun or a pile of coins doesn't
        // stack into a wall of noise.
        static bool Throttle(string name, float gap)
        {
            if (voices == null || !enabled) return false;
            float t = Time.unscaledTime;
            if (last.TryGetValue(name, out float l) && t - l < gap) return false;
            last[name] = t;
            return true;
        }

        static void Play(string key, float pan = 0)
        {
            var set = clips[key];
            var v = NextVoice();
            v.clip = set[Random.Range(0, set.Length)];
            float j = recipes[key].jitter;
            v.pitch = j > 0 ? Random.Range(1 - j, 1 + j) : 1;
            // As audio.js's out(): a sound given a pan goes through an
            // equal-power panner (71% each side in the middle), one without
            // goes straight out at full level. Unity pans every mono clip the
            // equal-power way, so here everything sits 3 dB under the web
            // and a panned sound takes the panner's 3 dB on top. (The spare
            // 3 dB is headroom: the web's compressor squeezed the whole mix,
            // here it only squeezes each clip.)
            if (float.IsNaN(pan)) pan = 0;
            pan = Mathf.Clamp(pan, -1, 1);
            v.panStereo = pan;
            v.volume = pan != 0 ? 0.7071f : 1;
            v.Play();
        }

        // A free voice, or else the one that started longest ago.
        static AudioSource NextVoice()
        {
            int pick = 0;
            for (int i = 0; i < voices.Length; i++)
            {
                if (!voices[i].isPlaying) { pick = i; break; }
                if (voiceStarted[i] < voiceStarted[pick]) pick = i;
            }
            voiceStarted[pick] = ++played;
            return voices[pick];
        }

        static void RenderAll()
        {
            clips = new Dictionary<string, AudioClip[]>();
            foreach (var kv in recipes)
            {
                var set = new AudioClip[kv.Value.variants];
                for (int v = 0; v < set.Length; v++)
                {
                    var data = Render(kv.Key, v);
                    set[v] = AudioClip.Create(kv.Key, data.Length, 1, SampleRate, false);
                    set[v].SetData(data, 0);
                }
                clips[kv.Key] = set;
            }
        }

        // ── Recipes ──────────────────────────────────────────────────────
        // Every recipe's name: the sfx names in audio.js, with "shot:<model>"
        // for each gun and "groan:big" for the big zombies' groan.
        public static IEnumerable<string> Recipes => recipes.Keys;

        // How many takes a recipe has.
        public static int Variants(string name) => Find(name).variants;

        // One take of a recipe as mono samples at SampleRate, master bus
        // included. Takes run from 0 to Variants(name) - 1 and always come
        // out the same.
        public static float[] Render(string name, int variant = 0)
        {
            var r = Find(name);
            if (variant < 0 || variant >= r.variants) throw new System.ArgumentOutOfRangeException(nameof(variant));
            var m = new Mix((Seed(name) + variant * 7919) & 0x7fffffff);
            r.build(m, variant);
            return m.Finish();
        }

        static Recipe Find(string name)
        {
            if (name == null || !recipes.TryGetValue(name, out var r)) throw new System.ArgumentException("No sound called " + name, nameof(name));
            return r;
        }

        sealed class Recipe
        {
            public int variants = 1;
            public float jitter;
            public System.Action<Mix, int> build;
        }

        // The recipes, as audio.js's sfx object.
        static Dictionary<string, Recipe> MakeRecipes()
        {
            var r = new Dictionary<string, Recipe>();
            // Fixed: nothing random. Noisy: random noise, so three takes and a
            // slight random pitch. Spread: a pitch picked at random from a
            // range, so takes spread across it and the random pitch fills in.
            void Fixed(string name, System.Action<Mix> b) => r[name] = new Recipe { build = (m, v) => b(m) };
            void Noisy(string name, System.Action<Mix> b) => r[name] = new Recipe { variants = 3, jitter = 0.03f, build = (m, v) => b(m) };
            void Spread(string name, int n, System.Action<Mix, float> b) =>
                r[name] = new Recipe { variants = n, jitter = 0.03f, build = (m, v) => b(m, (v + 0.5f) / n) };

            foreach (var s in Shots)
                Noisy("shot:" + s.model, m =>
                {
                    m.Noise(dur: s.dur, freq: s.freq, q: 0.7f, vol: s.vol, sweep: 0.35f);
                    m.Tone(freq: s.thump, to: 40, dur: s.dur * 0.9f, type: Wave.Triangle, vol: s.vol * 0.7f);
                });
            Noisy("hit", m => m.Noise(dur: 0.08f, freq: 500, q: 1.2f, lowpass: true, vol: 0.3f));
            Noisy("die", m =>
            {
                m.Tone(freq: 160, to: 55, dur: 0.35f, type: Wave.Sawtooth, vol: 0.12f);
                m.Noise(dur: 0.25f, freq: 300, lowpass: true, vol: 0.25f);
            });
            // The web picks the base pitch from 85-115 Hz (55-85 for big ones).
            Spread("groan", 4, (m, k) => m.Groan(false, 85 + k * 30));
            Spread("groan:big", 4, (m, k) => m.Groan(true, 55 + k * 30));
            Fixed("coin", m =>
            {
                m.Tone(freq: 1320, dur: 0.08f, type: Wave.Square, vol: 0.06f);
                m.Tone(freq: 1760, dur: 0.12f, type: Wave.Square, vol: 0.06f, delay: 0.05f);
            });
            Fixed("pickup", m =>
            {
                float[] f = { 523, 659, 784, 1047 };
                for (int i = 0; i < f.Length; i++) m.Tone(freq: f[i], dur: 0.14f, type: Wave.Triangle, vol: 0.18f, delay: i * 0.06f);
            });
            Fixed("powerup", m =>
            {
                m.Tone(freq: 392, to: 784, dur: 0.22f, type: Wave.Sawtooth, vol: 0.07f);
                float[] f = { 784, 988, 1175, 1568 };
                for (int i = 0; i < f.Length; i++) m.Tone(freq: f[i], dur: 0.16f, type: Wave.Square, vol: 0.05f, delay: 0.08f + i * 0.05f);
            });
            Noisy("shock", m =>
            {
                m.Noise(dur: 0.7f, freq: 320, lowpass: true, vol: 0.9f, sweep: 0.2f);
                m.Tone(freq: 180, to: 40, dur: 0.5f, type: Wave.Sine, vol: 0.6f);
            });
            Noisy("toss", m => m.Noise(dur: 0.14f, freq: 900, q: 1.2f, vol: 0.2f, sweep: 2.5f));
            Noisy("hurt", m =>
            {
                m.Noise(dur: 0.2f, freq: 220, lowpass: true, vol: 0.6f);
                m.Tone(freq: 120, to: 60, dur: 0.2f, type: Wave.Square, vol: 0.12f);
            });
            // The web plays the second click from a 160 ms setTimeout.
            Noisy("reload", m =>
            {
                m.Noise(dur: 0.04f, freq: 3000, q: 4, vol: 0.25f);
                m.Noise(dur: 0.05f, freq: 2200, q: 4, vol: 0.3f, delay: 0.16f);
            });
            Noisy("swap", m => m.Noise(dur: 0.06f, freq: 2600, q: 3, vol: 0.25f));
            Fixed("click", m => m.Tone(freq: 880, dur: 0.05f, type: Wave.Triangle, vol: 0.12f));
            Fixed("buy", m =>
            {
                m.Tone(freq: 660, dur: 0.1f, type: Wave.Square, vol: 0.08f);
                m.Tone(freq: 990, dur: 0.18f, type: Wave.Square, vol: 0.08f, delay: 0.08f);
            });
            Fixed("denied", m => m.Tone(freq: 180, dur: 0.18f, type: Wave.Square, vol: 0.1f));
            Fixed("win", m =>
            {
                float[] f = { 392, 523, 659, 784 };
                for (int i = 0; i < f.Length; i++) m.Tone(freq: f[i], dur: 0.5f, type: Wave.Triangle, vol: 0.18f, delay: i * 0.12f);
            });
            Fixed("lose", m =>
            {
                float[] f = { 330, 262, 196, 131 };
                for (int i = 0; i < f.Length; i++) m.Tone(freq: f[i], dur: 0.5f, type: Wave.Sawtooth, vol: 0.08f, delay: i * 0.18f);
            });
            Noisy("crumble", m =>
            {
                m.Noise(dur: 0.5f, freq: 400, lowpass: true, vol: 0.55f, sweep: 0.4f);
                m.Noise(dur: 0.25f, freq: 1800, q: 2, vol: 0.15f);
            });
            Noisy("boom", m =>
            {
                m.Noise(dur: 0.9f, freq: 700, lowpass: true, vol: 1.0f, sweep: 0.15f);
                m.Tone(freq: 90, to: 28, dur: 0.7f, type: Wave.Sine, vol: 0.7f);
            });
            Noisy("thunk", m =>
            {
                m.Tone(freq: 220, to: 90, dur: 0.12f, type: Wave.Triangle, vol: 0.3f);
                m.Noise(dur: 0.06f, freq: 900, q: 1, vol: 0.2f);
            });
            Noisy("ignite", m => m.Noise(dur: 0.6f, freq: 900, q: 0.5f, vol: 0.35f, attack: 0.08f, sweep: 0.6f));
            // The web picks the clang's pitch from 420-540 Hz, the
            // ricochet's from 2200-3600 Hz.
            Spread("clang", 4, (m, k) =>
            {
                m.Tone(freq: 420 + k * 120, to: 300, dur: 0.35f, type: Wave.Square, vol: 0.08f);
                m.Noise(dur: 0.12f, freq: 2400, q: 3, vol: 0.18f);
            });
            Spread("ricochet", 8, (m, k) => m.Tone(freq: 2200 + k * 1400, to: 900, dur: 0.12f, type: Wave.Triangle, vol: 0.05f));
            Noisy("chip", m => m.Noise(dur: 0.05f, freq: 1800, q: 2, vol: 0.12f));
            Noisy("rumble", m =>
            {
                m.Noise(dur: 1.6f, freq: 120, lowpass: true, vol: 0.7f, attack: 0.4f);
                m.Tone(freq: 45, to: 32, dur: 1.5f, type: Wave.Sine, vol: 0.4f, attack: 0.3f);
            });
            Noisy("collapse", m =>
            {
                m.Noise(dur: 2.8f, freq: 260, lowpass: true, vol: 1.0f, sweep: 0.3f, attack: 0.02f);
                m.Noise(dur: 1.4f, freq: 1400, q: 0.8f, vol: 0.35f, sweep: 0.4f);
                m.Tone(freq: 60, to: 22, dur: 1.6f, type: Wave.Sine, vol: 0.7f);
            });
            Noisy("bigBoom", m =>
            {
                m.Noise(dur: 1.8f, freq: 500, lowpass: true, vol: 1.0f, sweep: 0.12f);
                m.Noise(dur: 0.5f, freq: 2000, q: 0.6f, vol: 0.4f, sweep: 0.3f);
                m.Tone(freq: 70, to: 20, dur: 1.2f, type: Wave.Sine, vol: 0.8f);
            });
            Fixed("milestone", m =>
            {
                float[] f = { 523, 784 };
                for (int i = 0; i < f.Length; i++) m.Tone(freq: f[i], dur: 0.3f, type: Wave.Triangle, vol: 0.16f, delay: i * 0.1f);
            });
            Noisy("slam", m =>
            {
                m.Noise(dur: 0.45f, freq: 160, lowpass: true, vol: 0.9f);
                m.Tone(freq: 70, to: 30, dur: 0.4f, type: Wave.Sine, vol: 0.5f);
            });

            // The Black Market's (not in audio.js): a jet coming in low and
            // roaring overhead, and a helicopter's rotor beating closer, then
            // looping while it's here. The noise buffer runs dry after about
            // a second, so long sounds are layered from short ones.
            Fixed("jet", m =>
            {
                m.Noise(dur: 1.0f, freq: 450, lowpass: true, vol: 0.12f, attack: 0.6f);
                m.Noise(dur: 1.0f, freq: 800, lowpass: true, vol: 0.3f, attack: 0.3f, delay: 0.8f);
                m.Noise(dur: 1.1f, freq: 1600, q: 0.6f, vol: 0.6f, attack: 0.2f, sweep: 0.3f, delay: 1.6f);
                m.Tone(freq: 340, to: 150, dur: 2.6f, type: Wave.Sawtooth, vol: 0.035f, delay: 0.4f, attack: 1.2f);
            });
            Fixed("rotor", m =>
            {
                for (int i = 0; i < 44; i++) m.Noise(dur: 0.05f, freq: 140, lowpass: true, vol: 0.05f + 0.4f * i / 43f, delay: i / 12f);
            });
            Fixed("rotor:loop", m =>
            {
                for (int i = 0; i < 12; i++) m.Noise(dur: 0.05f, freq: 140, lowpass: true, vol: 0.4f, delay: i / 12f);
                m.Until(1);     // a whole second, so the beat keeps time round the loop
            });
            return r;
        }

        static Dictionary<string, string> MakeShotKeys()
        {
            var keys = new Dictionary<string, string>();
            foreach (var s in Shots) keys[s.model] = "shot:" + s.model;
            return keys;
        }

        static float[] MakeNoise()
        {
            var rnd = new System.Random(1);
            var d = new float[NoiseLength];
            for (int i = 0; i < d.Length; i++) d[i] = (float)(rnd.NextDouble() * 2 - 1);
            return d;
        }

        static int Seed(string s)
        {
            int h = 17;
            foreach (char c in s) h = h * 31 + c;
            return h;
        }

        static int Samples(float seconds) => (int)(seconds * SampleRate + 0.5f);

        // ── Synthesis ────────────────────────────────────────────────────
        // A recipe being rendered: audio.js's noise() and tone() added into
        // one buffer, then the master bus.
        sealed class Mix
        {
            readonly System.Random rnd;
            float[] buf = new float[SampleRate / 2];
            int len;

            public Mix(int seed) { rnd = new System.Random(seed); }

            float Rand() => (float)rnd.NextDouble();

            void Reserve(int end)
            {
                if (end > buf.Length) System.Array.Resize(ref buf, Math.Max(end, buf.Length * 2));
                if (end > len) len = end;
            }

            // Silence to `seconds`, for a loop that has to be that long.
            public void Until(float seconds) => Reserve(Samples(seconds));

            // White noise through a band-pass (or low-pass) filter whose
            // frequency can sweep exponentially to freq * sweep.
            public void Noise(float dur, float freq, float q = 1, bool lowpass = false, float vol = 0.5f,
                              float attack = 0.002f, float sweep = 0, float delay = 0)
            {
                // As the web's buffer source: the one-second noise buffer at
                // 0.8-1.2x speed from up to half a second in, not looping, so
                // a long noise runs dry early and its tail goes quiet.
                double pos = Rand() * 0.5 * SampleRate, rate = 0.8 + Rand() * 0.4;
                int dry = (int)Math.Ceiling((NoiseLength - 1 - pos) / rate);
                // It stops 20 ms after dur, or once the filter has rung out
                // after the noise ran dry.
                int n = Math.Min(Samples(dur + 0.02f), dry + Samples(0.05f));
                int start = Samples(delay), glide = Samples(dur);
                Reserve(start + n);
                var env = new Envelope(vol, attack, dur);
                var filter = new Biquad(lowpass, q);
                filter.Tune(freq);
                double f = freq, step = sweep != 0 ? Math.Pow(Math.Max(40, freq * sweep) / freq, 1.0 / glide) : 1;
                for (int i = 0; i < n; i++)
                {
                    float x = 0;
                    if (pos < NoiseLength - 1)
                    {
                        int k = (int)pos;
                        x = noise[k] + (noise[k + 1] - noise[k]) * (float)(pos - k);
                        pos += rate;
                    }
                    if (sweep != 0 && i <= glide) { filter.Tune((float)f); f *= step; }
                    buf[start + i] += filter.Run(x) * env.Next();
                }
            }

            // An oscillator whose pitch can glide exponentially to `to`.
            public void Tone(float freq, float dur, Wave type = Wave.Sine, float vol = 0.3f, float to = 0,
                             float delay = 0, float attack = 0.004f)
            {
                int start = Samples(delay), n = Samples(dur + 0.02f), glide = Samples(dur);
                Reserve(start + n);
                var env = new Envelope(vol, attack, dur);
                double f = freq, step = to != 0 ? Math.Pow(to / freq, 1.0 / glide) : 1;
                float phase = 0;
                for (int i = 0; i < n; i++)
                {
                    float dt = (float)(f / SampleRate);
                    buf[start + i] += Osc(type, phase, dt) * env.Next();
                    phase += dt;
                    if (phase >= 1) phase -= 1;
                    if (i < glide) f *= step;
                }
            }

            // The zombie groan, which audio.js wires up by hand: a sawtooth
            // sliding down to 70% over 0.9 s with a 7-11 Hz wobble of 6%,
            // through a narrow band-pass, swelling in over 0.15 s and gone by 1 s.
            public void Groan(bool big, float f)
            {
                int n = Samples(1.05f), slide = Samples(0.9f);
                Reserve(n);
                float lfoStep = (7 + Rand() * 4) / SampleRate, depth = f * 0.06f;
                var env = new Envelope(big ? 0.22f : 0.12f, 0.15f, 1.0f);
                var filter = new Biquad(false, 3);
                filter.Tune(big ? 380 : 650);
                float phase = 0, lfo = 0;
                for (int i = 0; i < n; i++)
                {
                    float hz = f * (1 - 0.3f * Math.Min(i, slide) / slide) + Sine(lfo) * depth;
                    float dt = hz / SampleRate;
                    buf[i] += filter.Run(Osc(Wave.Sawtooth, phase, dt)) * env.Next();
                    phase += dt;
                    if (phase >= 1) phase -= 1;
                    lfo += lfoStep;
                    if (lfo >= 1) lfo -= 1;
                }
            }

            // The master bus: gain 0.55 into the compressor.
            public float[] Finish()
            {
                var o = new float[len];
                for (int i = 0; i < len; i++) o[i] = buf[i] * MasterGain;
                Compressor.Run(o);
                return o;
            }
        }

        // audio.js's gain envelope: from 0.0001, an exponential ramp up to
        // vol over `attack`, another down to 0.0001 at `dur`, then held
        // (Web Audio's exponentialRampToValueAtTime, sample by sample).
        struct Envelope
        {
            const double Floor = 0.0001;
            double g, up, down;
            int i, a, d;

            public Envelope(float vol, float attack, float dur)
            {
                a = Math.Max(1, Samples(attack));
                d = Math.Max(a + 1, Samples(dur));
                g = Floor;
                up = Math.Pow(vol / Floor, 1.0 / a);
                down = Math.Pow(Floor / vol, 1.0 / (d - a));
                i = 0;
            }

            public float Next()
            {
                float v = (float)g;
                if (i < a) g *= up;
                else if (i < d) g *= down;
                i++;
                return v;
            }
        }

        // Web Audio's BiquadFilterNode, to the Web Audio spec's formulas: the
        // band-pass takes Q as a plain ratio, the low-pass takes it in dB. In
        // doubles, as browsers run it: the low low-passes need the precision.
        struct Biquad
        {
            readonly bool lowpass;
            readonly double halfOverQ;
            double b0, b1, b2, a1, a2, x1, x2, y1, y2;

            public Biquad(bool lowpass, float q)
            {
                this.lowpass = lowpass;
                halfOverQ = 0.5 / (lowpass ? Math.Pow(10, q / 20.0) : q);
                b0 = b1 = b2 = a1 = a2 = x1 = x2 = y1 = y2 = 0;
            }

            // Sweeps retune every sample, so below 1 radian (5 kHz here, past
            // every sweep) sin and 1 - cos come from their series: as exact,
            // and much quicker than calling Math.Sin and Math.Cos.
            public void Tune(float freq)
            {
                double w = 2 * Math.PI * Math.Min(freq, SampleRate / 2.0) / SampleRate, sin, oneMinusCos;
                if (w < 1)
                {
                    double w2 = w * w;
                    sin = w * (1 - w2 * (1.0 / 6) * (1 - w2 * (1.0 / 20) * (1 - w2 * (1.0 / 42) * (1 - w2 * (1.0 / 72)))));
                    oneMinusCos = w2 * 0.5 * (1 - w2 * (1.0 / 12) * (1 - w2 * (1.0 / 30) * (1 - w2 * (1.0 / 56) * (1 - w2 * (1.0 / 90)))));
                }
                else
                {
                    sin = Math.Sin(w);
                    oneMinusCos = 1 - Math.Cos(w);
                }
                double alpha = sin * halfOverQ, norm = 1 / (1 + alpha);   // 1 / a0
                if (lowpass) { b1 = oneMinusCos * norm; b0 = b2 = b1 * 0.5; }
                else { b0 = alpha * norm; b1 = 0; b2 = -b0; }
                a1 = -2 * (1 - oneMinusCos) * norm;
                a2 = (1 - alpha) * norm;
            }

            public float Run(float x)
            {
                double y = b0 * x + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
                x2 = x1; x1 = x;
                y2 = y1; y1 = y;
                return (float)y;
            }
        }

        // One sample of a wave at phase p (0-1), dt cycles per sample. Shaped
        // like Web Audio's waves (sine and triangle start at 0 going up), and
        // band-limited like them: the jumps and corners are smoothed over
        // two samples (polyBLEP and polyBLAMP) so high notes don't alias.
        static float Osc(Wave type, float p, float dt)
        {
            switch (type)
            {
                case Wave.Square:
                    return (p < 0.5f ? 1 : -1) + Blep(p, dt) - Blep(Frac(p + 0.5f), dt);
                case Wave.Sawtooth:
                {
                    float q = Frac(p + 0.5f);
                    return 2 * q - 1 - Blep(q, dt);
                }
                case Wave.Triangle:
                {
                    float q = Frac(p + 0.25f);
                    return 1 - 4 * Math.Abs(q - 0.5f) + 8 * dt * (Blamp(q, dt) - Blamp(Frac(q + 0.5f), dt));
                }
                default:
                    return Sine(p);
            }
        }

        // sin(2 pi p) for p in 0-1: folded into a quarter wave, then a series
        // good to a few millionths. Several times quicker than MathF.Sin.
        static float Sine(float p)
        {
            float x = 2 * MathF.PI * (p < 0.25f ? p : p < 0.75f ? 0.5f - p : p - 1), x2 = x * x;
            return x * (1 - x2 * (1f / 6) * (1 - x2 * (1f / 20) * (1 - x2 * (1f / 42) * (1 - x2 * (1f / 72)))));
        }

        static float Frac(float x) => x >= 1 ? x - 1 : x;

        // Correction for a jump of +2 at phase 0.
        static float Blep(float t, float dt)
        {
            if (t < dt) { t /= dt; return t + t - t * t - 1; }
            if (t > 1 - dt) { t = (t - 1) / dt; return t * t + t + t + 1; }
            return 0;
        }

        // Correction for a corner at phase 0 where the slope rises by 1 per sample.
        static float Blamp(float t, float dt)
        {
            if (t < dt) { t = 1 - t / dt; return t * t * t / 6; }
            if (t > 1 - dt) { t = 1 + (t - 1) / dt; return t * t * t / 6; }
            return 0;
        }

        // The web's DynamicsCompressorNode (threshold -16 dB, ratio 6, and
        // the defaults: 30 dB knee, 3 ms attack, 250 ms release) with the
        // browser's curve and automatic make-up gain, as Chromium's
        // DynamicsCompressorKernel has them. The web compresses the whole
        // mix; this runs on each clip alone, setting the gain every 32
        // samples from the loudest sample up to 6 ms ahead (the browser's
        // pre-delay). A soft ceiling after it keeps every peak under 0.9.
        static class Compressor
        {
            const float ThresholdDb = -16, KneeDb = 30, Ratio = 6;
            const int Block = 32, Ahead = 6;   // blocks: 6 ms at 32 kHz

            static readonly float threshold = DbToLin(ThresholdDb);
            static readonly float kneeEndDb = ThresholdDb + KneeDb, kneeEnd = DbToLin(kneeEndDb);
            static readonly float kneeK = KAtSlope(1 / Ratio);
            static readonly float kneeEndOutDb = LinToDb(KneeCurve(kneeEnd, kneeK));
            static readonly float makeup = MathF.Pow(1 / Saturate(1), 0.6f);

            public static void Run(float[] x)
            {
                int blocks = (x.Length + Block - 1) / Block;
                var peak = new float[blocks];
                for (int i = 0; i < x.Length; i++)
                {
                    float a = Math.Abs(x[i]);
                    if (a > peak[i / Block]) peak[i / Block] = a;
                }
                float attack = 1 - MathF.Exp(-Block / (0.003f * SampleRate));
                float release = 1 - MathF.Exp(-Block / (0.25f * SampleRate));
                float gain = 1;
                for (int b = 0; b < blocks; b++)
                {
                    float p = 0;
                    for (int j = b; j < blocks && j <= b + Ahead; j++) p = Math.Max(p, peak[j]);
                    float want = p > threshold ? Saturate(p) / p : 1;
                    float g = gain * makeup;
                    gain += (want - gain) * (want < gain ? attack : release);
                    // Glide to the new gain across the block.
                    float step = (gain * makeup - g) / Block;
                    int s0 = b * Block, s1 = Math.Min(s0 + Block, x.Length);
                    for (int i = s0; i < s1; i++)
                    {
                        g += step;
                        x[i] = Ceiling(x[i] * g);
                    }
                }
            }

            // Straight up to 0.8, then curving off towards 0.9.
            static float Ceiling(float v)
            {
                float a = Math.Abs(v);
                if (a <= 0.8f) return v;
                float c = 0.8f + 0.1f * MathF.Tanh((a - 0.8f) / 0.1f);
                return v < 0 ? -c : c;
            }

            // Linear up to the threshold, then an exponential knee.
            static float KneeCurve(float x, float k) =>
                x < threshold ? x : threshold + (1 - MathF.Exp(-k * (x - threshold))) / k;

            // The knee, then the fixed ratio past it.
            static float Saturate(float x) =>
                x < kneeEnd ? KneeCurve(x, kneeK) : DbToLin(kneeEndOutDb + (LinToDb(x) - kneeEndDb) / Ratio);

            static float SlopeAt(float x, float k)
            {
                if (x < threshold) return 1;
                float x2 = x * 1.001f;
                float dx = LinToDb(x2) - LinToDb(x);
                return (LinToDb(KneeCurve(x2, k)) - LinToDb(KneeCurve(x, k))) / dx;
            }

            // The knee's sharpness that makes its slope at the knee's end 1/ratio.
            static float KAtSlope(float slope)
            {
                float x = DbToLin(ThresholdDb + KneeDb), lo = 0.1f, hi = 10000, k = 5;
                for (int i = 0; i < 15; i++)
                {
                    if (SlopeAt(x, k) < slope) hi = k;
                    else lo = k;
                    k = MathF.Sqrt(lo * hi);
                }
                return k;
            }

            static float DbToLin(float db) => MathF.Pow(10, db / 20);
            static float LinToDb(float x) => 20 * MathF.Log10(x);
        }
    }
}
