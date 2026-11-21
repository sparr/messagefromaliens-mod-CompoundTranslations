namespace CompoundTranslations
{
    // The mod's values in Global.savedata.dict, each under a key with one of the mod's prefixes.
    internal static class SavedValues
    {
        // A value of "?", which the game writes for an untranslated word, is no translation.
        internal static bool IsBlank(string value) => string.IsNullOrWhiteSpace(value) || value.Trim() == "?";

        // The value saved under the key, trimmed, or null when there is none or it is blank.
        internal static string Get(string key)
        {
            var dict = Global.savedata?.dict;
            return dict != null && dict.TryGetValue(key, out var t) && !IsBlank(t) ? t.Trim() : null;
        }

        // Saves the value under the key, trimmed, or removes the key when the value is blank, and
        // saves the game when that changed the dictionary. Returns whether it did.
        internal static bool Store(string key, string value)
        {
            var dict = Global.savedata?.dict;
            if (dict == null)
            {
                return false;
            }
            var t = IsBlank(value) ? "" : value.Trim();
            var had = dict.TryGetValue(key, out var old);
            if (t == "" ? !had : old == t)
            {
                return false;
            }
            if (t == "")
            {
                dict.Remove(key);
            }
            else
            {
                dict[key] = t;
            }
            Global.SaveGame();
            return true;
        }
    }
}
