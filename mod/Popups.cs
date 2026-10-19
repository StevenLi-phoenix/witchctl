using System;
using System.Collections.Generic;
using System.Linq;
using Michsky.MUIP;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Witch.UI;
using Witch.UI.Automation;

namespace WitchCtl
{
    /// <summary>
    /// Small windows that are only a few buttons, listed in a table: window name → phase, button path → action id. They sit over the
    /// hub or the menu and the game's phase model knows none of them (`unknown`, or worse a stale `hub` with a sign that no longer
    /// reacts: after the tutorial run Start in the hub opens the announcement list and the mode choice, and an agent clicked the
    /// sign for good). The window that is on top (last in the window list) names the phase.
    /// </summary>
    internal static class Popups
    {
        private sealed class Spec
        {
            public string Window, Phase;
            public (string Id, string Path)[] Buttons;
            /// <summary>Report the window's visible texts (a short announcement such as "new item: ...").</summary>
            public bool ShowTexts;
            /// <summary>Per action id: what the agent should know before pressing it (goes into the action's description).</summary>
            public Dictionary<string, string> Notes = new Dictionary<string, string>();
        }

        private static readonly Spec[] Specs =
        {
            new Spec
            {
                Window = "ModeChoiceUI", Phase = "mode_choice",
                Buttons = new[]
                {
                    ("mode_choice_story", "ModeList/StoryMode"),
                    ("mode_choice_classic", "ModeList/NormalMode"),
                    ("mode_choice_sublimation", "ModeList/SublimationMode"),
                    ("mode_choice_slot", "ModeList/SlotMode"),
                    ("mode_choice_back", "ExitButton"),
                },
                Notes =
                {
                    ["mode_choice_classic"] = "The classic roguelike run: the one witchctl models (map, fights, rewards, events, rest, run end). With a saved run it asks first (phase modal).",
                    ["mode_choice_story"] = "Story mode: ModeChoiceUI has no handler for it (decompile), so the click is probably a no-op. Not modelled.",
                    ["mode_choice_sublimation"] = "The Path of Sublimation (its own save slot). Not modelled: later screens may show up as `unknown`.",
                    ["mode_choice_slot"] = "The slot-machine mode (\"Random Mode\"): needs player level 25, below it only a caption shows and nothing opens. Not modelled: the machine screen is `unknown`.",
                    ["mode_choice_back"] = "Close the window without choosing.",
                },
            },
            new Spec
            {
                Window = "ItemShowUI", Phase = "item_reveal", ShowTexts = true,
                Buttons = new[] { ("item_reveal_confirm", "Content/Confirm") },
            },
            new Spec
            {
                Window = "AnnouncementUI", Phase = "announcement",
                Buttons = new[] { ("announcement_close", "Windows/ExitButton") },
            },
        };

        internal const string ModalConfirm = "modal_confirm", ModalCancel = "modal_cancel";

        private static bool IsModalId(string actionId) =>
            actionId != null && (actionId.StartsWith(ModalConfirm, StringComparison.Ordinal) || actionId.StartsWith(ModalCancel, StringComparison.Ordinal));

        internal static bool Owns(string actionId) =>
            IsModalId(actionId) || (actionId != null && Specs.Any(s => s.Buttons.Any(b => b.Id == actionId)));

        /// <summary>The game's generic yes/no window (`UIManager.ShowModalWindow`, a `ModalWindow(Clone)` under `Upper Canvas`), or null.
        /// It is not in `GetAllUI()`, and while it is open every other root canvas has its raycasts switched off: a click on the screen
        /// behind it does nothing but ask again. Seen: classic mode with a saved run asks "continue game / new game".</summary>
        private static ModalWindowManager OpenModal()
        {
            var go = Witch.UI.UIManager.Instance?.WindowObj;
            return go != null && go.activeInHierarchy ? go.GetComponent<ModalWindowManager>() : null;
        }

        /// <summary>Phase `modal` while such a window is open; it outranks every other screen. Buttons are offered once the open animation
        /// is done (`isOn`) and only the ones the game kept (a button the caller did not ask for is set to null).</summary>
        internal static Screens.Screen DetectModal()
        {
            var modal = OpenModal();
            if (modal == null) return null;
            var title = Rich.Strip(modal.titleText);
            var text = Rich.Strip(modal.descriptionText);
            var screen = new Screens.Screen { Phase = "modal" };
            screen.Detail["modalTitle"] = title;
            screen.Detail["modalText"] = text;
            var effects = new Dictionary<string, string>();
            if (modal.isOn)
                foreach (var (confirm, button) in new[] { (true, modal.confirmButton), (false, modal.cancelButton) })
                {
                    if (button == null || !Interactable(button.transform)) continue;
                    var effect = Danger.ModalEffect(text, confirm);
                    var id = Danger.ModalActionId(confirm, effect);
                    if (effect != null) effects[confirm ? "confirm" : "cancel"] = effect;
                    screen.Actions.Add(new RuntimeLegalAction
                    {
                        Id = id, Kind = id, Label = Label(button.transform),
                        Description = "Answer the window \"" + title + "\"." + (effect == null ? "" : " Effect: " + effect + (Danger.Irreversible(effect) ? " (irreversible)." : "."))
                    });
                }
            if (effects.Count > 0) screen.Detail["modalEffects"] = effects;
            return screen;
        }

