// Everything on screen that isn't the world: a port of graveway/src/ui.js
// and the page in index.html, on UI Toolkit, built in code and styled by
// Resources/UI/Graveway.uss. Split by screen: this file has the shared
// parts and the in-run HUD; Hud.Screens.cs the menu, survivor select,
// pause, results and settings; Hud.Armory.cs the Armory; Hud.Notes.cs the
// playtest notes.
//
// Only buttons can be touched; everything else ignores the pointer, so a
// touch anywhere else steers (see StickInput.overUi). In-run buttons act
// the moment a finger lands on them, so they work while the other thumb is
// steering (a phone sends no tap for a second finger).

using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Graveway
{
    public sealed partial class Hud : MonoBehaviour
    {
        public struct Result
        {
            public string character;
            public int distance, stage, kills, coins, bonus, wallet, bestEver, startAt;
            public float time;
            public bool best;
            public Score score;              // where it went on the scoreboard; null if it didn't
            public bool board;               // false with playtest tools on
        }

        Game game;
        UIDocument doc;
        VisualElement root;
        string current = "menu";             // which screen: menu, select, armory, hud, pause, result
        Dictionary<string, Texture2D> portraits = new Dictionary<string, Texture2D>();
        bool portraitsWanted = true;

        public const string FeedbackRepo = "enachealex/My-Games";

        // Numbers the same way on every phone: "1,234", whatever the locale.
        static string N(float v) => Mathf.Floor(v).ToString("N0", CultureInfo.InvariantCulture);

        public void Init(Game g)
        {
            game = g;
            // An asset (made by Graveway → Set up project), not one built here:
            // a PanelSettings made at runtime misses references a build needs,
            // and the UI comes up blank on a phone.
            var settings = Resources.Load<PanelSettings>("UI/GravewayPanel");
            if (settings == null) throw new InvalidOperationException("Graveway: Resources/UI/GravewayPanel.asset is missing. Run Graveway → Set up project.");
            var go = new GameObject("UI Document");
            go.SetActive(false);
            go.transform.SetParent(transform, false);
            doc = go.AddComponent<UIDocument>();
            doc.panelSettings = settings;
            go.SetActive(true);

            root = doc.rootVisualElement;
            root.pickingMode = PickingMode.Ignore;
            var sheet = Resources.Load<StyleSheet>("UI/Graveway");
            if (sheet != null) root.styleSheets.Add(sheet);
            root.AddToClassList("gw-root");
            BuildHud();
            BuildMenu();
            BuildSelect();
            BuildArmory();
            BuildBlackMarket();
            BuildScores();
            BuildOverlays();
            BuildModals();
            foreach (var l in root.Query<Label>(className: "card-title").ToList()) Fit(l.parent, l);
            Fit(banner, bannerT);
            foreach (var l in root.Query<Label>(className: "a-head-title").ToList()) Fit(l, l);
            Show("menu");
            RenderMenu();
            // A release build looks for a newer one as soon as it loads.
            updates.Changed += () => { DrawUpdate(); DrawSettings(); };
            StartCoroutine(updates.Check(false));
            if (Save.Data.refunded > 0)
            {
                // Once: the old per-stat gun upgrades were paid back when parts came in.
                Banner("Guns take parts now", $"Your old upgrades were refunded: +{N(Save.Data.refunded)} coins", 4.5f);
                Save.Data.refunded = 0;
                Save.Persist();
            }
        }

        // ── Building blocks ──────────────────────────────────────────────
        static T Ignore<T>(T ve) where T : VisualElement { ve.pickingMode = PickingMode.Ignore; return ve; }

        static VisualElement El(string cls, VisualElement parent = null)
        {
            var v = Ignore(new VisualElement());
            foreach (var c in cls.Split(' ')) if (c.Length > 0) v.AddToClassList(c);
            parent?.Add(v);
            return v;
        }

        static Label Lbl(string cls, VisualElement parent, string text = "")
        {
            var l = Ignore(new Label(text));
            foreach (var c in cls.Split(' ')) if (c.Length > 0) l.AddToClassList(c);
            parent?.Add(l);
            return l;
        }

        // A button that acts on release, with the click sound.
        static Button Btn(string text, string cls, VisualElement parent, Action onClick)
        {
            var b = new Button(() => { Sfx.Click(); onClick(); }) { text = text };
            b.AddToClassList("btn");
            foreach (var c in cls.Split(' ')) if (c.Length > 0) b.AddToClassList(c);
            parent?.Add(b);
            return b;
        }

        // An in-run button: acts the moment a finger touches it.
        static VisualElement Press(string cls, VisualElement parent, Action onPress)
        {
            var v = new VisualElement();
            foreach (var c in cls.Split(' ')) if (c.Length > 0) v.AddToClassList(c);
            v.RegisterCallback<PointerDownEvent>(e => { onPress(); e.StopPropagation(); });
            parent?.Add(v);
            return v;
        }

        // Scrolls by dragging, as on a phone: no scroll bars.
        static ScrollView Scroller()
        {
            return new ScrollView(ScrollViewMode.Vertical)
            {
                verticalScrollerVisibility = ScrollerVisibility.Hidden,
                horizontalScrollerVisibility = ScrollerVisibility.Hidden,
            };
        }

        // Headings are sized for the web version's narrow Bebas Neue. In a
        // wider font they'd run off the screen, so they shrink to fit box.
        static readonly Dictionary<Label, float> fullSize = new Dictionary<Label, float>();

        static void Fit(VisualElement box, params Label[] labels)
        {
            void Refit()
            {
                float room = box.contentRect.width, text = 0;
                if (float.IsNaN(room) || room <= 0) return;
                foreach (var l in labels)
                {
                    float now = l.resolvedStyle.fontSize;
                    if (float.IsNaN(now) || now <= 0) return;
                    if (!fullSize.TryGetValue(l, out var full)) fullSize[l] = full = now;
                    text += l.MeasureTextSize(l.text, 0, VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined).x * full / now;
                    if (box != l) room -= l.resolvedStyle.marginLeft + l.resolvedStyle.marginRight;
                }
                if (text <= 0) return;
                float k = Mathf.Min(1, room * 0.98f / text);
                foreach (var l in labels)
                {
                    float size = Mathf.Floor(fullSize[l] * k);
                    if (Mathf.Abs(l.resolvedStyle.fontSize - size) >= 1) l.style.fontSize = size;
                }
            }
            box.RegisterCallback<GeometryChangedEvent>(_ => Refit());
            foreach (var l in labels) l.RegisterCallback<GeometryChangedEvent>(_ => Refit());
        }

        static void Shown(VisualElement v, bool on) => v.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
        static bool IsShown(VisualElement v) => v.resolvedStyle.display != DisplayStyle.None && v.style.display != DisplayStyle.None;

        // A coin and an amount, for prices and wallets.
        static VisualElement Coins(VisualElement parent, float amount, string cls = "row")
        {
            var r = El(cls, parent);
            r.Add(Icons.CoinIcon());
            Lbl("", r, N(amount));
            return r;
        }

        static void SetCoins(VisualElement row, float amount)
        {
            if (row.childCount > 1 && row[1] is Label l) l.text = N(amount);
        }

        // A label above a track with a fill, the value at the right.
        static VisualElement StatBar(VisualElement parent, string label, float v, string text)
        {
            var r = El("stat", parent);
            Lbl("stat-label", r, label);
            var track = El("stat-track", r);
            El("stat-fill", track).style.width = Length.Percent(Mathf.Clamp01(v) * 100);
            Lbl("stat-value", r, text);
            return r;
        }

        VisualElement Portrait(CharacterDef c, string size)
        {
            var p = El("portrait " + size);
            if (portraits.TryGetValue(c.id, out var tex) && tex) p.style.backgroundImage = tex;
            else Lbl("portrait-letter", p, c.name.Substring(0, 1));
            return p;
        }

        // ── Screens ──────────────────────────────────────────────────────
        VisualElement hud, menu, select, armory;

        void Show(string name)
        {
            current = name;
            Shown(menu, name == "menu");
            Shown(select, name == "select");
            Shown(armory, name == "armory");
            Shown(blackMarket, name == "blackmarket");
            Shown(scores, name == "scores");
            Shown(hud, name == "hud" || name == "pause");
            Shown(pause, name == "pause");
        }

        public void ShowHud()
        {
            hudCache.Clear();
            packKey = -1;
            Shown(result, false);
            Show("hud");
        }

        public void ShowPause()
        {
            pauseLevel.text = $"{N(game.Distance)} m · stage {game.Stage + 1}";
            Show("pause");
        }

        // Android back button and Escape: close whatever is on top, pause a
        // running run, and only leave the app from the main menu.
        public void Back()
        {
            if (IsShown(note)) { CloseNote(); return; }
            if (IsShown(notesModal)) { CloseNotes(); return; }
            if (IsShown(settingsModal)) { CloseSettings(); return; }
            if (IsShown(result)) { ToMenu(); return; }
            if (current == "armory") { CloseArmory(); return; }
            if (current == "blackmarket" || current == "scores") { ShowMenu(); return; }
            if (current == "select") { ToMenu(); return; }
            if (game.state == Game.State.Playing) { game.Pause(); return; }
            if (game.state == Game.State.Paused) { game.Resume(); return; }
            if (current == "menu" && !Application.isEditor) Application.Quit();
        }

        // ── Banner, toast, vignette, joystick ────────────────────────────
        VisualElement banner, toastWrap, vignette, stickBase, stickKnob;
        Label bannerT, bannerS, toast;
        float bannerUntil, toastT, hurtT;

        void BuildOverlays()
        {
            vignette = El("vignette", root);
            vignette.style.backgroundImage = Icons.Vignette;
            stickBase = El("stick-base", root);
            stickKnob = El("stick-knob", stickBase);
            Shown(stickBase, false);
            banner = El("banner", root);
            bannerT = Lbl("banner-t", banner);
            bannerS = Lbl("banner-s", banner);
            Shown(banner, false);
            toastWrap = El("toast", root);
            toast = Lbl("toast-pill", toastWrap);
            Shown(toastWrap, false);
        }

        public void Banner(string title, string sub, float seconds = 2.2f)
        {
            bannerT.text = title.ToUpperInvariant();
            bannerS.text = sub ?? "";
            Shown(bannerS, !string.IsNullOrEmpty(sub));
            Shown(banner, true);
            // Pops in: starts big and see-through, eases to normal (USS transition).
            banner.style.transitionDuration = new List<TimeValue> { new TimeValue(0) };
            banner.style.scale = new Scale(Vector3.one * 1.4f);
            banner.style.opacity = 0;
            banner.schedule.Execute(() =>
            {
                banner.style.transitionDuration = new List<TimeValue> { new TimeValue(0.35f) };
                banner.style.scale = new Scale(Vector3.one);
                banner.style.opacity = 1;
            }).StartingIn(16);
            bannerUntil = Time.unscaledTime + seconds;
        }

        void HideBanner() => Shown(banner, false);

        // A short line near the top: "+35 health", a power-up's name.
        public void Toast(string text, string kind = null, Color? color = null)
        {
            toastWrap.BringToFront();                 // over any modal, as on the web
            toast.text = text;
            toast.EnableInClassList("power", kind == "power");
            toast.style.color = color ?? (kind == "heal" ? Hex.C(0x7dff8a) : kind == "armor" ? Hex.C(0x5db4ff) : Hex.C(0xeef2ec));
            Shown(toastWrap, true);
            toastT = 1.3f;
        }

        public void FlashHurt() => hurtT = 0.45f;

        void Update()
        {
            if (portraitsWanted && Time.frameCount > 2)
            {
                portraitsWanted = false;
                RefreshPortraits();
            }
            if (IsShown(banner) && Time.unscaledTime > bannerUntil) HideBanner();
            if (toastT > 0)
            {
                toastT -= Time.unscaledDeltaTime;
                float k = 1.3f - toastT;
                toastWrap.style.opacity = k < 0.2f ? k / 0.2f : toastT < 0.33f ? toastT / 0.33f : 1;
                toastWrap.style.translate = new Translate(0, k < 0.2f ? 8 * (1 - k / 0.2f) : toastT < 0.33f ? -10 * (1 - toastT / 0.33f) : 0);
                if (toastT <= 0) Shown(toastWrap, false);
            }
            // Red flash when hurt; a slow pulse when health is low.
            hurtT = Mathf.Max(0, hurtT - Time.unscaledDeltaTime);
            var p = game.player;
            bool low = p != null && (game.state == Game.State.Playing || (game.state == Game.State.Dying && !p.reported)) && p.hp / p.maxHp < 0.3f;
            float pulse = low ? 0.25f + 0.35f * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 5.7f)) : 0;
            vignette.style.opacity = Mathf.Max(hurtT / 0.45f, pulse);
            SafeArea();
            Shortcuts();
        }

        // Keys the game doesn't handle itself (it only listens while playing).
        void Shortcuts()
        {
            if (root.panel?.focusController?.focusedElement is TextField) return;     // typing a note
            var st = game.state;
            // P, here only: handled in two places, each undid the other.
            if (GameKeys.Down(Key.P))
            {
                if (st == Game.State.Playing) game.Pause();
                else if (st == Game.State.Paused && current == "pause") game.Resume();
            }
            if (GameKeys.Down(Key.N) && (st == Game.State.Playing || st == Game.State.Paused) && !IsShown(note))
            {
                game.Pause();
                OpenNote();
            }
            if (current == "select" && !AnyModal())
            {
                if (GameKeys.Down(Key.LeftArrow) || GameKeys.Down(Key.A)) Cycle(-1);
                if (GameKeys.Down(Key.RightArrow) || GameKeys.Down(Key.D)) Cycle(1);
                if (GameKeys.Down(Key.Enter)) Play();
            }
            else if (current == "menu" && !AnyModal() && GameKeys.Down(Key.Enter)) Play();
        }

        bool AnyModal() => IsShown(note) || IsShown(notesModal) || IsShown(settingsModal) || IsShown(result);

        // Keep the screens clear of notches and the gesture bar. They're
        // absolutely placed, so the root's padding wouldn't reach them: each
        // is inset instead. (Modals are centred cards, well clear already.)
        Rect lastSafe;
        Vector2 lastSize;
        void SafeArea()
        {
            var s = Screen.safeArea;
            var size = root.layout.size;
            if ((s == lastSafe && size == lastSize) || root.panel == null || size.x <= 0) return;
            lastSafe = s;
            lastSize = size;
            // As shares of the screen, so it holds when the UI draws into a
            // texture of another size (the tests' pictures).
            float w = Mathf.Max(1, Screen.width), h = Mathf.Max(1, Screen.height);
            foreach (var screen in new[] { hud, menu, select, armory, blackMarket, scores })
            {
                screen.style.top = (h - s.yMax) / h * size.y;
                screen.style.bottom = s.yMin / h * size.y;
                screen.style.left = s.xMin / w * size.x;
                screen.style.right = (w - s.xMax) / w * size.x;
            }
        }

        // Back from the background: look for a newer build again, at most
        // every 15 minutes, as the web version does.
        void OnApplicationPause(bool paused)
        {
            if (!paused && Time.realtimeSinceStartup - updates.checkedAt > 15 * 60) StartCoroutine(updates.Check(false));
        }

        // The joystick appears under your thumb.
        public void UpdateStick(StickInput input)
        {
            if (root.panel == null) return;
            bool on = input.Active && game.state == Game.State.Playing;
            Shown(stickBase, on);
            if (!on) return;
            var o = RuntimePanelUtils.ScreenToPanel(root.panel, new Vector2(input.Origin.x, Screen.height - input.Origin.y));
            var edge = RuntimePanelUtils.ScreenToPanel(root.panel, new Vector2(input.Origin.x + input.Radius, Screen.height - input.Origin.y));
            float r = Mathf.Max(20, edge.x - o.x), scale = r / input.Radius, knob = r * 0.45f;
            stickBase.style.left = o.x - r;
            stickBase.style.top = o.y - r;
            stickBase.style.width = r * 2;
            stickBase.style.height = r * 2;
            stickKnob.style.width = knob * 2;
            stickKnob.style.height = knob * 2;
            stickKnob.style.left = r - knob;
            stickKnob.style.top = r - knob;
            stickKnob.style.translate = new Translate(input.Knob.x * scale, -input.Knob.y * scale);
        }

        // Is this screen point (pixels, origin bottom-left) on something
        // touchable? Then a touch there doesn't steer.
        public bool IsOverButton(Vector2 screen)
        {
            var panel = root?.panel;
            if (panel == null) return false;
            var pp = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(screen.x, Screen.height - screen.y));
            return panel.Pick(pp) != null;
        }

        // ── The in-run HUD ───────────────────────────────────────────────
        Label hDist, hStage, hKills, hDebug, hHpN, hArmorN, hWname, hAmmo, hMag;
        VisualElement hProg, hCoins, hBoss, hBossFill, hBuffs, hFace, hArmorRow, hHp, hArmor, hWbar, hWeapon, hReload;
        readonly Dictionary<string, int> hudCache = new Dictionary<string, int>();

        bool Changed(string key, int val)
        {
            if (hudCache.TryGetValue(key, out var old) && old == val) return false;
            hudCache[key] = val;
            return true;
        }

        static void Fill(VisualElement fill, float v) => fill.style.scale = new Scale(new Vector3(Mathf.Clamp01(v), 1, 1));

        void BuildHud()
        {
            hud = El("hud", root);
            var top = El("h-top", hud);
            hDist = Lbl("h-dist", top, "0 m");
            var stage = El("h-stage", top);
            hStage = Lbl("h-stage-label", stage, "STAGE 1");
            hProg = El("h-prog-fill", El("h-prog", stage));
            hCoins = Coins(top, 0, "h-coins");
            var pauseBtn = Press("icon-btn", top, () => { Sfx.Click(); game.Pause(); });
            pauseBtn.Add(Icons.Pause());
            hKills = Lbl("h-kills", hud);
            hDebug = Lbl("h-debug", hud);
            hBoss = El("h-boss", hud);
            Lbl("h-boss-label", hBoss, "BOSS");
            hBossFill = El("h-boss-fill", El("h-boss-bar", hBoss));
            Shown(hBoss, false);

            var bottom = El("h-bottom", hud);
            var vitals = El("vitals", bottom);
            hBuffs = El("h-buffs", vitals);
            hFace = El("h-face", vitals);
            hArmorRow = El("vbar", vitals);
            hArmorRow.Add(Icons.Shield());
            hArmor = El("vbar-fill armor", El("vbar-track thin", hArmorRow));
            hArmorN = Lbl("vbar-n", hArmorRow, "0");
            var hpRow = El("vbar", vitals);
            hpRow.Add(Icons.Heart());
            hHp = El("vbar-fill hp", El("vbar-track", hpRow));
            hHpN = Lbl("vbar-n", hpRow, "100");

            var weap = El("weap", bottom);
            hWbar = El("wbar", weap);
            hWeapon = Press("wcard-hud", weap, () => game.Reload());
            hWname = Lbl("wname", hWeapon, "PISTOL");
            var ammoRow = El("ammo-row", hWeapon);
            hAmmo = Lbl("ammo", ammoRow, "12");
            hMag = Lbl("mag", ammoRow, "/ 12");
            Lbl("tap", hWeapon, "TAP TO RELOAD");
            hReload = El("reload-fill", hWeapon);
            BuildPack();
        }

        public void UpdateHud()
        {
            var p = game.player;
            var r = game.run;
            if (p == null || r == null) return;
            UpdatePack();
            float d = game.Distance;
            if (Changed("dist", (int)d)) hDist.text = $"{N(d)} m";
            if (Changed("stage", game.Stage)) hStage.text = $"STAGE {game.Stage + 1}";
            float L = Tunables.Director.StageLength;
            if (Changed("prog", Mathf.RoundToInt(d % L / L * 100))) Fill(hProg, d % L / L);
            int coins = Save.Data.coins + Mathf.FloorToInt(r.coins) + r.bonus;
            if (Changed("coins", coins)) SetCoins(hCoins, coins);
            if (Changed("kills", r.kills)) hKills.text = $"{r.kills} kill{(r.kills == 1 ? "" : "s")}";
            if (Changed("face", Array.IndexOf(Tunables.Characters, game.Character) * 10 + (portraits.Count > 0 ? 1 : 0)))
            {
                hFace.Clear();
                hFace.Add(Portrait(game.Character, "sm"));
            }
            int hp = Mathf.CeilToInt(Mathf.Max(0, p.hp));
            if (Changed("hp", hp * 1000 + (int)p.maxHp)) { hHpN.text = hp.ToString(CultureInfo.InvariantCulture); Fill(hHp, Mathf.Max(0, p.hp) / p.maxHp); }
            int ar = Mathf.CeilToInt(p.armor);
            if (Changed("armor", ar))
            {
                Shown(hArmorRow, ar > 0);
                hArmorN.text = ar.ToString(CultureInfo.InvariantCulture);
                Fill(hArmor, p.armor / Tunables.MaxArmor);
            }

            // Weapon bar: one button per gun in hand, with a strip showing its ammo.
            var ws = p.weapons;
            if (Changed("wbar", ws.Count)) BuildWeaponBar();
            if (ws.Count > 1)
            {
                if (Changed("wsel", p.wi))
                    for (int i = 0; i < hWbar.childCount; i++) hWbar[i].EnableInClassList("on", i == p.wi);
                for (int i = 0; i < hWbar.childCount && i < ws.Count; i++)
                {
                    var w = ws[i];
                    int q = Mathf.RoundToInt((w.Reloading ? 0 : w.ammo / (float)w.stats.ammoCapacity) * 20);
                    if (Changed("wa" + i, q)) Fill(hWbar[i].Q(className: "wbtn-ammo"), q / 20f);
                }
                for (int i = 0; i < hWbar.childCount; i++) if (hWbar[i].Q<VectorIcon>() is VectorIcon gi) gi.Tint = i == p.wi ? Icons.Accent : Icons.Dim;
            }
            var cw = p.Weapon;
            if (Changed("weapon", p.wi * 100 + ws.Count)) hWname.text = cw.Name.ToUpperInvariant();
            bool free = game.BuffOn("rapid");
            int ammoKey = free ? -2 : cw.Reloading ? -1 : cw.ammo;
            if (Changed("ammo", ammoKey * 10000 + cw.stats.ammoCapacity))
            {
                hAmmo.text = free ? "∞" : cw.Reloading ? "···" : cw.ammo.ToString(CultureInfo.InvariantCulture);
                hMag.text = $"/ {cw.stats.ammoCapacity}";
                hWeapon.EnableInClassList("empty", !cw.Reloading && cw.ammo == 0 && !free);
            }
            float rp = cw.ReloadProgress;
            if (Changed("reload", rp < 0 ? -1 : Mathf.RoundToInt(rp * 50)))
            {
                hWeapon.EnableInClassList("reloading", rp >= 0);
                Fill(hReload, rp < 0 ? 0 : rp);
            }
            bool boss = r.boss != null && !r.boss.dead;
            if (Changed("boss", boss ? Mathf.RoundToInt(r.boss.hp / r.boss.maxHp * 100) : -1))
            {
                Shown(hBoss, boss);
                if (boss) Fill(hBossFill, r.boss.hp / r.boss.maxHp);
            }

            // Timed power-ups: a chip each, counting down.
            int buffMask = 0, bi = 0;
            foreach (var def in Tunables.PowerUps) { if (p.buffs.TryGetValue(def.id, out var t) && t > 0) buffMask |= 1 << bi; bi++; }
            if (Changed("buffs", buffMask)) BuildBuffs();
            for (int i = 0; i < hBuffs.childCount; i++)
            {
                var chip = hBuffs[i];
                string id = (string)chip.userData;
                float left = p.buffs.TryGetValue(id, out var t) ? t : 0, max = p.buffMax.TryGetValue(id, out var m) ? m : 1;
                if (Changed("bf" + id, Mathf.CeilToInt(left))) chip.Q<Label>().text = Mathf.CeilToInt(left).ToString(CultureInfo.InvariantCulture);
                if (Changed("bv" + id, Mathf.RoundToInt(left / max * 40))) Fill(chip.Q(className: "buff-fill"), left / max);
            }
            var s = Save.Data.settings;
            int dbgKey = (s.god || game.invincible ? 1 : 0) + (s.fps ? Mathf.RoundToInt(game.fps) * 10 + game.AliveCount() * 10000 : 0) * 2;
            if (Changed("dbg", dbgKey))
            {
                var parts = new List<string>();
                if (s.god || game.invincible) parts.Add("INVINCIBLE");
                if (s.fps) parts.Add($"{Mathf.RoundToInt(game.fps)} fps · {game.AliveCount()} up");
                hDebug.text = string.Join(" · ", parts);
                Shown(hDebug, parts.Count > 0);
            }
        }

        void BuildWeaponBar()
        {
            hWbar.Clear();
            var ws = game.player.weapons;
            if (ws.Count < 2) return;
            for (int i = 0; i < ws.Count; i++)
            {
                int k = i;
                var b = Press("wbtn", hWbar, () => game.SelectWeapon(k));
                b.Add(Icons.Gun(ws[i].Id, Icons.Dim));
                El("wbtn-ammo", b);
                Lbl("wbtn-n", b, (i + 1).ToString(CultureInfo.InvariantCulture));
            }
            foreach (var key in new List<string>(hudCache.Keys)) if (key.StartsWith("wa", StringComparison.Ordinal) || key == "wsel") hudCache.Remove(key);
        }

        void BuildBuffs()
        {
            hBuffs.Clear();
            var p = game.player;
            foreach (var def in Tunables.PowerUps)
            {
                if (!p.buffs.TryGetValue(def.id, out var t) || t <= 0) continue;
                var chip = El("buff", hBuffs);
                chip.userData = def.id;
                chip.style.borderTopColor = chip.style.borderBottomColor = chip.style.borderLeftColor = chip.style.borderRightColor = def.color;
                chip.style.color = def.color;
                var icon = El("buff-icon", chip);
                icon.style.backgroundImage = Tex.Power(def.id);
                Lbl("", chip, Mathf.CeilToInt(t).ToString(CultureInfo.InvariantCulture));
                El("buff-fill", chip).style.backgroundColor = def.color;
                hudCache.Remove("bf" + def.id);
                hudCache.Remove("bv" + def.id);
            }
        }

        // ── Portraits ────────────────────────────────────────────────────
        void RefreshPortraits()
        {
            if (!Studio.Available) return;
            foreach (var t in portraits.Values) if (t) Destroy(t);
            portraits = Studio.Portraits();
            RenderMenu();
            if (current == "select") RenderSelect();
            hudCache.Remove("face");
        }
    }
}
