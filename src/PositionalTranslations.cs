using System;
using System.Collections.Generic;
using Shapes2D;
using TMPro;
using UnityEngine;

namespace CompoundTranslations
{
    // Translations of a word for its position in a two-part compound: left or right in a
    // LeftRight compound, top or bottom in an UpDown compound, and outside or inside in an
    // Out_In compound. The other Out_ relations, which push the inner part toward a corner or
    // side, use the outside and inside translations. A hint shows a part's positional
    // translation, when it has one, in place of its translation.
    //
    // A word's information panel on the left gets Plugin.PositionGlyphs in its empty line, between
    // the composition line and the definitions, then Plugin.PositionsSetGlyph or
    // Plugin.PositionsUnsetGlyph for whether the word has any position translations, then
    // ButtonMarker. They are real words, drawn and translated like any other. Clicking one of them
    // pushes a left panel whose title is the word, Plugin.PossessiveGlyph, and
    // Plugin.PositionGlyphs, and whose text is Marker. Draw.Write does not draw a bracketed token
    // that is not at the start of a line. That panel shows each relation drawn both ways, the word
    // in the normal color and a placeholder square in the finished color, each with a field for
    // that position's translation. Only positions the word takes in compounds the player has
    // encountered are shown, and only words with such positions get the button. The drawings are
    // built with the game's own composition code and drawn as bare stroke shapes, so they never
    // become words.
    //
    // Translations are stored in Global.savedata.dict under KeyPrefix + slot + ":" + word.
    internal static class PositionalTranslations
    {
        internal const string KeyPrefix = "compoundtranslations.position:";
        private const string Marker = "[compoundtranslations.positions]";
        private const string ButtonMarker = "[compoundtranslations.positionbutton]";
        // The end of the composition line and the empty line after it, as the game writes them.
        private const string BlankLine = "newline  newline ";

        private sealed class Variant
        {
            public PosRelation Relation;
            public bool WordFirst;
            public string Slot;
            public int X;
            public int Y;
        }

        // Left panel cells: the glyph column of each variant, its row, and the label's width.
        private const int LabelWidth = 5;
        private static readonly Variant[] Variants =
        {
            new Variant { Relation = PosRelation.LeftRight, WordFirst = true, Slot = "left", X = 2, Y = 1 },
            new Variant { Relation = PosRelation.LeftRight, WordFirst = false, Slot = "right", X = 8, Y = 1 },
            new Variant { Relation = PosRelation.UpDown, WordFirst = true, Slot = "top", X = 2, Y = 2 },
            new Variant { Relation = PosRelation.UpDown, WordFirst = false, Slot = "bottom", X = 8, Y = 2 },
            new Variant { Relation = PosRelation.Out_In, WordFirst = true, Slot = "outside", X = 2, Y = 3 },
            new Variant { Relation = PosRelation.Out_In, WordFirst = false, Slot = "inside", X = 8, Y = 3 },
        };

        // A square outline, the stand-in for the other part of each drawn compound.
        private static List<List<Vector2>> Placeholder() => new List<List<Vector2>>
        {
            new List<Vector2> { new Vector2(-1f, -1f), new Vector2(1f, -1f), new Vector2(1f, 1f), new Vector2(-1f, 1f), new Vector2(-1f, -1f) },
        };

        private sealed class Field
        {
            public TMP_InputField Input;
            public Variant Variant;
            public string Word;
        }

        private static readonly List<Shape> shapes = new List<Shape>();
        // Underlines below the button glyphs, drawn like the game's menu links.
        private static readonly List<LineRenderer> underlines = new List<LineRenderer>();
        private static readonly List<Field> fields = new List<Field>();
        // The word whose information panel, with the button glyphs, is shown, or null.
        private static string buttonWord;
        private static string shownState;
        private static bool dirty = true;
        private static bool loggedError;

        private static bool Enabled => Plugin.Positions.Value;

        private static bool Visible => !Global.hideUi && !(Global.globalData?.hidetranslation ?? false);

        internal static void MarkDirty() => dirty = true;

        // PushLeftText prefix: adds the button glyphs to a word's information text.
        internal static void Decorate(string title, ref string info)
        {
            // While translations are hidden, the panel's fields are not drawn, so no button.
            if (Enabled && Visible && IsWordInfo(title, info) && !info.Contains(ButtonMarker) && EncounteredPositions.HasAny(title))
            {
                var i = info.IndexOf(BlankLine, StringComparison.Ordinal);
                if (i >= 0)
                {
                    info = info.Substring(0, i) + ButtonLine(title) + info.Substring(i + BlankLine.Length);
                }
            }
        }

