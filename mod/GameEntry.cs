using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.EventSystems;
using Witch.UI.Automation;
using Witch.UI.Window;
using UIManager = Witch.UI.UIManager;

namespace WitchCtl
{
    /// <summary>
    /// The screen before a run (<c>GameEntryUI</c>, phase <c>game_entry</c>). The game's own <c>game_entry_start</c> calls <c>StartGame()</c> and so
    /// skips the whole "get ready" window: every run used the first career, partner row 1, Strength + Wisdom, and whatever difficulty tags and
    /// card packs the profile still held. This module makes those choices readable (state section <c>entry</c>) and settable:
    /// <c>entry_career_N</c>, <c>entry_partner_N</c> (N = index in `entry.careers` / `entry.partners`), <c>entry_main_VAR</c>, <c>entry_second_VAR</c>,
    /// <c>entry_pack_on_ID</c> / <c>entry_pack_off_ID</c>, and <c>entry_prepare_open</c> / <c>entry_prepare_close</c> for the window they live in
    /// (a choice opens it by itself). Ids and limits: <see cref="EntryRules"/>.
    /// A choice is made with the call the game's own click makes (<c>ShowCareer.OnPointerClick</c>, <c>FightVarItem.OnPointerClick</c>,
    /// <c>CardPackUI.SetPackEnabled</c>) and read back from the statics it sets; card packs and difficulty tags are saved in the profile by the game
    /// itself (<c>CardPackUI.OnDestroy</c>, <c>SelectHardUI.OnDisable</c>), so they outlive the run: the state shows both.
    /// </summary>
    internal static class GameEntry
    {
        private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly FieldInfo MainVarField = typeof(GameEntryUI).GetField("MainVar", Private);
        private static readonly FieldInfo SecondVarField = typeof(GameEntryUI).GetField("SecondVar", Private);
        private static readonly Regex Name = new Regex("<name>(.*?)</name>", RegexOptions.Singleline);
        private static readonly Regex Des = new Regex("<des>(.*?)</des>", RegexOptions.Singleline);
        private static readonly Regex Cd = new Regex("<cd>(.*?)</cd>", RegexOptions.Singleline);

        internal static bool Owns(string actionId) => EntryRules.Owns(actionId);

        internal static GameEntryUI Window()
        {
            var ui = UIManager.Instance?.GetUI<GameEntryUI>("GameEntryUI");
            return ui != null && ui.gameObject.activeInHierarchy ? ui : null;
        }

        private static bool Open(GameEntryUI ui) => ui.transform.Find("Window Manager")?.gameObject.activeInHierarchy == true;

        private static bool Ready(GameEntryUI ui) => ui != null && RoleTable.Instance != null && !RoleTable.Instance.IsStarted;

        private static string IdOf(DataConfig c) => c?.data != null && c.data.TryGetValue("Id", out var id) ? id : null;

        private static string VarName(GameEntryUI ui, FieldInfo field) => (field?.GetValue(ui) as Transform)?.name;

        private static string Get(IDictionary<string, string> d, string key) => d != null && d.TryGetValue(key, out var v) ? v : "";

        /// <summary>The skills a career / partner shows in its panel: "Action1..3" and "Passive1..3" columns, each `&lt;name&gt;…&lt;des&gt;…&lt;cd&gt;`.</summary>
        private static List<string> Skills(DataConfig config)
        {
            var found = new List<string>();
            foreach (var key in new[] { "Action1", "Action2", "Action3", "Passive1", "Passive2", "Passive3" })
            {
                string raw;
                try { raw = config.data.Localize(key); } catch { continue; }
                if (string.IsNullOrEmpty(raw)) continue;
                var name = Name.Match(raw); var des = Des.Match(raw); var cd = Cd.Match(raw);
                var line = (name.Success ? name.Groups[1].Value : key) + (des.Success ? " — " + des.Groups[1].Value : "") + (cd.Success && cd.Groups[1].Value.Length > 0 ? " [" + cd.Groups[1].Value + "]" : "");
                found.Add((key.StartsWith("Action") ? "active: " : "passive: ") + Rich.Strip(line));
            }
            return found;
        }

