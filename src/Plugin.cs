using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

// The mod's name in glyphs, as game word IDs, for Mod Settings and any other mod that lists mods.
[assembly: AssemblyMetadata("MessageFromAliens.Name", "translate radicial")]

namespace CompoundTranslations
{
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "sparr.messagefromaliens.compoundtranslations";
        public const string Name = "Compound Translations";
        public const string Version = "0.3.1";

        // The words, as game word IDs, that name position translations: the setting's label in
        // Mod Settings and the button on a glyph's information panel.
        internal const string PositionGlyphs = "translate word position";

        // Between the glyph and PositionGlyphs in the position translations panel's title.
        internal const string PossessiveGlyph = "de";

        // Shown after PositionGlyphs on a glyph's information panel: whether the glyph has any
        // position translations.
        internal const string PositionsSetGlyph = "yes";
        internal const string PositionsUnsetGlyph = "no";

        internal static ManualLogSource Log;

        // Whether the game shows translations: neither the interface nor translations are hidden.
        internal static bool Visible => !Global.hideUi && !(Global.globalData?.hidetranslation ?? false);

        // The glyph under the mouse, or null.
        internal static AlienCharacter GlyphUnderMouse()
        {
            var cam = Camera.main;
            return cam != null && Physics.Raycast(cam.ScreenPointToRay(Input.mousePosition), out var hit) ? hit.transform.GetComponentInParent<AlienCharacter>() : null;
        }
        internal static ConfigEntry<QuotedString> Separator;
        internal static ConfigEntry<QuotedString> Brackets;
        internal static ConfigEntry<int> Depth;
        internal static ConfigEntry<bool> MarkHidden;
        internal static ConfigEntry<bool> OmitOuterBrackets;
        internal static ConfigEntry<bool> ShowWordType;
        internal static ConfigEntry<bool> Sequences;
        internal static ConfigEntry<bool> RevealOnHover;
        internal static ConfigEntry<bool> Positions;

        private void Awake()
        {
            Log = Logger;

            QuotedString.Register();
            Separator = Config.Bind("Display", "Separator", new QuotedString(" - "),
                Described("Text placed between the two parts of a compound word. Surrounding double quotes are removed, so spaces inside them are kept.",
                    "symbol between radicial", quoted: true));
            Brackets = Config.Bind("Display", "Brackets", new QuotedString("[]"),
                Described("Opening and closing brackets around each compound, as one string split in half, e.g. \"[]\", \"()\", or \"<<>>\". \"\" for none.",
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

            ShowWordType = Config.Bind("Display", "ShowWordType", true,
                Described("Append the compound's word type in parentheses, as shown on its information panel, to a hint when the type has a translation.",
                    "see noun verb adj"));

            Sequences = Config.Bind("Sequences", "Enabled", true,
                Described("Allow translations of runs of glyphs: while editing a label, shift+click another label in the same sentence.",
                    "translate word de sequence"));
            RevealOnHover = Config.Bind("Sequences", "RevealOnHover", true,
                Described("While the mouse is over a glyph inside a run, show the glyphs' own labels instead of the run's translation.",
                    "near see word"));

            Positions = Config.Bind("Positions", "Enabled", true,
                Described("Add a button to a glyph's information panel that opens translations for its positions in compounds: left or right, top or bottom, and outside or inside. Hints show those in place of its translation.",
                    PositionGlyphs));

            Config.SettingChanged += (_, _) =>
            {
                CompoundHint.RefreshVisible(null);
                SequenceTranslations.MarkChanged();
                PositionalTranslations.SettingChanged();
            };

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

        // Plugin objects must not add components or start coroutines from Awake, which runs
        // during Application's static constructor; Unity crashes. LateUpdate is safe.
        private void LateUpdate()
        {
            SequenceTranslations.Tick();
            PositionalTranslations.Tick();
        }
    }
}
