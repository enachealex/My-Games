// The menu, survivor select, pause, the end-of-run card and settings: a
// port of those parts of graveway/src/ui.js and index.html.

using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

namespace Graveway
{
    public sealed partial class Hud
    {
        static readonly int[] StartAt = { 0, 200, 500, 1000 };

        // ── Menu ─────────────────────────────────────────────────────────
        VisualElement mCoins, mChar, mBadge;
        Label mBest, mRuns, mBadgeN;

        void BuildMenu()
        {
            menu = El("screen menu", root);
            var top = El("menu-top", menu);
            mCoins = Coins(top, 0, "wallet");
            var gear = new Button(() => { Sfx.Click(); OpenSettings(); });
            gear.AddToClassList("icon-btn");
            gear.Add(Icons.Gear());
            mBadge = El("badge", gear);
            mBadgeN = Lbl("", mBadge);
            top.Add(gear);
            var title = El("title", menu);
            var tr = El("title-row", title);
            Fit(title, Lbl("title-main", tr, "GRAVE"), Lbl("title-main red", tr, "WAY"));
            Lbl("title-sub", title, "Walk the dead road. Get as far as you can.");
            var bottom = El("menu-bottom", menu);
            var stats = El("menu-stats", bottom);
            mChar = El("m-card", stats);
            var best = El("m-card m-best", stats);
            Lbl("small-caps", best, "BEST RUN");
            mBest = Lbl("m-big", best, "—");
            mRuns = Lbl("m-small", best, "No runs yet");
            Btn("PLAY", "primary big", bottom, OpenSelect);
            Btn("Black Market", "", bottom, () => OpenMarket("menu"));
            Lbl("hint", bottom, "Drag anywhere to move. Aiming and shooting are automatic.");
        }

        // The main menu, with nothing over it.
        public void ShowMenu()
        {
            foreach (var m in new[] { result, note, notesModal, settingsModal }) Shown(m, false);
            HideBanner();
            RenderMenu();
            Show("menu");
        }

        void RenderMenu()
        {
            var s = Save.Data;
            SetCoins(mCoins, s.coins);
            mBest.text = s.best > 0 ? $"{N(s.best)} m" : "—";
            mRuns.text = s.runs > 0 ? $"{s.runs} run{(s.runs == 1 ? "" : "s")} · {N(s.kills)} kills" : "No runs yet";
            int unsent = Save.Notes.FindAll(n => n.sent == null).Count;
            mBadgeN.text = unsent.ToString(CultureInfo.InvariantCulture);
            Shown(mBadge, unsent > 0);
            var c = Tunables.Character(s.character);
            mChar.Clear();
            mChar.Add(Portrait(c, "sm"));
            var who = El("", mChar);
            who.style.marginLeft = 10;
            Lbl("small-caps", who, "SURVIVOR");
            Lbl("m-big", who, c.name.ToUpperInvariant());
        }

        void ToMenu()
        {
            HideBanner();
            Shown(result, false);
            game.ShowMenu();
            ShowMenu();
        }

        // ── Survivor select ──────────────────────────────────────────────
        int pick;
        ScrollView cCard;
        VisualElement cDots;
        Button cGo;
        Vector2? swipeFrom;

        void BuildSelect()
        {
            select = El("screen", root);
            select.style.backgroundColor = new Color(9 / 255f, 13 / 255f, 16 / 255f, 0.85f);
            var head = El("a-head", select);
            var back = new Button(() => { Sfx.Click(); ToMenu(); });
            back.AddToClassList("icon-btn");
            back.Add(Icons.Back());
            head.Add(back);
            Lbl("a-head-title", head, "SURVIVOR");
            var wrap = El("c-wrap", select);
            var prev = Btn("‹", "", wrap, () => Cycle(-1));
            prev.ClearClassList(); prev.AddToClassList("c-arrow");
            cCard = Scroller();
            cCard.AddToClassList("panel");
            cCard.AddToClassList("c-card");
            wrap.Add(cCard);
            var next = Btn("›", "", wrap, () => Cycle(1));
            next.ClearClassList(); next.AddToClassList("c-arrow");
            // Swipe the card left or right to change survivor.
            cCard.RegisterCallback<PointerDownEvent>(e => swipeFrom = e.position, TrickleDown.TrickleDown);
            cCard.RegisterCallback<PointerUpEvent>(e =>
            {
                if (swipeFrom == null) return;
                var d = (Vector2)e.position - swipeFrom.Value;
                swipeFrom = null;
                if (Mathf.Abs(d.x) > 40 && Mathf.Abs(d.x) > Mathf.Abs(d.y) * 1.5f) Cycle(d.x < 0 ? 1 : -1);
            }, TrickleDown.TrickleDown);
            var foot = El("c-foot", select);
            cDots = El("c-dots", foot);
            cGo = Btn("Run", "primary big", foot, Play);
        }

