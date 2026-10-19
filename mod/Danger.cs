using System;
using System.Linq;

namespace WitchCtl
{
    /// <summary>
    /// What an agent must never do by accident, as plain string logic (no Unity types, so tests/modlogic compiles this file on its own).
    /// Two questions: which UI elements may not be listed or clicked (<see cref="Blocked"/>), and what a yes/no window's buttons
    /// really do (<see cref="ModalEffect"/>): the game words them only as Yes/No, and a "No" may delete the save.
    /// </summary>
    internal static class Danger
    {
        // ------------------------------------------------------------------ elements

        private static bool Has(string path, params string[] names) =>
            path.Split('/').Any(seg => names.Any(n => seg.Equals(n, StringComparison.OrdinalIgnoreCase)));

        private static bool Contains(string path, params string[] parts) =>
            parts.Any(p => path.IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0);

        /// <summary>Why this clickable must not be offered by `ui` or accepted by `click`, or null. Matching is on the window name and the
        /// path of segments (names from the decompile; a prefab that renames them slips through, which `recon` is there to catch).</summary>
        internal static string Blocked(string window, string path, string label)
        {
            window = window ?? ""; path = path ?? ""; label = label ?? "";

            if (window == "SettingUI") return "settings: its buttons quit the game, give up the run or upload a feedback report";
            if (window == "ModManagerUI") return "mod manager: it can switch this very mod off and downloads from the workshop";
            if (path.Length > 0 && Has(path, "CloseTheGame", "QuitLobby")) return "quits the game";
            if (Has(path, "ReturnHome", "ReturnToMenu")) return "gives up the run";
            if (Has(path, "EndSlot")) return "settles and ends the run";

            if (window == "MainMenuUI" && Has(path, "ExitGame")) return "quits the game";
            if (window == "MainMenuUI" && Has(path, "ModManager")) return "opens the mod manager";
            if (window == "MainMenuUI" && Has(path, "RightTop")) return "external links (Steam page, QQ group): opens the browser";
            if (window == "TopBarUI" && (Has(path, "ExitGame") || Contains(path, "Setting"))) return "opens settings or leaves the run";
            if (window == "MapSelectUI" && Has(path, "QuillBtn", "EreaseBtn", "GarbageBtn"))
                return "multiplayer map drawing: an open pen covers the map and swallows the pointer";
            if (window == "AnnouncementUI" && Contains(label, "跳转", "文档", "Document", "Open the")) return "opens a web page";
            return null;
        }

        // ------------------------------------------------------------------ yes/no windows

        internal const string Quit = "quit_game", ToMenu = "return_to_menu", EndRun = "end_run", Continue = "continue_save", NewGame = "new_game_deletes_save";

        private static readonly string[] QuitWords = { "quit the game", "退出游戏", "退出遊戲", "ゲームを終了", "게임을 종료" };
        private static readonly string[] MenuWords = { "main menu", "主菜单", "主選單", "メインメニュー", "메인 메뉴" };
        private static readonly string[] EndWords = { "结束此局", "結束此局", "end this" };
        private static readonly string[] SaveWords = { "save file", "存档", "存檔", "セーブ", "저장" };

        private static bool Any(string text, string[] words) =>
            words.Any(w => text.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0);

        /// <summary>
        /// What clicking the confirm or the cancel button of a window with this text will do, or null when it is ordinary / unknown.
        /// "Return to the main menu?" and "Quit the game?" answer yes = lose the run / leave; the "a save exists, continue?" window has
        /// confirm = load it and cancel = <b>New Game, which deletes the old save at once</b> (ModeChoiceUI.CreateNewSave).
        /// </summary>
        internal static string ModalEffect(string text, bool confirm)
        {
            text = text ?? "";
            if (Any(text, QuitWords)) return confirm ? Quit : null;
            if (Any(text, EndWords)) return confirm ? EndRun : null;
            if (Any(text, MenuWords)) return confirm ? ToMenu : null;
            if (Any(text, SaveWords)) return confirm ? Continue : NewGame;
            return null;
        }

        /// <summary>Effects the agent has to ask for by name: the action id carries the effect (`modal_cancel_new_game_deletes_save`),
        /// so a bot that only knows `modal_confirm` / `modal_cancel` can never trigger one.</summary>
        internal static bool Irreversible(string effect) => effect == Quit || effect == ToMenu || effect == EndRun || effect == NewGame;

        /// <summary>The action id for a modal button: plain when harmless, suffixed with the effect when not.</summary>
        internal static string ModalActionId(bool confirm, string effect) =>
            (confirm ? "modal_confirm" : "modal_cancel") + (Irreversible(effect) ? "_" + effect : "");
    }
}
