using GourmetProject.Game.Meta;
using UnityEngine;

namespace GourmetProject.Game.UI.Common
{
    internal interface IOpeningComicProgressStore
    {
        bool HasCompletedVersion { get; }
        int LoadCompletedVersion();
        void SaveCompletedVersion(int version);
    }

    /// <summary>
    /// 开场漫画属于应用级体验，不跟随单局或跨局进度存档删除。
    /// PlayerPrefs 的生命周期独立于 saves 目录，适合保存这一类一次性展示标记。
    /// </summary>
    internal sealed class PlayerPrefsOpeningComicProgressStore : IOpeningComicProgressStore
    {
        internal const string CompletedVersionKey = "GourmetProject.OpeningComic.CompletedVersion";

        internal static readonly PlayerPrefsOpeningComicProgressStore Instance = new();

        public bool HasCompletedVersion => PlayerPrefs.HasKey(CompletedVersionKey);

        public int LoadCompletedVersion()
        {
            return PlayerPrefs.GetInt(CompletedVersionKey, 0);
        }

        public void SaveCompletedVersion(int version)
        {
            PlayerPrefs.SetInt(CompletedVersionKey, version);
            PlayerPrefs.Save();
        }
    }

    /// <summary>开场漫画的一次性播放判定。版本号允许未来替换漫画后重新播放一次。</summary>
    internal static class OpeningComicProgress
    {
        internal const int CurrentVersion = 1;

        internal static bool ShouldPlay()
        {
            return ShouldPlay(PlayerPrefsOpeningComicProgressStore.Instance);
        }

        internal static bool ShouldPlay(IOpeningComicProgressStore store)
        {
            return store == null || store.LoadCompletedVersion() < CurrentVersion;
        }

        internal static void MarkCompleted()
        {
            MarkCompleted(PlayerPrefsOpeningComicProgressStore.Instance);
        }

        internal static void MarkCompleted(IOpeningComicProgressStore store)
        {
            if (store != null)
            {
                store.SaveCompletedVersion(System.Math.Max(CurrentVersion, store.LoadCompletedVersion()));
            }
        }

        /// <summary>
        /// 老版本把完成标记写在 meta_progress 中。仅当应用级标记还不存在时迁移，
        /// 保证升级后的玩家不会多看一次，同时不再反向写回游戏存档。
        /// </summary>
        internal static void MigrateLegacy(MetaProgressSaveData legacyProgress)
        {
            MigrateLegacy(PlayerPrefsOpeningComicProgressStore.Instance, legacyProgress);
        }

        internal static void MigrateLegacy(
            IOpeningComicProgressStore store,
            MetaProgressSaveData legacyProgress)
        {
            if (store == null
                || store.HasCompletedVersion
                || legacyProgress == null
                || legacyProgress.OpeningComicCompletedVersion <= 0)
            {
                return;
            }

            store.SaveCompletedVersion(legacyProgress.OpeningComicCompletedVersion);
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
