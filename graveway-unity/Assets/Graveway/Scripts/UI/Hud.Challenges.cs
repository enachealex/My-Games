// The run's challenges (Game.Challenges.cs): in the pause menu, under its
// buttons, with each one's progress and what it pays, and the bonuses earned
// so far over them. (They were over the run until a playtest note asked for
// the screen back.) When one's done, what it paid rises off the survivor.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using C = Graveway.Tunables.Challenges;

namespace Graveway
{
    public sealed partial class Hud
    {
        VisualElement pChallenges;

        // "+2% damage · +5% range", or "" for none.
        static string BonusText(int[] boosts)
        {
            var parts = new List<string>();
            for (int i = 0; i < boosts.Length; i++)
                if (boosts[i] > 0) parts.Add(C.Pay(i, boosts[i]));
            return string.Join(" · ", parts);
        }

        void DrawChallenges(RunStats r)
        {
            var list = game.Challenges;
            if (list == null || r == null) return;
            // Redrawn only when something on it changes.
            int key = 17;
            foreach (var c in list) key = key * 31 + (c.def.id.GetHashCode() ^ c.target * 7919 ^ Mathf.FloorToInt(c.progress) * 31 ^ c.stat);
            foreach (var b in r.boosts) key = key * 31 + b;
            if (!Changed("challenges", key)) return;
            pChallenges.Clear();
            Lbl("hc-head", pChallenges, "CHALLENGES");
            string earned = BonusText(r.boosts);
            Lbl("hc-earned", pChallenges, earned.Length > 0 ? $"So far this run: {earned}" : "Each one done adds to this run, until it ends.");
            foreach (var c in list)
            {
                var row = El("hc-row", pChallenges);
                var top = El("hc-top", row);
                Lbl("hc-text", top, c.Text);
                Lbl("hc-n", top, $"{Mathf.Min(Mathf.FloorToInt(c.progress), c.target)}/{c.target}");
                var bar = El("hc-bar", row);
                El("hc-fill", bar).style.width = Length.Percent(100f * Mathf.Clamp01(c.progress / c.target));
                Lbl("hc-pay", row, C.Pay(c.stat));
            }
        }

        // ── Words that rise off the survivor and fade ────────────────────
        sealed class Riser { public VisualElement el; public float t; }
        readonly List<Riser> risers = new List<Riser>();
        const float RiseTime = 1.8f, RiseBy = 70, RiserWidth = 280;

        // "+5% fire rate", with "Challenge done" over it. Two at once rise
        // together, one under the other, under the one heading.
        public void Rise(string text, string over = null)
        {
            var last = risers.Count > 0 ? risers[risers.Count - 1] : null;
            if (last != null && last.t < 0.4f && last.el.userData as string == over)
            {
                Lbl("riser-text", last.el, text.ToUpperInvariant());
                return;
            }
            var el = El("riser", hud);
            el.userData = over;
            if (over != null) Lbl("riser-over", el, over.ToUpperInvariant());
            Lbl("riser-text", el, text.ToUpperInvariant());
            risers.Add(new Riser { el = el });
            UpdateRisers(0);
        }

        void UpdateRisers(float dt)
        {
            if (risers.Count == 0) return;
            // Over the survivor's head, wherever they've got to: as shares of
            // the view, so it holds when the UI draws into a texture of
            // another size (the tests' pictures).
            var p = game.player;
            Vector2? at = null;
            var size = root.layout.size;
            if (p != null && game.Camera != null && size.x > 0)
            {
                var v = game.Camera.WorldToViewportPoint(game.WorldPoint(p.x, 2.4f, p.z));
                if (v.z > 0) at = new Vector2(v.x * size.x, (1 - v.y) * size.y);
            }
            for (int i = risers.Count - 1; i >= 0; i--)
            {
                var r = risers[i];
                r.t += dt;
                if (r.t >= RiseTime) { r.el.RemoveFromHierarchy(); risers.RemoveAt(i); continue; }
                float k = r.t / RiseTime;
                r.el.style.opacity = k < 0.1f ? k / 0.1f : k > 0.6f ? (1 - k) / 0.4f : 1;
                if (at == null) continue;
                r.el.style.left = at.Value.x - RiserWidth / 2;
                float h = float.IsNaN(r.el.layout.height) ? 0 : r.el.layout.height;
                r.el.style.top = at.Value.y - h - k * RiseBy;
            }
        }
    }
}
