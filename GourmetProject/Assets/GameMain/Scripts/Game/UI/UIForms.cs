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

        // 玩法界面。
        public const string Battle = "Assets/GameMain/UI/BattleForm.prefab";

        // 行动轴节点 / 道具 hover Tips（同为 View，配合 TipHoverTrigger 悬停显示）。
        public const string BossFeastTip = "Assets/GameMain/UI/TipsView/BossFeastTipView.prefab";
        public const string ShopNodeTip = "Assets/GameMain/UI/TipsView/ShopNodeTipView.prefab";
        public const string InterestNodeTip = "Assets/GameMain/UI/InterestNodeTipView.prefab";
        public const string ItemTip = "Assets/GameMain/UI/TipsView/ItemTipView.prefab";
        public const string Reward = "Assets/GameMain/UI/RewardForm.prefab";
        public const string Result = "Assets/GameMain/UI/ResultForm.prefab";
        public const string Defeat = "Assets/GameMain/UI/DefeatForm.prefab";

        // 主存档槽位：用于判断“开始游戏 / 继续游戏”。
        public const string GameSaveSlot = "slot0";
    }
}
