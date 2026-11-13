using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace CompoundTranslations
{
    // AlienCharacter.UpdateEarthText() sets the English label under a glyph from
    // Global.savedata.dict, which holds "?" for untranslated words. For an untranslated
    // word built from two other words, the label becomes "(<part 1> - <part 2>)" in the
    // dimmed color the game uses for finished items. Each part is the part's translation,
    // a nested hint for an untranslated compound part, or "?". A "?", including a whole
    // label, becomes "?+" when its word has translated parts that the hint does not show.
    internal static class CompoundHint
    {
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
            if (string.IsNullOrEmpty(word) || IsTranslated(word) || GetCompound(word) == null)
            {
                return null;
            }
            GetBrackets(out var open, out var close);
            var ctx = new Context
            {
                Separator = Plugin.Separator.Value?.Value ?? "",
                Open = open,
                Close = close,
                MarkHidden = Plugin.MarkHidden.Value,
            };
            var depth = Plugin.Depth.Value;
            var hint = depth == 0 ? null : Build(word, depth, ctx, !Plugin.OmitOuterBrackets.Value);
            return hint ?? (ctx.MarkHidden && HasTranslatedPart(word, ctx) ? UnknownWithHidden : null);
        }

        // levels: remaining bracket levels including this one; negative means unlimited.
        // bracket: whether to wrap this level in brackets; nested levels always are.
        // Returns null when no translated word appears within those levels.
        private static string Build(string word, int levels, Context ctx, bool bracket = true)
        {
            var info = GetCompound(word);
            if (info == null)
            {
                return null;
            }
            ctx.Path.Add(word);
            var found = false;
            var parts = new string[2];
            for (var i = 0; i < 2; i++)
            {
                var part = info.component[i];
                string text = null;
                if (IsTranslated(part))
                {
                    text = Global.savedata.dict[part].Trim();
                }
                else if (levels != 1 && !ctx.Path.Contains(part))
                {
                    text = Build(part, levels < 0 ? levels : levels - 1, ctx);
                }
                found |= text != null;
                parts[i] = text ?? (ctx.MarkHidden && HasTranslatedPart(part, ctx) ? UnknownWithHidden : Unknown);
            }
            ctx.Path.Remove(word);
            if (!found)
            {
                return null;
            }
            var inner = parts[0] + ctx.Separator + parts[1];
            return bracket ? ctx.Open + inner + ctx.Close : inner;
        }

        // Whether any word below this one, at any depth, is translated.
        private static bool HasTranslatedPart(string word, Context ctx)
        {
            if (ctx.HasTranslatedPart.TryGetValue(word, out var known))
            {
                return known;
            }
            var info = GetCompound(word);
            var result = false;
            if (info != null && ctx.Path.Add(word))
            {
                foreach (var part in info.component)
                {
                    if (IsTranslated(part) || (!ctx.Path.Contains(part) && HasTranslatedPart(part, ctx)))
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

        // A compound has two components that are both other words.
        internal static AlienInfo GetCompound(string word)
        {
            var words = Global.alien_text;
            if (string.IsNullOrEmpty(word) || words == null || !words.TryGetValue(word, out var info) || info.component == null || info.component.Count < 2)
            {
                return null;
            }
            var a = info.component[0];
            var b = info.component[1];
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b) || a == word || b == word)
            {
                return null;
            }
            return words.ContainsKey(a) && words.ContainsKey(b) ? info : null;
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
                    && GetCompound(ac.alien_txt) != null && !IsTranslated(ac.alien_txt))
                {
                    ac.UpdateEarthText();
                }
            }
        }
    }

    [HarmonyPatch(typeof(AlienCharacter), nameof(AlienCharacter.UpdateEarthText))]
    internal static class UpdateEarthTextPatch
    {
        private static void Postfix(AlienCharacter __instance)
        {
            try
            {
                CompoundHint.Apply(__instance);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"UpdateEarthText postfix failed: {e}");
            }
        }
    }

    // StartEdit clears the label for untranslated words; type in the normal color.
    [HarmonyPatch(typeof(AlienCharacter), nameof(AlienCharacter.StartEdit))]
    internal static class StartEditPatch
    {
        private static void Postfix(AlienCharacter __instance)
        {
            if (__instance?.txt != null)
            {
                __instance.txt.textComponent.color = CompoundHint.NormalColor();
            }
        }
    }

    // The game only refreshes labels of the edited word; also refresh visible compounds.
    [HarmonyPatch(typeof(AlienCharacter), nameof(AlienCharacter.EndEdit))]
    internal static class EndEditPatch
    {
        private static void Postfix(AlienCharacter __instance)
        {
            try
            {
                CompoundHint.RefreshVisible(__instance);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"EndEdit postfix failed: {e}");
            }
        }
    }
}
