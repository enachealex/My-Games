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
        static readonly int[] StartAt = { 0, 200, 500, 950 };   // 950: just short of the second boss, and the evac

        // ── Menu ─────────────────────────────────────────────────────────
        VisualElement mCoins, mChar, mBadge;
        Label mBest, mRuns, mBadgeN;
        Button mUpdate;
        public readonly Updates updates = new Updates();

        void BuildMenu()
        {
            menu = El("screen menu", root);
            menu.style.backgroundImage = Icons.MenuShade;
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
            mUpdate = Btn("", "update", bottom, () => updates.Install());
            Shown(mUpdate, false);
            // Tap your survivor to change them, your best run for the scoreboard.
            var stats = El("menu-stats", bottom);
            mChar = Card(stats, "", OpenSelect);
            var best = Card(stats, "m-best", () => OpenScores());
            Lbl("small-caps", best, "BEST RUN");
            mBest = Lbl("m-big", best, "—");
            mRuns = Lbl("m-small", best, "No runs yet");
            Lbl("m-chev", best, "›");
            Btn("PLAY", "primary big", bottom, Play);
            // Two shops: guns and parts, and things to call in during a run.
            var shops = El("row-btns", bottom);
            Btn("Armory", "", shops, () => OpenArmory("menu"));
            Btn("Black Market", "", shops, OpenBlackMarket);
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
            Lbl("m-chev", mChar, "›");
        }

        static Button Card(VisualElement parent, string cls, Action onClick)
        {
            var b = new Button(() => { Sfx.Click(); onClick(); });
            b.AddToClassList("m-card");
            foreach (var c in cls.Split(' ')) if (c.Length > 0) b.AddToClassList(c);
            parent.Add(b);
            return b;
        }

        // A newer build to install, on the menu.
        void DrawUpdate()
        {
            var n = updates.newer;
            Shown(mUpdate, n != null);
            if (n != null) mUpdate.text = $"New version {n.version} · tap to get it";
        }

        // Asked for in Settings: says so when there's nothing newer.
        System.Collections.IEnumerator CheckNow()
        {
            yield return updates.Check(true);
            if (updates.newer == null) Toast(updates.status);
        }

        void ToMenu()
        {
            HideBanner();
            Shown(result, false);
            game.ShowMenu();
            ShowMenu();
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
            Btn("Call the evac (playtest)", "note-btn", ps, () => { game.Resume(); game.PlaytestEvac(); });

            result = Modal(out var rc, scroll: true);
            rTitle = Lbl("card-title", rc, "OVERRUN");
            rDist = Lbl("r-dist", rc);
            rSub = Lbl("card-sub", rc);
            rStats = El("", rc);
            rWallet = El("r-wallet", rc);
            rBoard = El("r-board", rc);
            rHats = El("r-hats", rc);
            rTip = Lbl("r-tip", rc);
            var rs = El("stack", rc);
            Btn("RUN AGAIN", "primary big", rs, Play);
            var rr = El("row-btns", rs);
            Btn("Armory", "", rr, () => { game.ShowMenu(); OpenArmory("menu"); });
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
        // A run's coins kept after a quit or restart, or after the app closed
        // in the middle of one.
        public void RunKept(RunRecord r, bool unfinished)
        {
            if (unfinished) Banner("Last run saved", $"+{N(r.coins)} coins · {N(r.distance)} m", 3.5f);
            else if (r.coins > 0) Toast($"Run saved · +{N(r.coins)} coins");
        }

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
            DrawBoard(r);
            DrawNewHats(r);
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
            "Runners are the bony ones. Deal with them first.",
            "Tanks smash straight through barricades. Don't hide behind one.",
            "Rubble slows everything that crosses it, including you.",
            "Coins you pick up are yours to keep, even if you fall.",
            "Each survivor brings their own gun. Juno's launcher flattens walls.",
            "Glowing badges on the road are power-ups. Walk over one to use it.",
            "Zombies can hide behind walls and tankers. Step out to get a clear look.",
            "A suppressor keeps gunfire from waking the zombies further up the road.",
            "Every part trades something. Check the green and red under each one.",
            "Pick which guns you carry under Armory → Weapons. Your survivor's own always comes.",
        };

        string Tip()
        {
            var (cost, label) = CheapestUpgrade();
            if (label != null && cost <= Save.Data.coins) return $"You can afford {label} in the Armory.";
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
        Label sNotesN, sVersion, sUpdate;
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
            Toggle(stack, "updates", "Updates", () =>
            {
                if (updates.newer != null) updates.Install();
                else if (!updates.busy) StartCoroutine(CheckNow());
            });
            sUpdate = Lbl("fine", stack);
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
            Toggle(tools, "hats", "Every hat", () => { s.allHats = !s.allHats; Saved(); RefreshPortraits(); game.Dress(); });
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
            SetToggle("hats", s.allHats, s.allHats ? "On" : "Off");
            sNotesN.text = Save.Notes.Count.ToString(CultureInfo.InvariantCulture);
            sReset.text = resetArmed ? "Tap again to erase everything" : "Reset progress";
            sReset.EnableInClassList("armed", resetArmed);
            sVersion.text = $"Graveway · {BuildLabel}\n3D art by Quaternius (CC0). Bebas Neue font (OFL). Sounds are synthesised in code.";
            var n = updates.newer;
            SetToggle("updates", n != null, updates.busy ? "Checking…" : n != null ? $"Get {n.version}" : "Check now");
            string build = Updates.Code > 0 ? $"This is version {Application.version} (build {Updates.Code})" : $"This is version {Application.version}, not a release build";
            sUpdate.text = build + (updates.busy || string.IsNullOrEmpty(updates.status) ? "." : $" · {updates.status}.");
        }

        // As the web version's: "v0.2 (build 2) · Android", in Settings and on sent notes.
        static string BuildLabel => $"v{Application.version}{(Updates.Code > 0 ? $" (build {Updates.Code})" : "")} · " +
                                    (Application.platform == RuntimePlatform.Android ? "Android" : Application.isEditor ? "Editor" : Application.platform.ToString());
    }
}
