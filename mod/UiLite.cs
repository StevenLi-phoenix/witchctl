using System;
using System.Collections.Generic;
using System.Linq;
using Michsky.MUIP;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Witch.UI;
using Witch.UI.Window;

namespace WitchCtl
{
    /// <summary>
    /// Fast replacement for the game's own UI snapshot/interaction service (800 ms / 2.4 s per call, because it
    /// scores every node and scans all GameObjects). Walks only the active windows, once, and dispatches a plain
    /// pointer click straight to the target.
    /// </summary>
    internal static class UiLite
    {
        internal sealed class Clickable
        {
            public int id;
            public string window;
            public string path;
            public string label;
            public bool interactable;
            /// <summary>Why it may not be clicked (<see cref="Danger.Blocked"/>), or null. Blocked ones are listed apart in the dump.</summary>
            [Newtonsoft.Json.JsonIgnore] public string blocked;
            [Newtonsoft.Json.JsonIgnore] public GameObject go;
        }

        internal static object Dump()
        {
            var windows = new List<object>();
            foreach (var (name, root) in Roots())
            {
                var every = Clickables(root, name);
                var clickables = every.Where(c => c.blocked == null).ToList();
                var blocked = every.Where(c => c.blocked != null).Select(c => new { c.path, c.label, reason = c.blocked }).ToList();
                var inside = new HashSet<TMP_Text>();
                foreach (var c in every)
                    foreach (var t in c.go.GetComponentsInChildren<TMP_Text>(false)) inside.Add(t);

                var texts = root.GetComponentsInChildren<TMP_Text>(false)
                    .Where(t => !inside.Contains(t) && !string.IsNullOrWhiteSpace(t.text))
                    .Select(t => Clean(t.text))
                    .Distinct()
                    .ToList();

                windows.Add(new { name, clickables, blocked, texts });
            }
            return new { windows };
        }

        /// <summary>Clicks the single clickable matching <paramref name="id"/>, <paramref name="path"/> or <paramref name="text"/>.</summary>
        internal static object Click(int? id, string path, string text, bool right = false)
        {
            var all = Roots().SelectMany(r => Clickables(r.root, r.name)).ToList();
            var matches = all.Where(c =>
                id.HasValue ? c.id == id.Value :
                !string.IsNullOrEmpty(path) ? c.path == path || c.path.EndsWith("/" + path, StringComparison.Ordinal) :
                !string.IsNullOrEmpty(text) && c.label == text).ToList();

            if (matches.Count == 0)
                return new { ok = false, error = "no_match", message = "no active clickable matches", candidates = all.Select(Brief).ToList() };
            if (matches.Count > 1)
                return new { ok = false, error = "ambiguous", message = "more than one clickable matches; use id", candidates = matches.Select(Brief).ToList() };

            var target = matches[0];
            if (target.blocked != null)
            {
                UnityEngine.Debug.LogWarning("[WitchCtl] click refused (" + target.blocked + "): " + target.window + ":" + target.path);
                return new { ok = false, error = "blocked", message = target.blocked, target = Brief(target) };
            }
            if (!target.interactable)
                return new { ok = false, error = "not_interactable", target = Brief(target) };

            ClickObject(target.go, right);
            UnityEngine.Debug.Log("[WitchCtl] " + (right ? "right-click " : "click ") + target.window + ":" + target.path + " '" + target.label + "'");
            return new { ok = true, clicked = Brief(target) };
        }

