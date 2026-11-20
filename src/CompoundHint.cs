using System;
using System.Collections.Generic;
using UnityEngine;

namespace CompoundTranslations
{
    // AlienCharacter.UpdateEarthText() sets the English label under a glyph from
    // Global.savedata.dict, which holds "?" for untranslated words. For an untranslated
    // word built from two other words, the label becomes "<part 1> - <part 2>" in the
    // dimmed color the game uses for finished items; for a word made of one glyph repeated,
    // it becomes "<n>x <part>". Each part is the part's translation, a bracketed nested hint
    // for an untranslated compound part, or "?". A "?", including a whole label, becomes
    // "?+" when its word has translated parts that the hint does not show.
    internal static class CompoundHint
    {
        // A word built from other words: two different parts, or one part repeated.
        internal sealed class Structure
        {
            public string[] Parts;
            public int Repeat;
            public PosRelation Relation;
        }
        internal const string Unknown = "?";
        internal const string UnknownWithHidden = "?+";

        private static string warnedBrackets;

        private sealed class Context
        {
            public string Separator;
            public string Open;
            public string Close;
            public bool MarkHidden;
            public readonly HashSet<string> Path = new HashSet<string>();
            public readonly Dictionary<string, bool> HasTranslatedPart = new Dictionary<string, bool>();
        }

        internal static bool IsTranslated(string word)
        {
            var dict = Global.savedata?.dict;
            return dict != null && dict.TryGetValue(word, out var t) && t != null && t.Trim() != "" && t != Unknown;
        }

        // Returns the text to show in place of an untranslated word's "?", or null to keep "?".
        // That is a hint when a translated word appears within the configured depth,
        // otherwise "?+" when the word has translated parts at any depth and MarkHidden is on.
        internal static string GetHint(string word)
        {
            if (string.IsNullOrEmpty(word) || IsTranslated(word) || GetStructure(word) == null)
            {
                return null;
            }
            var ctx = NewContext();
            var depth = Plugin.Depth.Value;
            var hint = depth == 0 ? null : Build(word, depth, ctx, !Plugin.OmitOuterBrackets.Value);
            return hint ?? (ctx.MarkHidden && HasTranslatedPart(word, ctx) ? UnknownWithHidden : null);
        }

        // What a hint shows for the word as a part of a compound's hint, when the word has no
        // translation for its position there: its translation, its bracketed nested hint, or
        // "?" or "?+", as for a direct part of a word's own hint.
        internal static string PartFallback(string word)
        {
            if (IsTranslated(word))
            {
                return Global.savedata.dict[word].Trim();
            }
            var ctx = NewContext();
            var depth = Plugin.Depth.Value;
            // A hint expands its direct parts unless Depth stops at them, or turns hints off.
            var text = depth == 0 || depth == 1 ? null : Build(word, depth < 0 ? depth : depth - 1, ctx);
            return text ?? Missing(word, ctx);
        }

        private static Context NewContext()
        {
            GetBrackets(out var open, out var close);
            return new Context
            {
                Separator = Plugin.Separator.Value?.Value ?? "",
                Open = open,
                Close = close,
                MarkHidden = Plugin.MarkHidden.Value,
            };
        }

        // A part's text when neither it nor anything within the levels shown is translated.
        private static string Missing(string part, Context ctx) =>
            ctx.MarkHidden && HasTranslatedPart(part, ctx) ? UnknownWithHidden : Unknown;

        // levels: remaining bracket levels including this one; negative means unlimited.
        // bracket: whether to wrap this level in brackets; nested levels always are.
        // Returns null when no translated word appears within those levels.
        private static string Build(string word, int levels, Context ctx, bool bracket = true)
        {
            var structure = GetStructure(word);
            if (structure == null)
            {
                return null;
            }
            ctx.Path.Add(word);
            var found = false;
            var parts = new string[structure.Parts.Length];
            for (var i = 0; i < parts.Length; i++)
            {
                var part = structure.Parts[i];
                // A positional translation takes the place of the part's own translation.
                var text = PositionalTranslation(structure, i);
                if (text == null && IsTranslated(part))
                {
                    text = Global.savedata.dict[part].Trim();
                }
                else if (text == null && levels != 1 && !ctx.Path.Contains(part))
                {
                    text = Build(part, levels < 0 ? levels : levels - 1, ctx);
                }
                found |= text != null;
                parts[i] = text ?? Missing(part, ctx);
            }
            ctx.Path.Remove(word);
            if (!found)
            {
                return null;
            }
            var inner = structure.Repeat > 0 ? structure.Repeat + "x " + parts[0] : parts[0] + ctx.Separator + parts[1];
            return bracket ? ctx.Open + inner + ctx.Close : inner;
        }

        // Whether any word below this one, at any depth, is translated.
        private static bool HasTranslatedPart(string word, Context ctx)
        {
            if (ctx.HasTranslatedPart.TryGetValue(word, out var known))
            {
                return known;
            }
            var structure = GetStructure(word);
            var result = false;
            if (structure != null && ctx.Path.Add(word))
            {
                for (var i = 0; i < structure.Parts.Length; i++)
                {
                    var part = structure.Parts[i];
                    if (IsTranslated(part) || PositionalTranslation(structure, i) != null || (!ctx.Path.Contains(part) && HasTranslatedPart(part, ctx)))
                    {
                        result = true;
                        break;
                    }
                }
                ctx.Path.Remove(word);
            }
            ctx.HasTranslatedPart[word] = result;
            return result;
        }

