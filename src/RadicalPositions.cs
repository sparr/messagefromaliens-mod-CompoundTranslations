using System.Collections.Generic;
using HarmonyLib;
using UnityEngine.EventSystems;

namespace CompoundTranslations
{
    // A word's information panel names its two parts in its composition line, which the game
    // writes as the word, "de radicial be", the first part, "and", and the second part. Each
    // part's glyph there is labeled with the part's translation for its position in the word,
    // when it has one, in place of its own translation, and editing that label edits the
    // positional translation. A part with no positional translation for its position keeps its
    // own label, and editing that edits its own translation as usual.
    internal static class RadicalPositions
    {
        private static readonly string[] Line = { null, "de", "radicial", "be", null, "and", null };

        private sealed class Part
        {
            public string Word;
            public string Slot;
        }

        // The composition line's part glyphs drawn now, with the part and position each shows.
        private static readonly Dictionary<AlienCharacter, Part> parts = new Dictionary<AlienCharacter, Part>();
        // Labels whose edit started on a positional translation, with what that edit saves to.
        private static readonly Dictionary<AlienCharacter, Part> editing = new Dictionary<AlienCharacter, Part>();

        private static readonly AccessTools.FieldRef<AlienCharacter, bool> StartEditThisFrame =
            AccessTools.FieldRefAccess<AlienCharacter, bool>("start_edit_in_this_frame");

        private static string Positional(Part p) => PositionalTranslations.Get(p.Word, p.Slot);

        // Draw.Write postfix: finds the composition line among the left panel's glyphs.
        internal static void AfterWrite(string sec, List<AlienCharacter> drawn)
        {
            if (sec != "lefttext" || drawn == null)
            {
                return;
            }
            var stale = new List<AlienCharacter>();
            foreach (var ac in parts.Keys)
            {
                if (ac == null || ac.sector == "lefttext" || drawn.Contains(ac))
                {
                    stale.Add(ac);
                }
            }
            foreach (var ac in stale)
            {
                parts.Remove(ac);
            }
            for (var i = 0; i + Line.Length <= drawn.Count; i++)
            {
                if (!Matches(drawn, i))
                {
                    continue;
                }
                var structure = CompoundHint.GetStructure(drawn[i].alien_txt);
                if (structure == null || structure.Repeat > 0 || structure.Parts.Length != 2)
                {
                    continue;
                }
                Mark(drawn[i + 4], structure, 0);
                Mark(drawn[i + 6], structure, 1);
            }
        }

        private static bool Matches(List<AlienCharacter> drawn, int i)
        {
            for (var k = 0; k < Line.Length; k++)
            {
                if (drawn[i + k] == null || (Line[k] != null && drawn[i + k].alien_txt != Line[k]))
                {
                    return false;
                }
            }
            return true;
        }

        private static void Mark(AlienCharacter ac, CompoundHint.Structure structure, int i)
        {
            var slot = PositionalTranslations.SlotFor(structure.Relation, i);
            if (slot == null || CompoundHint.Canonical(ac.alien_txt) != structure.Parts[i])
            {
                return;
            }
            parts[ac] = new Part { Word = structure.Parts[i], Slot = slot };
            ac.UpdateEarthText();
        }

        // UpdateEarthText postfix: shows the positional translation in the label.
        internal static void Apply(AlienCharacter ac)
        {
            if (ac?.txt == null || !ac.showtxt || ac.txt.isFocused || ModFields.InUse(ac.txt)
                || Global.hideUi || Global.globalData.hidetranslation || Global.testword
                || !parts.TryGetValue(ac, out var p))
            {
                return;
            }
            var t = Positional(p);
            if (t != null)
            {
                ac.txt.text = t;
                ac.txt.textComponent.color = CompoundHint.NormalColor();
            }
        }

        // StartEdit postfix: starts the edit on the positional translation.
        internal static void AfterStartEdit(AlienCharacter ac, bool ran)
        {
            editing.Remove(ac);
            if (!ran || ac?.txt == null || !parts.TryGetValue(ac, out var p))
            {
                return;
            }
            var t = Positional(p);
            if (t != null)
            {
                editing[ac] = p;
                ac.txt.text = t;
            }
        }

        // LateUpdate prefix: the game puts the word's own translation into a label on the frame
        // its edit starts; this keeps the positional one there.
        internal static void BeforeLateUpdate(AlienCharacter ac)
        {
            if (editing.TryGetValue(ac, out var p) && StartEditThisFrame(ac))
            {
                StartEditThisFrame(ac) = false;
                ac.txt.text = Positional(p) ?? ac.txt.text;
            }
        }

        // EndEdit prefix: saves a positional edit in place of the game's save of the word's own
        // translation. Returns whether it did.
        internal static bool EndEdit(AlienCharacter ac, string s)
        {
            if (ac == null || !editing.TryGetValue(ac, out var p))
            {
                return false;
            }
            editing.Remove(ac);
            PositionalTranslations.Save(p.Word, p.Slot, s);
            var es = EventSystem.current;
            if (!ac.txt.isFocused && es != null && !es.alreadySelecting && es.currentSelectedGameObject == ac.txt.gameObject)
            {
                es.SetSelectedGameObject(null);
            }
            ac.UpdateEarthText();
            return true;
        }
    }
}
