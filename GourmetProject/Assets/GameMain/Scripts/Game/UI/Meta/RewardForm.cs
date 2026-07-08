using System;
using System.Collections.Generic;
using GourmetProject.Core.Rng;
using GourmetProject.Game.Adapter;
using GourmetProject.Game.Flow;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;
using GourmetProject.Game.UI.Hud;
using GourmetProject.Gameplay.Battle;
using GourmetProject.Gameplay.Model;
using GourmetProject.Runtime;
using GourmetProject.Runtime.UI;
using UnityEngine;
using UnityEngine.UI;
using GourmetProject.Game.UI;
using GourmetProject.Game.UI.Battle;
using GourmetProject.Game.UI.Common;
using GourmetProject.Game.UI.Menu;
using GourmetProject.Game.UI.Meta;
using GourmetProject.Game.UI.Widgets;

namespace GourmetProject.Game.UI.Meta
{
    /// <summary>
    /// 过关领奖界面：展示本周得分与发放的奖励，点「继续」推进到下一周（或通关返回菜单）。
    /// 结构全固定、落在 RewardForm.prefab，脚本只赋文本并按是否最终周切换按钮组。
    /// 奖励经 RewardGranter 按周命名流发放，并在推进时存档以支持「继续游戏」。
    /// </summary>
    public sealed class RewardForm : UGuiForm
    {
        [SerializeField] private Text _titleText;
        [SerializeField] private Text _scoreText;
        [SerializeField] private Button _continueButton;
        [SerializeField] private Button _endlessButton;
        [SerializeField] private Button _menuButton;
        [SerializeField] private RectTransform _rewardListContent;
        [SerializeField] private RewardChoiceRowView _rewardRowTemplate;

        private GameRun _run;
        private RewardOffer _offer;
        private readonly List<RewardChoiceRowView> _spawnedRows = new List<RewardChoiceRowView>();
        private string _rewardKey;
        private int _lastTotal;
        private int _lastTarget;

        protected override void OnInit(object userData)
        {
            base.OnInit(userData);
            _continueButton.onClick.AddListener(OnContinue);
            _endlessButton.onClick.AddListener(OnContinue);
            _menuButton.onClick.AddListener(OnReturnMenu);
        }

        protected override void OnOpen(object userData)
        {
            base.OnOpen(userData);

            _run = GameRunContext.Current;
            if (_run == null)
            {
                Close();
                return;
            }

            BattleSession session = BattleForm.Active?.Session;
            int total = session != null && session.IsSettled ? session.LastResult.Total : 0;
            int target = session?.RequiredScore ?? _run.RequiredScore;
            ActionExecutionContext actionContext = BattleForm.Active?.CurrentBattleActionContext;
            _rewardKey = GameRun.BuildRewardKey(_run.WeekIndex, _run.CurrentDay, actionContext);

            _offer = _run.GetPendingRewardOffer(_rewardKey);
            if (_offer == null)
            {
                IRandomStream rng = GameApp.Random.DomainStream(SeedDomains.Reward, _rewardKey);
                _offer = RewardGranter.GenerateOffer(_run, _run.CurrentWeek, rng, actionContext);
                _run.SetPendingRewardOffer(_rewardKey, _offer);
                RunPersistence.Save(_run);
            }

            _lastTotal = total;
            _lastTarget = target;

            RefreshOffer();
        }

        private void RefreshOffer()
        {
            _titleText.text = "美食成功";
            _scoreText.text = $"得分 {_lastTotal} / 目标 {_lastTarget}";

            // 行动轴模型下发奖不再推进周；底部只保留「继续行动」出口。
            _continueButton.gameObject.SetActive(true);
            _endlessButton.gameObject.SetActive(false);
            _menuButton.gameObject.SetActive(false);

            SetButtonLabel(_continueButton, "继续行动");
            _continueButton.interactable = _offer == null || (_offer.IsFullyClaimed && !_run.HasPendingFragmentPack);

            RebuildRewardRows();
        }

        private void OnContinue()
        {
            if (_offer != null && (!_offer.IsFullyClaimed || _run.HasPendingFragmentPack))
            {
                RefreshOffer();
                return;
            }

            CompleteRewards(closeForm: true);
        }

        /// <summary>结算并推进：清空 pending offer、存档、（可选）关界面并回到行动轴，等效于点「继续」。</summary>
        private void CompleteRewards(bool closeForm)
        {
            _run.ClearPendingRewardOffer();
            RunPersistence.Save(_run);
            if (closeForm)
            {
                Close();
            }

            BattleForm.Active?.OnRewardConfirmed();
        }

