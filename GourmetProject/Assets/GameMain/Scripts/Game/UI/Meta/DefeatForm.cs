using GourmetProject.Game.Flow;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;
using GourmetProject.Gameplay.Battle;
using GourmetProject.Runtime;
using GourmetProject.Runtime.UI;
using UnityEngine;
using UnityEngine.UI;
using GourmetProject.Game.UI;
using GourmetProject.Game.UI.Battle;

namespace GourmetProject.Game.UI.Meta
{
    /// <summary>
    /// 大局失败结算页：记录失败进度、删除运行存档，然后返回主菜单。
    /// </summary>
    public sealed class DefeatForm : UGuiForm
    {
        [SerializeField] private Text _resultText;
        [SerializeField] private Button _resultButton;

        private GameRun _run;
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
            if (_run == null)
            {
                Close();
                return;
            }

            int total = userData is DefeatFormData data ? data.Total : 0;
            BattleSession session = BattleForm.Active?.Session;
            int target = session?.RequiredScore ?? _run.RequiredScore;
            MetaProgressSaveData progress = MetaProgressPersistence.Load();
            _pendingProgressUpdate = MetaProgressService.EvaluateRunEnd(_run, false, total, target, progress);
            SettlementSummary summary = SettlementService.Build(_run, false, total, target, _pendingProgressUpdate);
            _resultText.text = $"{summary.Title}\n\n{summary.Body}";

            Text label = _resultButton.GetComponentInChildren<Text>();
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

            RunPersistence.Delete();
            Close();
            GameplayFlowSignal.RequestReturnToMenu();
        }

        private void Close()
        {
            GameApp.UI.CloseUIForm(UIForm);
        }
    }

    public sealed class DefeatFormData
    {
        public DefeatFormData(int total)
        {
            Total = total;
        }

        public int Total { get; }
    }
}