        void OpenSelect()
        {
            pick = Mathf.Max(0, Array.FindIndex(Tunables.Characters, c => c.id == Save.Data.character));
            RenderSelect();
            Show("select");
        }

        void Cycle(int d)
        {
            int n = Tunables.Characters.Length;
            pick = (pick + d + n) % n;
            Sfx.Click();
            RenderSelect();
        }

        void RenderSelect()
        {
            var c = Tunables.Characters[pick];
            // The survivor on the street behind the menu changes too.
            if (Save.Data.character != c.id)
            {
                Save.Data.character = c.id;
                Save.Persist();
                game.ShowMenu();
            }
            float maxHp = 0, maxSp = 0;
            foreach (var x in Tunables.Characters) { maxHp = Mathf.Max(maxHp, x.baseHealth); maxSp = Mathf.Max(maxSp, x.moveSpeed); }
            var st = Tunables.StatsFor(c, Save.Data.pup);
            var w = Tunables.Weapon(c.startingWeapon);
            bool owned = Save.Data.owned.Contains(w.id) || w.price == 0;
            var run = Save.RunWeapons(c);
            var more = new List<string>();
            for (int i = 1; i < run.Count; i++) more.Add(Tunables.Weapon(run[i]).name);

            cCard.Clear();
            cCard.Add(Portrait(c, "lg"));
            var name = El("c-name", cCard);
            Lbl("c-name-text", name, c.name.ToUpperInvariant());
            Lbl("pill", name, c.role.ToUpperInvariant());
            Lbl("blurb", cCard, c.blurb);
            StatBar(cCard, "Health", c.baseHealth / maxHp, $"{N(st.maxHp)} HP");
            StatBar(cCard, "Speed", c.moveSpeed / maxSp, $"{st.speed.ToString("0.0", CultureInfo.InvariantCulture)} m/s");
            var perk = El("perk", cCard);
            Lbl("small-caps", perk, "PERK");
            Lbl("perk-name", perk, c.perk.name);
            Lbl("perk-text", perk, c.perk.text);
            var gun = El("perk gunline", cCard);
            gun.Add(Icons.Gun(w.id, Hex.C(0xc9d2cc)));
            var gt = El("grow", gun);
            Lbl("small-caps", gt, "BRINGS");
            Lbl("perk-name", gt, w.name);
            Lbl("perk-text", gt, owned ? "Their own gun" : "Theirs for the run, even before you buy it");
            var load = El("perk gunline loadline", cCard);
            var lt = El("grow", load);
            Lbl("small-caps", lt, "ALSO CARRYING");
            Lbl("perk-name", lt, more.Count > 0 ? string.Join(", ", more) : "Nothing else");
            Btn("Loadout", "small", load, () => OpenMarket("select"));

            cDots.Clear();
            for (int i = 0; i < Tunables.Characters.Length; i++)
            {
                int k = i;
                var dot = new Button(() => { pick = k; Sfx.Click(); RenderSelect(); });
                dot.AddToClassList("c-dot");
                dot.EnableInClassList("on", i == pick);
                cDots.Add(dot);
            }
            cGo.text = $"RUN AS {c.name.ToUpperInvariant()}";
        }

        void Play()
        {
            HideBanner();
            Shown(result, false);
            game.StartRun();
        }

        // ── Modals ───────────────────────────────────────────────────────
        VisualElement pause, result, settingsModal;
        Label pauseLevel, rTitle, rDist, rSub, rTip;
        VisualElement rStats, rWallet;
        Result lastResult;
        bool hasResult;

        VisualElement Modal(out VisualElement card, bool scroll = false)
        {
            var m = El("modal", root);
            m.pickingMode = PickingMode.Position;       // nothing behind a modal can be touched
            if (scroll)
            {
                var sv = Scroller();
                sv.AddToClassList("card");
                m.Add(sv);
                card = sv.contentContainer;
            }
            else card = El("card", m);
            Shown(m, false);
            return m;
        }