        /// <summary>领取动作后：若这是最后一个奖励（offer 已全部领取且无待拼碎片），直接等效于点「继续」。</summary>
        private bool TryAutoComplete(bool closeForm)
        {
            if (_offer == null || _run == null || !_offer.IsFullyClaimed || _run.HasPendingFragmentPack)
            {
                return false;
            }

            CompleteRewards(closeForm);
            return true;
        }

        private void OnReturnMenu()
        {
            OnContinue();
        }

        private void Close()
        {
            GameApp.UI.CloseUIForm(UIForm);
        }

        private void ClaimBaseGold()
        {
            if (_offer == null || _offer.BaseGoldClaimed)
            {
                return;
            }

            RewardGranter.ApplyBaseGold(_run, _offer);
            _run.SetPendingRewardOffer(_rewardKey, _offer);
            RunPersistence.Save(_run);

            // 只剩金币这一个奖励，领完直接等效于点「继续」。
            if (TryAutoComplete(closeForm: true))
            {
                return;
            }

            RefreshOffer();
        }

        private void ClaimChoice(bool extra, int index, IReadOnlyList<RewardChoice> groupChoices)
        {
            if (_offer == null || groupChoices == null || index < 0 || index >= groupChoices.Count)
            {
                return;
            }

            if (IsChoiceResolved(extra))
            {
                return;
            }

            RewardChoice choice = groupChoices[index];
            if (choice == null)
            {
                return;
            }

            if (choice.Kind == cfg.RewardKind.DishChoice)
            {
                OpenDishPack(extra, groupChoices);
                return;
            }

            if (choice.Kind == cfg.RewardKind.FragmentChoice)
            {
                RewardGranter.ApplyFragmentPack(_run, groupChoices);
                _run.SetPendingRewardOffer(_rewardKey, _offer);
                RunPersistence.Save(_run);

                Close();
                BattleForm.Active?.OpenRewardBoardEdit(placed =>
                {
                    if (placed && !IsChoiceResolved(extra))
                    {
                        MarkChoiceClaimed(extra, index);
                        _run.SetPendingRewardOffer(_rewardKey, _offer);
                    }

                    RunPersistence.Save(_run);

                    // 拼完碎片（placed）且这是最后一个奖励：不再弹回 RewardForm，直接等效于点「继续」。
                    // 若在棋盘编辑里选择跳过（!placed），碎片奖励仍保留，照常弹回 RewardForm。
                    if (TryAutoComplete(closeForm: false))
                    {
                        return;
                    }

                    GameApp.UI.OpenUIForm(UIForms.Reward, UIForms.GroupDialog);
                });
                return;
            }

            RewardGranter.ApplyChoice(_run, choice);
            MarkChoiceClaimed(extra, index);
            _run.SetPendingRewardOffer(_rewardKey, _offer);
            RunPersistence.Save(_run);

            if (TryAutoComplete(closeForm: true))
            {
                return;
            }

            RefreshOffer();
        }

        private bool IsChoiceResolved(bool extra)
        {
            return extra ? _offer.ExtraChoiceResolved : _offer.MainChoiceResolved;
        }

        private void OpenDishPack(bool extra, IReadOnlyList<RewardChoice> groupChoices)
        {
            BattleForm battle = BattleForm.Active;
            if (battle == null || groupChoices == null || groupChoices.Count == 0)
            {
                return;
            }

            bool opened = battle.OpenRewardDishPack(
                groupChoices,
                (choiceIndex, bookIndex) => ClaimDishChoiceToBook(extra, choiceIndex, bookIndex, groupChoices),
                ReopenReward);
            if (opened)
            {
                Close();
            }
        }

        private bool ClaimDishChoiceToBook(
            bool extra,
            int choiceIndex,
            int bookIndex,
            IReadOnlyList<RewardChoice> groupChoices)
        {
            if (_offer == null || _run == null || groupChoices == null || IsChoiceResolved(extra)
                || choiceIndex < 0 || choiceIndex >= groupChoices.Count)
            {
                return false;
            }

            RewardChoice choice = groupChoices[choiceIndex];
            if (!RewardGranter.ApplyDishChoiceToBook(_run, choice, bookIndex))
            {
                return false;
            }

            MarkChoiceClaimed(extra, choiceIndex);
            SaveOfferAndReopenReward();
            return true;
        }

        private void SaveOfferAndReopenReward()
        {
            _run.SetPendingRewardOffer(_rewardKey, _offer);
            RunPersistence.Save(_run);

            // 菜品是最后一个奖励且已放入菜谱：直接等效于点「继续」，不再弹回 RewardForm。
            if (TryAutoComplete(closeForm: false))
            {
                return;
            }

            ReopenReward();
        }

