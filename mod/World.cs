using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;

namespace WitchCtl
{
    /// <summary>
    /// Read-only look at world (non-UI) objects. The game's own scene snapshot only covers a few roots, and rooms such as the rest
    /// scene ("Breaks") are plain prefabs instantiated at the scene root, so nothing else showed what was on screen there.
    /// <c>GET /world?root=Breaks&amp;depth=4</c> lists every descendant of the root objects whose name contains `root`: path, whether it is
    /// active, its components, whether the pointer can reach it (event handler + collider) and where it is on screen.
    /// </summary>
    internal static class World
    {
        /// <summary>The subtree of one open UI window (`?window=ShopUI`; `modal` = the yes/no window). UI windows hang off canvases, so
        /// `root` never reaches them; this is what shows which prefab button calls which method (`onClick`).</summary>
        internal static object DumpWindow(string window, int depth)
        {
            var found = new List<object>();
            Transform top = null;
            if (window == "modal") { var go = Witch.UI.UIManager.Instance?.WindowObj; if (go != null && go.activeInHierarchy) top = go.transform; }
            else top = UiLite.ActiveWindows().FirstOrDefault(w => w.name == window)?.transform;
            if (top == null) return new { window, count = 0, error = "no such open window", open = UiLite.WindowNames() };
            Walk(top, top.name, 0, Math.Max(1, depth), found);
            return new { window, count = found.Count, objects = found.Take(800).ToList() };
        }

        internal static object Dump(string root, int depth)
        {
            var found = new List<object>();
            if (string.IsNullOrEmpty(root)) return new { error = "pass ?root=<name part of a root object> or ?window=<open UI window>" };
            foreach (var top in UnityEngine.Object.FindObjectsOfType<Transform>())
            {
                if (top == null || top.parent != null || top.name.IndexOf(root, StringComparison.OrdinalIgnoreCase) < 0) continue;
                Walk(top, top.name, 0, Math.Max(1, depth), found);
            }
            return new { root, count = found.Count, objects = found.Take(600).ToList() };
        }

        private static void Walk(Transform t, string path, int level, int maxDepth, List<object> into)
        {
            into.Add(Describe(t, path));
            if (level >= maxDepth) return;
            foreach (Transform child in t) Walk(child, path + "/" + child.name, level + 1, maxDepth, into);
        }

        private static object Describe(Transform t, string path)
        {
            var components = t.GetComponents<UnityEngine.Component>().Where(c => c != null && !(c is Transform)).Select(c => c.GetType().Name).ToList();
            var handlers = t.GetComponents<MonoBehaviour>()
                .Where(m => m != null && (m is IPointerClickHandler || m is IPointerDownHandler || m is IBeginDragHandler || m is IDragHandler))
                .Select(m => m.GetType().Name).ToList();
            bool collider = t.GetComponent<Collider2D>() != null || t.GetComponent<Collider>() != null;
            object screen = null;
            try
            {
                var cam = Camera.main;
                if (cam != null) { var p = cam.WorldToScreenPoint(t.position); screen = new { x = (int)p.x, y = (int)p.y }; }
            }
            catch (Exception) { }
            return new { path, active = t.gameObject.activeInHierarchy, components, handlers, collider, screen, onClick = PersistentClicks(t) };
        }

        private static readonly BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
        private static readonly FieldInfo CallsField = typeof(UnityEventBase).GetField("m_Calls", Private);

        /// <summary>
        /// What a button runs when clicked, as `Type.Method`: the calls wired in the prefab and the listeners the game adds in code
        /// (`AddListener`, read from the event's runtime call list by reflection; most buttons here have only those). A lambda shows up as
        /// `Closure.&lt;Method&gt;b__N`, which still names the method that registered it. Covers `Button` and the MUIP `ButtonManager`.
        /// </summary>
        private static List<string> PersistentClicks(Transform t)
        {
            var calls = new List<string>();
            try
            {
                var button = t.GetComponent<UnityEngine.UI.Button>();
                if (button != null) AddCalls(button.onClick, "Button", calls);
                var manager = t.GetComponent<Michsky.MUIP.ButtonManager>();
                if (manager != null) AddCalls(manager.onClick, "ButtonManager", calls);
            }
            catch (Exception e) { calls.Add("(unreadable: " + e.GetType().Name + ")"); }
            return calls;
        }

        private static void AddCalls(UnityEventBase ev, string source, List<string> into)
        {
            if (ev == null) return;
            for (int i = 0; i < ev.GetPersistentEventCount(); i++)
            {
                var target = ev.GetPersistentTarget(i);
                into.Add(source + " prefab: " + (target == null ? "null" : target.GetType().Name) + "." + ev.GetPersistentMethodName(i));
            }
            var list = CallsField?.GetValue(ev);
            var runtime = list?.GetType().GetField("m_RuntimeCalls", Private)?.GetValue(list) as System.Collections.IEnumerable;
            if (runtime == null) return;
            foreach (var call in runtime)
            {
                var del = call?.GetType().GetField("Delegate", Private)?.GetValue(call) as MulticastDelegate;
                if (del == null) { into.Add(source + " runtime: (call object " + call?.GetType().Name + ")"); continue; }
                foreach (var d in del.GetInvocationList())
                    into.Add(source + " runtime: " + (d.Method.DeclaringType?.Name ?? "?") + "." + d.Method.Name
                             + (d.Target != null && d.Target.GetType() != d.Method.DeclaringType ? " on " + d.Target.GetType().Name : ""));
            }
        }
    }
}