        private static object Options(List<ShowCareer> items, DataConfig current) =>
            items.Where(i => i?.dataConfig?.data != null).Select((i, index) => new
            {
                index, id = IdOf(i.dataConfig), name = Rich.NameOf(i.dataConfig), selected = IdOf(i.dataConfig) == IdOf(current),
                sanMax = Get(i.dataConfig.data, "SanMax"), skills = Skills(i.dataConfig),
            }).ToList();

        private static DataConfig Default(DataType type, int row)
        {
            try
            {
                var lines = Singleton<GameConfigManager>.Instance.GetTable(type).Getlines();
                return new DataConfig(lines[row]["Id"], type);
            }
            catch (Exception) { return null; }
        }

        // ------------------------------------------------------------------ reading

        /// <summary>`entry` in the state while the screen is up.</summary>
        internal static void Attach(Dictionary<string, object> state)
        {
            var ui = Window();
            if (ui == null) return;
            try
            {
                var career = GameEntryUI.career ?? RoleTable.Instance?.Career;
                var partner = GameEntryUI.partner;
                var packs = ui.cardPackUI;
                var enabled = packs?.UseCardPack ?? new HashSet<string>();
                state["entry"] = new
                {
                    windowOpen = Open(ui),
                    // Nothing chosen yet = the game picks at the start: career row 0, partner row 1 (`GameEntryUI.CheckCareer`).
                    career = career != null ? new { id = IdOf(career), name = Rich.NameOf(career) } : null,
                    careerAtStart = career == null ? Rich.NameOf(Default(DataType.Career, 0)) : null,
                    careers = Options(ui.showCareers, career),
                    partner = partner != null ? new { id = IdOf(partner), name = Rich.NameOf(partner) } : null,
                    partnerAtStart = partner == null ? Rich.NameOf(Default(DataType.Partner, 1)) : null,
                    partners = Options(ui.showPartners, partner),
                    main = VarName(ui, MainVarField), second = VarName(ui, SecondVarField), attributes = EntryRules.Vars,
                    packs = packs == null ? null : new
                    {
                        enabled = enabled.Count, minimum = EntryRules.MinPacks,
                        cards = packs.CardCount, relics = packs.RelicCount, blessings = packs.BlessCount, enchantments = packs.EnchCount,
                        list = packs.TotalCardPack.Select(p => new { id = Get(p, "Id"), name = Rich.NameOf(new DataConfig(Get(p, "Id"), DataType.CardPack)), type = Get(p, "Type"), enabled = enabled.Contains(Get(p, "Id")) }).ToList(),
                    },
                    hardTags = HardTags(),
                };
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning("[WitchCtl] entry state failed: " + e);
                state["entryError"] = e.GetType().Name + ": " + e.Message;
            }
        }

        /// <summary>Difficulty tags the profile holds (they make every run harder and persist): only those above level 0.</summary>
        private static object HardTags()
        {
            var active = (SelectHardUI.UseSc ?? new List<HardTagEntry>()).Where(t => t != null && t.DynamicValue > 0)
                .Select(t => new { id = Get(t.Data, "Id"), name = Rich.Strip(t.Data.Localize("Name")), level = t.DynamicValue, max = Get(t.Data, "MaxCount") }).ToList();
            return new { active, extraReward = SelectHardUI.AddReward };
        }

        // ------------------------------------------------------------------ the actions offered

