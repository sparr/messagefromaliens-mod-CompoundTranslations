using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

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

        private void Awake()
        {
            Log = Logger;

            QuotedString.Register();
            Separator = Config.Bind("Display", "Separator", new QuotedString(" - "),
                "Text placed between the two parts of a compound word. Surrounding double quotes are removed, so spaces inside them are kept.");
            Brackets = Config.Bind("Display", "Brackets", new QuotedString("()"),
                "Opening and closing brackets around each compound, as one string split in half, e.g. \"()\", \"[]\", or \"<<>>\". \"\" for none.");
            Depth = Config.Bind("Display", "Depth", -1,
                new ConfigDescription(
                    "How many levels of compounds to expand. -1 expands all levels, 0 disables hints, 1 shows only the translations of a word's direct parts.",
                    new AcceptableValueRange<int>(-1, 100)));
            MarkHidden = Config.Bind("Display", "MarkHidden", true,
                "Show ?+ instead of ? for an untranslated word, or the whole label, when it has translated parts that are not shown, such as parts beyond the Depth limit.");

            Config.SettingChanged += (_, _) => CompoundHint.RefreshVisible(null);

            new Harmony(Guid).PatchAll(typeof(Plugin).Assembly);
            Log.LogInfo($"{Name} {Version} loaded");
        }
    }
}
