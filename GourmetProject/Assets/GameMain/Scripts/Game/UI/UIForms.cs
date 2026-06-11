namespace GourmetProject.Game.UI
{
    /// <summary>
    /// 界面资源路径与界面组常量的集中入口。后续若接入 Luban UI 配置表，
    /// 只需把这里的常量替换为查表即可，调用方无需改动。
    /// </summary>
    public static class UIForms
    {
        // 界面组名（在 ProcedureMenu 启动时通过 AddUIGroup 注册）。
        public const string GroupDefault = "Default";
        public const string GroupDialog = "Dialog";

        // 界面预制体资源路径（编辑器资源模式下直接用工程内路径）。
        public const string MainMenu = "Assets/GameMain/UI/MainMenuForm.prefab";
        public const string CharacterSelect = "Assets/GameMain/UI/CharacterSelectForm.prefab";
        public const string Settings = "Assets/GameMain/UI/SettingsForm.prefab";
        public const string ConfirmDialog = "Assets/GameMain/UI/ConfirmDialogForm.prefab";
        public const string CartoonSceneTransition = "Assets/GameMain/UI/CartoonSceneTransitionForm.prefab";

        // 主存档槽位：用于判断“开始游戏 / 继续游戏”。
        public const string GameSaveSlot = "slot0";
    }
}