        // The part's translation for its position in a two-part compound, or null.
        private static string PositionalTranslation(Structure structure, int i) =>
            structure.Repeat > 0 ? null : PositionalTranslations.Get(structure.Parts[i], PositionalTranslations.SlotFor(structure.Relation, i));

        private static void GetBrackets(out string open, out string close)
        {
            var s = Plugin.Brackets.Value?.Value ?? "";
            if (s.Length % 2 != 0)
            {
                if (warnedBrackets != s)
                {
                    Plugin.Log.LogWarning($"Brackets setting \"{s}\" has an odd length; using no brackets");
                    warnedBrackets = s;
                }
                s = "";
            }
            open = s.Substring(0, s.Length / 2);
            close = s.Substring(s.Length / 2);
        }

        // Returns null unless the word is built from other words. The game draws Double,
        // VerticalDouble, Triple, and Quad words from one component word repeated, and
        // LeftRight and UpDown words from two component words, which may be the same word.
        internal static Structure GetStructure(string word)
        {
            var words = Global.alien_text;
            if (string.IsNullOrEmpty(word) || words == null || !words.TryGetValue(word, out var info) || info.component == null || info.component.Count < 2)
            {
                return null;
            }
            bool IsPart(string w) => !string.IsNullOrEmpty(w) && w != word && words.ContainsKey(w);
            var a = Canonical(info.component[0]);
            var b = Canonical(info.component[1]);
            var repeat = info.characterstruct switch
            {
                PosRelation.Double => 2,
                PosRelation.VerticalDouble => 2,
                PosRelation.Triple => 3,
                PosRelation.Quad => 4,
                _ => 0,
            };
            if (repeat > 0)
            {
                return IsPart(a) ? new Structure { Parts = new[] { a }, Repeat = repeat } : null;
            }
            if (!IsPart(a) || !IsPart(b))
            {
                return null;
            }
            if (a == b && (info.characterstruct == PosRelation.LeftRight || info.characterstruct == PosRelation.UpDown))
            {
                return new Structure { Parts = new[] { a }, Repeat = 2 };
            }
            return new Structure { Parts = new[] { a, b }, Relation = info.characterstruct };
        }

        // The word a component stands for. A few components are separate entries that draw the
        // same glyph as another word and name it in their rename column; the game lists such a
        // component under that word, and so does the hint.
        internal static string Canonical(string part)
        {
            var words = Global.alien_text;
            if (string.IsNullOrEmpty(part) || !words.TryGetValue(part, out var info))
            {
                return part;
            }
            var rename = info.rename;
            return !string.IsNullOrEmpty(rename) && rename != part && words.ContainsKey(rename) ? rename : part;
        }

        // The game's label color and its dimmed color for finished items. The fallbacks copy
        // Draw.DefaultColor(0) and Draw.DefaultColor(3) for when Global.manager is not set yet.
        internal static Color NormalColor() => Global.manager != null ? Global.manager.DefaultColor(0) : Global.FrontColor();

        internal static Color FinishedColor()
        {
            if (Global.manager != null)
            {
                return Global.manager.DefaultColor(3);
            }
            var f = Global.FrontColor();
            var b = Global.BackColor();
            return new Color((f.r + b.r * 1.5f) / 2.5f, (f.g + b.g * 1.5f) / 2.5f, (f.b + b.b * 1.5f) / 2.5f);
        }

        internal static void Apply(AlienCharacter ac)
        {
            if (ac == null || ac.txt == null || !ac.showtxt || ac.txt.isFocused)
            {
                return;
            }
            var label = ac.txt.text ?? "";
            // UpdateEarthText may append a unit or number suffix after the "?", and clears the
            // label entirely when the UI or translations are hidden.
            var hint = label.StartsWith(Unknown) && !Global.testword ? GetHint(ac.alien_txt) : null;
            if (hint != null)
            {
                ac.txt.text = hint + label.Substring(Unknown.Length);
                ac.txt.textComponent.color = FinishedColor();
                Plugin.Log.LogDebug($"hint: {ac.txt.text}");
            }
            else
            {
                // Label slots are reused across pages, so restore the game's only label color.
                ac.txt.textComponent.color = NormalColor();
            }
        }

        // Re-render visible labels of untranslated compounds, whose hints may depend on
        // any word below them.
        internal static void RefreshVisible(AlienCharacter except)
        {
            var slots = Global.manager?.alien_slots;
            if (slots == null)
            {
                return;
            }
            foreach (var ac in slots)
            {
                if (ac != null && ac != except && ac.showtxt && ac.gameObject.activeInHierarchy
                    && GetStructure(ac.alien_txt) != null && !IsTranslated(ac.alien_txt))
                {
                    ac.UpdateEarthText();
                }
            }
        }
    }
}
