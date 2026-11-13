using BepInEx.Configuration;

namespace CompoundTranslations
{
    // BepInEx trims config values, so a plain string setting loses leading and trailing
    // spaces. This type is written as "value" and read with one pair of surrounding
    // double quotes removed, if present.
    public sealed class QuotedString
    {
        public readonly string Value;

        public QuotedString(string value)
        {
            Value = value ?? "";
        }

        public override string ToString() => "\"" + Value + "\"";

        public override bool Equals(object obj) => obj is QuotedString q && q.Value == Value;

        public override int GetHashCode() => Value.GetHashCode();

        public static QuotedString Parse(string s)
        {
            s = (s ?? "").Trim();
            if (s.Length >= 2 && s[0] == '"' && s[s.Length - 1] == '"')
            {
                s = s.Substring(1, s.Length - 2);
            }
            return new QuotedString(s);
        }

        internal static void Register()
        {
            if (TomlTypeConverter.CanConvert(typeof(QuotedString)))
            {
                return;
            }
            TomlTypeConverter.AddConverter(typeof(QuotedString), new TypeConverter
            {
                ConvertToString = (obj, _) => obj.ToString(),
                ConvertToObject = (str, _) => Parse(str),
            });
        }
    }
}
