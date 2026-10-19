using System;
using System.Linq;
using System.Reflection;
using Witch.UI;
using Witch.UI.Window;
using UIManager = Witch.UI.UIManager;

namespace WitchCtl
{
    /// <summary>
    /// `map_continue` is offered by the game as soon as the map screen is up, but <c>MapSelectUI.TryContinue</c> then only shows the
    /// caption "请填满所有节点" and returns while the map nodes are still being filled in (some seconds), yet the game's action reports
    /// success. An agent that acts on the first offer loses the click and, worse, believes it worked. So the offer is withheld until
    /// the click can take effect, and a click before that is refused with the reason.
    /// </summary>
    internal static class MapGate
    {
        private static readonly FieldInfo Closing_ = typeof(MapSelectUI).GetField("isCloseing", BindingFlags.NonPublic | BindingFlags.Instance);

        internal static bool Closing(MapSelectUI map) => Closing_ != null && (bool)Closing_.GetValue(map);

        /// <summary>Why <c>map_continue</c> would do nothing right now, or null when it works (or the map is not what is on screen).</summary>
        internal static string NotReady()
        {
            try
            {
                var ui = UIManager.Instance;
                var map = ui?.GetUI<MapSelectUI>("MapSelectUI");
                if (map == null || !map.gameObject.activeInHierarchy) return null;
                if (!PlayerManager.Instance.isServer) return null; // a client waits for the host; the game's own checks apply
                if (ui.GetUI<CurtainTurnUI>("CurtainTurnUI") != null || Closing(map))
                    return "the map is already closing: the next scene is loading";
                if (map.GetNodes().Any(node => node.data == null))
                {
                    int empty = MapNodes.EmptyCount(map);
                    return empty > 0
                        ? empty + " route node(s) are still empty: fill them with map_place_N first"
                        : "the map nodes are still being filled in by the game; wait a few seconds";
                }
                return null;
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning("[WitchCtl] map readiness check failed: " + e.Message);
                return null; // do not hide the action because the probe broke
            }
        }

        /// <summary>A refusal message for <c>map_continue</c> that cannot take effect yet, else null.</summary>
        internal static string Check(string actionId) => actionId == "map_continue" ? NotReady() : null;
    }
}
