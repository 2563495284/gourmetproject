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
    internal enum RewardSubflowKind
    {
        DishPack,
        ItemChoice,
        RandomizedItems,
    }

    public enum RewardSubflowLifecycle
    {
        PreparingChild,
        ChildReady,
        PreparingReturn,
        ParentRestored,
    }

    internal interface IRewardPageHost
    {
        GameRun Run { get; }

        RewardDishPackPanel RewardDishPackPanel { get; }

        RewardItemChoicePanel RewardItemChoicePanel { get; }

        RandomizedItemsPanel RandomizedItemsPanel { get; }

        void PrepareRewardSubflowLayer();

        void ShowRewardSubflowPanel(Component panel);

        void HideRewardSubflowPanel(Component panel);

        void HideRewardSubflowLayer();

        void NotifyRewardSubflowLifecycle(RewardSubflowLifecycle lifecycle);

        void RefreshPersistent();

        FoodTipsView FoodTips();

        ItemTipView ItemTips();

        void PlayRewardDishSelectionFly(RewardDishChoiceCardView sourceCard);

        Action PrepareRewardItemSelectionFly(
            RewardChoice choice,
            cfg.ItemKind kind,
            RewardItemChoiceCardView sourceCard);

        void PlayRandomizedItemFlys(IReadOnlyList<RandomizedItemResult> results);
    }

    /// <summary>
    /// Battle 内部领奖栈。根来源页不进入栈，也不会被 SwitchTo/SetActive；栈只拥有临时领奖页。
    /// 随机结果可以压在选择页之上，父选择页只软隐藏，直到子页返回后才恢复或完成。
    /// </summary>
    internal sealed class RewardPageCoordinator
    {
        private const string Tag = "Battle";

        private sealed class Frame
        {
            public RewardSubflowKind Kind;
            public Component Panel;
            public Action Close;
            public Action Finish;
            public bool Completed;
            public bool FinishInvoked;
        }

        private readonly IRewardPageHost _host;
        private readonly List<Frame> _stack = new List<Frame>();
        private Action _onIdle;
        private bool _returning;
        private bool _inspectionSuspended;

        public RewardPageCoordinator(IRewardPageHost host)
        {
            _host = host;
        }

        public bool IsActive => _stack.Count > 0;

        internal int Depth => _stack.Count;

        internal void WhenIdle(Action onIdle)
        {
            if (onIdle == null)
            {
                return;
            }

            if (!IsActive)
            {
                onIdle();
                return;
            }

            _onIdle += onIdle;
        }

        internal bool IsInspectionSuspended => _inspectionSuspended;

        internal bool SuspendForInspection()
        {
            if (_inspectionSuspended || _returning || _stack.Count == 0)
            {
                return false;
            }

            _inspectionSuspended = true;
            _host.HideRewardSubflowLayer();
            return true;
        }

        internal void ResumeFromInspection()
        {
            if (!_inspectionSuspended)
            {
                return;
            }

            _inspectionSuspended = false;
            if (_returning || _stack.Count == 0)
            {
                return;
            }

            Frame top = _stack[_stack.Count - 1];
            _host.PrepareRewardSubflowLayer();
            _host.ShowRewardSubflowPanel(top.Panel);
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
                InvokeOnce(onFinish);
                return false;
            }

            if (choices == null || choices.Count == 0)
            {
                InvokeOnce(onFinish);
                return false;
            }

            Frame frame = CreateFrame(
                RewardSubflowKind.DishPack,
                _host.RewardDishPackPanel,
                _host.RewardDishPackPanel.Close,
                onFinish);
            if (!TryPush(frame))
            {
                return false;
            }

            _host.RewardDishPackPanel.Open(
                _host.Run,
                group,
                choices,
                onChoiceSelected,
                () => Complete(frame),
                _host.FoodTips,
                _host.PlayRewardDishSelectionFly);
            ChildReady(frame);
            _host.RefreshPersistent();
            return true;
        }

        public bool OpenAcquireDishPack(string title, IReadOnlyList<RewardChoice> choices)
        {
            if (_host.Run == null || choices == null || choices.Count == 0)
            {
                return false;
            }

            return OpenRewardDishPack(
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
                null);
        }

        public bool OpenRewardItemChoices(string title, IReadOnlyList<RewardChoice> choices, cfg.ItemKind kind)
        {
            return OpenRewardItemChoices(
                new RewardChoiceGroup(title, choices),
                choices,
                kind,
                index =>
                {
                    if (index < 0 || index >= choices.Count
                        || !RewardGranter.TryClaimChoice(_host.Run, choices[index], out _))
                    {
                        return false;
                    }

                    RunPersistence.Save(_host.Run);
                    return true;
                },
                null);
        }

        public bool OpenRewardItemChoices(
            RewardChoiceGroup group,
            IReadOnlyList<RewardChoice> choices,
            cfg.ItemKind kind,
            Func<int, bool> onPick,
            Action onFinish)
        {
            if (_host.Run == null || choices == null || choices.Count == 0)
            {
                InvokeOnce(onFinish);
                return false;
            }

            if (_host.RewardItemChoicePanel == null)
            {
                Log.Error("BattleForm: reward item choice panel is not configured.", Tag);
                InvokeOnce(onFinish);
                return false;
            }

            Frame frame = CreateFrame(
                RewardSubflowKind.ItemChoice,
                _host.RewardItemChoicePanel,
                _host.RewardItemChoicePanel.Close,
                onFinish);
            if (!TryPush(frame))
            {
                return false;
            }

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

                    if (onPick?.Invoke(index) != true)
                    {
                        return false;
                    }

                    _host.RefreshPersistent();
                    playSelectionFly?.Invoke();
                    return true;
                },
                () => Complete(frame),
                _host.Run,
                _host.ItemTips());
            ChildReady(frame);
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

            Frame frame = CreateFrame(
                RewardSubflowKind.RandomizedItems,
                _host.RandomizedItemsPanel,
                _host.RandomizedItemsPanel.Close,
                null);
            if (!TryPush(frame))
            {
                return false;
            }

            _host.RandomizedItemsPanel.Open(
                string.IsNullOrWhiteSpace(title) ? "随机后的装饰品和消耗品" : title,
                results,
                () =>
                {
                    _host.PlayRandomizedItemFlys(results);
                    Complete(frame);
                });
            _host.RefreshPersistent();
            ChildReady(frame);
            return true;
        }

        public void CloseRewardPages()
        {
            if (_returning)
            {
                return;
            }

            _inspectionSuspended = false;
            for (int i = _stack.Count - 1; i >= 0; i--)
            {
                CloseFrame(_stack[i]);
            }

            _stack.Clear();
            _host.HideRewardSubflowLayer();
            NotifyIdle();
        }

        private bool TryPush(Frame frame)
        {
            if (frame == null || _returning)
            {
                return false;
            }

            if (_stack.Count > 0)
            {
                Frame parent = _stack[_stack.Count - 1];
                if (parent.Kind == frame.Kind || frame.Kind != RewardSubflowKind.RandomizedItems)
                {
                    return false;
                }
            }

            _host.NotifyRewardSubflowLifecycle(RewardSubflowLifecycle.PreparingChild);
            if (!_inspectionSuspended)
            {
                _host.PrepareRewardSubflowLayer();
            }
            if (_stack.Count > 0)
            {
                _host.HideRewardSubflowPanel(_stack[_stack.Count - 1].Panel);
            }

            _stack.Add(frame);
            return true;
        }

        private void ChildReady(Frame frame)
        {
            if (!_stack.Contains(frame))
            {
                return;
            }

            if (!_inspectionSuspended)
            {
                _host.ShowRewardSubflowPanel(frame.Panel);
            }
            _host.NotifyRewardSubflowLifecycle(RewardSubflowLifecycle.ChildReady);
        }

        private void Complete(Frame frame)
        {
            if (frame == null || frame.Completed)
            {
                return;
            }

            frame.Completed = true;
            int index = _stack.IndexOf(frame);
            if (index < 0 || index != _stack.Count - 1)
            {
                // 父选择页在 onPick 中触发了随机结果页。父页完成只记账，等子页退出时再续接。
                return;
            }

            ReturnFromTop();
        }

        private void ReturnFromTop()
        {
            if (_stack.Count == 0 || _returning)
            {
                return;
            }

            _returning = true;
            _host.NotifyRewardSubflowLifecycle(RewardSubflowLifecycle.PreparingReturn);
            _host.RefreshPersistent();

            Frame child = _stack[_stack.Count - 1];
            _stack.RemoveAt(_stack.Count - 1);

            if (_stack.Count > 0 && !_stack[_stack.Count - 1].Completed)
            {
                Frame parent = _stack[_stack.Count - 1];
                // 先恢复父页的可见和输入，再销毁子页，避免同一帧两层都不可见。
                if (!_inspectionSuspended)
                {
                    _host.ShowRewardSubflowPanel(parent.Panel);
                }
                CloseFrame(child);
                _host.NotifyRewardSubflowLifecycle(RewardSubflowLifecycle.ParentRestored);
                _returning = false;
                return;
            }

            // 根页或已经完成的父页：覆盖层仍保持可见，先让来源页/RewardForm 同步恢复，
            // 再关闭临时面板并撤掉覆盖层。
            Frame completion = child;
            while (_stack.Count > 0 && _stack[_stack.Count - 1].Completed)
            {
                completion = _stack[_stack.Count - 1];
                _stack.RemoveAt(_stack.Count - 1);
            }

            InvokeFinish(completion);
            CloseFrame(child);
            if (!ReferenceEquals(completion, child))
            {
                CloseFrame(completion);
            }

            for (int i = _stack.Count - 1; i >= 0; i--)
            {
                CloseFrame(_stack[i]);
            }

            _stack.Clear();
            _inspectionSuspended = false;
            _host.HideRewardSubflowLayer();
            _host.NotifyRewardSubflowLifecycle(RewardSubflowLifecycle.ParentRestored);
            _returning = false;
            NotifyIdle();
        }

        private void NotifyIdle()
        {
            if (IsActive)
            {
                return;
            }

            Action idle = _onIdle;
            _onIdle = null;
            idle?.Invoke();
        }

        private static Frame CreateFrame(
            RewardSubflowKind kind,
            Component panel,
            Action close,
            Action finish)
        {
            return new Frame
            {
                Kind = kind,
                Panel = panel,
                Close = close,
                Finish = finish,
            };
        }

        private static void InvokeOnce(Action callback)
        {
            callback?.Invoke();
        }

        private static void InvokeFinish(Frame frame)
        {
            if (frame == null || frame.FinishInvoked)
            {
                return;
            }

            frame.FinishInvoked = true;
            frame.Finish?.Invoke();
        }

        private static void CloseFrame(Frame frame)
        {
            if (frame == null)
            {
                return;
            }

            Action close = frame.Close;
            frame.Close = null;
            close?.Invoke();
        }
    }
}
