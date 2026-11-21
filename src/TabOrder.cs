using System;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;

namespace CompoundTranslations
{
    // Tab moves the selection through the labels in the order Draw.Update's Tab handler uses:
    // rows from the top, cells from the left. Position fields join that order at the cell of the
    // glyph they translate, after that cell's own label. The game's handler knows only its own
    // labels, so Tab into a position field is handled before it, and Tab from a run label or a
    // position field after it.
    //
    // Interface Tweaks can replace the game's handler with its own order, which goes through one
    // panel before the other, wraps around, and goes backward with Shift. While it does, its
    // InterfaceTweaks.TabOrder.Rank gives each cell's place and Backward the direction, and the
    // stops here follow them.
    internal static class TabOrder
    {
        private static bool resolved;
        private static Func<int, int, int> externalRank;
        private static Func<bool> externalBackward;

        // Looked up on first use, once every plugin's assembly has loaded.
        private static void Resolve()
        {
            if (resolved)
            {
                return;
            }
            resolved = true;
            // AccessTools.TypeByName logs a warning when the type is missing, as it is whenever
            // Interface Tweaks is not installed.
            Type type = null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                type = asm.GetType("InterfaceTweaks.TabOrder", false);
                if (type != null)
                {
                    break;
                }
            }
            var rank = type == null ? null : AccessTools.Method(type, "Rank", new[] { typeof(int), typeof(int) });
            var backward = type == null ? null : AccessTools.Method(type, "Backward", Type.EmptyTypes);
            if (rank != null && backward != null)
            {
                externalRank = AccessTools.MethodDelegate<Func<int, int, int>>(rank);
                externalBackward = AccessTools.MethodDelegate<Func<bool>>(backward);
            }
        }

        // Whether this frame's Tab goes backward, as only Interface Tweaks' order does.
        internal static bool Backward() => External(out var back) && back;

        // Whether Interface Tweaks' order applies, and if so, its direction.
        private static bool External(out bool backward)
        {
            Resolve();
            backward = false;
            if (externalRank == null || externalRank(0, 0) < 0)
            {
                return false;
            }
            backward = externalBackward();
            return true;
        }

        private static int frame = -1;
        // The selection when this frame's Tab was first seen, before any handler moved it.
        private static GameObject from;

        // Draw.Update prefix: records the selection Tab acts on, and moves it from a game label
        // into a position field when one comes next.
        internal static void BeforeGameTab()
        {
            var es = EventSystem.current;
            var m = Global.manager;
            if (!Input.GetKeyDown(KeyCode.Tab) || es == null || m?.txt_slots == null || frame == Time.frameCount)
            {
                return;
            }
            frame = Time.frameCount;
            from = es.currentSelectedGameObject;
            if (from == null)
            {
                return;
            }
            for (var x = 0; x < m.txt_slots.GetLength(0); x++)
            {
                for (var y = 0; y < m.txt_slots.GetLength(1); y++)
                {
                    if (m.txt_slots[x, y] != null && m.txt_slots[x, y].gameObject == from)
                    {
                        var next = Next(new Vector2Int(x, y), false);
                        if (next != null && IsPositionField(next))
                        {
                            // The game's handler then finds no label of its own selected.
                            es.SetSelectedGameObject(next);
                        }
                        return;
                    }
                }
            }
        }

        // Whether Tab was pressed this frame and nothing has acted on it yet, and what was
        // selected then.
        internal static bool Pressed(out GameObject selected)
        {
            selected = null;
            var es = EventSystem.current;
            if (!Input.GetKeyDown(KeyCode.Tab) || es == null)
            {
                return false;
            }
            if (frame != Time.frameCount)
            {
                frame = Time.frameCount;
                from = es.currentSelectedGameObject;
            }
            selected = es.currentSelectedGameObject;
            return selected != null && selected == from;
        }

        internal static void Select(GameObject next)
        {
            if (next != null && EventSystem.current != null)
            {
                from = null;
                EventSystem.current.SetSelectedGameObject(next);
            }
        }

        // The next active game label or position field after the cell, or before it for a
        // backward Tab, or null. afterField: the Tab comes from the cell's position field, which
        // follows the cell's label.
        internal static GameObject Next(Vector2Int cell, bool afterField)
        {
            var m = Global.manager;
            if (m?.txt_slots == null)
            {
                return null;
            }
            var width = m.txt_slots.GetLength(0);
            var height = m.txt_slots.GetLength(1);
            var external = External(out var back);
            // Twice the cell's place in Tab order, plus one for a field.
            int Order(Vector2Int c, bool field) =>
                (external ? externalRank(c.x, c.y) : (height - 1 - c.y) * width + c.x) * 2 + (field ? 1 : 0);
            var start = Order(cell, afterField);
            GameObject best = null, wrap = null;
            int bestOrder = 0, wrapOrder = 0;
            // Interface Tweaks' order wraps around from the last stop to the first.
            void Consider(GameObject go, int order)
            {
                if (order == start)
                {
                    return;
                }
                if ((back ? order < start : order > start) && (best == null || (back ? order > bestOrder : order < bestOrder)))
                {
                    best = go;
                    bestOrder = order;
                }
                if (external && (wrap == null || (back ? order > wrapOrder : order < wrapOrder)))
                {
                    wrap = go;
                    wrapOrder = order;
                }
            }
            for (var x = 0; x < width; x++)
            {
                for (var y = 0; y < height; y++)
                {
                    var t = m.txt_slots[x, y];
                    if (t != null && t.gameObject.activeSelf)
                    {
                        Consider(t.gameObject, Order(new Vector2Int(x, y), false));
                    }
                }
            }
            foreach (var (c, go) in PositionalTranslations.TabFields())
            {
                Consider(go, Order(c, true));
            }
            return best ?? wrap;
        }

        private static bool IsPositionField(GameObject go)
        {
            foreach (var (_, f) in PositionalTranslations.TabFields())
            {
                if (f == go)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
