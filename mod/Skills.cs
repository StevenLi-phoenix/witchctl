using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Witch.UI;
using Witch.UI.Automation;
using Witch.UI.Window;
using UIManager = Witch.UI.UIManager;

namespace WitchCtl
{
    /// <summary>
    /// Career skills (<c>SkillItem</c>, one or two per career next to the player: <c>Left/Skill1</c>, <c>Left/Skill2/Skill1|Skill2</c>). They are
    /// not buttons (a pointer-down <c>EventTrigger</c> that starts an aim line for most), so the mod calls the game's own <c>TryUse()</c> /
    /// <c>TrueUse()</c> and, for an aimed skill, sets the private <c>hitEnemy</c> the aim line would have set. Listed in
    /// <c>battle.skills</c>; actions <c>skill_use_N</c> (nothing to aim), <c>skill_use_N_enemy_M</c> / <c>skill_use_N_self</c> (aimed: one action per
    /// living enemy, and the player). N is the position in `battle.skills`. Skills run their script synchronously, like a card.
    /// </summary>
    internal static class Skills
    {
        private static readonly BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
        private static readonly FieldInfo ListField = typeof(FightUI).GetField("skillItems", Private);
        private static readonly FieldInfo HitEnemy = typeof(SkillItem).GetField("hitEnemy", Private);

        internal sealed class View
        {
            public int Index;
            public SkillItem Item;
            public string Id, Name, Text;
            public int Cooldown;
            public bool Aimed, Usable;
        }

        internal static bool Owns(string actionId) => actionId != null && actionId.StartsWith(SkillRules.Prefix, StringComparison.Ordinal);

        internal static List<View> List()
        {
            var views = new List<View>();
            var fight = UIManager.Instance?.GetUI<FightUI>("FightUI");
            var items = fight == null ? null : ListField?.GetValue(fight) as List<SkillItem>;
            if (items == null) return views;
            foreach (var item in items.Where(i => i != null && i.gameObject.activeInHierarchy && i.dataConfig?.data != null))
            {
                var id = item.dataConfig.data.TryGetValue("Id", out var i) ? i : "?";
                int cd = RoleTable.Instance != null && RoleTable.Instance.SkillTime.TryGetValue(id, out var t) ? t : 0;
                string baseScript = null;
                item.Vars?.TryGetValue("BaseScript", out baseScript);
                bool usable;
                try { usable = item.TryUse(); } catch (Exception) { usable = false; }
                views.Add(new View
                {
                    Index = views.Count, Item = item, Id = id,
                    Name = Rich.NameOf(item.dataConfig) ?? id, Text = Rich.Describe(item.dataConfig),
                    Cooldown = cd, Aimed = baseScript != "CommonCardItem", Usable = usable,
                });
            }
            return views;
        }

        /// <summary>The state's `battle.skills`.</summary>
        internal static object Read() =>
            List().Select(v => new { index = v.Index, id = v.Id, name = v.Name, text = v.Text, cooldown = v.Cooldown, aimed = v.Aimed, usable = v.Usable }).ToList();

        internal static List<RuntimeLegalAction> Actions()
        {
            var actions = new List<RuntimeLegalAction>();
            if (!Battle.PlayerWindow(out _) || !Battle.AnyTarget()) return actions;   // no living enemy = the fight is being won (seen live: a use then did nothing)
            var enemies = Battle.Enemies().Select((e, index) => (e, index)).Where(x => Battle.IsLiving(x.e)).ToList();
            foreach (var v in List().Where(v => v.Usable))
            {
                if (!v.Aimed)
                    actions.Add(Make(v, SkillRules.Id(v.Index, null, -1), "Use it now. " + v.Text));
                else
                {
                    foreach (var (e, index) in enemies)
                        actions.Add(Make(v, SkillRules.Id(v.Index, "enemy", index), "Aim it at enemy " + index + " (" + (Battle.NameOf(e)) + "). " + v.Text));
                    actions.Add(Make(v, SkillRules.Id(v.Index, "self", -1), "Aim it at yourself. " + v.Text));
                }
            }
            return actions;
        }

        private static RuntimeLegalAction Make(View v, string id, string description) => new RuntimeLegalAction
        {
            Id = id, Kind = "skill_use", Label = v.Name, Description = description,
            Parameters = new Dictionary<string, object> { ["skillId"] = v.Id, ["aimed"] = v.Aimed },
        };

        internal static RuntimePerformActionResult Perform(string actionId)
        {
            string refusal = null, message = null;
            if (!SkillRules.TryParse(actionId, out var slot, out var kind, out var enemyIndex)) refusal = "bad skill action id";
            else if (!Battle.PlayerWindow(out var why)) refusal = why;
            else if (!Battle.AnyTarget()) refusal = "no living enemy: the fight is being won";
            else
            {
                var view = List().FirstOrDefault(v => v.Index == slot);
                if (view == null) refusal = "no skill " + slot;
                else if (!view.Usable) refusal = view.Name + " cannot be used now" + (view.Cooldown > 0 ? " (cooldown " + view.Cooldown + ")" : "");
                else if (view.Aimed && kind == "") refusal = view.Name + " needs a target: skill_use_" + slot + "_enemy_N or skill_use_" + slot + "_self";
                else if (!view.Aimed && kind != "") refusal = view.Name + " takes no target: use skill_use_" + slot;
                else
                {
                    StatusManager target = null;
                    if (kind == "self") target = FightPlayer.Instance?.Status as StatusManager;
                    else if (kind == "enemy")
                    {
                        var enemy = Battle.Enemies().ElementAtOrDefault(enemyIndex);
                        target = enemy?.Status as StatusManager;
                        if (enemy == null || !Battle.IsLiving(enemy) || target == null) refusal = "enemy " + enemyIndex + " is not a valid target";
                    }
                    if (refusal == null && view.Aimed && target == null) refusal = "no such target";
                    if (refusal == null)
                    {
                        if (HitEnemy == null) refusal = "game internals changed (SkillItem.hitEnemy not found)";
                        else
                        {
                            int cdBefore = view.Cooldown, handBefore = Battle.Hand().Count;
                            try
                            {
                                if (view.Aimed) HitEnemy.SetValue(view.Item, target);   // what the aim line sets when the player releases on a target
                                view.Item.TrueUse();
                            }
                            finally
                            {
                                HitEnemy.SetValue(view.Item, null);
                                if (view.Item.scriptExecutor != null) view.Item.scriptExecutor.Target = null;   // as the game does after its own use
                            }
                            var after = List().FirstOrDefault(v => v.Index == slot);
                            var seen = SkillRules.Observed(cdBefore, after?.Cooldown ?? 0, handBefore, Battle.Hand().Count, Battle.Dealing(), DeckSelect.IsOpen());
                            message = "Used " + view.Name + (kind == "self" ? " on yourself" : kind == "enemy" ? " on enemy " + enemyIndex : "")
                                      + (seen != null ? ": " + seen + "." : ": no effect seen yet (cooldown " + cdBefore + "→" + (after?.Cooldown ?? 0) + ", hand unchanged, no picker, draw pile " + (FightCardManager.Instance?.cardList.Count ?? -1) + "); read the state before trying again.");
                        }
                    }
                }
            }
            if (refusal != null) return new RuntimePerformActionResult { Success = false, ActionId = actionId, Message = "refused: " + refusal };
            UnityEngine.Debug.Log("[WitchCtl] " + actionId + ": " + message);
            return new RuntimePerformActionResult { Success = true, ActionId = actionId, Message = message };
        }
    }
}
