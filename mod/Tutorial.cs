using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEngine;
using Witch.UI.Automation;
using Witch.UI.Window;

namespace WitchCtl
{
    /// <summary>
    /// The first-run tutorial ("Teach" saves) is a stack of <c>TutorialSpotlightUI</c> overlays, each a few slides of a speaker's text
    /// that a click advances. They sit over whatever phase is current (fight, map, top bar), block the pointer for a human, and
    /// are not windows the game's UI manager lists, so neither the game's phase model nor <see cref="UiLite"/> saw them: a player
    /// agent never read nor dismissed a single one. They are reported next to the phase (<c>tutorial</c> in the state) with one
    /// action, <c>tutorial_advance</c>, which clicks the topmost overlay like a human would.
    /// </summary>
    internal static class Tutorial
    {
        internal const string AdvanceId = "tutorial_advance";

        private static readonly FieldInfo SlideIndex = Field("currentSlideIndex");
        private static readonly FieldInfo Slides = Field("slides");
        private static readonly FieldInfo NameText = Field("nameText");
        private static readonly FieldInfo BodyText = Field("bodyText");
        private static readonly FieldInfo DialogueBox = Field("dialogueBox");

        private static FieldInfo Field(string name) => typeof(TutorialSpotlightUI).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);

        internal sealed class Overlay
        {
            public TutorialSpotlightUI Ui;
            public string Speaker;
            public string Text;
            public int Slide;
            public int Slides;
        }

        /// <summary>Overlays that are on screen right now, topmost first.</summary>
        internal static List<Overlay> Visible()
        {
            var shown = new List<Overlay>();
            try
            {
                foreach (var ui in UnityEngine.Object.FindObjectsOfType<TutorialSpotlightUI>())
                {
                    if (ui == null || !ui.gameObject.activeInHierarchy) continue;
                    // An overlay that waits for its trigger (a story line, a fight step) stays active but is made transparent and click-through.
                    var group = ui.GetComponent<CanvasGroup>();
                    if (group != null && (group.alpha < 0.5f || !group.blocksRaycasts)) continue;
                    var box = (DialogueBox?.GetValue(ui) as RectTransform)?.gameObject;
                    var text = (BodyText?.GetValue(ui) as TMP_Text)?.text;
                    if (box == null || !box.activeInHierarchy || string.IsNullOrWhiteSpace(text)) continue;
                    shown.Add(new Overlay
                    {
                        Ui = ui,
                        Speaker = Rich.Strip((NameText?.GetValue(ui) as TMP_Text)?.text),
                        Text = Rich.Strip(text),
                        Slide = SlideIndex == null ? 0 : (int)SlideIndex.GetValue(ui),
                        Slides = (Slides?.GetValue(ui) as System.Collections.ICollection)?.Count ?? 1,
                    });
                }
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning("[WitchCtl] tutorial overlay scan failed: " + e);
            }
            // A click lands on the one drawn last: the highest sibling index (they share the canvas).
            return shown.OrderByDescending(o => o.Ui.transform.GetSiblingIndex()).ToList();
        }

        /// <summary>Adds <c>tutorial</c> and the advance action to a state snapshot, whatever phase it describes.</summary>
        internal static void Attach(Dictionary<string, object> state)
        {
            var shown = Visible();
            if (shown.Count == 0) return;
            state["tutorial"] = shown.Select(o => new { speaker = o.Speaker, text = o.Text, slide = o.Slide + 1, slides = o.Slides }).ToList();
            if (state.TryGetValue("actions", out var actions) && actions is List<RuntimeLegalAction> list)
                list.Insert(0, Action(shown[0]));
        }

        internal static RuntimeLegalAction Action(Overlay top) => new RuntimeLegalAction
        {
            Id = AdvanceId,
            Kind = "tutorial_advance",
            Label = "继续",
            Description = "Click the tutorial overlay (" + (top.Slide + 1) + "/" + top.Slides + "): " + top.Text,
        };

        internal static bool Owns(string actionId) => actionId == AdvanceId;

        internal static RuntimePerformActionResult Perform(string actionId)
        {
            var shown = Visible();
            if (shown.Count == 0)
                return new RuntimePerformActionResult { Success = false, ActionId = actionId, Message = "refused: no tutorial overlay is showing" };

            var top = shown[0];
            string before = top.Text;
            UiLite.ClickObject(top.Ui.gameObject);

            // The overlay ignores a click on the frame it opened; say so rather than claim an advance that did not happen.
            var after = Visible().FirstOrDefault(o => o.Ui == top.Ui);
            bool moved = after == null || after.Slide != top.Slide || after.Text != before;
            return new RuntimePerformActionResult
            {
                Success = moved,
                ActionId = actionId,
                Message = moved
                    ? (after == null ? "Closed the tutorial overlay: " : "Next tutorial slide: ") + Brief(before)
                    : "refused: the tutorial overlay did not react (it ignores a click on the frame it opened); try again",
            };
        }

        private static string Brief(string text) => text.Length <= 60 ? text : text.Substring(0, 60) + "…";
    }
}
