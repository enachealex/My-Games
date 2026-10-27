// Survivor select: a carousel of cards. The card follows a finger left or
// right and, let go far enough (or flicked), glides on to the next
// survivor; otherwise back. The survivors either side peek in at the
// edges. The dots, the arrow keys and A/D change survivor too. Under the
// card: Run as them, or unlock them.

using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

namespace Graveway
{
    public sealed partial class Hud
    {
        int pick;
        VisualElement cWrap, cTrack, cDots, cPrice;
        readonly ScrollView[] cCards = new ScrollView[3];   // the survivor before, this one, the next
        Button cGo, cBuy;
        Label cNeed;

        const float CardPeek = 18, CardGap = 10, GlideTime = 0.24f;
        float slideW = 1;                                   // a card's width and the gap after it
        float cardFace = 210;                               // the portrait's size: smaller where the card's shorter
        int dragId = -1, glideDir;
        Vector2 dragFrom;
        bool? dragAcross;                                   // null until the finger's moved enough to tell
        float dragX, dragStart, glideLeft = -1;           // seconds of the glide still to go; -1 when there's none

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
            cWrap = El("c-wrap", select);
            cTrack = El("c-track", cWrap);
            for (int i = 0; i < 3; i++)
            {
                var card = Scroller();
                card.AddToClassList("panel");
                card.AddToClassList("c-card");
                card.AddToClassList("fade");
                cTrack.Add(card);
                cCards[i] = card;
            }
            cWrap.RegisterCallback<GeometryChangedEvent>(_ => LayCards());
            cWrap.RegisterCallback<PointerDownEvent>(DragDown, TrickleDown.TrickleDown);
            cWrap.RegisterCallback<PointerMoveEvent>(DragMove, TrickleDown.TrickleDown);
            cWrap.RegisterCallback<PointerUpEvent>(DragUp, TrickleDown.TrickleDown);
            cWrap.RegisterCallback<PointerCancelEvent>(_ => { if (dragId >= 0) { dragId = -1; Glide(0); } }, TrickleDown.TrickleDown);
            var foot = El("c-foot", select);
            cDots = El("c-dots", foot);
            cGo = Btn("Run", "primary big", foot, Play);
            // In its place for a survivor you haven't got yet.
            cBuy = Quiet("primary big c-buy", foot, b => BuySurvivor(b));
            Lbl("c-buy-label", cBuy, "UNLOCK");
            cPrice = Coins(cBuy, 0);
            cNeed = Lbl("need", foot);
        }

        // The three cards side by side, this one in the middle with a strip
        // of each neighbour showing.
        void LayCards()
        {
            float w = cWrap.resolvedStyle.width;
            if (float.IsNaN(w) || w <= 0) return;
            float cardW = w - 2 * (CardPeek + CardGap);
            slideW = cardW + CardGap;
            // Room for everything under the portrait (about 470 high) before
            // it: no smaller than 120, no bigger than 210.
            cardFace = Mathf.Clamp(cWrap.resolvedStyle.height - 470, 120, 210);
            for (int i = 0; i < 3; i++)
            {
                cCards[i].style.width = cardW;
                cCards[i].style.left = CardPeek + CardGap + (i - 1) * slideW;
                var face = cCards[i].Q(className: "portrait");
                if (face != null) face.style.width = face.style.height = cardFace;
            }
        }

        void OpenSelect()
        {
            pick = Mathf.Max(0, Array.FindIndex(Tunables.Characters, c => c.id == Save.Data.character));
            RenderSelect();
            Show("select");
        }

        // ── Dragging ─────────────────────────────────────────────────────
        void DragDown(PointerDownEvent e)
        {
            if (glideLeft >= 0 || dragId >= 0) return;
            dragId = e.pointerId;
            dragFrom = e.position;
            dragAcross = null;
            dragX = 0;
            dragStart = Time.unscaledTime;
        }

