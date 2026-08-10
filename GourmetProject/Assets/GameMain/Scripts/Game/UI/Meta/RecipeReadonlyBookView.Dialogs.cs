using System;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;
using GourmetProject.Game.UI.Common;
using GourmetProject.Gameplay.Model;
using GourmetProject.Runtime;

namespace GourmetProject.Game.UI.Meta
{
    public sealed partial class RecipeReadonlyBookView
    {
        private void ShowActiveItemConfirm(
            ItemDefinition item,
            ActiveTarget target,
            Action<ActiveTarget, Action> onConfirm)
        {
            if (item == null)
            {
                return;
            }

            var data = new ConfirmDialogData
            {
                Title = item.Name,
                Message = item.Desc,
                ConfirmText = "使用",
                CancelText = "返回",
                OnConfirm = () => onConfirm?.Invoke(target, null),
            };
            GameApp.UI.OpenUIForm(
                UIForms.ConfirmDialog,
                UIForms.GroupDialog,
                data);
        }

        private void ShowEventDeleteConfirm(
            string title,
            ActiveTarget target,
            Action onCancel,
            Action<ActiveTarget> onConfirm)
        {
            RecipeBookSlot slot = RecipeSlot(target);
            DishDef def = slot == null
                ? null
                : _run.Database.GetDish(slot.DishId);
            string dishName = def != null ? def.Name : target.Id;
            var data = new ConfirmDialogData
            {
                Title = string.IsNullOrWhiteSpace(title)
                    ? "确认删除食物"
                    : title,
                Message = $"确定要从食谱中删除「{dishName}」吗？",
                ConfirmText = "确定",
                CancelText = "返回",
                OnConfirm = () => onConfirm?.Invoke(target),
                OnCancel = onCancel,
            };
            GameApp.UI.OpenUIForm(
                UIForms.ConfirmDialog,
                UIForms.GroupDialog,
                data);
        }

        private void ShowShopDeleteConfirm(ActiveTarget target)
        {
            if (!ShopService.CanDeleteDish(_run))
            {
                RebuildWarehouseForCurrentState();
                return;
            }

            RecipeBookSlot slot = RecipeSlot(target);
            DishDef def = slot == null
                ? null
                : _run.Database.GetDish(slot.DishId);
            if (slot == null || def == null)
            {
                return;
            }

            int cost = ShopService.DeleteCost(_run);
            var data = new ConfirmDialogData
            {
                Title = "确认删除食物",
                Message = $"花费 {cost} 金币\n从食谱中删除「{def.Name}」？",
                ConfirmText = "确定",
                CancelText = "返回",
                OnConfirm = () =>
                {
                    if (!ShopService.DeleteDishAt(_run, target.Y))
                    {
                        RebuildWarehouseForCurrentState();
                        return;
                    }

                    _onChanged?.Invoke();
                    _onExit?.Invoke();
                },
            };
            GameApp.UI.OpenUIForm(
                UIForms.ConfirmDialog,
                UIForms.GroupDialog,
                data);
        }
    }
}
