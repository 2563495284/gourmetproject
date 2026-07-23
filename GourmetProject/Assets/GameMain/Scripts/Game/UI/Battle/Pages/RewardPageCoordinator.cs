using System;
using System.Collections.Generic;
using GourmetProject.Core.Diagnostics;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;
using GourmetProject.Game.UI.Meta;
using GourmetProject.Game.UI.Tooltips;
using GourmetProject.Runtime;
using UnityEngine;

namespace GourmetProject.Game.UI.Battle.Pages
{
    internal interface IRewardPageHost
    {
        GameRun Run { get; }

        GameplayView CurrentView { get; }

        RectTransform CenterTransform { get; }

        RewardDishPackPanel RewardDishPackPanel { get; }

        RewardItemChoicePanel RewardItemChoicePanel { get; set; }

        RewardItemChoicePanel RewardItemChoicePanelPrefab { get; }

        RandomizedItemsPanel RandomizedItemsPanel { get; }

        void SwitchTo(GameplayView view, Action buildCenter = null, Action onShown = null);

        void SetCenterTitle(string text);

        void RefreshPersistent();

        void ShowActionSelection();

        FoodTipsView FoodTips();

        ItemTipView ItemTips();

        void PlayRandomizedItemFlys(IReadOnlyList<RandomizedItemResult> results);
    }

    internal sealed class RewardPageCoordinator
    {
        private const string Tag = "Battle";
        private readonly IRewardPageHost _host;
        private GameplayView _returnView = GameplayView.None;

        public RewardPageCoordinator(IRewardPageHost host)
        {
            _host = host;
        }

        public bool OpenRewardDishPack(
            IReadOnlyList<RewardChoice> choices,
            Func<int, int, bool> onChoiceDropped,
            Action onSkip)
        {
            if (_host.Run == null || _host.RewardDishPackPanel == null)
            {
                Log.Error("BattleForm: reward dish pack panel is not configured.", Tag);
                return false;
            }

            CaptureReturnView();
            _host.SwitchTo(GameplayView.RewardDishPack, () =>
            {
                _host.SetCenterTitle("菜品包");
                _host.RewardDishPackPanel.Open(
                    _host.Run,
                    choices,
                    onChoiceDropped,
                    onSkip,
                    _host.FoodTips);
            });
            return true;
        }

        public bool OpenAcquireDishPack(string title, IReadOnlyList<RewardChoice> choices)
        {
            if (_host.Run == null || _host.RewardDishPackPanel == null || choices == null || choices.Count == 0)
            {
                return false;
            }

            GameplayView previous = _host.CurrentView;
            CaptureReturnView(previous);
            _host.SwitchTo(GameplayView.RewardDishPack, () =>
            {
                _host.SetCenterTitle(string.IsNullOrWhiteSpace(title) ? "菜品包" : title);
                _host.RewardDishPackPanel.Open(
                    _host.Run,
                    choices,
                    (choiceIndex, bookIndex) =>
                    {
                        if (choiceIndex < 0 || choiceIndex >= choices.Count)
                        {
                            return false;
                        }

                        if (!RewardGranter.ApplyDishChoice(_host.Run, choices[choiceIndex]))
                        {
                            return false;
                        }

                        RunPersistence.Save(_host.Run);
                        RestoreAfterAcquireView(previous);
                        return true;
                    },
                    () => RestoreAfterAcquireView(previous),
                    _host.FoodTips);
            });
            return true;
        }

        public bool OpenRewardItemChoices(string title, IReadOnlyList<RewardChoice> choices, cfg.ItemKind kind)
        {
            return OpenRewardItemChoices(
                title,
                choices,
                kind,
                index =>
                {
                    if (index >= 0 && index < choices.Count)
                    {
                        RewardGranter.ApplyChoice(_host.Run, choices[index]);
                        RunPersistence.Save(_host.Run);
                    }
                },
                null);
        }

