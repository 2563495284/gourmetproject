using System;
using System.Collections.Generic;
using GourmetProject.Core.Save;
using GourmetProject.Game.Meta;
using GourmetProject.Game.Run;
using GourmetProject.Runtime;

namespace GourmetProject.Game.Save
{
    /// <summary>
    /// 玩家总档。局外进度、引导进度和当前单局都落在同一个槽位；
    /// 一局结束时只清空 <see cref="Run"/>，不会删除整份总档。
    /// </summary>
    [Serializable]
    public sealed class GameSaveData
    {
        internal const string CurrentFormat = "gourmet_project_game_save";

        // 不提供字段默认值，以便把旧版直接存入 slot0 的 RunSaveData 与聚合总档区分开。
        public string Format;
        public int Version = 2;
        public MetaProgressSaveData MetaProgress = new();
        public GuideProgressSaveData GuideProgress = new();
        public RunSaveData Run;

        internal bool HasCurrentFormat => string.Equals(Format, CurrentFormat, StringComparison.Ordinal);

        public void Normalize()
        {
            Format = CurrentFormat;
            if (Version < 2)
            {
                Version = 2;
            }

            MetaProgress ??= new MetaProgressSaveData();
            MetaProgress.Normalize();
            GuideProgress ??= new GuideProgressSaveData();
            GuideProgress.Normalize();

            // 旧版本把开场完成版本放在 meta_progress 中，只向新的引导分区迁移。
            GuideProgress.OpeningComicCompletedVersion = Math.Max(
                GuideProgress.OpeningComicCompletedVersion,
                MetaProgress.OpeningComicCompletedVersion);
            MetaProgress.OpeningComicCompletedVersion = 0;
        }
    }

    /// <summary>总档中的引导信息；后续教程完成状态也统一扩展在这里。</summary>
    [Serializable]
    public sealed class GuideProgressSaveData
    {
        public int OpeningComicCompletedVersion;
        public List<string> CompletedTutorialIds = new();

        public void Normalize()
        {
            CompletedTutorialIds ??= new List<string>();
            var seen = new HashSet<string>();
            var normalized = new List<string>();
            foreach (string tutorialId in CompletedTutorialIds)
            {
                if (!string.IsNullOrEmpty(tutorialId) && seen.Add(tutorialId))
                {
                    normalized.Add(tutorialId);
                }
            }

            CompletedTutorialIds = normalized;
        }
    }

    /// <summary>玩家总档的唯一读写入口，并兼容旧版拆分的 run/meta_progress 槽位。</summary>
    public static class GameSavePersistence
    {
        public const string Slot = "slot0";
        private const string LegacyMetaProgressSlot = "meta_progress";

        public static GameSaveData Load()
        {
            return Load(GameApp.Save);
        }

        public static GameSaveData Load(ISaveService save)
        {
            if (save == null)
            {
                return CreateEmpty();
            }

            if (save.TryLoad(Slot, out GameSaveData root)
                && root != null
                && root.HasCurrentFormat)
            {
                root.Normalize();
                return root;
            }

            // 兼容旧结构：slot0 直接存 RunSaveData，meta_progress 单独占一个槽位。
            root = CreateEmpty();
            bool migrated = false;
            if (save.TryLoad(Slot, out RunSaveData legacyRun) && legacyRun != null)
            {
                root.Run = legacyRun;
                migrated = true;
            }

            if (save.TryLoad(LegacyMetaProgressSlot, out MetaProgressSaveData legacyMeta)
                && legacyMeta != null)
            {
                root.MetaProgress = legacyMeta;
                migrated = true;
            }

            root.Normalize();
            if (migrated)
            {
                Save(save, root);
            }

            return root;
        }

        public static void Save(GameSaveData data)
        {
            Save(GameApp.Save, data);
        }

        public static void Save(ISaveService save, GameSaveData data)
        {
            if (save == null)
            {
                return;
            }

            data ??= CreateEmpty();
            data.Normalize();
            save.Save(Slot, data);
        }

        public static void DeleteAll()
        {
            DeleteAll(GameApp.Save);
        }

        internal static void DeleteAll(ISaveService save)
        {
            if (save == null)
            {
                return;
            }

            save.Delete(Slot);
            // 防止清空总档后，旧版独立 meta 槽位在下次启动时再次迁移回来。
            save.Delete(LegacyMetaProgressSlot);
        }

        private static GameSaveData CreateEmpty()
        {
            var data = new GameSaveData();
            data.Normalize();
            return data;
        }
    }
}
