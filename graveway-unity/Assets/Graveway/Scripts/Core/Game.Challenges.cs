// Challenges in a run (a player's idea, PORT_PLAN.md note 19): three at a
// time, each saying up front what it pays: +1% damage, or +5% accuracy, range
// or rate of fire, for the rest of the run. Done, it pays (the words rise off
// the survivor), and another takes its place; a kind done again in the same
// run asks twice as much. The bonuses stack, and the next run starts at
// normal. They're in the pause menu, not over the run.

using System.Collections.Generic;
using UnityEngine;
using C = Graveway.Tunables.Challenges;

namespace Graveway
{
    public sealed class Challenge
    {
        public ChallengeDef def;
        public int stat;                     // into C.Stats: what it pays
        public int target;
        public float progress;
        public float from, fromZ;            // when it was offered: the distance, the survivor's furthest z
        public int sinceKill;                // and the run's kills by then
        public string Text => C.Describe(def.id, target);
    }

    public sealed partial class Game
    {
        // The bonus to one of C.Stats from challenges done this run, and the
        // hat being worn (1 = none).
        public float Bonus(int stat) => run == null ? 1 : 1 + C.Steps[stat] * run.boosts[stat] + Tunables.HatBoost(run.hat, stat);

        // The accuracy bonus as a spread: tighter by the same share.
        float SpreadBonus => run == null ? 1 : Mathf.Max(0.4f, 2 - Bonus(C.Accuracy));

        public IReadOnlyList<Challenge> Challenges => run?.challenges;

        void StartChallenges()
        {
            run.challengeRng = new Rng(seed ^ 0x9e3779b9u);
            for (int i = 0; i < C.Active; i++) run.challenges.Add(Offer());
            run.untouchedFrom = player.furthest;
        }

        // A kind not already on, its target grown by how often it's been done.
        Challenge Offer()
        {
            var open = new List<ChallengeDef>();
            foreach (var k in C.Kinds) if (!run.challenges.Exists(c => c.def.id == k.id)) open.Add(k);
            var def = open[(int)(run.challengeRng.Next() * open.Count)];
            run.repeats.TryGetValue(def.id, out int done);
            return New(def, C.Target(def, done));
        }

        // Counting from now: nothing done before it was offered counts for it.
        Challenge New(ChallengeDef def, int target) => new Challenge
        {
            def = def, stat = run.statTurn++ % C.Stats.Length, target = target,
            from = Distance, fromZ = player.furthest, sinceKill = run.kills,
        };

        // For tests: these challenges on, in place of whatever was.
        public void SetChallenges(params string[] ids)
        {
            run.challenges.Clear();
            foreach (var id in ids)
            {
                var def = System.Array.Find(C.Kinds, k => k.id == id);
                run.challenges.Add(New(def, C.Target(def, 0)));
            }
        }

        // Something done that counts towards a kind: by `by`, or set to `to`.
        void Count(string id, float by = 1, float to = -1)
        {
            if (run == null || state != State.Playing) return;
            for (int i = 0; i < run.challenges.Count; i++)
            {
                var c = run.challenges[i];
                if (c.def.id != id) continue;
                c.progress = to >= 0 ? to : c.progress + by;
                if (c.progress >= c.target) Complete(i);
            }
        }

        void Complete(int i)
        {
            var c = run.challenges[i];
            run.boosts[c.stat]++;
            run.challengesDone++;
            run.repeats[c.def.id] = run.repeats.TryGetValue(c.def.id, out int n) ? n + 1 : 1;
            run.challenges[i] = Offer();
            hud.Rise(C.Pay(c.stat), "Challenge done");
            Sfx.Milestone();
            Haptics.Vibrate(25);
        }

        // Distance ones, each frame: from when each was offered (or, without a
        // scratch, the last hit since).
        void UpdateChallenges()
        {
            if (run == null) return;
            foreach (var c in run.challenges.ToArray())
            {
                if (c.def.id == "distance") Count("distance", to: Distance - c.from);
                if (c.def.id == "untouched") Count("untouched", to: Mathf.Max(0, Mathf.Min(run.untouchedFrom, c.fromZ) - player.furthest));
            }
        }

        // ── What counts ──────────────────────────────────────────────────
        void ChallengeKill(Zombie z, bool blast)
        {
            Count("kills");
            if (z.kind == "runner") Count("runners");
            if (z.kind == "tank") Count("tanks");
            if (blast) Count("blasts");
            // The kills in the last few seconds (as time and which kill it was),
            // and of those, the ones since each streak challenge was offered.
            var log = run.killLog;
            log.Add(new Vector2(run.time, run.kills));
            while (log.Count > 0 && run.time - log[0].x > C.StreakSeconds) log.RemoveAt(0);
            foreach (var c in run.challenges.ToArray())
                if (c.def.id == "streak") Count("streak", to: log.FindAll(k => k.y > c.sinceKill).Count);
        }

        void ChallengeHurt()
        {
            if (run != null) run.untouchedFrom = player.furthest;
        }

        // A prop wrecked by the survivor's doing (not a zombie's).
        public void ChallengeWreck(string cause)
        {
            if (cause != "smash") Count("wrecks");
        }
    }
}