        void DragMove(PointerMoveEvent e)
        {
            if (e.pointerId != dragId) return;
            var d = (Vector2)e.position - dragFrom;
            if (dragAcross == null && d.magnitude > 10)
            {
                dragAcross = Mathf.Abs(d.x) > Mathf.Abs(d.y);
                if (dragAcross == true) cWrap.CapturePointer(dragId);  // the card's, not the scroll's, from here
                else { dragId = -1; return; }                       // scrolling the card: leave it be
            }
            if (dragAcross != true) return;
            dragX = d.x;
            cTrack.style.translate = new Translate(dragX, 0);
            e.StopPropagation();
        }

        void DragUp(PointerUpEvent e)
        {
            if (e.pointerId != dragId) return;
            dragId = -1;
            if (cWrap.HasPointerCapture(e.pointerId)) cWrap.ReleasePointer(e.pointerId);
            if (dragAcross != true) return;
            e.StopPropagation();
            // Far enough, or flicked: on to the next (or the one before).
            float speed = dragX / Mathf.Max(0.05f, Time.unscaledTime - dragStart);
            bool go = Mathf.Abs(dragX) > slideW * 0.22f || Mathf.Abs(speed) > 900 && Mathf.Abs(dragX) > 24;
            Glide(go ? (dragX < 0 ? 1 : -1) : 0);
        }

        // Glides the cards along one (dir ±1), or back where they were (0);
        // the survivor changes when they get there (FinishGlide, from Update).
        void Glide(int dir)
        {
            if (glideLeft >= 0) return;
            glideDir = dir;
            glideLeft = GlideTime;
            cTrack.AddToClassList("glide");
            cTrack.style.translate = new Translate(-dir * slideW, 0);
            if (dir != 0)
            {
                cCards[1].AddToClassList("side");
                cCards[1 + dir].RemoveFromClassList("side");
                Sfx.Click();
            }
        }

        void FinishGlide()
        {
            if (glideLeft < 0) return;
            // Frame time, unscaled, rather than the clock: the same on a
            // phone, and tests that step frames faster than real time see
            // it finish when it would.
            glideLeft -= Time.timeScale > 0 ? Time.deltaTime / Time.timeScale : Time.unscaledDeltaTime;
            if (glideLeft > 0) return;
            glideLeft = -1;
            // Back to the middle without a glide: the cards are filled again
            // for the survivor now there.
            cTrack.RemoveFromClassList("glide");
            foreach (var c in cCards) c.RemoveFromClassList("fade");
            cTrack.style.translate = new Translate(0, 0);
            if (glideDir != 0)
            {
                int n = Tunables.Characters.Length;
                pick = (pick + glideDir + n) % n;
                RenderSelect();
            }
            cTrack.schedule.Execute(() => { foreach (var c in cCards) c.AddToClassList("fade"); });
        }

        void Cycle(int d) => Glide(d);

        // ── The cards ────────────────────────────────────────────────────
        void RenderSelect()
        {
            int n = Tunables.Characters.Length;
            var c = Tunables.Characters[pick];
            bool mine = Save.HasSurvivor(c);
            // The survivor on the street behind the menu changes too, to any
            // that's yours.
            if (mine && Save.Data.character != c.id)
            {
                Save.Data.character = c.id;
                Save.Persist();
                game.ShowMenu();
            }
            for (int i = 0; i < 3; i++)
            {
                FillCard(cCards[i], Tunables.Characters[(pick + i - 1 + n) % n], i == 1);
                cCards[i].EnableInClassList("side", i != 1);
            }

            cDots.Clear();
            for (int i = 0; i < n; i++)
            {
                int k = i;
                var dot = new Button(() => { if (k == pick || glideLeft >= 0) return; pick = k; Sfx.Click(); RenderSelect(); });
                dot.AddToClassList("c-dot");
                dot.EnableInClassList("on", i == pick);
                dot.EnableInClassList("locked", !Save.HasSurvivor(Tunables.Characters[i]));
                cDots.Add(dot);
            }
            cGo.text = $"RUN AS {c.name.ToUpperInvariant()}";
            Shown(cGo, mine);
            Shown(cBuy, !mine);
            Shown(cNeed, !mine);
            if (!mine)
            {
                int coins = Save.Data.coins;
                SetCoins(cPrice, c.price);
                cBuy.EnableInClassList("off", coins < c.price);
                cNeed.text = coins < c.price ? $"{N(c.price - coins)} more coins needed" : $"You have {N(coins)} coins";
            }
        }

