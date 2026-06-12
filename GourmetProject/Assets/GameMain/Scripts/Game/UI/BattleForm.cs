using System.Collections.Generic;
using GourmetProject.Game.Gameplay;
using GourmetProject.Gameplay.Battle;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Model;
using GourmetProject.Gameplay.Scoring;
using GourmetProject.Runtime;
using GourmetProject.Runtime.UI;
using UnityEngine;
using UnityEngine.UI;
using GpBoard = GourmetProject.Gameplay.Board.Board;
using Log = GourmetProject.Core.Diagnostics.Log;

namespace GourmetProject.Game.UI
{
    /// <summary>
    /// 局内战斗界面（对应原型图 image1）：后厨菜谱上菜、4x4 棋盘摆盘、「吃!」结算分数过关/失败。
    /// 全部子控件由代码构建，prefab 仅需一个挂本脚本的根节点。
    /// </summary>
    public sealed class BattleForm : UGuiForm
    {
        private const string Tag = "Battle";
        private static readonly Color Bg = new(0.12f, 0.12f, 0.15f, 0.96f);
        private static readonly Color Panel = new(1f, 1f, 1f, 0.08f);
        private static readonly Color CellEmpty = new(1f, 1f, 1f, 0.10f);
        private static readonly Color CellBlocked = new(0f, 0f, 0f, 0.55f);
        private static readonly Color Accent = new(1f, 0.6f, 0.16f, 1f);

        /// <summary>当前打开的战斗界面，供事件/奖励弹窗回调推进周循环。</summary>
        public static BattleForm Active { get; private set; }

        private GameRun _run;
        private BattleSession _session;

        public GameRun Run => _run;
        public BattleSession Session => _session;

        private Text _scoreText;
        private Text _weekText;
        private Text _messageText;
        private Text _itemsText;
        private readonly List<Button> _recipeButtons = new();
        private Image[,] _cells;
        private Text[,] _cellLabels;
        private Button _eatButton;

        private RectTransform _activeItemsRoot;
        private readonly List<Button> _activeItemButtons = new();
        private readonly HashSet<string> _usedActiveItems = new();

        private RectTransform _resultPanel;
        private Text _resultText;
        private Button _resultButton;

        protected override void OnInit(object userData)
        {
            base.OnInit(userData);
            BuildLayout();
        }

        protected override void OnOpen(object userData)
        {
            base.OnOpen(userData);

            _run = GameRunContext.Current;
            if (_run == null)
            {
                Log.Error("BattleForm opened without an active run.", Tag);
                return;
            }

            Active = this;
            BeginWeek();
        }

        protected override void OnClose(bool isShutdown, object userData)
        {
            if (Active == this)
            {
                Active = null;
            }

            base.OnClose(isShutdown, userData);
        }

        /// <summary>进入新一周：先弹三选一事件，事件结算后才构建本周对局。</summary>
        public void BeginWeek()
        {
            _run.RequiredScoreOverride = -1;
            _session = null;
            _usedActiveItems.Clear();
            HideResult();
            RefreshAll();
            RunPersistence.Save(_run);
            SetMessage($"第 {_run.WeekIndex} 周 · 选择今天的行动");
            GameApp.UI.OpenUIForm(UIForms.WeekMap, UIForms.GroupDialog);
        }

        /// <summary>事件结算完成后构建对局并允许操作。</summary>
        public void BeginBattleAfterEvent(string eventFeedback)
        {
            _session = _run.BuildBattleSession();
            _usedActiveItems.Clear();
            HideResult();
            RebuildActiveItems();
            RefreshAll();
            string head = string.IsNullOrEmpty(eventFeedback) ? string.Empty : eventFeedback + " ";
            SetMessage($"{head}点击菜谱上菜，凑够分数后点「吃!」");
        }

        // —— 布局构建 ——

