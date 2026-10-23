// The Armory: unlock guns, choose which to carry, fit parts, and upgrade
// the survivor. A port of that part of graveway/src/ui.js.

using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

namespace Graveway
{
    public sealed partial class Hud
    {
        string armoryTab = "weapons", selWeapon = "pistol", openSlot, armoryFrom = "menu";
        bool partsDirty;
        VisualElement aCoins, aContent;
        ScrollView aBody;
        Button tabWeapons, tabSurvivor;

        void BuildArmory()
        {
            armory = El("screen armory", root);
            armory.pickingMode = PickingMode.Position;
            var head = El("a-head", armory);
            var back = new Button(() => { Sfx.Click(); CloseArmory(); });
            back.AddToClassList("icon-btn");
            back.Add(Icons.Back());
            head.Add(back);
            Lbl("a-head-title", head, "ARMORY");
            aCoins = Coins(head, 0, "wallet");
            var tabs = El("tabs", armory);
            tabWeapons = new Button(() => { Sfx.Click(); armoryTab = "weapons"; RenderArmory(); }) { text = "Weapons" };
            tabSurvivor = new Button(() => { Sfx.Click(); armoryTab = "survivor"; RenderArmory(); }) { text = "Survivor" };
            foreach (var t in new[] { tabWeapons, tabSurvivor }) { t.AddToClassList("tab"); tabs.Add(t); }
            aBody = Scroller();
            aBody.AddToClassList("a-body");
            armory.Add(aBody);
            aContent = El("a-content", aBody);
        }

        // from: "select" when opened from survivor select, which Back returns to.
        void OpenArmory(string from)
        {
            armoryFrom = from;
            openSlot = null;
            if (from == "select") armoryTab = "weapons";
            Shown(result, false);
            RenderArmory();
            Show("armory");
        }

        void CloseArmory()
        {
            if (partsDirty)
            {
                // New parts show on the guns in the portraits and on the menu survivor.
                partsDirty = false;
                RefreshPortraits();
                game.ShowMenu();
            }
            if (armoryFrom == "select") { RenderSelect(); Show("select"); return; }
            ShowMenu();
        }

        void RenderArmory(bool keepScroll = false)
        {
            var scroll = aBody.scrollOffset;
            SetCoins(aCoins, Save.Data.coins);
            tabWeapons.EnableInClassList("on", armoryTab == "weapons");
            tabSurvivor.EnableInClassList("on", armoryTab == "survivor");
            aContent.Clear();
            if (armoryTab == "weapons") WeaponsTab(); else SurvivorTab();
            if (keepScroll) aBody.schedule.Execute(() => aBody.scrollOffset = scroll);
        }

        static bool IsOwned(WeaponDef w) => w.price == 0 || Save.Data.owned.Contains(w.id);

