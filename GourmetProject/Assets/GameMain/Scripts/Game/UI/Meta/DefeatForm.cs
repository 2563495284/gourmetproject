using System;
using DG.Tweening;
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
        [SerializeField] private CanvasGroup _transitionGroup;
        [SerializeField] private RectTransform _transitionPanel;

        private GameRun _run;
        private MetaProgressUpdate _pendingProgressUpdate;
        private Sequence _transitionSequence;
        private bool _closing;

        protected override void OnInit(object userData)
        {
            base.OnInit(userData);
            _resultButton.onClick.AddListener(OnConfirm);
        }

        protected override void OnOpen(object userData)
        {
            base.OnOpen(userData);
            PrepareOpenTransition();

            _run = GameRunContext.Current;
            if (_run == null)
            {
                CloseImmediate();
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

            PlayOpenTransition();
        }

        protected override void OnClose(bool isShutdown, object userData)
        {
            _transitionSequence?.Kill();
            _transitionSequence = null;
            _closing = false;
            base.OnClose(isShutdown, userData);
        }

        private void OnConfirm()
        {
            if (_pendingProgressUpdate?.Progress != null)
            {
                MetaProgressPersistence.Save(_pendingProgressUpdate.Progress);
                _pendingProgressUpdate = null;
            }

            RunPersistence.Delete();
            CloseAnimated(GameplayFlowSignal.RequestReturnToMenu);
        }

        private void CloseImmediate()
        {
            GameApp.UI.CloseUIForm(UIForm);
        }

        private void PrepareOpenTransition()
        {
            _transitionSequence?.Kill();
            _closing = false;
            _resultButton.interactable = true;
            if (_transitionGroup != null)
            {
                _transitionGroup.alpha = 0f;
                _transitionGroup.blocksRaycasts = false;
            }

            if (_transitionPanel != null)
            {
                _transitionPanel.localScale = Vector3.one * 0.94f;
            }
        }

        private void PlayOpenTransition()
        {
            _transitionSequence = DOTween.Sequence().SetUpdate(true).SetTarget(this);
            if (_transitionGroup != null)
            {
                _transitionSequence.Append(DOTween.To(
                    () => _transitionGroup.alpha,
                    value => _transitionGroup.alpha = value,
                    1f,
                    0.2f).SetEase(Ease.OutQuad));
            }

            if (_transitionPanel != null)
            {
                _transitionSequence.Join(_transitionPanel.DOScale(1f, 0.24f).SetEase(Ease.OutCubic));
            }

            _transitionSequence.OnComplete(() =>
            {
                if (_transitionGroup != null)
                {
                    _transitionGroup.blocksRaycasts = true;
                }
            });
        }

        private void CloseAnimated(Action onClosed)
        {
            if (_closing)
            {
                return;
            }

            _closing = true;
            _resultButton.interactable = false;
            if (_transitionGroup != null)
            {
                _transitionGroup.blocksRaycasts = false;
            }

            _transitionSequence?.Kill();
            _transitionSequence = DOTween.Sequence().SetUpdate(true).SetTarget(this);
            if (_transitionGroup != null)
            {
                _transitionSequence.Append(DOTween.To(
                    () => _transitionGroup.alpha,
                    value => _transitionGroup.alpha = value,
                    0f,
                    0.16f).SetEase(Ease.InQuad));
            }

            if (_transitionPanel != null)
            {
                _transitionSequence.Join(_transitionPanel.DOScale(0.96f, 0.16f).SetEase(Ease.InQuad));
            }

            _transitionSequence.OnComplete(() =>
            {
                CloseImmediate();
                onClosed?.Invoke();
            });
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
