using GourmetProject.Core.Save;
using GourmetProject.Game.Save;
using GourmetProject.Runtime;

namespace GourmetProject.Game.Meta
{
    /// <summary>玩家总档中的跨局进度分区读写。</summary>
    public static class MetaProgressPersistence
    {
        public static MetaProgressSaveData Load()
        {
            return Load(GameApp.Save);
        }

        public static MetaProgressSaveData Load(ISaveService save)
        {
            return GameSavePersistence.Load(save).MetaProgress;
        }

        public static void Save(MetaProgressSaveData data)
        {
            Save(GameApp.Save, data);
        }

        public static void Save(ISaveService save, MetaProgressSaveData data)
        {
            if (save == null)
            {
                return;
            }

            GameSaveData root = GameSavePersistence.Load(save);
            root.MetaProgress = data ?? new MetaProgressSaveData();
            GameSavePersistence.Save(save, root);
        }

        public static void Delete()
        {
            if (GameApp.Save == null)
            {
                return;
            }

            GameSaveData root = GameSavePersistence.Load();
            root.MetaProgress = new MetaProgressSaveData();
            GameSavePersistence.Save(root);
        }
    }
}