        private void BuildLayout()
        {
            AddImage("Background", Bg, 0f, 0f, 1f, 1f);

            // 顶部：分数 + 要求分 + 周。
            UiBuilder.AddImage(CachedTransform, "ScorePanel", Panel, 0.34f, 0.86f, 0.66f, 0.98f, 6f);
            _scoreText = UiBuilder.AddText(CachedTransform, "ScoreText", "0", 52, Color.white, 0.34f, 0.90f, 0.66f, 0.99f);
            _weekText = UiBuilder.AddText(CachedTransform, "WeekText", "", 24, new Color(1f, 1f, 1f, 0.8f), 0.34f, 0.855f, 0.66f, 0.90f);

            // 顶部右：3 个 Buff 槽（占位）。
            UiBuilder.AddText(CachedTransform, "BuffTitle", "Buff", 18, new Color(1f, 1f, 1f, 0.7f), 0.68f, 0.92f, 0.74f, 0.97f);
            for (int i = 0; i < 3; i++)
            {
                float minX = 0.74f + i * 0.055f;
                UiBuilder.AddImage(CachedTransform, $"BuffSlot{i}", new Color(1f, 1f, 1f, 0.12f), minX, 0.90f, minX + 0.05f, 0.98f, 3f);
            }

            // 左上：总览（放弃本局回菜单）。
            UiBuilder.AddButton(CachedTransform, "OverviewButton", "总览", new Color(0.3f, 0.3f, 0.35f, 1f),
                0.02f, 0.88f, 0.10f, 0.97f, OnOverviewClicked, 22);

            // 左侧：后厨（菜谱1/2 + 上菜）。
            UiBuilder.AddImage(CachedTransform, "KitchenPanel", Panel, 0.03f, 0.40f, 0.22f, 0.80f, 4f);
            UiBuilder.AddText(CachedTransform, "KitchenTitle", "后厨", 26, Color.white, 0.03f, 0.74f, 0.22f, 0.80f);
            for (int i = 0; i < GameRun.RecipeSlotCount; i++)
            {
                int index = i;
                float top = 0.71f - i * 0.16f;
                Button button = UiBuilder.AddButton(CachedTransform, $"Recipe{i}", $"菜谱{i + 1}", new Color(0.85f, 0.7f, 0.4f, 1f),
                    0.04f, top - 0.12f, 0.21f, top, () => OnServeClicked(index), 22);
                _recipeButtons.Add(button);
            }

            // 中间：棋盘容器 + 4x4 格子。
            RectTransform boardRoot = UiBuilder.NewRect("BoardRoot", CachedTransform);
            UiBuilder.Anchor(boardRoot, 0.34f, 0.30f, 0.66f, 0.82f);
            BuildBoardCells(boardRoot);

            // 棋盘下方：吃!
            _eatButton = UiBuilder.AddButton(CachedTransform, "EatButton", "吃!", Accent,
                0.43f, 0.18f, 0.57f, 0.28f, OnEatClicked, 34);

            // 右侧：被动道具栏（汇总文本展示）。
            UiBuilder.AddImage(CachedTransform, "ItemsPanel", Panel, 0.88f, 0.34f, 0.98f, 0.95f, 4f);
            UiBuilder.AddText(CachedTransform, "ItemsTitle", "被动道具", 20, Color.white, 0.88f, 0.90f, 0.98f, 0.95f);
            _itemsText = UiBuilder.AddText(CachedTransform, "ItemsText", "", 16, new Color(1f, 1f, 1f, 0.85f), 0.88f, 0.34f, 0.98f, 0.90f, TextAnchor.UpperCenter);

            // 右下：主动道具按钮区。
            UiBuilder.AddText(CachedTransform, "ActiveTitle", "主动道具", 20, Color.white, 0.78f, 0.30f, 0.88f, 0.34f);
            _activeItemsRoot = UiBuilder.NewRect("ActiveItems", CachedTransform);
            UiBuilder.Anchor(_activeItemsRoot, 0.78f, 0.04f, 0.88f, 0.30f);

            // 底部消息条。
            _messageText = UiBuilder.AddText(CachedTransform, "MessageText", "", 22, new Color(1f, 1f, 1f, 0.9f), 0.2f, 0.04f, 0.8f, 0.12f);

            BuildResultPanel();
        }

