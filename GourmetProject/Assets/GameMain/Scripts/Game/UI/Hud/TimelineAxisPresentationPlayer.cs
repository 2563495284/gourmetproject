using System;

namespace GourmetProject.Game.UI.Hud
{
    /// <summary>
    /// 统一把不可变演出计划送入 View 的单 Cue 播放器，并保证每个计划只回调一次。
    /// View 只负责具体 Tween；最终状态、立即完成和取消语义集中在这里。
    /// </summary>
    public sealed class TimelineAxisPresentationPlayer
    {
        private readonly Action<TimelinePresentationCue, Action, float> _playCue;
        private readonly Action<TimelineAxisViewState> _renderFinal;
        private readonly Action _completeVisuals;
        private readonly Action _cancelVisuals;

        public TimelineAxisPresentationPlayer(
            Action<TimelinePresentationCue, Action, float> playCue,
            Action<TimelineAxisViewState> renderFinal,
            Action completeVisuals,
            Action cancelVisuals)
        {
            _playCue = playCue ?? throw new ArgumentNullException(nameof(playCue));
            _renderFinal = renderFinal ?? throw new ArgumentNullException(nameof(renderFinal));
            _completeVisuals = completeVisuals ?? throw new ArgumentNullException(nameof(completeVisuals));
            _cancelVisuals = cancelVisuals ?? throw new ArgumentNullException(nameof(cancelVisuals));
        }

        public void Play(
            TimelineAxisPresentationPlan plan,
            Action onComplete = null,
            float speed = 1f)
        {
            if (plan == null || plan.IsEmpty)
            {
                if (plan?.FinalState != null)
                {
                    _renderFinal(plan.FinalState);
                }

                onComplete?.Invoke();
                return;
            }

            bool invoked = false;
            void FinishOnce()
            {
                if (invoked)
                {
                    return;
                }

                invoked = true;
                _renderFinal(plan.FinalState);
                onComplete?.Invoke();
            }

            for (int i = 0; i < plan.Cues.Count; i++)
            {
                TimelinePresentationCue cue = plan.Cues[i];
                _playCue(cue, i == plan.Cues.Count - 1 ? FinishOnce : null, speed);
            }
        }

        public void Complete()
        {
            _completeVisuals();
        }

        public void Cancel()
        {
            _cancelVisuals();
        }
    }
}
