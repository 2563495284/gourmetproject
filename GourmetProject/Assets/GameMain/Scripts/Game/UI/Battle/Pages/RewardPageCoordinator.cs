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

        void ShowActionSelection(Action onShown = null);

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

    internal readonly struct RandomizedRewardResume
    {
        public readonly GameplayView ParentView;
        public readonly bool ParentCompleted;
        public readonly Action ParentFinish;

        public RandomizedRewardResume(GameplayView parentView, bool parentCompleted, Action parentFinish)
        {
            ParentView = parentView;
            ParentCompleted = parentCompleted;
            ParentFinish = parentFinish;
        }
    }

    /// <summary>
    /// 一次领奖操作的导航上下文。根返回页只在首次进入领奖页时记录；随机结果页属于当前领奖页，
    /// 不得把父领奖页覆盖为新的返回目标。
    /// </summary>
    internal sealed class RewardOperationSession
    {
        public GameplayView RootReturnView { get; private set; } = GameplayView.None;

        public GameplayView RandomizedParentView { get; private set; } = GameplayView.None;

        public bool RandomizedParentCompleted { get; private set; }

        private Action _deferredParentFinish;

        public void CaptureRoot(GameplayView current)
        {
            if (RootReturnView == GameplayView.None && !IsRewardPage(current))
            {
                RootReturnView = current;
            }
        }

        public void BeginRandomized(GameplayView previous)
        {
            RandomizedParentView = IsRewardPage(previous)
                ? previous
                : GameplayView.None;
            RandomizedParentCompleted = false;
            _deferredParentFinish = null;
        }

        public bool TryDeferParentCompletion(GameplayView page, Action onFinish)
        {
            if (RandomizedParentView != page)
            {
                return false;
            }

            RandomizedParentCompleted = true;
            _deferredParentFinish = onFinish;
            return true;
        }

        public RandomizedRewardResume ConsumeRandomized()
        {
            var resume = new RandomizedRewardResume(
                RandomizedParentView,
                RandomizedParentCompleted,
                _deferredParentFinish);
            ClearRandomized();
            return resume;
        }

        public GameplayView ConsumeRoot()
        {
            GameplayView target = RootReturnView;
            RootReturnView = GameplayView.None;
            return target;
        }

        public void Clear()
        {
            RootReturnView = GameplayView.None;
            ClearRandomized();
        }

        public static bool IsRewardPage(GameplayView view)
        {
            return view == GameplayView.RewardDishPack
                || view == GameplayView.RewardItemChoice
                || view == GameplayView.RandomizedItems;
        }

        private void ClearRandomized()
        {
            RandomizedParentView = GameplayView.None;
            RandomizedParentCompleted = false;
            _deferredParentFinish = null;
        }
    }

    internal sealed class RewardPageCoordinator
    {
        private const string Tag = "Battle";
        private readonly IRewardPageHost _host;
        private readonly RewardOperationSession _session = new RewardOperationSession();

        public RewardPageCoordinator(IRewardPageHost host)
        {
            _host = host;
        }

        public bool OpenRewardDishPack(
            RewardChoiceGroup group,
            IReadOnlyList<RewardChoice> choices,
            Func<int, bool> onChoiceSelected,
            Action onFinish)
        {
            if (_host.Run == null || _host.RewardDishPackPanel == null)
            {
                Log.Error("BattleForm: reward dish pack panel is not configured.", Tag);
                return false;
            }

            CaptureReturnView(_host.CurrentView);
            _host.SwitchTo(GameplayView.RewardDishPack, () =>
            {
                _host.RewardDishPackPanel.Open(
                    _host.Run,
                    group,
                    choices,
                    onChoiceSelected,
                    () => FinishRewardPage(
                        GameplayView.RewardDishPack,
                        _host.RewardDishPackPanel.Close,
                        onFinish),
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

            CaptureReturnView(_host.CurrentView);
            _host.SwitchTo(GameplayView.RewardDishPack, () =>
            {
                _host.RewardDishPackPanel.Open(
                    _host.Run,
                    new RewardChoiceGroup(title, choices),
                    choices,
                    choiceIndex =>
                    {
                        if (choiceIndex < 0
                            || choiceIndex >= choices.Count
                            || !RewardGranter.ApplyDishChoice(_host.Run, choices[choiceIndex]))
                        {
                            return false;
                        }

                        RunPersistence.Save(_host.Run);
                        return true;
                    },
                    () => FinishRewardPage(
                        GameplayView.RewardDishPack,
                        _host.RewardDishPackPanel.Close,
                        null),
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
                    }

                    RunPersistence.Save(_host.Run);
                },
                null);
        }

        public bool OpenRewardItemChoices(
            RewardChoiceGroup group,
            IReadOnlyList<RewardChoice> choices,
            cfg.ItemKind kind,
            Action<int> onPick,
            Action onFinish)
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

            CaptureReturnView(_host.CurrentView);
            _host.SwitchTo(GameplayView.RewardItemChoice, () =>
            {
                _host.RewardItemChoicePanel.Open(
                    group,
                    choices,
                    kind,
                    (sourceCard, index) =>
                    {
                        Action playSelectionFly = null;
                        if (index >= 0 && index < choices.Count)
                        {
                            playSelectionFly = _host.PrepareRewardItemSelectionFly(
                                choices[index],
                                kind,
                                sourceCard);
                        }

                        onPick?.Invoke(index);
                        playSelectionFly?.Invoke();
                    },
                    () =>
                    {
                        FinishRewardPage(
                            GameplayView.RewardItemChoice,
                            _host.RewardItemChoicePanel.Close,
                            onFinish);
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
            _session.BeginRandomized(previous);
            _host.SwitchTo(GameplayView.RandomizedItems, () =>
            {
                _host.RandomizedItemsPanel.Open(
                    string.IsNullOrWhiteSpace(title) ? "随机后的装饰品和消耗品" : title,
                    results,
                    () =>
                    {
                        _host.PlayRandomizedItemFlys(results);
                        CompleteRandomizedItems();
                    });
            });
            return true;
        }

        public void CloseRewardPages(bool restoreReturnView = true)
        {
            GameplayView current = _host.CurrentView;

            if (restoreReturnView && IsRewardPage(current))
            {
                RestoreAfterAcquireView(() =>
                {
                    CloseAllRewardPages();
                    _session.Clear();
                });
                return;
            }

            CloseAllRewardPages();
            _session.Clear();
        }

        private void FinishRewardPage(GameplayView page, Action closePage, Action onFinish)
        {
            if (_host.CurrentView == GameplayView.RandomizedItems
                && _session.TryDeferParentCompletion(page, onFinish))
            {
                return;
            }

            RestoreAfterAcquireView(() =>
            {
                closePage?.Invoke();
                onFinish?.Invoke();
            });
        }

        private void CompleteRandomizedItems()
        {
            RandomizedRewardResume resume = _session.ConsumeRandomized();
            GameplayView parent = resume.ParentView;

            if (IsRewardPage(parent) && !resume.ParentCompleted)
            {
                _host.SwitchTo(parent, onShown: _host.RandomizedItemsPanel.Close);
                return;
            }

            RestoreAfterAcquireView(() =>
            {
                _host.RandomizedItemsPanel.Close();
                CloseRewardPage(parent);
                resume.ParentFinish?.Invoke();
            });
        }

        private void CaptureReturnView(GameplayView view)
        {
            _session.CaptureRoot(view);
        }

        private static bool IsRewardPage(GameplayView view)
        {
            return RewardOperationSession.IsRewardPage(view);
        }

        private void RestoreAfterAcquireView(Action onRestored = null)
        {
            _host.RefreshPersistent();
            GameplayView target = _session.ConsumeRoot();
            switch (target)
            {
                case GameplayView.ActionSelect:
                    _host.ShowActionSelection(onRestored);
                    break;
                case GameplayView.Shop:
                    _host.SwitchTo(GameplayView.Shop, onShown: onRestored);
                    break;
                case GameplayView.RecipeSelection:
                    _host.SwitchTo(GameplayView.RecipeSelection, onShown: onRestored);
                    break;
                case GameplayView.Event:
                case GameplayView.RecipeInspect:
                case GameplayView.TableEdit:
                case GameplayView.TableView:
                    _host.SwitchTo(target, onShown: onRestored);
                    break;
                case GameplayView.Food:
                    _host.SwitchTo(GameplayView.Food, _host.RestoreBattleWorld, onRestored);
                    break;
                default:
                    _host.SwitchTo(GameplayView.Shop, onShown: onRestored);
                    break;
            }
        }

        private void CloseRewardPage(GameplayView page)
        {
            switch (page)
            {
                case GameplayView.RewardDishPack:
                    _host.RewardDishPackPanel?.Close();
                    break;
                case GameplayView.RewardItemChoice:
                    _host.RewardItemChoicePanel?.Close();
                    break;
                case GameplayView.RandomizedItems:
                    _host.RandomizedItemsPanel?.Close();
                    break;
            }
        }

        private void CloseAllRewardPages()
        {
            _host.RewardDishPackPanel?.Close();
            _host.RewardItemChoicePanel?.Close();
            _host.RandomizedItemsPanel?.Close();
        }
    }
}
