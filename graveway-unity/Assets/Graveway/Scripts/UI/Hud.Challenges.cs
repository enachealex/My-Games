// The run's challenges on the HUD (Game.Challenges.cs): a small panel at the
// top left, under the kills, with each one's progress and what it pays, and
// the bonuses earned so far over them.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using C = Graveway.Tunables.Challenges;

namespace Graveway
{
    public sealed partial class Hud
    {
        VisualElement hChallenges;

        // "+10% damage · +5% range", or "" for none.
        static string BonusText(int[] boosts)
        {
            var parts = new List<string>();
            for (int i = 0; i < boosts.Length; i++)
                if (boosts[i] > 0) parts.Add($"+{Mathf.RoundToInt(C.Step * 100 * boosts[i])}% {C.Names[i]}");
            return string.Join(" · ", parts);
        }

        void DrawChallenges(RunStats r)
        {
            var list = game.Challenges;
            if (list == null) return;
            // Redrawn only when something on it changes.
            int key = 17;
            foreach (var c in list) key = key * 31 + (c.def.id.GetHashCode() ^ c.target * 7919 ^ Mathf.FloorToInt(c.progress) * 31 ^ c.stat);
            foreach (var b in r.boosts) key = key * 31 + b;
            if (!Changed("challenges", key)) return;
            hChallenges.Clear();
            string earned = BonusText(r.boosts);
            Lbl("hc-head", hChallenges, earned.Length > 0 ? $"CHALLENGES · {earned.ToUpperInvariant()}" : "CHALLENGES");
            foreach (var c in list)
            {
                var row = El("hc-row", hChallenges);
                var top = El("hc-top", row);
                Lbl("hc-text", top, c.Text);
                Lbl("hc-n", top, $"{Mathf.Min(Mathf.FloorToInt(c.progress), c.target)}/{c.target}");
                var bar = El("hc-bar", row);
                El("hc-fill", bar).style.width = Length.Percent(100f * Mathf.Clamp01(c.progress / c.target));
                Lbl("hc-pay", row, $"+{Mathf.RoundToInt(C.Step * 100)}% {C.Names[c.stat]}");
            }
        }
    }
}
