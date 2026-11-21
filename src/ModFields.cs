using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace CompoundTranslations
{
    // The mod's own text fields, run labels and position fields, made from the game's label
    // prefab and placed and sized the way the game places its labels.
    internal static class ModFields
    {
        // A plain text field in the labels' font. It lets go of the selection after onEndEdit.
        internal static TMP_InputField Create(Draw m, string name, string what, Action onSelect, Action<string> onEndEdit)
        {
            var go = UnityEngine.Object.Instantiate(m.text_prefab);
            go.name = name;
            go.transform.SetParent(m.world_canva.transform);
            var field = go.GetComponent<TMP_InputField>();
            var model = m.txt_slots?[0, 0];
            if (model != null)
            {
                field.fontAsset = model.fontAsset;
            }
            field.characterLimit = 200;
            field.richText = false;
            field.onSelect.RemoveAllListeners();
            field.onEndEdit.RemoveAllListeners();
            // Arrow keys stay in the field instead of moving the selection to a nearby label.
            var nav = field.navigation;
            nav.mode = UnityEngine.UI.Navigation.Mode.None;
            field.navigation = nav;
            field.onSelect.AddListener(_ => onSelect());
            field.onEndEdit.AddListener(s =>
            {
                try
                {
                    onEndEdit(s);
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError($"{what} edit failed: {e}");
                }
                finally
                {
                    Release(field);
                }
            });
            return field;
        }

        // A field the player is using or about to: focused, or selected with its activation
        // still queued, as for the frame after a click.
        internal static bool InUse(TMP_InputField field) =>
            field != null && (field.isFocused || (EventSystem.current != null && EventSystem.current.currentSelectedGameObject == field.gameObject));

        // An edit that ends without a click, as on Enter or Escape, leaves the field selected,
        // so the next key would act on it. The game's labels let go the same way in
        // AlienCharacter.EndEdit.
        internal static void Release(TMP_InputField field)
        {
            var es = EventSystem.current;
            if (field != null && es != null && !es.alreadySelecting && !field.isFocused && es.currentSelectedGameObject == field.gameObject)
            {
                es.SetSelectedGameObject(null);
            }
        }

        internal static Vector2 CellSize(Camera cam, Draw m) =>
            (Vector2)cam.WorldToScreenPoint(m.alien_slots[1, 1].transform.position) - (Vector2)cam.WorldToScreenPoint(m.alien_slots[0, 0].transform.position);

        // Places a field by its bottom-left corner, as Draw.ResetTextPosition places a glyph's
        // label, and scales its font as AlienCharacter.UpdatePos does every frame.
        internal static void Place(TMP_InputField field, Camera cam, Vector3 corner, Vector2 size, Vector2 cell)
        {
            field.transform.position = (Vector2)cam.WorldToScreenPoint(corner);
            ((RectTransform)field.transform).sizeDelta = size;
            field.textComponent.fontSizeMax = 0.3f * cell.x;
            field.textComponent.textWrappingMode = TextWrappingModes.Normal;
        }
    }
}