        /// <summary>A plain click (left unless `right`): pointer down, up, click, the way the event system would deliver it.</summary>
        internal static void ClickObject(GameObject go, bool right = false)
        {
            var data = new PointerEventData(EventSystem.current) { button = right ? PointerEventData.InputButton.Right : PointerEventData.InputButton.Left, clickCount = 1 };
            ExecuteEvents.Execute(go, data, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.Execute(go, data, ExecuteEvents.pointerUpHandler);
            ExecuteEvents.Execute(go, data, ExecuteEvents.pointerClickHandler);
        }

        private static object Brief(Clickable c) => new { c.id, c.window, c.path, c.label, c.interactable };

        /// <summary>Names of the open windows, back to front. The game's phase can lag behind: a shop or a skill tree opened over the
        /// map still reports `map_select`, and this list is where it shows.</summary>
        internal static List<string> WindowNames() => ActiveWindows().Select(w => w.name).ToList();

        private static readonly string[] Ambient = { "ChatUI", "CaptionUI", "TopBarUI" };

        /// <summary>True when no window of the game's own is open besides the always-present chat, caption and top bar.</summary>
        internal static bool OnlyAmbientWindows() => WindowNames().All(w => Ambient.Contains(w));

        /// <summary>What `ui` and `click` walk: the open windows, plus the floating menu (<c>FloatingWindow</c>, e.g. the card editor's "add effect" list) when it
        /// is up. It is not a UIManager window, so it is not part of <see cref="ActiveWindows"/> and never counts for phase detection.</summary>
        private static List<(string name, Transform root)> Roots()
        {
            var roots = ActiveWindows().Select(w => (w.name, w.transform)).ToList();
            foreach (var floating in UnityEngine.Object.FindObjectsOfType<FloatingWindow>())
                if (floating != null && floating.gameObject.activeInHierarchy) roots.Add(("FloatingWindow", floating.transform));
            return roots;
        }

        internal static IEnumerable<UIBase> ActiveWindows()
        {
            var manager = Witch.UI.UIManager.Instance;
            if (manager == null) return Enumerable.Empty<UIBase>();
            return manager.GetAllUI()
                .Where(w => w != null && w.gameObject.activeInHierarchy)
                .OrderBy(w => w.transform.GetSiblingIndex())
                .ToList();
        }

        private static List<Clickable> Clickables(Transform root, string windowName)
        {
            var found = new Dictionary<int, Clickable>();
            foreach (var mb in root.GetComponentsInChildren<MonoBehaviour>(false))
            {
                if (mb == null || !(mb is IPointerClickHandler) || !mb.isActiveAndEnabled) continue;
                int id = mb.gameObject.GetInstanceID();
                if (found.ContainsKey(id)) continue;
                // A hand card is clickable (it is dragged and clicked by the player) but a click here would skip every check
                // Battle.cs makes; cards are played with `play`.
                if (mb.GetComponent<CardItem>() != null) continue;
                var path = PathFrom(mb.transform, root);
                var label = LabelOf(mb);
                found[id] = new Clickable
                {
                    id = id,
                    window = windowName,
                    path = path,
                    label = label,
                    interactable = IsInteractable(mb),
                    blocked = Danger.Blocked(windowName, path, label),
                    go = mb.gameObject,
                };
            }
            return found.Values.ToList();
        }

        private static bool IsInteractable(MonoBehaviour mb)
        {
            // A card-choice item looks clickable during its fade-in but ignores clicks until its private flag turns on.
            if (mb.GetComponent<CardChoiceItem>() is CardChoiceItem choice && !Screens.CanClick(choice)) return false;
            if (mb is ButtonManager button) return button.isInteractable;
            if (mb is Selectable selectable) return selectable.IsInteractable();
            return true;
        }

        private static string PathFrom(Transform t, Transform root)
        {
            var parts = new List<string>();
            for (var cur = t; cur != null && cur != root.parent; cur = cur.parent) parts.Add(cur.name);
            parts.Reverse();
            return string.Join("/", parts);
        }

        /// <summary>All visible texts inside the clickable, in order (a card item has its cost, name and description in separate fields).</summary>
        private static string LabelOf(MonoBehaviour mb)
        {
            var parts = mb.GetComponentsInChildren<TMP_Text>(false).Select(t => Rich.Strip(t.text)).Where(t => t.Length > 0).Distinct().ToList();
            return parts.Count > 0 ? string.Join(" ", parts) : Rich.Strip(mb.name);
        }

        private static string Clean(string s) => Rich.Strip(s);
    }
}
