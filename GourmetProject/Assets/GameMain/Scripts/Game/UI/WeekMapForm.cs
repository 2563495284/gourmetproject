using System.Collections.Generic;
using GourmetProject.Core.Rng;
using GourmetProject.Game.Gameplay;
using GourmetProject.Runtime;
using GourmetProject.Runtime.UI;
using UnityEngine;
using UnityEngine.UI;

namespace GourmetProject.Game.UI
{
    /// <summary>
    /// 周循环「三选一」事件界面：每周开局弹出，玩家选择一个事件，结算其效果后进入本周对局。
    /// 事件从 TbEvent 按周命名流确定性抽取，全部代码构建。
    /// </summary>
    public sealed class WeekMapForm : UGuiForm
    {
        private static readonly Color Dim = new(0f, 0f, 0f, 0.78f);
        private static readonly Color Box = new(0.16f, 0.16f, 0.2f, 1f);
        private static readonly Color CardColor = new(0.22f, 0.26f, 0.34f, 1f);

        private RectTransform _content;

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

            GameRun run = GameRunContext.Current;
            if (run == null)
            {
                Close();
                return;
            }

            Build(run);
        }

        private void Build(GameRun run)
        {
            _content = UiBuilder.NewRect("Content", CachedTransform);
            UiBuilder.Anchor(_content, 0.12f, 0.20f, 0.88f, 0.82f);
            UiBuilder.AddImage(_content, "Box", Box, 0f, 0f, 1f, 1f);

            string weekLabel = run.IsEndless ? $"无尽 第 {run.WeekIndex - run.TotalWeeks} 关" : $"第 {run.WeekIndex} 周";
            UiBuilder.AddText(_content, "Title", $"{weekLabel} · 选择今日行动", 34, Color.white,
                0.05f, 0.86f, 0.72f, 0.98f);

            // 商店为可选系统事件，不消耗当日行动，逛完回到本界面继续选事件。
            UiBuilder.AddButton(_content, "Shop", "前往商店", new Color(0.7f, 0.5f, 0.2f, 1f),
                0.74f, 0.87f, 0.96f, 0.97f, OpenShop, 20);

            List<cfg.GameEvent> events = PickEvents(run);
            if (events.Count == 0)
            {
                // 没有可选事件时直接进入对局。
                UiBuilder.AddButton(_content, "Skip", "直接开工", new Color(0.3f, 0.5f, 0.35f, 1f),
                    0.38f, 0.1f, 0.62f, 0.22f, () => Choose(run, null), 26);
                return;
            }

            int n = events.Count;
            float gap = 0.03f;
            float cardW = (1f - gap * (n + 1)) / n;
            for (int i = 0; i < n; i++)
            {
                cfg.GameEvent ev = events[i];
                float minX = gap + i * (cardW + gap);
                float maxX = minX + cardW;
                BuildCard(run, ev, minX, maxX);
            }
        }

        private void BuildCard(GameRun run, cfg.GameEvent ev, float minX, float maxX)
        {
            RectTransform card = UiBuilder.NewRect($"Card_{ev.Id}", _content);
            UiBuilder.Anchor(card, minX, 0.12f, maxX, 0.82f);
            UiBuilder.AddImage(card, "Bg", CardColor, 0f, 0f, 1f, 1f, 6f);

            UiBuilder.AddText(card, "Name", ev.Name, 26, Color.white, 0.06f, 0.82f, 0.94f, 0.96f);
            UiBuilder.AddText(card, "Desc", ev.Desc, 19, new Color(0.9f, 0.9f, 0.9f, 1f),
                0.08f, 0.30f, 0.92f, 0.80f, TextAnchor.UpperLeft);
            UiBuilder.AddText(card, "Time", $"耗时 {ev.TimeCost}", 17, new Color(1f, 0.8f, 0.4f, 1f),
                0.08f, 0.20f, 0.92f, 0.30f, TextAnchor.MiddleLeft);

            cfg.GameEvent captured = ev;
            UiBuilder.AddButton(card, "Pick", "选择", new Color(0.9f, 0.55f, 0.2f, 1f),
                0.12f, 0.04f, 0.88f, 0.16f, () => Choose(run, captured), 22);
        }

        private List<cfg.GameEvent> PickEvents(GameRun run)
        {
            var pool = new List<cfg.GameEvent>(GameApp.Config.Tables.TbEvent.DataList);
            IRandomStream rng = GameApp.Random.Stream($"event_w{run.WeekIndex}");
            rng.Shuffle(pool);

            int take = Mathf.Min(3, pool.Count);
            return pool.GetRange(0, take);
        }

        private void Choose(GameRun run, cfg.GameEvent ev)
        {
            string feedback = string.Empty;
            if (ev != null)
            {
                IRandomStream rng = GameApp.Random.Stream($"event_resolve_w{run.WeekIndex}");
                feedback = WeekEventResolver.Apply(run, ev, rng);
            }

            Close();
            BattleForm.Active?.BeginBattleAfterEvent(feedback);
        }

        private void OpenShop()
        {
            GameApp.UI.OpenUIForm(UIForms.Shop, UIForms.GroupDialog);
        }

        private void Close()
        {
            GameApp.UI.CloseUIForm(UIForm);
        }
    }
}
