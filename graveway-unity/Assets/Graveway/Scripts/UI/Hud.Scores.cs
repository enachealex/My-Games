// The scoreboard: the best 50 runs on this phone, each under a name, and
// online, the best in the world and in the player's country (Online.cs). After
// a run the name can be typed, or picked from the names already there (for a
// friend playing on your phone); the results say where it placed, here and
// online. The scoreboard opens from the menu's best run, or from the results.

using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

namespace Graveway
{
    public sealed partial class Hud
    {
        VisualElement scores, scList, scTabs;
        Label scHead;
        ScrollView scBody;
        Button scPhone, scWorld, scRegion;
        string scHighlight, scTab = "phone";
        readonly Dictionary<string, (Online.Board board, float at)> scCache = new Dictionary<string, (Online.Board, float)>();
        string scCountry = "";                          // the player's, as the server last said

        void BuildScores()
        {
            scores = El("screen armory", root);             // the Armory's dark backdrop
            scores.pickingMode = PickingMode.Position;
            var head = El("a-head", scores);
            var back = new Button(() => { Sfx.Click(); ShowMenu(); });
            back.AddToClassList("icon-btn");
            back.Add(Icons.Back());
            head.Add(back);
            Lbl("a-head-title", head, "SCOREBOARD");
            scTabs = El("tabs", scores);
            Button Tab(string id, string text)
            {
                var t = new Button(() => { Sfx.Click(); scTab = id; RenderScores(); }) { text = text };
                t.AddToClassList("tab");
                scTabs.Add(t);
                return t;
            }
            scPhone = Tab("phone", "This phone");
            scWorld = Tab("world", "World");
            scRegion = Tab("region", "Country");
            scBody = Scroller();
            scBody.AddToClassList("a-body");
            scores.Add(scBody);
            var content = El("a-content", scBody);
            scHead = Lbl("small-caps", content);
            scList = El("sc-list", content);
        }

        // highlight: a score's id, to pick out (and scroll to).
        void OpenScores(string highlight = null, string tab = "phone")
        {
            scHighlight = highlight;
            scTab = tab;
            scCache.Clear();
            SendScores();
            RenderScores();
            Show("scores");
        }

        void RenderScores()
        {
            bool online = Online.Configured;
            Shown(scTabs, online);
            if (!online) scTab = "phone";
            scPhone.EnableInClassList("on", scTab == "phone");
            scWorld.EnableInClassList("on", scTab == "world");
            scRegion.EnableInClassList("on", scTab == "region");
            scRegion.text = scCountry.Length > 0 ? Online.CountryName(scCountry) : "Country";
            scList.Clear();
            if (scTab == "phone") { PhoneScores(); return; }

            // Country names don't come with their "the": "United States · the best 50".
            scHead.text = scTab == "world" ? "THE WORLD · THE BEST 50"
                : scCountry.Length > 0 ? $"{Online.CountryName(scCountry).ToUpperInvariant()} · THE BEST 50" : "WHERE YOU ARE · THE BEST 50";
            if (!Online.On) { Lbl("need", scList, "Online scores are off. Turn them on in Settings."); return; }
            string tab = scTab;
            if (scCache.TryGetValue(tab, out var cached) && Time.unscaledTime - cached.at < 30) { OnlineScores(cached.board); return; }
            Lbl("need", scList, "Fetching the scoreboard…");
            StartCoroutine(Online.Fetch(tab, (board, problem) =>
            {
                if (board != null)
                {
                    scCache[tab] = (board, Time.unscaledTime);
                    if (!string.IsNullOrEmpty(board.country)) scCountry = board.country;
                }
                if (scTab != tab || current != "scores") return;          // moved on meanwhile
                if (board == null) { scList.Clear(); Lbl("need", scList, problem); return; }
                RenderScores();
            }));
        }

        void PhoneScores()
        {
            scHead.text = $"ON THIS PHONE · THE BEST {Save.KeepScores} RUNS";
            var list = Save.Data.scores;
            if (list.Count == 0)
            {
                Lbl("need", scList, "No runs on the scoreboard yet. Get up that road.");
                return;
            }
            VisualElement mine = null;
            for (int i = 0; i < list.Count; i++)
            {
                var s = list[i];
                bool me = s.id == scHighlight;
                var row = ScoreRow(i + 1, Tunables.Character(s.character), s.name, s.kills, s.place, s.distance, Day(s.at), me);
                if (me) mine = row;
            }
            if (mine != null) scBody.schedule.Execute(() => scBody.ScrollTo(mine));
        }