        private static RuntimePerformActionResult PerformModal(string actionId)
        {
            var modal = OpenModal();
            var confirm = actionId.StartsWith(ModalConfirm, StringComparison.Ordinal);
            var button = modal == null ? null : confirm ? modal.confirmButton : modal.cancelButton;
            if (button == null || !modal.isOn || !Interactable(button.transform))
                return new RuntimePerformActionResult { Success = false, ActionId = actionId, Message = "refused: that button is not available (yet)" };
            // The id has to name the effect when it is irreversible ("modal_cancel_new_game_deletes_save"); a plain or stale id is refused.
            var expected = Danger.ModalActionId(confirm, Danger.ModalEffect(Rich.Strip(modal.descriptionText), confirm));
            if (actionId != expected)
                return new RuntimePerformActionResult { Success = false, ActionId = actionId, Message = "refused: this button is now `" + expected + "`" };
            var message = "Clicked: " + Label(button.transform);
            UiLite.ClickObject(button.gameObject);
            UnityEngine.Debug.Log("[WitchCtl] " + actionId + ": " + message);
            return new RuntimePerformActionResult { Success = true, ActionId = actionId, Message = message };
        }

        private static (Spec Spec, UIBase Window)? TopMost()
        {
            foreach (var window in UiLite.ActiveWindows().Reverse())
            {
                var spec = Specs.FirstOrDefault(s => s.Window == window.name);
                if (spec != null) return (spec, window);
            }
            return null;
        }

        private static Transform Target(UIBase window, string path) => window.transform.Find(path);

        private static bool Interactable(Transform t)
        {
            if (t == null || !t.gameObject.activeInHierarchy) return false;
            var button = t.GetComponent<ButtonManager>();
            if (button != null) return button.isInteractable;
            var selectable = t.GetComponent<Selectable>();
            return selectable == null || selectable.IsInteractable();
        }

        private static string Label(Transform t)
        {
            var parts = t.GetComponentsInChildren<TMP_Text>(false).Select(x => Rich.Strip(x.text)).Where(x => x.Length > 0).Distinct().ToList();
            var text = parts.Count > 0 ? string.Join(" ", parts) : Rich.Strip(t.name);
            return System.Text.RegularExpressions.Regex.Replace(text, @"\s*\n\s*", " / ");   // one line per action in listings
        }

        internal static Screens.Screen Detect()
        {
            var top = TopMost();
            if (top == null) return null;
            var (spec, window) = top.Value;
            var screen = new Screens.Screen { Phase = spec.Phase };
            foreach (var (id, path) in spec.Buttons)
            {
                var target = Target(window, path);
                if (!Interactable(target)) continue;
                spec.Notes.TryGetValue(id, out var note);
                screen.Actions.Add(new RuntimeLegalAction { Id = id, Kind = id, Label = Label(target), Description = note ?? "Click " + path + " in " + spec.Window + "." });
            }
            if (spec.ShowTexts)
                screen.Detail["texts"] = window.GetComponentsInChildren<TMP_Text>(false).Select(x => Rich.Strip(x.text)).Where(x => x.Length > 0).Distinct().ToList();
            screen.NeedsUi = screen.Actions.Count == 0;   // nothing offered yet (fading in): show the raw UI instead of nothing
            return screen;
        }

        internal static RuntimePerformActionResult Perform(string actionId)
        {
            if (IsModalId(actionId)) return PerformModal(actionId);
            string refusal = null, message = null;
            var top = TopMost();
            var spec = Specs.First(s => s.Buttons.Any(b => b.Id == actionId));
            var path = spec.Buttons.First(b => b.Id == actionId).Path;
            if (top == null || top.Value.Spec != spec) refusal = spec.Window + " is not the window on top";
            else
            {
                var target = Target(top.Value.Window, path);
                if (!Interactable(target)) refusal = "that button is not available (yet)";
                else
                {
                    UiLite.ClickObject(target.gameObject);
                    message = "Clicked: " + Label(target);
                }
            }
            if (refusal != null) return new RuntimePerformActionResult { Success = false, ActionId = actionId, Message = "refused: " + refusal };
            UnityEngine.Debug.Log("[WitchCtl] " + actionId + ": " + message);
            return new RuntimePerformActionResult { Success = true, ActionId = actionId, Message = message };
        }
    }
}
