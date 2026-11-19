using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace CompoundTranslations
{
    [HarmonyPatch(typeof(AlienCharacter), nameof(AlienCharacter.UpdateEarthText))]
    internal static class UpdateEarthTextPatch
    {
        private static void Postfix(AlienCharacter __instance)
        {
            try
            {
                CompoundHint.Apply(__instance);
                SequenceTranslations.AfterUpdateEarthText(__instance);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"UpdateEarthText postfix failed: {e}");
            }
        }
    }

    [HarmonyPatch(typeof(AlienCharacter), nameof(AlienCharacter.StartEdit))]
    internal static class StartEditPatch
    {
        // Shift+click extends the selection into a run; clicking a glyph inside a run edits the run.
        private static bool Prefix(AlienCharacter __instance)
        {
            try
            {
                return SequenceTranslations.BeforeStartEdit(__instance);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"StartEdit prefix failed: {e}");
                return true;
            }
        }

        // StartEdit clears the label for untranslated words; type in the normal color.
        private static void Postfix(AlienCharacter __instance)
        {
            if (__instance?.txt != null)
            {
                __instance.txt.textComponent.color = CompoundHint.NormalColor();
            }
        }
    }

    [HarmonyPatch(typeof(AlienCharacter), nameof(AlienCharacter.EndEdit))]
    internal static class EndEditPatch
    {
        // Skip the save when the edit was replaced by a run's edit, so the label text the
        // field still holds is not written as a translation.
        private static bool Prefix(AlienCharacter __instance, out bool __state)
        {
            __state = SequenceTranslations.IsSuppressed(__instance);
            return !__state;
        }

        // The game only refreshes labels of the edited word; also refresh visible compounds.
        private static void Postfix(AlienCharacter __instance, bool __state)
        {
            if (__state)
            {
                return;
            }
            try
            {
                SequenceTranslations.AfterGlyphEndEdit(__instance);
                CompoundHint.RefreshVisible(__instance);
                // An open positions panel shows the word's translation as its fallback.
                PositionalTranslations.MarkDirty();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"EndEdit postfix failed: {e}");
            }
        }
    }

    // Any redraw of glyphs makes the sequence scan stale.
    [HarmonyPatch]
    internal static class WritePatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (var m in AccessTools.GetDeclaredMethods(typeof(Draw)))
            {
                if (m.Name == nameof(Draw.Write) || m.Name == nameof(Draw.ClearSelected))
                {
                    yield return m;
                }
            }
        }

        private static void Postfix() => SequenceTranslations.MarkDirty();
    }

    // A redraw starts by clearing the screen's lines: end a run label's edit first, while its
    // run is still known.
    [HarmonyPatch(typeof(Draw), nameof(Draw.ClearSelected))]
    internal static class ClearSelectedPatch
    {
        private static void Prefix()
        {
            try
            {
                SequenceTranslations.BeforeRedraw();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"ClearSelected prefix failed: {e}");
            }
        }
    }

    // The game gives the cursor the color of nothing clickable for the position buttons; give it
    // the clickable color instead.
    [HarmonyPatch(typeof(HardwareCursor), nameof(HardwareCursor.Apply))]
    internal static class CursorPatch
    {
        private static void Prefix(ref UnityEngine.Color c)
        {
            try
            {
                var d = Global.manager;
                if (d != null && c == d.DefaultColor(5) && PositionalTranslations.OverButton())
                {
                    c = d.DefaultColor(0);
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Cursor prefix failed: {e}");
            }
        }
    }

    // A word's information text gets the glyphs that open its position translations.
    [HarmonyPatch(typeof(Draw), nameof(Draw.PushLeftText))]
    internal static class PushLeftTextPatch
    {
        private static void Prefix(string title, ref string info)
        {
            try
            {
                PositionalTranslations.Decorate(title, ref info);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"PushLeftText: {e}");
            }
        }
    }

    // A redraw may change the left panel, so the position button and panel are rebuilt.
    [HarmonyPatch(typeof(Draw), nameof(Draw.UpdateText))]
    internal static class UpdateTextPatch
    {
        private static void Postfix() => PositionalTranslations.MarkDirty();
    }
}