        private void ReopenReward()
        {
            GameApp.UI.OpenUIForm(UIForms.Reward, UIForms.GroupDialog);
        }

        private void MarkChoiceClaimed(bool extra, int index)
        {
            if (extra)
            {
                _offer.MarkExtraChoiceClaimed(index);
            }
            else
            {
                _offer.MarkMainChoiceClaimed(index);
            }
        }

        private void RebuildRewardRows()
        {
            for (int i = 0; i < _spawnedRows.Count; i++)
            {
                if (_spawnedRows[i] != null)
                {
                    Destroy(_spawnedRows[i].gameObject);
                }
            }

            _spawnedRows.Clear();

            if (_rewardRowTemplate == null || _rewardListContent == null || _offer == null)
            {
                return;
            }

            _rewardRowTemplate.gameObject.SetActive(false);
            AddFixedGoldRow();

            AddChoiceRows("主奖励", _offer.MainChoices, extra: false);
            AddChoiceRows("额外奖励", _offer.ExtraChoices, extra: true);
        }

        private void AddFixedGoldRow()
        {
            if (_offer.BaseGoldClaimed)
            {
                return;
            }

            RewardChoiceRowView row = CreateRewardRow();
            if (row == null)
            {
                return;
            }

            row.Bind(
                $"金币 +{_offer.BaseGold}",
                "点击领取固定金币。",
                LoadGoldIcon(),
                false,
                true,
                false,
                ClaimBaseGold);
        }

        private void AddChoiceRows(
            string groupName,
            IReadOnlyList<RewardChoice> choices,
            bool extra)
        {
            if (choices == null || choices.Count == 0)
            {
                return;
            }

            int claimedIndex = extra ? _offer.ExtraChoiceIndex : _offer.MainChoiceIndex;
            if (claimedIndex >= 0)
            {
                return;
            }

            if (IsFragmentPack(choices))
            {
                AddFragmentPackRow(groupName, choices, extra);
                return;
            }

            if (IsDishPack(choices))
            {
                AddDishPackRow(groupName, choices, extra);
                return;
            }

            for (int i = 0; i < choices.Count; i++)
            {
                int index = i;
                RewardChoice choice = choices[i];
                RewardChoiceRowView row = CreateRewardRow();
                if (row == null)
                {
                    return;
                }

                row.Bind(
                    BuildChoiceTitle(groupName, choice),
                    BuildChoiceDescription(choice),
                    LoadChoiceIcon(choice),
                    false,
                    true,
                    false,
                    () => ClaimChoice(extra, index, choices));
            }
        }

        private void AddFragmentPackRow(
            string groupName,
            IReadOnlyList<RewardChoice> choices,
            bool extra)
        {
            RewardChoiceRowView row = CreateRewardRow();
            if (row == null)
            {
                return;
            }

            int index = 0;
            row.Bind(
                $"{groupName}：碎片选择包",
                BuildFragmentPackDescription(choices),
                LoadChoiceIcon(choices[index]),
                false,
                true,
                false,
                () => ClaimChoice(extra, index, choices));
        }

        private void AddDishPackRow(
            string groupName,
            IReadOnlyList<RewardChoice> choices,
            bool extra)
        {
            RewardChoiceRowView row = CreateRewardRow();
            if (row == null)
            {
                return;
            }

            int index = 0;
            row.Bind(
                $"{groupName}：菜品选择包",
                $"点击后从 {choices.Count} 个菜品中选择 1 个放入菜谱。",
                LoadChoiceIcon(choices[index]),
                false,
                true,
                false,
                () => ClaimChoice(extra, index, choices));
        }

        private RewardChoiceRowView CreateRewardRow()
        {
            if (_rewardRowTemplate == null || _rewardListContent == null)
            {
                return null;
            }

            RewardChoiceRowView row = Instantiate(_rewardRowTemplate, _rewardListContent);
            row.gameObject.SetActive(true);
            _spawnedRows.Add(row);
            return row;
        }

        private string BuildChoiceTitle(string groupName, RewardChoice choice)
        {
            if (choice == null)
            {
                return groupName;
            }

            string category;
            switch (choice.Kind)
            {
                case cfg.RewardKind.Gold:
                    category = "金币";
                    break;
                case cfg.RewardKind.DishChoice:
                    category = "菜品选择包";
                    break;
                case cfg.RewardKind.FragmentChoice:
                    category = "碎片选择包";
                    break;
                case cfg.RewardKind.PassiveItemChoice:
                    category = "被动道具";
                    break;
                case cfg.RewardKind.ActiveItemGrant:
                    category = "主动道具";
                    break;
                default:
                    category = "奖励";
                    break;
            }

            return string.IsNullOrEmpty(choice.Name)
                ? $"{groupName}：{category}"
                : $"{groupName}：{category} - {choice.Name}";
        }

