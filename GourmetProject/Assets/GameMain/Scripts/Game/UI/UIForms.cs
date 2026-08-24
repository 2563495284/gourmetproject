using GourmetProject.Game.Save;
using GourmetProject.Runtime;
using UnityEngine;

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
        public const string GroupTooltip = "Tooltip";
        public const string GroupTutorial = "Tutorial";
        public const string GroupTransition = "Transition";

        /// <summary>
        /// 返回跨界面组的 Tips 专用层。该层位于 Dialog 之上、Tutorial/Transition 之下；
        /// 尚未完成 UI Group 初始化时回退到调用方提供的父节点。
        /// </summary>
        public static Transform ResolveTooltipLayer(Transform fallback)
        {
            var group = GameApp.UI?.GetUIGroup(GroupTooltip);
            return group?.Helper is Component helper ? helper.transform : fallback;
        }

        // 界面预制体资源路径（编辑器资源模式下直接用工程内路径）。
        public const string MainMenu = "Assets/GameMain/Content/Prefabs/UI/Menu/MainMenuForm.prefab";
        public const string CharacterSelect = "Assets/GameMain/Content/Prefabs/UI/Menu/CharacterSelectForm.prefab";
        public const string Settings = "Assets/GameMain/Content/Prefabs/UI/Settings/SettingsForm.prefab";
        public const string ConfirmDialog = "Assets/GameMain/Content/Prefabs/UI/Common/ConfirmDialogForm.prefab";
        public const string CartoonSceneTransition = "Assets/GameMain/Content/Prefabs/UI/Common/CartoonSceneTransitionForm.prefab";
        public const string OpeningComic = "Assets/GameMain/Content/Prefabs/UI/Menu/OpeningComicForm.prefab";
        public const string Toast = "Assets/GameMain/Content/Prefabs/UI/Common/ToastForm.prefab";

        // 玩法界面。
        public const string Battle = "Assets/GameMain/Content/Prefabs/UI/Battle/BattleForm.prefab";
        public const string TimelineLab = "Assets/GameMain/Content/Prefabs/UI/Development/TimelinePresentationLabForm.prefab";
        public const string FlavorLab = "Assets/GameMain/Content/Prefabs/UI/Development/FlavorPresentationLabForm.prefab";

        // 时间轴节点 / 装饰品和消耗品 hover Tips（同为 View，配合 TipHoverTrigger 悬停显示）。
        public const string TimelineNodeTip = "Assets/GameMain/Content/Prefabs/UI/Tooltips/TimelineNodeTipView.prefab";
        public const string ItemTip = "Assets/GameMain/Content/Prefabs/UI/Tooltips/ItemTipView.prefab";
        public const string Reward = "Assets/GameMain/Content/Prefabs/UI/Meta/Rewards/RewardForm.prefab";
        public const string Result = "Assets/GameMain/Content/Prefabs/UI/Meta/Rewards/ResultForm.prefab";
        public const string HeartBreak = "Assets/GameMain/Content/Prefabs/UI/Meta/Rewards/HeartBreakForm.prefab";
        public const string StarAward = "Assets/GameMain/Content/Prefabs/UI/Meta/Rewards/StarAwardForm.prefab";

        // 玩家总档槽位。保留此别名供旧调用兼容；新持久化代码统一经 GameSavePersistence 访问。
        public const string GameSaveSlot = GameSavePersistence.Slot;
    }
}