        // ── Weapons ──────────────────────────────────────────────────────
        void WeaponsTab()
        {
            var all = new List<WeaponStats>();
            foreach (var w0 in Tunables.Weapons) all.Add(WeaponStats.Of(w0, Save.FittedParts(w0.id), null));
            float Max(Func<WeaponStats, float> f) { float m = 0; foreach (var x in all) m = Mathf.Max(m, f(x)); return m; }
            var surv = Tunables.Character(Save.Data.character);
            var eq = Save.Loadout(surv.id);

            LoadoutStrip(surv, eq);
            var grid = El("wgrid", aContent);
            foreach (var w0 in Tunables.Weapons)
            {
                var w1 = w0;
                bool owned = IsOwned(w1), mine = w1.id == surv.startingWeapon, on = mine || owned && eq.Contains(w1.id);
                var card = new Button(() => { if (selWeapon != w1.id) openSlot = null; selWeapon = w1.id; Sfx.Click(); RenderArmory(true); });
                card.AddToClassList("wcard");
                card.EnableInClassList("eq", on);
                card.EnableInClassList("sel", w1.id == selWeapon);
                card.Add(Icons.Gun(w1.id, owned ? Hex.C(0xc9d2cc) : Hex.C(0x5d6764)));
                Lbl("wn" + (w1.name.Length > 13 ? " long" : ""), card, w1.name);
                var tag = El("wt", card);
                if (mine) Lbl("", tag, "Carried");
                else if (!owned) Coins(tag, w1.price);
                else Lbl("", tag, on ? "Equipped" : "Owned");
                var sig = Array.Find(Tunables.Characters, c => c.startingWeapon == w1.id && w1.price > 0);
                if (sig != null) Lbl("wsig", card, $"{sig.name}'s gun");
                grid.Add(card);
            }

            int wi = Array.FindIndex(Tunables.Weapons, x => x.id == selWeapon);
            var w = Tunables.Weapons[wi];
            var s = all[wi];
            bool isOwned = IsOwned(w), own = surv.startingWeapon == w.id, equipped = !own && isOwned && eq.Contains(w.id);
            var panel = El("panel", aContent);
            var head = El("wd-head", panel);
            Lbl("wd-title", head, w.name.ToUpperInvariant());
            if (own) Lbl("pill", head, $"{surv.name.ToUpperInvariant()}'S OWN");
            else if (equipped) Lbl("pill", head, "EQUIPPED");
            var pic = Studio.GunPicture(w.model, Save.FittedParts(w.id));
            if (pic) El("gunpic", panel).style.backgroundImage = pic;
            Lbl("blurb", panel, w.blurb);

            // Every bar is "longer is better".
            const float Deg = 57.2958f;
            float Acc(WeaponStats x) => 1 / (0.5f + Deg * x.spread);
            float Kick(WeaponStats x) => 1 / (0.15f + Deg * x.recoil.per);
            float Steady(WeaponStats x) => x.recoil.recover / x.recoil.max;
            float Quick(WeaponStats x) => 1 / x.reloadTime;
            string F(float v, string fmt) => v.ToString(fmt, CultureInfo.InvariantCulture);
            void Bar(string label, float v, float top, string text) => StatBar(panel, label, Mathf.Max(0.03f, v / top), text);
            Bar(w.grenade ? "Blast" : "Damage", s.damage * s.pellets, Max(x => x.damage * x.pellets), s.pellets > 1 ? $"{Mathf.Round(s.damage)}×{s.pellets}" : F(Mathf.Round(s.damage), "0"));
            Bar("Fire rate", s.fireRate, Max(x => x.fireRate), $"{F(s.fireRate, "0.0")}/s");
            Bar("Accuracy", Acc(s), Max(Acc), $"±{F(Deg * s.spread, "0.0")}°");
            Bar("Recoil", Kick(s), Max(Kick), $"{F(Deg * s.recoil.per, "0.00")}°/shot");
            Bar("Stability", Steady(s), Max(Steady), $"settles {F(s.recoil.max / s.recoil.recover, "0.00")}s");
            Bar("Range", s.range, Max(x => x.range), $"{F(s.range, "0")} m");
            Bar("Magazine", s.ammoCapacity, Max(x => x.ammoCapacity), s.ammoCapacity.ToString(CultureInfo.InvariantCulture));
            Bar("Reload", Quick(s), Max(Quick), $"{F(s.reloadTime, "0.0")}s");
            var fine = new List<string>();
            if (s.blast != null) fine.Add($"{F(s.blast.radius, "0.0")} m blast");
            if (s.pierce > 0) fine.Add($"goes through {s.pierce}");
            var fitted = Save.FittedParts(w.id);
            foreach (var slot in w.slots)
                foreach (var (t, _, special) in PartEffects(Tunables.FittedPart(w, slot, fitted))) if (special) fine.Add(t.ToLowerInvariant());
            if (fine.Count > 0) Lbl("fine", panel, string.Join(" · ", fine));

            if (!isOwned)
            {
                bool can = Save.Data.coins >= w.price;
                var buy = Quiet("primary wide", panel, b => BuyGun(w, b));
                buy.EnableInClassList("off", !can);
                buy.style.flexDirection = FlexDirection.Row;
                Lbl("", buy, "Unlock for ");
                Coins(buy, w.price);
                if (!can) Lbl("need", panel, $"{N(w.price - Save.Data.coins)} more coins needed");
                if (own) Lbl("need", panel, $"{surv.name} brings it anyway, with the parts below. Unlock it to carry it as anyone else.");
            }
            // Their own gun is always carried, and is this one: the parts
            // fitted here are on it.
            else if (own) Lbl("need", panel, $"{surv.name} always brings this one, with the parts below.");
            else
            {
                bool full = !equipped && eq.Count >= Tunables.LoadoutSlots - 1;
                var b = Quiet((equipped ? "" : "primary ") + "wide", panel, x => Equip(w.id, x), equipped ? "Unequip" : "Equip");
                b.EnableInClassList("off", full);
                if (full) Lbl("need", panel, "Loadout full. Unequip a gun to make room.");
            }
            Lbl("h4", panel, "PARTS");
            PartsList(panel, w);
        }

