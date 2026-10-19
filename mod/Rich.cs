using System;
using System.Text.RegularExpressions;

namespace WitchCtl
{
    /// <summary>
    /// The game's strings are Unity rich text: costs are drawn as <c>&lt;sprite name="2"&gt;</c>, numbers are wrapped in
    /// <c>&lt;color=white&gt;</c>. An agent wants the plain words, so everything the mod reports goes through here.
    /// </summary>
    internal static class Rich
    {
        private static readonly Regex SpriteTag = new Regex("<sprite name=\"?([^\">]*)\"?>", RegexOptions.Compiled);
        private static readonly Regex AnyTag = new Regex("<[^>]+>", RegexOptions.Compiled);

        internal static string Strip(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = SpriteTag.Replace(s, "$1");   // cost icons carry the digit in their name
            s = AnyTag.Replace(s, "");
            return s.Replace("​", "").Replace("\r", "").Trim();
        }

        /// <summary>The game's own rendering of a card/buff description (placeholders resolved), stripped. Never throws.</summary>
        internal static string Describe(IDataConfig config)
        {
            if (config == null) return "";
            try { return Strip(config.Description()); }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning("[WitchCtl] description failed: " + e.Message);
                return "";
            }
        }

        /// <summary>Localized name from a config table row (falls back to the raw "Name" column).</summary>
        internal static string NameOf(IDataConfig config)
        {
            if (config?.data == null) return null;
            try { return Strip(config.data.Localize("Name")); }
            catch { return config.data.TryGetValue("Name", out var n) ? n : null; }
        }
    }
}