        // The panel's title: the word, the possessive, and the glyphs that name the panel.
        private static string PanelTitle(string word) => word + " " + Plugin.PossessiveGlyph + " " + Plugin.PositionGlyphs;

        private static string ButtonGlyphs(bool set) => Plugin.PositionGlyphs + " " + (set ? Plugin.PositionsSetGlyph : Plugin.PositionsUnsetGlyph);

        private static string ButtonLine(string word) => "newline " + ButtonGlyphs(HasAny(word)) + " " + ButtonMarker + " newline ";

        // Removes the button line, whichever indicator it shows, restoring the empty line.
        private static string Strip(string info)
        {
            var end = info.IndexOf(ButtonMarker + " newline ", StringComparison.Ordinal);
            if (end < 0)
            {
                return info;
            }
            var start = info.LastIndexOf("newline " + Plugin.PositionGlyphs + " ", end, StringComparison.Ordinal);
            if (start < 0)
            {
                return info;
            }
            end += ButtonMarker.Length + " newline ".Length;
            return info.Substring(0, start) + BlankLine + info.Substring(end);
        }

        private static bool HasAny(string word)
        {
            var canonical = CompoundHint.Canonical(word);
            foreach (var v in Variants)
            {
                if (Get(canonical, v.Slot) != null)
                {
                    return true;
                }
            }
            return false;
        }

        private static bool IsWordInfo(string title, string info) =>
            title != null && info != null && Global.alien_text != null && Global.alien_text.ContainsKey(title)
            && info.StartsWith(title + " ", StringComparison.Ordinal);

        internal static void SettingChanged()
        {
            dirty = true;
            RefreshHistory();
        }

        // Splits a left panel history entry. PushLeftText joins title and text with "&", or with
        // "&&" for text it does not parse, as a word's information panel is.
        private static bool SplitEntry(string entry, out string title, out string sep, out string info)
        {
            var i = entry.IndexOf('&');
            if (i < 0)
            {
                title = sep = info = null;
                return false;
            }
            title = entry.Substring(0, i);
            sep = string.CompareOrdinal(entry, i + 1, "&", 0, 1) == 0 ? "&&" : "&";
            info = entry.Substring(i + sep.Length);
            return true;
        }

        // Rewrites the button line of the information panels already in the left panel's
        // history, so a changed setting or translation shows in them too. Redraws when the
        // shown entry changed; other entries are rewritten again before every redraw
        // (BeforeRedraw), so a copy of the history taken at a redraw, as Interface Tweaks' undo
        // takes, is brought up to date when it is drawn.
        private static void RefreshHistory(bool redraw = true)
        {
            var m = Global.manager;
            if (m?.text_history == null)
            {
                return;
            }
            var changed = false;
            for (var i = 0; i < m.text_history.Count; i++)
            {
                if (!SplitEntry(m.text_history[i], out var title, out var sep, out var info))
                {
                    continue;
                }
                var next = Strip(info);
                Decorate(title, ref next);
                if (next != info)
                {
                    m.text_history[i] = title + sep + next;
                    changed |= i == m.currenttext;
                }
            }
            if (changed && redraw)
            {
                m.UpdateText();
            }
        }

        // UpdateText1 prefix: the left panel is drawn from the history, so its entries show the
        // current button line.
        internal static void BeforeRedraw() => RefreshHistory(redraw: false);

        // The slot a part fills in a compound of the given relation, or null when the relation
        // has no positional translations. part is 0 for the first component, 1 for the second.
        internal static string SlotFor(PosRelation relation, int part)
        {
            switch (relation)
            {
                case PosRelation.LeftRight:
                    return part == 0 ? "left" : "right";
                case PosRelation.UpDown:
                    return part == 0 ? "top" : "bottom";
                case PosRelation.Out_In:
                case PosRelation.Out_UpRight:
                case PosRelation.Out_UpLeft:
                case PosRelation.Out_DownRight:
                case PosRelation.Out_DownLeft:
                case PosRelation.Out_Up:
                case PosRelation.Out_Down:
                case PosRelation.Out_Left:
                case PosRelation.Out_Right:
                    return part == 0 ? "outside" : "inside";
                default:
                    return null;
            }
        }

        // The word's translation for the slot, or null when it has none or the feature is off.
        internal static string Get(string word, string slot) =>
            !Enabled || slot == null || string.IsNullOrEmpty(word) ? null : SavedValues.Get(Key(slot, word));

        private static string Key(string slot, string word) => KeyPrefix + slot + ":" + word;

