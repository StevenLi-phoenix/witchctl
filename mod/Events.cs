using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Michsky.MUIP;
using TMPro;
using UnityEngine;
using Witch.UI.Automation;
using Witch.UI.Window;
using UIManager = Witch.UI.UIManager;

namespace WitchCtl
{
    /// <summary>
    /// Story events (<c>EventUI</c>): a title, a description that fades in paragraph by paragraph, and up to four options
    /// (<c>Windows/Map0/Content/Selector/optionN</c>, each a <c>ButtonManager</c> whose click runs the option's script). After a choice
    /// the game swaps the options for one "leave the event" button (a clone of the chosen one) that runs the event's entry script
    /// (leave for the map, or start a fight). The game's phase model reports `unknown` and the UI dump labels the options with
    /// whatever text they hold, so an agent could not tell it was in an event (the bot sat in front of "开战" for good).
    /// Phase <c>event</c>, action <c>event_option_N</c> (N counts the visible option buttons, disabled ones included).
    /// </summary>
    internal static class Events
    {
        private const string Prefix = "event_option_";
        private const string SelectorPath = "Windows/Map0/Content/Selector";

        private static readonly BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
        private static readonly FieldInfo Paragraphs = typeof(EventUI).GetField("descriptionParagraphs", Private);
        private static readonly FieldInfo Closed = typeof(EventUI).GetField("hasClosed", Private);
        private static readonly FieldInfo EventId = typeof(EventUI).GetField("thisid", Private);

        internal sealed class Option
        {
            public int Index;
            public ButtonManager Button;
            public string Label;
            /// <summary>What the option does, the `&lt;subtip&gt;` part of its description that the game only shows on hover (set into the
            /// option's KeywordDisplay). Empty when the event gives none. Some choices are irreversible story branches.</summary>
            public string Effect;
            public bool Interactable;
        }

        internal static bool Owns(string actionId) => actionId != null && actionId.StartsWith(Prefix, StringComparison.Ordinal);

        private static EventUI Ui()
        {
            var ui = UIManager.Instance?.GetUI<EventUI>("EventUI");
            return ui != null && ui.gameObject.activeInHierarchy ? ui : null;
        }

        private static string Text(Transform root, string path) => Rich.Strip(root.Find(path)?.GetComponent<TMP_Text>()?.text);

        internal static List<Option> Options(EventUI ui)
        {
            var found = new List<Option>();
            var selector = ui.transform.Find(SelectorPath);
            if (selector == null) return found;
            foreach (Transform child in selector)
            {
                var button = child.GetComponent<ButtonManager>();
                if (button == null || !child.gameObject.activeInHierarchy) continue;
                var label = string.Join(" — ", child.GetComponentsInChildren<TMP_Text>(false)
                    .Select(t => Rich.Strip(t.text)).Where(t => t.Length > 0).Distinct());
                var effect = Rich.Strip(child.GetComponent<Witch.UI.KeywordDisplay>()?.text);
                found.Add(new Option { Index = found.Count, Button = button, Label = label, Effect = effect ?? "", Interactable = button.isInteractable });
            }
            return found;
        }

        /// <summary>The event screen, or null. A fight started by an option keeps the window up behind it: the fight wins then.</summary>
        internal static Screens.Screen Detect()
        {
            var ui = Ui();
            if (ui == null || Battle.IsActive()) return null;
            try
            {
                var options = Options(ui);
                var closed = Closed != null && (bool)Closed.GetValue(ui);
                var paragraphs = Paragraphs?.GetValue(ui) as string[];
                var screen = new Screens.Screen { Phase = "event" };
                screen.Detail["event"] = new
                {
                    id = EventId?.GetValue(ui) as string,
                    title = Text(ui.transform, "Windows/Map0/Content/Title"),
                    text = paragraphs == null ? null : Rich.Strip(string.Join("\n", paragraphs)),
                    resolved = closed,   // a choice was made: what is left is the button that leaves the event
                    options = options.Select(o => new { index = o.Index, label = o.Label, effect = o.Effect, interactable = o.Interactable }).ToList(),
                };
                if (options.Count == 0) screen.Detail["note"] = "the options are still fading in; wait a moment";
                foreach (var o in options.Where(o => o.Interactable))
                    screen.Actions.Add(new RuntimeLegalAction
                    {
                        Id = Prefix + o.Index,
                        Kind = "event_option",
                        Label = o.Label,
                        Description = closed ? "Leave the event."
                            : "Choose this option; its effect runs at once." + (o.Effect.Length > 0 ? " Effect: " + o.Effect : ""),
                    });
                return screen;
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning("[WitchCtl] event scan failed: " + e);
                return null;
            }
        }

        internal static RuntimePerformActionResult Perform(string actionId)
        {
            string refusal = null, message = null;
            var ui = Ui();
            if (ui == null || Battle.IsActive()) refusal = "no event is open";
            else if (!int.TryParse(actionId.Substring(Prefix.Length), out var index)) refusal = "bad option index";
            else
            {
                var option = Options(ui).FirstOrDefault(o => o.Index == index);
                if (option == null) refusal = "no option " + index + " on screen";
                else if (!option.Interactable) refusal = "option " + index + " cannot be chosen (disabled or already chosen)";
                else
                {
                    UiLite.ClickObject(option.Button.gameObject);
                    message = "Chose: " + option.Label + (option.Effect.Length > 0 ? " [" + option.Effect + "]" : "");
                }
            }
            if (refusal != null) return new RuntimePerformActionResult { Success = false, ActionId = actionId, Message = "refused: " + refusal };
            UnityEngine.Debug.Log("[WitchCtl] " + actionId + ": " + message);
            return new RuntimePerformActionResult { Success = true, ActionId = actionId, Message = message };
        }
    }
}
