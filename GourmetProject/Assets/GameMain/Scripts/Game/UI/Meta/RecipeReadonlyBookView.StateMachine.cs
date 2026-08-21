using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;
using GourmetProject.Gameplay.Battle;

namespace GourmetProject.Game.UI.Meta
{
    public sealed partial class RecipeReadonlyBookView
    {
        /// <summary>
        /// 单次食谱界面的不可变模式配置。它集中回答“显示什么”和“交互如何路由”，
        /// 视图本身只负责复用已有对象并把数据绑定到 prefab。
        /// </summary>
        internal sealed class RecipeReadonlyBookSession
        {
            private readonly RecipeReadonlyBookRequest _request;

            public RecipeReadonlyBookSession(RecipeReadonlyBookRequest request)
            {
                _request = request;
            }

            public RecipeReadonlyBookMode Mode => _request.Mode;

            public bool IsShopDelete =>
                Mode == RecipeReadonlyBookMode.ShopDeleteDish;

            public bool UsesSemanticTitle =>
                Mode == RecipeReadonlyBookMode.ActiveItemTarget;

            public string PanelTitle
            {
                get
                {
                    switch (Mode)
                    {
                        case RecipeReadonlyBookMode.ReadonlyBook:
                            return "查看食谱";
                        case RecipeReadonlyBookMode.ReadonlyDishPool:
                            return string.IsNullOrWhiteSpace(_request.Title)
                                ? "可能获得的食物"
                                : _request.Title;
                        case RecipeReadonlyBookMode.ShopDeleteDish:
                            return "选择一个食物进行删除";
                        case RecipeReadonlyBookMode.ActiveItemTarget:
                            return _request.Item != null
                                ? _request.Item.Desc
                                : "选择食物";
                        case RecipeReadonlyBookMode.EventDeleteDish:
                            return string.IsNullOrWhiteSpace(_request.Title)
                                ? "选择要删除的食物"
                                : _request.Title;
                        default:
                            return "查看食谱";
                    }
                }
            }

            public string ExitButtonText
            {
                get
                {
                    switch (Mode)
                    {
                        case RecipeReadonlyBookMode.ReadonlyBook:
                            return "返回";
                        case RecipeReadonlyBookMode.ShopDeleteDish:
                            return "返回商店";
                        case RecipeReadonlyBookMode.EventDeleteDish:
                            return "返回事件";
                        default:
                            return "取消";
                    }
                }
            }

            public bool CanClickDish =>
                Mode == RecipeReadonlyBookMode.ShopDeleteDish
                || Mode == RecipeReadonlyBookMode.ActiveItemTarget
                || Mode == RecipeReadonlyBookMode.EventDeleteDish;

            public bool ShowExitButton =>
                Mode != RecipeReadonlyBookMode.ReadonlyDishPool
                && (Mode != RecipeReadonlyBookMode.ReadonlyBook
                    || !_request.HideExitButton);

            public int BookIndexFilter
            {
                get
                {
                    switch (Mode)
                    {
                        case RecipeReadonlyBookMode.ReadonlyBook:
                            return _request.BookIndex;
                        case RecipeReadonlyBookMode.ReadonlyDishPool:
                            return 0;
                        default:
                            return -1;
                    }
                }
            }

            public void OnExitClicked()
            {
                switch (Mode)
                {
                    case RecipeReadonlyBookMode.ReadonlyBook:
                    case RecipeReadonlyBookMode.ShopDeleteDish:
                        _request.OnExit?.Invoke();
                        break;
                    case RecipeReadonlyBookMode.ActiveItemTarget:
                    case RecipeReadonlyBookMode.EventDeleteDish:
                        _request.OnCancel?.Invoke();
                        break;
                }
            }

            public void OnDishClicked(
                RecipeReadonlyBookView panel,
                RecipeEditDishView dish)
            {
                if (panel == null || dish == null || !CanClickDish)
                {
                    return;
                }

                switch (Mode)
                {
                    case RecipeReadonlyBookMode.ShopDeleteDish:
                        if (ShopService.DeleteDishRemaining(panel._run) <= 0)
                        {
                            dish.PlayInteractionFailed();
                            return;
                        }

                        if (panel.TryBuildRecipeTarget(
                                dish,
                                out ActiveTarget shopTarget))
                        {
                            panel.ShowShopDeleteConfirm(shopTarget);
                        }

                        break;
                    case RecipeReadonlyBookMode.ActiveItemTarget:
                        if (_request.Item != null
                            && panel.TryBuildRecipeTarget(
                                dish,
                                out ActiveTarget itemTarget))
                        {
                            panel.ShowActiveItemConfirm(
                                _request.Item,
                                itemTarget,
                                _request.OnTargetConfirmed);
                        }

                        break;
                    case RecipeReadonlyBookMode.EventDeleteDish:
                        if (panel.TryBuildRecipeTarget(
                                dish,
                                out ActiveTarget eventTarget))
                        {
                            panel.ShowEventDeleteConfirm(
                                _request.Title,
                                eventTarget,
                                () => { },
                                target => _request.OnTargetConfirmed?.Invoke(
                                    target,
                                    null));
                        }

                        break;
                }
            }
        }
    }
}
