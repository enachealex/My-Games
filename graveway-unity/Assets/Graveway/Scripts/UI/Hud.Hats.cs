// Hats, the first cosmetics: unlocked by what you've done (Tunables.Hats),
// and once unlocked, any survivor can wear one, each their own. Picked from
// the survivor's card; what unlocks the rest, and how close you are, shows
// under each. A run that unlocks one says so on the results.

using UnityEngine;
using UnityEngine.UIElements;

namespace Graveway
{
    public sealed partial class Hud
    {
        VisualElement hatsScreen, htFace, htCards;
        Label htTitle;
        CharacterDef htWho;

        void BuildHats()
        {
            hatsScreen = El("screen armory", root);         // the Armory's dark backdrop
            hatsScreen.pickingMode = PickingMode.Position;
            var head = El("a-head", hatsScreen);
            var back = new Button(() => { Sfx.Click(); CloseHats(); });
            back.AddToClassList("icon-btn");
            back.Add(Icons.Back());
            head.Add(back);
            Lbl("a-head-title", head, "HATS");
            var body = Scroller();
            body.AddToClassList("a-body");
            hatsScreen.Add(body);
            var content = El("a-content", body);
            htFace = El("hat-face", content);
            htTitle = Lbl("small-caps", content);
            htCards = El("hgrid", content);
        }

        void OpenHats(CharacterDef c)
        {
            htWho = c;
            RenderHats();
            Show("hats");
        }

        void CloseHats()
        {
            RenderSelect();
            Show("select");
        }

        void RenderHats()
        {
            var c = htWho;
            string wearing = Save.Hat(c.id);
            htFace.Clear();
            htFace.Add(Portrait(c, "lg"));
            int have = 0;
            foreach (var h in Tunables.Hats) if (Save.HasHat(h.id)) have++;
            htTitle.text = $"{c.name.ToUpperInvariant()}'S HAT · {have} OF {Tunables.Hats.Length} UNLOCKED, FOR ANYONE";
            htCards.Clear();
            Card(null);
            foreach (var h in Tunables.Hats) Card(h);

            void Card(HatDef h)
            {
                string id = h?.id ?? "";
                bool mine = h == null || Save.HasHat(id), on = id == wearing;
                Button b = null;
                b = new Button(() => PickHat(h, b));
                b.AddToClassList("hcard");
                b.EnableInClassList("on", on);
                b.EnableInClassList("locked", !mine);
                var pic = El("hcard-pic", b);
                if (h != null) pic.style.backgroundImage = Studio.HatPicture(id);
                else Lbl("hcard-none", pic, "—");
                Lbl("hcard-name", b, h?.name ?? "No hat");
                Lbl("hcard-sub", b, on ? "Wearing" : mine ? "Tap to wear" : h.goal);
                if (!mine)
                {
                    int now = Mathf.Min(Save.Stat(h.stat), h.target);
                    El("hbar-fill", El("hbar", b)).style.width = Length.Percent(100f * now / h.target);
                    Lbl("hcard-n", b, $"{N(now)} / {N(h.target)}");
                }
                htCards.Add(b);
            }
        }

        void PickHat(HatDef h, VisualElement button)
        {
            if (h != null && !Save.HasHat(h.id)) { Sfx.Denied(); Nudge(button); return; }
            Sfx.Click();
            Save.Wear(htWho.id, h?.id ?? "");
            game.Dress();
            RefreshPortraits();
            RenderHats();
        }

        // ── On the results ───────────────────────────────────────────────
        VisualElement rHats;

        void DrawNewHats(Result r)
        {
            rHats.Clear();
            if (r.hats == null) return;
            foreach (var id in r.hats)
            {
                var h = Tunables.Hat(id);
                var row = El("r-hat", rHats);
                El("r-hat-pic", row).style.backgroundImage = Studio.HatPicture(id);
                var words = El("grow", row);
                Lbl("r-hat-t", words, "NEW HAT");
                Lbl("r-hat-n", words, h.name);
                Lbl("r-hat-g", words, h.goal);
                Btn("Wear it", "small", row, () =>
                {
                    ToMenu();
                    OpenSelect();
                    OpenHats(Tunables.Character(Save.Data.character));
                });
            }
        }
    }
}
