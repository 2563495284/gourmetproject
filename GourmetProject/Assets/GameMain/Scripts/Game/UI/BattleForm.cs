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
            _world = BattleWorldController.Instance;
            if (_world == null)
            {
                Log.Error("BattleForm: battle scene controller not found (scene not loaded?).", Tag);
                return;
            }

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
            // 战斗棋盘与操作按钮改由 Battle.unity 场景内 SpriteRenderer 构建；BattleForm 仅保留结果弹窗等流程 UI。
            BuildResultPanel();
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
    }
}
