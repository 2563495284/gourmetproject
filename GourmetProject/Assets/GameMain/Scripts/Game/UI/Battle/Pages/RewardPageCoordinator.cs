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

        RewardDishPackPanel RewardDishPackPanel { get; }

        RewardItemChoicePanel RewardItemChoicePanel { get; }

        RandomizedItemsPanel RandomizedItemsPanel { get; }

        void SwitchTo(GameplayView view, Action buildCenter = null, Action onShown = null);

        void RefreshPersistent();

        void ShowActionSelection();

        void RestoreBattleWorld();

        FoodTipsView FoodTips();

        ItemTipView ItemTips();

        void PlayRewardDishSelectionFly(RewardDishChoiceCardView sourceCard);

        Action PrepareRewardItemSelectionFly(
            RewardChoice choice,
            cfg.ItemKind kind,
            RewardItemChoiceCardView sourceCard);

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
            RewardChoiceGroup group,
            IReadOnlyList<RewardChoice> choices,
            Func<int, bool> onChoiceSelected,
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
                _host.RewardDishPackPanel.Open(
                    _host.Run,
                    group,
                    choices,
                    onChoiceSelected,
                    onSkip,
                    _host.FoodTips,
                    _host.PlayRewardDishSelectionFly);
                _host.RefreshPersistent();
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
                _host.RewardDishPackPanel.Open(
                    _host.Run,
                    new RewardChoiceGroup(title, choices),
                    choices,
                    choiceIndex =>
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
                    _host.FoodTips,
                    _host.PlayRewardDishSelectionFly);
                _host.RefreshPersistent();
            });
            return true;
        }

        public bool OpenRewardItemChoices(string title, IReadOnlyList<RewardChoice> choices, cfg.ItemKind kind)
        {
            return OpenRewardItemChoices(
                new RewardChoiceGroup(title, choices),
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
            RewardChoiceGroup group,
            IReadOnlyList<RewardChoice> choices,
            cfg.ItemKind kind,
            Action<int> onPick,
            Action onSkip)
        {
            if (_host.Run == null || choices == null || choices.Count == 0)
            {
                return false;
            }

            if (_host.RewardItemChoicePanel == null)
            {
                Log.Error("BattleForm: reward item choice panel is not configured.", Tag);
                return false;
            }

            GameplayView previous = _host.CurrentView;
            CaptureReturnView(previous);
            _host.SwitchTo(GameplayView.RewardItemChoice, () =>
            {
                _host.RewardItemChoicePanel.Open(
                    group,
                    choices,
                    kind,
                    (sourceCard, index) =>
                    {
                        Action playSelectionFly =
                            index >= 0 && index < choices.Count
                                ? _host.PrepareRewardItemSelectionFly(choices[index], kind, sourceCard)
                                : null;
                        _host.RewardItemChoicePanel.Close();
                        RestoreAfterAcquireView(previous);
                        onPick?.Invoke(index);
                        playSelectionFly?.Invoke();
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
                _host.RandomizedItemsPanel.Open(
                    string.IsNullOrWhiteSpace(title) ? "随机后的装饰品和消耗品" : title,
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
                case GameplayView.TableEdit:
                case GameplayView.TableView:
                    _host.SwitchTo(target);
                    break;
                case GameplayView.Food:
                    _host.SwitchTo(GameplayView.Food, _host.RestoreBattleWorld);
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
                    _host.SwitchTo(GameplayView.Food, _host.RestoreBattleWorld);
                    break;
                default:
                    _host.SwitchTo(GameplayView.Shop);
                    break;
            }
        }
    }
}
