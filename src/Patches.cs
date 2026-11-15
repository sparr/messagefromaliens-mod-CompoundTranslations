using System;
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
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"UpdateEarthText postfix failed: {e}");
            }
        }
    }

    // StartEdit clears the label for untranslated words; type in the normal color.
    [HarmonyPatch(typeof(AlienCharacter), nameof(AlienCharacter.StartEdit))]
    internal static class StartEditPatch
    {
        private static void Postfix(AlienCharacter __instance)
        {
            if (__instance?.txt != null)
            {
                __instance.txt.textComponent.color = CompoundHint.NormalColor();
            }
        }
    }

    // The game only refreshes labels of the edited word; also refresh visible compounds.
    [HarmonyPatch(typeof(AlienCharacter), nameof(AlienCharacter.EndEdit))]
    internal static class EndEditPatch
    {
        private static void Postfix(AlienCharacter __instance)
        {
            try
            {
                CompoundHint.RefreshVisible(__instance);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"EndEdit postfix failed: {e}");
            }
        }
    }
}