        // A survivor's card. Those either side are only to see while they
        // slide in: nothing on them can be pressed.
        void FillCard(ScrollView card, CharacterDef c, bool here)
        {
            bool mine = Save.HasSurvivor(c);
            float maxHp = 0, maxSp = 0;
            foreach (var x in Tunables.Characters) { maxHp = Mathf.Max(maxHp, x.baseHealth); maxSp = Mathf.Max(maxSp, x.moveSpeed); }
            var st = Tunables.StatsFor(c, Save.Data.pup);
            var w = Tunables.Weapon(c.startingWeapon);
            bool owned = Save.Data.owned.Contains(w.id) || w.price == 0;

            card.Clear();
            card.scrollOffset = Vector2.zero;
            var portrait = Portrait(c, "lg");
            portrait.EnableInClassList("locked", !mine);
            portrait.style.width = portrait.style.height = cardFace;
            card.Add(portrait);
            var name = El("c-name", card);
            Lbl("c-name-text", name, c.name.ToUpperInvariant());
            Lbl("pill", name, mine ? c.role.ToUpperInvariant() : "LOCKED");
            Lbl("blurb", card, c.blurb);
            StatBar(card, "Health", c.baseHealth / maxHp, $"{N(st.maxHp)} HP");
            StatBar(card, "Speed", c.moveSpeed / maxSp, $"{st.speed.ToString("0.0", CultureInfo.InvariantCulture)} m/s");
            var perk = El("perk", card);
            Lbl("small-caps", perk, "PERK");
            Lbl("perk-name", perk, c.perk.name);
            Lbl("perk-text", perk, c.perk.text);
            // What their own gun (and they) get from it being theirs: more,
            // the dearer the survivor.
            var spec = El("perk", card);
            Lbl("small-caps", spec, $"SPECIALIST · TIER {c.specialty.tier} OF {Tunables.Characters.Length}");
            Lbl("perk-name", spec, $"{w.name} specialist");
            Lbl("perk-boost", spec, Parts(Tunables.SpecialtyText(c.specialty)));
            // Their guns: their own first (the Armory's, not a copy, so its
            // parts are on it), then what else they carry.
            var guns = El("perk gunline", card);
            guns.Add(Icons.Gun(w.id, Hex.C(0xc9d2cc)));
            var gt = El("grow", guns);
            Lbl("small-caps", gt, "GUNS");
            var names = new List<string>();
            foreach (var id in mine ? Save.RunWeapons(c) : new List<string> { w.id }) names.Add(Tunables.Weapon(id).name);
            Lbl("perk-name", gt, string.Join(" · ", names));
            Lbl("perk-text", gt, owned ? $"The {w.name}'s theirs, with its Armory parts"
                : $"The {w.name}'s theirs every run, even before you buy it");
            if (mine)
            {
                Btn("Loadout", "small", guns, () => OpenArmory("select"));
                // Any hat unlocked, on anyone.
                string hat = Save.Hat(c.id);
                var hl = El("perk gunline hatline", card);
                var pic = El("hat-pic", hl);
                if (hat.Length > 0) pic.style.backgroundImage = Studio.HatPicture(hat);
                var ht = El("grow", hl);
                Lbl("small-caps", ht, "HAT");
                Lbl("perk-name", ht, hat.Length > 0 ? Tunables.Hat(hat).name : "None");
                string boost = Tunables.Booster(Tunables.Hat(hat));
                if (boost.Length > 0) Lbl("perk-boost", ht, boost);
                Btn("Hats", "small", hl, () => OpenHats(c));
            }
            if (!here) card.Query<Button>().ForEach(b => b.SetEnabled(false));
        }

        void BuySurvivor(VisualElement button)
        {
            var c = Tunables.Characters[pick];
            var s = Save.Data;
            if (Save.HasSurvivor(c)) return;
            if (s.coins < c.price) { Sfx.Denied(); Nudge(button); return; }
            s.coins -= c.price;
            s.survivors.Add(c.id);
            Sfx.Buy();
            Toast($"{c.name} is with you now");
            Save.Persist();
            RenderSelect();                         // and the one you'll run as
        }
    }
}