        void BuildModals()
        {
            pause = Modal(out var pc);
            Lbl("card-title", pc, "PAUSED");
            pauseLevel = Lbl("card-sub", pc);
            var ps = El("stack", pc);
            Btn("RESUME", "primary big", ps, () => game.Resume());
            var pr = El("row-btns", ps);
            Btn("Restart", "", pr, Play);
            Btn("Quit", "", pr, ToMenu);
            NoteButton(ps, "Leave a note");

            result = Modal(out var rc, scroll: true);
            rTitle = Lbl("card-title red", rc, "OVERRUN");
            rDist = Lbl("r-dist", rc);
            rSub = Lbl("card-sub", rc);
            rStats = El("", rc);
            rWallet = El("r-wallet", rc);
            rTip = Lbl("r-tip", rc);
            var rs = El("stack", rc);
            Btn("RUN AGAIN", "primary big", rs, Play);
            var rr = El("row-btns", rs);
            Btn("Black Market", "", rr, () => { game.ShowMenu(); OpenMarket("menu"); });
            Btn("Menu", "", rr, ToMenu);
            NoteButton(rs, "Leave a note about this run");

            BuildSettings();
            BuildNotes();
        }

        void NoteButton(VisualElement parent, string text)
        {
            // The words as a child, not the button's own text, so the
            // pencil sits beside them.
            var b = Btn("", "note-btn", parent, OpenNote);
            var icon = Icons.Pencil();
            icon.style.width = 16; icon.style.height = 16; icon.style.marginRight = 6;
            b.Add(icon);
            Lbl("", b, text);
            b.style.flexDirection = FlexDirection.Row;
            b.style.justifyContent = Justify.Center;
            b.style.alignItems = Align.Center;
        }

        // ── The end of a run ─────────────────────────────────────────────
        public void ShowResult(Result r)
        {
            lastResult = r;
            hasResult = true;
            HideBanner();
            rDist.text = $"{N(r.distance)} M";
            rSub.text = r.best ? "NEW BEST!" : $"Stage {r.stage + 1} · {Mathf.Round(r.time)}s · {r.character}{(r.startAt > 0 ? $" · started at {r.startAt} m" : "")}";
            rSub.EnableInClassList("gold", r.best);
            rStats.Clear();
            void Row(string k, Action<VisualElement> v, bool total = false)
            {
                var row = El("r-row" + (total ? " total" : ""), rStats);
                Lbl("r-row-k", row, k);
                v(El("r-row-v", row));
            }
            Row("Zombies down", v => Lbl("", v, N(r.kills)));
            Row("Coins picked up", v => Coins(v, r.coins));
            Row($"Stage bonuses ({r.stage})", v => Coins(v, r.bonus));
            Row("Total", v => Coins(v, r.coins + r.bonus), true);
            rWallet.Clear();
            Lbl("", rWallet, "Wallet: ");
            Coins(rWallet, r.wallet);
            Lbl("", rWallet, $"· Best: {N(r.bestEver)} m").style.marginLeft = 4;
            rTip.text = Tip();
            Shown(result, true);
            current = "result";
            Shown(hud, false);
        }

        static readonly string[] Tips =
        {
            "Keep moving: stragglers keep coming even when you stand still.",
            "Red barrels go up with a crowd round them. Your aim knows that.",
            "Armor takes every hit until it's gone. Grab the blue vests.",
            "Runners have red eyes. Deal with them first.",
            "Tanks smash straight through barricades. Don't hide behind one.",
            "Rubble slows everything that crosses it, including you.",
            "Coins you pick up are yours to keep, even if you fall.",
            "Each survivor brings their own gun. Juno's launcher flattens walls.",
            "Glowing badges on the road are power-ups. Walk over one to use it.",
            "Zombies can hide behind walls and tankers. Step out to get a clear look.",
            "A suppressor keeps gunfire from waking the zombies further up the road.",
            "Every part trades something. Check the green and red under each one.",
            "Pick which guns you carry under Black Market → Weapons. Your survivor's own always comes.",
        };

        string Tip()
        {
            var (cost, label) = CheapestUpgrade();
            if (label != null && cost <= Save.Data.coins) return $"You can afford {label} in the Black Market.";
            return Tips[UnityEngine.Random.Range(0, Tips.Length)];
        }

        static (int cost, string label) CheapestUpgrade()
        {
            int best = int.MaxValue;
            string label = null;
            void Consider(int c, string l) { if (c < best) { best = c; label = l; } }
            var s = Save.Data;
            foreach (var w in Tunables.Weapons)
            {
                if (w.price > 0 && !s.owned.Contains(w.id)) { Consider(w.price, $"the {w.name}"); continue; }
                var g = Save.Parts(w.id);
                foreach (var slot in w.slots)
                    foreach (var part in Tunables.PartsFor(w, slot))
                        if (part.price > 0 && !g.owned.Contains($"{slot}:{part.id}")) Consider(Tunables.PartCost(w, part), $"a {part.name.ToLowerInvariant()} for the {w.name}");
            }
            foreach (var u in Tunables.PlayerUpgrades)
            {
                int lv = Save.Upgrade(u.id);
                if (lv < u.max) Consider(Tunables.PlayerUpgradeCost(u.id, lv), $"more {u.label.ToLowerInvariant()}");
            }
            return (best, label);
        }

