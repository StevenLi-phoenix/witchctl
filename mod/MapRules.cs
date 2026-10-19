using System;

namespace WitchCtl
{
    /// <summary>The map's rules as plain logic (no Unity types, tested in tests/modlogic).</summary>
    internal static class MapRules
    {
        /// <summary>Every sixth map (<c>MapManager.Level % 6 == 0</c>) the player builds the route; the others are filled by the host.</summary>
        internal const int Cycle = 6;

        internal static bool PlayerBuilds(int level) => level >= 0 && level % Cycle == 0;

        /// <summary>The node type from the prefab's object name: `FightPrefab(Clone)` → `Fight`, `EventPrefab` → `Event`. Null for anything else.</summary>
        internal static string KindOf(string objectName)
        {
            if (string.IsNullOrEmpty(objectName)) return null;
            var name = objectName.EndsWith("(Clone)", StringComparison.Ordinal) ? objectName.Substring(0, objectName.Length - "(Clone)".Length) : objectName;
            return name.EndsWith("Prefab", StringComparison.Ordinal) && name.Length > "Prefab".Length ? name.Substring(0, name.Length - "Prefab".Length) : null;
        }
    }
}
