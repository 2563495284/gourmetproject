using GourmetProject.Game.Meta;

namespace GourmetProject.Game.UI.Common
{
    /// <summary>开场漫画的一次性播放判定。版本号允许未来替换漫画后重新播放一次。</summary>
    internal static class OpeningComicProgress
    {
        internal const int CurrentVersion = 1;

        internal static bool ShouldPlay(MetaProgressSaveData progress)
        {
            return progress == null || progress.OpeningComicCompletedVersion < CurrentVersion;
        }

        internal static void MarkCompleted(MetaProgressSaveData progress)
        {
            if (progress != null)
            {
                progress.OpeningComicCompletedVersion = CurrentVersion;
            }
        }
    }

    /// <summary>与动画表现解耦的分镜推进状态，便于验证连点锁定和最终格行为。</summary>
    internal sealed class OpeningComicPlaybackState
    {
        internal OpeningComicPlaybackState(int panelCount)
        {
            PanelCount = panelCount > 0 ? panelCount : 1;
        }

        internal int PanelCount { get; }
        internal int CurrentIndex { get; private set; }
        internal bool IsBusy { get; private set; }
        internal bool IsFinalPanel => CurrentIndex >= PanelCount - 1;

        internal bool TryBeginAdvance()
        {
            if (IsBusy || IsFinalPanel)
            {
                return false;
            }

            IsBusy = true;
            return true;
        }

        internal void CompleteAdvance()
        {
            if (!IsBusy)
            {
                return;
            }

            CurrentIndex = System.Math.Min(CurrentIndex + 1, PanelCount - 1);
            IsBusy = false;
        }

        internal void CancelAdvance()
        {
            IsBusy = false;
        }
    }
}
