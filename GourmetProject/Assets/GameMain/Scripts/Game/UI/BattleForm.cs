using System.Collections.Generic;
using GourmetProject.Game.Gameplay;
using GourmetProject.Game.Gameplay.Presentation;
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
        private static readonly Color CellVoid = new(0f, 0f, 0f, 0.55f);
        private static readonly Color CellTagTint = new(1f, 0.85f, 0.35f, 0.22f);
        private static readonly Color Accent = new(1f, 0.6f, 0.16f, 1f);

        /// <summary>当前打开的战斗界面，供事件/奖励弹窗回调推进周循环。</summary>
        public static BattleForm Active { get; private set; }

        private GameRun _run;
        private BattleSession _session;
        private BattleWorldController _world;

        public GameRun Run => _run;
        public BattleSession Session => _session;

        private Text _scoreText;
        private Text _weekText;
        private Text _messageText;
        private Text _itemsText;
        private readonly List<Button> _recipeButtons = new();
        private RectTransform _boardRoot;
        private Image[,] _cells;
        private Text[,] _cellLabels;
        private Text[,] _cellBadges;
        private int _boardW;
        private int _boardH;
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

            if (_world != null)
            {
                _world.HideWorld();
            }

            base.OnClose(isShutdown, userData);
        }

        /// <summary>进入新一周：先弹三选一事件，事件结算后才构建本周对局。</summary>
        public void BeginWeek()
        {
            _run.RequiredScoreOverride = -1;
            _session = null;
            if (_world != null)
            {
                _world.HideWorld();
            }

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
            _world = BattleWorldController.GetOrCreate();
            _world.Initialize(
                _run,
                _session,
                SetMessage,
                RefreshAll,
                OnEatClicked,
                OnOverviewClicked,
                OnActiveItemClicked,
                OnDishClicked);
            RefreshAll();
            string head = string.IsNullOrEmpty(eventFeedback) ? string.Empty : eventFeedback + " ";
            SetMessage($"{head}点击菜谱上菜，系统会从所有可放入方式中随机摆上棋盘。");
        }

        // —— 布局构建 ——

        private void BuildLayout()
        {
            // 战斗棋盘与操作按钮改由场景内 SpriteRenderer 构建；BattleForm 仅保留结果弹窗等流程 UI。
            BuildResultPanel();
        }

        /// <summary>按本局胃的包围盒尺寸（重）构建棋盘格子。不同角色 max 尺寸不同，需销毁旧格重建。</summary>
        private void RebuildBoardCells(int w, int h)
        {
            if (_boardRoot == null || w <= 0 || h <= 0)
            {
                return;
            }

            for (int i = _boardRoot.childCount - 1; i >= 0; i--)
            {
                Destroy(_boardRoot.GetChild(i).gameObject);
            }

            _boardW = w;
            _boardH = h;
            _cells = new Image[w, h];
            _cellLabels = new Text[w, h];
            _cellBadges = new Text[w, h];

            UiBuilder.AddImage(_boardRoot, "BoardBg", new Color(1f, 1f, 1f, 0.06f), 0f, 0f, 1f, 1f);

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float minX = (float)x / w;
                    float maxX = (float)(x + 1) / w;
                    // 行 y=0 在顶部：归一化 Y 需翻转。
                    float minY = 1f - (float)(y + 1) / h;
                    float maxY = 1f - (float)y / h;

                    Image cell = UiBuilder.AddImage(_boardRoot, $"Cell_{x}_{y}", CellEmpty, minX, minY, maxX, maxY, 4f);
                    _cells[x, y] = cell;
                    _cellLabels[x, y] = UiBuilder.AddText(cell.transform, "Label", "", 20, Color.white, 0f, 0f, 1f, 1f);
                    // 右上角的格子强化标签角标。
                    _cellBadges[x, y] = UiBuilder.AddText(cell.transform, "Badge", "", 14, new Color(1f, 0.92f, 0.5f, 1f), 0.5f, 0.62f, 1f, 1f);

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

            _world?.TryServeDish(slotIndex);
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

            // 结算改为背包乱斗式逐菜演出，演出完成后再决定过关/失败 UI。
            if (_world != null)
            {
                _world.PlaySettlement(result, () => OnSettlementComplete(result));
            }
            else
            {
                OnSettlementComplete(result);
            }
        }

        private void OnSettlementComplete(ScoreResult result)
        {
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

            var pos = new GridPos(x, y);
            DishInstance inst = _session.Board.DishAt(pos);
            if (inst == null)
            {
                // 空的强化格：提示该格携带的标签。
                IReadOnlyList<string> cellTags = _session.Board.TagsAt(pos);
                if (cellTags.Count > 0)
                {
                    var sb = new System.Text.StringBuilder("强化格：");
                    for (int i = 0; i < cellTags.Count; i++)
                    {
                        TagDef tag = _run.Database.GetTag(cellTags[i]);
                        sb.Append(tag != null ? tag.Name : cellTags[i]);
                        if (i < cellTags.Count - 1)
                        {
                            sb.Append('、');
                        }
                    }

                    SetMessage(sb.ToString());
                }

                return;
            }

            var data = new DishDetailData(inst.Def, _run.Database, inst.TagIds);
            GameApp.UI.OpenUIForm(UIForms.DishDetail, UIForms.GroupDialog, data);
        }

        private void OnDishClicked(DishInstance inst)
        {
            if (inst == null || _run == null)
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
            _world?.SyncBoardFromSession();
            RefreshAll();
        }

        private bool TryExtraServe()
        {
            for (int i = 0; i < _session.Slots.Count; i++)
            {
                if (_session.Serve(i).Success)
                {
                    _world?.SyncBoardFromSession();
                    return true;
                }
            }

            return false;
        }

        // —— 刷新 ——

        private void RefreshAll()
        {
            _world?.RefreshAll();
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
            if (_session == null || _cells == null)
            {
                return;
            }

            GpBoard board = _session.Board;
            for (int y = 0; y < _boardH; y++)
            {
                for (int x = 0; x < _boardW; x++)
                {
                    var pos = new GridPos(x, y);

                    // 胃外「虚格」：不存在的格子，不可放、不可点。
                    if (!board.Exists(pos))
                    {
                        _cells[x, y].color = CellVoid;
                        _cellLabels[x, y].text = string.Empty;
                        _cellBadges[x, y].text = string.Empty;
                        continue;
                    }

                    DishInstance inst = board.DishAt(pos);
                    bool hasTag = board.TagsAt(pos).Count > 0;
                    if (inst == null)
                    {
                        _cells[x, y].color = hasTag ? CellTagTint : CellEmpty;
                        _cellLabels[x, y].text = string.Empty;
                    }
                    else
                    {
                        _cells[x, y].color = UiBuilder.ColorFromString(inst.Def.Id);
                        _cellLabels[x, y].text = ShortName(inst.Def.Name);
                    }

                    _cellBadges[x, y].text = hasTag ? CellTagBadge(board.TagsAt(pos)) : string.Empty;
                }
            }
        }

        /// <summary>格子强化标签角标：取首个标签短名，多个时加「+」。</summary>
        private string CellTagBadge(IReadOnlyList<string> tagIds)
        {
            if (tagIds.Count == 0)
            {
                return string.Empty;
            }

            TagDef tag = _run.Database.GetTag(tagIds[0]);
            string label = ShortName(tag != null ? tag.Name : tagIds[0]);
            return tagIds.Count > 1 ? label + "+" : label;
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
            if (_scoreText == null || _weekText == null)
            {
                return;
            }

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
            if (_itemsText == null || _run == null)
            {
                return;
            }

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
            if (_eatButton != null)
            {
                _eatButton.interactable = false;
            }
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