        // Saves the word's translation for the slot, or removes it when blank, and redraws what
        // shows it.
        internal static void Save(string word, string slot, string s)
        {
            if (SavedValues.Store(Key(slot, word), s))
            {
                CompoundHint.RefreshVisible(word);
                RefreshHistory();
            }
        }

        // What the buttons in the left panel's history were last worked out for.
        private static bool lastVisible = true;
        private static int lastWins = -1;
        private static object lastSave;
        private static int lastEncountered = -1;

        internal static void Tick()
        {
            try
            {
                var wins = Global.savedata?.win?.Count ?? 0;
                // Drawing a word for the first time can give its parts positions.
                var encountered = Enabled ? EncounteredPositions.Version : lastEncountered;
                if (Visible != lastVisible || wins != lastWins || !ReferenceEquals(Global.savedata, lastSave) || encountered != lastEncountered)
                {
                    lastVisible = Visible;
                    lastWins = wins;
                    lastSave = Global.savedata;
                    lastEncountered = encountered;
                    RefreshHistory();
                }
                var state = CurrentState(out var word, out var isPanel);
                if (dirty || state != shownState)
                {
                    dirty = false;
                    shownState = state;
                    Rebuild(word, isPanel);
                }
                PositionFields();
                TabFromField();
                if (buttonWord != null && Input.GetMouseButtonDown(0) && IsButtonGlyph(ClickedGlyph()))
                {
                    Global.manager.PushLeftText(PanelTitle(buttonWord), Marker);
                    Global.manager.UpdateText();
                }
            }
            catch (Exception e)
            {
                if (!loggedError)
                {
                    Plugin.Log.LogError($"Positional translations: {e}");
                    loggedError = true;
                }
            }
        }

        // Describes what the left panel shows, so a change is noticed even without a redraw.
        // word is the panel's word when it is a word's information panel or this mod's panel.
        private static string CurrentState(out string word, out bool isPanel)
        {
            word = null;
            isPanel = false;
            var m = Global.manager;
            if (!Enabled || !Visible || m == null || m.text_history == null || m.currenttext < 0 || m.currenttext >= m.text_history.Count)
            {
                return null;
            }
            var entry = m.text_history[m.currenttext];
            if (!SplitEntry(entry, out var title, out _, out var info))
            {
                return null;
            }
            if (info == Marker)
            {
                // The panel's title is the word followed by PanelTitle's other glyphs.
                var space = title.IndexOf(' ');
                title = space < 0 ? title : title.Substring(0, space);
            }
            if (Global.alien_text == null || !Global.alien_text.ContainsKey(title))
            {
                return null;
            }
            var page = m.currentleftpage != null && m.currenttext < m.currentleftpage.Count ? m.currentleftpage[m.currenttext] : 0;
            if (info == Marker)
            {
                isPanel = true;
            }
            else if (!IsWordInfo(title, info) || !info.Contains(ButtonMarker))
            {
                return null;
            }
            // Another mod, such as Calculator, may draw over the left panel without a history
            // entry of its own, so the drawn title must be the entry's.
            if (DrawnTitle(m) != (isPanel ? PanelTitle(title) : title))
            {
                return null;
            }
            word = title;
            return m.currenttext + "\n" + entry + "\n" + page;
        }

        // The words of the left panel's title as drawn, or null when it shows none.
        private static string DrawnTitle(Draw m)
        {
            if (m.alien_left_title == null)
            {
                return null;
            }
            var words = new List<string>();
            foreach (var ac in m.alien_left_title)
            {
                if (ac != null && ac.gameObject.activeInHierarchy && ac.sector == "lefttitle")
                {
                    words.Add(ac.alien_txt);
                }
            }
            return words.Count > 0 ? string.Join(" ", words) : null;
        }

        private static void Rebuild(string word, bool isPanel)
        {
            foreach (var s in shapes)
            {
                if (s != null)
                {
                    UnityEngine.Object.Destroy(s.gameObject);
                }
            }
            shapes.Clear();
            foreach (var u in underlines)
            {
                if (u != null)
                {
                    UnityEngine.Object.Destroy(u.gameObject);
                }
            }
            underlines.Clear();
            buttonWord = null;
            foreach (var f in fields)
            {
                if (f.Input != null && !ModFields.InUse(f.Input))
                {
                    f.Input.gameObject.SetActive(false);
                    f.Variant = null;
                }
            }
            var m = Global.manager;
            if (word == null || m == null)
            {
                return;
            }
            if (isPanel)
            {
                BuildPanel(m, word);
            }
            else
            {
                buttonWord = word;
                UnderlineButton(m);
            }
        }

