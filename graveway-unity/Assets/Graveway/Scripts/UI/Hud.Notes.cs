// Playtest notes. A note records what was happening when you wrote it, so
// "this is too hard" arrives with the distance, survivor, gun, health and
// frame rate; "Send to Claude" opens a GitHub issue with them filled in.
// A port of that part of graveway/src/ui.js.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

namespace Graveway
{
    public sealed partial class Hud
    {
        static readonly string[] NoteTags = { "Bug", "Too hard", "Too easy", "Controls", "Camera", "Looks", "Sound", "Slow / laggy", "Idea" };

        VisualElement note, notesModal;
        Label nContext, nsCount;
        TextField nText, nsText;
        Button nSave, nsSend, nsCopy, nsClear;
        readonly HashSet<string> noteTags = new HashSet<string>();
        readonly List<Button> tagButtons = new List<Button>();
        string noteCtx = "";
        bool clearArmed;

        void BuildNotes()
        {
            note = Modal(out var nc);
            Lbl("card-title", nc, "NOTE");
            nContext = Lbl("card-sub ctx", nc);
            var chips = El("chips", nc);
            foreach (var tag0 in NoteTags)
            {
                string tag = tag0;
                Button b = null;
                b = new Button(() =>
                {
                    Sfx.Click();
                    if (!noteTags.Remove(tag)) noteTags.Add(tag);
                    b.EnableInClassList("on", noteTags.Contains(tag));
                }) { text = tag };
                b.AddToClassList("chip-btn");
                chips.Add(b);
                tagButtons.Add(b);
            }
            nText = new TextField { multiline = true };
            nText.AddToClassList("note-text");
            nText.textEdition.placeholder = "What should change?";
            nc.Add(nText);
            var row = El("row-btns", nc);
            Btn("Cancel", "", row, CloseNote);
            nSave = Quiet("primary", row, _ => SaveNote(), "Save note");

            notesModal = Modal(out var sc, scroll: true);
            Lbl("card-title", sc, "PLAYTEST NOTES");
            nsCount = Lbl("card-sub", sc);
            nsText = new TextField { multiline = true, isReadOnly = true };
            nsText.AddToClassList("notes-text");
            nsText.verticalScrollerVisibility = ScrollerVisibility.Auto;
            sc.Add(nsText);
            nsSend = Btn("Send to Claude", "primary wide", sc, SendNotes);
            Lbl("fine", sc, "Opens a GitHub issue with your notes filled in. Tap Submit and Claude can read them.");
            var r2 = El("row-btns", sc);
            r2.style.marginTop = 10;
            nsClear = Btn("Delete all", "", r2, ClearNotes);
            nsCopy = Btn("Copy all", "", r2, CopyNotes);
            Btn("Done", "wide", sc, CloseNotes);
        }

        void OpenNote()
        {
            noteCtx = NoteContext();
            nContext.text = noteCtx;
            nText.value = "";
            noteTags.Clear();
            foreach (var b in tagButtons) b.EnableInClassList("on", false);
            Shown(note, true);
            nText.Focus();
        }

        void CloseNote()
        {
            Shown(note, false);
            nText.Blur();
        }

        void SaveNote()
        {
            string text = nText.value.Trim();
            if (text.Length == 0 && noteTags.Count == 0) { Sfx.Denied(); Nudge(nSave); return; }
            Sfx.Click();
            Save.Notes.Add(new Note { at = IsoNow(), tags = NoteTags.Where(noteTags.Contains).ToList(), text = text, ctx = noteCtx });
            Save.PersistNotes();
            CloseNote();
            int unsent = Save.Notes.Count(n => n.sent == null);
            Toast($"Note saved · {unsent} to send (Settings → Playtest notes)");
        }