        void OnlineScores(Online.Board board)
        {
            if (scTab == "region" && string.IsNullOrEmpty(board.region))
            {
                Lbl("need", scList, "The scoreboard can't tell where you are, so there's no board for here.");
                return;
            }
            if (board.scores.Count == 0)
            {
                Lbl("need", scList, scTab == "world" ? "No runs online yet. Yours could be the first." : "No runs from here yet. Yours could be the first.");
                return;
            }
            // This phone's runs, picked out.
            var ours = new HashSet<string>();
            string highlight = scHighlight != null ? Online.Hash(scHighlight) : null;
            foreach (var s in Save.Data.scores) if (s.sent) ours.Add(Online.Hash(s.id));
            VisualElement mine = null;
            foreach (var r in board.scores)
            {
                string where = scTab == "world" && r.c.Length > 0 ? r.c : "";
                var row = ScoreRow(r.r, Tunables.Character(r.s), r.n, r.k, r.p, r.d, where, ours.Contains(r.h));
                if (r.h == highlight) mine = row;
            }
            if (mine != null) scBody.schedule.Execute(() => scBody.ScrollTo(mine));
        }

        VisualElement ScoreRow(int rank, CharacterDef c, string name, int kills, int place, int distance, string tail, bool mine)
        {
            var row = El("sc-row" + (mine ? " mine" : ""), scList);
            Lbl("sc-rank" + (rank <= 3 ? " top" : ""), row, rank.ToString(CultureInfo.InvariantCulture));
            row.Add(Portrait(c, "sm"));
            var words = El("sc-words", row);
            Lbl("sc-name" + (name.Length == 0 ? " none" : ""), words, name.Length > 0 ? name : "No name");
            Lbl("sc-sub", words, Parts(new[] { c.name, $"{N(kills)} kills", place > 0 ? Tunables.PlaceAt(place).name : "", tail }));
            Lbl("sc-dist", row, $"{N(distance)} M");
            return row;
        }

        static string Day(string iso) =>
            DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var t)
                ? t.ToLocalTime().ToString("d MMM", CultureInfo.InvariantCulture) : "";

        static string RankText(Score s)
        {
            int rank = Save.Rank(s);
            return rank == 1 ? "Best on this phone!" : $"#{rank} on this phone";
        }

        // ── Sending ──────────────────────────────────────────────────────
        // Runs waiting to go online go now, if they can; what's on screen
        // catches up as each one does.
        void SendScores()
        {
            if (!Online.On || Online.Sending || Online.Waiting().Count == 0) return;
            StartCoroutine(Online.SendWaiting(() =>
            {
                scCache.Clear();
                if (hasResult && lastResult.score != null) DrawOnline(lastResult.score);
            }));
        }

        // ── On the results ───────────────────────────────────────────────
        VisualElement rBoard;
        Label rOnline;
        IVisualElementScheduledItem renameSend;

        // Where the run went on the scoreboard, and the name it's under.
        void DrawBoard(Result r)
        {
            rBoard.Clear();
            rOnline = null;
            var s = r.score;
            if (s == null)
            {
                if (!r.board) Lbl("need", rBoard, "Playtest tools were on, so this run isn't on the scoreboard.");
                else if (r.distance > 0) Lbl("need", rBoard, $"Not in this phone's best {Save.KeepScores} runs.");
                return;
            }
            var head = El("r-board-head", rBoard);
            var ranks = El("", head);
            Lbl("r-rank", ranks, RankText(s));
            rOnline = Lbl("r-online", ranks);
            Btn("Scoreboard", "small", head, () => { ToMenu(); OpenScores(s.id, s.sent ? "world" : "phone"); });
            DrawOnline(s);
            var field = new TextField { maxLength = Save.NameLength };
            field.AddToClassList("name-field");
            field.textEdition.placeholder = "Your name";
            field.SetValueWithoutNotify(s.name);
            rBoard.Add(field);
            // The other names already on the scoreboard, a tap away.
            var chips = El("name-chips", rBoard);
            void Chips()
            {
                chips.Clear();
                foreach (var n in Save.RecentNames())
                {
                    if (n == s.name) continue;
                    if (chips.childCount >= 3) break;
                    Btn(n, "chip", chips, () => field.value = n);
                }
            }
            Chips();
            field.RegisterValueChangedCallback(e =>
            {
                Save.NameScore(s, e.newValue);
                chips.schedule.Execute(Chips);
                // Online too, once the typing's stopped.
                renameSend?.Pause();
                renameSend = rBoard.schedule.Execute(SendScores).StartingIn(1500);
            });
            SendScores();
        }

        // Where it placed online: in the world, and where it was sent from.
        void DrawOnline(Score s)
        {
            if (rOnline == null) return;
            Shown(rOnline, Online.On);
            if (!Online.On) return;
            if (s.refused) rOnline.text = "Not taken by the online scoreboard";
            else if (!s.sent || s.world <= 0) rOnline.text = Online.Sending ? "Sending it online…" : "Goes online when you're connected";
            else
            {
                if (s.country.Length > 0) scCountry = s.country;
                rOnline.text = $"World #{N(s.world)}" + (s.region > 0 ? $" · {Online.CountryName(s.country)} #{N(s.region)}" : "");
            }
        }
    }
}
