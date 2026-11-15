using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

// The mod's name in glyphs, as game word IDs, for Mod Settings and any other mod that lists mods.
[assembly: AssemblyMetadata("MessageFromAliens.Name", "translate radicial")]

namespace CompoundTranslations
{
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "sparr.messagefromaliens.compoundtranslations";
        public const string Name = "Compound Translations";
        public const string Version = "0.1.0";

        internal static ManualLogSource Log;
        internal static ConfigEntry<QuotedString> Separator;
        internal static ConfigEntry<QuotedString> Brackets;
        internal static ConfigEntry<int> Depth;
        internal static ConfigEntry<bool> MarkHidden;
        internal static ConfigEntry<bool> OmitOuterBrackets;

        private void Awake()
        {
            Log = Logger;

            QuotedString.Register();
            Separator = Config.Bind("Display", "Separator", new QuotedString(" - "),
                Described("Text placed between the two parts of a compound word. Surrounding double quotes are removed, so spaces inside them are kept.",
                    "symbol between radicial", quoted: true));
            Brackets = Config.Bind("Display", "Brackets", new QuotedString("()"),
                Described("Opening and closing brackets around each compound, as one string split in half, e.g. \"()\", \"[]\", or \"<<>>\". \"\" for none.",
                    "radicial de edge", quoted: true));
            OmitOuterBrackets = Config.Bind("Display", "OmitOuterBrackets", true,
                Described("Leave out the brackets around the whole label, keeping them around nested compounds.", "tohide word de edge"));
            Depth = Config.Bind("Display", "Depth", -1,
                Described(
                    "How many levels of compounds to expand. -1 expands all levels, 0 disables hints, 1 shows only the translations of a word's direct parts.",
                    "radicial de depth", new AcceptableValueRange<int>(-1, 100)));
            MarkHidden = Config.Bind("Display", "MarkHidden", true,
                Described("Show ?+ instead of ? for an untranslated word, or the whole label, when it has translated parts that are not shown, such as parts beyond the Depth limit.",
                    "mark tohide de radicial"));

            Config.SettingChanged += (_, _) => CompoundHint.RefreshVisible(null);

            new Harmony(Guid).PatchAll(typeof(Plugin).Assembly);
            Log.LogInfo($"{Name} {Version} loaded");
        }

        // Mod Settings shows a setting tagged with its name in glyphs, as game word IDs.
        // Quoted marks a QuotedString setting, so Mod Settings edits it without the quotes.
        private static ConfigDescription Described(string description, string glyphs, AcceptableValueBase values = null, bool quoted = false)
        {
            var tags = new Dictionary<string, string> { ["MessageFromAliens.Label"] = glyphs };
            if (quoted)
            {
                tags["MessageFromAliens.Quoted"] = "true";
            }
            return new ConfigDescription(description, values, tags);
        }
    }
}