        string NoteContext()
        {
            var g = game;
            string fps = $"{Mathf.RoundToInt(g.fps)} fps";
            if (IsShown(result) && hasResult)
            {
                var r = lastResult;
                return $"After a run: {r.character}, {r.distance} m (stage {r.stage + 1}) in {Mathf.RoundToInt(r.time)}s, {r.kills} kills · {LoadoutText()} · {fps}";
            }
            if (g.run != null && g.state != Game.State.Menu)
            {
                var p = g.player;
                string armor = p.armor > 0 ? $" + {Mathf.CeilToInt(p.armor)} armor" : "";
                return $"{g.Character.name} · {Mathf.FloorToInt(g.Distance)} m (stage {g.Stage + 1}) · {g.Weapon.Name} · {Mathf.RoundToInt(g.run.time)}s in · " +
                    $"{Mathf.CeilToInt(p.hp)}/{Mathf.RoundToInt(p.maxHp)} HP{armor} · {g.run.kills} kills · {g.AliveCount()} zombies up · {fps}";
            }
            string where = current == "armory" ? "Armory" : current == "select" ? "Survivor select" : "Menu";
            return $"{where} · {LoadoutText()} · {fps}";
        }

        // Every gun you have, with the parts that aren't what it came with,
        // e.g. "SMG[long,reddot]*" (* equipped).
        static string LoadoutText()
        {
            var s = Save.Data;
            var eq = Save.Loadout();
            var guns = new List<string>();
            foreach (var w in Tunables.Weapons)
            {
                if (w.price != 0 && !s.owned.Contains(w.id)) continue;
                var f = Save.FittedParts(w.id);
                var extra = new List<string>();
                foreach (var slot in w.slots)
                {
                    var part = Tunables.FittedPart(w, slot, f);
                    if (part != null && part.price > 0) extra.Add(part.id);
                }
                guns.Add(w.name + (extra.Count > 0 ? "[" + string.Join(",", extra) + "]" : "") + (eq.Contains(w.id) ? "*" : ""));
            }
            return $"{s.coins} coins · best {s.best} m · guns (*equipped): {string.Join(", ", guns)}";
        }

        static string IsoNow() => DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

        // "21 Oct, 14:05", in local time.
        static string When(string iso)
        {
            if (DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var t))
                return t.ToLocalTime().ToString("d MMM, HH:mm", CultureInfo.InvariantCulture);
            return iso;
        }

        static string Plural(int n, string word) => $"{n} {word}{(n == 1 ? "" : "s")}";
        static string NoteLine(Note n, string tagOpen, string tagClose) =>
            (n.tags.Count > 0 ? tagOpen + string.Join(", ", n.tags) + tagClose + " " : "") + (string.IsNullOrEmpty(n.text) ? "(no comment)" : n.text);

        static string NotesText()
        {
            var notes = Save.Notes;
            var sb = new StringBuilder($"Graveway playtest notes\n{BuildLabel} · screen {Screen.width}×{Screen.height} · {Plural(notes.Count, "note")}\n");
            for (int i = 0; i < notes.Count; i++)
            {
                var n = notes[i];
                if (i > 0) sb.Append('\n');
                sb.Append($"\n{i + 1}. {NoteLine(n, "[", "]")}\n   {n.ctx}\n   {When(n.at)}{(n.sent != null ? " · sent" : "")}");
            }
            return sb.ToString();
        }

        void OpenNotes()
        {
            clearArmed = false;
            var notes = Save.Notes;
            int unsent = notes.Count(n => n.sent == null);
            bool any = notes.Count > 0;
            nsClear.text = "Delete all";
            nsClear.EnableInClassList("armed", false);
            nsClear.SetEnabled(any);
            nsCopy.SetEnabled(any);
            nsSend.SetEnabled(any);
            nsSend.text = unsent > 0 || !any ? "Send to Claude" : "Send all again";
            nsText.value = any ? NotesText() : "No notes yet. While playing, pause and tap \"Leave a note\".";
            string state = unsent == 0 ? " · all sent" : unsent < notes.Count ? $" · {unsent} not sent yet" : "";
            nsCount.text = any ? Plural(notes.Count, "note") + state : "";
            Shown(notesModal, true);
        }

        void CloseNotes()
        {
            Shown(notesModal, false);
            DrawSettings();
        }

