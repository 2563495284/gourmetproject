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
    /// 固定壳（遮罩/面板/标题/商店与跳过按钮/卡片容器）在 WeekMapForm.prefab，
    /// 事件卡按周命名流确定性抽取后用 WeekEventCardView 子 prefab 数据驱动实例化。
    /// </summary>
    public sealed class WeekMapForm : UGuiForm
    {
        [SerializeField] private Text _titleText;
        [SerializeField] private Button _shopButton;
        [SerializeField] private Button _skipButton;
        [SerializeField] private RectTransform _cardsContainer;
        [SerializeField] private WeekEventCardView _cardPrefab;

        private readonly List<WeekEventCardView> _cards = new();

        protected override void OnInit(object userData)
        {
            base.OnInit(userData);
            _shopButton.onClick.AddListener(OpenShop);
        }

        protected override void OnOpen(object userData)
        {
            base.OnOpen(userData);

            GameRun run = GameRunContext.Current;
            if (run == null)
            {
                Close();
                return;
            }

            Build(run);
        }

        protected override void OnClose(bool isShutdown, object userData)
        {
            ClearCards();
            base.OnClose(isShutdown, userData);
        }

        private void Build(GameRun run)
        {
            ClearCards();

            string weekLabel = run.IsEndless ? $"无尽 第 {run.WeekIndex - run.TotalWeeks} 关" : $"第 {run.WeekIndex} 周";
            _titleText.text = $"{weekLabel} · 选择今日行动";

            List<cfg.GameEvent> events = PickEvents(run);
            bool hasEvents = events.Count > 0;

            // 没有可选事件时隐藏卡片容器、显示「直接开工」。
            _cardsContainer.gameObject.SetActive(hasEvents);
            _skipButton.gameObject.SetActive(!hasEvents);
            _skipButton.onClick.RemoveAllListeners();
            _skipButton.onClick.AddListener(() => Choose(run, null));

            if (!hasEvents)
            {
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
                SpawnCard(run, ev, minX, maxX);
            }
        }

        private void SpawnCard(GameRun run, cfg.GameEvent ev, float minX, float maxX)
        {
            WeekEventCardView card = Instantiate(_cardPrefab, _cardsContainer);
            var rect = (RectTransform)card.transform;
            rect.anchorMin = new Vector2(minX, 0.12f);
            rect.anchorMax = new Vector2(maxX, 0.82f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;

            cfg.GameEvent captured = ev;
            card.Bind(ev, () => Choose(run, captured));
            _cards.Add(card);
        }

        private void ClearCards()
        {
            foreach (WeekEventCardView card in _cards)
            {
                if (card != null)
                {
                    Destroy(card.gameObject);
                }
            }

            _cards.Clear();
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
