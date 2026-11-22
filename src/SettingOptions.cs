using System;
using System.Reflection;

namespace CompoundTranslations
{
    // The choice glyphs of a settings page: the separate clickable options after a setting's
    // name. Each is its own item, so it never joins a run, though the setting's name still can.
    //
    // On the game's settings page these are the glyphs Draw.Update's click handler acts on, other
    // than the reset line, whose whole line is one button. On Mod Settings' pages they are the
    // glyphs its public ModSettings.Api.IsChoiceGlyph accepts.
    internal static class SettingOptions
    {
        private const string GamePage = "modify game";

        private static bool resolved;
        private static Func<AlienCharacter, bool> modIsChoice;

        // Looked up once, when first needed, after every plugin has loaded. Without Mod Settings,
        // or with a version older than its Api class, its pages' options are not kept out of runs.
        private static void Resolve()
        {
            if (resolved)
            {
                return;
            }
            resolved = true;
            // AccessTools.TypeByName logs a warning when the type is missing, as it is whenever
            // Mod Settings is not installed.
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = asm.GetType("ModSettings.Api", false);
                var method = type?.GetMethod("IsChoiceGlyph", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(AlienCharacter) }, null);
                if (method != null && method.ReturnType == typeof(bool))
                {
                    modIsChoice = (Func<AlienCharacter, bool>)Delegate.CreateDelegate(typeof(Func<AlienCharacter, bool>), method);
                    return;
                }
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
            return modIsChoice;
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
