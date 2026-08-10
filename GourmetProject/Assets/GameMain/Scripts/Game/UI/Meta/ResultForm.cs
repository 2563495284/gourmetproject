using GourmetProject.Game.Flow;
using GourmetProject.Game.Analytics;
using BreakInfinity;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;
using GourmetProject.Gameplay.Battle;
using GourmetProject.Runtime;
using GourmetProject.Runtime.UI;
using UnityEngine;
using UnityEngine.UI;
using GourmetProject.Game.UI;
using GourmetProject.Game.UI.Battle;
using TMPro;

namespace GourmetProject.Game.UI.Meta
{
    /// <summary>
    /// 大局结算界面（胜利/失败）：展示结算摘要并按结果保留或删除存档，确认后返回菜单。
    /// 原先内嵌在 BattleForm 里的结果壳拆出为独立弹层，走 GroupDialog，和 RewardForm 一致。
    /// 结构固定落在 ResultForm.prefab，脚本只按 userData（胜负 + 得分）填文本并处理确认。
    /// </summary>
    public sealed class ResultForm : UGuiForm
    {
        [SerializeField] private TMP_Text _resultText;
        [SerializeField] private Button _resultButton;

        private GameRun _run;
        private bool _victory;
        private MetaProgressUpdate _pendingProgressUpdate;

        protected override void OnInit(object userData)
        {
            base.OnInit(userData);
            _resultButton.onClick.AddListener(OnConfirm);
        }

        protected override void OnOpen(object userData)
        {
            base.OnOpen(userData);

            _run = GameRunContext.Current;
            if (_run == null || userData is not ResultFormData data)
            {
                Close();
                return;
            }

            _victory = data.Win;
            _pendingProgressUpdate = null;

            BattleSession session = BattleForm.Active?.Session;
            int target = session?.RequiredScore ?? _run.RequiredScore;
            MetaProgressSaveData progress = MetaProgressPersistence.Load();
            _pendingProgressUpdate = MetaProgressService.EvaluateRunEnd(_run, data.Win, data.Total, target, progress);
            if (data.Win)
            {
                GameAnalyticsService.TrackRunMilestone(_run, data.Total, target);
            }
            SettlementSummary summary = SettlementService.Build(_run, data.Win, data.Total, target, _pendingProgressUpdate);
            _resultText.text = $"{summary.Title}\n\n{summary.Body}";

            TMP_Text label = _resultButton.GetComponentInChildren<TMP_Text>();
            if (label != null)
            {
                label.text = summary.ButtonLabel;
            }
        }

        private void OnConfirm()
        {
            if (_pendingProgressUpdate?.Progress != null)
            {
                MetaProgressPersistence.Save(_pendingProgressUpdate.Progress);
                _pendingProgressUpdate = null;
            }

            if (_victory)
            {
                // 通关：保留存档（可继续无尽），返回菜单。
                RunPersistence.Save(_run);
            }
            else
            {
                RunPersistence.Delete();
            }

            Close();
            GameplayFlowSignal.RequestReturnToMenu();
        }

        private void Close()
        {
            GameApp.UI.CloseUIForm(UIForm);
        }
    }

    /// <summary>打开 ResultForm 时携带的数据：本局胜负与最终得分。</summary>
    public sealed class ResultFormData
    {
        public ResultFormData(bool win, BigDouble total)
        {
            Win = win;
            Total = total;
        }

        public bool Win { get; }

        public BigDouble Total { get; }
    }
}
