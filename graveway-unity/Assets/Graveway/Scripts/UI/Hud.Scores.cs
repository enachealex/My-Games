// The scoreboard: each run's distance under a name, the best 50 on this
// device. After a run the name can be typed, or picked from the names
// already there (for a friend playing on your phone). The scoreboard opens
// from the menu's best run, or from the results. Boards online, worldwide
// and by region, are planned: PORT_PLAN.md.

using System;
using System.Globalization;
using UnityEngine.UIElements;

namespace Graveway
{
    public sealed partial class Hud
    {
        VisualElement scores, scList;
        ScrollView scBody;
        string scHighlight;

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
            scBody = Scroller();
            scBody.AddToClassList("a-body");
            scores.Add(scBody);
            var content = El("a-content", scBody);
            Lbl("small-caps", content, $"ON THIS PHONE · THE BEST {Save.KeepScores} RUNS");
            scList = El("sc-list", content);
        }

        // highlight: a score's id, to pick out (and scroll to).
        void OpenScores(string highlight = null)
        {
            scHighlight = highlight;
            RenderScores();
            Show("scores");
        }

        void RenderScores()
        {
            scList.Clear();
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
                var c = Tunables.Character(s.character);
                bool me = s.id == scHighlight;
                var row = El("sc-row" + (me ? " mine" : ""), scList);
                if (me) mine = row;
                Lbl("sc-rank" + (i < 3 ? " top" : ""), row, (i + 1).ToString(CultureInfo.InvariantCulture));
                row.Add(Portrait(c, "sm"));
                var words = El("sc-words", row);
                Lbl("sc-name" + (s.name.Length == 0 ? " none" : ""), words, s.name.Length > 0 ? s.name : "No name");
                Lbl("sc-sub", words, $"{c.name} · {N(s.kills)} kills · {Day(s.at)}");
                Lbl("sc-dist", row, $"{N(s.distance)} M");
            }
            if (mine != null) scBody.schedule.Execute(() => scBody.ScrollTo(mine));
        }

        static string Day(string iso) =>
            DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var t)
                ? t.ToLocalTime().ToString("d MMM", CultureInfo.InvariantCulture) : "";

        static string RankText(Score s)
        {
            int rank = Save.Rank(s);
            return rank == 1 ? "Best on this phone!" : $"#{rank} on this phone";
        }

        // ── On the results ───────────────────────────────────────────────
        VisualElement rBoard;

        // Where the run went on the scoreboard, and the name it's under.
        void DrawBoard(Result r)
        {
            rBoard.Clear();
            var s = r.score;
            if (s == null)
            {
                if (!r.board) Lbl("need", rBoard, "Playtest tools were on, so this run isn't on the scoreboard.");
                else if (r.distance > 0) Lbl("need", rBoard, $"Not in this phone's best {Save.KeepScores} runs.");
                return;
            }
            var head = El("r-board-head", rBoard);
            Lbl("r-rank", head, RankText(s));
            Btn("Scoreboard", "small", head, () => { ToMenu(); OpenScores(s.id); });
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
            });
        }
    }
}