        internal static List<RuntimeLegalAction> Actions()
        {
            var actions = new List<RuntimeLegalAction>();
            var ui = Window();
            if (!Ready(ui)) return actions;
            try
            {
                actions.Add(Open(ui)
                    ? new RuntimeLegalAction { Id = EntryRules.PrepareClose, Kind = EntryRules.PrepareClose, Label = "收起准备", Description = "Close the get-ready window (the choices stay)." }
                    : new RuntimeLegalAction { Id = EntryRules.PrepareOpen, Kind = EntryRules.PrepareOpen, Label = "出发前准备", Description = "Open the get-ready window (career, partner, attributes, card packs). A choice opens it by itself." });
                var career = GameEntryUI.career ?? RoleTable.Instance.Career;
                AddChoices(actions, ui.showCareers, career, EntryRules.Career, "Choose this career.");
                AddChoices(actions, ui.showPartners, GameEntryUI.partner, EntryRules.Partner, "Choose this partner (its blessing is the starting blessing).");
                foreach (var slot in new[] { "main", "second" })
                {
                    var current = VarName(ui, slot == "main" ? MainVarField : SecondVarField);
                    foreach (var v in EntryRules.Vars.Where(v => v != current))
                        actions.Add(new RuntimeLegalAction
                        {
                            Id = EntryRules.Var(slot, v), Kind = "entry_" + slot, Label = v,
                            Description = (slot == "main" ? "Core" : "Secondary") + " attribute: +2 and two starting cards. The two must differ (the other one moves if it is the same).",
                        });
                }
                var packs = ui.cardPackUI;
                if (packs != null)
                    foreach (var p in packs.TotalCardPack)
                    {
                        var id = Get(p, "Id");
                        var on = packs.UseCardPack.Contains(id);
                        if (EntryRules.PackRefusal(!on, on, packs.UseCardPack.Count) != null) continue;
                        actions.Add(new RuntimeLegalAction
                        {
                            Id = EntryRules.Pack(!on, id), Kind = on ? "entry_pack_off" : "entry_pack_on", Label = Rich.NameOf(new DataConfig(id, DataType.CardPack)) ?? id,
                            Description = (on ? "Disable" : "Enable") + " this card pack for the card, relic and blessing pools (saved in the profile).",
                        });
                    }
            }
            catch (Exception e) { UnityEngine.Debug.LogWarning("[WitchCtl] entry actions failed: " + e); }
            return actions;
        }

        private static void AddChoices(List<RuntimeLegalAction> actions, List<ShowCareer> items, DataConfig current, Func<int, string> id, string description)
        {
            var index = 0;
            foreach (var item in items.Where(i => i?.dataConfig?.data != null))
            {
                if (IdOf(item.dataConfig) != IdOf(current))
                    actions.Add(new RuntimeLegalAction
                    {
                        Id = id(index), Kind = id(0).Substring(0, id(0).LastIndexOf('_')), Label = Rich.NameOf(item.dataConfig) ?? IdOf(item.dataConfig),
                        Description = description + " " + string.Join("; ", Skills(item.dataConfig)),
                    });
                index++;
            }
        }

        // ------------------------------------------------------------------ doing things

        internal static RuntimePerformActionResult Perform(string actionId)
        {
            string refusal = null, message = null;
            var ui = Window();
            if (!Ready(ui)) refusal = "the before-the-run screen is not open, or the run has started";
            else if (!EntryRules.TryParse(actionId, out var kind, out var arg)) refusal = "unknown entry action";
            else
            {
                try
                {
                    switch (kind)
                    {
                        case "prepare_open":
                            if (Open(ui)) refusal = "the window is already open";
                            else { ui.OpenChoice(); message = Open(ui) ? "Opened the get-ready window." : null; refusal = message == null ? "the game did not open the window" : null; }
                            break;
                        case "prepare_close":
                            if (!Open(ui)) refusal = "the window is already closed";
                            else { ui.CloseWindow(); message = "Closed the get-ready window."; }
                            break;
                        case "career": case "partner": message = Choose(ui, kind, int.Parse(arg), ref refusal); break;
                        case "main": case "second": message = ChooseVar(ui, kind, arg, ref refusal); break;
                        default: message = SetPack(ui, kind == "pack_on", arg, ref refusal); break;
                    }
                }
                catch (Exception e)
                {
                    UnityEngine.Debug.LogWarning("[WitchCtl] " + actionId + " failed: " + e);
                    refusal = e.GetType().Name + ": " + e.Message;
                }
            }
            if (refusal != null) return new RuntimePerformActionResult { Success = false, ActionId = actionId, Message = "refused: " + refusal };
            UnityEngine.Debug.Log("[WitchCtl] " + actionId + ": " + message);
            return new RuntimePerformActionResult { Success = true, ActionId = actionId, Message = message };
        }