        public bool OpenRewardItemChoices(
            string title,
            IReadOnlyList<RewardChoice> choices,
            cfg.ItemKind kind,
            Action<int> onPick,
            Action onSkip)
        {
            if (_host.Run == null || choices == null || choices.Count == 0)
            {
                return false;
            }

            EnsureRewardItemChoicePanel();
            if (_host.RewardItemChoicePanel == null)
            {
                return false;
            }

            GameplayView previous = _host.CurrentView;
            CaptureReturnView(previous);
            _host.SwitchTo(GameplayView.RewardItemChoice, () =>
            {
                _host.SetCenterTitle(string.Empty);
                _host.RewardItemChoicePanel.Open(
                    string.IsNullOrWhiteSpace(title) ? "选择一个道具" : title,
                    choices,
                    kind,
                    index =>
                    {
                        _host.RewardItemChoicePanel.Close();
                        RestoreAfterAcquireView(previous);
                        onPick?.Invoke(index);
                    },
                    () =>
                    {
                        _host.RewardItemChoicePanel.Close();
                        RestoreAfterAcquireView(previous);
                        onSkip?.Invoke();
                    },
                    _host.Run,
                    _host.ItemTips());
            });
            return true;
        }

        public bool OpenRandomizedItemsPanel(string title, IReadOnlyList<RandomizedItemResult> results)
        {
            if (_host.Run == null || results == null)
            {
                return false;
            }

            if (_host.RandomizedItemsPanel == null)
            {
                Log.Error("BattleForm: randomized items panel is not configured.", Tag);
                return false;
            }

            GameplayView previous = _host.CurrentView;
            CaptureReturnView(previous);
            _host.SwitchTo(GameplayView.RandomizedItems, () =>
            {
                _host.SetCenterTitle(string.Empty);
                _host.RandomizedItemsPanel.Open(
                    string.IsNullOrWhiteSpace(title) ? "随机后的道具" : title,
                    results,
                    () =>
                    {
                        _host.PlayRandomizedItemFlys(results);
                        _host.RandomizedItemsPanel.Close();
                        RestoreAfterAcquireView(previous);
                    });
            });
            return true;
        }

        public void CloseRewardPages(bool restoreReturnView = true)
        {
            _host.RewardDishPackPanel?.Close();
            _host.RewardItemChoicePanel?.Close();
            _host.RandomizedItemsPanel?.Close();

            if (!restoreReturnView || !IsRewardPage(_host.CurrentView))
            {
                return;
            }

            GameplayView target = _returnView;
            _returnView = GameplayView.None;
            switch (target)
            {
                case GameplayView.ActionSelect:
                    _host.ShowActionSelection();
                    break;
                case GameplayView.Shop:
                case GameplayView.RecipeSelection:
                case GameplayView.Food:
                case GameplayView.TableEdit:
                case GameplayView.TableView:
                    _host.SwitchTo(target);
                    break;
                default:
                    _host.SwitchTo(GameplayView.Shop);
                    break;
            }
        }

        private void CaptureReturnView()
        {
            CaptureReturnView(_host.CurrentView);
        }

        private void CaptureReturnView(GameplayView view)
        {
            if (!IsRewardPage(view))
            {
                _returnView = view;
            }
        }

        private static bool IsRewardPage(GameplayView view)
        {
            return view == GameplayView.RewardDishPack
                || view == GameplayView.RewardItemChoice
                || view == GameplayView.RandomizedItems;
        }

        private void EnsureRewardItemChoicePanel()
        {
            if (_host.RewardItemChoicePanel != null || _host.CenterTransform == null)
            {
                return;
            }

            if (_host.RewardItemChoicePanelPrefab == null)
            {
                Debug.LogError("BattleForm 缺少奖励道具选择面板 prefab。");
                return;
            }

            RewardItemChoicePanel panel = UnityEngine.Object.Instantiate(
                _host.RewardItemChoicePanelPrefab,
                _host.CenterTransform);
            RectTransform rect = panel.transform as RectTransform;
            if (rect != null)
            {
                rect.anchorMin = new Vector2(0.18f, 0.03f);
                rect.anchorMax = new Vector2(0.82f, 0.9f);
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
                rect.anchoredPosition = Vector2.zero;
                rect.localScale = Vector3.one;
            }

            panel.gameObject.SetActive(false);
            _host.RewardItemChoicePanel = panel;
        }

        private void RestoreAfterAcquireView(GameplayView previous)
        {
            _host.RefreshPersistent();
            switch (previous)
            {
                case GameplayView.ActionSelect:
                    _host.ShowActionSelection();
                    break;
                case GameplayView.Shop:
                    _host.SwitchTo(GameplayView.Shop);
                    break;
                case GameplayView.RecipeSelection:
                    _host.SwitchTo(GameplayView.RecipeSelection);
                    break;
                case GameplayView.Food:
                    _host.SwitchTo(GameplayView.Food);
                    break;
                default:
                    _host.SwitchTo(GameplayView.Shop);
                    break;
            }
        }
    }
}