        // ── Settings ─────────────────────────────────────────────────────
        readonly Dictionary<string, Button> toggles = new Dictionary<string, Button>();
        Button sReset;
        Label sNotesN, sVersion;
        bool resetArmed;

        Button Toggle(VisualElement parent, string id, string label, Action onClick)
        {
            var b = new Button(() => { Sfx.Click(); onClick(); });
            b.AddToClassList("toggle");
            Lbl("", b, label);
            Lbl("toggle-value", b);
            parent.Add(b);
            toggles[id] = b;
            return b;
        }

        void SetToggle(string id, bool on, string value)
        {
            var b = toggles[id];
            b.EnableInClassList("on", on);
            ((Label)b[1]).text = value;
        }

        void BuildSettings()
        {
            settingsModal = Modal(out var sc, scroll: true);
            Lbl("card-title", sc, "SETTINGS");
            var stack = El("stack", sc);
            var s = Save.Data.settings;
            Toggle(stack, "sound", "Sound", () => { s.sound = !s.sound; Sfx.Enabled = s.sound; Saved(); });
            Toggle(stack, "haptics", "Vibration", () => { s.haptics = !s.haptics; Haptics.Enabled = s.haptics; Saved(); });
            Toggle(stack, "quality", "Graphics", () => { s.quality = s.quality == "high" ? "low" : "high"; game.SetQuality(s.quality); Saved(); });
            var notesBtn = Toggle(stack, "notes", "Playtest notes", OpenNotes);
            sNotesN = (Label)notesBtn[1];
            Lbl("s-head", sc, "PLAYTEST TOOLS");
            var tools = El("stack", sc);
            var row = El("row-btns", tools);
            Btn("+1,000 coins", "small", row, () => { Save.Data.coins += 1000; Save.Persist(); RenderMenu(); Toast("+1,000 coins"); });
            Toggle(tools, "start", "Start runs at", () =>
            {
                int i = Array.IndexOf(StartAt, s.startAt);
                s.startAt = StartAt[(i + 1) % StartAt.Length];
                Saved();
            });
            Toggle(tools, "god", "Invincible", () => { s.god = !s.god; Saved(); });
            Toggle(tools, "fps", "Show frame rate", () => { s.fps = !s.fps; Saved(); });
            Toggle(tools, "powers", "Lots of power-ups", () => { s.powers = !s.powers; Saved(); });
            var end = El("stack", sc);
            sReset = Btn("Reset progress", "danger", end, () =>
            {
                if (!resetArmed) { resetArmed = true; DrawSettings(); return; }
                Save.Reset();
                resetArmed = false;
                game.ShowMenu();
                DrawSettings();
                RenderMenu();
            });
            Btn("Done", "primary", end, CloseSettings);
            sVersion = Lbl("credits", sc);
            void Saved() { Save.Persist(); DrawSettings(); }
        }

        void OpenSettings()
        {
            resetArmed = false;
            DrawSettings();
            Shown(settingsModal, true);
        }

        void CloseSettings()
        {
            Shown(settingsModal, false);
            RenderMenu();
        }

        void DrawSettings()
        {
            var s = Save.Data.settings;
            SetToggle("sound", s.sound, s.sound ? "On" : "Off");
            SetToggle("haptics", s.haptics, s.haptics ? "On" : "Off");
            SetToggle("quality", s.quality == "high", s.quality == "high" ? "High" : "Battery saver");
            SetToggle("start", s.startAt > 0, $"{s.startAt} m");
            SetToggle("god", s.god, s.god ? "On" : "Off");
            SetToggle("fps", s.fps, s.fps ? "On" : "Off");
            SetToggle("powers", s.powers, s.powers ? "On" : "Off");
            sNotesN.text = Save.Notes.Count.ToString(CultureInfo.InvariantCulture);
            sReset.text = resetArmed ? "Tap again to erase everything" : "Reset progress";
            sReset.EnableInClassList("armed", resetArmed);
            sVersion.text = $"Graveway · {BuildLabel}\nEverything is drawn and synthesised in code for now. Bebas Neue font (OFL) comes with the art.";
        }

        static string BuildLabel => $"v{Application.version} · {(Application.platform == RuntimePlatform.Android ? "Android" : Application.isEditor ? "Editor" : Application.platform.ToString())}";
    }
}
