// The Black Market (playtest note 4): things to call in during a run,
// bought with coins and carried in a backpack, and that backpack on the
// HUD's left edge, within thumb reach. A shop of its own: the Armory sells
// guns, parts and upgrades.

using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

namespace Graveway
{
    public sealed partial class Hud
    {
        VisualElement blackMarket, bmCoins, bmPack, bmList;
        ScrollView bmBody;

        void BuildBlackMarket()
        {
            blackMarket = El("screen armory", root);            // the Armory's dark backdrop
            blackMarket.pickingMode = PickingMode.Position;
            var head = El("a-head", blackMarket);
            var back = new Button(() => { Sfx.Click(); ShowMenu(); });
            back.AddToClassList("icon-btn");
            back.Add(Icons.Back());
            head.Add(back);
            Lbl("a-head-title", head, "BLACK MARKET");
            bmCoins = Coins(head, 0, "wallet");
            bmBody = Scroller();
            bmBody.AddToClassList("a-body");
            blackMarket.Add(bmBody);
            var content = El("a-content", bmBody);
            var pack = El("loadout", content);
            Lbl("small-caps", pack, $"BACKPACK · {Tunables.BackpackSize} SLOTS · TAP ONE IN A RUN TO CALL IT IN");
            bmPack = El("lo-slots", pack);
            bmList = El("", content);
        }

        void OpenBlackMarket()
        {
            RenderBlackMarket();
            Show("blackmarket");
        }

        void RenderBlackMarket(bool keepScroll = false)
        {
            var scroll = bmBody.scrollOffset;
            var s = Save.Data;
            SetCoins(bmCoins, s.coins);
            bmPack.Clear();
            for (int i = 0; i < Tunables.BackpackSize; i++)
            {
                bool full = i < s.backpack.Count;
                var slot = El("lo-slot" + (full ? "" : " empty"), bmPack);
                if (full)
                {
                    var icon = Icons.Item(s.backpack[i], Icons.Accent);
                    icon.AddToClassList("item-icon-sm");
                    slot.Add(icon);
                }
                else Lbl("lo-slot-label", slot, "Empty");
            }
            bmList.Clear();
            bool roomy = s.backpack.Count < Tunables.BackpackSize;
            foreach (var it0 in Tunables.Items)
            {
                var it = it0;
                int have = 0;
                foreach (var id in s.backpack) if (id == it.id) have++;
                var card = El("panel bm-item", bmList);
                var row = El("bm-row", card);
                row.Add(Icons.Item(it.id, Icons.Accent));
                var words = El("bm-words", row);
                Lbl("bm-name", words, it.name);
                Lbl("bm-blurb", words, it.blurb);
                Lbl("bm-have", words, have > 0 ? $"{have} in your backpack"
                    : it.delay > 0 ? $"Here {it.delay.ToString("0", CultureInfo.InvariantCulture)} s after you call it in" : "Waits for the next boss");
                var buy = Quiet("small bm-buy", row, b => BuyItem(it, b));
                buy.EnableInClassList("off", s.coins < it.price || !roomy);
                buy.style.flexDirection = FlexDirection.Row;
                Coins(buy, it.price);
            }
            if (keepScroll) bmBody.schedule.Execute(() => bmBody.scrollOffset = scroll);
        }

        void BuyItem(ItemDef it, VisualElement button)
        {
            var s = Save.Data;
            if (s.backpack.Count >= Tunables.BackpackSize) { Sfx.Denied(); Nudge(button); Toast("Your backpack's full"); return; }
            if (s.coins < it.price) { Sfx.Denied(); Nudge(button); return; }
            s.coins -= it.price;
            s.backpack.Add(it.id);
            Sfx.Buy();
            Save.Persist();
            RenderBlackMarket(true);
        }

        // ── The backpack on the HUD ──────────────────────────────────────
        VisualElement hPack;
        int packKey = -1;

        void BuildPack() => hPack = El("h-pack", hud);

        // One button for each kind of item in the backpack, with how many;
        // dimmed while one's on its way or can't be used; the helicopter's
        // seconds above it while it fires. Rebuilt only when that changes.
        void UpdatePack()
        {
            var pack = Save.Data.backpack;
            float heli = game.HeliLeft;
            int key = 17 + Mathf.CeilToInt(heli);
            foreach (var it in Tunables.Items)
            {
                int n = 0;
                foreach (var id in pack) if (id == it.id) n++;
                key = key * 31 + n * 2 + (n > 0 && game.Busy(it.id) ? 1 : 0);
            }
            if (key == packKey) return;
            packKey = key;
            hPack.Clear();
            foreach (var it0 in Tunables.Items)
            {
                var it = it0;
                int n = 0;
                foreach (var id in pack) if (id == it.id) n++;
                if (n == 0 && !(it.id == "heli" && heli >= 0)) continue;
                var b = Press("pack-btn", hPack, () =>
                {
                    if (game.CallIn(it.id)) { packKey = -1; Haptics.Vibrate(20); }
                    else Sfx.Denied();
                });
                b.EnableInClassList("busy", n == 0 || game.Busy(it.id));
                b.Add(Icons.Item(it.id, Icons.Text));
                if (n > 1) Lbl("pack-n", b, n.ToString(CultureInfo.InvariantCulture));
                if (it.id == "heli" && heli >= 0) Lbl("pack-t", b, $"{Mathf.CeilToInt(heli)}s");
            }
        }
    }
}
