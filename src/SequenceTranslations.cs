using System;
using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace CompoundTranslations
{
    // Translations for runs of consecutive glyphs within one sentence. While a glyph's label is
    // being edited, shift+clicking another label in the same sentence selects both glyphs and
    // everything between them, and opens one label for the whole run. Every occurrence of the
    // same run is bracketed, its glyphs' own labels are hidden, and the run's translation is shown
    // centered below it. Clearing a run's translation removes it.
    //
    // A sentence is the glyphs one Draw.Write call placed on one source line, which may wrap
    // across rows. Lines that list separate glyphs are not sentences (IsList). A run is
    // identified by its glyph IDs in order. It cannot span a space, counted across wraps as
    // Write laid them out (AddGaps). Translations are stored in Global.savedata.dict under
    // KeyPrefix + the run, so they are saved with the game's save slot. The game only reads
    // that dictionary by word, never enumerates it.
    internal static class SequenceTranslations
    {
        internal const string KeyPrefix = "compoundtranslations.sequence:";
        // The token of an empty cell. Older versions saved runs with it, one per space; those
        // runs are never matched now, and stay in the save untouched.
        private const string Gap = ".";

        private sealed class Cell
        {
            public AlienCharacter Ac;
            // A glyph ID, Gap for an empty cell, or null for a cell that cannot be part of a run.
            public string Token;
            public int X;
            public int Y;
            // The glyph's position as Write numbered it, for a glyph cell.
            public int LineX;
            public int LineY;
        }

        // One Draw.Write call's layout. Its rows run from column Left to Left + Width - 1,
        // relative to its origin X; a continued row starts Indent cells in.
        private sealed class WriteCall
        {
            public string Sector;
            public int X;
            public int Y;
            public int Left;
            public int Width;
            public int Indent;
        }

        // Where a Write call put a glyph. The game reuses glyph objects, so the record holds
        // only while the glyph still shows what that call drew.
        private sealed class Placement
        {
            public WriteCall Call;
            public string Word;
            public int Line;
            public int LineX;
            public int LineY;
            // On a line that lists separate glyphs, not a sentence (IsList).
            public bool List;

            public bool Current(AlienCharacter ac) => ac.alien_txt == Word && ac.sector == Call.Sector && ac.line == Line && ac.line_x == LineX && ac.line_y == LineY;
        }

        private sealed class Sentence
        {
            // Stays the same when the page is redrawn: the section, the call's origin, and the
            // line number within the call.
            public (string Sector, int X, int Y, int Line) Id;
            public WriteCall Call;
            public readonly List<Cell> Cells = new List<Cell>();
        }

        private sealed class Occurrence
        {
            public Sentence Sentence;
            public int Start;
            public int End;
            public string Key;

            public bool Same(Occurrence o) => o != null && o.Sentence.Id == Sentence.Id
                && o.Start == Start && o.End == End && o.Key == Key;
        }

        private static readonly Dictionary<AlienCharacter, Placement> placements = new Dictionary<AlienCharacter, Placement>();

        private sealed class Label
        {
            public TMP_InputField Field;
            public Occurrence Occ;
            public readonly List<LineRenderer> Lines = new List<LineRenderer>();
        }

        private static readonly List<Sentence> sentences = new List<Sentence>();
        private static readonly Dictionary<AlienCharacter, (Sentence Sentence, int Index)> cellOf = new Dictionary<AlienCharacter, (Sentence, int)>();
        private static readonly List<Occurrence> occurrences = new List<Occurrence>();
        private static Dictionary<AlienCharacter, Occurrence> covering = new Dictionary<AlienCharacter, Occurrence>();
        private static readonly Dictionary<TMP_InputField, AlienCharacter> glyphOf = new Dictionary<TMP_InputField, AlienCharacter>();
        private static readonly List<Label> labels = new List<Label>();

        // The glyph grid changed since the last scan, so scan data is stale.
        private static bool dirty = true;
        // Translations or settings changed; the scan is still current.
        private static bool needsRender = true;
        private static bool lastVisible;
        // A run being edited that has no saved translation yet.
        private static Occurrence pending;
        // The cells of a run whose glyph the mouse is over, shown as individual labels.
        private static Occurrence revealed;
        private static Label editing;
        // A run whose label takes the selection in the next LateUpdate, from the glyph inside it
        // that has it now.
        private static Occurrence deferredRun;
        private static AlienCharacter deferredFrom;
        private static bool loggedError;

        // The selection whose edit ended most recently, for extending it with shift+click.
        private static Sentence lastSentence;
        private static int lastStart;
        private static int lastEnd;
        private static int lastFrame = -1;

        private static bool Shift => Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

        // A shift+click: Shift held while the mouse was pressed this frame. Tab with Shift held
        // also moves the selection to the next label, which must not start a run.
        private static bool ShiftClick => Shift && Input.GetMouseButtonDown(0);

        private static bool Enabled => Plugin.Sequences.Value;

        internal static void MarkDirty() => dirty = true;

        internal static void MarkChanged() => needsRender = true;

        internal static void Tick()
        {
            try
            {
                if (deferredRun != null)
                {
                    var occ = deferredRun;
                    deferredRun = null;
                    if (EventSystem.current?.currentSelectedGameObject == deferredFrom?.txt?.gameObject)
                    {
                        OpenRun(occ);
                    }
                    // Its label was spared from hiding while it was selected.
                    deferredFrom?.UpdateEarthText();
                    deferredFrom = null;
                }
                if (Plugin.Visible != lastVisible)
                {
                    needsRender = true;
                }
                // A label activated again without being selected anew, as by a click after
                // Enter, starts an edit without onSelect.
                foreach (var l in labels)
                {
                    if (l.Occ != null && l.Field != null && l.Field.isFocused && editing != l)
                    {
                        OnLabelSelect(l);
                    }
                }
                TabFromLabel();
                // Rescan before deciding what to reveal, so a redraw does not drop the reveal
                // for a frame.
                if (dirty)
                {
                    Refresh();
                }
                UpdateReveal();
                if (needsRender)
                {
                    Refresh();
                }
                PositionLabels();
            }
            catch (Exception e)
            {
                if (!loggedError)
                {
                    Plugin.Log.LogError($"Sequence translations failed: {e}");
                    loggedError = true;
                }
            }
        }

        // UpdateEarthText postfix: keep the labels of glyphs inside a run hidden.
        internal static void AfterUpdateEarthText(AlienCharacter ac)
        {
            if (!dirty && ac != null && covering.ContainsKey(ac))
            {
                SetHidden(ac, true);
            }
        }

        // Hides or shows a glyph's own label without touching its text. The game saves the
        // field's text as the glyph's translation whenever an edit of it ends, so clearing the
        // text to hide it could erase the translation.
        private static void SetHidden(AlienCharacter ac, bool hidden)
        {
            var text = ac?.txt?.textComponent;
            if (text == null)
            {
                return;
            }
            // A label being edited, or selected and about to be, stays visible.
            if (hidden && ModFields.InUse(ac.txt))
            {
                hidden = false;
            }
            if (text.enabled == hidden)
            {
                text.enabled = !hidden;
            }
        }

        // While the mouse is over a glyph inside a run, that run is set aside so the glyphs'
        // own labels show. It stays set aside while the mouse is over any glyph it covered.
        private static void UpdateReveal()
        {
            Occurrence next = null;
            if (Plugin.RevealOnHover.Value && Enabled && Plugin.Visible && editing == null && pending == null && !labels.Exists(l => ModFields.InUse(l.Field)))
            {
                var ac = Plugin.GlyphUnderMouse();
                if (ac != null && cellOf.TryGetValue(ac, out var at))
                {
                    if (revealed != null && revealed.Sentence == at.Sentence && at.Index >= revealed.Start && at.Index <= revealed.End)
                    {
                        next = revealed;
                    }
                    else if (covering.TryGetValue(ac, out var occ))
                    {
                        next = occ;
                    }
                }
            }
            if (next != revealed)
            {
                revealed = next;
                needsRender = true;
            }
        }

        // TMP_InputField.OnPointerDown prefix. A press on a glyph's label or a run's label that
        // opens a run selects the run's label instead, before the pressed field is selected, and
        // returns false. The pressed field is never selected or activated, so it never ends an
        // edit either.
        internal static bool BeforePointerDown(TMP_InputField field, PointerEventData e)
        {
            // While translations are hidden, no run labels are drawn to edit.
            if (!Enabled || !Plugin.Visible || field == null || e == null || e.button != PointerEventData.InputButton.Left)
            {
                return true;
            }
            if (dirty)
            {
                Refresh();
            }
            var occ = RunFor(field);
            if (occ == null)
            {
                return true;
            }
            // The click that follows the press would activate the pressed field.
            e.eligibleForClick = false;
            OpenRun(occ);
            return false;
        }

        // The run a press on the field edits in place of the field's own: for a shift+click, the
        // selection whose edit just ended extended to the field's glyph or run, when that can be
        // a run; otherwise, for a glyph's label, the run that covers the glyph. A selection that
        // cannot be a run, as one across a space, is not made: the press edits the pressed glyph,
        // or the run that covers it, and a new selection starts there.
        private static Occurrence RunFor(TMP_InputField field)
        {
            var label = labels.Find(l => l.Field == field && l.Occ != null);
            Occurrence cov = null;
            Sentence s;
            int index;
            if (label != null)
            {
                s = label.Occ.Sentence;
                index = label.Occ.Start;
                cov = label.Occ;
            }
            else if (glyphOf.TryGetValue(field, out var ac) && cellOf.TryGetValue(ac, out var at))
            {
                s = at.Sentence;
                index = at.Index;
                covering.TryGetValue(ac, out cov);
            }
            else
            {
                return null;
            }
            if (ShiftClick && lastFrame == Time.frameCount && lastSentence == s)
            {
                var extended = Creatable(s, Math.Min(lastStart, cov?.Start ?? index), Math.Max(lastEnd, cov?.End ?? index));
                if (extended != null && !extended.Same(label?.Occ))
                {
                    return extended;
                }
            }
            return label == null ? cov : null;
        }

        // StartEdit prefix. A glyph inside a run that is selected by Tab or the arrow keys hands
        // the selection to the run's label. Its own label is not activated, so it never ends an
        // edit. Returns false to skip the game's start of an edit.
        internal static bool BeforeStartEdit(AlienCharacter ac)
        {
            if (ac?.txt == null)
            {
                return true;
            }
            if (Enabled && Plugin.Visible && dirty)
            {
                Refresh();
            }
            var cov = Enabled && Plugin.Visible && covering.TryGetValue(ac, out var c) ? c : null;
            // TMP_InputField.OnSelect reads this after its onSelect listeners, StartEdit among
            // them, have run.
            ac.txt.shouldActivateOnSelect = cov == null;
            if (cov == null)
            {
                return true;
            }
            // Selecting another object from inside onSelect is refused by the EventSystem.
            deferredRun = cov;
            deferredFrom = ac;
            return false;
        }

        // EndEdit postfix for a glyph's own label.
        internal static void AfterGlyphEndEdit(AlienCharacter ac)
        {
            if (ac != null && cellOf.TryGetValue(ac, out var at))
            {
                Remember(at.Sentence, at.Index, at.Index);
            }
            // The new translation may be "and", "or", or "to", which changes where runs match.
            needsRender = true;
        }

        private static void Remember(Sentence s, int start, int end)
        {
            lastSentence = s;
            lastStart = start;
            lastEnd = end;
            lastFrame = Time.frameCount;
        }

        private static Occurrence MakeOccurrence(Sentence s, int start, int end)
        {
            if (end <= start || start < 0 || end >= s.Cells.Count)
            {
                return null;
            }
            var tokens = new string[end - start + 1];
            for (var i = start; i <= end; i++)
            {
                var t = s.Cells[i].Token;
                if (t == null || t == Gap)
                {
                    return null;
                }
                tokens[i - start] = t;
            }
            return new Occurrence { Sentence = s, Start = start, End = end, Key = KeyPrefix + string.Join(" ", tokens) };
        }

        // A new run, unless Match would never show it there, as where a word in it belongs to
        // both sides of a conjunction (SharesWord): it would be saved and vanish at once.
        private static Occurrence Creatable(Sentence s, int start, int end)
        {
            var occ = MakeOccurrence(s, start, end);
            return occ != null && !SharesWord(s, occ.Start, occ.End) ? occ : null;
        }

        // Tab from a run's label, which the game's Tab handler does not know, moves on to the
        // next label or position field after the run, or before it for a backward Tab (TabOrder).
        private static void TabFromLabel()
        {
            if (!TabOrder.Pressed(out var selected))
            {
                return;
            }
            var label = labels.Find(l => l.Occ != null && l.Field != null && selected == l.Field.gameObject);
            if (label == null)
            {
                return;
            }
            // Backward, the run's stop is its first glyph's, so the label before it comes next.
            var end = label.Occ.Sentence.Cells[TabOrder.Backward() ? label.Occ.Start : label.Occ.End];
            TabOrder.Select(TabOrder.Next(new Vector2Int(end.X, end.Y), false));
        }

        // Makes the run the one being edited and selects its label, which activates it.
        private static void OpenRun(Occurrence occ)
        {
            pending = occ;
            Refresh();
            var target = labels.Find(l => l.Occ != null && l.Occ.Same(pending) && l.Field.gameObject.activeSelf);
            if (target == null)
            {
                pending = null;
                needsRender = true;
                return;
            }
            EventSystem.current?.SetSelectedGameObject(target.Field.gameObject);
        }

        private static string Translation(string key) => SavedValues.Get(key) ?? "";

        private static void Refresh()
        {
            if (dirty)
            {
                Scan();
            }
            Match();
            Render();
            dirty = false;
            needsRender = false;
            lastVisible = Plugin.Visible;
        }

        // Draw.Write postfix: record the call's layout and which glyphs it drew.
        internal static void AfterWrite(Draw draw, List<string> s, int maxx, int xx, int yy, bool isLeft, string sec, bool nospacing, List<AlienCharacter> drawn)
        {
            if (drawn == null || s == null)
            {
                return;
            }
            // Write starts every row of a right-aligned call at minus the most glyphs on any
            // line, counting every token but newlines and those starting with @.
            var widest = 0;
            var count = 0;
            foreach (var token in s)
            {
                if (token == "newline")
                {
                    count = 0;
                }
                else if (!token.StartsWith("@", StringComparison.Ordinal))
                {
                    count++;
                }
                widest = Math.Max(widest, count);
            }
            var call = new WriteCall
            {
                Sector = sec,
                X = xx,
                Y = yy,
                Left = isLeft ? 0 : -widest,
                Width = maxx,
                Indent = nospacing ? 0 : 1,
            };
            foreach (var ac in drawn)
            {
                if (ac != null)
                {
                    placements[ac] = new Placement { Call = call, Word = ac.alien_txt, Line = ac.line, LineX = ac.line_x, LineY = ac.line_y, List = IsList(draw, sec, ac.line) };
                }
            }
        }

        private static readonly Func<Draw, bool> IsEditNote = AccessTools.MethodDelegate<Func<Draw, bool>>(AccessTools.Method(typeof(Draw), "IsEditNote"));

        private static readonly AccessTools.FieldRef<Draw, int> UnderstandLevel = AccessTools.FieldRefAccess<Draw, int>("understand_lv");

        // Lines that list glyphs side by side as separate items, not words of a sentence: each
        // panel's row of buttons, on every page; on the dictionary page and in the note
        // editor, the row of category glyphs and the grid of a category's words; on a quiz page,
        // the row of glyphs it offers; in the minigames, the rows of moves, choices, cards, and
        // board cells. Write draws each as one line, sometimes among lines of sentences in the
        // same call. These are the glyphs the game makes clickable one by one on those pages.
        private static bool IsList(Draw draw, string sec, int line)
        {
            if (sec == "leftmenu" || sec == "rightmenu")
            {
                return true;
            }
            var page = draw?.current_page;
            if (page == null)
            {
                return false;
            }
            if (page == "language de book" || IsEditNote(draw))
            {
                return sec == "righttextup" || sec == "righttextdown";
            }
            switch (page)
            {
                // The moves, each followed by its cost.
                case "hero de arena game":
                // The actions, then the choices or the items.
                case "explorer by word":
                // The draw and stop buttons.
                case "no pair game":
                    return sec == "righttextdown";
                // A test's row of digits and buttons, or of answers.
                case "understand rule":
                    return sec == "righttextup" && UnderstandLevel(draw) != 0;
                // The board.
                case "2e1 detrap de variant":
                    return sec == "righttextdown" && !string.IsNullOrEmpty(MV.current_variant);
                // The rows of numbers and cards, above a line of text.
                case "1e1 de sequence":
                    return sec == "righttextup" && line <= 1;
                // The rows of cards, around two lines of text.
                case "1e1 planet card":
                    return sec == "righttextdown" && line != 2 && line != 5;
            }
            return sec == "righttextup" && Global.quiz_supply != null && Global.quiz_supply.ContainsKey(page);
        }

        private static void Scan()
        {
            sentences.Clear();
            cellOf.Clear();
            glyphOf.Clear();
            var slots = Global.manager?.alien_slots;
            if (slots == null)
            {
                return;
            }
            var bySentence = new Dictionary<(WriteCall, int), Sentence>();
            var isOption = SettingOptions.For(Global.manager);
            // Rows are filled top to bottom, which is decreasing y, and left to right.
            for (var y = slots.GetLength(1) - 1; y >= 0; y--)
            {
                for (var x = 0; x < slots.GetLength(0); x++)
                {
                    var ac = slots[x, y];
                    if (ac == null || !ac.gameObject.activeInHierarchy || !placements.TryGetValue(ac, out var p) || !p.Current(ac) || p.List)
                    {
                        continue;
                    }
                    var k = (p.Call, p.Line);
                    if (!bySentence.TryGetValue(k, out var s))
                    {
                        s = new Sentence { Id = (p.Call.Sector, p.Call.X, p.Call.Y, p.Line), Call = p.Call };
                        bySentence[k] = s;
                        sentences.Add(s);
                    }
                    var last = s.Cells.Count > 0 ? s.Cells[s.Cells.Count - 1] : null;
                    if (last != null)
                    {
                        AddGaps(s, last, p, x, y);
                    }
                    // A settings page's option glyph keeps its cell but never joins a run.
                    var eligible = ac.sp != null && ac.sp.enabled && ac.showtxt && ac.txt != null && !string.IsNullOrEmpty(ac.alien_txt) && !(isOption?.Invoke(ac) ?? false);
                    cellOf[ac] = (s, s.Cells.Count);
                    if (ac.txt != null)
                    {
                        glyphOf[ac.txt] = ac;
                    }
                    s.Cells.Add(new Cell { Ac = ac, Token = eligible ? ac.alien_txt : null, X = x, Y = y, LineX = p.LineX, LineY = p.LineY });
                }
            }
        }

        // Adds the empty cells between the sentence's last glyph and the next one at (x, y).
        // Write leaves a cell empty only for a space token, and wraps only when a row is full,
        // so every empty cell from the last glyph to the end of its row, on any row between,
        // and from the start of the next glyph's row (after the indent) is a space in the text.
        // A space at the end of a row shows no gap, and the indent is not a space.
        private static void AddGaps(Sentence s, Cell last, Placement next, int x, int y)
        {
            var call = s.Call;
            var first = call.Left + call.Indent;
            var end = call.Left + call.Width - 1;
            var origin = last.X - last.LineX;
            if (last.LineY == next.LineY)
            {
                for (var gx = last.X + 1; gx < x; gx++)
                {
                    s.Cells.Add(new Cell { Token = Gap, X = gx, Y = y });
                }
                return;
            }
            for (var lx = last.LineX + 1; lx <= end; lx++)
            {
                s.Cells.Add(new Cell { Token = Gap, X = origin + lx, Y = last.Y });
            }
            for (var gy = last.Y - 1; gy > y; gy--)
            {
                for (var lx = first; lx <= end; lx++)
                {
                    s.Cells.Add(new Cell { Token = Gap, X = origin + lx, Y = gy });
                }
            }
            for (var lx = first; lx < next.LineX; lx++)
            {
                s.Cells.Add(new Cell { Token = Gap, X = origin + lx, Y = y });
            }
        }

        // Re-find an occurrence from an earlier scan in the current one.
        private static Occurrence Rebind(Occurrence o)
        {
            if (o == null)
            {
                return null;
            }
            foreach (var s in sentences)
            {
                if (s.Id == o.Sentence.Id)
                {
                    var n = MakeOccurrence(s, o.Start, o.End);
                    return n != null && n.Key == o.Key ? n : null;
                }
            }
            return null;
        }

        private static Dictionary<string, List<string[]>> LoadRuns()
        {
            var runs = new Dictionary<string, List<string[]>>();
            var dict = Global.savedata?.dict;
            if (dict == null)
            {
                return runs;
            }
            foreach (var kv in dict)
            {
                if (!kv.Key.StartsWith(KeyPrefix, StringComparison.Ordinal) || SavedValues.IsBlank(kv.Value))
                {
                    continue;
                }
                var tokens = kv.Key.Substring(KeyPrefix.Length).Split(' ');
                if (tokens.Length < 2 || Array.IndexOf(tokens, Gap) >= 0)
                {
                    continue;
                }
                if (!runs.TryGetValue(tokens[0], out var list))
                {
                    runs[tokens[0]] = list = new List<string[]>();
                }
                list.Add(tokens);
            }
            foreach (var list in runs.Values)
            {
                list.Sort((a, b) => b.Length.CompareTo(a.Length));
            }
            return runs;
        }

        // Find saved runs in each sentence, left to right, longest first, without overlaps.
        // The run being created takes its cells first.
        private static void Match()
        {
            occurrences.Clear();
            var newCovering = new Dictionary<AlienCharacter, Occurrence>();
            pending = Rebind(pending);
            revealed = Rebind(revealed);
            // While translations are hidden, glyphs inside runs edit and show as their own.
            if (Enabled && Plugin.Visible)
            {
                var runs = LoadRuns();
                foreach (var s in sentences)
                {
                    var taken = new bool[s.Cells.Count];
                    if (pending != null && pending.Sentence == s)
                    {
                        Add(pending, taken, newCovering);
                    }
                    if (revealed != null && revealed.Sentence == s)
                    {
                        for (var i = revealed.Start; i <= revealed.End; i++)
                        {
                            taken[i] = true;
                        }
                    }
                    for (var i = 0; i < s.Cells.Count; i++)
                    {
                        var t = s.Cells[i].Token;
                        if (taken[i] || t == null || t == Gap || !runs.TryGetValue(t, out var candidates))
                        {
                            continue;
                        }
                        foreach (var run in candidates)
                        {
                            if (i + run.Length > s.Cells.Count)
                            {
                                continue;
                            }
                            var ok = true;
                            for (var j = 0; j < run.Length && ok; j++)
                            {
                                ok = !taken[i + j] && s.Cells[i + j].Token == run[j];
                            }
                            if (ok && !SharesWord(s, i, i + run.Length - 1))
                            {
                                var occ = new Occurrence { Sentence = s, Start = i, End = i + run.Length - 1, Key = KeyPrefix + string.Join(" ", run) };
                                Add(occ, taken, newCovering);
                                i = occ.End;
                                break;
                            }
                        }
                    }
                }
            }
            var old = covering;
            covering = newCovering;
            foreach (var ac in old.Keys)
            {
                if (ac != null && !covering.ContainsKey(ac))
                {
                    SetHidden(ac, false);
                }
            }
            foreach (var ac in covering.Keys)
            {
                SetHidden(ac, true);
            }
        }

        // Whether a word of the run from start to end also belongs to words outside it, across a
        // conjunction, so that showing the run there would be wrong. In "up and down face",
        // "face" belongs to "up" too, so a run "down face" is wrong there. In "change into or
        // outof", "change" belongs to "outof" too, so a run "change into" is wrong there, and in
        // "by see . read or learn", "by" belongs to the whole list, so a run "by see" is wrong.
        //
        // The glyphs on either side of a glyph translated as "and" or "or" are coordinated when
        // they share a word type. "to" coordinates only two numbers, as in "1 to 2 year":
        // between nouns it names a place or a recipient. Before the run's first glyph, only the
        // conjunction counts: a word shared after a list sits by its last item. After the run's
        // last glyph, a list of single glyphs separated by spaces and ending in "and" or "or"
        // counts too, when all its items share a word type with that glyph (ListType). Spaces
        // also separate clauses, and those rarely end at one glyph of the same type.
        private static bool SharesWord(Sentence s, int start, int end)
        {
            var cells = s.Cells;
            if (start >= 2 && start < end && Joins(cells[start - 1].Token) is string j1
                && Coordinated(cells[start - 2].Token, cells[start].Token, j1) is HashSet<string> t1
                && !Parallel(t1, cells[start + 1].Token, true))
            {
                return true;
            }
            if (end <= start)
            {
                return false;
            }
            var t2 = end + 2 < cells.Count && Joins(cells[end + 1].Token) is string j2
                ? Coordinated(cells[end].Token, cells[end + 2].Token, j2)
                : ListType(s, end);
            return t2 != null && !Parallel(t2, cells[end - 1].Token, false);
        }

        // Where the coordinated glyphs' run neighbor w shows the coordination joins whole
        // phrases, each with its own words, rather than single words that share w:
        // - Nouns or adjectives beside a word of their own type, as in "metal planet and water
        //   planet" or "no hot and no cold".
        // - Nouns beside a noun or number, as in chemical formulas: "Na Cl and H 2 O".
        // - Numbers after a noun or number, as in "C O 2 and 2 H" or a number's own earlier
        //   digits. Numbers before a noun share it, as in "1 to 2 year".
        // after says w follows the coordinated glyph; otherwise it precedes it.
        private static bool Parallel(HashSet<string> shared, string w, bool after)
        {
            var types = Types(w);
            if (types == null)
            {
                return false;
            }
            var nounOrNum = types.Contains("noun") || types.Contains("num");
            return ((shared.Contains("noun") || shared.Contains("adj")) && types.Overlaps(shared))
                || (shared.Contains("noun") && nounOrNum)
                || (shared.Contains("num") && !after && nounOrNum);
        }

        // The word types glyphs a and b, joined by a conjunction that Joins named joins, share as
        // coordinated words, or null.
        private static HashSet<string> Coordinated(string a, string b, string joins)
        {
            var shared = Types(a);
            var other = Types(b);
            if (shared == null || other == null)
            {
                return null;
            }
            shared.IntersectWith(other);
            if (joins == "to")
            {
                shared.IntersectWith(new[] { "num" });
            }
            return shared.Count > 0 ? shared : null;
        }

        // The word types shared by the glyph at end and a list after it: spaces, each followed by
        // one glyph, and then "and" or "or" and the last item's first glyph. Null if there is no
        // such list or its items share no type.
        private static HashSet<string> ListType(Sentence s, int end)
        {
            var cells = s.Cells;
            var shared = Types(cells[end].Token);
            var k = end + 1;
            while (shared != null && shared.Count > 0 && k < cells.Count && cells[k].Token == Gap)
            {
                while (k < cells.Count && cells[k].Token == Gap)
                {
                    k++;
                }
                if (k + 2 >= cells.Count || Types(cells[k].Token) is not HashSet<string> item)
                {
                    return null;
                }
                shared.IntersectWith(item);
                k++;
                var joins = Joins(cells[k].Token);
                if (joins == "and" || joins == "or")
                {
                    var last = Types(cells[k + 1].Token);
                    if (last == null)
                    {
                        return null;
                    }
                    shared.IntersectWith(last);
                    return shared.Count > 0 ? shared : null;
                }
            }
            return null;
        }

        // A new set of the glyph's word types, or null for a gap, a cell outside runs, or a glyph
        // without types.
        private static HashSet<string> Types(string glyph)
        {
            if (glyph == null || glyph == Gap || Global.alien_text == null || !Global.alien_text.TryGetValue(glyph, out var w) || w.wordtype == null)
            {
                return null;
            }
            return new HashSet<string>(w.wordtype);
        }

        // "and", "or", or "to" if the glyph's translation is that word or lists it among
        // alternatives separated by punctuation of any kind, as in "end / to" or "end, to".
        // Spaces, hyphens, and apostrophes stay inside an alternative, so "near to" is not "to".
        // Otherwise null.
        private static readonly System.Text.RegularExpressions.Regex Alternatives = new System.Text.RegularExpressions.Regex(@"[^\p{L}\p{N}\s'-]+");

        internal static string Joins(string glyph)
        {
            if (glyph == null || glyph == Gap || !CompoundHint.IsTranslated(glyph))
            {
                return null;
            }
            foreach (var part in Alternatives.Split(Global.savedata.dict[glyph]))
            {
                var t = part.Trim().ToLowerInvariant();
                if (t == "and" || t == "or" || t == "to")
                {
                    return t;
                }
            }
            return null;
        }

        private static void Add(Occurrence occ, bool[] taken, Dictionary<AlienCharacter, Occurrence> cover)
        {
            occurrences.Add(occ);
            for (var i = occ.Start; i <= occ.End; i++)
            {
                taken[i] = true;
                var ac = occ.Sentence.Cells[i].Ac;
                if (ac != null)
                {
                    cover[ac] = occ;
                }
            }
        }

        private static void Render()
        {
            var manager = Global.manager;
            var show = manager != null && Plugin.Visible;

            // A label keeps its run while that run still matches, so a change elsewhere, such as
            // a run revealed on hover, rewrites no other label. A label being edited, or selected
            // with its activation queued, keeps its run first, so what is typed is saved under the
            // run the player chose. If its run no longer matches, it stays on screen until its
            // edit ends.
            var kept = new Dictionary<Occurrence, Label>();
            var keptLabels = new HashSet<Label>();
            foreach (var inUse in new[] { true, false })
            {
                foreach (var l in labels)
                {
                    if (l.Occ == null || l.Field == null || (l == editing || ModFields.InUse(l.Field)) != inUse || !(show || inUse))
                    {
                        continue;
                    }
                    var occ = occurrences.Find(o => o.Same(l.Occ) && !kept.ContainsKey(o));
                    if (occ != null)
                    {
                        l.Occ = occ;
                        kept[occ] = l;
                        keptLabels.Add(l);
                    }
                    else if (inUse)
                    {
                        keptLabels.Add(l);
                    }
                }
            }
            var free = new Queue<Label>();
            foreach (var l in labels)
            {
                if (!keptLabels.Contains(l))
                {
                    free.Enqueue(l);
                }
            }
            if (show)
            {
                foreach (var occ in occurrences)
                {
                    if (!kept.TryGetValue(occ, out var label))
                    {
                        label = free.Count > 0 ? free.Dequeue() : CreateLabel(manager);
                        label.Occ = occ;
                        label.Field.text = Translation(occ.Key);
                    }
                    else if (label != editing && !ModFields.InUse(label.Field))
                    {
                        // The translation may have changed, as by an undo.
                        var t = Translation(occ.Key);
                        if (label.Field.text != t)
                        {
                            label.Field.text = t;
                        }
                    }
                    // The game recolors only its own labels when the color scheme changes.
                    label.Field.textComponent.color = CompoundHint.NormalColor();
                    label.Field.gameObject.SetActive(true);
                }
            }
            foreach (var l in free)
            {
                l.Occ = null;
                SetLines(l, Array.Empty<Vector3[]>());
                if (l.Field != null)
                {
                    l.Field.gameObject.SetActive(false);
                }
            }
        }

        // Draw.ClearSelected prefix, as a redraw starts: end a run label's edit while its run is
        // still known, so it is saved under that run, as the game's own labels are saved when the
        // redraw deactivates them.
        internal static void BeforeRedraw()
        {
            var es = EventSystem.current;
            if (es == null)
            {
                return;
            }
            foreach (var l in labels)
            {
                if (l.Occ != null && l.Field != null && l.Field.isFocused && es.currentSelectedGameObject == l.Field.gameObject)
                {
                    es.SetSelectedGameObject(null);
                }
            }
        }

        // Index ranges of the occurrence's cells on each screen row.
        private static List<(int First, int Last)> Rows(Occurrence occ)
        {
            var rows = new List<(int, int)>();
            var cells = occ.Sentence.Cells;
            var first = occ.Start;
            for (var i = occ.Start + 1; i <= occ.End + 1; i++)
            {
                if (i > occ.End || cells[i].Y != cells[first].Y)
                {
                    rows.Add((first, i - 1));
                    first = i;
                }
            }
            return rows;
        }

        private static Vector3 CellPosition(Cell c)
        {
            var slot = Global.manager.alien_slots[c.X, c.Y];
            return slot.sp != null ? slot.sp.transform.position : slot.transform.position;
        }

        private static Label CreateLabel(Draw manager)
        {
            var label = new Label();
            label.Field = ModFields.Create(manager, "sequence translation", "Sequence translation", () => OnLabelSelect(label), s => OnLabelEndEdit(label, s));
            labels.Add(label);
            return label;
        }

        private static void OnLabelSelect(Label label)
        {
            if (label.Occ == null)
            {
                return;
            }
            editing = label;
            label.Field.text = Translation(label.Occ.Key);
            label.Field.textComponent.color = CompoundHint.NormalColor();
        }

        private static void OnLabelEndEdit(Label label, string s)
        {
            if (editing == label)
            {
                editing = null;
            }
            var occ = label.Occ;
            if (occ == null)
            {
                return;
            }
            SavedValues.Store(occ.Key, s);
            if (pending != null && pending.Same(occ))
            {
                pending = null;
            }
            Remember(occ.Sentence, occ.Start, occ.End);
            needsRender = true;
        }

        // Each label is placed as a glyph's label is, widened to the run's row.
        private static void PositionLabels()
        {
            var cam = Camera.main;
            var slots = Global.manager?.alien_slots;
            if (cam == null || slots == null)
            {
                return;
            }
            var cell = ModFields.CellSize(cam, Global.manager);
            foreach (var label in labels)
            {
                var occ = label.Occ;
                if (occ == null || label.Field == null || !label.Field.gameObject.activeSelf)
                {
                    continue;
                }
                // Put the label under the widest row of a run that wraps.
                var best = (First: occ.Start, Last: occ.Start);
                foreach (var row in Rows(occ))
                {
                    if (row.Last - row.First > best.Last - best.First)
                    {
                        best = row;
                    }
                }
                var first = occ.Sentence.Cells[best.First];
                var corner = slots[first.X, first.Y].transform.position + Vector3.down + Vector3.left * 0.5f;
                ModFields.Place(label.Field, cam, corner, new Vector2(cell.x * (best.Last - best.First + 1), cell.y / 3f), cell);
                // The bracket marks the run only while its translation is being edited.
                SetLines(label, label == editing ? Bracket(label, best.First, cam) : Array.Empty<Vector3[]>());
            }
        }

        // An upward-open bracket under the run, through the middle of its label and broken
        // around the label's text, with ticks up at the run's two ends. A run that wraps gets
        // a line on each row, with ticks only at its real start and end.
        private static List<Vector3[]> Bracket(Label label, int labelRow, Camera cam)
        {
            const float inset = 0.05f;
            const float gap = 0.1f;
            var occ = label.Occ;
            var cells = occ.Sentence.Cells;
            var lines = new List<Vector3[]>();
            foreach (var (first, last) in Rows(occ))
            {
                var p0 = CellPosition(cells[first]);
                var p1 = CellPosition(cells[last]);
                var left = p0.x - 0.5f + inset;
                var right = p1.x + 0.5f - inset;
                // Labels span 0.5 below a point 1 under the glyph's center (Draw.ResetTextPosition).
                var y = p0.y - 0.75f;
                var top = y + 0.125f;
                var start = first == occ.Start ? new[] { new Vector3(left, top) } : new Vector3[0];
                var end = last == occ.End ? new[] { new Vector3(right, top) } : new Vector3[0];
                if (first != labelRow)
                {
                    lines.Add(Concat(start, new[] { new Vector3(left, y), new Vector3(right, y) }, end));
                    continue;
                }
                TextSpan(label, cam, (left + right) / 2f, out var textLeft, out var textRight);
                var a = Mathf.Clamp(textLeft - gap, left, right);
                var b = Mathf.Clamp(textRight + gap, left, right);
                lines.Add(Concat(start, new[] { new Vector3(left, y), new Vector3(a, y) }, new Vector3[0]));
                lines.Add(Concat(new Vector3[0], new[] { new Vector3(b, y), new Vector3(right, y) }, end));
            }
            return lines;
        }

        private static Vector3[] Concat(Vector3[] a, Vector3[] b, Vector3[] c)
        {
            var r = new Vector3[a.Length + b.Length + c.Length];
            a.CopyTo(r, 0);
            b.CopyTo(r, a.Length);
            c.CopyTo(r, a.Length + b.Length);
            return r;
        }

        // World x extent of the label's rendered text; an empty label is a point at center.
        private static void TextSpan(Label label, Camera cam, float center, out float left, out float right)
        {
            left = right = center;
            var tc = label.Field.textComponent;
            if (string.IsNullOrEmpty(label.Field.text) || tc == null)
            {
                return;
            }
            var bounds = tc.textBounds;
            if (bounds.size.x <= 0f)
            {
                return;
            }
            // The label canvas is screen space, so its world coordinates are screen pixels.
            left = cam.ScreenToWorldPoint(tc.transform.TransformPoint(bounds.min)).x;
            right = cam.ScreenToWorldPoint(tc.transform.TransformPoint(bounds.max)).x;
        }

        private static void SetLines(Label label, IList<Vector3[]> polylines)
        {
            var manager = Global.manager;
            while (label.Lines.Count > polylines.Count || (manager == null && label.Lines.Count > 0))
            {
                var last = label.Lines[label.Lines.Count - 1];
                if (last != null)
                {
                    UnityEngine.Object.Destroy(last.gameObject);
                }
                label.Lines.RemoveAt(label.Lines.Count - 1);
            }
            if (manager == null)
            {
                return;
            }
            var color = CompoundHint.NormalColor();
            // One screen pixel, in world units.
            var cam = Camera.main;
            var width = cam != null && cam.pixelHeight > 0 ? 2f * cam.orthographicSize / cam.pixelHeight : 0.01f;
            for (var i = 0; i < polylines.Count; i++)
            {
                if (i == label.Lines.Count || label.Lines[i] == null)
                {
                    var line = manager.DrawLineRenderer(Vector2.zero, Vector2.zero, 0);
                    line.gameObject.name = "sequence bracket";
                    if (i == label.Lines.Count)
                    {
                        label.Lines.Add(line);
                    }
                    else
                    {
                        label.Lines[i] = line;
                    }
                }
                var lr = label.Lines[i];
                lr.positionCount = polylines[i].Length;
                lr.SetPositions(polylines[i]);
                lr.startColor = lr.endColor = color;
                lr.startWidth = lr.endWidth = width;
            }
        }
    }
}