        void ClearNotes()
        {
            if (!clearArmed)
            {
                clearArmed = true;
                nsClear.text = "Tap again to delete";
                nsClear.EnableInClassList("armed", true);
                return;
            }
            Save.Notes.Clear();
            Save.PersistNotes();
            OpenNotes();
        }

        void CopyNotes()
        {
            GUIUtility.systemCopyBuffer = NotesText();
            Toast("Copied — paste it to Claude");
        }

        // Opens a new GitHub issue on FeedbackRepo with the notes filled in,
        // so all that's left is tapping Submit. Notes not sent before go
        // first; if they've all been sent, they all go again. A URL can only
        // carry so much, so a long backlog goes over several sends.
        void SendNotes()
        {
            var notes = Save.Notes;
            var unsent = notes.Where(n => n.sent == null).ToList();
            var pool = unsent.Count > 0 ? unsent : notes.ToList();
            if (pool.Count == 0) return;
            string issues = $"https://github.com/{FeedbackRepo}/issues/new?";
            (string body, string url) Build(List<Note> list, int left)
            {
                string title = $"Playtest notes: {list.Count} from {BuildLabel}";
                string body = IssueBody(list, left);
                return (body, $"{issues}title={Uri.EscapeDataString(title)}&body={Uri.EscapeDataString(body)}");
            }
            int count = pool.Count;
            var output = Build(pool, 0);
            while (output.url.Length > 7000 && count > 1) { count--; output = Build(pool.GetRange(0, count), pool.Count - count); }
            if (output.url.Length > 7000)
            {
                // One enormous note: send the start of it.
                var first = pool[0];
                var one = new Note { at = first.at, tags = first.tags, ctx = first.ctx, text = first.text.Substring(0, Mathf.Min(2500, first.text.Length)) + " …(cut short)" };
                output = Build(new List<Note> { one }, pool.Count - 1);
            }
            string at = IsoNow();
            for (int i = 0; i < count; i++) pool[i].sent = at;
            Save.PersistNotes();
            // A copy on the clipboard too, in case the page opens with an empty form.
            GUIUtility.systemCopyBuffer = output.body;
            Application.OpenURL(output.url);
            Toast(pool.Count > count ? $"Sending {count} of {pool.Count} · send again for the rest" : "Tap Submit on GitHub to send it");
            OpenNotes();
            RenderMenu();
        }

        static string IssueBody(List<Note> list, int left)
        {
            var lines = new List<string>
            {
                "<!-- graveway-playtest -->",
                $"**Build:** {BuildLabel} · screen {Screen.width}×{Screen.height}",
                $"**Sent:** {When(IsoNow())}",
                "",
            };
            for (int i = 0; i < list.Count; i++)
            {
                var n = list[i];
                lines.Add($"{i + 1}. {NoteLine(n, "**[", "]**")}");
                lines.Add($"   _{n.ctx}_");
                lines.Add($"   _{When(n.at)}_");
            }
            if (left > 0)
            {
                lines.Add("");
                lines.Add($"_{Plural(left, "more note")} didn't fit. They'll come in the next send._");
            }
            return string.Join("\n", lines);
        }

        // ── Shared by the Armory and the notes ───────────────────────────
        // A button that makes its own sound: buying plays the till, a refusal
        // the buzz with a nudge.
        static Button Quiet(string cls, VisualElement parent, Action<Button> onClick, string text = "")
        {
            Button b = null;
            b = new Button(() => onClick(b)) { text = text };
            b.AddToClassList("btn");
            foreach (var c in cls.Split(' ')) if (c.Length > 0) b.AddToClassList(c);
            parent?.Add(b);
            return b;
        }

        // A little side-to-side shake: "can't do that".
        static void Nudge(VisualElement v)
        {
            float t0 = Time.unscaledTime;
            IVisualElementScheduledItem item = null;
            item = v.schedule.Execute(() =>
            {
                float t = (Time.unscaledTime - t0) / 0.3f;
                if (t >= 1) { v.style.translate = StyleKeyword.Null; item.Pause(); return; }
                v.style.translate = new Translate(Mathf.Sin(t * Mathf.PI * 4) * 5, 0);
            }).Every(16);
        }
    }
}