        // Underlines each button glyph the way Draw.Write underlines a menu link's glyphs: a
        // line 0.92 below the glyph's center in color 3, spanning its cell, but starting 0.45
        // left of center in the panel's first column.
        private static void UnderlineButton(Draw m)
        {
            var slots = m.alien_slots;
            if (slots == null)
            {
                return;
            }
            for (var x = 0; x < slots.GetLength(0); x++)
            {
                for (var y = 0; y < slots.GetLength(1); y++)
                {
                    var ac = slots[x, y];
                    if (ac == null || ac.sp == null || !ac.gameObject.activeInHierarchy || !IsButtonGlyph(ac))
                    {
                        continue;
                    }
                    var p = ac.sp.transform.position;
                    underlines.Add(m.DrawLineRenderer(p + new Vector3(x == 0 ? -0.45f : -0.5f, -0.92f), p + new Vector3(0.5f, -0.92f), 3));
                }
            }
        }

        private static void BuildPanel(Draw m, string word)
        {
            var canonical = CompoundHint.Canonical(word);
            for (var i = 0; i < Variants.Length; i++)
            {
                var v = Variants[i];
                if (!EncounteredPositions.Has(canonical, v.Slot))
                {
                    continue;
                }
                DrawCompound(m, word, v.Relation, v.WordFirst, v.X, v.Y, EncounteredPositions.Modifier(canonical, v.Slot));
                while (fields.Count <= i)
                {
                    var f = new Field();
                    f.Input = CreateInput(m, "CompoundTranslations.Position" + fields.Count, f);
                    fields.Add(f);
                }
                var field = fields[i];
                if (ModFields.InUse(field.Input))
                {
                    continue;
                }
                field.Variant = v;
                field.Word = canonical;
                ShowValue(field);
                field.Input.gameObject.SetActive(true);
            }
        }

        // The field holds the positional translation, or is empty and shows, in the finished
        // color, what hints show for the word without it. The fallback is the field's
        // placeholder, so no way of ending an edit can save it.
        private static void ShowValue(Field f)
        {
            f.Input.textComponent.color = CompoundHint.NormalColor();
            f.Input.text = Get(f.Word, f.Variant.Slot) ?? "";
            if (f.Input.placeholder is TMP_Text placeholder)
            {
                placeholder.color = CompoundHint.FinishedColor();
                placeholder.text = CompoundHint.PartFallback(f.Word);
            }
        }

        private static void OnSelect(Field f)
        {
            if (f.Variant == null)
            {
                return;
            }
            f.Input.textComponent.color = CompoundHint.NormalColor();
        }

        private static void OnEndEdit(Field f, string s)
        {
            if (f.Variant == null)
            {
                return;
            }
            Save(f.Word, f.Variant.Slot, s);
            ShowValue(f);
        }

        // Builds the compound the way AlienInfo.GetCharacter builds a real one with the given
        // modifier string, scales it the way Draw.DrawPath1 fits a glyph to its cell, and draws the
        // word's strokes and the placeholder's in separate colors.
        private static void DrawCompound(Draw m, string word, PosRelation relation, bool wordFirst, int x, int y, string modifier)
        {
            var wordStrokes = Global.StringToCharacter(word);
            if (wordStrokes == null || wordStrokes.Count == 0)
            {
                return;
            }
            var other = Placeholder();
            var wordCentered = !(Global.alien_text.TryGetValue(word, out var info) && (info.struct_modifier ?? "").Contains("!"));
            var first = wordFirst ? wordStrokes : other;
            var second = wordFirst ? other : wordStrokes;
            var firstCount = first.Count;
            // The same flags AlienInfo.GetCharacter reads from struct_modifier.
            var mod = modifier ?? "";
            var combined = Global.Pos_combine(first, second, relation,
                pos2_offset: (mod.Contains("x") ? 0.05f : 0f) * Vector2.down,
                equalsize: mod.Contains("="), left_large: mod.Contains("l"), right_large: mod.Contains("r"),
                in_large: mod.Contains("+"), in_small: mod.Contains("-"), in_small2: mod.Contains("x"),
                extraspace: mod.Contains("s"), nospace: mod.Contains("n"), lessspace: mod.Contains("z"),
                cp1_center: wordFirst ? wordCentered : true, cp2_center: wordFirst ? true : wordCentered, nn: "");
            float maxX = 0f, maxY = 0f;
            foreach (var line in combined)
            {
                foreach (var p in line)
                {
                    maxX = Mathf.Max(maxX, Mathf.Abs(p.x));
                    maxY = Mathf.Max(maxY, Mathf.Abs(p.y));
                }
            }
            if (maxX * 2f > 0.8f || maxY * 2f > 0.8f)
            {
                combined = Global.Scale_to(combined, maxX, maxY, new Vector2(0.8f, 0.8f));
            }
            var a = combined.GetRange(0, Math.Min(firstCount, combined.Count));
            var b = combined.GetRange(a.Count, combined.Count - a.Count);
            var center = (Vector3)m.PosToScreenPos(x, y);
            // DrawPath1 does not rescale strokes that fit in resize_to.
            var noResize = new Vector2(10f, 10f);
            AddShape(m.DrawPath1(a, center, 0, -1f, noResize, collider: false), wordFirst ? CompoundHint.NormalColor() : CompoundHint.FinishedColor());
            AddShape(m.DrawPath1(b, center, 0, -1f, noResize, collider: false), wordFirst ? CompoundHint.FinishedColor() : CompoundHint.NormalColor());
        }