        // The slots along the top of the Weapons tab: the survivor's own gun,
        // then one per equipped gun, then the empty ones. Each survivor has
        // their own loadout.
        void LoadoutStrip(CharacterDef surv, List<string> eq)
        {
            var box = El("loadout", aContent);
            var extras = new List<WeaponDef>();
            foreach (var w in Tunables.Weapons) if (eq.Contains(w.id) && Save.Data.owned.Contains(w.id)) extras.Add(w);
            Lbl("small-caps", box, $"{surv.name.ToUpperInvariant()}'S LOADOUT · {extras.Count}/{Tunables.LoadoutSlots - 1} SLOTS + THEIR OWN GUN");
            var slots = El("lo-slots", box);
            void Slot(string id, string label, string cls)
            {
                var b = new Button(() => { if (selWeapon != id) openSlot = null; selWeapon = id; Sfx.Click(); RenderArmory(true); });
                b.AddToClassList("lo-slot");
                if (cls != null) b.AddToClassList(cls);
                b.Add(Icons.Gun(id, cls == "own" ? Hex.C(0xc9d2cc) : Icons.Accent));
                Lbl("lo-slot-label", b, label);
                slots.Add(b);
            }
            Slot(surv.startingWeapon, $"{surv.name}'s", "own");
            foreach (var w in extras) Slot(w.id, w.name, null);
            for (int i = extras.Count; i < Tunables.LoadoutSlots - 1; i++)
            {
                var e = El("lo-slot empty", slots);
                Lbl("lo-slot-label", e, "Empty");
            }
        }

        // One row per part slot; the open one lists what can go in it.
        void PartsList(VisualElement panel, WeaponDef w)
        {
            var g = Save.Parts(w.id);
            var fitted = Save.FittedParts(w.id);
            foreach (var slot0 in w.slots)
            {
                string slot = slot0;
                var cur = Tunables.FittedPart(w, slot, fitted);
                bool open = openSlot == slot;
                var row = new Button(() => { openSlot = openSlot == slot ? null : slot; Sfx.Click(); RenderArmory(true); });
                row.AddToClassList("slotrow");
                row.EnableInClassList("open", open);
                Lbl("slot-name", row, Tunables.PartSlots[slot].ToUpperInvariant());
                Lbl("slot-part", row, cur.name);
                Lbl("slot-arrow", row, open ? "▲" : "▼");
                panel.Add(row);
                if (!open) continue;
                var list = El("partlist", panel);
                foreach (var part0 in Tunables.PartsFor(w, slot))
                {
                    var part = part0;
                    bool has = part.price == 0 || g.owned.Contains($"{slot}:{part.id}"), on = part.id == cur.id;
                    int cost = Tunables.PartCost(w, part);
                    var pr = El("part", list);
                    pr.EnableInClassList("on", on);
                    var pt = El("pt", pr);
                    Lbl("pt-name", pt, part.name);
                    Lbl("pt-blurb", pt, string.IsNullOrEmpty(part.blurb) ? "What it comes with." : part.blurb);
                    var effects = PartEffects(part);
                    if (effects.Count > 0)
                    {
                        var fx = El("fx", pt);
                        foreach (var (t, good, _) in effects) Lbl("fx-chip " + (good ? "up" : "down"), fx, t);
                    }
                    if (on) Lbl("pill", pr, "FITTED");
                    else if (has) Quiet("small", pr, x => Fit(w, slot, part, false, x), "Fit");
                    else
                    {
                        var b = Quiet("small", pr, x => Fit(w, slot, part, true, x));
                        b.EnableInClassList("off", Save.Data.coins < cost);
                        b.style.flexDirection = FlexDirection.Row;
                        Coins(b, cost);
                    }
                }
            }
        }

