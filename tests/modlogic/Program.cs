using System;
using System.Collections.Generic;

namespace WitchCtl
{
    internal static class Program
    {
        private static int failed, passed;

        private static void Check(bool ok, string what)
        {
            if (ok) { passed++; return; }
            failed++;
            Console.Error.WriteLine("FAIL: " + what);
        }

        private static void Eq(object expected, object actual, string what) =>
            Check(Equals(expected, actual), what + " (expected " + (expected ?? "null") + ", got " + (actual ?? "null") + ")");

        private static int Main()
        {
            // ---- Danger.Blocked: things that quit the game, give up the run or break the map
            Eq(null, Danger.Blocked("BattleRewardsUI", "Window Manager/Close", "Close"), "an ordinary close button is fine");
            Check(Danger.Blocked("MainMenuUI", "background/TextList/ExitGame", "Exit") != null, "main menu exit");
            Check(Danger.Blocked("MainMenuUI", "background/TextList/ModManager", "Mods") != null, "main menu mod manager");
            Check(Danger.Blocked("MainMenuUI", "RightTop/Steam", "") != null, "main menu steam link");
            Check(Danger.Blocked("MainMenuUI", "MainMenuUI/background/RightTop/Qq", "Qq") != null, "main menu external link (QQ group, seen live)");
            Eq(null, Danger.Blocked("MainMenuUI", "background/TextList/StartGame", "Start"), "main menu start stays clickable");
            Check(Danger.Blocked("TopBarUI", "Content/Buttons/Setting", "") != null, "top bar settings");
            Check(Danger.Blocked("TopBarUI", "Content/Buttons/ExitGame", "") != null, "top bar exit");
            Check(Danger.Blocked("TopBarUI", "Content/Buttons/EndSlot", "") != null, "end slot in any window");
            Check(Danger.Blocked("SettingUI", "Save/Apply", "Apply") != null, "the whole settings window is blocked");
            Check(Danger.Blocked("ModManagerUI", "Close", "") != null, "the whole mod manager is blocked");
            Check(Danger.Blocked("SettingUI", "CloseTheGame", "") != null, "close the game");
            Check(Danger.Blocked("BreaksUI", "Content/ReturnHome", "") != null, "return home gives the run up");
            Check(Danger.Blocked("MapSelectUI", "Draw/QuillBtn", "") != null, "map pen");
            Check(Danger.Blocked("MapSelectUI", "Draw/EreaseBtn", "") != null, "map eraser");
            Check(Danger.Blocked("MapSelectUI", "Draw/GarbageBtn", "") != null, "map clear");
            Eq(null, Danger.Blocked("MapSelectUI", "Content/Continue", "Continue"), "map continue stays");
            Check(Danger.Blocked("AnnouncementUI", "Windows/Button", "跳转文档") != null, "announcement web link");
            Eq(null, Danger.Blocked("AnnouncementUI", "Windows/ExitButton", ""), "announcement close stays");
            // ExitGame is the leave button of the departure window (back to the hub): not a quit there
            Eq(null, Danger.Blocked("GameEntryUI", "Content/ExitGame", "Leave"), "game entry leave stays");
            // the first-launch language picker has its own ReturnMain (closes the picker): only the settings window is blocked
            Eq(null, Danger.Blocked("LanguageUseUI", "Content/ReturnMain", "OK"), "language picker confirm stays");
            Eq(null, Danger.Blocked(null, null, null), "nulls do not throw");
            Eq(null, Danger.Blocked("FightUI", "Right/ExitGameGuide", ""), "a segment that merely starts with a name is not a match");

            // ---- Danger.ModalEffect: what the Yes/No buttons really do
            Eq(Danger.Quit, Danger.ModalEffect("Quit the game？", true), "quit confirm");
            Eq(null, Danger.ModalEffect("Quit the game？", false), "quit cancel is harmless");
            Eq(Danger.Quit, Danger.ModalEffect("是否退出游戏？", true), "quit in Chinese");
            Eq(Danger.ToMenu, Danger.ModalEffect("Return to the main menu?", true), "menu confirm");
            Eq(Danger.ToMenu, Danger.ModalEffect("是否返回主菜单", true), "menu in Chinese");
            Eq(Danger.EndRun, Danger.ModalEffect("是否要结束此局", true), "slot end");
            Eq(Danger.Continue, Danger.ModalEffect("A save file already exists for this mode. Do you want to load it and continue the game?", true), "save: confirm loads");
            Eq(Danger.NewGame, Danger.ModalEffect("A save file already exists for this mode. Do you want to load it and continue the game?", false), "save: cancel deletes it");
            Eq(Danger.NewGame, Danger.ModalEffect("该模式已存在存档，是否读取存档继续游戏？", false), "save: cancel deletes it (Chinese)");
            Eq(null, Danger.ModalEffect("费用不足", true), "an ordinary notice has no effect");
            Eq(null, Danger.ModalEffect(null, true), "null text");

            // ---- Danger.ModalActionId: harmless keeps the old id, irreversible carries its effect
            Eq("modal_confirm", Danger.ModalActionId(true, null), "plain confirm");
            Eq("modal_cancel", Danger.ModalActionId(false, null), "plain cancel");
            Eq("modal_confirm", Danger.ModalActionId(true, Danger.Continue), "continue the save keeps the plain id");
            Eq("modal_cancel_new_game_deletes_save", Danger.ModalActionId(false, Danger.NewGame), "new game must be asked for by name");
            Eq("modal_confirm_quit_game", Danger.ModalActionId(true, Danger.Quit), "quit must be asked for by name");
            Eq("modal_confirm_return_to_menu", Danger.ModalActionId(true, Danger.ToMenu), "menu must be asked for by name");

            // ---- DeckRules: mirrors ShowCard.MoveItem / SellItem / DecomposeItem, so a refusal is ours and no modal opens in the game
            Check(DeckRules.IsPickerAction("deck_pick_0") && DeckRules.IsPickerAction("deck_select_cancel"), "picker ids are the picker's");
            Check(!DeckRules.IsPickerAction("deck_equip_3") && !DeckRules.IsPickerAction("deck_close") && !DeckRules.IsPickerAction(null), "deck window ids are not");
            Eq(null, DeckRules.Equip(deck: 12, top: 20), "equip with room");
            Check(DeckRules.Equip(deck: 20, top: 20) != null, "equip into a full deck");
            Eq(null, DeckRules.Unequip(deck: 14, bottom: 13, spare: 2, spareMax: 5), "unequip above the minimum");
            Check(DeckRules.Unequip(deck: 13, bottom: 13, spare: 2, spareMax: 5) != null, "unequip at the minimum");
            Check(DeckRules.Unequip(deck: 15, bottom: 13, spare: 5, spareMax: 5) != null, "unequip with a full backpack");
            // selling
            Eq(null, DeckRules.Sell(eternal: false, curse: false, wisdom: 0, equipped: false, deck: 13, bottom: 13), "sell a spare card at the minimum deck size");
            Eq(null, DeckRules.Sell(false, false, 0, equipped: true, deck: 14, bottom: 13), "sell an equipped card above the minimum");
            Check(DeckRules.Sell(false, false, 0, equipped: true, deck: 13, bottom: 13) != null, "sell an equipped card at the minimum");
            Check(DeckRules.Sell(eternal: true, false, 0, false, 20, 13) != null, "an Eternal card cannot be removed");
            Check(DeckRules.Sell(false, curse: true, wisdom: 19, false, 20, 13) != null, "a curse needs wisdom 20 to sell");
            Eq(null, DeckRules.Sell(false, curse: true, wisdom: 20, false, 20, 13), "a curse with wisdom 20");
            // destroying costs gold
            Eq(null, DeckRules.Destroy(false, false, 0, false, 20, 13, money: 20, cost: 20), "destroy with exactly enough gold");
            Check(DeckRules.Destroy(false, false, 0, false, 20, 13, money: 19, cost: 20) != null, "destroy without gold");
            Check(DeckRules.Destroy(true, false, 0, false, 20, 13, money: 99, cost: 20) != null, "destroy an Eternal card");
            Check(DeckRules.Destroy(false, curse: true, wisdom: 19, false, 20, 13, money: 99, cost: 20) != null, "destroy a curse: wisdom must reach the cost");
            Check(DeckRules.Destroy(false, false, 0, equipped: true, deck: 13, bottom: 13, money: 99, cost: 20) != null, "destroy an equipped card at the minimum");
            Eq(null, DeckRules.Destroy(false, false, 0, equipped: false, deck: 13, bottom: 13, money: 99, cost: 20), "destroy a spare card at the minimum deck size");

            // ---- ShopRules: the game returns silently when the money is short (no feedback at all), and a full backpack opens a modal
            Eq(null, ShopRules.Buy("Relic", price: 150, gold: 150, backpack: 5, backpackMax: 5, enchantable: 0), "relic with exactly enough gold");
            Check(ShopRules.Buy("Relic", 150, 149, 0, 5, 0) != null, "relic one gold short");
            Eq(null, ShopRules.Buy("Card", 50, 60, backpack: 4, backpackMax: 5, enchantable: 0), "card with room in the backpack");
            Check(ShopRules.Buy("Card", 50, 60, backpack: 5, backpackMax: 5, enchantable: 0) != null, "card into a full backpack");
            Check(ShopRules.Buy("Card", 50, 10, 0, 5, 0) != null, "card without gold");
            Eq(null, ShopRules.Buy("Bless", 20, 20, 5, 5, 0), "blessings do not use the backpack");
            Check(ShopRules.Buy("EnchTag", 80, 200, 0, 5, enchantable: 0) != null, "an enchant needs a card without one");
            Eq(null, ShopRules.Buy("EnchTag", 80, 200, 0, 5, enchantable: 3), "an enchant with candidates");
            Eq(null, ShopRules.Buy("RandomBlessing", 70, 70, 5, 5, 0), "divination with the gold");
            Check(ShopRules.Buy("Mystery", 1, 99, 0, 5, 0) != null, "an unknown kind is refused instead of guessed");
            Eq(true, ShopRules.UsesEnchantPath("EnchTag"), "enchants go through EnchTryBuy (TryBuy would charge twice)");
            Eq(false, ShopRules.UsesEnchantPath("Relic"), "everything else through TryBuy");
            Eq(null, ShopRules.Refresh(count: 0, max: 3, step: 10, gold: 20), "first refresh costs 20");
            Check(ShopRules.Refresh(0, 3, 10, 19) != null, "refresh without gold");
            Check(ShopRules.Refresh(3, 3, 10, 999) != null, "refresh limit reached");
            Eq(30, ShopRules.RefreshCost(count: 1, step: 10), "second refresh = 1 x step + 20");

            // ---- SkillRules.Observed: a returned TrueUse is not proof (the first live use of 神选 changed nothing)
            Eq(null, SkillRules.Observed(0, 0, 6, 6, 0, false), "nothing changed → no effect seen");
            Eq("cooldown 0→1, a card picker opened", SkillRules.Observed(0, 1, 6, 6, 0, true), "cooldown and picker");
            Eq("2 card(s) being dealt, hand 6→7", SkillRules.Observed(0, 0, 6, 7, 2, false), "cards dealt");
            Eq(null, SkillRules.Observed(2, 1, 6, 6, 0, false), "a cooldown that only ticks down is not an effect");

            // ---- SkillRules: action ids of career skills (target in the id: /act carries only an id)
            Eq(true, SkillRules.TryParse("skill_use_0", out var slot, out var kind, out var enemy), "plain id parses");
            Eq(0, slot, "slot of skill_use_0"); Eq("", kind, "no target kind"); Eq(-1, enemy, "no enemy");
            Eq(true, SkillRules.TryParse("skill_use_2_self", out slot, out kind, out enemy), "self target parses");
            Eq(2, slot, "slot of skill_use_2_self"); Eq("self", kind, "self kind");
            Eq(true, SkillRules.TryParse("skill_use_1_enemy_3", out slot, out kind, out enemy), "enemy target parses");
            Eq(1, slot, "slot"); Eq("enemy", kind, "enemy kind"); Eq(3, enemy, "enemy index");
            Eq(false, SkillRules.TryParse("skill_use_", out _, out _, out _), "no slot");
            Eq(false, SkillRules.TryParse("skill_use_x", out _, out _, out _), "slot not a number");
            Eq(false, SkillRules.TryParse("skill_use_1_enemy", out _, out _, out _), "enemy without index");
            Eq(false, SkillRules.TryParse("skill_use_1_enemy_-2", out _, out _, out _), "negative enemy");
            Eq(false, SkillRules.TryParse("skill_use_1_partner_0", out _, out _, out _), "partners are not offered");
            Eq(false, SkillRules.TryParse("battle_play_card_1", out _, out _, out _), "not a skill id");
            Eq(false, SkillRules.TryParse(null, out _, out _, out _), "null");
            Eq("skill_use_1", SkillRules.Id(1, null, -1), "id without target");
            Eq("skill_use_1_self", SkillRules.Id(1, "self", -1), "id for self");
            Eq("skill_use_1_enemy_0", SkillRules.Id(1, "enemy", 0), "id for an enemy");

            // ---- TargetRules: who a targeted card may hit (card Vars CanSelf / CanEnemy)
            Eq(null, TargetRules.Choose(canSelf: false, canEnemy: true, wantSelf: false, wantEnemy: false, out var tk), "default: an ordinary attack hits an enemy");
            Eq("enemy", tk, "kind enemy");
            Eq(null, TargetRules.Choose(true, true, false, false, out tk), "a card that may hit both defaults to an enemy");
            Eq("enemy", tk, "default prefers the enemy");
            Eq(null, TargetRules.Choose(true, false, false, false, out tk), "a self-only card defaults to the player");
            Eq("self", tk, "kind self");
            Check(TargetRules.Choose(false, false, false, false, out _) != null, "a card with no legal target kind");
            Eq(null, TargetRules.Choose(true, true, wantSelf: true, wantEnemy: false, out tk), "explicit self on a CanSelf card");
            Eq("self", tk, "explicit self");
            Check(TargetRules.Choose(false, true, wantSelf: true, wantEnemy: false, out _) != null, "self on a card that cannot target yourself");
            Check(TargetRules.Choose(true, false, wantSelf: false, wantEnemy: true, out _) != null, "enemy on a card that excludes enemies");
            Check(TargetRules.Choose(true, true, wantSelf: true, wantEnemy: true, out _) != null, "asking for both is ambiguous");
            Eq(true, TargetRules.CanSelf("True"), "CanSelf True"); Eq(false, TargetRules.CanSelf(null), "CanSelf missing"); Eq(false, TargetRules.CanSelf("False"), "CanSelf False");
            Eq(true, TargetRules.CanEnemy(null), "CanEnemy missing = true"); Eq(false, TargetRules.CanEnemy("False"), "CanEnemy False"); Eq(true, TargetRules.CanEnemy("True"), "CanEnemy True");

            // ---- HouseRules: the hub's page buttons an agent may press
            foreach (var ok in new[] { "OutsiderShop", "cardShop", "rollShop", "task", "announcement", "CardEditor" }) Eq(null, HouseRules.Refusal(ok), "hub page button allowed: " + ok);
            foreach (var no in new[] { "ModManager", "TeachMode", "store", "Dictionary", "GameMenu", "crystalBall", "Achievement", "shadow", "Unknown" })
                Check(HouseRules.Refusal(no) != null, "hub page button refused: " + no);

            // ---- MapRules: who builds the route, and the node type from the prefab name
            Check(MapRules.PlayerBuilds(0) && MapRules.PlayerBuilds(6) && MapRules.PlayerBuilds(12), "levels 0, 6, 12 are built by the player");
            Check(!MapRules.PlayerBuilds(1) && !MapRules.PlayerBuilds(5) && !MapRules.PlayerBuilds(7), "other levels are filled by the host");
            Eq("Fight", MapRules.KindOf("FightPrefab(Clone)"), "kind of a cloned fight prefab");
            Eq("Event", MapRules.KindOf("EventPrefab"), "kind without (Clone)");
            Eq(null, MapRules.KindOf("Null"), "not a prefab name");
            Eq(null, MapRules.KindOf("Prefab(Clone)"), "no type before Prefab");
            Eq(null, MapRules.KindOf(null), "null name");

            // ---- EntryRules: ids of the before-the-run choices (the choice is part of the id) and the card-pack floor
            Eq(true, EntryRules.TryParse("entry_career_2", out var ek, out var ea) && ek == "career" && ea == "2", "career index");
            Eq(true, EntryRules.TryParse("entry_partner_0", out ek, out ea) && ek == "partner" && ea == "0", "partner index");
            Eq(true, EntryRules.TryParse("entry_main_Lucky", out ek, out ea) && ek == "main" && ea == "Lucky", "main attribute");
            Eq(true, EntryRules.TryParse("entry_second_Wisdom", out ek, out ea) && ek == "second" && ea == "Wisdom", "second attribute");
            Eq(true, EntryRules.TryParse("entry_pack_off_cardpack_9", out ek, out ea) && ek == "pack_off" && ea == "cardpack_9", "pack id keeps its underscore");
            Eq(true, EntryRules.TryParse("entry_pack_on_cardpack_14", out ek, out ea) && ek == "pack_on" && ea == "cardpack_14", "pack on");
            Eq(true, EntryRules.TryParse(EntryRules.PrepareOpen, out ek, out _) && ek == "prepare_open", "open the window");
            Eq(true, EntryRules.TryParse(EntryRules.PrepareClose, out ek, out _) && ek == "prepare_close", "close the window");
            Check(!EntryRules.TryParse("entry_career_x", out _, out _), "career index must be a number");
            Check(!EntryRules.TryParse("entry_career_-1", out _, out _), "career index must not be negative");
            Check(!EntryRules.TryParse("entry_main_Charm", out _, out _), "unknown attribute");
            Check(!EntryRules.TryParse("entry_pack_on_", out _, out _), "empty pack id");
            Check(!EntryRules.TryParse("entry_unknown", out _, out _) && !EntryRules.TryParse("game_entry_start", out _, out _) && !EntryRules.TryParse(null, out _, out _), "other ids are not ours");
            Eq("entry_main_Strength", EntryRules.Var("main", "Strength"), "id builder: main");
            Eq("entry_pack_off_cardpack_3", EntryRules.Pack(false, "cardpack_3"), "id builder: pack off");
            Eq(null, EntryRules.PackRefusal(enable: true, isOn: false, enabled: 6), "enabling needs no floor");
            Check(EntryRules.PackRefusal(enable: true, isOn: true, enabled: 7) != null, "enabling an enabled pack");
            Check(EntryRules.PackRefusal(enable: false, isOn: false, enabled: 7) != null, "disabling a disabled pack");
            Check(EntryRules.PackRefusal(enable: false, isOn: true, enabled: 6) != null, "disabling at the floor of 6");
            Eq(null, EntryRules.PackRefusal(enable: false, isOn: true, enabled: 7), "disabling above the floor");

            // ---- OutsiderRules: the hub's meta shops (cards / origins) pay with truth crystals or time, once per level
            Eq("buy", OutsiderRules.Mode(bought: 0, buyCount: 1, canClose: false), "unbought one-off");
            Eq("buy", OutsiderRules.Mode(bought: 2, buyCount: 5, canClose: false), "levels left");
            Eq("owned", OutsiderRules.Mode(bought: 1, buyCount: 1, canClose: false), "bought one-off, always on");
            Eq("toggle", OutsiderRules.Mode(bought: 1, buyCount: 1, canClose: true), "bought one-off that can be switched off");
            Eq("owned", OutsiderRules.Mode(bought: 5, buyCount: 5, canClose: true), "maxed levels are not a switch");
            Eq(null, OutsiderRules.BuyRefusal("Truth", 200, truth: 672, time: 0), "enough truth");
            Eq(null, OutsiderRules.BuyRefusal("Truth", 200, truth: 200, time: 0), "exactly enough truth");
            Check(OutsiderRules.BuyRefusal("Truth", 200, truth: 199, time: 5000) != null, "time does not pay a truth price");
            Eq(null, OutsiderRules.BuyRefusal("Time", 30, truth: 0, time: 30), "enough time");
            Check(OutsiderRules.BuyRefusal("Time", 30, truth: 9999, time: 29) != null, "truth does not pay a time price");
            Check(OutsiderRules.BuyRefusal("Gold", 1, truth: 9, time: 9) != null, "unknown currency");
            Eq(true, OutsiderRules.TryParse("outsider_buy_3", out var ok1, out var oi) && ok1 == "buy" && oi == 3, "buy id");
            Eq(true, OutsiderRules.TryParse("outsider_toggle_0", out ok1, out oi) && ok1 == "toggle" && oi == 0, "toggle id");
            Eq(true, OutsiderRules.TryParse(OutsiderRules.LeaveId, out ok1, out _) && ok1 == "leave", "leave id");
            Check(!OutsiderRules.TryParse("outsider_buy_x", out _, out _) && !OutsiderRules.TryParse("outsider_buy_-1", out _, out _) && !OutsiderRules.TryParse("shop_buy_1", out _, out _) && !OutsiderRules.TryParse(null, out _, out _), "foreign or malformed ids");
            Eq("outsider_buy_4", OutsiderRules.Buy(4), "id builder: buy");
            Eq("outsider_toggle_2", OutsiderRules.Toggle(2), "id builder: toggle");

            Console.WriteLine(passed + " passed, " + failed + " failed");
            return failed == 0 ? 0 : 1;
        }
    }
}
