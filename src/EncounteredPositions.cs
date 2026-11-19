using System.Collections.Generic;

namespace CompoundTranslations
{
    // The positions each word takes in two-part compounds the player has encountered.
    //
    // A word is encountered once the game has drawn its glyph: AlienCharacter.UpdateEarthText
    // adds every drawn word to Global.savedata.dict, as "?" until it is translated, wherever it
    // is drawn, in pages, menus, definitions, quiz supplies, and the dictionary. The parts of an
    // encountered compound, at any depth, are encountered too, since they are drawn inside it.
    // For each position, the modifier strings of those compounds are counted, so the panel can
    // draw the position the way the player has usually seen it.
    internal static class EncounteredPositions
    {
        // The dictionary the table was built from, and how many keys it had: drawing a new word
        // adds one.
        private static Dictionary<string, string> builtFrom;
        private static int builtKeys = -1;
        // Word, then slot as named by PositionalTranslations.SlotFor, then modifier string, to
        // the number of encountered compounds.
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

        // The most common modifier string of the encountered compounds where the word fills the
        // slot, or "" when there are none.
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

        private static void Refresh()
        {
            var dict = Global.savedata?.dict;
            var keys = dict?.Count ?? 0;
            if (dict == builtFrom && keys == builtKeys)
            {
                return;
            }
            builtFrom = dict;
            builtKeys = keys;
            version++;
            slots = new Dictionary<string, Dictionary<string, Dictionary<string, int>>>();
            var words = Global.alien_text;
            if (dict == null || words == null)
            {
                return;
            }
            var encountered = new HashSet<string>();
            var queue = new Queue<string>();
            foreach (var key in dict.Keys)
            {
                if (words.ContainsKey(key) && encountered.Add(key))
                {
                    queue.Enqueue(key);
                }
            }
            var direct = encountered.Count;
            while (queue.Count > 0)
            {
                var info = words[queue.Dequeue()];
                if (info.component == null)
                {
                    continue;
                }
                var parts = new List<string>();
                foreach (var c in info.component)
                {
                    var part = CompoundHint.Canonical(c);
                    if (!string.IsNullOrEmpty(part) && words.ContainsKey(part))
                    {
                        parts.Add(part);
                        if (encountered.Add(part))
                        {
                            queue.Enqueue(part);
                        }
                    }
                }
                // Positions count only where hints use them: two different parts.
                if (info.component.Count < 2 || parts.Count < 2 || parts[0] == parts[1]
                    || CompoundHint.Canonical(info.component[0]) != parts[0])
                {
                    continue;
                }
                for (var i = 0; i < 2; i++)
                {
                    var slot = PositionalTranslations.SlotFor(info.characterstruct, i);
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
                    var mod = info.struct_modifier ?? "";
                    mods[mod] = mods.TryGetValue(mod, out var n) ? n + 1 : 1;
                }
            }
            Plugin.Log.LogDebug($"Encountered words: {direct} drawn, {encountered.Count} with their parts.");
        }
    }
}