        void Fit(WeaponDef w, string slot, PartDef part, bool buy, VisualElement button)
        {
            var g = Save.Parts(w.id);
            if (buy)
            {
                int cost = Tunables.PartCost(w, part);
                if (Save.Data.coins < cost) { Sfx.Denied(); Nudge(button); return; }
                Save.Data.coins -= cost;
                g.owned.Add($"{slot}:{part.id}");
                Sfx.Buy();
            }
            else Sfx.Click();
            g.fitted[slot] = part.id;
            partsDirty = true;
            Save.Persist();
            RenderArmory(true);
        }

        void BuyGun(WeaponDef w, VisualElement button)
        {
            var s = Save.Data;
            if (s.coins < w.price) { Sfx.Denied(); Nudge(button); return; }
            s.coins -= w.price;
            var eq = Save.Loadout();
            s.owned.Add(w.id);
            // Straight into your loadout (unless it's your survivor's own, which
            // they carry anyway): a free slot, or in place of the cheapest gun
            // equipped.
            if (w.id != Tunables.Character(s.character).startingWeapon)
            {
                if (eq.Count >= Tunables.LoadoutSlots - 1)
                {
                    string cheapest = eq[0];
                    foreach (var id in eq) if (Tunables.Weapon(id).price < Tunables.Weapon(cheapest).price) cheapest = id;
                    eq.Remove(cheapest);
                    Toast($"Equipped in place of the {Tunables.Weapon(cheapest).name}");
                }
                eq.Add(w.id);
            }
            Sfx.Buy();
            Save.Persist();
            RenderArmory(true);
        }

        void Equip(string id, VisualElement button)
        {
            var eq = Save.Loadout();
            if (eq.Contains(id)) eq.Remove(id);
            else if (eq.Count >= Tunables.LoadoutSlots - 1) { Sfx.Denied(); Nudge(button); return; }
            else eq.Add(id);
            Sfx.Click();
            Save.Persist();
            RenderArmory(true);
        }

        // ── Survivor upgrades ────────────────────────────────────────────
        void SurvivorTab()
        {
            var c = Tunables.Character(Save.Data.character);
            var st = Tunables.StatsFor(c, Save.Data.pup);
            string F(float v, string fmt) => v.ToString(fmt, CultureInfo.InvariantCulture);
            var now = new Dictionary<string, string>
            {
                { "maxHp", $"{F(st.maxHp, "0")} HP" }, { "speed", $"{F(st.speed, "0.0")} m/s" }, { "magnet", $"{F(st.magnet, "0.0")} m" },
            };
            var panel = El("panel", aContent);
            Lbl("blurb", panel, $"These stay with you, whichever survivor you pick. Shown for {c.name}.");
            foreach (var u0 in Tunables.PlayerUpgrades)
            {
                var u = u0;
                int lv = Save.Upgrade(u.id);
                string step = u.unit == "%" ? $"+{Mathf.RoundToInt(u.step * 100)}%" : $"+{F(u.step, "0.##")}{u.unit}";
                var row = El("uprow", panel);
                var ul = El("ul", row);
                Lbl("ul-label", ul, u.label);
                Lbl("ul-sub", ul, $"{now[u.id]} · {step} each");
                var pips = El("pips", ul);
                for (int i = 0; i < u.max; i++) El("pip" + (i < lv ? " on" : ""), pips);
                if (lv >= u.max) Lbl("pill", row, "MAX");
                else
                {
                    int cost = Tunables.PlayerUpgradeCost(u.id, lv);
                    var b = Quiet("small", row, x => Upgrade(u.id, x));
                    b.EnableInClassList("off", Save.Data.coins < cost);
                    b.style.flexDirection = FlexDirection.Row;
                    Coins(b, cost);
                }
            }
        }

