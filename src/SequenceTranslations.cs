using System;
using System.Collections.Generic;
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
    // A sentence is the glyphs one Draw.Write call placed on one source line (same sector and
    // AlienCharacter.line), which may wrap across rows. A run is identified by its glyph IDs in
    // order, with "." for each empty cell between glyphs on a row. Translations are stored in
    // Global.savedata.dict under KeyPrefix + the run, so they are saved with the game's save slot.
    // The game only reads that dictionary by word, never enumerates it.
    internal static class SequenceTranslations
    {
        internal const string KeyPrefix = "compoundtranslations.sequence:";
        private const string Gap = ".";
        private const int CharacterLimit = 200;

        private sealed class Cell
        {
            public AlienCharacter Ac;
            // A glyph ID, Gap for an empty cell, or null for a cell that cannot be part of a run.
            public string Token;
            public int X;
            public int Y;
        }

        private sealed class Sentence
        {
            public string Sector;
            public int Line;
            public readonly List<Cell> Cells = new List<Cell>();
        }

        private sealed class Occurrence
        {
            public Sentence Sentence;
            public int Start;
            public int End;
            public string Key;

            public bool Same(Occurrence o) => o != null && o.Sentence.Sector == Sentence.Sector
                && o.Sentence.Line == Sentence.Line && o.Start == Start && o.End == End && o.Key == Key;
        }

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
        private static readonly List<Label> labels = new List<Label>();
        private static readonly HashSet<AlienCharacter> suppressEndEdit = new HashSet<AlienCharacter>();
        // Glyphs whose edit was replaced by a run's. The game never started editing them, so any
        // end of edit they report, whenever Unity sends it, must not save their field's text.
        private static readonly HashSet<AlienCharacter> replaced = new HashSet<AlienCharacter>();

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
        private static Action deferred;
        private static bool loggedError;

        // The selection whose edit ended most recently, for extending it with shift+click.
        private static Sentence lastSentence;
        private static int lastStart;
        private static int lastEnd;
        private static int lastFrame = -1;

        private static bool Shift => Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

        private static bool Enabled => Plugin.Sequences.Value;

        private static bool Visible => !Global.hideUi && !(Global.globalData?.hidetranslation ?? false);

        internal static void MarkDirty() => dirty = true;

        internal static void MarkChanged() => needsRender = true;

        internal static void Tick()
        {
            try
            {
                if (deferred != null)
                {
                    var d = deferred;
                    deferred = null;
                    d();
                }
                if (Visible != lastVisible)
                {
                    needsRender = true;
                }
                UpdateReveal();
                if (dirty || needsRender)
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
            if (hidden && ac.txt.isFocused)
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
            if (Plugin.RevealOnHover.Value && Enabled && Visible && editing == null && pending == null && !dirty)
            {
                var ac = HoveredGlyph();
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

        private static AlienCharacter HoveredGlyph()
        {
            var cam = Camera.main;
            if (cam == null || !Physics.Raycast(cam.ScreenPointToRay(Input.mousePosition), out var hit))
            {
                return null;
            }
            return hit.transform.GetComponentInParent<AlienCharacter>();
        }

        // StartEdit prefix. Returns false to replace the glyph's own edit with a run's.
        internal static bool BeforeStartEdit(AlienCharacter ac)
        {
            var proceed = StartsOwnEdit(ac);
            if (ac != null)
            {
                if (proceed)
                {
                    replaced.Remove(ac);
                }
                else
                {
                    replaced.Add(ac);
                }
            }
            return proceed;
        }

        private static bool StartsOwnEdit(AlienCharacter ac)
        {
            if (!Enabled || dirty || ac == null || !cellOf.TryGetValue(ac, out var at))
            {
                return true;
            }
            covering.TryGetValue(ac, out var cov);
            var shift = Shift;
            if (shift && lastFrame == Time.frameCount && lastSentence == at.Sentence)
            {
                var start = Math.Min(lastStart, cov?.Start ?? at.Index);
                var end = Math.Max(lastEnd, cov?.End ?? at.Index);
                var occ = MakeOccurrence(at.Sentence, start, end);
                if (occ != null)
                {
                    Defer(occ, ac, null);
                    return false;
                }
            }
            if (cov != null)
            {
                Defer(cov, ac, null);
                return false;
            }
            return true;
        }

        internal static bool IsSuppressed(AlienCharacter ac) => ac != null && (suppressEndEdit.Contains(ac) || replaced.Contains(ac));

        // EndEdit postfix for a glyph's own label.
        internal static void AfterGlyphEndEdit(AlienCharacter ac)
        {
            if (ac != null && cellOf.TryGetValue(ac, out var at))
            {
                Remember(at.Sentence, at.Index, at.Index);
            }
            // The new translation may be "and" or "or", which changes where runs match.
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
                if (t == null || ((i == start || i == end) && t == Gap))
                {
                    return null;
                }
                tokens[i - start] = t;
            }
            return new Occurrence { Sentence = s, Start = start, End = end, Key = KeyPrefix + string.Join(" ", tokens) };
        }

        // Selecting another object from inside an onSelect handler is refused by the
        // EventSystem, so the switch to the run's label happens in the next LateUpdate.
        // fromGlyph or fromLabel is the field that was just selected and is being replaced.
        private static void Defer(Occurrence occ, AlienCharacter fromGlyph, Label fromLabel)
        {
            deferred = () =>
            {
                // A run's label still holds its saved translation, so ending its edit first
                // changes nothing, and frees the label for reuse.
                if (fromLabel?.Field != null && EventSystem.current?.currentSelectedGameObject == fromLabel.Field.gameObject)
                {
                    EventSystem.current.SetSelectedGameObject(null);
                }
                pending = occ;
                Refresh();
                Label target = null;
                foreach (var l in labels)
                {
                    if (l.Occ != null && l.Occ.Same(pending) && l.Field.gameObject.activeSelf)
                    {
                        target = l;
                        break;
                    }
                }
                if (fromGlyph != null)
                {
                    suppressEndEdit.Add(fromGlyph);
                }
                try
                {
                    EventSystem.current?.SetSelectedGameObject(target?.Field.gameObject);
                }
                finally
                {
                    if (fromGlyph != null)
                    {
                        suppressEndEdit.Remove(fromGlyph);
                    }
                }
                if (target == null)
                {
                    pending = null;
                    needsRender = true;
                }
                fromGlyph?.UpdateEarthText();
            };
        }

        private static string Translation(string key)
        {
            var dict = Global.savedata?.dict;
            return dict != null && dict.TryGetValue(key, out var t) && t != null ? t : "";
        }

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
            lastVisible = Visible;
        }

        private static void Scan()
        {
            sentences.Clear();
            cellOf.Clear();
            var slots = Global.manager?.alien_slots;
            if (slots == null)
            {
                return;
            }
            var bySentence = new Dictionary<(string, int), Sentence>();
            // Rows are filled top to bottom, which is decreasing y, and left to right.
            for (var y = slots.GetLength(1) - 1; y >= 0; y--)
            {
                for (var x = 0; x < slots.GetLength(0); x++)
                {
                    var ac = slots[x, y];
                    if (ac == null || ac.sector == null || !ac.gameObject.activeInHierarchy)
                    {
                        continue;
                    }
                    var k = (ac.sector, ac.line);
                    if (!bySentence.TryGetValue(k, out var s))
                    {
                        s = new Sentence { Sector = ac.sector, Line = ac.line };
                        bySentence[k] = s;
                        sentences.Add(s);
                    }
                    var last = s.Cells.Count > 0 ? s.Cells[s.Cells.Count - 1] : null;
                    if (last != null && last.Y == y)
                    {
                        for (var gx = last.X + 1; gx < x; gx++)
                        {
                            s.Cells.Add(new Cell { Token = Gap, X = gx, Y = y });
                        }
                    }
                    var eligible = ac.sp != null && ac.sp.enabled && ac.showtxt && ac.txt != null && !string.IsNullOrEmpty(ac.alien_txt);
                    cellOf[ac] = (s, s.Cells.Count);
                    s.Cells.Add(new Cell { Ac = ac, Token = eligible ? ac.alien_txt : null, X = x, Y = y });
                }
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
                if (s.Sector == o.Sentence.Sector && s.Line == o.Sentence.Line)
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
                if (!kv.Key.StartsWith(KeyPrefix, StringComparison.Ordinal) || string.IsNullOrWhiteSpace(kv.Value))
                {
                    continue;
                }
                var tokens = kv.Key.Substring(KeyPrefix.Length).Split(' ');
                if (tokens.Length < 2)
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
            if (Enabled)
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
                        if (taken[i] || t == null || t == Gap || !runs.TryGetValue(t, out var candidates) || SharesHead(s, i))
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
                            if (ok)
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

        // In "up and down face", "face" belongs to "up" too, so a run "down face" there would
        // be wrong. A run is not matched right after a glyph translated as "and" or "or" when
        // the glyph before that shares a word type with the run's first glyph.
        private static bool SharesHead(Sentence s, int i)
        {
            if (i < 2)
            {
                return false;
            }
            var conj = s.Cells[i - 1].Token;
            var before = s.Cells[i - 2].Token;
            var first = s.Cells[i].Token;
            if (conj == null || conj == Gap || before == null || before == Gap || !CompoundHint.IsTranslated(conj))
            {
                return false;
            }
            var t = Global.savedata.dict[conj].Trim();
            if (!t.Equals("and", StringComparison.OrdinalIgnoreCase) && !t.Equals("or", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            var words = Global.alien_text;
            if (words == null || !words.TryGetValue(before, out var a) || !words.TryGetValue(first, out var b) || a.wordtype == null || b.wordtype == null)
            {
                return false;
            }
            foreach (var type in a.wordtype)
            {
                if (b.wordtype.Contains(type))
                {
                    return true;
                }
            }
            return false;
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
            var show = manager != null && Visible;

            Label keep = null;
            if (editing?.Field != null)
            {
                foreach (var occ in occurrences)
                {
                    if (occ.Same(editing.Occ))
                    {
                        editing.Occ = occ;
                        keep = editing;
                        break;
                    }
                }
            }
            var free = new Queue<Label>();
            foreach (var l in labels)
            {
                if (l != keep)
                {
                    free.Enqueue(l);
                }
            }
            if (show)
            {
                foreach (var occ in occurrences)
                {
                    Label label;
                    if (keep != null && keep.Occ == occ)
                    {
                        label = keep;
                    }
                    else
                    {
                        label = free.Count > 0 ? free.Dequeue() : CreateLabel(manager);
                        label.Occ = occ;
                        label.Field.text = Translation(occ.Key);
                    }
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
            // The label being edited no longer matches what is on screen: end the edit,
            // which saves it under its run.
            if (editing != null && keep == null && editing.Field != null && EventSystem.current?.currentSelectedGameObject == editing.Field.gameObject)
            {
                EventSystem.current.SetSelectedGameObject(null);
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
            var go = UnityEngine.Object.Instantiate(manager.text_prefab);
            go.name = "sequence translation";
            go.transform.SetParent(manager.world_canva.transform);
            var field = go.GetComponent<TMP_InputField>();
            var model = manager.txt_slots?[0, 0];
            if (model != null)
            {
                field.fontAsset = model.fontAsset;
            }
            field.characterLimit = CharacterLimit;
            field.richText = false;
            field.textComponent.color = CompoundHint.NormalColor();
            field.onSelect.RemoveAllListeners();
            field.onEndEdit.RemoveAllListeners();
            var label = new Label { Field = field };
            field.onSelect.AddListener(_ => OnLabelSelect(label));
            field.onEndEdit.AddListener(s => OnLabelEndEdit(label, s));
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
            // Shift+click on a run's label extends the selection that was just being edited.
            var occ = label.Occ;
            if (Shift && lastFrame == Time.frameCount && lastSentence == occ.Sentence)
            {
                var extended = MakeOccurrence(occ.Sentence, Math.Min(lastStart, occ.Start), Math.Max(lastEnd, occ.End));
                if (extended != null && !extended.Same(occ))
                {
                    Defer(extended, null, label);
                }
            }
        }

        private static void OnLabelEndEdit(Label label, string s)
        {
            try
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
                var dict = Global.savedata?.dict;
                if (dict != null)
                {
                    var t = (s ?? "").Trim();
                    var had = dict.TryGetValue(occ.Key, out var old);
                    if (t == "" ? had : old != t)
                    {
                        if (t == "")
                        {
                            dict.Remove(occ.Key);
                        }
                        else
                        {
                            dict[occ.Key] = t;
                        }
                        Global.SaveGame();
                    }
                }
                if (pending != null && pending.Same(occ))
                {
                    pending = null;
                }
                Remember(occ.Sentence, occ.Start, occ.End);
                needsRender = true;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Sequence translation edit failed: {e}");
            }
        }

        // Same placement and sizing as Draw.ResetTextPosition gives each glyph's label
        // (bottom-left pivot, one cell wide), widened to the run's row, and the same font
        // limits as AlienCharacter.UpdatePos.
        private static void PositionLabels()
        {
            var cam = Camera.main;
            var slots = Global.manager?.alien_slots;
            if (cam == null || slots == null)
            {
                return;
            }
            var cell = (Vector2)cam.WorldToScreenPoint(slots[1, 1].transform.position) - (Vector2)cam.WorldToScreenPoint(slots[0, 0].transform.position);
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
                label.Field.transform.position = (Vector2)cam.WorldToScreenPoint(corner);
                ((RectTransform)label.Field.transform).sizeDelta = new Vector2(cell.x * (best.Last - best.First + 1), cell.y / 3f);
                // AlienCharacter.UpdatePos scales the game's labels this way every frame; the
                // prefab's own maximum does not follow the screen size.
                label.Field.textComponent.fontSizeMax = 0.3f * cell.x;
                label.Field.textComponent.textWrappingMode = TextWrappingModes.Normal;
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
