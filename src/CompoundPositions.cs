using System.Collections.Generic;

namespace CompoundTranslations
{
    // The positions each word takes in the game's two-part compounds, whether or not the player
    // has seen them. For each position, the modifier strings of those compounds are counted, so
    // the panel can draw the position the way the game usually draws it.
    internal static class CompoundPositions
    {
        // The word table the table was built from, and how many words it had.
        private static Dictionary<string, AlienInfo> builtFrom;
        private static int builtWords = -1;
        // Word, then slot as named by PositionalTranslations.SlotFor, then modifier string, to
        // the number of compounds.
        private static Dictionary<string, Dictionary<string, Dictionary<string, int>>> slots =
            new Dictionary<string, Dictionary<string, Dictionary<string, int>>>();
        private static int version;

        // Changes whenever the table is rebuilt, so callers can tell when the words with
        // positions may have changed.
        internal static int Version
        {
            get
            {
                Refresh();
                return version;
            }
        }

        internal static bool Has(string word, string slot)
        {
            Refresh();
            return slots.TryGetValue(CompoundHint.Canonical(word), out var s) && s.ContainsKey(slot);
        }

        internal static bool HasAny(string word)
        {
            Refresh();
            return slots.TryGetValue(CompoundHint.Canonical(word), out var s) && s.Count > 0;
        }

        // The most common modifier string of the compounds where the word fills the slot, or ""
        // when there are none.
        internal static string Modifier(string word, string slot)
        {
            Refresh();
            var best = "";
            var count = 0;
            if (slots.TryGetValue(CompoundHint.Canonical(word), out var s) && s.TryGetValue(slot, out var mods))
            {
                foreach (var kv in mods)
                {
                    if (kv.Value > count || (kv.Value == count && string.CompareOrdinal(kv.Key, best) < 0))
                    {
                        best = kv.Key;
                        count = kv.Value;
                    }
                }
            }
            return best;
        }

        // Builds the table once per word table, which content packs can change when the game
        // reads its text again.
        private static void Refresh()
        {
            var words = Global.alien_text;
            var count = words?.Count ?? 0;
            if (words == builtFrom && count == builtWords)
            {
                return;
            }
            builtFrom = words;
            builtWords = count;
            version++;
            slots = new Dictionary<string, Dictionary<string, Dictionary<string, int>>>();
            if (words == null)
            {
                return;
            }
            foreach (var kv in words)
            {
                // Positions count only where hints use them: compounds of two parts, which
                // CompoundHint.GetStructure keeps apart unless it reads them as one part repeated.
                var structure = CompoundHint.GetStructure(kv.Key);
                if (structure == null || structure.Repeat > 0)
                {
                    continue;
                }
                var parts = structure.Parts;
                for (var i = 0; i < 2; i++)
                {
                    var slot = PositionalTranslations.SlotFor(structure.Relation, i);
                    if (slot == null)
                    {
                        continue;
                    }
                    if (!slots.TryGetValue(parts[i], out var bySlot))
                    {
                        slots[parts[i]] = bySlot = new Dictionary<string, Dictionary<string, int>>();
                    }
                    if (!bySlot.TryGetValue(slot, out var mods))
                    {
                        bySlot[slot] = mods = new Dictionary<string, int>();
                    }
                    var mod = kv.Value.struct_modifier ?? "";
                    mods[mod] = mods.TryGetValue(mod, out var n) ? n + 1 : 1;
                }
            }
            Plugin.Log.LogDebug($"Compound positions: {slots.Count} words take a position in {count} words.");
        }
    }
}
