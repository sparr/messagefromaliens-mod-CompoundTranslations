using System.Collections.Generic;

namespace CompoundTranslations
{
    // The positions each word takes in two-part compounds the player has encountered.
    //
    // Global.InitWordSource maps each word to the pages it appears on, plus each page's chain of
    // parent menus through links the player has unlocked. It runs again whenever a win is
    // recorded. A word appears on a page the player can reach when its list holds a root menu:
    // a parent page that no menu links to. The parts of an encountered compound, at any depth,
    // are encountered too, since they are drawn inside it. For each position, the modifier
    // strings of those compounds are counted, so the panel can draw the position the way the
    // player has usually seen it.
    internal static class EncounteredPositions
    {
        private static Dictionary<string, List<string>> builtFrom;
        private static int builtWins = -1;
        // Word, then slot as named by PositionalTranslations.SlotFor, then modifier string, to
        // the number of encountered compounds.
        private static Dictionary<string, Dictionary<string, Dictionary<string, int>>> slots =
            new Dictionary<string, Dictionary<string, Dictionary<string, int>>>();

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
            var source = Global.word_source;
            var wins = Global.savedata?.win?.Count ?? 0;
            if (source == builtFrom && wins == builtWins)
            {
                return;
            }
            builtFrom = source;
            builtWins = wins;
            slots = new Dictionary<string, Dictionary<string, Dictionary<string, int>>>();
            var words = Global.alien_text;
            if (source == null || words == null)
            {
                return;
            }
            var roots = Roots();
            var encountered = new HashSet<string>();
            var queue = new Queue<string>();
            foreach (var kv in source)
            {
                if (words.ContainsKey(kv.Key) && kv.Value.Exists(roots.Contains) && encountered.Add(kv.Key))
                {
                    queue.Enqueue(kv.Key);
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
            Plugin.Log.LogInfo($"Encountered words: {direct} on reachable pages, {encountered.Count} with their parts, {roots.Count} root menus.");
        }

        // Parent pages that no menu links to.
        private static HashSet<string> Roots()
        {
            var linked = new HashSet<string>();
            if (Global.link_message != null)
            {
                foreach (var links in Global.link_message.Values)
                {
                    linked.UnionWith(links);
                }
            }
            var roots = new HashSet<string>();
            if (Global.isParentpage != null)
            {
                foreach (var p in Global.isParentpage)
                {
                    if (!linked.Contains(p))
                    {
                        roots.Add(p);
                    }
                }
            }
            return roots;
        }
    }
}