        // Shapes2D multiplies the outline color by the SpriteRenderer's color. Glyph sprites get
        // their color from SpriteRenderer.color, so the shapes are colored the same way.
        private static void AddShape(Shape s, Color color)
        {
            if (s == null)
            {
                return;
            }
            s.gameObject.name = "CompoundTranslations.PositionGlyph";
            s.settings.outlineColor = Color.white;
            s.ComputeAndApply();
            var sr = s.GetComponent<SpriteRenderer>();
            if (sr != null)
            {
                sr.color = color;
            }
            shapes.Add(s);
        }

        private static TMP_InputField CreateInput(Draw m, string name, Field f)
        {
            var input = ModFields.Create(m, name, "Positional translation", () => OnSelect(f), s => OnEndEdit(f, s));
            // The prefab has no placeholder; a copy of its text shows the fallback.
            var placeholder = UnityEngine.Object.Instantiate(input.textComponent.gameObject, input.textComponent.transform.parent);
            placeholder.name = "Placeholder";
            input.placeholder = placeholder.GetComponent<TMP_Text>();
            input.gameObject.SetActive(false);
            return input;
        }

        // Each field is placed as a glyph's label is, widened to LabelWidth cells centered on
        // the glyph.
        private static void PositionFields()
        {
            var cam = Camera.main;
            var m = Global.manager;
            if (cam == null || m?.alien_slots == null)
            {
                return;
            }
            var cell = ModFields.CellSize(cam, m);
            foreach (var f in fields)
            {
                if (f.Variant == null || f.Input == null || !f.Input.gameObject.activeSelf)
                {
                    continue;
                }
                var left = f.Variant.X - LabelWidth / 2;
                var corner = (Vector3)m.PosToScreenPos(left, f.Variant.Y) + Vector3.down + Vector3.left * 0.5f;
                ModFields.Place(f.Input, cam, corner, new Vector2(cell.x * LabelWidth, cell.y / 3f), cell);
            }
        }

        // A field's place in the game's Tab order: the cell of the glyph it translates, in
        // Draw.alien_slots, whose rows count up from the bottom.
        private static Vector2Int Cell(Field f) => new Vector2Int(f.Variant.X, Global.manager.alien_slots.GetLength(1) - 1 - f.Variant.Y);

        // The shown position fields, for TabOrder.
        internal static IEnumerable<(Vector2Int Cell, GameObject Field)> TabFields()
        {
            foreach (var f in fields)
            {
                if (f.Variant != null && f.Input != null && f.Input.gameObject.activeInHierarchy)
                {
                    yield return (Cell(f), f.Input.gameObject);
                }
            }
        }

        // Tab from a position field moves on to the next label or field.
        private static void TabFromField()
        {
            if (!TabOrder.Pressed(out var selected))
            {
                return;
            }
            var f = fields.Find(x => x.Variant != null && x.Input != null && x.Input.gameObject == selected);
            if (f != null)
            {
                TabOrder.Select(TabOrder.Next(Cell(f), true));
            }
        }

        private static AlienCharacter ClickedGlyph()
        {
            var cam = Camera.main;
            if (cam == null || !Physics.Raycast(cam.ScreenPointToRay(Input.mousePosition), out var hit))
            {
                return null;
            }
            return hit.transform.GetComponentInParent<AlienCharacter>();
        }

        // Whether the mouse is over one of the position buttons of the shown information panel.
        internal static bool OverButton() => buttonWord != null && IsButtonGlyph(ClickedGlyph());

        // One of the button glyphs: a left panel glyph on the line that holds only them.
        private static bool IsButtonGlyph(AlienCharacter ac)
        {
            if (ac == null || ac.sector != "lefttext" || ac.line_str == null)
            {
                return false;
            }
            var line = string.Join(" ", ac.line_str.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
            return line == ButtonGlyphs(true) || line == ButtonGlyphs(false);
        }
    }
}