        private static void EnsureOpen(GameEntryUI ui) { if (!Open(ui)) ui.OpenChoice(); }   // the choices animate inside this window

        private static string Choose(GameEntryUI ui, string kind, int index, ref string refusal)
        {
            var items = (kind == "career" ? ui.showCareers : ui.showPartners).Where(i => i?.dataConfig?.data != null).ToList();
            var item = index < items.Count ? items[index] : null;
            if (item == null) { refusal = "no such " + kind + " (" + items.Count + " available)"; return null; }
            var current = kind == "career" ? GameEntryUI.career ?? RoleTable.Instance.Career : GameEntryUI.partner;
            if (IdOf(current) == IdOf(item.dataConfig)) { refusal = "that " + kind + " is already chosen"; return null; }
            EnsureOpen(ui);
            item.OnPointerClick(new PointerEventData(EventSystem.current));   // what a click does: ShowDetail(...) sets the static, and the career also RoleTable.Career
            var now = kind == "career" ? GameEntryUI.career : GameEntryUI.partner;
            if (IdOf(now) != IdOf(item.dataConfig)) { refusal = "the game did not take the " + kind + " (still " + (IdOf(now) ?? "none") + ")"; return null; }
            return "Chose the " + kind + " " + Rich.NameOf(item.dataConfig) + ".";
        }

        private static string ChooseVar(GameEntryUI ui, string slot, string name, ref string refusal)
        {
            var field = slot == "main" ? MainVarField : SecondVarField;
            if (field == null) { refusal = "game internals changed (GameEntryUI." + (slot == "main" ? "MainVar" : "SecondVar") + " not found)"; return null; }
            if (VarName(ui, field) == name) { refusal = name + " is already the " + slot + " attribute"; return null; }
            var button = (slot == "main" ? ui.MainParent : ui.SecondParent)?.Find(name);
            if (button == null) { refusal = "no attribute " + name + " under the " + slot + " list"; return null; }
            EnsureOpen(ui);
            var click = new PointerEventData(EventSystem.current);
            button.GetComponent<SwitchButton>()?.OnPointerClick(click);                  // the visual toggle, as the game itself does when it moves the other one
            button.GetComponent<Witch.FightVarItem>()?.OnPointerClick(click);            // → SelectVar(name, slot)
            if (VarName(ui, field) != name) { refusal = "the game did not take it (" + slot + " is " + (VarName(ui, field) ?? "none") + ")"; return null; }
            return "Chose " + name + " as the " + slot + " attribute (main " + VarName(ui, MainVarField) + ", second " + VarName(ui, SecondVarField) + ").";
        }

        private static string SetPack(GameEntryUI ui, bool enable, string id, ref string refusal)
        {
            var packs = ui.cardPackUI;
            if (packs == null || !packs.TotalCardPack.Any(p => Get(p, "Id") == id)) { refusal = "no such card pack"; return null; }
            var on = packs.UseCardPack.Contains(id);
            if ((refusal = EntryRules.PackRefusal(enable, on, packs.UseCardPack.Count)) != null) return null;
            int before = packs.UseCardPack.Count;
            packs.SetPackEnabled(id, enable);
            if (packs.UseCardPack.Contains(id) != enable) { refusal = "the game did not " + (enable ? "enable" : "disable") + " the pack"; return null; }
            return (enable ? "Enabled " : "Disabled ") + (Rich.NameOf(new DataConfig(id, DataType.CardPack)) ?? id) + " (packs " + before + "→" + packs.UseCardPack.Count + "; saved when the screen closes).";
        }
    }
}