        void Upgrade(string id, VisualElement button)
        {
            int lv = Save.Upgrade(id), cost = Tunables.PlayerUpgradeCost(id, lv);
            if (Save.Data.coins < cost) { Sfx.Denied(); Nudge(button); return; }
            Save.Data.pup[id] = lv + 1;
            Save.Data.coins -= cost;
            Sfx.Buy();
            Save.Persist();
            RenderArmory(true);
        }

        // What a part does, as short labelled changes: (text, good?, special?).
        // Specials are the effects that aren't a number (fire, quiet...).
        static List<(string, bool, bool)> PartEffects(PartDef part)
        {
            var m = part.mods ?? new PartMods();
            var fx = part.fx;
            var output = new List<(string, bool, bool)>();
            string Pct(float v) => $"{Mathf.RoundToInt(Mathf.Abs(v - 1) * 100)}%";
            void More(string label, float v) { if (v != 1) output.Add(($"{label} {(v > 1 ? "+" : "−")}{Pct(v)}", v > 1, false)); }
            void Less(string label, float v, string up, string down) { if (v != 1) output.Add(($"{label} {Pct(v)} {(v < 1 ? up : down)}", v < 1, false)); }
            if (m.pellets > 0) output.Add((m.pellets == 1 ? "One heavy slug" : $"{m.pellets} pellets", true, false));
            More("Damage", m.damage);
            More("Fire rate", m.fireRate);
            if (m.spread != 1) output.Add(($"Accuracy {(m.spread < 1 ? "+" : "−")}{Pct(m.spread)}", m.spread < 1, false));
            Less("Recoil", m.recoil, "less", "more");
            More("Stability", m.stability);
            More("Range", m.range);
            More("Magazine", m.ammoCapacity);
            Less("Reload", m.reloadTime, "faster", "slower");
            More("Knockback", m.knockback);
            More("Blast", m.blast);
            if (m.impulse != 1) output.Add(($"Throws {m.impulse.ToString("0.##", CultureInfo.InvariantCulture)}× harder", true, false));
            if (m.pierce > 0) output.Add(($"Through {m.pierce} more zombie{(m.pierce > 1 ? "s" : "")}", true, false));
            if (fx != null)
            {
                if (fx.noPierce) output.Add(("Never goes through", false, false));
                if (fx.fireDps > 0) output.Add(("Sets zombies alight", true, true));
                if (fx.small != 1) output.Add(($"{(fx.small > 1 ? "+" : "−")}{Pct(fx.small)} vs walkers & runners", fx.small > 1, false));
                if (fx.big != 1) output.Add(($"{(fx.big > 1 ? "+" : "−")}{Pct(fx.big)} vs tanks & bosses", fx.big > 1, false));
                if (fx.props != 1) output.Add(($"{fx.props.ToString("0.##", CultureInfo.InvariantCulture)}× damage to cover", true, false));
                if (fx.quiet) output.Add(("Quiet: zombies don't hear it", true, true));
                if (fx.cluster > 0) output.Add(($"{fx.cluster} bomblets", true, true));
                if (fx.laser) output.Add(("Laser line", true, true));
                if (fx.light) output.Add(("Bigger flashlight", true, true));
            }
            return output;
        }
    }
}
