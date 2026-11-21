using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;

namespace CompoundTranslations
{
    // The choice glyphs of a settings page: the separate clickable options after a setting's
    // name. Each is its own item, so it never joins a run, though the setting's name still can.
    //
    // On the game's settings page these are the glyphs Draw.Update's click handler acts on, other
    // than the reset line, whose whole line is one button. On Mod Settings' pages they are the
    // glyphs in ModSettings.SettingsPages.choices while the page they were laid out for is open,
    // the same test its own click handler makes.
    internal static class SettingOptions
    {
        private const string GamePage = "modify game";

        private static bool resolved;
        private static IList modChoices;
        private static FieldInfo choiceGlyph;
        private static FieldInfo modShown;
        private static MethodInfo modOpenPage;

        private static void Resolve()
        {
            if (resolved)
            {
                return;
            }
            resolved = true;
            // AccessTools.TypeByName logs a warning when the type is missing, as it is whenever
            // Mod Settings is not installed.
            Type type = null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                type = asm.GetType("ModSettings.SettingsPages", false);
                if (type != null)
                {
                    break;
                }
            }
            if (type == null)
            {
                return;
            }
            var choices = AccessTools.Field(type, "choices");
            var choice = AccessTools.Inner(type, "Choice");
            choiceGlyph = choice == null ? null : AccessTools.Field(choice, "Glyph");
            modShown = AccessTools.Field(type, "shown");
            modOpenPage = AccessTools.Method(type, "OpenPage", new[] { typeof(Draw) });
            modChoices = choices?.GetValue(null) as IList;
            if (modChoices == null || choiceGlyph == null || modShown == null || modOpenPage == null)
            {
                modChoices = null;
                Plugin.Log.LogWarning("Mod Settings' choice glyphs were not found; its option glyphs may form runs.");
            }
        }

        // The option glyphs on the page now drawn, or null when it has none.
        internal static Func<AlienCharacter, bool> For(Draw draw)
        {
            if (draw == null)
            {
                return null;
            }
            if (draw.current_page == GamePage)
            {
                return IsGameOption;
            }
            Resolve();
            if (modChoices == null || modChoices.Count == 0)
            {
                return null;
            }
            var shown = modShown.GetValue(null);
            if (shown == null || !ReferenceEquals(shown, modOpenPage.Invoke(null, new object[] { draw })))
            {
                return null;
            }
            var glyphs = new System.Collections.Generic.HashSet<AlienCharacter>();
            foreach (var c in modChoices)
            {
                if (choiceGlyph.GetValue(c) is AlienCharacter ac)
                {
                    glyphs.Add(ac);
                }
            }
            return ac => ac.sector == "righttextdown" && glyphs.Contains(ac);
        }

        // Draw.Update's click and cursor tests for the settings page, without the reset line.
        private static bool IsGameOption(AlienCharacter ac)
        {
            if (ac.sector != "righttextdown" || ac.line_str == null)
            {
                return false;
            }
            var line = ac.line_str;
            var word = ac.alien_txt;
            if (line.StartsWith("window", StringComparison.Ordinal))
            {
                return word != "window";
            }
            if (line.StartsWith("halfscreen area", StringComparison.Ordinal))
            {
                return word != "halfscreen" && word != "area";
            }
            if (line.StartsWith("word color", StringComparison.Ordinal))
            {
                return word != "word" && word != "color";
            }
            if (line.StartsWith("data warehouse", StringComparison.Ordinal))
            {
                return word != "warehouse" && word != "data";
            }
            if (line.StartsWith("tohide", StringComparison.Ordinal) || line.StartsWith("translate", StringComparison.Ordinal))
            {
                return word == "yes" || word == "no";
            }
            return false;
        }
    }
}