        private static string BuildChoiceDescription(RewardChoice choice)
        {
            if (choice == null)
            {
                return string.Empty;
            }

            if (choice.Kind == cfg.RewardKind.Gold || choice.IsFallbackGold)
            {
                return $"领取后获得金币 +{choice.GoldAmount}。";
            }

            if (!string.IsNullOrEmpty(choice.Description))
            {
                return choice.Description;
            }

            switch (choice.Kind)
            {
                case cfg.RewardKind.DishChoice:
                    return "加入菜谱池，后续美食挑战中可能抽到。";
                case cfg.RewardKind.FragmentChoice:
                    return "获得胃部碎片包，进入棋盘编辑后选择并拼贴一块。";
                case cfg.RewardKind.PassiveItemChoice:
                    return "获得后持续生效，重复获得时会升级或折算。";
                case cfg.RewardKind.ActiveItemGrant:
                    return "获得一个主动道具，可在战斗中使用。";
                default:
                    return "领取后加入本轮运行。";
            }
        }

        private static bool IsFragmentPack(IReadOnlyList<RewardChoice> choices)
        {
            return choices != null && choices.Count > 0 && choices[0]?.Kind == cfg.RewardKind.FragmentChoice;
        }

        private static bool IsDishPack(IReadOnlyList<RewardChoice> choices)
        {
            return choices != null && choices.Count > 0 && choices[0]?.Kind == cfg.RewardKind.DishChoice;
        }

        private static string BuildFragmentPackDescription(IReadOnlyList<RewardChoice> choices)
        {
            if (choices == null || choices.Count == 0)
            {
                return "点击后进入棋盘编辑。";
            }

            return $"点击后进入棋盘编辑，从 {choices.Count} 个胃部碎片中选择 1 个拼贴。";
        }

        private Sprite LoadChoiceIcon(RewardChoice choice)
        {
            if (choice == null)
            {
                return null;
            }

            switch (choice.Kind)
            {
                case cfg.RewardKind.Gold:
                    return LoadGoldIcon();
                case cfg.RewardKind.DishChoice:
                    return LoadDishIcon(choice.Id);
                case cfg.RewardKind.FragmentChoice:
                    return Resources.Load<Sprite>("Sprites/UI/ui_icon_shop_fragment")
                        ?? Resources.Load<Sprite>("Sprites/UI/card_action_food_fragment");
                case cfg.RewardKind.PassiveItemChoice:
                case cfg.RewardKind.ActiveItemGrant:
                {
                    cfg.Item item = _run?.Tables?.TbItem.GetOrDefault(choice.Id) ?? GameApp.Config.Tables.TbItem.GetOrDefault(choice.Id);
                    Sprite icon = RunItemSlotView.LoadIcon(item);
                    if (icon != null)
                    {
                        return icon;
                    }

                    return Resources.Load<Sprite>(choice.Kind == cfg.RewardKind.ActiveItemGrant
                        ? "Sprites/UI/card_action_food_active"
                        : "Sprites/UI/card_action_food_passive");
                }
                default:
                    return null;
            }
        }

        private Sprite LoadDishIcon(string dishId)
        {
            DishDef dish = _run?.Database.GetDish(dishId);
            if (dish != null)
            {
                if (!string.IsNullOrEmpty(dish.Icon))
                {
                    Sprite icon = Resources.Load<Sprite>(dish.Icon);
                    if (icon != null)
                    {
                        return icon;
                    }
                }

                Sprite fallback = Resources.Load<Sprite>($"Sprites/Items/{dish.BaseId}")
                    ?? Resources.Load<Sprite>($"Sprites/Items/{dish.Id}");
                if (fallback != null)
                {
                    return fallback;
                }
            }

            return Resources.Load<Sprite>("Sprites/UI/ui_icon_shop_food")
                ?? Resources.Load<Sprite>("Sprites/UI/card_action_food_dish");
        }

        private static Sprite LoadGoldIcon()
        {
            return Resources.Load<Sprite>("Sprites/UI/icon_coin")
                ?? Resources.Load<Sprite>("Sprites/UI/card_action_food_gold");
        }

        private static void SetButtonLabel(Button button, string text)
        {
            Text label = button.GetComponentInChildren<Text>();
            if (label != null)
            {
                label.text = text;
            }
        }
    }
}
