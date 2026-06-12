using GourmetProject.Core.Rng;
using GourmetProject.Game.Gameplay;
using GourmetProject.Gameplay.Battle;
using GourmetProject.Runtime;
using GourmetProject.Runtime.UI;
using UnityEngine;

namespace GourmetProject.Game.UI
{
    /// <summary>
    /// 过关领奖界面：展示本周得分与发放的奖励，点「继续」推进到下一周（或通关返回菜单）。
    /// 奖励经 RewardGranter 按周命名流发放，并在推进时存档以支持「继续游戏」。
    /// </summary>
    public sealed class RewardForm : UGuiForm
    {
        private static readonly Color Dim = new(0f, 0f, 0f, 0.82f);
        private static readonly Color Box = new(0.14f, 0.18f, 0.16f, 1f);

        private RectTransform _content;
        private GameRun _run;
        private bool _isFinalWeek;

        protected override void OnInit(object userData)
        {
            base.OnInit(userData);
            UiBuilder.AddImage(CachedTransform, "Dim", Dim, 0f, 0f, 1f, 1f);
        }

        protected override void OnOpen(object userData)
        {
            base.OnOpen(userData);

            if (_content != null)
            {
                Destroy(_content.gameObject);
                _content = null;
            }

            _run = GameRunContext.Current;
            if (_run == null)
            {
                Close();
                return;
            }

            BattleSession session = BattleForm.Active?.Session;
            int total = session != null && session.IsSettled ? session.LastResult.Total : 0;
            int target = session?.RequiredScore ?? _run.RequiredScore;

            IRandomStream rng = GameApp.Random.Stream($"reward_w{_run.WeekIndex}");
            string reward = RewardGranter.Grant(_run, _run.CurrentWeek, rng);
            _isFinalWeek = !_run.HasNextWeek;

            Build(total, target, reward);
        }

        private void Build(int total, int target, string reward)
        {
            _content = UiBuilder.NewRect("Content", CachedTransform);
            UiBuilder.Anchor(_content, 0.24f, 0.26f, 0.76f, 0.74f);
            UiBuilder.AddImage(_content, "Box", Box, 0f, 0f, 1f, 1f);

            string title = _isFinalWeek ? "通关！" : $"第 {_run.WeekIndex} 周 · 过关！";
            UiBuilder.AddText(_content, "Title", title, 38, new Color(1f, 0.85f, 0.4f, 1f), 0.05f, 0.80f, 0.95f, 0.96f);
            UiBuilder.AddText(_content, "Score", $"得分 {total} / 目标 {target}", 26, Color.white, 0.05f, 0.66f, 0.95f, 0.80f);
            UiBuilder.AddText(_content, "Reward", reward, 24, new Color(0.7f, 1f, 0.7f, 1f), 0.05f, 0.46f, 0.95f, 0.64f);
            UiBuilder.AddText(_content, "Gold", $"当前金币 {_run.Gold}", 22, new Color(1f, 0.9f, 0.5f, 1f), 0.05f, 0.34f, 0.95f, 0.46f);

            if (_isFinalWeek)
            {
                // 配置周打完后进入（或继续）无尽模式，或带着存档返回菜单。
                string nextLabel = _run.IsEndless ? "继续挑战" : "进入无尽模式";
                UiBuilder.AddButton(_content, "Endless", nextLabel, new Color(0.9f, 0.55f, 0.2f, 1f),
                    0.08f, 0.08f, 0.48f, 0.22f, OnContinue, 24);
                UiBuilder.AddButton(_content, "Menu", "返回菜单", new Color(0.35f, 0.35f, 0.4f, 1f),
                    0.52f, 0.08f, 0.92f, 0.22f, OnReturnMenu, 24);
            }
            else
            {
                UiBuilder.AddButton(_content, "Continue", "进入下一周", new Color(0.9f, 0.55f, 0.2f, 1f),
                    0.32f, 0.08f, 0.68f, 0.22f, OnContinue, 26);
            }
        }

        private void OnContinue()
        {
            _run.WeekIndex++;
            _run.RequiredScoreOverride = -1;
            RunPersistence.Save(_run);
            Close();
            BattleForm.Active?.BeginWeek();
        }

        private void OnReturnMenu()
        {
            // 保留存档，便于之后从主菜单「继续游戏」回到无尽进度。
            RunPersistence.Save(_run);
            Close();
            GameplayFlowSignal.RequestReturnToMenu();
        }

        private void Close()
        {
            GameApp.UI.CloseUIForm(UIForm);
        }
    }
}