        private void BuildBoardCells(RectTransform boardRoot)
        {
            UiBuilder.AddImage(boardRoot, "BoardBg", new Color(1f, 1f, 1f, 0.06f), 0f, 0f, 1f, 1f);

            int w = GameRun.BoardWidth;
            int h = GameRun.BoardHeight;
            _cells = new Image[w, h];
            _cellLabels = new Text[w, h];

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float minX = (float)x / w;
                    float maxX = (float)(x + 1) / w;
                    // 行 y=0 在顶部：归一化 Y 需翻转。
                    float minY = 1f - (float)(y + 1) / h;
                    float maxY = 1f - (float)y / h;

                    Image cell = UiBuilder.AddImage(boardRoot, $"Cell_{x}_{y}", CellEmpty, minX, minY, maxX, maxY, 4f);
                    _cells[x, y] = cell;
                    _cellLabels[x, y] = UiBuilder.AddText(cell.transform, "Label", "", 20, Color.white, 0f, 0f, 1f, 1f);

                    int cx = x;
                    int cy = y;
                    var cellButton = cell.gameObject.AddComponent<Button>();
                    cellButton.targetGraphic = cell;
                    cellButton.transition = Selectable.Transition.None;
                    cellButton.onClick.AddListener(() => OnCellClicked(cx, cy));
                }
            }
        }

        private void BuildResultPanel()
        {
            RectTransform panel = UiBuilder.NewRect("ResultPanel", CachedTransform);
            UiBuilder.Anchor(panel, 0f, 0f, 1f, 1f);
            _resultPanel = panel;

            UiBuilder.AddImage(panel, "Dim", new Color(0f, 0f, 0f, 0.75f), 0f, 0f, 1f, 1f);
            UiBuilder.AddImage(panel, "Box", new Color(0.15f, 0.15f, 0.2f, 1f), 0.3f, 0.35f, 0.7f, 0.65f, 4f);
            _resultText = UiBuilder.AddText(panel, "ResultText", "", 34, Color.white, 0.3f, 0.48f, 0.7f, 0.64f);
            _resultButton = UiBuilder.AddButton(panel, "ResultButton", "确定", Accent, 0.42f, 0.37f, 0.58f, 0.46f, OnResultConfirm, 26);

            panel.gameObject.SetActive(false);
        }

        private Image AddImage(string name, Color color, float minX, float minY, float maxX, float maxY)
            => UiBuilder.AddImage(CachedTransform, name, color, minX, minY, maxX, maxY);

        // —— 交互 ——

        private void OnServeClicked(int slotIndex)
        {
            if (_session == null || _session.IsSettled)
            {
                return;
            }

            ServeResult result = _session.Serve(slotIndex);
            switch (result.Outcome)
            {
                case ServeOutcome.Placed:
                    SetMessage($"上菜：{result.Dish.Def.Name}");
                    break;
                case ServeOutcome.SlotEmpty:
                    SetMessage($"菜谱{slotIndex + 1} 已空。");
                    break;
                case ServeOutcome.NoFittingDish:
                    SetMessage("棋盘放不下这本菜谱里的菜了，试试「吃!」结算。");
                    break;
                case ServeOutcome.LimitReached:
                    SetMessage($"限量供应：本局最多上 {_session.MaxServes} 道菜，点「吃!」结算吧。");
                    break;
            }

            RefreshAll();
        }

        private void OnEatClicked()
        {
            if (_session == null || _session.IsSettled)
            {
                return;
            }

            if (_session.Board.DishCount == 0)
            {
                SetMessage("棋盘还是空的，先上几道菜吧。");
                return;
            }

            ScoreResult result = _session.Settle();
            RefreshAll();

            if (_session.IsWin)
            {
                // 过关：领奖弹窗负责发奖、推进周与存档。
                GameApp.UI.OpenUIForm(UIForms.Reward, UIForms.GroupDialog);
            }
            else
            {
                ShowResult(false, result.Total);
            }
        }

        private void OnResultConfirm()
        {
            // 仅失败时走到这里：结束本局运行，清除存档并返回菜单。
            RunPersistence.Delete();
            GameplayFlowSignal.RequestReturnToMenu();
        }

        private void OnOverviewClicked()
        {
            GameplayFlowSignal.RequestReturnToMenu();
        }

        private void OnCellClicked(int x, int y)
        {
            if (_session == null)
            {
                return;
            }

            DishInstance inst = _session.Board.DishAt(new GridPos(x, y));
            if (inst == null)
            {
                return;
            }

            var data = new DishDetailData(inst.Def, _run.Database, inst.TagIds);
            GameApp.UI.OpenUIForm(UIForms.DishDetail, UIForms.GroupDialog, data);
        }

        private void OnActiveItemClicked(string itemId)
        {
            if (_session == null || _session.IsSettled || _usedActiveItems.Contains(itemId))
            {
                return;
            }

            cfg.Item item = GameApp.Config.Tables.TbItem.GetOrDefault(itemId);
            if (item == null)
            {
                return;
            }

            switch (item.EffectType)
            {
                case "ClearBoard":
                    _session.ClearBoard();
                    SetMessage("重摆铃：已清空棋盘。");
                    break;
                case "ExtraServe":
                    if (TryExtraServe())
                    {
                        SetMessage("加菜券：额外上了一道菜。");
                    }
                    else
                    {
                        SetMessage("加菜券：没有能放下的菜了。");
                    }

                    break;
                default:
                    SetMessage($"使用了 {item.Name}。");
                    break;
            }

            _usedActiveItems.Add(itemId);
            RefreshAll();
        }

        private bool TryExtraServe()
        {
            for (int i = 0; i < _session.Slots.Count; i++)
            {
                if (_session.Serve(i).Success)
                {
                    return true;
                }
            }

            return false;
        }

        // —— 刷新 ——

        private void RefreshAll()
        {
            RefreshBoard();
            RefreshRecipes();
            RefreshScore();
            RefreshItems();
            RefreshActiveItems();
        }

        private void RebuildActiveItems()
        {
            foreach (Button button in _activeItemButtons)
            {
                if (button != null)
                {
                    Destroy(button.gameObject);
                }
            }

            _activeItemButtons.Clear();

            int idx = 0;
            foreach (string itemId in _run.ItemIds)
            {
                cfg.Item item = GameApp.Config.Tables.TbItem.GetOrDefault(itemId);
                if (item == null || item.Kind != cfg.ItemKind.Active)
                {
                    continue;
                }

                float top = 1f - idx * 0.34f;
                float bottom = top - 0.30f;
                if (bottom < 0f)
                {
                    break;
                }

                string captured = itemId;
                Button button = UiBuilder.AddButton(_activeItemsRoot, $"Active_{itemId}", item.Name,
                    new Color(0.4f, 0.55f, 0.75f, 1f), 0f, bottom, 1f, top, () => OnActiveItemClicked(captured), 18);
                _activeItemButtons.Add(button);
                idx++;
            }
        }

        private void RefreshActiveItems()
        {
            for (int i = 0; i < _activeItemButtons.Count; i++)
            {
                Button button = _activeItemButtons[i];
                if (button == null)
                {
                    continue;
                }

                string itemId = button.name.StartsWith("Active_") ? button.name.Substring("Active_".Length) : null;
                bool used = itemId != null && _usedActiveItems.Contains(itemId);
                button.interactable = _session != null && !_session.IsSettled && !used;
            }
        }

        private void RefreshBoard()
        {
            int boardW = _session?.Board.Width ?? GameRun.BoardWidth;
            int boardH = _session?.Board.Height ?? GameRun.BoardHeight;

            for (int y = 0; y < GameRun.BoardHeight; y++)
            {
                for (int x = 0; x < GameRun.BoardWidth; x++)
                {
                    // 超出本局棋盘范围（Boss「缩盘」）的格子标记为不可用。
                    if (x >= boardW || y >= boardH)
                    {
                        _cells[x, y].color = CellBlocked;
                        _cellLabels[x, y].text = string.Empty;
                        continue;
                    }

                    DishInstance inst = _session?.Board.DishAt(new GridPos(x, y));
                    if (inst == null)
                    {
                        _cells[x, y].color = CellEmpty;
                        _cellLabels[x, y].text = string.Empty;
                    }
                    else
                    {
                        _cells[x, y].color = UiBuilder.ColorFromString(inst.Def.Id);
                        _cellLabels[x, y].text = ShortName(inst.Def.Name);
                    }
                }
            }
        }

        private void RefreshRecipes()
        {
            for (int i = 0; i < _recipeButtons.Count; i++)
            {
                if (_session == null || i >= _session.Slots.Count)
                {
                    UiBuilder.SetButtonLabel(_recipeButtons[i], $"菜谱{i + 1}");
                    _recipeButtons[i].interactable = false;
                    continue;
                }

                RecipeSlot slot = _session.Slots[i];
                UiBuilder.SetButtonLabel(_recipeButtons[i], $"菜谱{i + 1}\n上菜 (剩{slot.Count})");
                _recipeButtons[i].interactable = !_session.IsSettled && !slot.IsEmpty;
            }
        }

        private void RefreshScore()
        {
            if (_session == null)
            {
                _scoreText.text = "0";
                _weekText.text = $"第 {_run.WeekIndex}/{_run.TotalWeeks} 周 · 目标 {_run.RequiredScore}";
                return;
            }

            int current = _session.IsSettled ? _session.LastResult.Total : _session.PreviewScore().Total;
            _scoreText.text = current.ToString();

            string weekLabel = _run.IsEndless ? $"无尽 第 {_run.WeekIndex - _run.TotalWeeks} 关" : $"第 {_run.WeekIndex}/{_run.TotalWeeks} 周";
            string suffix = string.Empty;
            if (_run.IsBossWeek)
            {
                suffix += " · BOSS";
            }

            if (_session.MaxServes >= 0)
            {
                suffix += $" · 上菜 {_session.ServesUsed}/{_session.MaxServes}";
            }

            _weekText.text = $"{weekLabel} · 目标 {_session.RequiredScore}{suffix}";
        }

        private void RefreshItems()
        {
            if (_run.ItemIds.Count == 0)
            {
                _itemsText.text = "（无）";
                return;
            }

            var sb = new System.Text.StringBuilder();
            foreach (string itemId in _run.ItemIds)
            {
                cfg.Item item = GameApp.Config.Tables.TbItem.GetOrDefault(itemId);
                if (item != null && item.Kind == cfg.ItemKind.Passive)
                {
                    sb.AppendLine($"· {item.Name}");
                }
            }

            _itemsText.text = sb.Length == 0 ? "（无）" : sb.ToString();
        }

        private void ShowResult(bool win, int total)
        {
            _resultPanel.gameObject.SetActive(true);
            _resultText.text = win
                ? $"过关！\n得分 {total} / 目标 {_session.RequiredScore}"
                : $"失败…\n得分 {total} / 目标 {_session.RequiredScore}";
            UiBuilder.SetButtonLabel(_resultButton, "返回菜单");
            RefreshRecipes();
            _eatButton.interactable = false;
        }

        private void HideResult()
        {
            if (_resultPanel != null)
            {
                _resultPanel.gameObject.SetActive(false);
            }

            if (_eatButton != null)
            {
                _eatButton.interactable = true;
            }
        }

        private void SetMessage(string message)
        {
            if (_messageText != null)
            {
                _messageText.text = message;
            }
        }

        private static string ShortName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return string.Empty;
            }

            return name.Length <= 2 ? name : name.Substring(0, 1);
        }
    }
}
