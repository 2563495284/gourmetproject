using GourmetProject.Game.Gameplay;
using GourmetProject.Game.Gameplay.Presentation;
using GourmetProject.Gameplay.Battle;
using GourmetProject.Gameplay.Board;
using GourmetProject.Gameplay.Scoring;
using GourmetProject.Runtime;
using GourmetProject.Runtime.UI;
using UnityEngine;
using UnityEngine.UI;
using Log = GourmetProject.Core.Diagnostics.Log;

namespace GourmetProject.Game.UI
{
    /// <summary>
    /// 局内战斗界面：后厨菜谱上菜、4x4 棋盘摆盘、「吃!」结算分数过关/失败。
    /// 棋盘/菜谱/操作按钮由 Battle.unity 场景内的 BattleWorldController 用 SpriteRenderer 构建，
    /// BattleForm 只保留过关/失败的结果弹窗（固定结构在 BattleForm.prefab）与周循环流程编排。
    /// </summary>
    public sealed class BattleForm : UGuiForm
    {
        private const string Tag = "Battle";

        /// <summary>当前打开的战斗界面，供事件/奖励弹窗回调推进周循环。</summary>
        public static BattleForm Active { get; private set; }

        [SerializeField] private GameObject _resultPanel;
        [SerializeField] private Text _resultText;
        [SerializeField] private Button _resultButton;

        private GameRun _run;
        private BattleSession _session;
        private BattleWorldController _world;

        public GameRun Run => _run;
        public BattleSession Session => _session;

        protected override void OnInit(object userData)
        {
            base.OnInit(userData);
            _resultButton.onClick.AddListener(OnResultConfirm);
            _resultPanel.SetActive(false);
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

            HideResult();
            RunPersistence.Save(_run);
            GameApp.UI.OpenUIForm(UIForms.WeekMap, UIForms.GroupDialog);
        }

        /// <summary>事件结算完成后构建对局并允许操作。</summary>
        public void BeginBattleAfterEvent(string eventFeedback)
        {
            _session = _run.BuildBattleSession();
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
        }

        // —— 交互 ——

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
            if (_session == null || _session.IsSettled)
            {
                return;
            }

            cfg.Item item = GameApp.Config.Tables.TbItem.GetOrDefault(itemId);
            if (item == null || item.Kind != cfg.ItemKind.Active)
            {
                return;
            }

            RunItemState state = _run.GetItemState(itemId);
            if (state == null || state.Count <= 0)
            {
                _world?.ShowMessage($"{item.Name}：没有可用数量。");
                RefreshAll();
                return;
            }

            if (item.TriggerTiming != cfg.ItemTriggerTiming.BeforeEat)
            {
                _world?.ShowMessage($"{item.Name}：现在不是使用时机。");
                RefreshAll();
                return;
            }

            ActiveItemUseResult result = ActiveItemEffectRegistry.TryUse(_session, item);
            _world?.ShowMessage(result.Message);
            if (!result.Success)
            {
                RefreshAll();
                return;
            }

            _run.UseActiveItem(itemId);
            if (result.BoardChanged)
            {
                _world?.SyncBoardFromSession();
            }

            RunPersistence.Save(_run);
            RefreshAll();
        }

        // —— 刷新与结果 ——

        private void RefreshAll()
        {
            // 棋盘、菜谱、分数、道具的实际刷新都在 BattleWorldController 内完成。
            _world?.RefreshAll();
        }

        private void ShowResult(bool win, int total)
        {
            _resultPanel.SetActive(true);
            _resultText.text = win
                ? $"过关！\n得分 {total} / 目标 {_session.RequiredScore}"
                : $"失败…\n得分 {total} / 目标 {_session.RequiredScore}";

            Text resultLabel = _resultButton.GetComponentInChildren<Text>();
            if (resultLabel != null)
            {
                resultLabel.text = "返回菜单";
            }
        }

        private void HideResult()
        {
            if (_resultPanel != null)
            {
                _resultPanel.SetActive(false);
            }
        }

        private void SetMessage(string message)
        {
            // 提示消息的实际展示由战斗场景（BattleWorldController）负责；此回调保留以满足其接口契约。
        }
    }
}
